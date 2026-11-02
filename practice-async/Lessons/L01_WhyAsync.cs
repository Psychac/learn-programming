using practice_async.Infrastructure;

namespace practice_async.Lessons;

internal sealed class L01_WhyAsync : Lesson
{
    public override string Title => "The mental model: why async exists";
    public override string Summary => "Blocked vs waiting threads, concurrency vs parallelism, async != threads";

    public override string Concept => """
        Picture a restaurant. A THREAD is a waiter. The KITCHEN is anything slow outside your code
        (disk, network, database, a timer). You are writing the waiter's instructions.

          BLOCKING code : the waiter takes an order, walks to the kitchen and STARES at the pass
                          until the dish is ready. He can serve nobody else. 10 tables = 10 waiters.
          ASYNC code    : the waiter takes the order, hands it to the kitchen and walks away to serve
                          other tables. When the kitchen rings the bell, ANY free waiter delivers the dish.

        `await` is the moment the waiter hands the order over and walks away.
        The code AFTER the await is "deliver the dish" — it runs later, on whichever waiter is free.

        Nobody is faster at cooking. Async doesn't make a single request quicker; it lets a few
        waiters serve thousands of tables because waiting costs nothing.
        """;

    public override async Task RunAsync()
    {
        Log.Step("DEMO 1: one waiter (ONE thread), three tables, each dish takes 300ms");
        using var oneWaiter = new SingleThreadContext("Waiter");

        var blocking = new Timeline();
        await oneWaiter.InvokeAsync(() =>
        {
            for (int table = 1; table <= 3; table++)
                blocking.Block($"table {table}", 300);   // stares at the kitchen: thread held
            return Task.CompletedTask;
        });
        Log.Info("BLOCKING waiter (Thread.Sleep): tables are served one after another");
        blocking.Render();

        var asyncTl = new Timeline();
        await oneWaiter.InvokeAsync(async () =>
        {
            // Start all three orders, THEN wait for all of them. Still just one thread.
            await Task.WhenAll(Enumerable.Range(1, 3).Select(t => asyncTl.WaitAsync($"table {t}", 300)));
        });
        Log.Info("ASYNC waiter (await Task.Delay): the SAME single thread serves all three at once");
        asyncTl.Render();
        Log.Note("Same thread, same 300ms 'cooking' — 900ms vs ~300ms. The ▓ blocks are a thread doing nothing;\n" +
                 "the ░ blocks cost nothing because no thread is attached to them.");

        var p = Quiz.Predict(
            "2000 requests each `await Task.Delay(500)` at the same time. Roughly how long, and how many extra threads?",
            [
                "~1000ms, ~2000 threads (one per request)",
                "~500ms, about the same number of threads as before",
                "~500ms, ~2000 threads",
                "~16 minutes (500ms x 2000 / cores)",
            ],
            correct: 1,
            why: "Awaiting a timer holds no thread. The OS timer fires; a pool thread runs the continuation. All 2000 overlap.");
        int before = ThreadPool.ThreadCount;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 2000).Select(_ => Task.Delay(500)));
        Log.Good($"2000 concurrent awaits: {sw.ElapsedMilliseconds}ms, pool threads {before} -> {ThreadPool.ThreadCount}");
        p.Reveal();

        Log.Step("DEMO 2: concurrency vs parallelism (the words interviewers love to mix up)");
        Log.Note("CONCURRENCY = several things IN PROGRESS at once (can be interleaved on one thread).\n" +
                 "PARALLELISM = several things EXECUTING at the same instant (needs several cores).\n" +
                 "ASYNC       = a way to get concurrency of WAITING without dedicating threads.");

        var cpuSerial = new Timeline();
        await Task.Run(() => { cpuSerial.Work("job A", 200); cpuSerial.Work("job B", 200); });
        Log.Info("Two CPU jobs on ONE thread (no parallelism, no concurrency):");
        cpuSerial.Render();

        var cpuParallel = new Timeline();
        await Task.WhenAll(Task.Run(() => cpuParallel.Work("job A", 200)), Task.Run(() => cpuParallel.Work("job B", 200)));
        Log.Info($"Two CPU jobs on TWO threads (parallelism; {Environment.ProcessorCount} cores available):");
        cpuParallel.Render();
        Log.Note("await can't speed up the █ blocks: CPU work needs a core. It only removes the ▓ (wasted) blocks.");

        Log.Step("DEMO 3: which tool for which problem?");
        Log.Note("""
            waiting for network/disk/DB/timer  (I/O-bound) -> async/await, no extra threads
            computing hashes/images/parsing    (CPU-bound) -> Task.Run / Parallel.For (needs threads/cores)
            many items, each needs I/O         -> async + bounded concurrency (SemaphoreSlim, Parallel.ForEachAsync)
            long-lived producer/consumer       -> Channel<T>
            """);
    }

    public override IEnumerable<QA> Interview =>
    [
        new("What problem does async/await solve?",
            "It lets a thread do other work instead of blocking while it waits for I/O. That improves scalability\n" +
            "(throughput per thread) and responsiveness (UI thread stays free). It doesn't make one operation faster."),
        new("Does async/await create new threads?",
            "No. async/await is a compiler transformation into a state machine + continuations. For I/O there's no thread\n" +
            "while waiting. Threads only appear when a continuation runs (on a pool thread or the captured context) or when\n" +
            "you explicitly use Task.Run for CPU work."),
        new("Difference between concurrency, parallelism and asynchrony?",
            "Concurrency: multiple tasks in progress (may interleave on one thread). Parallelism: multiple tasks literally\n" +
            "executing simultaneously on multiple cores. Asynchrony: not blocking the caller while an operation completes;\n" +
            "it enables concurrency without parallelism or extra threads."),
        new("When should you NOT use async?",
            "Pure CPU-bound work (async gives nothing; use Task.Run/Parallel at the right layer), trivial synchronous\n" +
            "operations, or hot paths where overhead matters and the work completes synchronously (consider ValueTask)."),
        new("Why does async help server scalability?",
            "A request waiting on a DB would otherwise hold a pool thread (~1MB stack plus scheduling cost). With await, that\n" +
            "thread returns to the pool and serves other requests, so a few threads handle thousands of in-flight requests\n" +
            "and you avoid thread-pool starvation."),
    ];
}
