using System.Reflection;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class BluetoothLifecycleTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    public BluetoothLifecycleTests()
    {
        Localization.Initialize();
        Localization.SetLanguage("pl");
    }

    [Fact]
    public async Task DisconnectDuringConnectionInvalidatesTheLifetimeWithoutReportingAnEstablishedLoss()
    {
        using CancellationTokenSource lifetime = new();
        await using BluetoothSoundbarController controller = new();
        SetConnection(controller, lifetime, 41, false);
        int notifications = 0;
        controller.ConnectionLost += _ => notifications++;

        Invoke(controller, "LoseConnection", "disconnected", 41L);

        Assert.True(lifetime.IsCancellationRequested);
        Assert.Equal(42L, Field<long>(controller, "connectionEpoch"));
        Assert.False(controller.IsConnected);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task EstablishedConnectionLossIsReportedOnlyOnce()
    {
        using CancellationTokenSource lifetime = new();
        await using BluetoothSoundbarController controller = new();
        SetConnection(controller, lifetime, 41, true);
        List<string> messages = [];
        controller.ConnectionLost += messages.Add;

        Invoke(controller, "LoseConnection", "first loss", 41L);
        Invoke(controller, "LoseConnection", "stale loss", 41L);
        Invoke(controller, "LoseConnection", "already cancelled", 42L);

        Assert.True(lifetime.IsCancellationRequested);
        Assert.Equal(42L, Field<long>(controller, "connectionEpoch"));
        Assert.False(controller.IsConnected);
        Assert.Equal("first loss", Assert.Single(messages));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleLossCannotCancelTheNextConnection(bool connected)
    {
        using CancellationTokenSource lifetime = new();
        await using BluetoothSoundbarController controller = new();
        SetConnection(controller, lifetime, 12, connected);
        int notifications = 0;
        controller.ConnectionLost += _ => notifications++;

        Invoke(controller, "LoseConnection", "old connection", 11L);

        Assert.False(lifetime.IsCancellationRequested);
        Assert.Equal(12L, Field<long>(controller, "connectionEpoch"));
        Assert.Equal(connected, controller.IsConnected);
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData("OnValueChanged")]
    [InlineData("OnConnectionStatusChanged")]
    public async Task StaleCallbacksReturnBeforeAccessingTheirWinRtArguments(string method)
    {
        using CancellationTokenSource lifetime = new();
        await using BluetoothSoundbarController controller = new();
        SetConnection(controller, lifetime, 12, true);

        Invoke(controller, method, null, 11L);

        Assert.False(lifetime.IsCancellationRequested);
        Assert.Equal(12L, Field<long>(controller, "connectionEpoch"));
        Assert.True(controller.IsConnected);
    }

    [Fact]
    public async Task AQueuedConnectionCannotAcquireTheLifecycleAfterDisposalBegins()
    {
        await using BluetoothSoundbarController controller = new();
        SemaphoreSlim gate = Field<SemaphoreSlim>(controller, "lifecycleGate");
        await gate.WaitAsync();
        bool ownsInitialGate = true;
        Task? acquisition = null;
        Task? disposal = null;
        try
        {
            acquisition = InvokeTask(controller, "AcquireConnectionLifecycleAsync", CancellationToken.None);
            Assert.False(acquisition.IsCompleted);
            disposal = controller.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            gate.Release();
            ownsInitialGate = false;

            await Assert.ThrowsAsync<ObjectDisposedException>(() => acquisition).WaitAsync(TestTimeout);
            await disposal.WaitAsync(TestTimeout);

            Assert.Equal(1, gate.CurrentCount);
            Assert.False(controller.IsConnected);
        }
        finally
        {
            if (ownsInitialGate || acquisition?.IsCompletedSuccessfully == true)
                gate.Release();
            if (disposal is not null)
                await disposal.WaitAsync(TestTimeout);
        }
    }

    [Fact]
    public async Task DisposedControllerRejectsLifecycleAcquisitionWithoutTakingTheGate()
    {
        await using BluetoothSoundbarController controller = new();
        await controller.DisposeAsync();
        SemaphoreSlim gate = Field<SemaphoreSlim>(controller, "lifecycleGate");
        Task acquisition = InvokeTask(controller, "AcquireConnectionLifecycleAsync", CancellationToken.None);
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => acquisition).WaitAsync(TestTimeout);
            Assert.Equal(1, gate.CurrentCount);
        }
        finally
        {
            if (acquisition.IsCompletedSuccessfully)
                gate.Release();
        }
    }

    [Fact]
    public async Task CancelledLifecycleWaitDoesNotReleaseAnUnacquiredGate()
    {
        await using BluetoothSoundbarController controller = new();
        using CancellationTokenSource caller = new();
        SemaphoreSlim gate = Field<SemaphoreSlim>(controller, "lifecycleGate");
        await gate.WaitAsync();
        try
        {
            Task acquisition = InvokeTask(controller, "AcquireConnectionLifecycleAsync", caller.Token);
            Assert.False(acquisition.IsCompleted);
            caller.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition).WaitAsync(TestTimeout);

            Assert.Equal(0, gate.CurrentCount);
        }
        finally
        {
            gate.Release();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueTimeoutPreservesTheConnectionAndDoesNotReleaseAnUnacquiredGate(bool alreadyExpired)
    {
        using CancellationTokenSource lifetime = new();
        await using BluetoothSoundbarController controller = new();
        using CancellationTokenSource deadline = new();
        SetConnection(controller, lifetime, 12, true);
        int notifications = 0;
        controller.ConnectionLost += _ => notifications++;
        SemaphoreSlim gate = Field<SemaphoreSlim>(controller, "operationGate");
        await gate.WaitAsync();
        try
        {
            if (alreadyExpired)
                deadline.Cancel();
            Task waiting = InvokeTask(controller, "WaitForOperationGateAsync", deadline.Token, CancellationToken.None);
            if (!alreadyExpired)
            {
                Assert.False(waiting.IsCompleted);
                deadline.Cancel();
            }

            TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() => waiting).WaitAsync(TestTimeout);

            Assert.Equal(Localization.Text("Bluetooth.CommandTimeout"), error.Message);
            Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
            Assert.Equal(0, gate.CurrentCount);
            Assert.True(controller.IsConnected);
            Assert.False(lifetime.IsCancellationRequested);
            Assert.Equal(12L, Field<long>(controller, "connectionEpoch"));
            Assert.Equal(0, notifications);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task CallerCancellationInTheQueueIsNotConvertedToATimeout()
    {
        using CancellationTokenSource lifetime = new();
        await using BluetoothSoundbarController controller = new();
        using CancellationTokenSource caller = new();
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(caller.Token);
        SetConnection(controller, lifetime, 12, true);
        SemaphoreSlim gate = Field<SemaphoreSlim>(controller, "operationGate");
        await gate.WaitAsync();
        try
        {
            Task waiting = InvokeTask(controller, "WaitForOperationGateAsync", operation.Token, caller.Token);
            Assert.False(waiting.IsCompleted);
            caller.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting).WaitAsync(TestTimeout);

            Assert.Equal(0, gate.CurrentCount);
            Assert.True(controller.IsConnected);
            Assert.False(lifetime.IsCancellationRequested);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task OperationTokenCapturesItsEpochAndTracksItsConnectionLifetime()
    {
        using CancellationTokenSource lifetime = new();
        await using BluetoothSoundbarController controller = new();
        SetConnection(controller, lifetime, 12, true);
        object?[] arguments = [CancellationToken.None, 0L];

        using CancellationTokenSource operation = Assert.IsAssignableFrom<CancellationTokenSource>(
            Invoke(controller, "CreateOperationToken", arguments));

        Assert.Equal(12L, Assert.IsType<long>(arguments[1]));
        Assert.False(operation.IsCancellationRequested);
        lifetime.Cancel();
        Assert.True(operation.IsCancellationRequested);
    }

    private static void SetConnection(BluetoothSoundbarController controller, CancellationTokenSource lifetime, long epoch, bool connected)
    {
        SetField(controller, "connectionLifetime", lifetime);
        SetField(controller, "connectionEpoch", epoch);
        SetField(controller, "connected", connected ? 1 : 0);
    }

    private static T Field<T>(BluetoothSoundbarController controller, string name)
    {
        FieldInfo? field = typeof(BluetoothSoundbarController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field.GetValue(controller)!;
    }

    private static void SetField(BluetoothSoundbarController controller, string name, object value)
    {
        FieldInfo? field = typeof(BluetoothSoundbarController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(controller, value);
    }

    private static Task InvokeTask(BluetoothSoundbarController controller, string name, params object?[] arguments) =>
        Assert.IsAssignableFrom<Task>(Invoke(controller, name, arguments));

    private static object? Invoke(BluetoothSoundbarController controller, string name, params object?[] arguments)
    {
        MethodInfo? method = typeof(BluetoothSoundbarController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(controller, arguments);
    }
}
