using System.Threading;
using System.Windows;
using PianoPracticeTool.Core;
using PianoPracticeTool.Services.Practice;
using PianoPracticeTool.Services.Settings;

namespace PianoPracticeTool;

public sealed partial class MainWindow
{
    private const int PracticeSnapshotIntervalMilliseconds = 250;

    private int _performancePresentationActive;
    private int _practiceSnapshotCaptureActive;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        _songLibraryView.Loaded += SongLibraryView_PracticePresentationLoaded;
        ApplyPerformanceTimingProfile(_settings);
    }

    private void SongLibraryView_PracticePresentationLoaded(object sender, RoutedEventArgs e)
        => _songLibraryView.SetBestScores(_practiceHistoryService.GetBestHundredPointScoresBySong());

    private void SetPerformancePresentationActive(
        bool active)
    {
        Volatile.Write(
            ref _performancePresentationActive,
            active
                ? 1
                : 0);
    }

    private void CapturePracticeSnapshot(
        object? state)
    {
        if (_disposed
            || Volatile.Read(
                ref _performancePresentationActive) == 0
            || Interlocked.Exchange(
                ref _practiceSnapshotCaptureActive,
                1) != 0)
        {
            return;
        }

        try
        {
            if (!Monitor.TryEnter(
                    _practiceWorkflowSync))
            {
                return;
            }

            try
            {
                QueuePracticeSnapshot(
                    _practiceWorkflow
                        .CreateSnapshot());
            }
            finally
            {
                Monitor.Exit(
                    _practiceWorkflowSync);
            }
        }
        finally
        {
            Volatile.Write(
                ref _practiceSnapshotCaptureActive,
                0);
        }
    }

    private void ApplyPerformanceTimingProfile(AppSettings settings)
    {
        var scale = Math.Clamp(settings.JudgementTimingPercent / 100d, 0.50d, 1.50d);
        lock (_practiceWorkflowSync)
        {
            _practiceWorkflow.SetPerformanceTimingProfile(PerformanceTimingProfile.FromScale(scale));
        }
    }
}
