using practice_async.Infrastructure;

namespace practice_async.Lessons;

internal sealed class L18_InterviewLab : Lesson
{
    public override string Title => "Interview Lab: output puzzles & build-it-yourself";
    public override string Summary => "Classic gotcha puzzles with real output, plus re-implementing WhenAll/Delay/Timeout/Throttle";

    public override string Concept => """
        Interviews test three things: (1) can you predict what code does, (2) do you know WHY, and
        (3) can you build the primitives yourself. This lesson drills all three.

        PART A: gotcha puzzles — commit to an answer first, then see the real output.
        PART B: implement the library functions you normally take for granted. If you can write
                WhenAll and Delay on a whiteboard, nobody doubts you understand tasks.
        """;

    public override async Task RunAsync()
    {
        // ---------------------------------------------------------------- PART A
        Log.Header("PART A — gotcha puzzles");

        Log.Step("PUZZLE 1: which exception does `await Task.WhenAll(slowFail, fastFail)` throw?");
        var p1 = Quiz.Predict(
            "A fails after 300ms, B fails after 50ms. Which message does the catch block see?",
            ["B (it failed first)", "A (first in the argument list)", "an AggregateException with both", "neither; WhenAll swallows errors"],
            correct: 1,
            why: "await throws only ONE exception: the first inner exception of the aggregate, ordered by the order tasks were passed, not by time. " +
                 "Both are available on whenAllTask.Exception.InnerExceptions.");
        try { await Task.WhenAll(FailAsync("A", 300), FailAsync("B", 50)); }
        catch (Exception ex) { Log.Info($"caught: {ex.GetType().Name}: {ex.Message}"); }
        p1.Reveal();

        Log.Step("PUZZLE 2: `return task` inside try/catch in a NON-async method");
        var p2 = Quiz.Predict(
            "Does the catch block run when FailAsync throws?",
            ["Yes, returns -1", "No, the caller's await throws", "No, it returns 0", "Deadlocks"],
            correct: 1,
            why: "Without async/await the method returns the (later-faulting) task immediately; the try block has already exited. " +
                 "Add `async` and `await` to make catch work. Same reason `using` + return task disposes early.");
        try { Log.Info($"result {await NotAwaitedInTry()}"); }
        catch (Exception ex) { Log.Info($"caller's await threw: {ex.Message}"); }
        Log.Info($"with async/await in the method: {await AwaitedInTry()}");
        p2.Reveal();

        Log.Step("PUZZLE 3: lazy LINQ + tasks — how many times does Fetch run?");
        var p3 = Quiz.Predict(
            "var tasks = ids.Select(Fetch); await tasks.First(); await Task.WhenAll(tasks);   // 5 ids",
            ["5", "6", "10", "1"],
            correct: 1,
            why: "Select is lazy: every enumeration RE-RUNS the lambda and starts new tasks. First() starts 1, WhenAll enumerates all 5 again... " +
                 "the first element is started twice: 1 + 5 = 6. Materialise with .ToList() / .ToArray().");
        fetchCalls = 0;
        IEnumerable<Task<int>> lazy = Enumerable.Range(1, 5).Select(Fetch);
        await lazy.First();
        await Task.WhenAll(lazy);
        Log.Info($"Fetch ran {fetchCalls} times");
        p3.Reveal();

        Log.Step("PUZZLE 4: List.ForEach(async ...) — when does 'done' print?");
        var p4 = Quiz.Predict(
            "list.ForEach(async x => { await Task.Delay(100); Log(x); }); Log(\"done\");",
            ["After all items", "Before any item", "Between items", "Exception"],
            correct: 1,
            why: "ForEach takes Action, so the async lambda compiles to async void. ForEach returns as soon as each lambda hits its first await. " +
                 "Use Task.WhenAll(list.Select(async x => ...)) or a foreach with await.");
        new List<int> { 1, 2 }.ForEach(async x => { await Task.Delay(100); Log.Info($"item {x}"); });
        Log.Info("done");
        await Task.Delay(250);
        p4.Reveal();

        Log.Step("PUZZLE 5: timing out with WhenAny does NOT stop the work");
        var p5 = Quiz.Predict(
            "We race a 500ms job against a 100ms Delay and move on when Delay wins. Does the 500ms job still finish?",
            ["No, it's cancelled", "Yes, it keeps running in the background", "It throws TimeoutException", "It is garbage collected"],
            correct: 1,
            why: "WhenAny only tells you who finished first. Nothing cancels the loser. Pass a CancellationToken into the work to stop it, " +
                 "and observe/ignore its eventual exception.");
        Task job = Task.Run(async () => { await Task.Delay(500); Log.Warn("...the 'timed out' job just finished anyway"); });
        Task winner = await Task.WhenAny(job, Task.Delay(100));
        Log.Info(winner == job ? "job won" : "timeout won; we moved on");
        await job;
        p5.Reveal();

        // ---------------------------------------------------------------- PART B
        Log.Header("PART B — build the primitives yourself");

        Log.Step("BUILD 1: Task.Delay from a Timer + TaskCompletionSource (no thread blocked)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await MyDelay(200);
        Log.Good($"MyDelay(200) took {sw.ElapsedMilliseconds}ms");

        Log.Step("BUILD 2: Task.WhenAll from scratch (counter + TCS)");
        sw.Restart();
        int[] r = await MyWhenAll([FetchValue(1, 150), FetchValue(2, 50), FetchValue(3, 100)]);
        Log.Good($"MyWhenAll -> [{string.Join(",", r)}] in {sw.ElapsedMilliseconds}ms (order preserved, time = slowest)");
        try { await MyWhenAll([FetchValue(1, 10), FailAsync2("boom", 20)]); }
        catch (Exception ex) { Log.Good($"MyWhenAll propagates failure: {ex.Message}"); }

        Log.Step("BUILD 3: a timeout that actually CANCELS the work and cleans up its timer");
        try { await WithTimeout(ct => SlowWork(ct, 1000), TimeSpan.FromMilliseconds(150)); }
        catch (TimeoutException) { Log.Good("timed out, and the inner work observed cancellation (see line above)"); }
        Log.Info($"fast work: {await WithTimeout(ct => SlowWork(ct, 50), TimeSpan.FromMilliseconds(500))}");

        Log.Step("BUILD 4: bounded parallel ForEach (worker pool pulling from a shared enumerator)");
        int running = 0, peak = 0;
        await ForEachThrottled(Enumerable.Range(1, 10), 3, async i =>
        {
            int now = Interlocked.Increment(ref running);
            int old; while (now > (old = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, old) != old) { }
            await Task.Delay(50);
            Interlocked.Decrement(ref running);
        });
        Log.Good($"10 items, limit 3 -> peak concurrency {peak}");
    }

    // ---- puzzle helpers
    private static int fetchCalls;
    private static async Task<int> Fetch(int i)
    {
        Interlocked.Increment(ref fetchCalls);
        await Task.Delay(10);
        return i;
    }

    private static async Task FailAsync(string name, int ms)
    {
        await Task.Delay(ms);
        throw new InvalidOperationException($"{name} failed");
    }

    private static Task<int> NotAwaitedInTry()
    {
        try { return FailInt(); }
        catch { return Task.FromResult(-1); }
    }

    private static async Task<int> AwaitedInTry()
    {
        try { return await FailInt(); }
        catch { return -1; }
    }

    private static async Task<int> FailInt()
    {
        await Task.Delay(10);
        throw new InvalidOperationException("FailInt");
    }

    // ---- build-it-yourself solutions
    private static Task MyDelay(int ms)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new Timer(_ => tcs.TrySetResult());       // the OS timer calls us; no thread waits meanwhile
        timer.Change(ms, Timeout.Infinite);
        tcs.Task.ContinueWith(_ => timer.Dispose(), TaskScheduler.Default);
        return tcs.Task;
    }

    private static Task<T[]> MyWhenAll<T>(IReadOnlyList<Task<T>> tasks)
    {
        if (tasks.Count == 0) return Task.FromResult(Array.Empty<T>());
        var tcs = new TaskCompletionSource<T[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        int remaining = tasks.Count;
        foreach (var t in tasks)
        {
            t.ContinueWith(_ =>
            {
                if (Interlocked.Decrement(ref remaining) != 0) return;     // last one finishing completes the aggregate
                var faults = tasks.Where(x => x.IsFaulted).SelectMany(x => x.Exception!.InnerExceptions).ToList();
                if (faults.Count > 0) tcs.SetException(faults);
                else if (tasks.Any(x => x.IsCanceled)) tcs.SetCanceled();
                else tcs.SetResult(tasks.Select(x => x.Result).ToArray()); // input order, not completion order
            }, TaskScheduler.Default);
        }
        return tcs.Task;
    }

    private static async Task<int> FetchValue(int v, int ms) { await Task.Delay(ms); return v; }
    private static async Task<int> FailAsyncInt(string name, int ms) { await FailAsync(name, ms); return 0; }
    private static Task<int> FailAsync2(string name, int ms) => FailAsyncInt(name, ms);

    private static async Task<T> WithTimeout<T>(Func<CancellationToken, Task<T>> work, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);   // cancel the WORK, not just stop waiting
        try
        {
            return await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation timed out after {timeout.TotalMilliseconds}ms");
        }
    }

    private static async Task<string> SlowWork(CancellationToken ct, int ms)
    {
        try
        {
            await Task.Delay(ms, ct);
            return "finished";
        }
        catch (OperationCanceledException)
        {
            Log.Info("SlowWork: observed cancellation and stopped");
            throw;
        }
    }

    private static Task ForEachThrottled<T>(IEnumerable<T> source, int maxConcurrency, Func<T, Task> body)
    {
        var gate = new object();
        var e = source.GetEnumerator();
        async Task Worker()
        {
            while (true)
            {
                T item;
                lock (gate)                      // enumerators aren't thread-safe; take items one at a time
                {
                    if (!e.MoveNext()) return;
                    item = e.Current;
                }
                await body(item);
            }
        }
        return Task.WhenAll(Enumerable.Range(0, maxConcurrency).Select(_ => Task.Run(Worker)))
            .ContinueWith(t => { e.Dispose(); t.GetAwaiter().GetResult(); }, TaskScheduler.Default);
    }

    public override IEnumerable<QA> Interview =>
    [
        new("Scenario: an ASP.NET Core API is slow under load but CPU is low and requests queue up. First suspect?",
            "Thread-pool starvation from sync-over-async (.Result/.Wait()/GetAwaiter().GetResult(), Thread.Sleep, blocking I/O) on\n" +
            "request threads. Confirm via dotnet-counters (ThreadPool Queue Length, Thread Count) / dotnet-stack. Fix: async all\n" +
            "the way, remove blocking, bound outgoing concurrency."),
        new("How do you call async code from Main, a constructor, Dispose, or an event handler?",
            "Main: `static async Task Main`. Constructor: you can't await; use an async factory `static async Task<T> CreateAsync()`\n" +
            "with a private ctor (or lazy init). Dispose: implement IAsyncDisposable and `await using`. Event handlers: async void\n" +
            "is acceptable there, with try/catch inside."),
        new("Design: download 1000 URLs with max 10 in flight, per-request timeout, retries and overall cancel.",
            "Parallel.ForEachAsync with MaxDegreeOfParallelism=10 (or SemaphoreSlim/Channel workers); a linked CTS = overall token +\n" +
            "CancelAfter(perRequestTimeout) per attempt; retry with exponential backoff + jitter only on transient errors; collect\n" +
            "results/errors in a thread-safe collection; flow the token everywhere; don't create 1000 tasks that all start at once."),
        new("How do you unit-test async code?",
            "Make tests `async Task` and await the SUT. Don't Thread.Sleep. Control time via TimeProvider (Task.Delay(delay, provider)\n" +
            "and FakeTimeProvider) and control completion via TaskCompletionSource to test ordering, cancellation and failure paths.\n" +
            "Assert exceptions with Assert.ThrowsAsync."),
        new("Is Task.WhenAll parallel?",
            "WhenAll itself just waits. Concurrency comes from the tasks already having been STARTED. For I/O they overlap without\n" +
            "extra threads; for CPU work you need Task.Run/Parallel so there are threads/cores to run them."),
        new("Why shouldn't you wrap request work in Task.Run on ASP.NET Core?",
            "You're already on a pool thread. Task.Run just queues another hop (latency, allocation) and frees nothing. It only makes\n" +
            "sense for CPU work you want to run concurrently with other request work, and even then consider a background queue."),
        new("What's the difference between `Task.WhenAny` timeout and a CancellationToken timeout?",
            "WhenAny stops WAITING; the work continues unless it was given a token. CancellationTokenSource.CancelAfter signals the\n" +
            "work to stop cooperatively. Best: use both, or Task.WaitAsync(timeout, token) plus a token passed to the work."),
        new("Why is a lazily-evaluated LINQ query of tasks dangerous?",
            "Each enumeration re-invokes the lambda, starting new operations. Materialise with ToList()/ToArray() before awaiting or\n" +
            "passing to WhenAll."),
    ];
}
