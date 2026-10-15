using System.IO;
using System.Security;
using Xunit;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed class WindowsStartupRegistrationTests
{
    private const string ExecutablePath = @"C:\Yamaha Test\Yamahama.exe";
    private const string ConfigurationPath = @"C:\Yamaha Settings\config.json";
    private const string DefaultCommand = "\"C:\\Yamaha Test\\Yamahama.exe\"";
    private const string ConfiguredCommand = DefaultCommand + " --config \"C:\\Yamaha Settings\\config.json\"";

    [Fact]
    public void DefaultExecutableIsTheApphostBesideTheApplication()
    {
        FakeStartupRegistrationStore store = new();
        WindowsStartupRegistration registration = new(null, null, store, store.ReadFileAttributes);

        registration.SetEnabled(true);

        string expectedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Yamahama.exe"));
        Assert.Equal($"\"{expectedPath}\"", store.Value);
        Assert.Equal(expectedPath, store.VerifiedPath);
    }

    [Theory]
    [InlineData(@"C:\Yamaha\Yamahama.exe", null, "\"C:\\Yamaha\\Yamahama.exe\"")]
    [InlineData(ExecutablePath, ConfigurationPath, ConfiguredCommand)]
    [InlineData(@"C:\Apps & Tools\Yamahama.EXE", @"C:\Data & Settings\my config.json",
        "\"C:\\Apps & Tools\\Yamahama.EXE\" --config \"C:\\Data & Settings\\my config.json\"")]
    [InlineData(@"\\server\share name\Yamahama.exe", @"\\server\settings\config.json",
        "\"\\\\server\\share name\\Yamahama.exe\" --config \"\\\\server\\settings\\config.json\"")]
    [InlineData(@"C:\音楽\Yamahama.exe", @"C:\設定\config.json", "\"C:\\音楽\\Yamahama.exe\" --config \"C:\\設定\\config.json\"")]
    public void CommandQuotesEachAbsolutePath(string executablePath, string? configurationPath, string expected)
    {
        FakeStartupRegistrationStore store = new();
        WindowsStartupRegistration registration = new(executablePath, configurationPath, store, store.ReadFileAttributes);

        registration.SetEnabled(true);

        Assert.Equal(expected, store.Value);
        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void RelativePathsAreResolvedBeforeRegistration()
    {
        FakeStartupRegistrationStore store = new();
        const string executablePath = @"app\..\Yamahama.exe";
        const string configurationPath = @"settings\..\chosen config.json";
        WindowsStartupRegistration registration = new(executablePath, configurationPath, store, store.ReadFileAttributes);

        registration.SetEnabled(true);

        Assert.Equal($"\"{Path.GetFullPath(executablePath)}\" --config \"{Path.GetFullPath(configurationPath)}\"", store.Value);
        Assert.Equal(Path.GetFullPath(executablePath), store.VerifiedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("C:\\Yamaha\n\\Yamahama.exe")]
    [InlineData("C:\\Yamaha\r\\Yamahama.exe")]
    [InlineData("C:\\Yamaha\t\\Yamahama.exe")]
    [InlineData("C:\\Yamaha\0\\Yamahama.exe")]
    [InlineData("C:\\Yamaha\u2028\\Yamahama.exe")]
    [InlineData("C:\\Yamaha\u2029\\Yamahama.exe")]
    [InlineData("\"C:\\Yamaha\\Yamahama.exe\" --extra")]
    [InlineData(@"C:\Yamaha\Yamahama.exe --extra")]
    [InlineData(@"C:\Yamaha\Yamahama.dll")]
    [InlineData(@"C:\Yamaha\*.exe")]
    [InlineData(@"C:\Yamaha\Yamahama?.exe")]
    [InlineData(@"C:\Yamaha|Test\Yamahama.exe")]
    [InlineData(@"C:\Yamaha<Test\Yamahama.exe")]
    [InlineData(@"C:\Yamaha>Test\Yamahama.exe")]
    [InlineData(@"C:\Yamaha\Yamahama.exe:payload.exe")]
    [InlineData(@"C:\Yamaha\Yamahama.exe\")]
    [InlineData(@"C:\")]
    [InlineData(@"\\.\C:\Yamaha\Yamahama.exe")]
    [InlineData(@"\\?\C:\Yamaha\Yamahama.exe")]
    public void InvalidExecutableIsRejectedWithoutAccessingStorage(string path)
    {
        FakeStartupRegistrationStore store = new();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new WindowsStartupRegistration(path, null, store, store.ReadFileAttributes));

        Assert.Equal("executablePath", error.ParamName);
        AssertUnused(store);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("C:\\Settings\\config.json\n")]
    [InlineData("C:\\Settings\\config.json\r")]
    [InlineData("C:\\Settings\\config.json\t")]
    [InlineData("C:\\Settings\\config.json\0")]
    [InlineData("C:\\Settings\\config.json\" --extra \"")]
    [InlineData(@"C:\Settings\*.json")]
    [InlineData(@"C:\Settings\config?.json")]
    [InlineData(@"C:\Settings\config.json:stream")]
    [InlineData(@"C:\Settings\")]
    [InlineData(@"C:\")]
    [InlineData(@"\\.\C:\Settings\config.json")]
    public void InvalidConfigurationIsRejectedWithoutAccessingStorage(string path)
    {
        FakeStartupRegistrationStore store = new();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new WindowsStartupRegistration(ExecutablePath, path, store, store.ReadFileAttributes));

        Assert.Equal("configurationPath", error.ParamName);
        AssertUnused(store);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunCommandLengthIncludesQuotesAndConfiguration(bool useConfiguration)
    {
        FakeStartupRegistrationStore store = new();
        string executablePath = useConfiguration ? ExecutablePath : @"C:\" + new string('a', 251) + ".exe";
        string? configurationPath = useConfiguration
            ? @"C:\" + new string('a', 260 - DefaultCommand.Length - " --config \"\"".Length - @"C:\".Length - ".json".Length) + ".json"
            : null;
        WindowsStartupRegistration registration = new(executablePath, configurationPath, store, store.ReadFileAttributes);

        registration.SetEnabled(true);

        Assert.Equal(260, Assert.IsType<string>(store.Value).Length);
        FakeStartupRegistrationStore oversizedStore = new();
        WindowsStartupRegistration oversized = new(
            useConfiguration ? executablePath : executablePath.Replace(".exe", "a.exe", StringComparison.Ordinal),
            configurationPath?.Replace(".json", "a.json", StringComparison.Ordinal), oversizedStore, oversizedStore.ReadFileAttributes);
        ArgumentException error = Assert.Throws<ArgumentException>(() => oversized.SetEnabled(true));
        Assert.Contains("260", error.Message);
        AssertUnused(oversizedStore);
    }

    [Fact]
    public void ConstructorDoesNotReadTheRegistryOrFileSystem()
    {
        FakeStartupRegistrationStore store = new();

        _ = CreateRegistration(store);

        AssertUnused(store);
    }

    [Fact]
    public void RegistrationImplementsTheInterfaceAndOperationsAreIdempotent()
    {
        FakeStartupRegistrationStore store = new();
        IStartupRegistration registration = CreateRegistration(store);

        Assert.False(registration.IsEnabled);
        registration.SetEnabled(false);
        Assert.Equal(0, store.DeleteCount);
        Assert.Equal(0, store.FileReadCount);

        registration.SetEnabled(true);
        registration.SetEnabled(true);
        Assert.True(registration.IsEnabled);
        Assert.Equal(DefaultCommand, store.Value);
        Assert.Equal(1, store.WriteCount);
        Assert.Equal(2, store.FileReadCount);

        registration.SetEnabled(false);
        registration.SetEnabled(false);
        Assert.False(registration.IsEnabled);
        Assert.Null(store.Value);
        Assert.Equal(1, store.DeleteCount);
        Assert.Equal(2, store.FileReadCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(ExecutablePath)]
    [InlineData("\"C:\\Another Installation\\Yamahama.exe\"")]
    [InlineData(DefaultCommand + " --extra")]
    [InlineData(ConfiguredCommand)]
    [InlineData("\"c:\\yamaha test\\yamahama.exe\"")]
    [InlineData(1)]
    public void EnabledRequiresTheExactOwnedCommand(object? value)
    {
        FakeStartupRegistrationStore store = new() { Value = value };
        WindowsStartupRegistration registration = CreateRegistration(store);

        Assert.False(registration.IsEnabled);
        Assert.Same(value, store.Value);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, store.DeleteCount);
        Assert.Equal(0, store.FileReadCount);
    }

    [Fact]
    public void EnabledTracksCurrentStorageAndConfigurationWithoutCheckingTheFileSystem()
    {
        FakeStartupRegistrationStore store = new() { Value = DefaultCommand };
        WindowsStartupRegistration registration = CreateRegistration(store, ConfigurationPath);

        Assert.False(registration.IsEnabled);
        store.Value = ConfiguredCommand;
        Assert.True(registration.IsEnabled);
        store.Value = DefaultCommand + " --config \"C:\\Other Settings\\config.json\"";
        Assert.False(registration.IsEnabled);
        store.Value = null;
        Assert.False(registration.IsEnabled);
        Assert.Equal(0, store.FileReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignValuesAreNeverOverwrittenOrDeleted(bool enabled)
    {
        object[] foreignValues = ["", "\"C:\\Another Installation\\Yamahama.exe\"", ConfiguredCommand,
            DefaultCommand + " --extra", 1, new[] { DefaultCommand }];
        foreach (object foreignValue in foreignValues)
        {
            FakeStartupRegistrationStore store = new() { Value = foreignValue };
            WindowsStartupRegistration registration = CreateRegistration(store);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(enabled));

            Assert.Contains(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", error.Message);
            Assert.Contains("'Yamahama'", error.Message);
            Assert.Contains(DefaultCommand, error.Message);
            Assert.Contains("left unchanged", error.Message);
            Assert.Same(foreignValue, store.Value);
            Assert.Equal(0, store.WriteCount);
            Assert.Equal(0, store.DeleteCount);
            Assert.Equal(0, store.FileReadCount);
        }
    }

    [Fact]
    public void ConfigurationSpecificRegistrationCannotDeleteADifferentConfiguration()
    {
        FakeStartupRegistrationStore store = new() { Value = ConfiguredCommand };
        WindowsStartupRegistration registration = CreateRegistration(store, @"C:\Other Settings\config.json");

        Assert.False(registration.IsEnabled);
        Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(false));

        Assert.Equal(ConfiguredCommand, store.Value);
        Assert.Equal(0, store.DeleteCount);
    }

    [Fact]
    public void DisableDoesNotRequireTheExecutableToStillExist()
    {
        FakeStartupRegistrationStore store = new()
        {
            Value = ConfiguredCommand,
            FileError = new FileNotFoundException("The apphost has been removed.")
        };
        WindowsStartupRegistration registration = CreateRegistration(store, ConfigurationPath);

        registration.SetEnabled(false);

        Assert.Null(store.Value);
        Assert.Equal(1, store.DeleteCount);
        Assert.Equal(0, store.FileReadCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DefaultCommand)]
    public void FileSystemErrorsPropagateEvenWhenAlreadyRegistered(string? existingCommand)
    {
        Exception[] errors = [new FileNotFoundException("Missing apphost."),
            new DirectoryNotFoundException("Missing application directory."),
            new UnauthorizedAccessException("Access denied."), new IOException("Device unavailable."),
            new SecurityException("File access blocked.")];
        foreach (Exception expectedError in errors)
        {
            FakeStartupRegistrationStore store = new() { Value = existingCommand, FileError = expectedError };
            WindowsStartupRegistration registration = CreateRegistration(store);

            Exception? actualError = Record.Exception(() => registration.SetEnabled(true));

            Assert.Same(expectedError, actualError);
            Assert.Equal(existingCommand, store.Value);
            Assert.Equal(ExecutablePath, store.VerifiedPath);
            Assert.Equal(0, store.WriteCount);
            Assert.Equal(0, store.DeleteCount);
        }
    }

    [Fact]
    public void DirectoryCannotBeRegisteredAsAnApphost()
    {
        FakeStartupRegistrationStore store = new() { Attributes = FileAttributes.Directory };
        WindowsStartupRegistration registration = CreateRegistration(store);

        IOException error = Assert.Throws<IOException>(() => registration.SetEnabled(true));

        Assert.Contains("not a directory", error.Message);
        Assert.Null(store.Value);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public void DefaultFileSystemReaderRejectsAMissingApphostWithoutRegistryAccess()
    {
        FakeStartupRegistrationStore store = new();
        string missingExecutable = Path.Combine(AppContext.BaseDirectory, $"missing-{Guid.NewGuid():N}.exe");
        WindowsStartupRegistration registration = new(missingExecutable, null, store);

        Assert.Throws<FileNotFoundException>(() => registration.SetEnabled(true));

        Assert.Null(store.Value);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, store.DeleteCount);
    }

    [Fact]
    public void RegistryReadErrorsPropagateFromQueriesAndBothMutations()
    {
        foreach (Exception expectedError in CreateRegistryErrors())
        {
            FakeStartupRegistrationStore store = new() { ReadError = expectedError };
            WindowsStartupRegistration registration = CreateRegistration(store);

            Assert.Same(expectedError, Record.Exception(() => { _ = registration.IsEnabled; }));
            Assert.Same(expectedError, Record.Exception(() => registration.SetEnabled(true)));
            Assert.Same(expectedError, Record.Exception(() => registration.SetEnabled(false)));

            Assert.Equal(0, store.FileReadCount);
            Assert.Equal(0, store.WriteCount);
            Assert.Equal(0, store.DeleteCount);
        }
    }

    [Fact]
    public void RegistryWriteErrorsPropagateWithoutReportingRegistrationAsEnabled()
    {
        foreach (Exception expectedError in CreateRegistryErrors())
        {
            FakeStartupRegistrationStore store = new() { WriteError = expectedError };
            WindowsStartupRegistration registration = CreateRegistration(store);

            Assert.Same(expectedError, Record.Exception(() => registration.SetEnabled(true)));

            Assert.False(registration.IsEnabled);
            Assert.Null(store.Value);
            Assert.Equal(1, store.FileReadCount);
            Assert.Equal(1, store.WriteCount);
            Assert.Equal(0, store.DeleteCount);
        }
    }

    [Fact]
    public void RegistryDeleteErrorsPropagateWithoutReportingRegistrationAsDisabled()
    {
        foreach (Exception expectedError in CreateRegistryErrors())
        {
            FakeStartupRegistrationStore store = new() { Value = DefaultCommand, DeleteError = expectedError };
            WindowsStartupRegistration registration = CreateRegistration(store);

            Assert.Same(expectedError, Record.Exception(() => registration.SetEnabled(false)));

            Assert.True(registration.IsEnabled);
            Assert.Equal(DefaultCommand, store.Value);
            Assert.Equal(0, store.FileReadCount);
            Assert.Equal(0, store.WriteCount);
            Assert.Equal(1, store.DeleteCount);
        }
    }

    private static WindowsStartupRegistration CreateRegistration(FakeStartupRegistrationStore store, string? configurationPath = null)
        => new(ExecutablePath, configurationPath, store, store.ReadFileAttributes);

    private static Exception[] CreateRegistryErrors() =>
        [new UnauthorizedAccessException("Registry access denied."), new IOException("Registry unavailable."),
            new SecurityException("Registry access blocked.")];

    private static void AssertUnused(FakeStartupRegistrationStore store)
    {
        Assert.Equal(0, store.ReadCount);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, store.DeleteCount);
        Assert.Equal(0, store.FileReadCount);
    }

    private sealed class FakeStartupRegistrationStore : IStartupRegistrationStore
    {
        internal object? Value { get; set; }
        internal FileAttributes Attributes { get; init; } = FileAttributes.Normal;
        internal Exception? FileError { get; init; }
        internal Exception? ReadError { get; init; }
        internal Exception? WriteError { get; init; }
        internal Exception? DeleteError { get; init; }
        internal int ReadCount { get; private set; }
        internal int WriteCount { get; private set; }
        internal int DeleteCount { get; private set; }
        internal int FileReadCount { get; private set; }
        internal string? VerifiedPath { get; private set; }

        internal FileAttributes ReadFileAttributes(string path)
        {
            FileReadCount++;
            VerifiedPath = path;
            if (FileError is not null)
                throw FileError;
            return Attributes;
        }

        public object? Read()
        {
            ReadCount++;
            if (ReadError is not null)
                throw ReadError;
            return Value;
        }

        public void Write(string command)
        {
            WriteCount++;
            if (WriteError is not null)
                throw WriteError;
            Value = command;
        }

        public void Delete()
        {
            DeleteCount++;
            if (DeleteError is not null)
                throw DeleteError;
            Value = null;
        }
    }
}
