using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Practice;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Views;

public sealed partial class SongLibraryView : UserControl
{
    private readonly Dictionary<string, bool> _octaveCompressionChoices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _scoreCorrectionChoices =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<SongCatalogEntry> _songs =
        Array.Empty<SongCatalogEntry>();
    private IReadOnlyDictionary<string, PracticeModeBestScores> _bestScores =
        new Dictionary<string, PracticeModeBestScores>(
            StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings = new();
    private SongDifficulty? _difficultyFilter;

    public SongLibraryView()
    {
        InitializeComponent();
    }

    public event EventHandler<SongSelectedEventArgs>? SongSelected;

    public event EventHandler<SongMetadataEditRequestedEventArgs>?
        MetadataEditRequested;

    public event EventHandler? OpenFileRequested;

    public event EventHandler? RefreshRequested;

    public void SetSongs(
        IReadOnlyList<SongCatalogEntry> songs,
        IReadOnlyList<string> errors)
        => SetSongs(
            songs,
            errors,
            new AppSettings());

    public void SetSongs(
        IReadOnlyList<SongCatalogEntry> songs,
        IReadOnlyList<string> errors,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(songs);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(settings);

        _songs = songs.ToArray();
        _settings = settings.Clone();
        ApplyFilter();

        WarningPanel.Visibility =
            errors.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        WarningText.Text = errors.Count > 0
            ? string.Join(
                Environment.NewLine,
                errors)
            : string.Empty;
    }

    public void SetBestScores(
        IReadOnlyDictionary<string, PracticeModeBestScores> bestScores)
    {
        ArgumentNullException.ThrowIfNull(bestScores);
        _bestScores = bestScores.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (SearchTextBox is null
            || SongListView is null)
        {
            return;
        }

        var query = SearchTextBox.Text.Trim();
        IEnumerable<SongCatalogEntry> filteredSongs = _songs;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filteredSongs = filteredSongs.Where(song =>
                song.Title.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase)
                || song.Score.Composer.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase)
                || song.FileName.Contains(
                    query,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        if (_difficultyFilter is SongDifficulty difficulty)
        {
            filteredSongs = filteredSongs.Where(song =>
                song.Difficulty == difficulty);
        }

        var rows = filteredSongs
            .Select(CreateSongRow)
            .ToArray();

        SongListView.ItemsSource = rows;
        EmptyStatePanel.Visibility =
            rows.Length > 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        SongCountText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} / {1:N0} 曲",
            rows.Length,
            _songs.Count);
    }

    private SongRow CreateSongRow(
        SongCatalogEntry song)
    {
        var compression =
            SongOctaveCompressionService.Analyze(
                song.Score,
                _settings.KeyboardLowestMidi,
                _settings.KeyboardHighestMidi);
        var fullPath =
            System.IO.Path.GetFullPath(
                song.FilePath);
        var useCompression =
            compression.CanCompress
            && _octaveCompressionChoices.TryGetValue(
                fullPath,
                out var compressionEnabled)
            && compressionEnabled;
        var useScoreCorrection =
            song.Consistency.CanAutoCorrect
            && _scoreCorrectionChoices.TryGetValue(
                fullPath,
                out var correctionEnabled)
            && correctionEnabled;

        return new SongRow(
            song,
            compression,
            GetBestScores(song.FilePath),
            useCompression,
            useScoreCorrection,
            _settings);
    }

    private PracticeModeBestScores? GetBestScores(
        string songPath)
    {
        var fullPath =
            System.IO.Path.GetFullPath(
                songPath);
        return _bestScores.TryGetValue(
                fullPath,
                out var scores)
            ? scores
            : null;
    }

    private void StartPracticeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: SongRow row })
        {
            RaiseSongSelected(row);
        }
    }

    private void SongListView_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (SongListView.SelectedItem
            is SongRow row)
        {
            RaiseSongSelected(row);
        }
    }

    private void RaiseSongSelected(
        SongRow row)
        => SongSelected?.Invoke(
            this,
            new SongSelectedEventArgs(
                row.Song,
                row.UseOctaveCompression,
                row.UseScoreCorrection));

    private void MetadataButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: SongRow row
            })
        {
            return;
        }

        MetadataEditRequested?.Invoke(
            this,
            new SongMetadataEditRequestedEventArgs(
                row.Song));
    }

    private void CompressionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: SongRow row
            }
            || !row.CanUseCompression)
        {
            return;
        }

        var fullPath =
            System.IO.Path.GetFullPath(
                row.Song.FilePath);
        _octaveCompressionChoices[fullPath] =
            !row.UseOctaveCompression;
        ApplyFilter();
    }

    private void ScoreCorrectionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: SongRow row
            }
            || !row.CanUseScoreCorrection)
        {
            return;
        }

        var fullPath =
            System.IO.Path.GetFullPath(
                row.Song.FilePath);
        _scoreCorrectionChoices[fullPath] =
            !row.UseScoreCorrection;
        ApplyFilter();
    }

    private void DifficultyFilter_Checked(
        object sender,
        RoutedEventArgs e)
    {
        _difficultyFilter = sender switch
        {
            RadioButton radio
                when ReferenceEquals(
                    radio,
                    IntroductoryDifficultyFilter)
                => SongDifficulty.Introductory,
            RadioButton radio
                when ReferenceEquals(
                    radio,
                    BeginnerDifficultyFilter)
                => SongDifficulty.Beginner,
            RadioButton radio
                when ReferenceEquals(
                    radio,
                    IntermediateDifficultyFilter)
                => SongDifficulty.Intermediate,
            RadioButton radio
                when ReferenceEquals(
                    radio,
                    AdvancedDifficultyFilter)
                => SongDifficulty.Advanced,
            RadioButton radio
                when ReferenceEquals(
                    radio,
                    UnknownDifficultyFilter)
                => SongDifficulty.Unknown,
            _ => null
        };

        ApplyFilter();
    }

    private void OpenFileButton_Click(
        object sender,
        RoutedEventArgs e)
        => OpenFileRequested?.Invoke(
            this,
            EventArgs.Empty);

    private void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
        => RefreshRequested?.Invoke(
            this,
            EventArgs.Empty);

    private void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
        => ApplyFilter();

    private sealed class SongRow
    {
        public SongRow(
            SongCatalogEntry song,
            SongOctaveCompressionAnalysis compression,
            PracticeModeBestScores? bestScores,
            bool useOctaveCompression,
            bool useScoreCorrection,
            AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(
                settings);

            Song = song;
            UseOctaveCompression =
                useOctaveCompression;
            UseScoreCorrection =
                useScoreCorrection;
            CanUseCompression =
                compression.CanCompress;
            CompressionVisibility =
                compression.IsNeeded
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            CanUseScoreCorrection =
                song.Consistency.CanAutoCorrect;
            ScoreCorrectionVisibility =
                song.Consistency.CanAutoCorrect
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ConsistencyWarningVisibility =
                song.Consistency.HasIssues
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            DifficultyText =
                song.DifficultyText;
            ComposerText =
                string.IsNullOrWhiteSpace(
                    song.Score.Composer)
                    ? string.Empty
                    : $"／ {song.Score.Composer}";

            BestScoreText = string.Format(
                CultureInfo.CurrentCulture,
                "BEST 待機 {0} / 通常 {1} / 原曲 {2}",
                FormatBestScore(
                    bestScores?.WaitForCorrectNotes),
                FormatBestScore(
                    bestScores?.PlayAlong),
                FormatBestScore(
                    bestScores?.OriginalTempo));

            CompressionButtonText =
                !compression.CanCompress
                    ? "楽曲圧縮: 不可"
                    : useOctaveCompression
                        ? "楽曲圧縮: ON"
                        : "楽曲圧縮: OFF";
            CompressionToolTip =
                compression.Message
                + Environment.NewLine
                + "楽曲圧縮は、曲の音をオクターブ単位で移動して現在の鍵盤範囲へ収めます。";

            ScoreCorrectionButtonText =
                useScoreCorrection
                    ? "譜面補正: ON"
                    : "譜面補正: OFF";
            ConsistencyWarningToolTip =
                PracticeScorePreparation
                    .BuildIssueSummary(
                        song.Score,
                        song.Consistency);
            ScoreCorrectionToolTip =
                ConsistencyWarningToolTip
                + Environment.NewLine
                + "ONにしても元のMusicXMLは変更せず、練習用データだけを補正します。";

            AvailabilityTags =
                BuildAvailabilityTags(
                    song.Score,
                    settings,
                    compression);

            var handCorrectionText =
                song.AutomaticHandCorrectionCount > 0
                    ? Environment.NewLine
                      + $"演奏用の左右手割当を {song.AutomaticHandCorrectionCount:N0} 音、自動調整しています。"
                    : string.Empty;
            CardToolTip =
                $"MIDI鍵盤範囲: {MidiPitch.ToName(settings.KeyboardLowestMidi)}–{MidiPitch.ToName(settings.KeyboardHighestMidi)}"
                + Environment.NewLine
                + string.Join(
                    " / ",
                    AvailabilityTags.Select(tag =>
                        tag.ToolTip))
                + handCorrectionText
                + Environment.NewLine
                + "MIDIキーボード本体のオクターブシフトは練習設定画面で選べます。";
        }

        public SongCatalogEntry Song { get; }

        public bool UseOctaveCompression { get; }

        public bool UseScoreCorrection { get; }

        public bool CanUseCompression { get; }

        public bool CanUseScoreCorrection { get; }

        public Visibility CompressionVisibility { get; }

        public Visibility ScoreCorrectionVisibility { get; }

        public Visibility ConsistencyWarningVisibility { get; }

        public string DifficultyText { get; }

        public string ComposerText { get; }

        public string BestScoreText { get; }

        public string CompressionButtonText { get; }

        public string CompressionToolTip { get; }

        public string ScoreCorrectionButtonText { get; }

        public string ScoreCorrectionToolTip { get; }

        public string ConsistencyWarningToolTip { get; }

        public string CardToolTip { get; }

        public IReadOnlyList<SongAvailabilityTag> AvailabilityTags { get; }

        private static IReadOnlyList<SongAvailabilityTag> BuildAvailabilityTags(
            MusicScore score,
            AppSettings settings,
            SongOctaveCompressionAnalysis compression)
        {
            var tags =
                new List<SongAvailabilityTag>();
            var original =
                KeyboardCompatibilityService
                    .AnalyzePracticeAvailability(
                        score,
                        settings);
            if (original.Level
                != KeyboardPracticeAvailabilityLevel.Insufficient)
            {
                tags.Add(
                    CreateAvailabilityTag(
                        original.ShortText,
                        "楽曲圧縮なし: "
                        + original.Description,
                        requiresCompression: false));
            }

            if (compression.CanCompress)
            {
                var compressedScore =
                    SongOctaveCompressionService
                        .Compress(
                            score,
                            settings.KeyboardLowestMidi,
                            settings.KeyboardHighestMidi);
                var compressed =
                    KeyboardCompatibilityService
                        .AnalyzePracticeAvailability(
                            compressedScore,
                            settings);
                if (compressed.Level
                        != KeyboardPracticeAvailabilityLevel.Insufficient
                    && (original.Level
                            == KeyboardPracticeAvailabilityLevel.Insufficient
                        || compressed.Level
                            != original.Level))
                {
                    tags.Add(
                        CreateAvailabilityTag(
                            "圧縮で"
                            + compressed.ShortText,
                            "楽曲圧縮あり: "
                            + compressed.Description,
                            requiresCompression: true));
                }
            }

            if (tags.Count == 0)
            {
                tags.Add(
                    new SongAvailabilityTag(
                        "音域不足",
                        "現在の鍵盤範囲では、片手練習を含めても全音域を演奏できません。",
                        new SolidColorBrush(
                            Color.FromRgb(
                                255,
                                107,
                                129)),
                        new SolidColorBrush(
                            Color.FromArgb(
                                38,
                                255,
                                107,
                                129))));
            }

            return tags;
        }

        private static SongAvailabilityTag CreateAvailabilityTag(
            string text,
            string toolTip,
            bool requiresCompression)
        {
            if (requiresCompression)
            {
                return new SongAvailabilityTag(
                    text,
                    toolTip,
                    new SolidColorBrush(
                        Color.FromRgb(
                            255,
                            209,
                            102)),
                    new SolidColorBrush(
                        Color.FromArgb(
                            38,
                            255,
                            209,
                            102)));
            }

            return new SongAvailabilityTag(
                text,
                toolTip,
                new SolidColorBrush(
                    Color.FromRgb(
                        85,
                        214,
                        138)),
                new SolidColorBrush(
                    Color.FromArgb(
                        38,
                        85,
                        214,
                        138)));
        }

        private static string FormatBestScore(
            double? score)
            => score is null
                ? "—"
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "{0:0.0}",
                    score.Value);
    }
}

public sealed class SongSelectedEventArgs : EventArgs
{
    public SongSelectedEventArgs(
        SongCatalogEntry song,
        bool useOctaveCompression = false,
        bool useScoreCorrection = false)
    {
        Song = song
            ?? throw new ArgumentNullException(
                nameof(song));
        UseOctaveCompression =
            useOctaveCompression;
        UseScoreCorrection =
            useScoreCorrection;
    }

    public SongCatalogEntry Song { get; }

    public bool UseOctaveCompression { get; }

    public bool UseScoreCorrection { get; }
}

public sealed record SongAvailabilityTag(
    string Text,
    string ToolTip,
    Brush Foreground,
    Brush Background);

public sealed class SongMetadataEditRequestedEventArgs : EventArgs
{
    public SongMetadataEditRequestedEventArgs(
        SongCatalogEntry song)
    {
        Song = song
            ?? throw new ArgumentNullException(
                nameof(song));
    }

    public SongCatalogEntry Song { get; }
}
