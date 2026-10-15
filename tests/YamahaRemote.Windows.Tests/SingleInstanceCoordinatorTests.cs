using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    private const int GreetingMarker = 0x31535259;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void DefaultNameDependsOnlyOnCurrentWindowsUserAndSession()
    {
        using WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
        using Process currentProcess = Process.GetCurrentProcess();
        string identity = $"{currentUser.User!.Value}:{currentProcess.SessionId}";
        string expected = "YamahaRemote.SingleInstance."
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        Assert.Equal(expected, SingleInstanceCoordinator.GetPipeName());
        Assert.Equal(expected, SingleInstanceCoordinator.GetPipeName());
        Assert.NotEqual(expected, SingleInstanceCoordinator.GetPipeName(NewIdentity()));
    }

    [Fact]
    public void SecondaryHandlesDoNotKeepThePrimaryReservationAlive()
    {
        string identity = NewIdentity();
        using SingleInstanceCoordinator primary = CreatePrimary(identity);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using SingleInstanceCoordinator? secondary = SingleInstanceCoordinator.TryCreate(() => { }, identity);
            Assert.Null(secondary);
        }

        primary.Dispose();

        using SingleInstanceCoordinator replacement = CreatePrimary(identity);
    }

    [Fact]
    public async Task ConcurrentElectionCreatesExactlyOnePrimary()
    {
        string identity = NewIdentity();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SingleInstanceCoordinator?>[] contenders = Enumerable.Range(0, 8).Select(attempt => Task.Run(async () =>
        {
            await start.Task;
            return SingleInstanceCoordinator.TryCreate(() => { }, identity);
        })).ToArray();
        start.SetResult();
        SingleInstanceCoordinator?[] coordinators = await Task.WhenAll(contenders).WaitAsync(TestTimeout);
        try
        {
            Assert.Single(coordinators, coordinator => coordinator is not null);
        }
        finally
        {
            foreach (SingleInstanceCoordinator? coordinator in coordinators)
                coordinator?.Dispose();
        }

        using SingleInstanceCoordinator replacement = CreatePrimary(identity);
    }

    [Fact]
    public async Task DifferentIdentitiesHaveIndependentReservationsAndCallbacks()
    {
        string firstIdentity = NewIdentity();
        string secondIdentity = NewIdentity();
        int firstActivations = 0;
        int secondActivations = 0;
        using SingleInstanceCoordinator first = CreatePrimary(firstIdentity, () => Interlocked.Increment(ref firstActivations));
        using SingleInstanceCoordinator second = CreatePrimary(secondIdentity, () => Interlocked.Increment(ref secondActivations));

        Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, firstIdentity));
        Assert.Equal(1, Volatile.Read(ref firstActivations));
        Assert.Equal(0, Volatile.Read(ref secondActivations));
        Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, secondIdentity));
        Assert.Equal(1, Volatile.Read(ref secondActivations));
    }

    [Fact]
    public async Task FactoryImmediatelyAcceptsACompleteHandshakeAndAcknowledgesTheCallback()
    {
        string identity = NewIdentity();
        int activations = 0;
        SynchronizationContext? callbackContext = null;
        using SingleInstanceCoordinator primary = CreatePrimary(identity, () =>
        {
            callbackContext = SynchronizationContext.Current;
            Interlocked.Increment(ref activations);
        });
        using CancellationTokenSource deadline = new(TestTimeout);
        using NamedPipeClientStream client = CreateClient(identity);

        await client.ConnectAsync(deadline.Token);
        Assert.Equal(Environment.ProcessId, await ReadGreetingAsync(client, deadline.Token));
        Assert.Equal(0, Volatile.Read(ref activations));
        await client.WriteAsync(new byte[] { 1 }, deadline.Token);
        byte[] acknowledgement = new byte[1];
        await client.ReadExactlyAsync(acknowledgement, deadline.Token);

        Assert.Equal(2, acknowledgement[0]);
        Assert.Equal(1, Volatile.Read(ref activations));
        Assert.Null(callbackContext);
        await client.WriteAsync(new byte[] { 3 }, deadline.Token);
    }

    [Fact]
    public async Task SequentialAndConcurrentRequestsAreAllAcknowledgedOnce()
    {
        string identity = NewIdentity();
        int activations = 0;
        using SingleInstanceCoordinator primary = CreatePrimary(identity, () => Interlocked.Increment(ref activations));

        for (int request = 0; request < 4; request++)
            Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
        Task<bool>[] requests = Enumerable.Range(0, 6)
            .Select(request => SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity)).ToArray();
        bool[] accepted = await Task.WhenAll(requests).WaitAsync(TestTimeout);

        Assert.All(accepted, result => Assert.True(result));
        Assert.Equal(10, Volatile.Read(ref activations));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledClientsExpireBeforeAndAfterAcknowledgement(bool sendRequest)
    {
        string identity = NewIdentity();
        int activations = 0;
        using SingleInstanceCoordinator primary = CreatePrimary(identity, () => Interlocked.Increment(ref activations));
        using CancellationTokenSource deadline = new(TestTimeout);
        using NamedPipeClientStream stalled = CreateClient(identity);
        await stalled.ConnectAsync(deadline.Token);
        await ReadGreetingAsync(stalled, deadline.Token);
        if (sendRequest)
        {
            await stalled.WriteAsync(new byte[] { 1 }, deadline.Token);
            byte[] acknowledgement = new byte[1];
            await stalled.ReadExactlyAsync(acknowledgement, deadline.Token);
            Assert.Equal(2, acknowledgement[0]);
        }

        Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
        Assert.Equal(sendRequest ? 2 : 1, Volatile.Read(ref activations));
    }

    [Fact]
    public async Task MalformedRequestAndDisconnectedClientDoNotStopTheListener()
    {
        string identity = NewIdentity();
        int activations = 0;
        using SingleInstanceCoordinator primary = CreatePrimary(identity, () => Interlocked.Increment(ref activations));
        using CancellationTokenSource deadline = new(TestTimeout);
        using (NamedPipeClientStream malformed = CreateClient(identity))
        {
            await malformed.ConnectAsync(deadline.Token);
            await ReadGreetingAsync(malformed, deadline.Token);
            await malformed.WriteAsync(new byte[] { 255 }, deadline.Token);
            await ObserveClosureAsync(malformed, deadline.Token);
        }
        using (NamedPipeClientStream disconnected = CreateClient(identity))
        {
            await disconnected.ConnectAsync(deadline.Token);
            await ReadGreetingAsync(disconnected, deadline.Token);
        }

        Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
        Assert.Equal(1, Volatile.Read(ref activations));
    }

    [Fact]
    public async Task ThrowingCallbackRefusesAcknowledgementAndTheNextRequestStillWorks()
    {
        string identity = NewIdentity();
        int attempts = 0;
        using SingleInstanceCoordinator primary = CreatePrimary(identity, () =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("Rejected activation.");
        });

        Assert.False(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
        Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("zero-pid")]
    [InlineData("wildcard-pid")]
    [InlineData("wrong-pid")]
    public async Task InvalidGreetingOrPidIsRejectedWithoutAnActivationCommand(string invalidPart)
    {
        string identity = NewIdentity();
        using CancellationTokenSource deadline = new(TestTimeout);
        using NamedPipeServerStream server = CreateServer(identity);
        Task conversation = RunServerAsync(server, async pipe =>
        {
            int marker = invalidPart == "marker" ? 0 : GreetingMarker;
            int processId = invalidPart switch
            {
                "zero-pid" => 0,
                "wildcard-pid" => -1,
                "wrong-pid" => Environment.ProcessId == 1 ? 2 : 1,
                _ => Environment.ProcessId
            };
            await pipe.WriteAsync(Greeting(marker, processId), deadline.Token);
            await ObserveClosureAsync(pipe, deadline.Token);
        }, deadline.Token);

        Assert.False(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
        await conversation.WaitAsync(TestTimeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrNegativeAcknowledgementReturnsFalse(bool sendNegativeAcknowledgement)
    {
        string identity = NewIdentity();
        using CancellationTokenSource deadline = new(TestTimeout);
        using NamedPipeServerStream server = CreateServer(identity);
        Task conversation = RunServerAsync(server, async pipe =>
        {
            await pipe.WriteAsync(Greeting(GreetingMarker, Environment.ProcessId), deadline.Token);
            byte[] request = new byte[1];
            await pipe.ReadExactlyAsync(request, deadline.Token);
            Assert.Equal(1, request[0]);
            if (sendNegativeAcknowledgement)
                await pipe.WriteAsync(new byte[] { 255 }, deadline.Token);
            await ObserveClosureAsync(pipe, deadline.Token);
        }, deadline.Token);

        Assert.False(await SingleInstanceCoordinator.RequestActivationAsync(TimeSpan.FromMilliseconds(500), identity));
        await conversation.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task CancellationInterruptsAConnectedServerThatNeverCompletesItsGreeting()
    {
        string identity = NewIdentity();
        using CancellationTokenSource deadline = new(TestTimeout);
        using CancellationTokenSource cancellation = new();
        using NamedPipeServerStream server = CreateServer(identity);
        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task conversation = RunServerAsync(server, async pipe =>
        {
            await pipe.WriteAsync(new byte[] { 89 }, deadline.Token);
            connected.SetResult();
            await ObserveClosureAsync(pipe, deadline.Token);
        }, deadline.Token);
        Task<bool> activation = SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity, cancellation.Token);
        await connected.Task.WaitAsync(TestTimeout);

        cancellation.Cancel();

        Assert.False(await activation.WaitAsync(TimeSpan.FromSeconds(1)));
        await conversation.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task MissingServerHasABoundedTimeoutAndSupportsCallerCancellation()
    {
        string identity = NewIdentity();
        Stopwatch elapsed = Stopwatch.StartNew();

        Assert.False(await SingleInstanceCoordinator.RequestActivationAsync(TimeSpan.FromMilliseconds(250), identity)
            .WaitAsync(TestTimeout));
        Assert.True(elapsed.Elapsed < TestTimeout);
        using CancellationTokenSource cancellation = new();
        Task<bool> request = SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity, cancellation.Token);
        cancellation.Cancel();
        Assert.False(await request.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCancelsPendingAcceptOrReadAndAllowsReacquisitionFromAnotherThread(bool connectClient)
    {
        string identity = NewIdentity();
        using SingleInstanceCoordinator primary = CreatePrimary(identity);
        using CancellationTokenSource deadline = new(TestTimeout);
        using NamedPipeClientStream client = CreateClient(identity);
        if (connectClient)
        {
            await client.ConnectAsync(deadline.Token);
            await ReadGreetingAsync(client, deadline.Token);
        }

        await Task.Run(primary.Dispose).WaitAsync(TimeSpan.FromSeconds(1));
        primary.Dispose();

        using SingleInstanceCoordinator replacement = CreatePrimary(identity);
        Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
    }

    [Fact]
    public async Task CallbackCanDisposeItsCoordinatorWithoutJoiningItself()
    {
        string identity = NewIdentity();
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SingleInstanceCoordinator? coordinator = null;
        coordinator = CreatePrimary(identity, () =>
        {
            coordinator!.Dispose();
            disposed.SetResult();
        });
        using (coordinator)
        {
            Task<bool> activation = SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity);
            await disposed.Task.WaitAsync(TestTimeout);
            Assert.False(await activation.WaitAsync(TestTimeout));

            using CancellationTokenSource deadline = new(TestTimeout);
            SingleInstanceCoordinator? replacement;
            while ((replacement = SingleInstanceCoordinator.TryCreate(() => { }, identity)) is null)
                await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
            using (replacement)
                Assert.True(await SingleInstanceCoordinator.RequestActivationAsync(TestTimeout, identity));
        }
    }

    [Fact]
    public async Task InvalidArgumentsDoNotReserveAnIdentity()
    {
        string identity = NewIdentity();

        Assert.Throws<ArgumentNullException>(() => SingleInstanceCoordinator.TryCreate(null!, identity));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SingleInstanceCoordinator.RequestActivationAsync(TimeSpan.Zero, identity));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SingleInstanceCoordinator.RequestActivationAsync(Timeout.InfiniteTimeSpan, identity));

        using SingleInstanceCoordinator primary = CreatePrimary(identity);
    }

    private static string NewIdentity() => $"test-{Guid.NewGuid():N}";

    private static SingleInstanceCoordinator CreatePrimary(string identity, Action? activationRequested = null) =>
        Assert.IsType<SingleInstanceCoordinator>(SingleInstanceCoordinator.TryCreate(activationRequested ?? (() => { }), identity));

    private static NamedPipeClientStream CreateClient(string identity) => new(".",
        SingleInstanceCoordinator.GetPipeName(identity), PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);

    private static NamedPipeServerStream CreateServer(string identity) => new(SingleInstanceCoordinator.GetPipeName(identity),
        PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static async Task<int> ReadGreetingAsync(Stream pipe, CancellationToken cancellationToken)
    {
        byte[] greeting = new byte[8];
        await pipe.ReadExactlyAsync(greeting, cancellationToken);
        Assert.Equal(GreetingMarker, BinaryPrimitives.ReadInt32LittleEndian(greeting));
        return BinaryPrimitives.ReadInt32LittleEndian(greeting.AsSpan(4));
    }

    private static byte[] Greeting(int marker, int processId)
    {
        byte[] greeting = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(greeting, marker);
        BinaryPrimitives.WriteInt32LittleEndian(greeting.AsSpan(4), processId);
        return greeting;
    }

    private static async Task RunServerAsync(NamedPipeServerStream server, Func<NamedPipeServerStream, Task> conversation,
        CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        await conversation(server);
    }

    private static async Task ObserveClosureAsync(Stream pipe, CancellationToken cancellationToken)
    {
        try
        {
            Assert.Equal(0, await pipe.ReadAsync(new byte[1], cancellationToken));
        }
        catch (IOException)
        {
        }
    }
}
