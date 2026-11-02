using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>Common bugs, each demonstrated, then fixed.</summary>
internal sealed class L17_Pitfalls : Lesson
{
    public override string Title => "Pitfalls & best practices";
    public override string Summary => "async void, forgotten await, shared-state races across awaits, return-vs-await with using, fire-and-forget";

    public override string Concept => """
            A field guide of the bugs that actually hit production. For each: see the symptom, learn the cause,
            then the one-line fix. Keep this list in your head during code review. Interviewers ask
            "what can go wrong here?" about almost any async snippet.
            """;

    private int _balance;

    public override async Task RunAsync()
    {
        Log.Step("1. async void: errors can't be caught by the caller");
        using (var ui = new SingleThreadContext("FakeUI"))
        {
            await ui.InvokeAsync(async () =>
            {
                try
                {
                    BadAsyncVoid();
                    Log.Info("try block finished: no exception here (it hasn't been thrown yet)");
                }
                catch (Exception)
                {
                    Log.Good("never reached");
                }
                await Task.Delay(300); // the async void exception is posted to the context and appears below
            });
        }
        Log.Note("async void exceptions are re-thrown on the SynchronizationContext (crashing a UI app) or the pool (crashing the process).\n" +
                 "Only use async void for event handlers. Everything else: async Task.");

        Log.Step("2. Forgotten await: the next line runs BEFORE the work is done");
        var order = new List<string>();
        Task forgot = AddLaterAsync(order, "late");
        order.Add("immediately after call");
        await forgot;
        Log.Info($"order: {string.Join(" -> ", order)}");
        Log.Note("The compiler warns CS4014 for exactly this. If you really mean fire-and-forget, write `_ = DoAsync();` (see 6).");

        Log.Step("3. Loop variable capture with a shared variable");
        var captured = new List<Task<int>>();
        for (int i = 0; i < 3; i++)
            captured.Add(Task.Run(async () => { await Task.Delay(50); return i; })); // 'i' is ONE variable for the whole loop
        Log.Warn($"for-loop capture : {string.Join(",", await Task.WhenAll(captured))}   <- all see the final i");
        captured.Clear();
        for (int i = 0; i < 3; i++)
        {
            int copy = i;
            captured.Add(Task.Run(async () => { await Task.Delay(50); return copy; }));
        }
        Log.Good($"copy per iteration: {string.Join(",", await Task.WhenAll(captured))}");
        Log.Note("(foreach creates a fresh variable per iteration, so it is safe.)");

        Log.Step("4. Shared state across an await is NOT atomic: lost updates");
        _balance = 0;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => UnsafeIncrementAsync()));
        Log.Warn($"unsafe: expected 10, got {_balance}");
        _balance = 0;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => SafeIncrementAsync()));
        Log.Good($"with SemaphoreSlim(1,1): expected 10, got {_balance}");
        Log.Note("`lock` can't contain `await` (compile error CS1996). Use SemaphoreSlim(1,1) as an async mutex,\n" +
                 "or avoid shared mutable state (Channels, immutable data, Interlocked for simple counters).");

        Log.Step("5. Returning a Task from inside using/try without await = disposed too early");
        try { await ReadBadAsync(); }
        catch (ObjectDisposedException ex) { Log.Warn($"BUG: {ex.Message}"); }
        string good = await ReadGoodAsync();
        Log.Good($"fixed with async/await: {good}");
        Log.Note("Eliding async/await is only safe when there's no using/try/catch/finally around the call.");

        Log.Step("6. Fire-and-forget done safely");
        _ = FireAndForgetSafeAsync(() => ThrowLaterAsync());
        await Task.Delay(200);
        Log.Note("Unobserved exceptions in a plain `_ = DoAsync();` vanish. Wrap with a handler (or use a queue/BackgroundService).");

        Log.Step("7. Task.Run is not a free async: don't wrap sync code to 'make it async' in libraries");
        Log.Note("Task.Run(() => File.ReadAllText(...)) just moves the blocked thread elsewhere. Use real async APIs\n" +
                 "(File.ReadAllTextAsync). Let CALLERS decide to Task.Run CPU-bound work.");

        Log.Step("8. Other rules of thumb");
        Log.Note("* Name async methods ...Async. Return Task/Task<T>, never void (except event handlers).\n" +
                 "* Flow CancellationToken everywhere.\n" +
                 "* Don't block: no .Result/.Wait()/Thread.Sleep in async code (use await/Task.Delay).\n" +
                 "* Use ConfigureAwait(false) in library code.\n" +
                 "* Await every task. Prefer WhenAll over manual loops of await when you want concurrency.\n" +
                 "* Avoid async lambdas passed to Action parameters (e.g. List.ForEach): that creates async void.\n" +
                 "* Don't use Task.Factory.StartNew / ContinueWith where Task.Run / await will do.");
    }

    private static async void BadAsyncVoid()
    {
        await Task.Delay(50);
        throw new InvalidOperationException("thrown from async void");
    }

    private static async Task AddLaterAsync(List<string> list, string item)
    {
        await Task.Delay(100);
        list.Add(item);
    }

    private async Task UnsafeIncrementAsync()
    {
        int tmp = _balance;
        await Task.Delay(20);   // others read the same old value in the meantime
        _balance = tmp + 1;
    }

    private readonly SemaphoreSlim _mutex = new(1, 1);

    private async Task SafeIncrementAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            int tmp = _balance;
            await Task.Delay(20);
            _balance = tmp + 1;
        }
        finally { _mutex.Release(); }
    }

    private sealed class Resource : IDisposable
    {
        private bool _disposed;
        public async Task<string> ReadAsync()
        {
            await Task.Delay(100);
            ObjectDisposedException.ThrowIf(_disposed, this);
            return "resource data";
        }
        public void Dispose() => _disposed = true;
    }

    private static Task<string> ReadBadAsync()
    {
        using var r = new Resource();
        return r.ReadAsync(); // returns immediately; 'using' disposes r before ReadAsync finishes
    }

    private static async Task<string> ReadGoodAsync()
    {
        using var r = new Resource();
        return await r.ReadAsync();
    }

    private static async Task ThrowLaterAsync()
    {
        await Task.Delay(50);
        throw new InvalidOperationException("background failure");
    }

    private static async Task FireAndForgetSafeAsync(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { Log.Good($"fire-and-forget handler observed: {ex.Message}"); }
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            List common async/await mistakes.
            """, """
            async void; blocking (.Result/.Wait); forgetting await (lost errors/ordering); await in a loop when concurrency was intended; unbounded concurrency; not passing CancellationToken;
            shared state across awaits without sync; returning a task from inside using/try; async lambdas in Action parameters; capturing the for-loop variable; Task.Run in libraries; ConfigureAwait omissions in libraries.
            """),
        new("""
            What's the problem with `using var r = ...; return r.DoAsync();` (no await)?
            """, """
            The using disposes r as soon as the method returns the not-yet-finished task, so the operation runs against a disposed object. Make the method async and await.
            """),
        new("""
            When can you elide async/await (return the Task directly)?
            """, """
            When the method just forwards a single task and has no using/try/catch/finally or post-processing. It saves a state machine but changes exception timing (sync throws escape) and stack traces.
            """),
        new("""
            How do you do safe fire-and-forget?
            """, """
            Prefer queueing to a background worker (Channel + BackgroundService). If you must: wrap in a method that try/catches and logs, call as `_ = RunAsync();`. In ASP.NET Core don't use request-scoped services in it.
            """),
        new("""
            Why is capturing a `for` loop variable in a task lambda a bug but `foreach` is fine?
            """, """
            A for-loop variable is one variable shared by all iterations, so tasks started in the loop see its final value. foreach declares a fresh variable per iteration (C# 5+). Copy to a local in for loops.
            """),
    ];
}
