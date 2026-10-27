using Prismatoid;
using Xunit;

namespace YamahaRemote.Windows.Tests;

public sealed class PrismScreenReaderSpeechTests
{
    [Fact]
    public void InitializationIsLazyAndEmptyOrEmbeddedNullMessagesAreRejected()
    {
        FakePrismNative native = new();
        using PrismScreenReaderSpeech speech = new(native);
        Assert.False(speech.TrySpeak(""));
        Assert.False(speech.TrySpeak(" \t"));
        Assert.False(speech.TrySpeak("first\0second"));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public void FirstAvailableReaderReceivesUnicodeOnceWithoutInterrupting()
    {
        FakePrismNative native = new();
        native.Available.UnionWith([PrismBackendId.Nvda, PrismBackendId.Jaws]);
        using PrismScreenReaderSpeech speech = new(native);
        Assert.True(speech.TrySpeak("Głośność 9. Wzmocnienie subwoofera −2."));
        Assert.Equal(PrismBackendId.Nvda, speech.LastBackend);
        var request = Assert.Single(native.Spoken);
        Assert.Equal("Głośność 9. Wzmocnienie subwoofera −2.", request.Message);
        Assert.False(request.Interrupt);
        Assert.Equal(new[] { PrismBackendId.Nvda }, native.Created);
        Assert.Empty(native.LiveHandles);
        Assert.Equal(new[] { "Context", "Create", "Features", "Initialize", "Speak", "Free" }, native.Calls);
    }

    [Fact]
    public void NoRunningReaderDoesNotStartAStandaloneOrUiaBackend()
    {
        FakePrismNative native = new();
        using PrismScreenReaderSpeech speech = new(native);
        Assert.False(speech.TrySpeak("Volume 9"));
        Assert.Equal(9, native.Created.Count);
        Assert.DoesNotContain(PrismBackendId.Sapi, native.Created);
        Assert.DoesNotContain(PrismBackendId.OneCore, native.Created);
        Assert.DoesNotContain(PrismBackendId.Uia, native.Created);
        Assert.DoesNotContain("Initialize", native.Calls);
        Assert.Empty(native.Spoken);
        Assert.Empty(native.LiveHandles);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void BothRuntimeAvailabilityAndSpeechFeatureAreRequired(bool available, bool canSpeak)
    {
        FakePrismNative native = new() { SupportsSpeech = canSpeak };
        if (available)
            native.Available.Add(PrismBackendId.Nvda);
        using PrismScreenReaderSpeech speech = new(native);
        Assert.False(speech.TrySpeak("Volume 9"));
        Assert.Empty(native.Spoken);
        Assert.DoesNotContain("Initialize", native.Calls);
        Assert.Empty(native.LiveHandles);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InitializationOrSpeechFailureFreesReaderAndAllowsNextReader(bool failInitialization)
    {
        FakePrismNative native = new();
        native.Available.UnionWith([PrismBackendId.Nvda, PrismBackendId.Jaws]);
        if (failInitialization)
            native.FailInitialization.Add(PrismBackendId.Nvda);
        else
            native.FailSpeech.Add(PrismBackendId.Nvda);
        using PrismScreenReaderSpeech speech = new(native);
        Assert.True(speech.TrySpeak("Volume 9"));
        Assert.Equal(PrismBackendId.Jaws, speech.LastBackend);
        Assert.Equal(new[] { PrismBackendId.Nvda, PrismBackendId.Jaws }, native.Created);
        Assert.Equal(failInitialization ? 1 : 2, native.Spoken.Count);
        Assert.Empty(native.LiveHandles);
    }

    [Fact]
    public void NullBackendAndNullContextAreHandledWithoutLeaking()
    {
        FakePrismNative native = new() { NullContext = true, NullBackends = true };
        using PrismScreenReaderSpeech speech = new(native);
        Assert.False(speech.TrySpeak("First"));
        Assert.Empty(native.Created);
        native.NullContext = false;
        Assert.False(speech.TrySpeak("Second"));
        Assert.Equal(2, native.Calls.Count(call => call == "Context"));
        Assert.DoesNotContain("Features", native.Calls);
        Assert.Empty(native.LiveHandles);
    }

    [Fact]
    public void ReaderRestartIsObservedUsingFreshHandlesAndOneContext()
    {
        FakePrismNative native = new();
        native.Available.Add(PrismBackendId.Nvda);
        using PrismScreenReaderSpeech speech = new(native);
        Assert.True(speech.TrySpeak("Before restart"));
        native.Available.Clear();
        Assert.False(speech.TrySpeak("Reader stopped"));
        Assert.Null(speech.LastBackend);
        native.Available.Add(PrismBackendId.Nvda);
        Assert.True(speech.TrySpeak("Reader restarted"));
        Assert.Equal(1, native.Calls.Count(call => call == "Context"));
        Assert.Equal(2, native.Spoken.Count);
        Assert.NotEqual(native.Spoken[0].Handle, native.Spoken[1].Handle);
        Assert.Empty(native.LiveHandles);
    }

    [Fact]
    public void MissingLibraryFailsQuietlyWithoutRepeatedLoadAttempts()
    {
        FakePrismNative native = new() { FailContextLoad = true };
        using PrismScreenReaderSpeech speech = new(native);
        Assert.False(speech.TrySpeak("First"));
        Assert.False(speech.TrySpeak("Second"));
        Assert.Equal(new[] { "Context" }, native.Calls);
    }

    [Fact]
    public void NativeFeatureFailureStillReleasesBackendAndContext()
    {
        FakePrismNative native = new() { FailFeatures = true };
        PrismScreenReaderSpeech speech = new(native);
        Assert.False(speech.TrySpeak("First"));
        Assert.Empty(native.LiveHandles);
        speech.Dispose();
        speech.Dispose();
        Assert.Equal(1, native.Calls.Count(call => call == "Shutdown"));
        Assert.False(speech.TrySpeak("Closed"));
    }

    [Fact]
    public void AcceptedSpeechDoesNotBecomeFailureWhenReleaseReportsALibraryError()
    {
        FakePrismNative native = new() { FailRelease = true };
        native.Available.Add(PrismBackendId.Nvda);
        using PrismScreenReaderSpeech speech = new(native);
        Assert.True(speech.TrySpeak("Accepted"));
        Assert.Single(native.Spoken);
        Assert.False(speech.TrySpeak("Library unavailable"));
        Assert.Empty(native.LiveHandles);
    }

    [Fact]
    public void ForeignThreadCannotUseOrDisposeOwnersNativeState()
    {
        FakePrismNative native = new();
        using PrismScreenReaderSpeech speech = new(native);
        Exception? speakError = null;
        Exception? disposeError = null;
        Thread foreign = new(() =>
        {
            speakError = Record.Exception(() => speech.TrySpeak("Wrong thread"));
            disposeError = Record.Exception(speech.Dispose);
        });
        foreign.Start();
        Assert.True(foreign.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(speakError);
        Assert.IsType<InvalidOperationException>(disposeError);
        Assert.Empty(native.Calls);
        native.Available.Add(PrismBackendId.Nvda);
        Assert.True(speech.TrySpeak("Owner remains usable"));
    }

    [Fact]
    public void DisposeIsIdempotentAndNoRequestUsesAClosedContext()
    {
        FakePrismNative native = new();
        native.Available.Add(PrismBackendId.Nvda);
        PrismScreenReaderSpeech speech = new(native);
        Assert.True(speech.TrySpeak("Open"));
        speech.Dispose();
        string[] calls = native.Calls.ToArray();
        speech.Dispose();
        Assert.False(speech.TrySpeak("Closed"));
        Assert.Equal(calls, native.Calls);
        Assert.Equal(1, calls.Count(call => call == "Shutdown"));
    }

    private sealed class FakePrismNative : IPrismNative
    {
        private int _nextHandle = 10;
        internal HashSet<PrismBackendId> Available { get; } = [];
        internal HashSet<PrismBackendId> FailInitialization { get; } = [];
        internal HashSet<PrismBackendId> FailSpeech { get; } = [];
        internal Dictionary<nint, PrismBackendId> LiveHandles { get; } = [];
        internal List<string> Calls { get; } = [];
        internal List<PrismBackendId> Created { get; } = [];
        internal List<(nint Handle, string Message, bool Interrupt)> Spoken { get; } = [];
        internal bool NullContext { get; set; }
        internal bool NullBackends { get; set; }
        internal bool SupportsSpeech { get; set; } = true;
        internal bool FailContextLoad { get; set; }
        internal bool FailFeatures { get; set; }
        internal bool FailRelease { get; set; }
        public nint Initialize()
        {
            Calls.Add("Context");
            if (FailContextLoad)
                throw new DllNotFoundException();
            return NullContext ? 0 : 1;
        }
        public nint CreateBackend(nint context, PrismBackendId id)
        {
            Assert.Equal(1, context);
            Calls.Add("Create");
            Created.Add(id);
            if (NullBackends)
                return 0;
            nint handle = ++_nextHandle;
            LiveHandles.Add(handle, id);
            return handle;
        }
        public PrismBackendFeature GetFeatures(nint backend)
        {
            Calls.Add("Features");
            if (FailFeatures)
                throw new EntryPointNotFoundException();
            return (Available.Contains(LiveHandles[backend]) ? PrismBackendFeature.IsSupportedAtRuntime : 0)
                | (SupportsSpeech ? PrismBackendFeature.SupportsSpeak : 0);
        }
        public bool InitializeBackend(nint backend)
        {
            Calls.Add("Initialize");
            return !FailInitialization.Contains(LiveHandles[backend]);
        }
        public bool Speak(nint backend, string message, bool interrupt)
        {
            Calls.Add("Speak");
            Spoken.Add((backend, message, interrupt));
            return !FailSpeech.Contains(LiveHandles[backend]);
        }
        public void FreeBackend(nint backend)
        {
            Calls.Add("Free");
            Assert.True(LiveHandles.Remove(backend));
            if (FailRelease)
                throw new DllNotFoundException();
        }
        public void Shutdown(nint context)
        {
            Assert.Equal(1, context);
            Assert.Empty(LiveHandles);
            Calls.Add("Shutdown");
        }
    }
}
