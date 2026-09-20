namespace PianoPracticeTool.Services.Practice;

public sealed class PracticePlaybackScheduler : IDisposable
{
    private const int PulseIntervalMilliseconds = 5;

    private readonly Action _pulse;
    private readonly ManualResetEvent _stopEvent = new(initialState: false);
    private readonly object _syncRoot = new();
    private Thread? _thread;
    private bool _disposed;

    public PracticePlaybackScheduler(Action pulse)
    {
        _pulse = pulse ?? throw new ArgumentNullException(nameof(pulse));
    }

    public event EventHandler<PracticePlaybackSchedulerErrorEventArgs>? SchedulerError;

    public void Start()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PracticePlaybackScheduler));
            }

            if (_thread is not null)
            {
                return;
            }

            _stopEvent.Reset();
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "PianoPracticePlayback",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_syncRoot)
        {
            thread = _thread;
            if (thread is null)
            {
                return;
            }

            _stopEvent.Set();
            _thread = null;
        }

        if (!ReferenceEquals(Thread.CurrentThread, thread))
        {
            thread.Join();
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        _stopEvent.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Run()
    {
        while (!_stopEvent.WaitOne(PulseIntervalMilliseconds))
        {
            try
            {
                _pulse();
            }
            catch (Exception ex)
            {
                SchedulerError?.Invoke(
                    this,
                    new PracticePlaybackSchedulerErrorEventArgs(ex));
                return;
            }
        }
    }
}

public sealed class PracticePlaybackSchedulerErrorEventArgs : EventArgs
{
    public PracticePlaybackSchedulerErrorEventArgs(Exception exception)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    public Exception Exception { get; }
}
