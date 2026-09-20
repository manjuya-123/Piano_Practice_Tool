using System.Diagnostics;

namespace PianoPracticeTool.Services.Practice;

internal sealed class PracticeRunClock
{
    private readonly Stopwatch _stopwatch = new();
    private double _accumulatedSeconds;

    public double ElapsedSeconds => _accumulatedSeconds
        + (_stopwatch.IsRunning ? _stopwatch.Elapsed.TotalSeconds : 0d);

    public bool IsRunning => _stopwatch.IsRunning;

    public void Restart()
    {
        _accumulatedSeconds = 0d;
        _stopwatch.Restart();
    }

    public void Resume()
    {
        if (_stopwatch.IsRunning)
        {
            return;
        }

        _stopwatch.Restart();
    }

    public void Pause()
    {
        if (!_stopwatch.IsRunning)
        {
            return;
        }

        _accumulatedSeconds += _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Reset();
    }

    public void Reset()
    {
        _accumulatedSeconds = 0d;
        _stopwatch.Reset();
    }
}
