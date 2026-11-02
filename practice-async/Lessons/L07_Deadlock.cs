using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: the classic deadlock. A thread with a SynchronizationContext blocks (.Result / .Wait()) on
/// an async method. The async method's continuation needs that very thread to resume -> neither can proceed.
/// "Async all the way": never block on async code.
/// </summary>
internal sealed class L07_Deadlock : Lesson
{
    public override string Title => "Deadlock: why .Result / .Wait() is dangerous";
    public override string Summary => "Reproduce the UI-thread deadlock safely (with a timeout) and the ways out";

    public override string Concept => """
            Two people in a narrow doorway, each politely waiting for the other to go first.

              The UI thread says: "I'll keep the room until your result is ready" (.Result / .Wait()).
              The async method says: "when my timer fires I need the ROOM (UI thread) to finish." (captured context)

            Neither can move. The fix is never to hold the room while waiting: AWAIT (lets the room be used),
            or ensure the async code doesn't need the room (ConfigureAwait(false)).
            """;

    public override async Task RunAsync()
    {
        using var ui = new SingleThreadContext("FakeUI");

        Log.Step("1. Blocking on async code from the 'UI thread' -> deadlock (detected via a 1s timeout)");
        await ui.InvokeAsync(() =>
        {
            Task<string> t = GetDataAsync();                  // starts, hits await, captures the UI context
            bool finished = t.Wait(TimeSpan.FromSeconds(1));  // UI thread now BLOCKED waiting for t
            if (!finished)
                Log.Warn("DEADLOCK: t never finished. The continuation is queued to the UI thread, which is stuck in Wait().");
            return Task.CompletedTask;
        });
        // Let the stuck continuation run now that the UI thread is free again, to tidy up.
        await Task.Delay(500);

        Log.Step("2. Fix A: 'async all the way' — await instead of block");
        await ui.InvokeAsync(async () =>
        {
            string s = await GetDataAsync();
            Log.Good($"UI: awaited result '{s}' with no deadlock");
        });

        Log.Step("3. Fix B: library uses ConfigureAwait(false) so it never needs the blocked thread");
        await ui.InvokeAsync(() =>
        {
            Task<string> t = GetDataNoContextAsync();
            bool finished = t.Wait(TimeSpan.FromSeconds(1));
            Log.Good($"Blocking Wait() finished={finished}, result='{t.Result}'  (works, but blocks the UI thread: still bad UX)");
            return Task.CompletedTask;
        });

        Log.Step("4. Fix C (last resort): offload to the pool first");
        await ui.InvokeAsync(() =>
        {
            string s = Task.Run(GetDataAsync).GetAwaiter().GetResult();
            Log.Good($"Task.Run(...).GetAwaiter().GetResult() => '{s}' (no context on the pool thread, so no deadlock)");
            return Task.CompletedTask;
        });

        Log.Step("Takeaways");
        Log.Note("* Rule: don't block on tasks (.Result, .Wait(), .GetAwaiter().GetResult()) in code that might have a context.\n" +
                 "* Blocking also wastes the thread and can starve the ThreadPool under load ('sync over async').\n" +
                 "* .Result/.Wait wrap errors in AggregateException; GetAwaiter().GetResult() rethrows the original.\n" +
                 "* Console apps don't deadlock this way (no context), but libraries can't know who will call them.\n" +
                 "* The mirror anti-pattern is 'async over sync' (Task.Run around blocking code to fake async) — see lesson 16.");
    }

    private static async Task<string> GetDataAsync()
    {
        await Task.Delay(100);  // captures the context -> wants to resume on the UI thread
        return "data";
    }

    private static async Task<string> GetDataNoContextAsync()
    {
        await Task.Delay(100).ConfigureAwait(false);
        return "data";
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            Explain the classic async deadlock.
            """, """
            A thread with a single-threaded SynchronizationContext (UI, legacy ASP.NET) calls .Result/.Wait() on an async method. The method's
            await captured that context, so its continuation is posted to the blocked thread. The thread waits for the task; the task waits for the
            thread. Fixes: await instead, ConfigureAwait(false) in the callee, or run on the pool (Task.Run).
            """),
        new("""
            Why doesn't the same code deadlock in a console app or ASP.NET Core?
            """, """
            They have no SynchronizationContext, so the continuation runs on a pool thread and the blocked thread isn't needed. It still wastes a thread
            and risks starvation, so it's still wrong.
            """),
        new("""
            What is 'sync over async' and why is it bad?
            """, """
            Blocking on async code (.Result, .Wait(), GetAwaiter().GetResult()). It can deadlock, ties up threads and causes thread-pool starvation under load.
            Prefer async all the way; if you must bridge, do it once at the outermost layer.
            """),
        new("""
            Difference between .Result/.Wait() and GetAwaiter().GetResult() on failure?
            """, """
            Result/Wait wrap the exception in AggregateException; GetAwaiter().GetResult() rethrows the original exception (like await), preserving type and stack trace.
            """),
    ];
}
