using System.Diagnostics;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: `await` each call one after another = sequential. To get concurrency, START the tasks first
/// (don't await yet), then await them together. Concurrency comes from starting, not from await.
/// </summary>
internal sealed class L10_Composition : Lesson
{
    public override string Title => "Composing tasks: sequential, WhenAll, WhenAny, throttling";
    public override string Summary => "Sequential vs concurrent timing, WhenAll/WhenAny/WhenEach, Parallel.ForEachAsync, SemaphoreSlim, retry";

    public override string Concept => """
            Cooking dinner: you do NOT put the rice on, stand watching it, THEN start the soup.
            You start everything, then wait for the dishes together.

              `await A(); await B();`                         = sequential (times add up)
              `var a = A(); var b = B(); await a; await b;`   = concurrent (time = the slowest)
              Task.WhenAll = wait for everyone   Task.WhenAny = first one back   Task.WhenEach = serve as they come
              A semaphore = "only 3 burners": bounded concurrency
            """;

    public override async Task RunAsync()
    {
        var sw = Stopwatch.StartNew();

        Log.Step("1. Sequential: await inside the flow -> times ADD UP");
        int a = await FetchAsync("A", 300);
        int b = await FetchAsync("B", 300);
        int c = await FetchAsync("C", 300);
        Log.Info($"sequential total: {sw.ElapsedMilliseconds}ms (sum={a + b + c})");

        Log.Step("2. Concurrent: start all, then await -> total = the slowest");
        sw.Restart();
        Task<int> ta = FetchAsync("A", 300);
        Task<int> tb = FetchAsync("B", 300);
        Task<int> tc = FetchAsync("C", 300);
        // All three are now running. Awaiting them one-by-one is fine; the waiting overlaps.
        int[] results = await Task.WhenAll(ta, tb, tc);
        Log.Good($"concurrent total: {sw.ElapsedMilliseconds}ms (results={string.Join(",", results)})");
        Log.Note("WhenAll returns results in the SAME ORDER as the input tasks, not completion order.");

        Log.Step("3. Task.WhenAny: react to the first one that finishes (e.g. fastest mirror wins)");
        using (var cts = new CancellationTokenSource())
        {
            var racers = new List<Task<string>>
            {
                MirrorAsync("mirror-1", 400, cts.Token),
                MirrorAsync("mirror-2", 150, cts.Token),
                MirrorAsync("mirror-3", 600, cts.Token),
            };
            Task<string> winner = await Task.WhenAny(racers);
            Log.Good($"winner: {await winner}");  // await again to observe exceptions/result
            cts.Cancel();                          // stop the losers
            try { await Task.WhenAll(racers); } catch (OperationCanceledException) { Log.Info("losers cancelled"); }
        }

        Log.Step("4. Task.WhenEach (.NET 9+): process results AS THEY COMPLETE");
        var jobs = new[] { FetchAsync("slow", 500), FetchAsync("fast", 100), FetchAsync("medium", 300) };
        await foreach (Task<int> done in Task.WhenEach(jobs))
            Log.Info($"completed with {await done}");

        Log.Step("5. Timeout around a task (WaitAsync)");
        try { await FetchAsync("slow", 1000).WaitAsync(TimeSpan.FromMilliseconds(200)); }
        catch (TimeoutException) { Log.Good("timed out after 200ms"); }

        Log.Step("6. Throttling: 8 jobs but max 3 at a time using SemaphoreSlim");
        sw.Restart();
        using (var gate = new SemaphoreSlim(3))
        {
            int running = 0, maxRunning = 0;
            var work = Enumerable.Range(1, 8).Select(async i =>
            {
                await gate.WaitAsync();             // async-friendly: waits without blocking a thread
                try
                {
                    int now = Interlocked.Increment(ref running);
                    InterlockedMax(ref maxRunning, now);
                    Log.Info($"job {i} start (running={now})");
                    await Task.Delay(200);
                }
                finally
                {
                    Interlocked.Decrement(ref running);
                    gate.Release();
                }
            }).ToList();
            await Task.WhenAll(work);
            Log.Good($"max concurrent = {maxRunning}, total {sw.ElapsedMilliseconds}ms (≈ 3 waves x 200ms)");
        }

        Log.Step("7. Parallel.ForEachAsync (.NET 6+): built-in throttled async loop");
        sw.Restart();
        await Parallel.ForEachAsync(
            Enumerable.Range(1, 6),
            new ParallelOptions { MaxDegreeOfParallelism = 2 },
            async (i, ct) =>
            {
                await Task.Delay(200, ct);
                Log.Info($"item {i} done");
            });
        Log.Good($"6 items, DOP=2 -> {sw.ElapsedMilliseconds}ms");

        Log.Step("8. Retry with exponential backoff (a common composition helper)");
        int attempts = 0;
        string value = await RetryAsync(async () =>
        {
            attempts++;
            await Task.Delay(10);
            if (attempts < 3) throw new HttpRequestException($"transient failure #{attempts}");
            return "success";
        }, maxAttempts: 5);
        Log.Good($"got '{value}' after {attempts} attempts");

        Log.Step("9. Common mistake: await in a loop when you wanted concurrency");
        sw.Restart();
        foreach (var name in new[] { "x", "y", "z" }) await FetchAsync(name, 150);
        Log.Warn($"foreach+await: {sw.ElapsedMilliseconds}ms (sequential)");
        sw.Restart();
        await Task.WhenAll(new[] { "x", "y", "z" }.Select(n => FetchAsync(n, 150)));
        Log.Good($"Select+WhenAll: {sw.ElapsedMilliseconds}ms (concurrent)");
        Log.Note("Sequential is correct when order/dependency matters or the server can't take parallel load.");

        Log.Step("Takeaways");
        Log.Note("* Start tasks first, await later = concurrency.\n" +
                 "* WhenAll (all), WhenAny (first), WhenEach (stream in completion order).\n" +
                 "* Bound concurrency with SemaphoreSlim.WaitAsync or Parallel.ForEachAsync — never fire 10,000 requests unbounded.\n" +
                 "* Never use lock for async regions (can't await inside); use SemaphoreSlim(1,1).");
    }

    private static async Task<int> FetchAsync(string name, int ms)
    {
        await Task.Delay(ms);
        return ms;
    }

    private static async Task<string> MirrorAsync(string name, int ms, CancellationToken ct)
    {
        await Task.Delay(ms, ct);
        return $"{name} ({ms}ms)";
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while (value > (cur = Volatile.Read(ref target)))
            if (Interlocked.CompareExchange(ref target, value, cur) == cur) return;
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxAttempts)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt - 1));
                Log.Info($"attempt {attempt} failed ({ex.Message}); retrying in {delay.TotalMilliseconds}ms");
                await Task.Delay(delay);
            }
        }
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            How do you run several async operations concurrently and await them all?
            """, """
            Start them first (each call returns a hot Task), then `await Task.WhenAll(tasks)`. Writing `await` between starts makes it sequential.
            """),
        new("""
            WhenAll vs WhenAny vs WhenEach?
            """, """
            WhenAll completes when all do (results in input order, aggregated exceptions). WhenAny completes when the first does and returns that task (others keep running).
            WhenEach (.NET 9) yields tasks as they complete via IAsyncEnumerable.
            """),
        new("""
            How do you limit concurrency?
            """, """
            SemaphoreSlim.WaitAsync around each unit of work, Parallel.ForEachAsync with MaxDegreeOfParallelism, a fixed number of worker loops reading a Channel, or
            TPL Dataflow. Never fire unbounded tasks at a remote service.
            """),
        new("""
            How would you implement retry with backoff correctly?
            """, """
            Loop with try/catch around `await action(ct)`, catching only transient exceptions (`when` filters), delay with exponential backoff + jitter via Task.Delay(delay, ct), stop after a max,
            rethrow the last error, and honour cancellation (don't retry OperationCanceledException).
            """),
        new("""
            What's wrong with `await Task.WhenAll(items.Select(async x => await Process(x)))` for 100k items?
            """, """
            It starts all 100k at once: memory, socket/port exhaustion and overload of downstream services. Batch or throttle.
            """),
    ];
}
