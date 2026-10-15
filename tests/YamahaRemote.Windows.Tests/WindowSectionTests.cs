using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static async Task ExerciseSectionsAsync()
    {
        string previousLanguage = Localization.CurrentLanguage;
#pragma warning disable WPF0001
        ThemeMode previousTheme = Application.Current.ThemeMode;
#pragma warning restore WPF0001
        string path = ConfigurationTests.TestPath("sections-window.json");
        UiPreferences preferences = new()
        {
            ConfirmExit = false, FirstRunCompleted = true, Language = "pl", Theme = "Light", TextScale = 100,
            ShowHints = true, ShowOperationMessages = true, AnnounceStateChanges = false,
            SavedDevices = [new("Section test device (not hardware)", 1)], StartupDeviceAddress = 1,
            AutoConnectAtStartup = true, PowerOnAtStartup = false, PowerOffOnExit = true,
            KeepPoweredOn = false, StartWithWindows = false, CloseToTray = false
        };
        Assert.True(preferences.TrySave(out _, path));
        Localization.SetLanguage("pl");
        FakeSoundbarController controller = new() { InitiallyPoweredOn = true };
        FakeStartupRegistration startup = new();
        List<string> warnings = [];
        MainWindow window = new(controller, null, path, startup, confirm: (message, title) => false,
            showShutdownWarning: warnings.Add)
        {
            ShowActivated = false, ShowInTaskbar = false
        };
        bool ownerClosed = false;
        window.Closed += (_, _) => ownerClosed = true;
        Dictionary<SectionWindowCase, SectionWindow> opened = [];
        T Control<T>(string name) where T : FrameworkElement => Assert.IsAssignableFrom<T>(window.FindName(name));
        SectionWindowCase CreateCase(string pageName, string menuName, string identifier, string titleKey,
            string focusName, Func<SectionWindow> open)
        {
            TabItem page = Control<TabItem>(pageName);
            return new(page, Assert.IsAssignableFrom<FrameworkElement>(page.Content), Control<MenuItem>(menuName),
                identifier, titleKey, Assert.IsAssignableFrom<Control>(window.FindName(focusName)), open);
        }
        async Task SettleAsync()
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            foreach (Window child in window.OwnedWindows)
                child.UpdateLayout();
        }
        UiPreferences SavedPreferences()
        {
            UiPreferences saved = UiPreferences.Load(out string? warning, path);
            Assert.Null(warning);
            return saved;
        }
        async Task ExerciseMainTabNavigationAsync()
        {
            window.Activate();
            TabControl tabs = Control<TabControl>("MainTabs");
            TabItem devices = Control<TabItem>("DevicesTab");
            AutomationPeer devicesPeer = SectionPeer(tabs).GetChildren().Single(peer => peer.GetAutomationId() == devices.Name);
            Assert.IsAssignableFrom<ISelectionItemProvider>(devicesPeer.GetPattern(PatternInterface.SelectionItem)).Select();
            await SettleAsync();
            Assert.Same(devices, tabs.SelectedItem);
            AssertMainPageLayout(window);
            foreach (Label label in SectionElements(devices).OfType<Label>())
            {
                FrameworkElement target = Assert.IsAssignableFrom<FrameworkElement>(label.Target);
                Assert.Contains(target, SectionElements(devices));
                Assert.NotEmpty(SectionPeer(target).GetName());
            }
            ListBox savedDevices = Control<ListBox>("SavedDevicesList");
            Assert.True(savedDevices.IsVisible);
            Assert.True(devices.Focus());
            string focusBeforeTraversal = $"{Keyboard.FocusedElement?.GetType().Name}/{(Keyboard.FocusedElement as FrameworkElement)?.Name}";
            Assert.True(devices.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            await SettleAsync();
            List<string> navigationTrace = [];
            for (DependencyObject? element = savedDevices.ItemContainerGenerator.ContainerFromIndex(0); element is not null; element = VisualTreeHelper.GetParent(element))
            {
                if (element is FrameworkElement navigationElement)
                    navigationTrace.Add($"{navigationElement.GetType().Name}/{navigationElement.Name}: visible={navigationElement.IsVisible}, enabled={navigationElement.IsEnabled}, focusable={navigationElement.Focusable}, stop={KeyboardNavigation.GetIsTabStop(navigationElement)}, navigation={KeyboardNavigation.GetTabNavigation(navigationElement)}, index={KeyboardNavigation.GetTabIndex(navigationElement)}");
            }
            Assert.True(savedDevices.IsKeyboardFocusWithin,
                $"Expected focus in the saved list, got {Keyboard.FocusedElement?.GetType().Name}/{(Keyboard.FocusedElement as FrameworkElement)?.Name}; before traversal {focusBeforeTraversal}; header focused {devices.IsKeyboardFocused}; owner active {window.IsActive}; list enabled {savedDevices.IsEnabled}; items {savedDevices.Items.Count}; selected index {savedDevices.SelectedIndex}. {string.Join("; ", navigationTrace)}");
            if (savedDevices.HasItems)
                Assert.True(Assert.IsType<ListBoxItem>(Keyboard.FocusedElement).IsSelected);
            else
                Assert.Same(savedDevices, Keyboard.FocusedElement);
            Button[] enabledActions = SectionElements(devices).OfType<Button>().Where(button => button.IsEnabled).ToArray();
            Assert.NotEmpty(enabledActions);
            Assert.True(enabledActions[0].Focus());
            await SettleAsync();
            for (int index = 1; index < enabledActions.Length; index++)
            {
                Assert.True(enabledActions[index - 1].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                await SettleAsync();
                Assert.True(enabledActions[index].IsKeyboardFocusWithin,
                    $"Expected device action focus in {enabledActions[index].Name}.");
            }
            TextBox status = Control<TextBox>("StatusMessage");
            Assert.True(enabledActions[^1].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                $"Cannot leave {enabledActions[^1].Name}; focus={(Keyboard.FocusedElement as FrameworkElement)?.Name}; active={window.IsActive}; status visible={status.IsVisible}, enabled={status.IsEnabled}, focusable={status.Focusable}, tabstop={status.IsTabStop}; scale={window.FontSize}.");
            await SettleAsync();
            Assert.True(Control<TextBox>("StatusMessage").IsKeyboardFocusWithin);
            TextBlock summary = Control<TextBlock>("ConnectionSummary");
            Assert.True(summary.IsVisible);
            Assert.True(summary.Focus());
            TabItem control = Control<TabItem>("ControlTab");
            AutomationPeer controlPeer = SectionPeer(tabs).GetChildren().Single(peer => peer.GetAutomationId() == control.Name);
            Assert.IsAssignableFrom<ISelectionItemProvider>(controlPeer.GetPattern(PatternInterface.SelectionItem)).Select();
            await SettleAsync();
            Assert.Same(control, tabs.SelectedItem);
            Assert.False(savedDevices.IsVisible);
            Assert.True(summary.IsVisible);
            Assert.True(Control<TextBlock>("ReadoutStatus").Focus());
            Assert.True(Control<TextBlock>("ReadoutStatus").IsKeyboardFocused);
        }
        async Task ExerciseStartupNavigationAsync()
        {
            SectionWindow settings = window.OpenSettingsWindow();
            settings.Activate();
            await SettleAsync();
            SelectSettingsTab(window, "StartupSettingsTab");
            string[] order =
            [
                "StartWithWindowsCheckBox", "StartupDeviceChoice", "AutoConnectAtStartupCheckBox",
                "PowerOnAtStartupCheckBox", "KeepPoweredOnCheckBox"
            ];
            Assert.True(Control<Control>(order[0]).Focus());
            for (int index = 1; index < order.Length; index++)
            {
                Assert.True(Control<Control>(order[index - 1]).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.True(Control<Control>(order[index]).IsKeyboardFocusWithin,
                    $"Expected startup/power focus in {order[index]}.");
            }
        }
        void AssertSessionAlive(bool visible = true)
        {
            Assert.False(ownerClosed);
            Assert.Equal(visible, window.IsVisible);
            Assert.False(controller.Disposed);
            Assert.True(controller.IsConnected);
            Assert.True(controller.State!.Power);
            Assert.Empty(controller.Commands);
            Assert.DoesNotContain("Disconnect", controller.Operations);
            Assert.DoesNotContain("Dispose", controller.Operations);
            Assert.Equal(new ulong[] { 1 }, controller.ConnectionAddresses);
            Assert.True(SavedPreferences().PowerOffOnExit);
            Assert.False(startup.IsEnabled);
            Assert.Empty(warnings);
            TabControl tabs = Control<TabControl>("MainTabs");
            AssertMainPageLayout(window);
            Assert.Same(Control<TabItem>("ControlTab"), tabs.SelectedItem);
        }
        try
        {
            window.Show();
            await WaitForAsync(() => controller.IsConnected && !Control<Button>("CancelButton").IsEnabled);
            await SettleAsync();
            AssertSessionAlive();
            await ExerciseMainTabNavigationAsync();
            AssertSessionAlive();
            MenuItem fileMenu = Control<MenuItem>("FileMenu");
            MenuItem helpMenu = Control<MenuItem>("HelpMenu");
            Menu menu = Assert.IsType<Menu>(fileMenu.Parent);
            Assert.Collection(menu.Items.Cast<MenuItem>(), item => Assert.Same(fileMenu, item),
                item => Assert.Same(helpMenu, item));
            Assert.Null(window.FindName("ThemeMenu"));
            Assert.Null(window.FindName("ViewMenu"));
            Assert.Contains(Control<MenuItem>("SettingsMenuItem"), fileMenu.Items.Cast<object>());
            foreach (string name in new[] { "SoundbarHelpMenuItem", "InformationMenuItem", "FirmwareMenuItem" })
                Assert.Contains(Control<MenuItem>(name), helpMenu.Items.Cast<object>());

            SectionWindowCase[] cases =
            [
                CreateCase("SettingsTab", "SettingsMenuItem", "SettingsWindow", "Window.SettingsTitle",
                    "SettingsLanguageChoice", window.OpenSettingsWindow),
                CreateCase("HelpTab", "SoundbarHelpMenuItem", "HelpWindow", "Window.HelpTitle",
                    "HelpReadout", window.OpenHelpWindow),
                CreateCase("FirmwareHelpTab", "FirmwareMenuItem", "FirmwareWindow", "Window.FirmwareTitle",
                    "FirmwareDeviceReadout", window.OpenFirmwareWindow),
                CreateCase("InformationTab", "InformationMenuItem", "InformationWindow", "Window.InformationTitle",
                    "InformationStatus", window.OpenInformationWindow)
            ];
            foreach (SectionWindowCase sectionCase in cases)
            {
                Assert.DoesNotContain(sectionCase.Page, Control<TabControl>("MainTabs").Items.Cast<object>());
                Assert.True(sectionCase.Menu.IsEnabled);
                Assert.NotEmpty(SectionPeer(sectionCase.Menu).GetName());
                sectionCase.Menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await SettleAsync();
                SectionWindow section = Assert.IsType<SectionWindow>(Assert.Single(window.OwnedWindows.Cast<Window>(),
                    child => AutomationProperties.GetAutomationId(child) == sectionCase.Identifier));
                opened.Add(sectionCase, section);
                AssertSectionAttached(window, sectionCase, section);
                Assert.True(sectionCase.InitialFocus.IsKeyboardFocusWithin,
                    $"Expected initial keyboard focus in {sectionCase.InitialFocus.Name}.");
                Assert.Same(section, sectionCase.Open());
                section.WindowState = WindowState.Minimized;
                Assert.Same(section, sectionCase.Open());
                await SettleAsync();
                Assert.Equal(WindowState.Normal, section.WindowState);
                Assert.Equal(opened.Count, window.OwnedWindows.Count);
                AssertSectionAttached(window, sectionCase, section);
                AssertSessionAlive();
            }

            await ExerciseStartupNavigationAsync();
            AssertSessionAlive();
            ComboBox languages = Control<ComboBox>("SettingsLanguageChoice");
            foreach (string language in new[] { "en", "pl", "en" })
            {
                languages.SelectedItem = languages.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == language);
                Assert.True(window.CommitSettings());
                await SettleAsync();
                Assert.Equal(language, Localization.CurrentLanguage);
                Assert.Equal(language, SavedPreferences().Language);
                Assert.Equal(Localization.Text("Menu.File"), fileMenu.Header);
                Assert.Equal(Localization.Text("Menu.Help"), helpMenu.Header);
                Assert.Equal(Localization.Text("Help.Overview"), Control<TextBox>("HelpReadout").Text);
                Assert.Contains("SR-C30A", Control<TextBox>("ModelReadout").Text);
                Assert.Contains("1.23", Control<TextBox>("FirmwareReadout").Text);
                foreach ((string name, string key) in new[]
                {
                    ("SettingsLanguageChoice", "Settings.Language"), ("SettingsThemeChoice", "Settings.Theme"),
                    ("SettingsTextScaleChoice", "Settings.TextScale"), ("ShowHintsCheckBox", "Settings.ShowHintsName"),
                    ("HelpReadout", "Help.Name"), ("FirmwareDeviceReadout", "Guide.FirmwareDeviceName"),
                    ("InformationStatus", "Information.StatusName"), ("SettingsMenuItem", "Tabs.SettingsName"),
                    ("SoundbarHelpMenuItem", "Tabs.HelpName"), ("FirmwareMenuItem", "Guide.FirmwareTabName"),
                    ("InformationMenuItem", "Tabs.InformationName"), ("ControlTab", "Tabs.ControlName"),
                    ("DevicesTab", "Tabs.DevicesName"), ("StartupDeviceChoice", "Startup.Device"),
                    ("StartWithWindowsCheckBox", "Startup.Windows"), ("AutoConnectAtStartupCheckBox", "Startup.AutoConnect"),
                    ("PowerOnAtStartupCheckBox", "Startup.PowerOn"), ("PowerOffOnExitCheckBox", "Shutdown.PowerOffOption"),
                    ("KeepPoweredOnCheckBox", "PowerProtection.Enable")
                })
                    Assert.Equal(Localization.Text(key), SectionPeer(Control<FrameworkElement>(name)).GetName());
                foreach (SectionWindowCase sectionCase in cases)
                    AssertSectionAttached(window, sectionCase, opened[sectionCase]);
                await ExerciseMainTabNavigationAsync();
                await ExerciseStartupNavigationAsync();
                AssertSessionAlive();
            }

            ComboBox themes = Control<ComboBox>("SettingsThemeChoice");
            Dictionary<string, Color[]> themeForegrounds = [];
#pragma warning disable WPF0001
            foreach ((string themeName, ThemeMode theme) in new[]
            {
                ("Light", ThemeMode.Light), ("Dark", ThemeMode.Dark),
                ("System", ThemeMode.System), ("Light", ThemeMode.Light)
            })
            {
                themes.SelectedItem = themes.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == themeName);
                Assert.True(window.CommitSettings());
                await SettleAsync();
                Assert.Equal(themeName, SavedPreferences().Theme);
                Assert.Equal(theme, Application.Current.ThemeMode);
                Color[] colors = new[] { window }.Cast<Window>().Concat(opened.Values)
                    .Select(current => Assert.IsType<SolidColorBrush>(current.Foreground).Color).ToArray();
                if (themeForegrounds.TryGetValue(themeName, out Color[]? previous))
                    Assert.Equal(previous, colors);
                themeForegrounds[themeName] = colors;
                foreach (SectionWindowCase sectionCase in cases)
                {
                    SectionWindow section = opened[sectionCase];
                    TextBox status = Assert.IsType<TextBox>(section.FindName("SectionStatusMessage"));
                    Assert.Equal(Assert.IsType<SolidColorBrush>(section.Background).Color,
                        Assert.IsType<SolidColorBrush>(status.Background).Color);
                    foreach (Label label in SectionElements(sectionCase.Content).OfType<Label>())
                        Assert.Equal(Assert.IsType<SolidColorBrush>(section.Foreground).Color,
                            Assert.IsType<SolidColorBrush>(label.Foreground).Color);
                    foreach (TextBox readout in SectionElements(sectionCase.Content).OfType<TextBox>())
                    {
                        Assert.Equal(Assert.IsType<SolidColorBrush>(section.Foreground).Color,
                            Assert.IsType<SolidColorBrush>(readout.Foreground).Color);
                        Assert.Equal(Assert.IsType<SolidColorBrush>(section.Background).Color,
                            Assert.IsType<SolidColorBrush>(readout.Background).Color);
                    }
                    AssertSectionAttached(window, sectionCase, section);
                }
            }
#pragma warning restore WPF0001
            if (!SystemParameters.HighContrast)
            {
                for (int index = 0; index < themeForegrounds["Light"].Length; index++)
                    Assert.NotEqual(themeForegrounds["Light"][index], themeForegrounds["Dark"][index]);
            }

            foreach (int scale in new[] { 200, 125, 100, 150 })
            {
                Control<ComboBox>("SettingsTextScaleChoice").SelectedValue = scale;
                Assert.True(window.CommitSettings());
                await SettleAsync();
                Assert.Equal(scale, SavedPreferences().TextScale);
                double expectedSize = SystemFonts.MessageFontSize * scale / 100.0;
                Assert.Equal(expectedSize, window.FontSize);
                Assert.Equal(expectedSize, Control<Button>("SearchButton").FontSize);
                foreach (SectionWindowCase sectionCase in cases)
                {
                    SectionWindow section = opened[sectionCase];
                    Assert.Equal(expectedSize, section.FontSize);
                    Assert.Equal(expectedSize, sectionCase.InitialFocus.FontSize);
                    Assert.Equal(expectedSize, Assert.IsType<Button>(section.FindName(sectionCase.Identifier == "SettingsWindow" ? "SettingsCancelButton" : "SectionCloseButton")).FontSize);
                }
                await ExerciseMainTabNavigationAsync();
                await ExerciseStartupNavigationAsync();
                AssertSessionAlive();
            }

            CheckBox hints = Control<CheckBox>("ShowHintsCheckBox");
            IToggleProvider hintsToggle = Assert.IsAssignableFrom<IToggleProvider>(SectionPeer(hints).GetPattern(PatternInterface.Toggle));
            foreach (bool enabled in new[] { false, true, false })
            {
                hintsToggle.Toggle();
                Assert.True(window.CommitSettings());
                await SettleAsync();
                Assert.Equal(enabled, hints.IsChecked);
                Assert.Equal(enabled, SavedPreferences().ShowHints);
                Assert.Equal(enabled, UsageHints.GetEnabled(window));
                foreach (string name in new[]
                {
                    "SearchButton", "SavedDevicesList", "DevicesList", "StartupDeviceChoice", "AutoConnectAtStartupCheckBox",
                    "StartWithWindowsCheckBox", "PowerOffOnExitCheckBox", "KeepPoweredOnCheckBox",
                    "ShowHintsCheckBox", "HelpReadout", "FirmwareGuideReadout", "LastResponseReadout"
                })
                {
                    FrameworkElement element = Control<FrameworkElement>(name);
                    Assert.NotEmpty(UsageHints.GetText(element));
                    Assert.Equal(enabled, UsageHints.GetEnabled(element));
                    Assert.Equal(enabled ? UsageHints.GetText(element) : string.Empty, SectionPeer(element).GetHelpText());
                    Assert.NotEmpty(SectionPeer(element).GetName());
                }
                Style hintStyle = Assert.IsType<Style>(window.FindResource("UsageHintStyle"));
                TextBlock[] visualHints = opened.Values.SelectMany(SectionElements).OfType<TextBlock>()
                    .Where(element => ReferenceEquals(element.Style, hintStyle)).ToArray();
                Assert.NotEmpty(visualHints);
                Assert.All(visualHints, element => Assert.Equal(enabled ? Visibility.Visible : Visibility.Collapsed, element.Visibility));
                foreach (SectionWindowCase sectionCase in cases)
                    AssertSectionAttached(window, sectionCase, opened[sectionCase]);
                AssertSessionAlive();
            }

            foreach (string closeMethod in new[] { "close", "button", "escape" })
            {
                foreach (SectionWindowCase sectionCase in cases)
                {
                    SectionWindow section = opened[sectionCase];
                    if (closeMethod == "close")
                        section.Close();
                    else
                    {
                        section.Activate();
                        Button close = Assert.IsType<Button>(section.FindName(sectionCase.Identifier == "SettingsWindow" ? "SettingsCancelButton" : "SectionCloseButton"));
                        if (closeMethod == "button")
                            Assert.IsAssignableFrom<IInvokeProvider>(SectionPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
                        else
                        {
                            Assert.True(close.Focus());
                            PresentationSource source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(section));
                            KeyEventArgs escape = new(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                            {
                                RoutedEvent = Keyboard.KeyDownEvent
                            };
                            close.RaiseEvent(escape);
                            Assert.True(escape.Handled);
                        }
                    }
                    await SettleAsync();
                    AssertSectionDetached(window, sectionCase, section);
                    Assert.Equal(cases.Length - 1, window.OwnedWindows.Count);
                    AssertSessionAlive();
                    foreach (SectionWindowCase other in cases.Where(other => other != sectionCase))
                        AssertSectionAttached(window, other, opened[other]);
                    SectionWindow reopened = sectionCase.Open();
                    opened[sectionCase] = reopened;
                    await SettleAsync();
                    Assert.NotSame(section, reopened);
                    Assert.Same(reopened, sectionCase.Open());
                    AssertSectionAttached(window, sectionCase, reopened);
                    Assert.Equal(150, Control<ComboBox>("SettingsTextScaleChoice").SelectedValue);
                    Assert.Equal(window.FontSize, reopened.FontSize);
                    Assert.Equal(window.FontSize, sectionCase.InitialFocus.FontSize);
                    Assert.False(UsageHints.GetEnabled(reopened));
                    Assert.Equal(cases.Length, window.OwnedWindows.Count);
                    AssertSessionAlive();
                }
            }

            Control<CheckBox>("CloseToTrayCheckBox").IsChecked = true;
            Assert.True(window.CommitSettings());
            Assert.True(SavedPreferences().CloseToTray);
            TrayController tray = Assert.IsType<TrayController>(window.Tray);
            Assert.True(tray.Visible);
            Assert.True(window.HideForCloseShortcut());
            await SettleAsync();
            AssertSessionAlive(visible: false);
            Assert.True(tray.Visible);
            Assert.Empty(window.OwnedWindows.Cast<Window>());
            foreach (SectionWindowCase sectionCase in cases)
                AssertSectionDetached(window, sectionCase, opened[sectionCase]);
            window.RestoreFromTray();
            await SettleAsync();
            AssertSessionAlive();
            foreach (SectionWindowCase sectionCase in cases)
            {
                SectionWindow reopened = sectionCase.Open();
                Assert.NotSame(opened[sectionCase], reopened);
                opened[sectionCase] = reopened;
                await SettleAsync();
                AssertSectionAttached(window, sectionCase, reopened);
                Assert.Equal(window.FontSize, reopened.FontSize);
                Assert.False(UsageHints.GetEnabled(reopened));
            }
            AssertSessionAlive();
            window.RequestExit();
            await WaitForAsync(() => controller.Disposed && ownerClosed);
            Assert.Equal(new[] { SoundbarCommand.PowerOff }, controller.Commands);
            Assert.Equal(new[] { "Disconnect", "Dispose" }, controller.Operations.TakeLast(2));
            Assert.Empty(warnings);
            Assert.Null(window.Tray);
            Assert.Empty(window.OwnedWindows.Cast<Window>());
            foreach (SectionWindowCase sectionCase in cases)
                AssertSectionDetached(window, sectionCase, opened[sectionCase]);
        }
        finally
        {
            try
            {
                if (!ownerClosed)
                    window.RequestExit();
                await WaitForAsync(() => controller.Disposed && ownerClosed);
            }
            finally
            {
                Localization.SetLanguage(previousLanguage);
#pragma warning disable WPF0001
                Application.Current.ThemeMode = previousTheme;
#pragma warning restore WPF0001
            }
        }
    }

    private static void AssertMainPageLayout(MainWindow owner)
    {
        TabControl tabs = Assert.IsType<TabControl>(owner.FindName("MainTabs"));
        TabItem controlPage = Assert.IsType<TabItem>(owner.FindName("ControlTab"));
        TabItem devicesPage = Assert.IsType<TabItem>(owner.FindName("DevicesTab"));
        Assert.Collection(tabs.Items.Cast<object>(), item => Assert.Same(controlPage, item), item => Assert.Same(devicesPage, item));
        foreach ((TabItem page, string headerKey, string nameKey) in new[]
        {
            (controlPage, "Tabs.Control", "Tabs.ControlName"), (devicesPage, "Tabs.Devices", "Tabs.DevicesName")
        })
        {
            Assert.Same(tabs, page.Parent);
            Assert.Same(owner, Window.GetWindow(page));
            Assert.Equal(Localization.Text(headerKey), page.Header);
            Assert.Equal(Localization.Text(nameKey), SectionPeer(page).GetName());
            Assert.True(page.Focusable);
            Assert.True(page.IsTabStop);
        }
        string[] deviceOrder =
        [
            "SavedDevicesList", "DevicesList", "SearchButton", "ConnectButton", "ConnectOnceButton", "AddDeviceButton",
            "RemoveSavedDeviceButton", "DisconnectButton", "ForgetButton"
        ];
        FrameworkElement[] deviceElements = SectionElements(devicesPage).ToArray();
        FrameworkElement[] controlElements = SectionElements(controlPage).ToArray();
        Assert.Equal(deviceOrder, deviceElements.Where(element => element is ListBox or Button).Select(element => element.Name));
        Button connect = Assert.IsType<Button>(owner.FindName("ConnectButton"));
        Button connectOnce = Assert.IsType<Button>(owner.FindName("ConnectOnceButton"));
        Binding connectOnceEnabled = Assert.IsType<Binding>(BindingOperations.GetBinding(connectOnce, UIElement.IsEnabledProperty));
        Assert.Equal("ConnectButton", connectOnceEnabled.ElementName);
        Assert.Equal("IsEnabled", connectOnceEnabled.Path.Path);
        Assert.Equal(connect.IsEnabled, connectOnce.IsEnabled);
        Assert.Equal(Localization.Text("Device.ConnectOnce"), connectOnce.Content);
        Assert.Equal(Localization.Text("Device.ConnectOnce"), SectionPeer(connectOnce).GetName());
        foreach (string name in deviceOrder.Append("EmptySavedMessage"))
        {
            FrameworkElement element = Assert.IsAssignableFrom<FrameworkElement>(owner.FindName(name));
            Assert.Contains(element, deviceElements);
            Assert.DoesNotContain(element, controlElements);
            Assert.Same(owner, Window.GetWindow(element));
            if (element is Control deviceControl)
            {
                Assert.True(deviceControl.Focusable);
                Assert.Equal(deviceControl is not ListBox list || !list.HasItems, deviceControl.IsTabStop);
                Assert.NotEmpty(SectionPeer(deviceControl).GetName());
            }
        }
        foreach (string name in new[] { "ReadoutStatus", "PowerToggle", "InputChoice", "VolumeSlider", "SubwooferSlider" })
        {
            FrameworkElement element = Assert.IsAssignableFrom<FrameworkElement>(owner.FindName(name));
            Assert.Contains(element, controlElements);
            Assert.DoesNotContain(element, deviceElements);
        }
        TextBlock summary = Assert.IsType<TextBlock>(owner.FindName("ConnectionSummary"));
        Assert.Same(tabs.Parent, summary.Parent);
        Assert.True(summary.Focusable);
        Assert.True(KeyboardNavigation.GetIsTabStop(summary));
        Assert.True(KeyboardNavigation.GetTabIndex(summary) < KeyboardNavigation.GetTabIndex(tabs));
        foreach (string name in new[] { "ConnectionSummary", "StatusMessage", "RefreshButton", "CancelButton" })
        {
            FrameworkElement element = Assert.IsAssignableFrom<FrameworkElement>(owner.FindName(name));
            Assert.DoesNotContain(element, deviceElements);
            Assert.DoesNotContain(element, controlElements);
            Assert.Same(owner, Window.GetWindow(element));
        }
        ScrollViewer scroll = Assert.IsType<ScrollViewer>(devicesPage.Content);
        Assert.False(scroll.Focusable);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(scroll));
    }

    private static void AssertStartupSettingsLayout(MainWindow owner, SectionWindow section)
    {
        TabControl settingsTabs = Assert.IsType<TabControl>(owner.FindName("SettingsTabs"));
        string[] tabNames = ["AppearanceSettingsTab", "MainScreenSettingsTab", "ControlsSettingsTab", "StartupSettingsTab", "ClosingSettingsTab", "ProfilesSettingsTab"];
        Assert.Equal(tabNames, settingsTabs.Items.Cast<TabItem>().Select(tab => tab.Name));
        Assert.All(settingsTabs.Items.Cast<TabItem>(), tab => Assert.NotEmpty(SectionPeer(tab).GetName()));
        foreach (string notice in new[] { "StartupSafetyNotice", "PowerProtectionSafetyNotice" })
        {
            TextBox readout = Assert.IsType<TextBox>(owner.FindName(notice));
            Assert.DoesNotContain(readout, SectionElements(section));
            Assert.Contains(readout, SectionElements(Assert.IsType<TabItem>(owner.FindName("HelpTab")).Content as DependencyObject
                ?? Assert.IsType<SectionWindow>(owner.OwnedWindows.Cast<Window>().Single(child => AutomationProperties.GetAutomationId(child) == "HelpWindow"))));
            Assert.True(readout.IsReadOnly && readout.Focusable);
            Assert.NotEmpty(SectionPeer(readout).GetName());
        }
        GroupBox startup = Assert.IsType<GroupBox>(owner.FindName("StartupSettingsGroup"));
        GroupBox power = Assert.IsType<GroupBox>(owner.FindName("PowerSettingsGroup"));
        Assert.Equal(Localization.Text("Startup.Heading"), startup.Header);
        Assert.Equal(Localization.Text("Settings.PowerHeading"), power.Header);
        StackPanel parent = Assert.IsType<StackPanel>(startup.Parent);
        Assert.Same(parent, power.Parent);
        Assert.Equal(parent.Children.IndexOf(startup) + 1, parent.Children.IndexOf(power));
        foreach ((GroupBox group, string[] expectedOrder) in new[]
        {
            (startup, new[] { "StartWithWindowsCheckBox", "StartupDeviceChoice", "AutoConnectAtStartupCheckBox", "PowerOnAtStartupCheckBox" }),
            (power, new[] { "KeepPoweredOnCheckBox" })
        })
        {
            Assert.Same(section, Window.GetWindow(group));
            StackPanel content = Assert.IsType<StackPanel>(group.Content);
            Control[] controls = content.Children.OfType<Control>().Where(element => !string.IsNullOrEmpty(element.Name)).ToArray();
            Assert.Equal(expectedOrder, controls.Select(element => element.Name));
            Assert.All(controls, element =>
            {
                Assert.Same(section, Window.GetWindow(element));
                Assert.True(element.Focusable);
                Assert.True(element.IsTabStop);
                Assert.NotEmpty(SectionPeer(element).GetName());
            });
        }
        foreach (string name in new[] { "SavedDevicesList", "DevicesList", "ConnectButton", "ConnectOnceButton", "DisconnectButton", "ConnectionSummary" })
            Assert.DoesNotContain(Assert.IsAssignableFrom<FrameworkElement>(owner.FindName(name)), SectionElements(section));
    }

    private static void AssertSectionAttached(MainWindow owner, SectionWindowCase sectionCase, SectionWindow section)
    {
        Assert.True(section.IsVisible);
        Assert.True(section.IsLoaded);
        Assert.False(section.ShowInTaskbar);
        Assert.Same(owner, section.Owner);
        Assert.Contains(section, owner.OwnedWindows.Cast<Window>());
        Assert.Null(sectionCase.Page.Content);
        Assert.Same(sectionCase.Content, Assert.IsType<ContentControl>(section.FindName("SectionContent")).Content);
        Assert.Same(section, Window.GetWindow(sectionCase.InitialFocus));
        Assert.Equal(sectionCase.Identifier, AutomationProperties.GetAutomationId(section));
        Assert.Equal(Localization.Text(sectionCase.TitleKey), section.Title);
        Assert.Equal(owner.Language, section.Language);
        Assert.Equal(owner.Language, sectionCase.InitialFocus.Language);
        Assert.Equal(section.Title, SectionPeer(section).GetName());
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(section));
        Assert.True(sectionCase.InitialFocus.Focusable);
        Assert.True(KeyboardNavigation.GetIsTabStop(sectionCase.InitialFocus));
        Button close = Assert.IsType<Button>(section.FindName(sectionCase.Identifier == "SettingsWindow" ? "SettingsCancelButton" : "SectionCloseButton"));
        Assert.True(close.IsEnabled);
        Assert.True(close.Focusable);
        Assert.True(close.IsTabStop);
        if (sectionCase.Identifier == "SettingsWindow")
        {
            Assert.Equal(Visibility.Visible, close.Visibility);
            Assert.NotEmpty(SectionPeer(close).GetName());
            Button accept = Assert.IsType<Button>(section.FindName("SettingsOkButton"));
            Assert.True(accept.IsDefault && accept.IsTabStop && accept.Focusable);
            Assert.Equal(Visibility.Visible, accept.Visibility);
            Assert.NotEmpty(SectionPeer(accept).GetName());
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(section.FindName("SectionCloseButton")).Visibility);
        }
        else
        {
            Assert.Equal(Localization.Text("Window.Close"), close.Content);
            Assert.Equal(Localization.Text("Window.CloseName"), SectionPeer(close).GetName());
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(section.FindName("SettingsOkButton")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(section.FindName("SettingsCancelButton")).Visibility);
        }
        TextBox status = Assert.IsType<TextBox>(section.FindName("SectionStatusMessage"));
        Assert.Equal(Assert.IsType<TextBox>(owner.FindName("StatusMessage")).Text, status.Text);
        Assert.Equal(Visibility.Visible, status.Visibility);
        Assert.True(status.IsReadOnly);
        Assert.Equal(Localization.Text("Status.MessagePrefix") + status.Text, SectionPeer(status).GetName());
        if (sectionCase.Identifier == "SettingsWindow")
            AssertStartupSettingsLayout(owner, section);
        Label[] labels = SectionElements(sectionCase.Content).OfType<Label>().ToArray();
        Assert.NotEmpty(labels);
        TabControl? settingsTabs = sectionCase.Identifier == "SettingsWindow"
            ? Assert.IsType<TabControl>(owner.FindName("SettingsTabs")) : null;
        object? selectedTab = settingsTabs?.SelectedItem;
        IInputElement? previousFocus = Keyboard.FocusedElement;
        foreach (Label label in labels)
        {
            FrameworkElement target = Assert.IsAssignableFrom<FrameworkElement>(label.Target);
            if (settingsTabs is not null)
            {
                settingsTabs.SelectedItem = settingsTabs.Items.Cast<TabItem>().Single(tab => SectionElements(tab).Contains(target));
                section.UpdateLayout();
            }
            Assert.Same(section, Window.GetWindow(target));
            Assert.NotEmpty(Assert.IsType<string>(label.Content));
            Assert.Same(label, AutomationProperties.GetLabeledBy(target));
            AutomationPeer labelPeer = SectionPeer(label);
            AutomationPeer peer = SectionPeer(target);
            Assert.NotEmpty(peer.GetName());
            Assert.Same(labelPeer, peer.GetLabeledBy());
            Assert.Same(label, Assert.IsAssignableFrom<FrameworkElementAutomationPeer>(peer.GetLabeledBy()).Owner);
        }
        if (settingsTabs is not null)
        {
            settingsTabs.SelectedItem = selectedTab;
            section.UpdateLayout();
            if (previousFocus is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } focusTarget)
                focusTarget.Focus();
        }
    }

    private static void AssertSectionDetached(MainWindow owner, SectionWindowCase sectionCase, SectionWindow section)
    {
        Assert.False(section.IsVisible);
        Assert.DoesNotContain(section, owner.OwnedWindows.Cast<Window>());
        Assert.Null(Assert.IsType<ContentControl>(section.FindName("SectionContent")).Content);
        Assert.Same(sectionCase.Content, sectionCase.Page.Content);
    }

    private static AutomationPeer SectionPeer(UIElement element) => Assert.IsAssignableFrom<AutomationPeer>(
        UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element));

    private static IEnumerable<FrameworkElement> SectionElements(DependencyObject root)
    {
        if (root is FrameworkElement element)
            yield return element;
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (FrameworkElement descendant in SectionElements(child))
                yield return descendant;
        }
    }

    private sealed record SectionWindowCase(TabItem Page, FrameworkElement Content, MenuItem Menu,
        string Identifier, string TitleKey, Control InitialFocus, Func<SectionWindow> Open);
}
