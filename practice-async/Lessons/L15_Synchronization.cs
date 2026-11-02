using System.Collections.Concurrent;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

internal sealed class L15_Synchronization : Lesson
{
    public override string Title => "Shared state & synchronization in async code";
    public override string Summary => "Races across awaits, lock vs SemaphoreSlim, AsyncLock, Interlocked, AsyncLazy, safe caches";

    public override string Concept => """
        Async removes thread-blocking but NOT shared-state bugs. Two flows can interleave at every `await`.

          Bank account analogy: you read the balance (100), go for a coffee (await), come back and write
          100+10. Meanwhile a colleague did the same. One deposit is LOST.

        Tools:
          Interlocked / Concurrent* collections : single atomic operations
          lock                                  : sync only; you CANNOT await inside (compile error CS1996)
          SemaphoreSlim(1,1)                    : async-friendly mutex (WaitAsync). Not re-entrant!
          Channel<T> / one owner flow           : avoid sharing — pass messages instead (best option)
          Lazy<Task<T>> / AsyncLazy             : run an async init exactly once
        """;

    private int _balance;

    public override async Task RunAsync()
    {
        Log.Step("DEMO 1: the lost update (read, await, write)");
        _balance = 0;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => DepositUnsafeAsync()));
        Log.Warn($"10 deposits of 1 -> balance {_balance} (expected 10)");

        Log.Step("DEMO 2: fix with an async mutex (SemaphoreSlim(1,1))");
        _balance = 0;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => DepositSafeAsync()));
        Log.Good($"10 deposits of 1 -> balance {_balance}");

        Log.Step("DEMO 3: why `lock` can't span an await");
        Log.Note("lock(x) { await ...; }  -> error CS1996. Reason: lock is owned by a THREAD, but after the await a\n" +
                 "different thread may resume, and the original thread would still hold... nothing it can release.\n" +
                 "Also a long wait inside a lock would block all other callers' threads. Use SemaphoreSlim.WaitAsync().");

        Log.Step("DEMO 4: SemaphoreSlim is NOT re-entrant — calling yourself deadlocks");
        using (var m = new SemaphoreSlim(1, 1))
        {
            await m.WaitAsync();
            Task second = m.WaitAsync();
            await Task.Delay(150);
            Log.Warn($"second WaitAsync by the SAME flow completed? {second.IsCompleted}  (it would wait forever)");
            m.Release();   // release the first so the second can finish
            await second;
            m.Release();
        }
        Log.Note("Design rule: don't call an AsyncLock-protected method from another method that already holds the lock.");

        Log.Step("DEMO 5: a reusable AsyncLock with `using` (nicer than try/finally)");
        var gate = new AsyncLock();
        int shared = 0;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using (await gate.LockAsync())
            {
                int tmp = shared;
                await Task.Delay(10);
                shared = tmp + 1;
            }
        }));
        Log.Good($"AsyncLock: shared = {shared} (expected 5)");

        Log.Step("DEMO 6: Interlocked for simple atomic counters (no await inside)");
        long counter = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100_000; i++) Interlocked.Increment(ref counter);
        })));
        Log.Good($"Interlocked counter = {counter} (expected 800000)");
        long unsafeCounter = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100_000; i++) unsafeCounter++;
        })));
        Log.Warn($"plain ++ counter  = {unsafeCounter} (expected 800000; lost updates are normal)");

        Log.Step("DEMO 7: initialize something expensive and async EXACTLY ONCE (AsyncLazy)");
        int initCalls = 0;
        var lazyConfig = new AsyncLazy<string>(async () =>
        {
            Interlocked.Increment(ref initCalls);
            Log.Info("expensive async init running...");
            await Task.Delay(150);
            return "config-v1";
        });
        var readers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => lazyConfig.Value));
        Log.Good($"5 concurrent readers got {readers[0]}; init ran {initCalls} time(s)");

        Log.Step("DEMO 8: ConcurrentDictionary.GetOrAdd + async: the factory can run MORE THAN ONCE");
        var cache = new ConcurrentDictionary<string, Task<int>>();
        int calls = 0;
        Task<int> Load(string k) { Interlocked.Increment(ref calls); return SlowLoad(); }
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => cache.GetOrAdd("k", Load))));
        Log.Warn($"naive GetOrAdd: factory ran {calls} time(s); under contention it can be >1 (the stored value stays consistent, but work is duplicated)");
        var cache2 = new ConcurrentDictionary<string, Lazy<Task<int>>>();
        calls = 0;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
            cache2.GetOrAdd("k", _ => new Lazy<Task<int>>(() => Load("k"))).Value)));
        Log.Good($"Lazy<Task<T>> in the dictionary: factory ran {calls} time(s)");

        Log.Step("DEMO 9: best fix is often NOT sharing — funnel through one owner");
        Log.Note("Channel<T> + a single consumer owns the state; everyone else sends messages (lesson 14).\n" +
                 "No locks, no races, easy to reason about. Actor-style code is the usual answer for complex state.");
    }

    private async Task DepositUnsafeAsync()
    {
        int tmp = _balance;
        await Task.Delay(20);
        _balance = tmp + 1;
    }

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private async Task DepositSafeAsync()
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

    private static async Task<int> SlowLoad()
    {
        await Task.Delay(50);
        return 1;
    }

    public override IEnumerable<QA> Interview =>
    [
        new("Why can't you `await` inside a `lock`? What do you use instead?",
            "A lock is thread-affine: Monitor must be released by the thread that took it, but after an await another thread\n" +
            "may resume. The compiler forbids it (CS1996). Use SemaphoreSlim(1,1).WaitAsync() with try/finally Release, or an\n" +
            "AsyncLock wrapper, or avoid sharing with channels."),
        new("Is SemaphoreSlim re-entrant? What happens if the holder waits on it again?",
            "No. It's a counter, not owned by a flow. A second WaitAsync from the same flow waits for a Release that never\n" +
            "comes: self-deadlock. Structure code so locked sections don't call each other."),
        new("Does async code need thread-safety?",
            "Yes. Concurrent async operations interleave at awaits (and run on multiple pool threads), so shared mutable state\n" +
            "needs protection or confinement. Single-threaded contexts (UI) only guarantee no simultaneous execution, not\n" +
            "atomicity across awaits."),
        new("How do you initialise a shared resource asynchronously exactly once?",
            "Lazy<Task<T>> (or AsyncLazy) stored in a field/dictionary; every caller awaits the same task. Remember to decide\n" +
            "what happens if it faults (cache the failure or reset). Avoid double-checked locking with awaits."),
        new("Interlocked vs lock vs SemaphoreSlim: when each?",
            "Interlocked: one atomic read-modify-write on a primitive. lock: short synchronous critical sections. SemaphoreSlim:\n" +
            "when the critical section awaits, or when you need to limit concurrency to N. Channels/actors: avoid shared state."),
    ];
}

/// <summary>Minimal async mutex that releases via Dispose so it can be used with `using`.</summary>
internal sealed class AsyncLock
{
    private readonly SemaphoreSlim _sem = new(1, 1);
    private readonly Task<IDisposable> _releaser;

    public AsyncLock() => _releaser = Task.FromResult<IDisposable>(new Releaser(this));

    public Task<IDisposable> LockAsync()
    {
        Task wait = _sem.WaitAsync();
        return wait.IsCompletedSuccessfully
            ? _releaser  // fast path: uncontended, no allocation
            : wait.ContinueWith((_, state) => (IDisposable)state!, _releaser.Result,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private sealed class Releaser(AsyncLock owner) : IDisposable
    {
        public void Dispose() => owner._sem.Release();
    }
}

/// <summary>Runs an async factory at most once; all callers await the same Task.</summary>
internal sealed class AsyncLazy<T>(Func<Task<T>> factory)
{
    private readonly Lazy<Task<T>> _lazy = new(() => Task.Run(factory));
    public Task<T> Value => _lazy.Value;
}
