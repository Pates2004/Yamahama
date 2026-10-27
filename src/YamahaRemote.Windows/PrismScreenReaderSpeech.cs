using System.Diagnostics;
using System.Text;
using Prismatoid;
using Prismatoid.NativeInterop;

namespace YamahaRemote.Windows;

internal interface IPrismNative
{
    nint Initialize();
    nint CreateBackend(nint context, PrismBackendId id);
    PrismBackendFeature GetFeatures(nint backend);
    bool InitializeBackend(nint backend);
    bool Speak(nint backend, string message, bool interrupt);
    void FreeBackend(nint backend);
    void Shutdown(nint context);
}

internal sealed class PrismScreenReaderSpeech(IPrismNative? native = null) : IDisposable
{
    private static readonly PrismBackendId[] Readers =
    [
        PrismBackendId.Nvda, PrismBackendId.Jaws, PrismBackendId.Zdsr,
        PrismBackendId.ZoomText, PrismBackendId.BoyPcReader, PrismBackendId.PcTalker,
        PrismBackendId.SenseReader, PrismBackendId.SystemAccess, PrismBackendId.WindowEyes
    ];
    private readonly IPrismNative _native = native ?? new PrismNative();
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private nint _context;
    private bool _disposed;
    private bool _libraryUnavailable;

    internal PrismBackendId? LastBackend { get; private set; }

    internal bool TrySpeak(string message)
    {
        VerifyThread();
        LastBackend = null;
        if (_disposed || _libraryUnavailable || string.IsNullOrWhiteSpace(message) || message.Contains('\0'))
            return false;
        bool accepted = false;
        try
        {
            if (_context == 0)
                _context = _native.Initialize();
            if (_context == 0)
                return false;
            foreach (PrismBackendId id in Readers)
            {
                nint backend = _native.CreateBackend(_context, id);
                if (backend == 0)
                    continue;
                try
                {
                    PrismBackendFeature required = PrismBackendFeature.IsSupportedAtRuntime | PrismBackendFeature.SupportsSpeak;
                    if ((_native.GetFeatures(backend) & required) != required || !_native.InitializeBackend(backend))
                        continue;
                    accepted = _native.Speak(backend, message, interrupt: false);
                    if (accepted)
                        LastBackend = id;
                }
                finally
                {
                    _native.FreeBackend(backend);
                }
                if (accepted)
                    return true;
            }
        }
        catch (Exception exception) when (IsLibraryFailure(exception))
        {
            _libraryUnavailable = true;
            Trace.WriteLine(exception);
        }
        return accepted;
    }

    public void Dispose()
    {
        VerifyThread();
        if (_disposed)
            return;
        _disposed = true;
        nint context = _context;
        _context = 0;
        if (context == 0)
            return;
        try { _native.Shutdown(context); }
        catch (Exception exception) when (IsLibraryFailure(exception)) { Trace.WriteLine(exception); }
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("Prism speech must be used and disposed on its owning thread.");
    }

    private static bool IsLibraryFailure(Exception exception) =>
        exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException;
}

internal sealed unsafe class PrismNative : IPrismNative
{
    public nint Initialize()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Windows reader backends require an STA owner.");
        PrismConfig configuration = Methods.prism_config_init();
        return (nint)Methods.prism_init(&configuration);
    }

    public nint CreateBackend(nint context, PrismBackendId id) =>
        (nint)Methods.prism_registry_create((Prismatoid.NativeInterop.PrismContext*)context, id.Value);

    public PrismBackendFeature GetFeatures(nint backend) =>
        (PrismBackendFeature)Methods.prism_backend_get_features((Prismatoid.NativeInterop.PrismBackend*)backend);

    public bool InitializeBackend(nint backend) =>
        Methods.prism_backend_initialize((Prismatoid.NativeInterop.PrismBackend*)backend) == PrismError.PRISM_OK;

    public bool Speak(nint backend, string message, bool interrupt)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(message + '\0');
        fixed (byte* text = utf8)
            return Methods.prism_backend_speak((Prismatoid.NativeInterop.PrismBackend*)backend, (sbyte*)text, interrupt) == PrismError.PRISM_OK;
    }

    public void FreeBackend(nint backend) => Methods.prism_backend_free((Prismatoid.NativeInterop.PrismBackend*)backend);

    public void Shutdown(nint context) => Methods.prism_shutdown((Prismatoid.NativeInterop.PrismContext*)context);
}
