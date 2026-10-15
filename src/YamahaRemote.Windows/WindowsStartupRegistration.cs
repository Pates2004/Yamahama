using System.IO;
using Microsoft.Win32;

namespace YamahaRemote.Windows;

internal interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

internal interface IStartupRegistrationStore
{
    object? Read();
    void Write(string command);
    void Delete();
}

internal sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Yamahama";
    private const int MaximumCommandLength = 260;
    private readonly IStartupRegistrationStore _store;
    private readonly Func<string, FileAttributes> _readFileAttributes;
    private readonly string _executablePath;
    private readonly string _command;

    internal WindowsStartupRegistration(string? executablePath = null, string? configurationPath = null)
        : this(executablePath, configurationPath, new RegistryStartupRegistrationStore())
    {
    }

    internal WindowsStartupRegistration(string? executablePath, string? configurationPath,
        IStartupRegistrationStore store, Func<string, FileAttributes>? readFileAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _readFileAttributes = readFileAttributes ?? File.GetAttributes;
        _executablePath = NormalizeFilePath(
            executablePath ?? Path.Combine(AppContext.BaseDirectory, "Yamahama.exe"), nameof(executablePath));
        if (!string.Equals(Path.GetExtension(_executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The startup executable must be an .exe apphost file.", nameof(executablePath));

        _command = $"\"{_executablePath}\"";
        if (configurationPath is not null)
            _command += $" --config \"{NormalizeFilePath(configurationPath, nameof(configurationPath))}\"";
    }

    public bool IsEnabled => Matches(_store.Read());

    public void SetEnabled(bool enabled)
    {
        if (enabled && _command.Length > MaximumCommandLength)
            throw new ArgumentException($"The Windows Run command cannot exceed {MaximumCommandLength} characters, including quotes and arguments.");
        object? existingValue = _store.Read();
        if (existingValue is not null && !Matches(existingValue))
            throw new InvalidOperationException(
                $"Cannot change startup registration: HKCU\\{RunKeyPath} value '{ValueName}' contains a different command or a non-string value. "
                + $"Expected command: {_command}. The existing entry was left unchanged; manage the other installation's registration separately.");

        if (enabled)
        {
            if ((_readFileAttributes(_executablePath) & FileAttributes.Directory) != 0)
                throw new IOException($"The startup executable '{_executablePath}' must be a file, not a directory.");
            if (existingValue is null)
                _store.Write(_command);
        }
        else if (existingValue is not null)
        {
            _store.Delete();
        }
    }

    private bool Matches(object? value) => value is string command
        && string.Equals(command, _command, StringComparison.Ordinal);

    private static string NormalizeFilePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Any(character => char.IsControl(character)
                || character is '"' or '<' or '>' or '|' or '*' or '?' or '\u2028' or '\u2029'))
            throw new ArgumentException("A startup path must be non-empty and contain no quotes, control characters or invalid filename characters.", parameterName);

        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)!;
        if (fullPath.StartsWith(@"\\.\", StringComparison.Ordinal)
            || fullPath.Length == root.Length
            || Path.EndsInDirectorySeparator(fullPath)
            || fullPath[root.Length..].Contains(':'))
            throw new ArgumentException("A startup path must name a regular file, not a directory, device or alternate data stream.", parameterName);

        return fullPath;
    }

    private sealed class RegistryStartupRegistrationStore : IStartupRegistrationStore
    {
        public object? Read()
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return runKey?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public void Write(string command)
        {
            using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new IOException($"Could not open HKCU\\{RunKeyPath} for startup registration.");
            runKey.SetValue(ValueName, command, RegistryValueKind.String);
        }

        public void Delete()
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
