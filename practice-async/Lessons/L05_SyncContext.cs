using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: when you `await`, the runtime captures SynchronizationContext.Current (or, failing that,
/// TaskScheduler.Current) and resumes the method THERE. UI apps have a context that means "the UI thread";
/// console apps and ASP.NET Core have none, so continuations run on any pool thread.
/// ConfigureAwait(false) = "I don't care where I resume; don't go back to the captured context".
/// </summary>
internal sealed class L05_SyncContext : Lesson
{
    public override string Title => "SynchronizationContext & ConfigureAwait";
    public override string Summary => "Where does code resume after await? Simulated UI thread demo";

    public override string Concept => """
            A SynchronizationContext is a RETURN ADDRESS: "when my work is done, call me back HERE".
            Hotel analogy: you leave a message at the front desk asking for a call back to ROOM 12 (the UI thread),
            because only room 12 has your laptop (UI controls). A console app or ASP.NET Core leaves no return
            address: any free staff member may call you back.

              await x                       -> "call me back at my return address (if any)"
              await x.ConfigureAwait(false) -> "any room will do; I don't need my laptop"

            UI frameworks have a context (one thread, one message queue). Classic ASP.NET had one too (request context).
            ASP.NET Core and console apps have NONE, so continuations run on the thread pool.
            """;

    public override async Task RunAsync()
    {
        Log.Step("1. Console app: no SynchronizationContext, so resumption lands on ANY pool thread");
        Log.Info($"Context here: {SynchronizationContext.Current?.GetType().Name ?? "null"}");
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(50);
            Log.Info($"after await #{i}");
        }

        Log.Step("2. Simulated UI thread: await resumes ON THE SAME THREAD every time");
        using var ui = new SingleThreadContext("FakeUI");
        Log.Info($"(the fake UI thread id is T{ui.ThreadId})");
        await ui.InvokeAsync(async () =>
        {
            Log.Info("UI: start");
            await Task.Delay(100);
            Log.Good("UI: after await  -> back on the UI thread (context captured)");
            await Task.Delay(100);
            Log.Good("UI: after 2nd await -> again the UI thread");
        });

        Log.Step("3. ConfigureAwait(false): resume wherever; don't hop back to the UI thread");
        await ui.InvokeAsync(async () =>
        {
            Log.Info("UI: start");
            await Task.Delay(100).ConfigureAwait(false);
            Log.Warn("UI: after ConfigureAwait(false) -> now on a POOL thread (touching UI controls here would crash in WPF/WinForms)");
        });

        Log.Step("4. A realistic library method: every await uses ConfigureAwait(false)");
        await ui.InvokeAsync(async () =>
        {
            Log.Info("UI: calling library");
            string data = await LibraryCode.LoadAsync();
            Log.Good($"UI: library returned '{data}' — and we're back on the UI thread because THIS await has no ConfigureAwait(false)");
        });

        Log.Step("Takeaways / rules of thumb");
        Log.Note("* App code (UI event handlers): plain `await` — you WANT to return to the UI thread.\n" +
                 "* Library code: `await x.ConfigureAwait(false)` so you don't force callers' contexts and avoid deadlocks (lesson 7).\n" +
                 "* ASP.NET Core and console apps have no SynchronizationContext, so ConfigureAwait(false) changes nothing there\n" +
                 "  (but it is still the convention in reusable libraries).\n" +
                 "* ConfigureAwait only affects the code AFTER that await, per await. Apply it to every await in the method.\n" +
                 "* .NET 8+: ConfigureAwaitOptions (e.g. SuppressThrowing, ForceYielding) offers finer control.");
    }

    private static class LibraryCode
    {
        public static async Task<string> LoadAsync()
        {
            Log.Info("library: start");
            await Task.Delay(100).ConfigureAwait(false);
            Log.Info("library: after await (pool thread, context ignored)");
            await Task.Delay(50).ConfigureAwait(false);
            return "payload";
        }
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            What does `await` capture, and where does the continuation run?
            """, """
            It captures SynchronizationContext.Current if non-null, otherwise TaskScheduler.Current (usually the default pool scheduler).
            The continuation is posted there. With neither, it runs on a thread-pool thread (or inline if the task completed already).
            """),
        new("""
            What does ConfigureAwait(false) do and when should you use it?
            """, """
            It tells the awaiter not to marshal back to the captured context. Use it in library/general-purpose code that doesn't touch
            UI/request-context state: avoids deadlocks and context-switch overhead. In app code that needs the UI thread, don't use it.
            """),
        new("""
            Is ConfigureAwait(false) needed in ASP.NET Core?
            """, """
            There is no SynchronizationContext, so it changes nothing functionally there, but libraries still use it by convention since
            they can't know who calls them (UI app, legacy ASP.NET).
            """),
        new("""
            Does ConfigureAwait(false) affect the rest of the method?
            """, """
            Only that await, and only for the code that follows until the next await. Each await needs its own. It doesn't stop the
            ExecutionContext (AsyncLocal) from flowing. If the task is already complete, it has no effect (you stay on the thread).
            """),
        new("""
            What are ConfigureAwaitOptions (.NET 8)?
            """, """
            A flags enum for Task.ConfigureAwait(options): ContinueOnCapturedContext, SuppressThrowing (don't throw on fault/cancel), and
            ForceYielding (always yield even if complete). Example: await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).
            """),
    ];
}
