using System.Globalization;
using System.Windows;
using PianoPracticeTool.Core;
using PianoPracticeTool.Core.Editing;

namespace PianoPracticeTool.Views;

public sealed partial class ScoreEditorView
{
    private static readonly HandChoice[] InspectorHandChoices =
    {
        new(Hand.Right, "右手"),
        new(Hand.Left, "左手")
    };

    private void InspectorApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null
            || _selectedNoteIds.Count == 0)
        {
            RefreshNoteInspector();
            return;
        }

        if (_selectedNoteIds.Count > 1)
        {
            ApplySelectedHand();
            return;
        }

        var noteId = _selectedNoteIds.Single();
        if (!int.TryParse(
                InspectorMidiTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var midiNote)
            || midiNote is < 0 or > 127)
        {
            InspectorMessageText.Text = "MIDIノート番号は0～127で入力してください。";
            return;
        }

        if (!TryParseInspectorDouble(InspectorStartTextBox.Text, out var startBeat)
            || startBeat < 0d)
        {
            InspectorMessageText.Text = "開始位置は0以上のbeatで入力してください。";
            return;
        }

        if (!TryParseInspectorDouble(InspectorDurationTextBox.Text, out var durationBeat)
            || durationBeat <= 0d)
        {
            InspectorMessageText.Text = "長さは0より大きいbeatで入力してください。";
            return;
        }

        if (!int.TryParse(
                InspectorFingerTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var finger)
            || finger is < 0 or > 5)
        {
            InspectorMessageText.Text = "指番号は0（未指定）または1～5で入力してください。";
            return;
        }

        if (InspectorHandComboBox.SelectedItem
            is not HandChoice handChoice)
        {
            InspectorMessageText.Text =
                "右手または左手を選択してください。";
            return;
        }

        var hand =
            handChoice.Hand;
        var voice = string.IsNullOrWhiteSpace(InspectorVoiceTextBox.Text)
            ? "1"
            : InspectorVoiceTextBox.Text.Trim();
        var startTick = SnapTick(EditableMusicScore.BeatToTick(startBeat));
        var durationTick = Math.Max(
            EditableMusicScore.MinimumDurationTicks,
            SnapTick(EditableMusicScore.BeatToTick(durationBeat)));

        try
        {
            _session.UpdateNote(
                noteId,
                midiNote,
                startTick,
                durationTick,
                hand,
                voice,
                finger);
            ApplySessionToView(
                $"{MidiPitch.ToName(midiNote)} のノート情報を更新しました。");
        }
        catch (InvalidOperationException ex)
        {
            InspectorMessageText.Text = ex.Message;
        }
        catch (OverflowException)
        {
            InspectorMessageText.Text = "入力値が大きすぎます。";
        }
    }

    private void ApplySelectedHand()
    {
        if (_session is null
            || _selectedNoteIds.Count <= 1)
        {
            RefreshNoteInspector();
            return;
        }

        if (InspectorHandComboBox.SelectedItem
            is not HandChoice handChoice)
        {
            InspectorMessageText.Text =
                "左右が混在しています。右手または左手を選択してください。";
            return;
        }

        var selectedCount =
            _selectedNoteIds.Count;
        try
        {
            _session.SetHand(
                _selectedNoteIds,
                handChoice.Hand);
            ApplySessionToView(
                handChoice.Hand == Hand.Right
                    ? $"{selectedCount}ノートを右手へ変更しました。"
                    : $"{selectedCount}ノートを左手へ変更しました。");
        }
        catch (InvalidOperationException ex)
        {
            InspectorMessageText.Text =
                ex.Message;
        }
    }

    private void RefreshNoteInspector()
    {
        if (InspectorSelectionText is null)
        {
            return;
        }

        if (_session is null
            || _selectedNoteIds.Count == 0)
        {
            SetInspectorEnabled(false);
            InspectorApplyButton.Content =
                "ノート情報を適用";
            InspectorSelectionText.Text =
                "ノートを選択してください。";
            InspectorMessageText.Text =
                string.Empty;
            ClearInspectorDetailValues();
            return;
        }

        if (_selectedNoteIds.Count > 1)
        {
            var selectedNotes =
                _session.Score.Notes
                    .Where(note =>
                        _selectedNoteIds.Contains(
                            note.Id))
                    .ToArray();
            if (selectedNotes.Length == 0)
            {
                SetInspectorEnabled(false);
                InspectorApplyButton.Content =
                    "ノート情報を適用";
                InspectorSelectionText.Text =
                    "選択ノートが見つかりません。";
                ClearInspectorDetailValues();
                return;
            }

            SetInspectorEnabled(false);
            InspectorHandComboBox.IsEnabled =
                true;
            InspectorApplyButton.IsEnabled =
                true;
            InspectorApplyButton.Content =
                "選択ノートの手を適用";
            InspectorSelectionText.Text =
                $"{selectedNotes.Length}ノート選択中。手の指定をまとめて変更できます。";
            ClearInspectorDetailValues();

            var distinctHands =
                selectedNotes
                    .Select(note =>
                        note.Hand)
                    .Distinct()
                    .ToArray();
            InspectorHandComboBox.SelectedItem =
                distinctHands.Length == 1
                    ? InspectorHandChoices.First(choice =>
                        choice.Hand == distinctHands[0])
                    : null;
            InspectorMessageText.Text =
                distinctHands.Length == 1
                    ? $"現在は全て{FormatInspectorHand(distinctHands[0])}です。"
                    : "左右が混在しています。右手または左手を選んで一括適用できます。";
            return;
        }

        var noteId =
            _selectedNoteIds.Single();
        var note =
            _session.Score.Notes.FirstOrDefault(item =>
                item.Id == noteId);
        if (note is null)
        {
            SetInspectorEnabled(false);
            InspectorApplyButton.Content =
                "ノート情報を適用";
            InspectorSelectionText.Text =
                "選択ノートが見つかりません。";
            ClearInspectorDetailValues();
            return;
        }

        SetInspectorEnabled(true);
        InspectorApplyButton.Content =
            "ノート情報を適用";
        InspectorSelectionText.Text =
            $"ID {note.Id} / {MidiPitch.ToName(note.MidiNote)}";
        InspectorMidiTextBox.Text =
            note.MidiNote.ToString(
                CultureInfo.CurrentCulture);
        InspectorPitchNameText.Text =
            MidiPitch.ToName(
                note.MidiNote);
        InspectorStartTextBox.Text =
            EditableMusicScore
                .TickToBeat(note.StartTick)
                .ToString(
                    "0.###",
                    CultureInfo.CurrentCulture);
        InspectorDurationTextBox.Text =
            EditableMusicScore
                .TickToBeat(note.DurationTick)
                .ToString(
                    "0.###",
                    CultureInfo.CurrentCulture);
        InspectorVoiceTextBox.Text =
            string.IsNullOrWhiteSpace(note.Voice)
                ? "1"
                : note.Voice;
        InspectorFingerTextBox.Text =
            note.Finger.ToString(
                CultureInfo.CurrentCulture);
        InspectorHandComboBox.SelectedItem =
            InspectorHandChoices.First(choice =>
                choice.Hand == note.Hand);
        InspectorTickText.Text =
            $"start {note.StartTick:N0} tick / length {note.DurationTick:N0} tick";
        InspectorMessageText.Text =
            "適用時に現在のスナップへ吸着します。";
    }

    private void ClearInspectorDetailValues()
    {
        InspectorMidiTextBox.Text =
            string.Empty;
        InspectorStartTextBox.Text =
            string.Empty;
        InspectorDurationTextBox.Text =
            string.Empty;
        InspectorVoiceTextBox.Text =
            string.Empty;
        InspectorFingerTextBox.Text =
            string.Empty;
        InspectorPitchNameText.Text =
            "—";
        InspectorTickText.Text =
            string.Empty;
    }

    private static string FormatInspectorHand(
        Hand hand)
        => hand == Hand.Right
            ? "右手"
            : "左手";

    private void SetInspectorEnabled(bool enabled)
    {
        InspectorMidiTextBox.IsEnabled = enabled;
        InspectorStartTextBox.IsEnabled = enabled;
        InspectorDurationTextBox.IsEnabled = enabled;
        InspectorVoiceTextBox.IsEnabled = enabled;
        InspectorFingerTextBox.IsEnabled = enabled;
        InspectorHandComboBox.IsEnabled = enabled;
        InspectorApplyButton.IsEnabled = enabled;
    }

    private static bool TryParseInspectorDouble(string value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private sealed record HandChoice(Hand Hand, string Label);
}
