using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace YamahaRemote.Windows;

public partial class App : Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private FirstRunWindow? _startupWindow;
    private bool _activationPending;
    private PrismScreenReaderSpeech? _screenReaderSpeech;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _singleInstance = SingleInstanceCoordinator.TryCreate(QueueActivation);
            if (_singleInstance is null)
            {
                if (SingleInstanceCoordinator.RequestActivationAsync(timeout: TimeSpan.FromSeconds(6)).GetAwaiter().GetResult())
                {
                    Shutdown();
                    return;
                }
                _singleInstance = SingleInstanceCoordinator.TryCreate(QueueActivation);
                if (_singleInstance is null)
                {
                    Localization.Initialize();
                    MessageBox.Show(Localization.Text("Startup.InstanceUnavailable"),
                        Localization.Text("Startup.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown(1);
                    return;
                }
            }
            Localization.Initialize();
            string? configurationPath = ParseConfigurationPath(eventArgs.Args);
            UiPreferences configuration = UiPreferences.Load(out string? warning, configurationPath);
            Localization.SetLanguage(configuration.Language ?? "pl");
            if (warning is not null)
            {
                MessageBox.Show(warning, Localization.Text("Configuration.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            if (!configuration.IsSetupComplete)
            {
                FirstRunWindow setup = new(configuration, configurationPath);
                _startupWindow = setup;
                setup.ContentRendered += OnStartupWindowRendered;
                bool? accepted = setup.ShowDialog();
                _startupWindow = null;
                if (accepted != true || setup.CompletedConfiguration is null)
                {
                    Shutdown();
                    return;
                }
                configuration = setup.CompletedConfiguration;
            }
            Localization.SetLanguage(configuration.Language!);
            _screenReaderSpeech = new PrismScreenReaderSpeech();
            MainWindow = new MainWindow(new BluetoothSoundbarController(), preferencesPath: configurationPath,
                speakMessage: _screenReaderSpeech.TrySpeak);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
            if (_activationPending)
                ActivateExistingWindow();
        }
        catch (Exception exception)
        {
            string? logPath = WriteStartupError(exception);
            MessageBox.Show(
                StartupErrorText(exception, logPath),
                "Yamahama",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _screenReaderSpeech?.Dispose();
        _screenReaderSpeech = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(eventArgs);
    }

    private void QueueActivation()
    {
        if (Dispatcher.HasShutdownStarted)
            return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ActivateExistingWindow));
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted)
        {
        }
    }

    private void OnStartupWindowRendered(object? sender, EventArgs eventArgs)
    {
        if (_activationPending)
            ActivateExistingWindow();
    }

    private void ActivateExistingWindow()
    {
        Window? window = _startupWindow ?? MainWindow;
        if (window is null || !window.IsLoaded)
        {
            _activationPending = true;
            return;
        }
        _activationPending = false;
        WindowActivation.Restore(window);
    }

    internal static string? ParseConfigurationPath(string[] arguments)
    {
        if (arguments.Length == 0)
            return null;
        if (arguments.Length == 2 && arguments[0] == "--config" && !string.IsNullOrWhiteSpace(arguments[1]))
            return Path.GetFullPath(arguments[1]);
        throw new ArgumentException(Localization.Text("Startup.Arguments"));
    }

    private static string StartupErrorText(Exception exception, string? logPath)
    {
        try
        {
            return Localization.Text("Startup.Error", YamahaRemote.Windows.MainWindow.DescribeError(exception, false),
                logPath is null ? string.Empty : Localization.Text("Startup.Log", logPath));
        }
        catch
        {
            return "Nie można uruchomić Yamahama / Yamahama could not start.\n\n" + exception.Message
                + (logPath is null ? string.Empty : "\n\nLog: " + logPath);
        }
    }

    private static string? WriteStartupError(Exception exception)
    {
        try
        {
            string directory = AppContext.BaseDirectory;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "startup.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}\n{exception}\n\n");
            return path;
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
