using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class BluetoothAddressResolutionTests
{
    private const ulong Address = 0x123456789ABC;

    [Fact]
    public async Task CachedDeviceDoesNotTriggerDiscoveryOrDisposeTheReturnedHandle()
    {
        DeviceHandle device = new();
        int resolutions = 0;
        using CancellationTokenSource cancellation = new();
        DeviceHandle? result = await BluetoothSoundbarController.ResolveWithCacheRefreshAsync(Address,
            (address, token) =>
            {
                Assert.Equal(Address, address);
                Assert.Equal(cancellation.Token, token);
                resolutions++;
                return Task.FromResult<DeviceHandle?>(device);
            }, (_, _) => throw new InvalidOperationException("Discovery must not run for a cached device."), cancellation.Token);
        Assert.Same(device, result);
        Assert.NotNull(result);
        Assert.Equal(1, resolutions);
        Assert.False(device.Disposed);
        result.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingCacheRefreshesOnlyTheRequestedAddressAndRetriesOnce(bool resolvedAfterRefresh)
    {
        DeviceHandle device = new();
        List<string> operations = [];
        using CancellationTokenSource cancellation = new();
        DeviceHandle? result = await BluetoothSoundbarController.ResolveWithCacheRefreshAsync(Address,
            (address, token) =>
            {
                Assert.Equal(Address, address);
                Assert.Equal(cancellation.Token, token);
                operations.Add("Resolve");
                return Task.FromResult(operations.Count == 3 && resolvedAfterRefresh ? device : null);
            }, (address, token) =>
            {
                Assert.Equal(Address, address);
                Assert.Equal(cancellation.Token, token);
                operations.Add("Discover");
                return Task.CompletedTask;
            }, cancellation.Token);
        Assert.Equal(new[] { "Resolve", "Discover", "Resolve" }, operations);
        Assert.Same(resolvedAfterRefresh ? device : null, result);
        Assert.False(device.Disposed);
        device.Dispose();
    }

    [Fact]
    public async Task PreCanceledResolutionNeverCallsWindowsOrDiscovery()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BluetoothSoundbarController.ResolveWithCacheRefreshAsync<DeviceHandle>(Address,
            (_, _) => throw new InvalidOperationException("Resolution must not run."),
            (_, _) => throw new InvalidOperationException("Discovery must not run."), cancellation.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CancellationBetweenStagesPreventsFurtherOperations(int canceledStage)
    {
        using CancellationTokenSource cancellation = new();
        int stage = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BluetoothSoundbarController.ResolveWithCacheRefreshAsync<DeviceHandle>(Address,
            (_, _) =>
            {
                if (++stage == canceledStage)
                    cancellation.Cancel();
                return Task.FromResult<DeviceHandle?>(null);
            }, (_, _) =>
            {
                if (++stage == canceledStage)
                    cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellation.Token));
        Assert.Equal(canceledStage, stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDisposesAnAcquiredButUnreturnedHandle(bool afterRefresh)
    {
        using CancellationTokenSource cancellation = new();
        DeviceHandle device = new();
        int resolutions = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BluetoothSoundbarController.ResolveWithCacheRefreshAsync<DeviceHandle>(Address,
            (_, _) =>
            {
                if (++resolutions == 1 && afterRefresh)
                    return Task.FromResult<DeviceHandle?>(null);
                cancellation.Cancel();
                return Task.FromResult<DeviceHandle?>(device);
            }, (_, _) => Task.CompletedTask, cancellation.Token));
        Assert.True(device.Disposed);
        Assert.Equal(afterRefresh ? 2 : 1, resolutions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FailureAtAnyStagePropagatesWithoutExtraRetries(int failedStage)
    {
        InvalidOperationException failure = new("Simulated Windows failure.");
        int stage = 0;
        Exception caught = await Assert.ThrowsAsync<InvalidOperationException>(() => BluetoothSoundbarController.ResolveWithCacheRefreshAsync<DeviceHandle>(Address,
            (_, _) => ++stage == failedStage ? throw failure : Task.FromResult<DeviceHandle?>(null),
            (_, _) => ++stage == failedStage ? throw failure : Task.CompletedTask, CancellationToken.None));
        Assert.Same(failure, caught);
        Assert.Equal(failedStage, stage);
    }

    [Fact]
    public async Task PendingDiscoveryIsCancelableAndNeverStartsTheSecondResolution()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource discoveryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int resolutions = 0;
        Task<DeviceHandle?> operation = BluetoothSoundbarController.ResolveWithCacheRefreshAsync<DeviceHandle>(Address,
            (_, _) =>
            {
                resolutions++;
                return Task.FromResult<DeviceHandle?>(null);
            }, async (_, token) =>
            {
                discoveryStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }, cancellation.Token);
        await discoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(operation.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, resolutions);
    }

    private sealed class DeviceHandle : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
