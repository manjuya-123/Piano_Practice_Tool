using System.Windows;
using System.Windows.Controls;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Views;

public sealed partial class SongMetadataView : UserControl
{
    private static readonly IReadOnlyList<DifficultyChoice> DifficultyChoices = new[]
    {
        new DifficultyChoice(SongDifficulty.Introductory, "入門"),
        new DifficultyChoice(SongDifficulty.Beginner, "初級"),
        new DifficultyChoice(SongDifficulty.Intermediate, "中級"),
        new DifficultyChoice(SongDifficulty.Advanced, "上級"),
        new DifficultyChoice(SongDifficulty.Unknown, "未分類")
    };

    private SongCatalogEntry? _song;
    private bool _updatingControls;
    private bool _scaleChanged;

    public SongMetadataView()
    {
        InitializeComponent();
        DifficultyComboBox.ItemsSource = DifficultyChoices;
        ScaleTypeComboBox.ItemsSource =
            MusicTheoryAnalyzer.ScaleDefinitions;
    }

    public event EventHandler? BackRequested;

    public event EventHandler<SongMetadataSaveRequestedEventArgs>? SaveRequested;

    public void SetSong(SongCatalogEntry song)
    {
        ArgumentNullException.ThrowIfNull(song);
        _song = song;

        _updatingControls = true;
        try
        {
            FilePathText.Text = song.FilePath;
            TitleTextBox.Text = song.Title;
            ComposerTextBox.Text = song.Score.Composer;
            DifficultyComboBox.SelectedItem =
                DifficultyChoices.First(choice =>
                    choice.Difficulty == song.Difficulty);

            var key = MusicTheoryAnalyzer.AnalyzeKeyAt(
                song.Score,
                0d);
            var scale = MusicTheoryAnalyzer.GetScaleDefinition(
                    key?.ScaleId)
                ?? MusicTheoryAnalyzer.GetScaleDefinition("major")!;
            ScaleTypeComboBox.SelectedItem = scale;
            RefreshTonicChoices(
                scale.Id,
                key?.Fifths ?? 0);

            CurrentScaleText.Text = key is null
                ? "現在: —"
                : $"現在: {key.DisplayName}{(key.IsInferred ? "（推定）" : string.Empty)}";
            _scaleChanged = false;
            UpdateScaleSaveState();
            StatusText.Text = string.Empty;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    public void SetStatus(string message)
        => StatusText.Text = message;

    private void BackButton_Click(
        object sender,
        RoutedEventArgs e)
        => BackRequested?.Invoke(
            this,
            EventArgs.Empty);

    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_song is null
            || DifficultyComboBox.SelectedItem
                is not DifficultyChoice difficulty
            || ScaleTypeComboBox.SelectedItem
                is not MusicScaleDefinition scale
            || ScaleTonicComboBox.SelectedItem
                is not ScaleTonicChoice tonic)
        {
            return;
        }

        var title = TitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            StatusText.Text = "曲名を入力してください。";
            TitleTextBox.Focus();
            return;
        }

        SaveRequested?.Invoke(
            this,
            new SongMetadataSaveRequestedEventArgs(
                _song,
                new SongMetadataUpdate(
                    title,
                    ComposerTextBox.Text.Trim(),
                    difficulty.Difficulty,
                    _scaleChanged,
                    tonic.Fifths,
                    scale.Id)));
    }

    private void ScaleTypeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updatingControls
            || ScaleTypeComboBox.SelectedItem
                is not MusicScaleDefinition scale)
        {
            return;
        }

        var preferredFifths =
            ScaleTonicComboBox.SelectedItem
                is ScaleTonicChoice tonic
                    ? tonic.Fifths
                    : 0;

        _updatingControls = true;
        try
        {
            RefreshTonicChoices(
                scale.Id,
                preferredFifths);
        }
        finally
        {
            _updatingControls = false;
        }

        MarkScaleChanged();
    }

    private void ScaleSelection_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_updatingControls)
        {
            MarkScaleChanged();
        }
    }

    private void MarkScaleChanged()
    {
        _scaleChanged = true;
        UpdateScaleSaveState();
    }

    private void UpdateScaleSaveState()
    {
        if (ScaleSaveStateText is null)
        {
            return;
        }

        ScaleSaveStateText.Text = _scaleChanged
            ? "保存時に曲頭のScale / Keyを更新します"
            : "Scale / Keyは変更しません";
        ScaleSaveStateText.Foreground =
            (System.Windows.Media.Brush)FindResource(
                _scaleChanged
                    ? "AccentTextBrush"
                    : "TextSecondaryBrush");
    }

    private void RefreshTonicChoices(
        string scaleId,
        int preferredFifths)
    {
        var definition =
            MusicTheoryAnalyzer.GetScaleDefinition(scaleId);
        if (definition is null)
        {
            return;
        }

        var choices = Enumerable.Range(-7, 15)
            .Select(fifths =>
            {
                var display =
                    MusicTheoryAnalyzer.GetScaleDisplayName(
                        fifths,
                        definition.Id);
                var suffix = " " + definition.DisplayName;
                var tonicLabel = display.EndsWith(
                        suffix,
                        StringComparison.Ordinal)
                    ? display[..^suffix.Length]
                    : display;
                return new ScaleTonicChoice(
                    fifths,
                    tonicLabel);
            })
            .GroupBy(
                choice => choice.Label,
                StringComparer.Ordinal)
            .Select(group =>
                group.OrderBy(choice =>
                    Math.Abs(choice.Fifths))
                .First())
            .OrderBy(choice =>
                Math.Abs(choice.Fifths))
            .ThenBy(choice => choice.Fifths)
            .ToArray();

        ScaleTonicComboBox.ItemsSource = choices;
        ScaleTonicComboBox.SelectedItem =
            choices.FirstOrDefault(choice =>
                choice.Fifths == preferredFifths)
            ?? choices.FirstOrDefault();
    }

    private sealed record DifficultyChoice(
        SongDifficulty Difficulty,
        string Label);

    private sealed record ScaleTonicChoice(
        int Fifths,
        string Label);
}

public sealed class SongMetadataSaveRequestedEventArgs : EventArgs
{
    public SongMetadataSaveRequestedEventArgs(
        SongCatalogEntry song,
        SongMetadataUpdate update)
    {
        Song = song
            ?? throw new ArgumentNullException(nameof(song));
        Update = update
            ?? throw new ArgumentNullException(nameof(update));
    }

    public SongCatalogEntry Song { get; }

    public SongMetadataUpdate Update { get; }
}
