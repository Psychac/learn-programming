using System.Globalization;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

internal sealed class L06_ExecutionContext : Lesson
{
    public override string Title => "ExecutionContext & AsyncLocal: ambient data across awaits";
    public override string Summary => "AsyncLocal vs ThreadLocal vs [ThreadStatic], copy-on-write flow, SuppressFlow, culture";

    public override string Concept => """
        Your logical flow of work carries a BACKPACK (ExecutionContext). It holds ambient data such as
        AsyncLocal<T> values, CurrentCulture, current principal. When you await or queue work, the runtime
        photocopies the backpack and hands the copy to the continuation — no matter which thread it lands on.

            ThreadLocal<T> / [ThreadStatic]  = stuff glued to a THREAD   -> lost when the flow hops threads
            AsyncLocal<T>                    = stuff glued to the FLOW   -> follows your await across threads

        It's COPY-ON-WRITE: a child may change its copy, the parent never sees it. This is how logging
        correlation IDs and ambient request context (HttpContext accessor, tracing Activity) work in ASP.NET Core.

        Don't confuse with SynchronizationContext (lesson 5): that decides WHERE to resume; the
        ExecutionContext is WHAT data comes along. ConfigureAwait(false) affects the former, never the latter.
        """;

    private static readonly AsyncLocal<string?> RequestId = new();
    private static readonly ThreadLocal<string?> ThreadValue = new();
    [ThreadStatic] private static string? _threadStatic;

    public override async Task RunAsync()
    {
        using var ctx = new SingleThreadContext("OriginThread");

        Log.Step("DEMO 1: AsyncLocal follows the flow; ThreadLocal/[ThreadStatic] stay behind with the thread");
        await ctx.InvokeAsync(async () =>
        {
            RequestId.Value = "req-123";
            ThreadValue.Value = "set on origin thread";
            _threadStatic = "set on origin thread";
            Log.Info($"before await: AsyncLocal={RequestId.Value}, ThreadLocal={ThreadValue.Value}, ThreadStatic={_threadStatic}");

            await Task.Delay(50).ConfigureAwait(false); // resume on a pool thread (not the origin thread)
            Log.Info($"after await : AsyncLocal={RequestId.Value}, ThreadLocal={ThreadValue.Value ?? "(null!)"}, ThreadStatic={_threadStatic ?? "(null!)"}");
            Log.Note("Different thread => the thread-bound values are gone. AsyncLocal survived because the ExecutionContext flowed.");

            await Task.Run(() => Log.Info($"inside Task.Run: AsyncLocal={RequestId.Value} (flows into queued work too)"));
        });

        Log.Step("DEMO 2: copy-on-write — children can't leak changes back to the parent");
        RequestId.Value = "parent";
        await ChildChangesValueAsync();
        Log.Info($"parent after child returns: {RequestId.Value}   <- unchanged!");
        Log.Note("A change made inside an async method is invisible to the caller once it returns, even though the\n" +
                 "async method ran 'in' the caller. (A non-async method that sets it DOES affect the caller.)");
        SyncChildChangesValue();
        Log.Info($"after a SYNC method set it: {RequestId.Value}   <- changed, same flow, no copy was made");

        Log.Step("DEMO 3: siblings are isolated from each other");
        await Task.WhenAll(
            Worker("job-A"), Worker("job-B"), Worker("job-C"));

        Log.Step("DEMO 4: SuppressFlow — queue work WITHOUT the backpack (perf / security isolation)");
        RequestId.Value = "secret";
        Task suppressed = RunWithoutContextFlow(() => Log.Info($"suppressed: AsyncLocal={RequestId.Value ?? "(null)"}"));
        await suppressed;
        await Task.Run(() => Log.Info($"normal    : AsyncLocal={RequestId.Value}"));

        Log.Step("DEMO 5: CurrentCulture is flowed the same way");
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        await Task.Delay(20);
        Log.Info($"after await: {CultureInfo.CurrentCulture.Name}, formatted 1234.5 -> {1234.5.ToString("N1")}");
        Log.Note("Culture, principal and AsyncLocal values all ride the ExecutionContext.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    // Keep SuppressFlow in a small SYNCHRONOUS method: the returned AsyncFlowControl must be disposed on the same
    // thread/context it was created on, which is awkward to guarantee inside an async method.
    private static Task RunWithoutContextFlow(Action action)
    {
        using (ExecutionContext.SuppressFlow())
            return Task.Run(action);
    }

    private static async Task ChildChangesValueAsync()
    {
        RequestId.Value = "changed-by-child";
        await Task.Delay(10);
        Log.Info($"child sees: {RequestId.Value}");
    }

    private static void SyncChildChangesValue() => RequestId.Value = "changed-by-sync-method";

    private static async Task Worker(string name)
    {
        RequestId.Value = name;
        await Task.Delay(30);
        Log.Info($"{name} sees its own value: {RequestId.Value}");
    }

    public override IEnumerable<QA> Interview =>
    [
        new("What is ExecutionContext and how is it different from SynchronizationContext?",
            "ExecutionContext is the ambient data bag (AsyncLocal values, culture, principal, etc.) captured and flowed across\n" +
            "awaits, Task.Run and thread-pool queueing. SynchronizationContext decides WHERE continuations run (UI thread etc.).\n" +
            "ConfigureAwait(false) skips the SynchronizationContext but still flows the ExecutionContext."),
        new("AsyncLocal vs ThreadLocal vs [ThreadStatic]?",
            "ThreadLocal/ThreadStatic are bound to a thread, which is wrong in async code because continuations may resume on a\n" +
            "different thread. AsyncLocal is bound to the logical flow and survives awaits. Typical uses: correlation IDs, tracing,\n" +
            "ambient transaction/tenant."),
        new("If a child async method sets an AsyncLocal, does the caller see it?",
            "No. Each async method gets its own copy of the ExecutionContext; changes made inside are discarded when it returns\n" +
            "(copy-on-write). Setting it in a plain synchronous method does change the caller's value."),
        new("How do you stop context from flowing, and why would you?",
            "ExecutionContext.SuppressFlow() (or UnsafeQueueUserWorkItem/UnsafeOnCompleted APIs). Used for performance or to\n" +
            "avoid leaking ambient identity/data into long-lived background work such as a timer started during a request."),
    ];
}
