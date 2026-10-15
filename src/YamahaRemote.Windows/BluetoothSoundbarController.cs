using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Foundation;
using Windows.Storage.Streams;
using YamahaRemote.Core;

namespace YamahaRemote.Windows;

public sealed class BluetoothSoundbarController : ISoundbarController
{
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object receiveLock = new();
    private readonly PacketDecoder decoder = new();
    private BluetoothLEDevice? bluetoothDevice;
    private GattDeviceService? service;
    private GattSession? session;
    private GattCharacteristic? writeCharacteristic;
    private GattCharacteristic? notifyCharacteristic;
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? notificationHandler;
    private TypedEventHandler<BluetoothLEDevice, object>? connectionStatusHandler;
    private CancellationTokenSource? connectionLifetime;
    private Task? pollingTask;
    private TaskCompletionSource<SoundbarIdentity>? identityResponse;
    private TaskCompletionSource<SoundbarState>? stateResponse;
    private SoundbarState? state;
    private int connected;
    private long connectionEpoch;
    private bool disposed;

    public bool IsConnected => Volatile.Read(ref connected) == 1;
    public SoundbarState? State => Volatile.Read(ref state);
    public event Action<SoundbarState>? StateChanged;
    public event Action<string>? ConnectionLost;

    public async Task<IReadOnlyList<DiscoveredSoundbar>> DiscoverAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        await EnsureAdapterAsync(cancellationToken);
        ConcurrentDictionary<ulong, DiscoveredSoundbar> devices = new();
        TaskCompletionSource<BluetoothError> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BluetoothLEAdvertisementWatcher watcher = new() { ScanningMode = BluetoothLEScanningMode.Active };
        void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            string name = args.Advertisement.LocalName;
            bool matchingService = args.Advertisement.ServiceUuids.Contains(SoundbarProtocol.ServiceId);
            if (!matchingService && !name.Contains("SR-C30", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Yamaha", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.IsNullOrWhiteSpace(name))
                name = devices.TryGetValue(args.BluetoothAddress, out var previous) ? previous.Name : Localization.Text("Device.AdvertisementFallback");
            devices[args.BluetoothAddress] = new(name, args.BluetoothAddress, args.RawSignalStrengthInDBm);
        }
        void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
            => stopped.TrySetResult(args.Error);
        watcher.Received += OnReceived;
        watcher.Stopped += OnStopped;
        try
        {
            watcher.Start();
            Task delay = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            Task completed = await Task.WhenAny(delay, stopped.Task);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed == stopped.Task && await stopped.Task != BluetoothError.Success)
                throw new LocalizedOperationException("Bluetooth.SearchStopped");
            return devices.Values.OrderByDescending(device => device.SignalStrength).ToArray();
        }
        finally
        {
            watcher.Stop();
            watcher.Received -= OnReceived;
            watcher.Stopped -= OnStopped;
        }
    }

    public async Task<SoundbarIdentity> ConnectAsync(DiscoveredSoundbar device, CancellationToken cancellationToken)
    {
        await AcquireConnectionLifecycleAsync(cancellationToken);
        try
        {
            await DisconnectCoreAsync();
            await EnsureAdapterAsync(cancellationToken);
            long epoch;
            TaskCompletionSource<SoundbarIdentity> identityCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (receiveLock)
            {
                epoch = ++connectionEpoch;
                connectionLifetime = new();
                identityResponse = identityCompletion;
            }
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            CancellationToken token = timeout.Token;
            bluetoothDevice = await ResolveWithCacheRefreshAsync(device.Address,
                async (address, cancellation) => await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellation).WaitAsync(cancellation),
                WaitForAdvertisementAsync, token)
                ?? throw new LocalizedOperationException("Bluetooth.DeviceUnavailable");
            connectionStatusHandler = (sender, _) => OnConnectionStatusChanged(sender, epoch);
            bluetoothDevice.ConnectionStatusChanged += connectionStatusHandler;
            GattDeviceServicesResult services = await bluetoothDevice.GetGattServicesForUuidAsync(
                SoundbarProtocol.ServiceId, BluetoothCacheMode.Uncached).AsTask(token).WaitAsync(token);
            EnsureSuccess(services.Status, Localization.Text("Bluetooth.ReadServices"));
            service = services.Services.FirstOrDefault();
            foreach (GattDeviceService extra in services.Services.Skip(1))
                extra.Dispose();
            if (service is null)
                throw new LocalizedOperationException("Bluetooth.MissingService");
            GattCharacteristicsResult characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
                .AsTask(token).WaitAsync(token);
            EnsureSuccess(characteristics.Status, Localization.Text("Bluetooth.ReadChannels"));
            writeCharacteristic = characteristics.Characteristics.FirstOrDefault(characteristic => characteristic.Uuid == SoundbarProtocol.WriteId);
            notifyCharacteristic = characteristics.Characteristics.FirstOrDefault(characteristic => characteristic.Uuid == SoundbarProtocol.NotifyId);
            if (writeCharacteristic is null || notifyCharacteristic is null ||
                !notifyCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                throw new LocalizedOperationException("Bluetooth.MissingChannels");
            session = await GattSession.FromDeviceIdAsync(bluetoothDevice.BluetoothDeviceId).AsTask(token).WaitAsync(token);
            if (session?.CanMaintainConnection == true)
                session.MaintainConnection = true;
            notificationHandler = (_, args) => OnValueChanged(args, epoch);
            notifyCharacteristic.ValueChanged += notificationHandler;
            EnsureSuccess(await notifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(token).WaitAsync(token), Localization.Text("Bluetooth.EnableNotifications"));
            for (int attempt = 0; attempt < 5 && !identityCompletion.Task.IsCompleted; attempt++)
            {
                await WritePayloadAsync(SoundbarProtocol.Handshake, token);
                await Task.WhenAny(identityCompletion.Task, Task.Delay(TimeSpan.FromSeconds(1), token));
                token.ThrowIfCancellationRequested();
            }
            SoundbarIdentity identity = await identityCompletion.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
            if (identity.ModelCode != 0x27)
                throw new LocalizedOperationException("Bluetooth.WrongModel", identity.Model);
            await ReadStateAsync(token);
            lock (receiveLock)
            {
                token.ThrowIfCancellationRequested();
                if (epoch != connectionEpoch)
                    throw new OperationCanceledException(token);
                Volatile.Write(ref connected, 1);
            }
            pollingTask = PollAsync(connectionLifetime.Token, epoch);
            return identity;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await DisconnectCoreAsync();
            throw new TimeoutException(Localization.Text("Bluetooth.ConnectTimeout"));
        }
        catch
        {
            await DisconnectCoreAsync();
            throw;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    internal static async Task<T?> ResolveWithCacheRefreshAsync<T>(ulong address,
        Func<ulong, CancellationToken, Task<T?>> resolve,
        Func<ulong, CancellationToken, Task> refreshCache,
        CancellationToken cancellationToken) where T : class, IDisposable
    {
        T? result = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await resolve(address, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is not null)
                return result;
            await refreshCache(address, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            result = await resolve(address, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
    }

    private static async Task WaitForAdvertisementAsync(ulong address, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        TaskCompletionSource advertisement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BluetoothLEAdvertisementWatcher watcher = new() { ScanningMode = BluetoothLEScanningMode.Active };
        void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (args.BluetoothAddress == address)
                advertisement.TrySetResult();
        }
        void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
            => advertisement.TrySetException(new LocalizedOperationException("Bluetooth.SearchStopped"));
        watcher.Received += OnReceived;
        watcher.Stopped += OnStopped;
        try
        {
            watcher.Start();
            await advertisement.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        }
        catch (TimeoutException)
        {
            throw new LocalizedOperationException("Bluetooth.SavedDeviceNotAdvertising");
        }
        finally
        {
            watcher.Received -= OnReceived;
            watcher.Stopped -= OnStopped;
            watcher.Stop();
        }
    }

    public Task SendAsync(SoundbarCommand command, CancellationToken cancellationToken)
        => ExecuteMutationAsync(SoundbarProtocol.CommandPayload(command),
            (before, after) => SoundbarProtocol.IsCommandConfirmed(command, before, after), cancellationToken);

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken)
        => ExecuteMutationAsync(SoundbarProtocol.VolumePayload(volume), (_, after) => after.Volume == volume, cancellationToken);

    private async Task ExecuteMutationAsync(byte[] payload, Func<SoundbarState, SoundbarState, bool> confirmed, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateOperationToken(cancellationToken, out long epoch);
        await WaitForOperationGateAsync(timeout.Token, cancellationToken);
        try
        {
            EnsureConnected();
            SoundbarState before = await ReadStateAsync(timeout.Token);
            await WritePayloadAsync(payload, timeout.Token);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(180), timeout.Token);
                SoundbarState after = await ReadStateAsync(timeout.Token);
                if (confirmed(before, after))
                    return;
            }
            throw new LocalizedOperationException("Bluetooth.ChangeUnconfirmed");
        }
        catch (TimeoutException)
        {
            LoseConnection(Localization.Text("Bluetooth.ConfirmationLost"), epoch);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LoseConnection(Localization.Text("Bluetooth.CommunicationTimeout"), epoch);
            throw new TimeoutException(Localization.Text("Bluetooth.CommandTimeout"));
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateOperationToken(cancellationToken, out _);
        await WaitForOperationGateAsync(timeout.Token, cancellationToken);
        try
        {
            EnsureConnected();
            await ReadStateAsync(timeout.Token);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task PollAsync(CancellationToken token, long epoch)
    {
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                await RefreshAsync(token);
                failures = 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (++failures >= 3)
                {
                    LoseConnection(Localization.Text("Bluetooth.ResponsesLost"), epoch);
                    return;
                }
            }
        }
    }

    private async Task<SoundbarState> ReadStateAsync(CancellationToken token)
    {
        TaskCompletionSource<SoundbarState> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (receiveLock)
        {
            token.ThrowIfCancellationRequested();
            stateResponse = response;
        }
        try
        {
            await WritePayloadAsync(SoundbarProtocol.StatusRequest, token);
            return await response.Task.WaitAsync(TimeSpan.FromSeconds(4), token);
        }
        finally
        {
            lock (receiveLock)
            {
                if (ReferenceEquals(stateResponse, response))
                    stateResponse = null;
            }
        }
    }

    private async Task WritePayloadAsync(byte[] payload, CancellationToken token)
    {
        GattCharacteristic characteristic = writeCharacteristic
            ?? throw new LocalizedOperationException("Bluetooth.NoControlChannel");
        GattWriteOption option;
        if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write))
            option = GattWriteOption.WriteWithResponse;
        else if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            option = GattWriteOption.WriteWithoutResponse;
        else
            throw new LocalizedOperationException("Bluetooth.ChannelNotWritable");
        using DataWriter writer = new();
        writer.WriteBytes(SoundbarProtocol.Encode(payload));
        EnsureSuccess(await characteristic.WriteValueAsync(writer.DetachBuffer(), option).AsTask(token).WaitAsync(token), Localization.Text("Bluetooth.SendCommand"));
    }

    private void OnValueChanged(GattValueChangedEventArgs args, long epoch)
    {
        if (epoch != Interlocked.Read(ref connectionEpoch))
            return;
        byte[] bytes = new byte[args.CharacteristicValue.Length];
        using DataReader reader = DataReader.FromBuffer(args.CharacteristicValue);
        reader.ReadBytes(bytes);
        SoundbarState? latest = null;
        lock (receiveLock)
        {
            if (epoch != connectionEpoch || connectionLifetime?.IsCancellationRequested != false)
                return;
            foreach (byte[] payload in decoder.Append(bytes))
            {
                if (SoundbarProtocol.TryParseIdentity(payload, out SoundbarIdentity? identity))
                    identityResponse?.TrySetResult(identity!);
                if (SoundbarProtocol.TryParseState(payload, out SoundbarState? updated))
                {
                    latest = updated;
                    Volatile.Write(ref state, updated);
                    stateResponse?.TrySetResult(updated!);
                }
            }
        }
        if (latest is not null && epoch == Interlocked.Read(ref connectionEpoch))
            StateChanged?.Invoke(latest);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, long epoch)
    {
        if (epoch == Interlocked.Read(ref connectionEpoch) && sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            LoseConnection(Localization.Text("Bluetooth.Disconnected"), epoch);
    }

    private void LoseConnection(string message, long expectedEpoch)
    {
        bool wasConnected;
        long lossEpoch;
        lock (receiveLock)
        {
            if (expectedEpoch != connectionEpoch || connectionLifetime?.IsCancellationRequested != false)
                return;
            wasConnected = Interlocked.Exchange(ref connected, 0) == 1;
            lossEpoch = ++connectionEpoch;
            connectionLifetime?.Cancel();
            Volatile.Write(ref state, null);
            decoder.Reset();
        }
        if (wasConnected && lossEpoch == Interlocked.Read(ref connectionEpoch))
            ConnectionLost?.Invoke(message);
    }

    public async Task DisconnectAsync()
    {
        await lifecycleGate.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task DisconnectCoreAsync()
    {
        lock (receiveLock)
        {
            ++connectionEpoch;
            Volatile.Write(ref connected, 0);
            connectionLifetime?.Cancel();
            identityResponse?.TrySetCanceled();
            stateResponse?.TrySetCanceled();
            identityResponse = null;
            stateResponse = null;
            Volatile.Write(ref state, null);
            decoder.Reset();
        }
        if (pollingTask is not null)
            await pollingTask;
        pollingTask = null;
        await operationGate.WaitAsync();
        try
        {
            if (notifyCharacteristic is not null && notificationHandler is not null)
                notifyCharacteristic.ValueChanged -= notificationHandler;
            if (bluetoothDevice is not null && connectionStatusHandler is not null)
                bluetoothDevice.ConnectionStatusChanged -= connectionStatusHandler;
            notificationHandler = null;
            connectionStatusHandler = null;
            notifyCharacteristic = null;
            writeCharacteristic = null;
            session?.Dispose();
            service?.Dispose();
            bluetoothDevice?.Dispose();
            session = null;
            service = null;
            bluetoothDevice = null;
            connectionLifetime?.Dispose();
            connectionLifetime = null;
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task AcquireConnectionLifecycleAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        await lifecycleGate.WaitAsync(token);
        bool disposing = Volatile.Read(ref disposed);
        if (disposing)
            lifecycleGate.Release();
        ObjectDisposedException.ThrowIf(disposing, this);
    }

    private async Task WaitForOperationGateAsync(CancellationToken operationToken, CancellationToken callerToken)
    {
        try
        {
            await operationGate.WaitAsync(operationToken);
        }
        catch (OperationCanceledException exception) when (!callerToken.IsCancellationRequested)
        {
            throw new TimeoutException(Localization.Text("Bluetooth.CommandTimeout"), exception);
        }
    }

    private CancellationTokenSource CreateOperationToken(CancellationToken callerToken, out long epoch)
    {
        lock (receiveLock)
        {
            EnsureConnected();
            epoch = connectionEpoch;
            CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken, connectionLifetime!.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(18));
            return timeout;
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new LocalizedOperationException("Bluetooth.ConnectionRequired");
    }

    private static void EnsureSuccess(GattCommunicationStatus status, string operation)
    {
        if (status != GattCommunicationStatus.Success)
            throw new LocalizedOperationException("Bluetooth.OperationFailed", operation, (int)status);
    }

    private static async Task EnsureAdapterAsync(CancellationToken token)
    {
        BluetoothAdapter? adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(token).WaitAsync(token);
        if (adapter is null || !adapter.IsLowEnergySupported)
            throw new LocalizedOperationException("Bluetooth.AdapterMissing");
        Radio? radio = await adapter.GetRadioAsync().AsTask(token).WaitAsync(token);
        if (radio is not null && radio.State != RadioState.On)
            throw new LocalizedOperationException("Bluetooth.Disabled");
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref disposed))
            return;
        Volatile.Write(ref disposed, true);
        await DisconnectAsync();
    }
}
