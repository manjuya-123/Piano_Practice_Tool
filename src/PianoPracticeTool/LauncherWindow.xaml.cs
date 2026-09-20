using System.Windows;
using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Views;

namespace PianoPracticeTool;

public sealed partial class LauncherWindow : Window
{
    public LauncherWindow()
    {
        InitializeComponent();
    }

    private void PracticeButton_Click(object sender, RoutedEventArgs e)
        => OpenModeWindow(new MainWindow());

    private void EditorButton_Click(object sender, RoutedEventArgs e)
        => OpenModeWindow(new EditorWindow());

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsService = new AppSettingsService();
        var settings = settingsService.Load();

        using var midiInputService = new NAudioMidiInputService();
        using var soundService = new NAudioMidiSoundService();

        IReadOnlyList<MidiInputDevice> inputDevices;
        IReadOnlyList<MidiOutputDevice> outputDevices;

        try
        {
            soundService.Initialize();
            soundService.ConfigureBuiltInSoundFont(
                SoundFontSettings.CreateConfiguration(settings));
            inputDevices = midiInputService.GetDevices().ToArray();
            outputDevices = soundService.GetDevices().ToArray();
            ConnectStandaloneInput(midiInputService, inputDevices, settings);
            ConnectStandaloneOutput(soundService, outputDevices, settings);
        }
        catch (Exception ex)
        {
            inputDevices = Array.Empty<MidiInputDevice>();
            outputDevices = Array.Empty<MidiOutputDevice>();
            MessageBox.Show(
                this,
                ex.Message,
                "設定用MIDI初期化エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var window = new SettingsWindow(
            settings,
            inputDevices.Select(device => device.Name).ToArray(),
            outputDevices.Select(device => device.Name).ToArray(),
            GetStandaloneMidiStatus(midiInputService, soundService, inputDevices, outputDevices))
        {
            Owner = this
        };

        void OnInputNoteOn(object? sender, MidiNoteEventArgs e)
        {
            if (window.View.IsKeyboardRangeCaptureActive)
            {
                Dispatcher.BeginInvoke(() => window.View.CaptureKeyboardRangeMidiNote(e.MidiNote));
            }
        }

        void OnRefreshMidi(object? sender, EventArgs e)
        {
            try
            {
                inputDevices = midiInputService.GetDevices().ToArray();
                outputDevices = soundService.GetDevices().ToArray();
                window.View.UpdateMidiState(
                    inputDevices.Select(device => device.Name).ToArray(),
                    outputDevices.Select(device => device.Name).ToArray(),
                    GetStandaloneMidiStatus(midiInputService, soundService, inputDevices, outputDevices));
            }
            catch (Exception ex)
            {
                window.View.SetStatus($"MIDI再検出失敗: {ex.Message}");
            }
        }

        void OnPreviewBuiltInSoundFont(object? sender, SoundFontConfigurationEventArgs e)
        {
            try
            {
                soundService.ConfigureBuiltInSoundFont(e.Configuration);
            }
            catch (Exception ex)
            {
                window.View.SetStatus($"SoundFontプレビュー更新失敗: {ex.Message}");
            }
        }

        async void OnTestBuiltInSoundFont(object? sender, SoundFontConfigurationEventArgs e)
        {
            if (!string.Equals(
                    soundService.DeviceName,
                    MidiOutputDevice.BuiltInSoundFont.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                window.View.SetStatus("発音先に「Built-in SoundFont Synthesizer」を選択して設定を保存してください。");
                return;
            }

            try
            {
                soundService.AllNotesOff();
                soundService.ConfigureBuiltInSoundFont(e.Configuration);
                soundService.NoteOn(60, 127, 1);
                await Task.Delay(700);
                soundService.NoteOff(60, 0, 1);
                await Task.Delay(900);
                window.View.SetStatus("現在のSoundFont・Preset・リバーブ設定で試聴しました。");
            }
            catch (Exception ex)
            {
                window.View.SetStatus($"SoundFont音源の試聴に失敗しました: {ex.Message}");
            }
        }

        void OnSave(object? sender, SettingsSaveRequestedEventArgs e)
        {
            try
            {
                settings = e.Settings.Clone();
                settingsService.Save(settings);
                soundService.ConfigureBuiltInSoundFont(
                    SoundFontSettings.CreateConfiguration(settings));

                inputDevices = midiInputService.GetDevices().ToArray();
                outputDevices = soundService.GetDevices().ToArray();
                ConnectStandaloneInput(midiInputService, inputDevices, settings);
                ConnectStandaloneOutput(soundService, outputDevices, settings);

                window.View.SetState(
                    settings,
                    inputDevices.Select(device => device.Name).ToArray(),
                    outputDevices.Select(device => device.Name).ToArray(),
                    $"保存しました / {GetStandaloneMidiStatus(midiInputService, soundService, inputDevices, outputDevices)}");
            }
            catch (Exception ex)
            {
                window.View.SetStatus($"設定保存失敗: {ex.Message}");
            }
        }

        midiInputService.NoteOn += OnInputNoteOn;
        window.View.RefreshMidiRequested += OnRefreshMidi;
        window.View.BuiltInSoundFontPreviewChanged += OnPreviewBuiltInSoundFont;
        window.View.BuiltInSoundFontTestRequested += OnTestBuiltInSoundFont;
        window.View.SaveRequested += OnSave;

        try
        {
            window.ShowDialog();
        }
        finally
        {
            midiInputService.NoteOn -= OnInputNoteOn;
            window.View.RefreshMidiRequested -= OnRefreshMidi;
            window.View.BuiltInSoundFontPreviewChanged -= OnPreviewBuiltInSoundFont;
            window.View.BuiltInSoundFontTestRequested -= OnTestBuiltInSoundFont;
            window.View.SaveRequested -= OnSave;
        }
    }

    private static void ConnectStandaloneInput(
        IMidiInputService inputService,
        IReadOnlyList<MidiInputDevice> devices,
        AppSettings settings)
    {
        if (inputService.IsConnected)
        {
            inputService.Disconnect();
        }

        if (!settings.AutoConnectMidi || devices.Count == 0)
        {
            return;
        }

        var device = devices.FirstOrDefault(item => string.Equals(
                item.Name,
                settings.PreferredMidiDeviceName,
                StringComparison.OrdinalIgnoreCase))
            ?? devices[0];
        inputService.Connect(device);
    }

    private static void ConnectStandaloneOutput(
        IMidiSoundService soundService,
        IReadOnlyList<MidiOutputDevice> devices,
        AppSettings settings)
    {
        soundService.Disconnect();
        var device = MidiOutputSelector.Select(devices, settings.PreferredMidiOutputDeviceName);
        if (device is null)
        {
            return;
        }

        soundService.ConfigureBuiltInSoundFont(
            SoundFontSettings.CreateConfiguration(settings));
        soundService.Connect(device);
    }

    private static string GetStandaloneMidiStatus(
        IMidiInputService inputService,
        IMidiSoundService soundService,
        IReadOnlyList<MidiInputDevice> inputDevices,
        IReadOnlyList<MidiOutputDevice> outputDevices)
    {
        var inputText = inputService.IsConnected
            ? $"IN: {inputService.ConnectedDevice?.Name}"
            : inputDevices.Count > 0
                ? $"IN: {inputDevices.Count}台検出 / 未接続"
                : "IN: デバイスなし";
        var outputText = soundService.IsAvailable
            ? $"OUT: {soundService.DeviceName}"
            : outputDevices.Count > 0
                ? $"OUT: {outputDevices.Count}台検出 / 未接続"
                : "OUT: デバイスなし";
        return $"MIDI {inputText} / {outputText}";
    }

    private void OpenModeWindow(Window window)
    {
        IsEnabled = false;
        Hide();
        window.Closed += ModeWindow_Closed;
        window.Show();
    }

    private void ModeWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= ModeWindow_Closed;
        }

        IsEnabled = true;
        Show();
        Activate();
    }
}
