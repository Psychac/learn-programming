using System.Threading.Channels;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: Channel&lt;T&gt; is an async-friendly producer/consumer queue. Producers WriteAsync, consumers
/// ReadAllAsync. A bounded channel gives BACKPRESSURE: fast producers wait when the buffer is full.
/// </summary>
internal sealed class L14_Channels : Lesson
{
    public override string Title => "Channels: async producer/consumer pipelines";
    public override string Summary => "Bounded/unbounded channels, backpressure, multiple consumers, completion";

    public override string Concept => """
            A Channel is a CONVEYOR BELT between workers.
              Producers put items on the belt (WriteAsync). Consumers take them off (ReadAllAsync).
              A BOUNDED belt has finite length: when full, producers WAIT (backpressure): the system slows
              itself rather than eating all your memory. Complete() = "no more items coming".
            """;

    public override async Task RunAsync()
    {
        Log.Step("1. One producer, one slow consumer, bounded channel (capacity 2) -> backpressure");
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true });

        Task producer = Task.Run(async () =>
        {
            for (int i = 1; i <= 6; i++)
            {
                await channel.Writer.WriteAsync(i);   // WAITS when the channel is full
                Log.Info($"produced {i}");
            }
            channel.Writer.Complete();                // tells readers: no more items
        });

        Task consumer = Task.Run(async () =>
        {
            await foreach (int item in channel.Reader.ReadAllAsync())
            {
                Log.Good($"  consumed {item}");
                await Task.Delay(200);                // slow consumer
            }
            Log.Info("consumer: channel completed, loop ended");
        });
        await Task.WhenAll(producer, consumer);
        Log.Note("The producer ran at most ~2 items ahead of the consumer. Unbounded channels would just grow memory.");

        Log.Step("2. Fan-out: 1 producer, 3 consumers (work distribution)");
        var jobs = Channel.CreateUnbounded<int>();
        Task[] workers = Enumerable.Range(1, 3).Select(id => Task.Run(async () =>
        {
            await foreach (int job in jobs.Reader.ReadAllAsync())
            {
                Log.Info($"worker {id} handling job {job}");
                await Task.Delay(150);
            }
        })).ToArray();
        for (int j = 1; j <= 7; j++) jobs.Writer.TryWrite(j);
        jobs.Writer.Complete();
        await Task.WhenAll(workers);

        Log.Step("3. Pipeline: stage1 -> stage2 -> printer, each stage async and decoupled");
        var raw = Channel.CreateBounded<int>(3);
        var squared = Channel.CreateBounded<int>(3);
        Task s1 = Task.Run(async () =>
        {
            for (int i = 1; i <= 5; i++) await raw.Writer.WriteAsync(i);
            raw.Writer.Complete();
        });
        Task s2 = Task.Run(async () =>
        {
            await foreach (int i in raw.Reader.ReadAllAsync())
            {
                await Task.Delay(50);
                await squared.Writer.WriteAsync(i * i);
            }
            squared.Writer.Complete();
        });
        Task s3 = Task.Run(async () =>
        {
            await foreach (int sq in squared.Reader.ReadAllAsync())
                Log.Info($"result {sq}");
        });
        await Task.WhenAll(s1, s2, s3);

        Log.Step("4. Failure propagation: complete the writer with an exception");
        var failing = Channel.CreateUnbounded<int>();
        failing.Writer.TryWrite(1);
        failing.Writer.Complete(new InvalidOperationException("producer crashed"));
        try
        {
            await foreach (int x in failing.Reader.ReadAllAsync())
                Log.Info($"read {x}");
        }
        catch (InvalidOperationException ex) { Log.Good($"consumer observed producer failure: {ex.Message}"); }

        Log.Step("Takeaways");
        Log.Note("* Channel replaces BlockingCollection<T> in async code (no threads blocked).\n" +
                 "* Always Complete() the writer or consumers wait forever.\n" +
                 "* Bounded + WriteAsync = natural backpressure; choose FullMode (Wait/DropOldest/DropNewest...) deliberately.\n" +
                 "* Combine with multiple consumers for parallel workers with controlled concurrency.");
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            Channel<T> vs BlockingCollection<T> vs ConcurrentQueue<T>?
            """, """
            ConcurrentQueue has no waiting. BlockingCollection blocks threads while waiting. Channel<T> is async-native: WriteAsync/ReadAsync don't block threads, support cancellation, bounding and completion.
            """),
        new("""
            What is backpressure and how does Channel provide it?
            """, """
            Slowing producers when consumers can't keep up. A bounded channel makes WriteAsync wait when full (or you choose FullMode: DropOldest/DropNewest/DropWrite), preventing unbounded memory growth.
            """),
        new("""
            How do you shut a channel pipeline down cleanly?
            """, """
            Producers call writer.Complete() (or Complete(exception) to propagate failure). Consumers' ReadAllAsync loops end after draining. Await the consumer tasks to ensure all items were processed.
            """),
        new("""
            When use SingleReader/SingleWriter options?
            """, """
            When you guarantee one reader/writer; it lets Channel use cheaper lock-free implementations. Violating the promise causes undefined behaviour.
            """),
    ];
}
