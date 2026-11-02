namespace practice_async.Infrastructure;

/// <summary>One-page revision sheet, printed by the 'c' menu option.</summary>
internal static class CheatSheet
{
    public static void Print()
    {
        Log.Header("ASYNC/AWAIT CHEAT SHEET (last-night-before-the-interview edition)");
        Console.WriteLine("""

        THE 10 SENTENCES
         1. A Task is a promise object (status + result/exception + continuations), not a thread.
         2. async/await = compiler state machine; no threads are created by it.
         3. An async method runs synchronously until its first await on an INCOMPLETE task, then returns a Task.
         4. await on a completed task doesn't yield; on an incomplete one it registers a continuation and returns.
         5. The continuation resumes on the captured SynchronizationContext/TaskScheduler (else a pool thread).
         6. ConfigureAwait(false) = don't go back to the captured context (library code). ExecutionContext still flows.
         7. Exceptions live in the Task and surface at await; await shows only the first of many.
         8. Cancellation is cooperative: pass the token down, check it, throw OperationCanceledException.
         9. Concurrency = start tasks first, await later (WhenAll). Bound it (SemaphoreSlim/ForEachAsync).
        10. Never block on async code (.Result/.Wait). Async all the way.

        DECISION TABLE
         waiting for I/O / timer ................ await an async API
         heavy CPU on UI thread ................. await Task.Run(...)
         heavy CPU, many items .................. Parallel.For / ForEach / PLINQ
         many I/O items, limit N ................ Parallel.ForEachAsync(MaxDOP) or SemaphoreSlim
         stream of items over time .............. IAsyncEnumerable<T> or Channel<T>
         producer/consumer with backpressure .... bounded Channel<T>
         first-of-N / timeout ................... WhenAny / WaitAsync(timeout) + token to the work
         adapt callback/event to Task ........... TaskCompletionSource (RunContinuationsAsynchronously)
         protect state across awaits ............ SemaphoreSlim(1,1) / channel owner (NOT lock)
         run async init once .................... Lazy<Task<T>> / AsyncLazy
         ambient per-request data ............... AsyncLocal<T> (not ThreadLocal)
         hot sync-completing API ................ ValueTask<T> (await once!)

        RETURN TYPES      Task / Task<T> default | ValueTask hot path | void ONLY event handlers | IAsyncEnumerable<T> streams
        NAMING / SHAPE    MethodAsync(args..., CancellationToken cancellationToken = default)

        SMELLS IN CODE REVIEW
         .Result .Wait() .GetAwaiter().GetResult()   async void     Task.Run in ASP.NET Core request path
         missing await (CS4014)                      Thread.Sleep in async code (use Task.Delay)
         lock containing await (can't)               for-loop variable captured by lambda
         return task inside using/try (no await)     unbounded Select(...).WhenAll over huge input
         lazy LINQ of tasks enumerated twice         async lambda passed to Action parameter (List.ForEach)
         WhenAny 'timeout' with no cancellation      new TaskCompletionSource without RunContinuationsAsynchronously
         ConfigureAwait missing in library code      swallowing OperationCanceledException

        NUMBERS TO KNOW
         thread stack ~1MB (default) | pool starts ~1 thread per core, injects ~1-2/s when starved
         Task<int> alloc ~ 72+ bytes per call (completed-synchronously async still allocates)
        """);
    }
}
