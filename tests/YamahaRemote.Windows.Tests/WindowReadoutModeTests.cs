using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;
using YamahaRemote.Core;
using YamahaRemote.Windows;

namespace YamahaRemote.Windows.Tests;

public sealed partial class WindowFlowTests
{
    private static readonly ValueDisplayMode[] ReadoutModes =
    [
        ValueDisplayMode.ReadOnlyFields, ValueDisplayMode.StaticText, ValueDisplayMode.None
    ];

    private static async Task ExerciseReadoutModesAsync()
    {
        string previousLanguage = Localization.CurrentLanguage;
#pragma warning disable WPF0001
        ThemeMode previousTheme = Application.Current.ThemeMode;
#pragma warning restore WPF0001
        try
        {
            Assert.Equal(ValueDisplayMode.StaticText, new UiPreferences().ValueDisplayMode);
            foreach (string language in new[] { "pl", "en" })
            {
                Localization.SetLanguage(language);
                await ExerciseReadoutModesLanguageAsync(language);
                ExerciseFirstRunReadoutModes(language);
            }
        }
        finally
        {
            Localization.SetLanguage(previousLanguage);
#pragma warning disable WPF0001
            Application.Current.ThemeMode = previousTheme;
#pragma warning restore WPF0001
        }
    }

    private static async Task ExerciseReadoutModesLanguageAsync(string language)
    {
        string path = ConfigurationTests.TestPath("readout-modes-" + language + ".json");
        UiPreferences initial = new()
        {
            ConfirmExit = false, FirstRunCompleted = true, Language = language, Theme = "Dark",
            SavedDevices = [new("Readout test device (not hardware)", 1)],
            StartupDeviceAddress = 1, AutoConnectAtStartup = true,
            VolumeControlMode = LevelControlMode.Buttons, SubwooferControlMode = LevelControlMode.Slider,
            ShowOperationMessages = false, AnnounceStateChanges = true
        };
        Assert.True(initial.TrySave(out _, path));
        FakeSoundbarController controller = new();
        List<string> announcements = [];
        MainWindow CreateWindow() => new(controller, null, path, new FakeStartupRegistration(),
            confirm: (_, _) => true, announceState: announcements.Add)
        {
            ShowActivated = false, ShowInTaskbar = false
        };
        MainWindow window = CreateWindow();
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        async Task ShowAsync()
        {
            window.Show();
            await WaitForAsync(() => window.IsLoaded && controller.IsConnected && !Control<Button>("CancelButton").IsEnabled);
            await SettleReadoutWindowAsync(window);
        }
        UiPreferences Saved() => LoadReadoutPreferences(path);
        try
        {
            await ShowAsync();
            AssertReadoutPresentation(window, ValueDisplayMode.StaticText);
            foreach (ValueDisplayMode mode in ReadoutModes)
            {
                SectionWindow settings = window.OpenSettingsWindow();
                await SettleReadoutWindowAsync(window);
                SelectSettingsTab(window, "ControlsSettingsTab");
                ComboBox choice = Control<ComboBox>("ValueDisplayModeChoice");
                Assert.Same(window, settings.Owner);
                Assert.Same(settings, Window.GetWindow(choice));
                Assert.True(settings.IsVisible);
                Assert.True(choice.IsVisible);
                Assert.Null(window.FindName("UseReadOnlyValueFieldsCheckBox"));
                AssertReadoutModeChoice(settings, choice, Saved().ValueDisplayMode);
                var beforePreferences = ReadoutMutationCounts(controller);
                choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, mode));
                Assert.True(window.CommitSettings());
                await SettleReadoutWindowAsync(window);
                Assert.Equal(mode, Saved().ValueDisplayMode);
                Assert.Equal(language, Saved().Language);
                AssertReadoutModeChoice(settings, choice, mode);
                AssertReadoutPresentation(window, mode);
                foreach (bool showHints in new[] { false, true, mode != ValueDisplayMode.None })
                {
                    Control<CheckBox>("ShowHintsCheckBox").IsChecked = showHints;
                    Assert.True(window.CommitSettings());
                    await SettleReadoutWindowAsync(window);
                    Assert.Equal(showHints, Saved().ShowHints);
                    AssertReadoutNavigationNames(window, showHints);
                }
                Control<ComboBox>("SettingsTextScaleChoice").SelectedValue = 200;
                Assert.True(window.CommitSettings());
                await SettleReadoutWindowAsync(window);
                Assert.Equal(200, Saved().TextScale);
                Assert.Equal(SystemFonts.MessageFontSize * 2, window.FontSize);
                Assert.Equal(window.FontSize, settings.FontSize);
                AssertReadoutLayout(window, mode, 200);
                Control<CheckBox>("ShowVolumeRowItem").IsChecked = false;
                Control<CheckBox>("ShowSubwooferRowItem").IsChecked = false;
                Control<CheckBox>("ShowSoundModeRowItem").IsChecked = false;
                Assert.True(window.CommitSettings());
                await SettleReadoutWindowAsync(window);
                ControlRow[] hidden = [ControlRow.Volume, ControlRow.Subwoofer, ControlRow.SoundMode];
                Assert.Equal(hidden.Order(), Saved().HiddenRows.Order());
                AssertReadoutPresentation(window, mode, hidden);
                if (mode == ValueDisplayMode.None)
                {
                    choice.SelectedIndex = 1;
                    Assert.True(window.CommitSettings());
                    await SettleReadoutWindowAsync(window);
                    AssertReadoutPresentation(window, ValueDisplayMode.StaticText, hidden);
                    choice.SelectedIndex = 2;
                    Assert.True(window.CommitSettings());
                    await SettleReadoutWindowAsync(window);
                    AssertReadoutPresentation(window, mode, hidden);
                }
                Control<CheckBox>("ShowVolumeRowItem").IsChecked = true;
                Control<CheckBox>("ShowSubwooferRowItem").IsChecked = true;
                Control<CheckBox>("ShowSoundModeRowItem").IsChecked = true;
                Assert.True(window.CommitSettings());
                await SettleReadoutWindowAsync(window);
                Assert.Empty(Saved().HiddenRows);
                AssertReadoutPresentation(window, mode);
                AssertReadoutLayout(window, mode, 200);
                AssertReadoutLevelControls(window, LevelControlMode.Buttons, LevelControlMode.Slider);
                Assert.Equal(beforePreferences, ReadoutMutationCounts(controller));
                Assert.Empty(announcements);

                Assert.True(MainWindow.IncreaseVolumeCommand.CanExecute(null, window));
                MainWindow.IncreaseVolumeCommand.Execute(null, window);
                await WaitForAsync(() => controller.State!.Volume == 21 && !Control<Button>("CancelButton").IsEnabled);
                await SettleReadoutWindowAsync(window);
                Assert.Equal(Localization.Text("Feedback.Volume", "21"), Assert.Single(announcements));
                Control<CheckBox>("AnnounceExternalStateChangesCheckBox").IsChecked = false;
                Assert.True(window.CommitSettings());
                controller.PublishState(controller.State! with { ClearVoice = true });
                await SettleReadoutWindowAsync(window);
                Assert.Single(announcements);
                Control<CheckBox>("AnnounceExternalStateChangesCheckBox").IsChecked = true;
                Assert.True(window.CommitSettings());
                controller.PublishState(controller.State! with { ClearVoice = false });
                await SettleReadoutWindowAsync(window);
                Assert.Equal(2, announcements.Count);
                Assert.Equal(Localization.Text("Feedback.ClearVoice", Control<TextBox>("ClearVoiceReadout").Text), announcements[^1]);

                window.Close();
                await WaitForAsync(() => controller.Disposed && !window.IsVisible);
                controller = new();
                announcements.Clear();
                window = CreateWindow();
                await ShowAsync();
                settings = window.OpenSettingsWindow();
                await SettleReadoutWindowAsync(window);
                SelectSettingsTab(window, "ControlsSettingsTab");
                AssertReadoutModeChoice(settings, Control<ComboBox>("ValueDisplayModeChoice"), mode);
                Assert.Equal(mode, Saved().ValueDisplayMode);
                AssertReadoutPresentation(window, mode);
                AssertReadoutLayout(window, mode, 200);
                AssertReadoutNavigationNames(window, mode != ValueDisplayMode.None);
                AssertReadoutLevelControls(window, LevelControlMode.Buttons, LevelControlMode.Slider);
                Assert.Empty(controller.Commands);
                Assert.Equal(0, controller.VolumeRequests);
                Assert.Empty(announcements);
            }
            await ExerciseReadoutFailureAndDefaultsAsync(window, controller, path, announcements);
            Assert.True(LoadReadoutPreferences(path).IsSetupComplete);
        }
        finally
        {
            if (!controller.Disposed)
                window.Close();
            await WaitForAsync(() => controller.Disposed && !window.IsVisible);
        }
    }
    private static async Task ExerciseReadoutFailureAndDefaultsAsync(MainWindow window,
        FakeSoundbarController controller, string path, List<string> announcements)
    {
        T Control<T>(string name) where T : FrameworkElement => ReadoutControl<T>(window, name);
        SectionWindow settings = window.OpenSettingsWindow();
        SelectSettingsTab(window, "ControlsSettingsTab");
        ComboBox choice = Control<ComboBox>("ValueDisplayModeChoice");
        var beforeFailure = ReadoutMutationCounts(controller);
        byte[] beforeSave = File.ReadAllBytes(path);
        using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            choice.SelectedIndex = 0;
            Assert.False(window.CommitSettings());
            await SettleReadoutWindowAsync(window);
            AssertReadoutModeChoice(settings, choice, ValueDisplayMode.ReadOnlyFields);
            AssertReadoutPresentation(window, ValueDisplayMode.None);
            TextBox failure = Control<TextBox>("StatusMessage");
            Assert.True(failure.IsVisible);
            Assert.Contains(Localization.Text("Configuration.SaveError"), failure.Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(failure));
            TextBox sectionFailure = ReadoutControl<TextBox>(settings, "SectionStatusMessage");
            Assert.True(sectionFailure.IsVisible);
            Assert.Equal(failure.Text, sectionFailure.Text);
        }
        Assert.Equal(beforeSave, File.ReadAllBytes(path));
        Assert.Equal(ValueDisplayMode.None, LoadReadoutPreferences(path).ValueDisplayMode);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        choice.SelectedIndex = 0;
        Assert.True(window.CommitSettings());
        await SettleReadoutWindowAsync(window);
        Assert.Equal(ValueDisplayMode.ReadOnlyFields, LoadReadoutPreferences(path).ValueDisplayMode);
        AssertReadoutPresentation(window, ValueDisplayMode.ReadOnlyFields);
        choice.SelectedIndex = 2;
        Control<ComboBox>("VolumeControlModeChoice").SelectedIndex = 1;
        Control<ComboBox>("SubwooferControlModeChoice").SelectedIndex = 0;
        Assert.True(window.CommitSettings());
        await SettleReadoutWindowAsync(window);
        Assert.Equal(LevelControlMode.Slider, LoadReadoutPreferences(path).VolumeControlMode);
        Assert.Equal(LevelControlMode.Buttons, LoadReadoutPreferences(path).SubwooferControlMode);
        AssertReadoutLevelControls(window, LevelControlMode.Slider, LevelControlMode.Buttons);
        AssertReadoutPresentation(window, ValueDisplayMode.None);
        AssertReadoutLayout(window, ValueDisplayMode.None, 200);
        Assert.Equal(beforeFailure, ReadoutMutationCounts(controller));
        Assert.Empty(announcements);

        IRangeValueProvider volumeRange = Assert.IsAssignableFrom<IRangeValueProvider>(
            SectionPeer(Control<ConfirmedSlider>("VolumeSlider")).GetPattern(PatternInterface.RangeValue));
        volumeRange.SetValue(22);
        await WaitForAsync(() => controller.State!.Volume == 22 && !Control<Button>("CancelButton").IsEnabled);
        Assert.Equal(22, volumeRange.Value);
        Assert.Equal(1, controller.VolumeRequests);
        MainWindow.IncreaseSubwooferCommand.Execute(null, window);
        await WaitForAsync(() => controller.State!.Subwoofer == 1 && !Control<Button>("CancelButton").IsEnabled);
        Assert.Equal(SoundbarCommand.SubwooferUp, Assert.Single(controller.Commands));
        Assert.Equal(Localization.Text("Feedback.Subwoofer", "1"), announcements[^1]);

        controller.PublishState(controller.State! with { Power = false });
        await SettleReadoutWindowAsync(window);
        var beforeStandbyEdit = ReadoutMutationCounts(controller);
        Assert.False(LoadReadoutPreferences(path).ShowHints);
        Assert.False(LoadReadoutPreferences(path).ShowOperationMessages);
        Assert.True(MainWindow.IncreaseSubwooferCommand.CanExecute(null, window));
        MainWindow.IncreaseSubwooferCommand.Execute(null, window);
        await SettleReadoutWindowAsync(window);
        Assert.Equal(beforeStandbyEdit, ReadoutMutationCounts(controller));
        TextBox warning = Control<TextBox>("StatusMessage");
        Assert.Equal(Localization.Text("State.PowerOffEditBlocked"), warning.Text);
        Assert.True(warning.IsVisible);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(warning));
        Assert.Equal(Localization.Text("Status.MessagePrefix") + warning.Text, SectionPeer(warning).GetName());
        Assert.True(ReadoutControl<TextBox>(settings, "SectionStatusMessage").IsVisible);
        Assert.Equal(warning.Text, ReadoutControl<TextBox>(settings, "SectionStatusMessage").Text);
        AssertReadoutPresentation(window, ValueDisplayMode.None);

        Control<TabItem>("DevicesTab").IsSelected = true;
        await SettleReadoutWindowAsync(window);
        ListBox saved = Control<ListBox>("SavedDevicesList");
        ListBox discovered = Control<ListBox>("DevicesList");
        Assert.False(saved.IsTabStop);
        Assert.True(discovered.IsTabStop);
        Assert.Empty(discovered.Items.Cast<object>());
        foreach (ListBox list in new[] { saved, discovered })
        {
            Assert.Same(window.FindResource("DeviceListStyle"), list.Style);
            Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(list));
            Assert.NotEmpty(SectionPeer(list).GetName());
        }
        Control<TabItem>("ControlTab").IsSelected = true;
        await SettleReadoutWindowAsync(window);
        var beforeReset = ReadoutMutationCounts(controller);
        Control<Button>("ResetApplicationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ValueDisplayMode.None, LoadReadoutPreferences(path).ValueDisplayMode);
        AssertReadoutPresentation(window, ValueDisplayMode.None);
        Assert.True(window.CommitSettings());
        await SettleReadoutWindowAsync(window);
        Assert.Equal(beforeReset, ReadoutMutationCounts(controller));
        Assert.Equal(ValueDisplayMode.StaticText, LoadReadoutPreferences(path).ValueDisplayMode);
        AssertReadoutModeChoice(settings, choice, ValueDisplayMode.StaticText);
        AssertReadoutPresentation(window, ValueDisplayMode.StaticText);
        AssertReadoutNavigationNames(window, true);
        AssertReadoutLayout(window, ValueDisplayMode.StaticText, 100);
    }

    private static void AssertReadoutModeChoice(Window owner, ComboBox choice, ValueDisplayMode expected)
    {
        owner.UpdateLayout();
        Assert.Equal(Localization.Text("Feedback.DisplayModeName"), SectionPeer(choice).GetName());
        Assert.Equal(ReadoutModes, choice.Items.Cast<ComboBoxItem>().Select(item => Assert.IsType<ValueDisplayMode>(item.Tag)));
        Assert.Equal(expected, Assert.IsType<ValueDisplayMode>(Assert.IsType<ComboBoxItem>(choice.SelectedItem).Tag));
        Label label = Assert.Single(SectionElements(owner).OfType<Label>(), candidate => ReferenceEquals(candidate.Target, choice));
        Assert.Equal(Localization.Text("Feedback.DisplayModeName"), label.Content);
        Assert.True(label.IsVisible);
        string[] keys = ["Feedback.ReadOnlyFields", "Feedback.StaticText", "Feedback.NoValues"];
        for (int index = 0; index < keys.Length; index++)
        {
            ComboBoxItem item = Assert.IsType<ComboBoxItem>(choice.Items[index]);
            string text = Localization.Text(keys[index]);
            Assert.NotEqual(keys[index], text);
            Assert.Equal(text, item.Content);
            Assert.Equal(text, SectionPeer(item).GetName());
        }
    }

    private static void AssertReadoutPresentation(MainWindow window, ValueDisplayMode mode, params ControlRow[] hiddenRows)
    {
        (ControlRow Row, string Prefix, string NameKey)[] readouts =
        [
            (ControlRow.Power, "Power", "Readout.PowerName"), (ControlRow.Input, "Input", "Readout.InputName"),
            (ControlRow.Volume, "Volume", "Readout.VolumeName"), (ControlRow.Mute, "Mute", "Readout.MuteName"),
            (ControlRow.Subwoofer, "Subwoofer", "Readout.SubwooferName"), (ControlRow.SoundMode, "Mode", "Readout.ModeName"),
            (ControlRow.ClearVoice, "ClearVoice", "Readout.ClearVoiceName"),
            (ControlRow.BassExtension, "Bass", "Readout.BassExtensionName"),
            (ControlRow.IndicatorBrightness, "Brightness", "Readout.BrightnessName")
        ];
        Grid grid = ReadoutControl<Grid>(window, "ControlRowsGrid");
        foreach (var readout in readouts)
        {
            bool hidden = hiddenRows.Contains(readout.Row);
            TextBox field = ReadoutControl<TextBox>(window, readout.Prefix + "Readout");
            TextBlock text = ReadoutControl<TextBlock>(window, readout.Prefix + "StaticReadout");
            Label label = ReadoutControl<Label>(window, readout.Prefix + "ReadoutLabel");
            bool fieldVisible = !hidden && mode == ValueDisplayMode.ReadOnlyFields;
            bool textVisible = !hidden && mode == ValueDisplayMode.StaticText;
            bool labelVisible = fieldVisible || !hidden && mode == ValueDisplayMode.None
                && readout.Row is ControlRow.Volume or ControlRow.Subwoofer;
            Assert.Equal(fieldVisible ? Visibility.Visible : Visibility.Collapsed, field.Visibility);
            Assert.Equal(textVisible ? Visibility.Visible : Visibility.Collapsed, text.Visibility);
            Assert.Equal(labelVisible ? Visibility.Visible : Visibility.Collapsed, label.Visibility);
            Assert.Equal(fieldVisible, field.IsVisible);
            Assert.Equal(textVisible, text.IsVisible);
            Assert.Equal(labelVisible, label.IsVisible);
            if (fieldVisible)
            {
                Assert.True(field.Focusable);
                Assert.True(field.IsTabStop);
                Assert.True(field.IsReadOnly);
                Assert.Equal(Localization.Text(readout.NameKey), SectionPeer(field).GetName());
                IValueProvider value = Assert.IsAssignableFrom<IValueProvider>(SectionPeer(field).GetPattern(PatternInterface.Value));
                Assert.True(value.IsReadOnly);
                Assert.Equal(field.Text, value.Value);
            }
            if (textVisible)
            {
                Assert.True(text.Focusable);
                Assert.True(KeyboardNavigation.GetIsTabStop(text));
                Assert.Equal(AutomationControlType.Text, SectionPeer(text).GetAutomationControlType());
                Assert.Equal(Localization.Text("Feedback." + readout.Row, field.Text), text.Text);
                Assert.Equal(text.Text, SectionPeer(text).GetName());
            }
            if (hidden)
            {
                int rowIndex = Grid.GetRow(field);
                Assert.All(grid.Children.Cast<UIElement>().Where(child => Grid.GetRow(child) == rowIndex),
                    child => Assert.Equal(Visibility.Collapsed, child.Visibility));
                Assert.Equal(0, grid.RowDefinitions[rowIndex].ActualHeight);
            }
        }
        foreach (string name in new[] { "ConnectionSummary", "ReadoutStatus" })
        {
            TextBlock status = ReadoutControl<TextBlock>(window, name);
            Assert.True(status.IsVisible);
            Assert.NotEmpty(status.Text);
            Assert.Equal(status.Text, SectionPeer(status).GetName());
        }
        foreach ((string name, string key) in new[]
        {
            ("PowerToggle", "Control.Power"), ("InputChoice", "Control.TargetInputName"),
            ("MuteToggle", "Control.Mute"), ("ModeChoice", "Control.TargetModeName"),
            ("ClearVoiceToggle", "Control.ClearVoice"), ("BassToggle", "Control.BassExtension"),
            ("BrightnessChoice", "Control.TargetBrightnessName"), ("RefreshButton", "Control.RefreshName")
        })
        {
            Control control = ReadoutControl<Control>(window, name);
            Assert.Equal(Localization.Text(key), SectionPeer(control).GetName());
            ControlRow? row = name switch
            {
                "PowerToggle" => ControlRow.Power,
                "InputChoice" => ControlRow.Input,
                "MuteToggle" => ControlRow.Mute,
                "ModeChoice" => ControlRow.SoundMode,
                "ClearVoiceToggle" => ControlRow.ClearVoice,
                "BassToggle" => ControlRow.BassExtension,
                "BrightnessChoice" => ControlRow.IndicatorBrightness,
                _ => null
            };
            Assert.Equal(row is null || !hiddenRows.Contains(row.Value), control.IsVisible);
        }
    }

    private static void AssertReadoutLayout(MainWindow window, ValueDisplayMode mode, int scale)
    {
        Grid grid = ReadoutControl<Grid>(window, "ControlRowsGrid");
        Assert.Equal(150 * scale / 100.0, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(mode == ValueDisplayMode.None ? 0 : 170 * scale / 100.0, grid.ColumnDefinitions[1].Width.Value);
        Assert.True(grid.ColumnDefinitions[0].ActualWidth > 0);
        if (mode != ValueDisplayMode.None)
            return;
        Assert.Equal(0, grid.ColumnDefinitions[1].ActualWidth);
        foreach (string prefix in new[] { "Volume", "Subwoofer" })
        {
            Label label = ReadoutControl<Label>(window, prefix + "ReadoutLabel");
            Assert.True(label.IsVisible);
            Assert.Equal(Localization.Text("Control." + prefix + "Label"), label.Content);
            Assert.Null(label.Target);
            Assert.Equal(TextWrapping.Wrap, Assert.IsType<AccessText>(label.ContentTemplate.LoadContent()).TextWrapping);
            Assert.True(label.ActualWidth > 0);
            Rect labelBounds = label.TransformToAncestor(grid).TransformBounds(new Rect(label.RenderSize));
            Assert.True(labelBounds.Right <= grid.ColumnDefinitions[0].ActualWidth + 1);
            foreach (string suffix in new[] { "Buttons", "Slider" })
            {
                FrameworkElement editor = ReadoutControl<FrameworkElement>(window, prefix + suffix);
                if (!editor.IsVisible)
                    continue;
                Rect editorBounds = editor.TransformToAncestor(grid).TransformBounds(new Rect(editor.RenderSize));
                Assert.True(editorBounds.Width > 0);
                Assert.True(editorBounds.Left >= labelBounds.Right - 1);
            }
        }
    }

    private static void AssertReadoutLevelControls(MainWindow window, LevelControlMode volume, LevelControlMode subwoofer)
    {
        foreach ((string prefix, LevelControlMode mode) in new[] { ("Volume", volume), ("Subwoofer", subwoofer) })
        {
            ConfirmedSlider slider = ReadoutControl<ConfirmedSlider>(window, prefix + "Slider");
            WrapPanel buttons = ReadoutControl<WrapPanel>(window, prefix + "Buttons");
            Assert.Equal(mode == LevelControlMode.Slider, slider.IsVisible);
            Assert.Equal(mode == LevelControlMode.Buttons, buttons.IsVisible);
            Assert.Equal(Localization.Text("Levels." + prefix), SectionPeer(slider).GetName());
            foreach (string direction in new[] { "Decrease", "Increase" })
                Assert.Equal(Localization.Text("Control." + direction + prefix + "Name"),
                    SectionPeer(ReadoutControl<Button>(window, direction + prefix + "Button")).GetName());
        }
    }

    private static void AssertReadoutNavigationNames(MainWindow window, bool showHints)
    {
        TabControl tabs = ReadoutControl<TabControl>(window, "MainTabs");
        string expected = showHints ? Localization.Text("Tabs.Name") : string.Empty;
        Assert.Equal(expected, AutomationProperties.GetName(tabs));
        Assert.Equal(expected, SectionPeer(tabs).GetName());
        foreach (string prefix in new[] { "Control", "Devices" })
            Assert.Equal(Localization.Text("Tabs." + prefix + "Name"), SectionPeer(ReadoutControl<TabItem>(window, prefix + "Tab")).GetName());
        Menu menu = ReadoutControl<Menu>(window, "MainMenu");
        Assert.False(menu.IsTabStop);
        Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(menu));
        Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetControlTabNavigation(menu));
        Assert.True(menu.IsVisible);
        Assert.NotEmpty(SectionPeer(ReadoutControl<MenuItem>(window, "FileMenu")).GetName());
        Assert.NotEmpty(SectionPeer(ReadoutControl<MenuItem>(window, "HelpMenu")).GetName());
    }

    private static void ExerciseFirstRunReadoutModes(string language)
    {
        string path = ConfigurationTests.TestPath("first-run-readout-modes-" + language + ".json");
        UiPreferences initial = new();
        Assert.True(initial.TrySave(out _, path));
        foreach (ValueDisplayMode mode in ReadoutModes)
        {
            FirstRunWindow completed = RunDialog(initial, dialog =>
            {
                ComboBox languageChoice = ReadoutControl<ComboBox>(dialog, "LanguageChoice");
                languageChoice.SelectedItem = languageChoice.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, language));
                ComboBox themeChoice = ReadoutControl<ComboBox>(dialog, "ThemeChoice");
                themeChoice.SelectedItem = themeChoice.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "Dark"));
                ComboBox choice = ReadoutControl<ComboBox>(dialog, "ValueDisplayModeChoice");
                Assert.Null(dialog.FindName("UseReadOnlyValueFieldsChoice"));
                AssertReadoutModeChoice(dialog, choice, initial.ValueDisplayMode);
                choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, mode));
                AssertReadoutModeChoice(dialog, choice, mode);
                Button next = ReadoutControl<Button>(dialog, "ContinueButton");
                Assert.True(next.IsEnabled);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            Assert.NotNull(completed.CompletedConfiguration);
            Assert.Equal(mode, completed.CompletedConfiguration.ValueDisplayMode);
            initial = LoadReadoutPreferences(path);
            Assert.True(initial.IsSetupComplete);
            Assert.Equal(language, initial.Language);
            Assert.Equal(mode, initial.ValueDisplayMode);
        }
        byte[] beforeReopen = File.ReadAllBytes(path);
        FirstRunWindow reopened = RunDialog(initial, dialog =>
            AssertReadoutModeChoice(dialog, ReadoutControl<ComboBox>(dialog, "ValueDisplayModeChoice"), ValueDisplayMode.None));
        Assert.Null(reopened.CompletedConfiguration);
        Assert.Equal(beforeReopen, File.ReadAllBytes(path));

        FirstRunWindow RunDialog(UiPreferences preferences, Action<FirstRunWindow> action)
        {
            FirstRunWindow dialog = new(preferences, path) { ShowActivated = false, ShowInTaskbar = false };
            Exception? failure = null;
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    action(dialog);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    if (dialog.IsVisible)
                        dialog.Close();
                }
            }, DispatcherPriority.ApplicationIdle);
            dialog.ShowDialog();
            if (failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return dialog;
        }
    }

    private static T ReadoutControl<T>(FrameworkElement owner, string name) where T : FrameworkElement =>
        Assert.IsAssignableFrom<T>(owner.FindName(name));

    private static (int Commands, int VolumeRequests, int CompletedMutations) ReadoutMutationCounts(FakeSoundbarController controller) =>
        (controller.Commands.Count, controller.VolumeRequests, controller.CompletedMutations);

    private static UiPreferences LoadReadoutPreferences(string path)
    {
        UiPreferences saved = UiPreferences.Load(out string? warning, path);
        Assert.Null(warning);
        return saved;
    }

    private static async Task SettleReadoutWindowAsync(MainWindow window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        foreach (Window owned in window.OwnedWindows)
            owned.UpdateLayout();
    }
}
