using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PianoPracticeTool.Services.Songs;

namespace PianoPracticeTool.Views;

public sealed record EditorSongRequestedEventArgs(SongCatalogEntry Song);

public sealed partial class EditorSongLibraryView : UserControl
{
    private IReadOnlyList<SongCatalogEntry> _songs = Array.Empty<SongCatalogEntry>();

    public EditorSongLibraryView()
    {
        InitializeComponent();
    }

    public event EventHandler<EditorSongRequestedEventArgs>? DirectEditRequested;

    public event EventHandler<EditorSongRequestedEventArgs>? DuplicateEditRequested;

    public event EventHandler? OpenFileRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? BackRequested;

    public void SetSongs(IReadOnlyList<SongCatalogEntry> songs, IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(songs);
        ArgumentNullException.ThrowIfNull(errors);
        _songs = songs.ToArray();
        ApplyFilter();

        WarningPanel.Visibility = errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = errors.Count > 0
            ? string.Join(Environment.NewLine, errors)
            : string.Empty;
    }

    private void ApplyFilter()
    {
        var query = SearchTextBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _songs
            : _songs.Where(song =>
                    song.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || song.FileName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        var rows = filtered.Select(song => new EditorSongRow(song)).ToArray();
        SongListView.ItemsSource = rows;
        EmptyStatePanel.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SongCountText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:N0} / {1:N0} 曲",
            rows.Length,
            _songs.Count);
    }

    private void DirectEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditorSongRow row })
        {
            DirectEditRequested?.Invoke(this, new EditorSongRequestedEventArgs(row.Song));
        }
    }

    private void DuplicateEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditorSongRow row })
        {
            DuplicateEditRequested?.Invoke(this, new EditorSongRequestedEventArgs(row.Song));
        }
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        => OpenFileRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
        => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter();

    private sealed record EditorSongRow(SongCatalogEntry Song);
}
