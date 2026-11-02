using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: TaskCompletionSource lets YOU create a Task whose completion you control manually.
/// It's the bridge from callbacks/events/old APIs into async/await. This is how real I/O APIs are built:
/// the OS signals completion, someone calls SetResult.
/// </summary>
internal sealed class L11_TaskCompletionSource : Lesson
{
    public override string Title => "TaskCompletionSource, event wrapping & progress";
    public override string Summary => "Bridge callback/event APIs to Tasks, RunContinuationsAsynchronously, IProgress<T>";

    public override string Concept => """
            A TaskCompletionSource is a BUZZER YOU PRESS YOURSELF. You hand out the ticket (tcs.Task) and
            later, when something happens (a callback fires, an event is raised, a packet arrives), you press the
            button: SetResult / SetException / SetCanceled. Everything awaiting the ticket wakes up.

            Real async I/O works this way underneath: the OS signals completion; the runtime completes a Task.
            That is exactly why an await on I/O needs no thread.
            """;

    public override async Task RunAsync()
    {
        Log.Step("1. Manual promise: a Task with no code behind it");
        var tcs = new TaskCompletionSource<string>();
        Log.Info($"before: status={tcs.Task.Status}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            Log.Info("someone calls SetResult");
            tcs.SetResult("hello from TCS");
        });
        Log.Info($"await gives: {await tcs.Task}");

        Log.Step("2. Wrap a callback-based API (old-style) into a Task");
        string reply = await LegacyApi.RequestAsync("ping");
        Log.Good($"wrapped legacy API returned: {reply}");
        try { await LegacyApi.RequestAsync("bad"); }
        catch (Exception ex) { Log.Good($"error flowed through the Task: {ex.Message}"); }

        Log.Step("3. Wrap an EVENT: await 'the next time X happens'");
        var button = new FakeButton();
        Task<string> clicked = button.NextClickAsync();
        _ = Task.Run(async () => { await Task.Delay(150); button.Click("OK"); });
        Log.Info($"awaited a click event: {await clicked}");

        Log.Step("4. SetResult can run continuations INLINE on the caller's thread -> use RunContinuationsAsynchronously");
        var inlineTcs = new TaskCompletionSource();
        Task waiter = Task.Run(async () =>
        {
            await inlineTcs.Task;
            Log.Warn("continuation (no flag): runs INSIDE SetResult(), on the thread that called it, may block it or deadlock");
            Thread.Sleep(100);
        });
        await Task.Delay(50);
        Log.Info("calling SetResult…");
        inlineTcs.SetResult();
        Log.Info("…SetResult returned (took the continuation's time if inline)");
        await waiter;

        var safeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task waiter2 = Task.Run(async () =>
        {
            await safeTcs.Task;
            Log.Good("continuation (with flag): queued to the pool, SetResult returns immediately");
        });
        await Task.Delay(50);
        safeTcs.SetResult();
        await waiter2;
        Log.Note("Rule: new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) unless you know better.");

        Log.Step("5. Completion options: SetResult / SetException / SetCanceled and TrySet* (safe if racing)");
        var racy = new TaskCompletionSource<int>();
        Log.Info($"TrySetResult(1) -> {racy.TrySetResult(1)}");
        Log.Info($"TrySetResult(2) -> {racy.TrySetResult(2)}  (already completed, returns false)");
        Log.Info($"SetResult would have THROWN InvalidOperationException; value stays {racy.Task.Result}");

        Log.Step("6. IProgress<T>: report progress from async work");
        var progress = new Progress<int>(p => Log.Info($"progress callback: {p}%"));
        await DoWorkWithProgressAsync(progress);
        Log.Note("Progress<T> captures SynchronizationContext at construction, so in a UI app the callback runs on the UI thread.\n" +
                 "In a console it runs on pool threads, possibly out of order: don't assume ordering there.");

        Log.Step("Takeaways");
        Log.Note("* TaskCompletionSource = Task you complete by hand: the adapter for callbacks, events, custom signalling.\n" +
                 "* Exactly one completion wins; use TrySet* when racing.\n" +
                 "* Always pass RunContinuationsAsynchronously to avoid running unknown code inside SetResult.\n" +
                 "* IProgress<T> is the idiomatic way to report progress; don't pass UI controls into async methods.");
    }

    private static async Task DoWorkWithProgressAsync(IProgress<int> progress)
    {
        for (int i = 0; i <= 100; i += 25)
        {
            await Task.Delay(60);
            progress.Report(i);
        }
        await Task.Delay(100); // give the callbacks (posted to the pool) time to print before we move on
    }

    private static class LegacyApi
    {
        // Pretend this is an old callback API we can't change.
        private static void Request(string input, Action<string> onSuccess, Action<Exception> onError)
        {
            new Thread(() =>
            {
                Thread.Sleep(150);
                if (input == "bad") onError(new InvalidOperationException("legacy API failed"));
                else onSuccess(input.ToUpperInvariant());
            }) { IsBackground = true }.Start();
        }

        // The adapter: callback -> Task.
        public static Task<string> RequestAsync(string input)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Request(input, tcs.SetResult, tcs.SetException);
            return tcs.Task;
        }
    }

    private sealed class FakeButton
    {
        public event Action<string>? Clicked;
        public void Click(string label) => Clicked?.Invoke(label);

        public Task<string> NextClickAsync()
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(string label)
            {
                Clicked -= Handler; // unsubscribe: one-shot
                tcs.TrySetResult(label);
            }
            Clicked += Handler;
            return tcs.Task;
        }
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            What is TaskCompletionSource and when do you use it?
            """, """
            A producer-side handle for a Task whose completion you control manually. Use it to adapt callback/event/Begin-End APIs to Tasks, for custom signalling (async
            manual-reset events), and in tests to deterministically control when a task completes.
            """),
        new("""
            Why pass TaskCreationOptions.RunContinuationsAsynchronously?
            """, """
            Otherwise SetResult may run awaiting continuations synchronously on the calling thread, inside SetResult, which can block the producer, cause re-entrancy
            or deadlocks. The flag queues continuations to the pool instead.
            """),
        new("""
            SetResult vs TrySetResult?
            """, """
            SetResult throws InvalidOperationException if the task already completed; TrySetResult returns false instead. Use TrySet* whenever completion can race (timeout vs result).
            """),
        new("""
            How would you convert an event into an awaitable?
            """, """
            Create a TCS, subscribe a handler that unsubscribes itself and calls TrySetResult, return tcs.Task. Consider cancellation (register a token to TrySetCanceled and unsubscribe).
            """),
        new("""
            How does IProgress<T>/Progress<T> work?
            """, """
            Progress<T> captures SynchronizationContext at creation and posts Report callbacks to it, so UI updates run on the UI thread. Without a context they run on pool threads and may be reordered.
            """),
    ];
}
