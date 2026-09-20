using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Midi;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool;

public sealed partial class EditorWindow
{
    private readonly IMidiInputService _editorMidiInputService = new NAudioMidiInputService();
    private readonly HashSet<int> _editorActiveMidiNotes = new();
    private IReadOnlyList<MidiInputDevice> _editorMidiDevices = Array.Empty<MidiInputDevice>();
    private bool _editorMidiEventsWired;
    private bool _editorMidiDisposed;

    private void EditorWindowMidi_ContentRendered(object? sender, EventArgs e)
    {
        if (_editorMidiDisposed)
        {
            return;
        }

        WireEditorMidiEvents();
        RefreshEditorMidiDevices();
        TryConnectEditorMidi();
    }

    private void EditorWindowMidi_Activated(object? sender, EventArgs e)
    {
        if (_editorMidiDisposed || _editorMidiInputService.IsConnected)
        {
            return;
        }

        RefreshEditorMidiDevices();
        TryConnectEditorMidi();
    }

    private void EditorWindowMidi_Closed(object? sender, EventArgs e)
    {
        if (_editorMidiDisposed)
        {
            return;
        }

        _editorMidiDisposed = true;
        if (_editorMidiEventsWired)
        {
            _editorMidiInputService.NoteOn -= EditorMidiInputService_NoteOn;
            _editorMidiInputService.NoteOff -= EditorMidiInputService_NoteOff;
            _editorMidiInputService.ControlChange -= EditorMidiInputService_ControlChange;
            _editorMidiInputService.InputError -= EditorMidiInputService_InputError;
            _editorMidiEventsWired = false;
        }

        try
        {
            _editorMidiInputService.Disconnect();
        }
        catch
        {
            // Window shutdown must continue even when the device has already disappeared.
        }

        _editorMidiInputService.Dispose();
        _editorActiveMidiNotes.Clear();
        _editorView.SetActiveMidiNotes(_editorActiveMidiNotes);
        _editorView.SetConnectedKeyboardRange(null);
        _soundService.AllNotesOff();
    }

    private void WireEditorMidiEvents()
    {
        if (_editorMidiEventsWired)
        {
            return;
        }

        _editorMidiInputService.NoteOn += EditorMidiInputService_NoteOn;
        _editorMidiInputService.NoteOff += EditorMidiInputService_NoteOff;
        _editorMidiInputService.ControlChange += EditorMidiInputService_ControlChange;
        _editorMidiInputService.InputError += EditorMidiInputService_InputError;
        _editorMidiEventsWired = true;
    }

    private void RefreshEditorMidiDevices()
    {
        try
        {
            _editorMidiDevices = _editorMidiInputService.GetDevices().ToArray();
            UpdateEditorMidiStatus();
        }
        catch (Exception ex)
        {
            _editorMidiDevices = Array.Empty<MidiInputDevice>();
            _editorView.SetConnectedKeyboardRange(null);
            EditorMidiStatusText.Text = $"MIDI: 検出失敗 ({ex.Message})";
        }
    }

    private void TryConnectEditorMidi()
    {
        if (!_settings.AutoConnectMidi)
        {
            _editorView.SetConnectedKeyboardRange(null);
            EditorMidiStatusText.Text = "MIDI: 自動接続オフ";
            return;
        }

        var device = FindEditorPreferredMidiDevice();
        if (device is null)
        {
            _editorView.SetConnectedKeyboardRange(null);
            UpdateEditorMidiStatus();
            return;
        }

        if (_editorMidiInputService.IsConnected
            && string.Equals(
                _editorMidiInputService.ConnectedDevice?.Name,
                device.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            ApplyEditorKeyboardRange();
            UpdateEditorMidiStatus();
            return;
        }

        try
        {
            if (_editorMidiInputService.IsConnected)
            {
                _editorMidiInputService.Disconnect();
            }

            _editorMidiInputService.Connect(device);
            ConnectEditorMidiOutput();
            _editorActiveMidiNotes.Clear();
            _editorView.SetActiveMidiNotes(_editorActiveMidiNotes);
            ApplyEditorKeyboardRange();
            UpdateEditorMidiStatus();
        }
        catch (Exception ex)
        {
            _editorView.SetConnectedKeyboardRange(null);
            EditorMidiStatusText.Text = $"MIDI: 接続失敗 ({ex.Message})";
        }
    }

    private MidiInputDevice? FindEditorPreferredMidiDevice()
    {
        if (_editorMidiDevices.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_settings.PreferredMidiDeviceName))
        {
            var preferred = _editorMidiDevices.FirstOrDefault(device => string.Equals(
                device.Name,
                _settings.PreferredMidiDeviceName,
                StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return _editorMidiDevices[0];
    }

    private void ApplyEditorKeyboardRange()
    {
        var range = KeyboardCompatibilityService.CreatePlayableMidiRange(
            _settings,
            _editorOctaveShiftSemitones);
        _editorView.SetConnectedKeyboardRange(range);
    }

    private void UpdateEditorMidiStatus()
    {
        var inputText = _editorMidiInputService.IsConnected
            ? $"IN: {_editorMidiInputService.ConnectedDevice?.Name}"
            : _editorMidiDevices.Count > 0
                ? $"IN: {_editorMidiDevices.Count}台検出 / 未接続"
                : "IN: デバイスなし";
        var outputText = _soundService.IsAvailable
            ? $"OUT: {_soundService.DeviceName}"
            : "OUT: 未接続";

        var range = KeyboardCompatibilityService.CreatePlayableMidiRange(
            _settings,
            _editorOctaveShiftSemitones);
        var shiftText = _editorOctaveShiftSemitones == 0
            ? "shift なし"
            : $"shift {_editorOctaveShiftSemitones / 12:+#;-#;0} oct";
        var rangeText =
            $" / {MidiPitch.ToName(range.LowestMidi)}–{MidiPitch.ToName(range.HighestMidi)} / {shiftText}";
        EditorMidiStatusText.Text = $"MIDI {inputText} / {outputText}{rangeText}";
    }

    private void EditorMidiInputService_NoteOn(object? sender, MidiNoteEventArgs e)
    {
        if (_editorMidiDisposed)
        {
            return;
        }

        var settingsView = _settingsWindow?.View;
        if (settingsView?.IsKeyboardRangeCaptureActive == true)
        {
            Dispatcher.BeginInvoke(() => settingsView.CaptureKeyboardRangeMidiNote(e.MidiNote));
            return;
        }

        try
        {
            _soundService.NoteOn(e.MidiNote, Math.Clamp(e.Velocity, 1, 127), e.Channel);
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => EditorMidiStatusText.Text = $"MIDI: 発音失敗 ({ex.Message})");
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_editorMidiDisposed)
            {
                return;
            }

            _editorActiveMidiNotes.Add(e.MidiNote);
            _editorView.SetActiveMidiNotes(_editorActiveMidiNotes);
            EditorMidiStatusText.Text = $"入力: {MidiPitch.ToName(e.MidiNote)} / vel {e.Velocity}";
        });
    }

    private void EditorMidiInputService_NoteOff(object? sender, MidiNoteEventArgs e)
    {
        if (_editorMidiDisposed)
        {
            return;
        }

        try
        {
            _soundService.NoteOff(e.MidiNote, Math.Clamp(e.Velocity, 0, 127), e.Channel);
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => EditorMidiStatusText.Text = $"MIDI: 離鍵処理失敗 ({ex.Message})");
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_editorMidiDisposed)
            {
                return;
            }

            _editorActiveMidiNotes.Remove(e.MidiNote);
            _editorView.SetActiveMidiNotes(_editorActiveMidiNotes);
        });
    }

    private void EditorMidiInputService_ControlChange(object? sender, MidiControlChangeEventArgs e)
    {
        if (_editorMidiDisposed)
        {
            return;
        }

        try
        {
            _soundService.ControlChange(e.Controller, e.Value, e.Channel);
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => EditorMidiStatusText.Text = $"MIDI: CC処理失敗 ({ex.Message})");
        }
    }

    private void EditorMidiInputService_InputError(object? sender, MidiInputErrorEventArgs e)
        => Dispatcher.BeginInvoke(() => EditorMidiStatusText.Text = $"MIDI: {e.Message}");
}
