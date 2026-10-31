namespace ShoutingIguana.Engine.Crawling;

/// <summary>An async gate that workers pass through before each request. Pausing lets in-flight requests finish.</summary>
internal sealed class PauseGate
{
    private readonly Lock _sync = new();
    private TaskCompletionSource _open = CreateOpen();

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return !_open.Task.IsCompleted;
            }
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_open.Task.IsCompleted)
            {
                _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _open.TrySetResult();
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken)
    {
        Task gate;
        lock (_sync)
        {
            gate = _open.Task;
        }

        return gate.IsCompleted ? Task.CompletedTask : gate.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateOpen()
    {
        var open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        open.SetResult();
        return open;
    }
}
