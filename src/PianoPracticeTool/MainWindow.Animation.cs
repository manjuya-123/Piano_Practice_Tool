using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

namespace PianoPracticeTool;

public sealed partial class MainWindow
{
    private bool _isAnimationRenderingSubscribed;
    private int _presentationUiFlushPending;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_isAnimationRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += CompositionTarget_Rendering;
        _isAnimationRenderingSubscribed = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isAnimationRenderingSubscribed)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isAnimationRenderingSubscribed = false;
        }

        base.OnClosed(e);
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (!ReferenceEquals(PageHost.Content, _performanceView))
        {
            return;
        }

        var transport = _practiceWorkflow.GetTransportPosition();
        _performanceView.SetAnimationPosition(transport.State, transport.Beat);
    }

    private void RequestPresentationUiFlush()
    {
        if (_disposed
            || Interlocked.Exchange(
                ref _presentationUiFlushPending,
                1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                Interlocked.Exchange(
                    ref _presentationUiFlushPending,
                    0);
                if (_disposed)
                {
                    return;
                }

                FlushPendingPresentationUi();
            }));
    }

    private void FlushPendingPresentationUi()
    {
        var version = Volatile.Read(ref _presentationUiVersion);
        if (version == _appliedPresentationUiVersion)
        {
            return;
        }

        _performanceView.SetActiveMidiNotes(_activeMidiNotes.Keys.ToArray());

        var inputText = Interlocked.Exchange(ref _pendingMidiInputText, null);
        if (inputText is not null)
        {
            MidiInputMonitorText.Text = inputText;
        }

        var snapshot = Interlocked.Exchange(ref _pendingPracticeSnapshot, null);
        if (snapshot is not null)
        {
            ApplyPracticeSnapshot(snapshot);
        }

        _appliedPresentationUiVersion = version;
    }
}
