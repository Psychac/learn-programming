using System.Collections.Concurrent;

namespace practice_async.Infrastructure;

/// <summary>
/// A tiny stand-in for a UI thread (WinForms/WPF/old ASP.NET): one dedicated thread that runs
/// posted work items one at a time. It installs itself as SynchronizationContext.Current on that
/// thread, so `await` captures it and resumes back on the same thread — exactly like a UI app.
/// Console apps have no SynchronizationContext, which is why lessons use this to demonstrate one.
/// </summary>
internal sealed class SingleThreadContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;

    public int ThreadId => _thread.ManagedThreadId;

    public SingleThreadContext(string name = "FakeUI")
    {
        _thread = new Thread(Pump) { Name = name, IsBackground = true };
        _thread.Start();
    }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Send is not needed for these demos.");

    /// <summary>Runs <paramref name="work"/> on the context thread; the returned task completes when it finishes.</summary>
    public Task InvokeAsync(Func<Task> work)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try
            {
                await work();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
        {
            try
            {
                callback(state);
            }
            catch (Exception ex)
            {
                // This is what a UI framework does with an exception escaping from `async void`:
                // it surfaces on the context (usually crashing the app). We just report it.
                Log.Warn($"[context] unhandled exception reached the context: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
    }
}
