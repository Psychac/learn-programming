using System.Runtime.CompilerServices;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: Task&lt;T&gt; is a class: every call that returns a result allocates. ValueTask&lt;T&gt; is a struct that can
/// wrap either a plain value (no allocation) or a Task. Use it for hot paths that OFTEN complete synchronously.
/// </summary>
internal sealed class L13_ValueTask : Lesson
{
    public override string Title => "ValueTask: avoiding allocations";
    public override string Summary => "Measure Task vs ValueTask allocation and learn the usage rules";

    public override string Concept => """
            Task<T> is a BOX shipped every time, even when the answer is already in your pocket.
            ValueTask<T> is "either the answer itself (no box) or a box". Great when most calls are cache hits.

            The price: stricter rules. A ValueTask is a one-shot ticket: await it once, don't share it.
            Default to Task<T>; reach for ValueTask only for hot paths that often complete synchronously.
            """;

    private static readonly Dictionary<int, int> Cache = new() { [1] = 100 };

    public override async Task RunAsync()
    {
        Log.Step("1. Measure allocations for 200,000 calls that complete synchronously (cache hit)");
        const int N = 200_000;

        // warm-up
        for (int i = 0; i < 1000; i++) { await ViaTaskAsync(1); await ViaValueTaskAsync(1); }

        long before = GC.GetAllocatedBytesForCurrentThread();
        long sum = 0;
        for (int i = 0; i < N; i++) sum += await ViaTaskAsync(1);
        long taskBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < N; i++) sum += await ViaValueTaskAsync(1);
        long valueTaskBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Log.Info($"Task<int>      : {taskBytes / 1024,8} KB allocated");
        Log.Good($"ValueTask<int> : {valueTaskBytes / 1024,8} KB allocated   (checksum {sum})");

        Log.Note("Run with `dotnet run -c Release` for accurate numbers: Debug builds turn async state machines into " +
                 "classes (allocating) and disable JIT optimizations.");

        Log.Step("2. It still works for the truly asynchronous path (cache miss)");
        Log.Info($"miss -> {await ViaValueTaskAsync(2)}");

        Log.Step("3. Rules of ValueTask (breaking them gives wrong results / exceptions)");
        Log.Note("* Await it ONCE. Never await the same ValueTask twice or from multiple consumers.\n" +
                 "* Never call .Result/.GetAwaiter().GetResult() before it has completed.\n" +
                 "* Never use it concurrently. If you need to WhenAll/WhenAny/store it, call .AsTask() once.\n" +
                 "* A method returning ValueTask can return a value directly: `return new ValueTask<int>(value);`");
        ValueTask<int> vt = ViaValueTaskAsync(1);
        Task<int> asTask = vt.AsTask(); // safe to hand to WhenAll etc.
        Log.Info($"AsTask result: {(await Task.WhenAll(asTask, ViaTaskAsync(1)))[0]}");

        Log.Step("4. Without async: returning a ValueTask by hand (the real hot-path pattern)");
        Log.Info($"hit  -> IsCompletedSuccessfully={GetManual(1).IsCompletedSuccessfully}");
        Log.Info($"miss -> IsCompletedSuccessfully={GetManual(2).IsCompletedSuccessfully} (wrapping a real Task)");

        Log.Step("Takeaways");
        Log.Note("* Default to Task/Task<T>. Reach for ValueTask only when profiling/design shows frequent sync completion\n" +
                 "  (caches, buffered reads: Stream.ReadAsync, Channel readers, etc.).\n" +
                 "* It trades allocation for stricter usage rules. Library authors care most.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> ViaTaskAsync(int key)
    {
        if (Cache.TryGetValue(key, out int v)) return v;
        await Task.Delay(10);
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async ValueTask<int> ViaValueTaskAsync(int key)
    {
        if (Cache.TryGetValue(key, out int v)) return v;
        await Task.Delay(10);
        return -1;
    }

    private static ValueTask<int> GetManual(int key) =>
        Cache.TryGetValue(key, out int v)
            ? new ValueTask<int>(v)                 // no allocation
            : new ValueTask<int>(SlowAsync());      // wraps a Task

    private static async Task<int> SlowAsync()
    {
        await Task.Delay(10);
        return -1;
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            What is ValueTask and why does it exist?
            """, """
            A struct wrapper around either a result, a Task<T>, or an IValueTaskSource<T>. When an operation completes synchronously it avoids allocating a Task object, reducing GC pressure in hot paths
            (Stream.ReadAsync, channels, caches).
            """),
        new("""
            What are the rules for consuming a ValueTask?
            """, """
            Await it once; don't await multiple times or concurrently; don't call GetResult before completion; don't block on it. If you need to store, share, or WhenAll it, convert with .AsTask() exactly once.
            """),
        new("""
            Should every async method return ValueTask?
            """, """
            No. Task is simpler, safe to await many times and compose. ValueTask adds complexity and, if it usually completes asynchronously, can even cost more. Use after measuring or in library hot paths.
            """),
        new("""
            What is IValueTaskSource?
            """, """
            An interface that lets pooled/reusable objects back a ValueTask so even asynchronous completions avoid allocations (used by sockets, Channels, Pipelines).
            """),
    ];
}
