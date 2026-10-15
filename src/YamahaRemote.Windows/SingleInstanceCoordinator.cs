using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace YamahaRemote.Windows;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const int GreetingMarker = 0x31535259;
    private const byte ActivationRequest = 1;
    private const byte ActivationAccepted = 2;
    private const byte AcknowledgementReceived = 3;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(2);
    private readonly Action _callback;
    private readonly Mutex _reservation;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _pipeLock = new();
    private readonly Task _listener;
    private NamedPipeServerStream? _activePipe;
    private int _callbackThreadId;
    private int _disposed;

    private SingleInstanceCoordinator(Action callback, Mutex reservation, string pipeName)
    {
        _callback = callback;
        _reservation = reservation;
        _pipeName = pipeName;
        _activePipe = CreateServer(pipeName);
        _listener = ListenAsync();
    }

    internal static SingleInstanceCoordinator? TryCreate(Action activationRequested, string? identity = null)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        string pipeName = GetPipeName(identity);
        Mutex reservation = new(false, @"Local\" + pipeName, out bool createdNew);
        if (!createdNew)
        {
            reservation.Dispose();
            return null;
        }

        try
        {
            return new SingleInstanceCoordinator(activationRequested, reservation, pipeName);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    internal static async Task<bool> RequestActivationAsync(TimeSpan timeout, string? identity = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            using NamedPipeClientStream pipe = new(".", GetPipeName(identity), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            byte[] greeting = new byte[8];
            await pipe.ReadExactlyAsync(greeting, deadline.Token).ConfigureAwait(false);
            int processId = BinaryPrimitives.ReadInt32LittleEndian(greeting.AsSpan(4));
            if (BinaryPrimitives.ReadInt32LittleEndian(greeting) != GreetingMarker || processId <= 0
                || !GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverProcessId)
                || serverProcessId != (uint)processId)
                return false;

            deadline.Token.ThrowIfCancellationRequested();
            _ = AllowSetForegroundWindow((uint)processId);
            await pipe.WriteAsync(new byte[] { ActivationRequest }, deadline.Token).ConfigureAwait(false);
            byte[] acknowledgement = new byte[1];
            await pipe.ReadExactlyAsync(acknowledgement, deadline.Token).ConfigureAwait(false);
            if (acknowledgement[0] != ActivationAccepted)
                return false;

            await pipe.WriteAsync(new byte[] { AcknowledgementReceived }, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    internal static string GetPipeName(string? identity = null)
    {
        using WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
        using Process currentProcess = Process.GetCurrentProcess();
        string userSid = currentUser.User?.Value
            ?? throw new InvalidOperationException("The current Windows user has no security identifier.");
        string scopedIdentity = $"{userSid}:{currentProcess.SessionId}";
        if (identity is not null)
            scopedIdentity += "\0" + identity;
        return "YamahaRemote.SingleInstance." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scopedIdentity)));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _shutdown.Cancel();
        lock (_pipeLock)
            _activePipe?.Dispose();

        if (Volatile.Read(ref _callbackThreadId) == Environment.CurrentManagedThreadId)
        {
            _ = _listener.ContinueWith(completed =>
            {
                _ = completed.Exception;
                ReleaseResources();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }

        try
        {
            _listener.GetAwaiter().GetResult();
        }
        finally
        {
            ReleaseResources();
        }
    }

    private static NamedPipeServerStream CreateServer(string pipeName) => new(pipeName, PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync()
    {
        CancellationToken shutdownToken = _shutdown.Token;
        while (!shutdownToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                lock (_pipeLock)
                {
                    if (shutdownToken.IsCancellationRequested)
                        return;
                    pipe = _activePipe ?? CreateServer(_pipeName);
                    _activePipe = pipe;
                }

                await pipe.WaitForConnectionAsync(shutdownToken).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                deadline.CancelAfter(ConnectionTimeout);
                await HandleConnectionAsync(pipe, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or OperationCanceledException or ObjectDisposedException)
            {
                if (pipe is null && !shutdownToken.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
            }
            finally
            {
                lock (_pipeLock)
                {
                    pipe?.Dispose();
                    _activePipe = null;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        byte[] greeting = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(greeting, GreetingMarker);
        BinaryPrimitives.WriteInt32LittleEndian(greeting.AsSpan(4), Environment.ProcessId);
        await pipe.WriteAsync(greeting, cancellationToken).ConfigureAwait(false);
        byte[] request = new byte[1];
        await pipe.ReadExactlyAsync(request, cancellationToken).ConfigureAwait(false);
        if (request[0] != ActivationRequest)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _callbackThreadId, Environment.CurrentManagedThreadId);
        try
        {
            _callback();
        }
        catch (Exception)
        {
            return;
        }
        finally
        {
            Volatile.Write(ref _callbackThreadId, 0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await pipe.WriteAsync(new byte[] { ActivationAccepted }, cancellationToken).ConfigureAwait(false);
        await pipe.ReadExactlyAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private void ReleaseResources()
    {
        _shutdown.Dispose();
        _reservation.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
