using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: an exception thrown inside an async Task method does NOT propagate to the caller of the method.
/// It is stored in the returned Task (Faulted). It is rethrown when you `await` that Task.
/// </summary>
internal sealed class L08_Exceptions : Lesson
{
    public override string Title => "Exception handling in async code";
    public override string Summary => "Faulted tasks, await unwrapping, WhenAll with many errors, AggregateException, finally";

    public override string Concept => """
            An async method is a delivery service: if the kitchen catches fire, the DRIVER doesn't crash into your
            house when he leaves; he arrives with a note saying "the order failed". The failure travels INSIDE the
            Task (the receipt). It explodes where you OPEN the receipt: at `await`.

              * Not awaiting = never opening the receipt = the error is silently lost.
              * Many failing tasks = many notes; `await` shows you the first, the Task keeps them all.
            """;

    public override async Task RunAsync()
    {
        Log.Step("1. The exception is captured in the Task, not thrown at the call site");
        Task t = FailAfterAsync(50);
        Log.Info($"Called FailAfterAsync, no exception yet. Status={t.Status}");
        try
        {
            await t;
        }
        catch (InvalidOperationException ex)
        {
            Log.Good($"Caught at await: {ex.GetType().Name}: {ex.Message}");
        }
        Log.Info($"Status now: {t.Status}");

        Log.Step("2. Even a throw BEFORE the first await is captured (async methods never throw synchronously)");
        Task t2 = ThrowImmediatelyAsync();
        Log.Info($"No exception at call site; t2.Status={t2.Status}");
        try { await t2; } catch (Exception ex) { Log.Good($"Caught at await: {ex.Message}"); }

        Log.Step("3. Contrast: a NON-async Task-returning method can throw synchronously");
        try
        {
            Task t3 = ValidateThenRunNonAsync(null!);
            Log.Info("not reached");
            await t3;
        }
        catch (ArgumentNullException ex)
        {
            Log.Good($"Thrown synchronously at the call: {ex.ParamName}");
        }
        Log.Note("Guard clauses in non-async wrappers throw immediately (good for argument bugs).\n" +
                 "In async methods the same guard becomes a faulted Task — surprising to callers who forgot to await.");

        Log.Step("4. Forgetting to await => the error goes unnoticed");
        Task ignored = FailAfterAsync(10);
        await Task.Delay(100);
        Log.Warn($"We never awaited it; task is {ignored.Status} but nothing crashed. The error is silently lost!");
        Log.Note("(If a faulted task is garbage-collected unobserved, TaskScheduler.UnobservedTaskException fires; it no longer crashes the process.)");
        _ = ignored.Exception; // observing it marks it handled

        Log.Step("5. Many failures: Task.WhenAll — await rethrows only the FIRST, but ALL are on task.Exception");
        Task all = Task.WhenAll(FailAfterAsync(30, "A"), FailAfterAsync(60, "B"), Task.Delay(10));
        try
        {
            await all;
        }
        catch (Exception ex)
        {
            Log.Info($"await threw only: {ex.Message}");
            AggregateException agg = all.Exception!;
            foreach (var inner in agg.InnerExceptions)
                Log.Info($"  inner exception: {inner.Message}");
        }

        Log.Step("6. .Result / .Wait() wrap in AggregateException; await unwraps");
        try { FailAfterAsync(10).Wait(); }
        catch (AggregateException ex) { Log.Info($".Result threw {ex.GetType().Name} -> inner {ex.InnerException!.GetType().Name}"); }
        try { await FailAfterAsync(10); }
        catch (InvalidOperationException ex) { Log.Info($"await threw {ex.GetType().Name} directly"); }

        Log.Step("7. try/catch/finally behaves exactly as in sync code, and awaits are allowed inside catch/finally");
        try
        {
            await FailAfterAsync(10);
        }
        catch (InvalidOperationException)
        {
            await Task.Delay(20);
            Log.Info("catch: did async cleanup/logging");
        }
        finally
        {
            await Task.Delay(20);
            Log.Info("finally: async cleanup done");
        }

        Log.Step("8. Handling per-task results without throwing: inspect the tasks");
        Task<int>[] tasks = [OkAsync(1), BadAsync(2), OkAsync(3)];
        try { await Task.WhenAll(tasks); } catch { /* we inspect individually below */ }
        foreach (var task in tasks)
            Log.Info(task.IsFaulted ? $"faulted: {task.Exception!.InnerException!.Message}" : $"ok: {task.Result}");

        Log.Step("Takeaways");
        Log.Note("* Faults live in the Task; they surface where you await.\n" +
                 "* Always await (or otherwise observe) every Task you create.\n" +
                 "* WhenAll: inspect task.Exception for all errors; await shows the first.\n" +
                 "* Prefer await over .Result/.Wait so you get the real exception type.");
    }

    private static async Task FailAfterAsync(int ms, string label = "x")
    {
        await Task.Delay(ms);
        throw new InvalidOperationException($"failure {label} after {ms}ms");
    }

    private static async Task ThrowImmediatelyAsync()
    {
        if (DateTime.Now.Year > 0) throw new InvalidOperationException("thrown before any await");
        await Task.Delay(1);
    }

    private static Task ValidateThenRunNonAsync(string? arg)
    {
        ArgumentNullException.ThrowIfNull(arg);
        return Task.Delay(10);
    }

    private static async Task<int> OkAsync(int n) { await Task.Delay(10); return n; }
    private static async Task<int> BadAsync(int n) { await Task.Delay(10); throw new InvalidOperationException($"bad {n}"); }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            Where is an exception thrown from an async Task method observed?
            """, """
            It's captured in the returned Task (status Faulted) and rethrown when you await it (or .Result/.Wait()). It never propagates synchronously to the call site,
            even if thrown before the first await. If nobody awaits or inspects the task, it's silently lost.
            """),
        new("""
            What happens when several tasks in Task.WhenAll fail?
            """, """
            The WhenAll task faults with an AggregateException holding all of them; `await` rethrows just the first inner exception (by input order), but task.Exception
            exposes all. To handle each, inspect the individual tasks.
            """),
        new("""
            async void: why avoid it?
            """, """
            No Task is returned, so callers can't await, catch exceptions or know when it finishes. Exceptions are rethrown on the SynchronizationContext (or thread pool),
            typically crashing the process. Only for event handlers; wrap the body in try/catch.
            """),
        new("""
            What are unobserved task exceptions?
            """, """
            A faulted Task whose exception was never observed. When it's finalized, TaskScheduler.UnobservedTaskException is raised. Since .NET 4.5 it no longer
            terminates the process by default, but it indicates a bug (a missing await).
            """),
        new("""
            Can you await inside catch/finally?
            """, """
            Yes (since C# 6). The compiler stores the exception and runs the awaits correctly. Useful for async cleanup/logging.
            """),
        new("""
            How do you preserve the stack trace when rethrowing?
            """, """
            Use `throw;` (not `throw ex;`) in catch, and ExceptionDispatchInfo.Capture(ex).Throw() if rethrowing later or on another thread.
            """),
    ];
}
