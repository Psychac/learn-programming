using System.Runtime.CompilerServices;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: Task&lt;IEnumerable&lt;T&gt;&gt; gives you ALL items at once, after everything is ready.
/// IAsyncEnumerable&lt;T&gt; gives you items ONE AT A TIME, each produced asynchronously (streaming/paging).
/// </summary>
internal sealed class L12_AsyncStreams : Lesson
{
    public override string Title => "Async streams (IAsyncEnumerable), await using, PeriodicTimer";
    public override string Summary => "yield return in async methods, await foreach, cancellation, IAsyncDisposable";

    public override string Concept => """
            Task<List<T>>       = a truck that arrives ONCE with the whole order (you wait for everything)
            IAsyncEnumerable<T> = a SUSHI CONVEYOR BELT: items arrive one by one as they're ready, and you can
                                  stop taking at any time (break / cancel).

            `async` + `yield return` lets you write the producer as a simple loop; `await foreach` consumes it.
            """;

    public override async Task RunAsync()
    {
        Log.Step("1. Producing and consuming an async stream");
        await foreach (int page in ReadPagesAsync(3))
            Log.Info($"consumed page {page}");
        Log.Note("The producer and consumer interleave: page 1 is processed before page 2 is even fetched.");

        Log.Step("2. Contrast: Task<List<int>> makes you wait for everything");
        var all = await ReadAllPagesAsync(3);
        Log.Info($"got all {all.Count} pages only at the end");

        Log.Step("3. Cancellation: WithCancellation + [EnumeratorCancellation]");
        using (var cts = new CancellationTokenSource(350))
        {
            try
            {
                await foreach (int n in EndlessAsync().WithCancellation(cts.Token))
                    Log.Info($"item {n}");
            }
            catch (OperationCanceledException) { Log.Good("stream cancelled mid-flight"); }
        }

        Log.Step("4. Breaking out early disposes the enumerator (finally blocks in the producer run)");
        await foreach (int n in StreamWithCleanupAsync())
        {
            Log.Info($"item {n}");
            if (n == 2) break;
        }

        Log.Step("5. IAsyncDisposable + await using: async cleanup (flush a stream, close a connection)");
        await using (var conn = new FakeConnection("db"))
        {
            Log.Info("using the connection");
        } // DisposeAsync awaited here
        Log.Info("after await using");

        Log.Step("6. PeriodicTimer: a clean async replacement for timer callbacks / Task.Delay loops");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
        using var cts2 = new CancellationTokenSource(520);
        int ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cts2.Token))
                Log.Info($"tick {++ticks}");
        }
        catch (OperationCanceledException) { Log.Good($"stopped after {ticks} ticks"); }
        Log.Note("PeriodicTimer doesn't overlap runs: if the body is slow, ticks are coalesced rather than stacked.");

        Log.Step("Takeaways");
        Log.Note("* `async IAsyncEnumerable<T>` method = async + yield return.\n" +
                 "* Use for paged APIs, file/DB streaming, message feeds: low memory, low latency to first item.\n" +
                 "* Add [EnumeratorCancellation] CancellationToken ct to make WithCancellation work.\n" +
                 "* await using / IAsyncDisposable for resources needing async cleanup.");
    }

    private static async IAsyncEnumerable<int> ReadPagesAsync(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            await Task.Delay(150); // simulate a network round trip per page
            Log.Info($"producer: fetched page {i}");
            yield return i;
        }
    }

    private static async Task<List<int>> ReadAllPagesAsync(int count)
    {
        var list = new List<int>();
        await foreach (var p in ReadPagesAsync(count)) list.Add(p);
        return list;
    }

    private static async IAsyncEnumerable<int> EndlessAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 1; ; i++)
        {
            await Task.Delay(100, ct);
            yield return i;
        }
    }

    private static async IAsyncEnumerable<int> StreamWithCleanupAsync()
    {
        try
        {
            for (int i = 1; i <= 5; i++)
            {
                await Task.Delay(30);
                yield return i;
            }
        }
        finally
        {
            Log.Good("producer: finally block ran (consumer stopped early)");
        }
    }

    private sealed class FakeConnection(string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Log.Info($"{name}: flushing & closing asynchronously...");
            await Task.Delay(100);
            Log.Info($"{name}: closed");
        }
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            What is IAsyncEnumerable<T> and when use it instead of Task<IEnumerable<T>>?
            """, """
            A pull-based async sequence: MoveNextAsync is awaited per item. Use it for paged APIs, streaming from DB/files/network, or when the consumer can act on early items
            before the producer finishes, and when you may stop early or cancel.
            """),
        new("""
            How do you pass a CancellationToken to an async iterator?
            """, """
            Add a parameter with [EnumeratorCancellation] CancellationToken ct = default to the iterator method; consumers call .WithCancellation(token). Without the attribute the token passed
            via WithCancellation is ignored.
            """),
        new("""
            What happens to finally blocks when the consumer breaks out of await foreach?
            """, """
            The enumerator's DisposeAsync is called, which runs the iterator's pending finally/using blocks. So resource cleanup in the producer is safe.
            """),
        new("""
            What is IAsyncDisposable / await using?
            """, """
            Async cleanup: DisposeAsync returns a ValueTask so cleanup can await (flush buffers, close connections gracefully). `await using var x = ...;` calls it. Implement both IDisposable and
            IAsyncDisposable if callers may use either.
            """),
        new("""
            PeriodicTimer vs System.Threading.Timer vs Task.Delay loop?
            """, """
            PeriodicTimer.WaitForNextTickAsync is awaited in a normal loop, doesn't overlap executions, supports cancellation, and has no callback re-entrancy issues. Timer callbacks can overlap and
            swallow exceptions; Delay loops drift by the body's duration.
            """),
    ];
}
