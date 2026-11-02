using System.Diagnostics;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: I/O-bound waits need no thread (await). CPU-bound work needs a thread: if you run it on a
/// UI/request thread you block that thread; offload with Task.Run (or Parallel.For to use multiple cores).
/// </summary>
internal sealed class L16_CpuVsIo : Lesson
{
    public override string Title => "CPU-bound vs I/O-bound: Task.Run, Parallel, ThreadPool";
    public override string Summary => "Frozen UI demo, offloading with Task.Run, Parallel.For, starvation notes";

    public override string Concept => """
            Two kinds of slowness:
              I/O-bound: you're waiting for someone else (the kitchen). No waiter needed -> await.
              CPU-bound: YOU are chopping vegetables. A person (core) must do it. await can't help.

            If a UI waiter chops vegetables, the whole restaurant freezes. So hand the chopping to a back-room
            worker (Task.Run) and await it. If you have many vegetables and many cooks (cores), Parallel.For.
            """;

    public override async Task RunAsync()
    {
        using var ui = new SingleThreadContext("FakeUI");

        Log.Step("1. CPU work directly on the 'UI thread' freezes everything else on it");
        await ui.InvokeAsync(async () =>
        {
            int ticks = 0;
            using var stop = new CancellationTokenSource();
            Task heartbeat = HeartbeatAsync(() => ticks++, stop.Token); // runs on the UI thread between awaits

            await Task.Delay(50);
            ticks = 0;
            Burn(600);                       // blocks the UI thread for 600ms
            await Task.Delay(10);            // give queued heartbeat continuations a chance
            Log.Warn($"heartbeat ticks during blocking CPU work: {ticks}  (UI was frozen: ~6 ticks were due, at most 1 got through)");

            stop.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        });

        Log.Step("2. Offload with Task.Run: UI thread stays responsive");
        await ui.InvokeAsync(async () =>
        {
            int ticks = 0;
            using var stop = new CancellationTokenSource();
            Task heartbeat = HeartbeatAsync(() => ticks++, stop.Token);

            await Task.Delay(50);
            ticks = 0;
            long result = await Task.Run(() => Burn(600)); // runs on a pool thread; UI thread free while awaiting
            Log.Good($"heartbeat ticks during Task.Run work: {ticks}  (≈6 expected; UI stayed alive), result={result}");

            stop.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        });

        Log.Step("3. Several cores: Parallel.For / Parallel.ForEach for CPU-bound data parallelism");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 4; i++) Burn(150);
        long serial = sw.ElapsedMilliseconds;
        sw.Restart();
        Parallel.For(0, 4, _ => Burn(150));
        Log.Info($"4 x 150ms CPU jobs: serial={serial}ms, Parallel.For={sw.ElapsedMilliseconds}ms ({Environment.ProcessorCount} cores)");
        sw.Restart();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Burn(150))));
        Log.Info($"Task.Run + WhenAll: {sw.ElapsedMilliseconds}ms");
        Log.Note("Await-based concurrency does NOT speed up CPU work (there's nothing to wait for); more cores do.");

        Log.Step("4. Mixed: async I/O and CPU in a pipeline (keep each in its proper tool)");
        string text = await FakeDownloadAsync();                      // I/O: await
        int words = await Task.Run(() => text.Split(' ').Length);     // CPU: offload (if heavy)
        Log.Info($"downloaded {text.Length} chars, {words} words");

        Log.Step("5. Thread-pool starvation (explained; not run because timing is machine-dependent)");
        Log.Note("The pool starts with ~1 thread per core and injects more slowly (roughly 1-2 per second) when threads are blocked.\n" +
                 "A server doing Thread.Sleep / .Result / .Wait() on pool threads under load queues work behind them:\n" +
                 "latency spikes while the CPU sits idle. With await, waiting threads are RETURNED to the pool, so the\n" +
                 "same few threads serve thousands of requests (lesson 1, step 3).");

        Log.Step("Takeaways");
        Log.Note("* I/O wait -> await the async API (no thread). CPU work -> Task.Run / Parallel.For (a thread, by necessity).\n" +
                 "* In ASP.NET Core don't Task.Run request work: you're already on a pool thread; it only adds overhead.\n" +
                 "* In UI apps do Task.Run for heavy CPU so the UI thread keeps pumping.\n" +
                 "* Never block pool threads with sync waits; it starves the pool and tanks throughput.");
    }

    private static async Task HeartbeatAsync(Action onTick, CancellationToken ct)
    {
        while (true)
        {
            await Task.Delay(100, ct);
            onTick();
        }
    }

    /// <summary>Busy-loop for roughly <paramref name="ms"/> milliseconds of CPU time.</summary>
    private static long Burn(int ms)
    {
        var sw = Stopwatch.StartNew();
        long x = 0;
        while (sw.ElapsedMilliseconds < ms) x += Math.Sqrt(x + 1) > 0 ? 1 : 0;
        return x;
    }

    private static async Task<string> FakeDownloadAsync()
    {
        await Task.Delay(150);
        return string.Join(' ', Enumerable.Repeat("lorem ipsum dolor", 1000));
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            How do you decide between async/await, Task.Run and Parallel.For?
            """, """
            I/O-bound: async APIs with await (no threads). CPU-bound on a UI thread: await Task.Run(...) to keep the UI responsive. CPU-bound data parallelism: Parallel.For/ForEach/PLINQ across cores.
            On ASP.NET Core don't Task.Run request work for the sake of 'async'.
            """),
        new("""
            Is `await Task.Run(() => SyncIoCall())` a good way to make it async?
            """, """
            It moves the blocking to a pool thread; it doesn't free any thread overall ('async over sync'), but it can keep a UI responsive. Use real async APIs (ReadAsync...) in libraries.
            """),
        new("""
            What is thread-pool starvation?
            """, """
            All pool workers are blocked (sleeping, waiting on locks/.Result) while work is queued; new threads are injected slowly, so latency explodes while CPU is idle. Avoid blocking in pool threads; use async; raise min threads only as a last resort.
            """),
        new("""
            Does Parallel.ForEach work well with async lambdas?
            """, """
            No: it takes Action so async lambdas become async void and the loop doesn't wait. Use Parallel.ForEachAsync or Task.WhenAll with throttling.
            """),
    ];
}
