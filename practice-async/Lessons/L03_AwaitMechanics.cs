using practice_async.Infrastructure;

namespace practice_async.Lessons;

internal sealed class L03_AwaitMechanics : Lesson
{
    public override string Title => "How async/await really works (the state machine)";
    public override string Summary => "Sync until first await, bookmarks, a hand-written state machine, fast path, Task.Yield, awaitable pattern";

    public override string Concept => """
        Think of reading a book with a BOOKMARK.

          async method  = a book you read from the start
          await (slow)  = you hit a page that says "wait for the parcel". You slip in a bookmark,
                          hand the caller a RECEIPT (the Task), and close the book. Your hands are free.
          parcel arrives= somebody opens the book at the bookmark and continues reading (continuation)
          await (ready) = "parcel already here" -> you simply keep reading, no bookmark needed
          return x      = you finish the book and the receipt is stamped DONE with x

        The compiler turns every async method into a STATE MACHINE object: local variables become fields,
        each await becomes a numbered resume point, and a `MoveNext()` method jumps to the right one.
        The three rules you must be able to recite:
          1. An async method runs SYNCHRONOUSLY (on the caller's thread) up to its first await of an
             incomplete task.
          2. At that await it RETURNS a Task to the caller; the rest becomes a continuation.
          3. When the awaited task completes, the continuation resumes the method (where depends on the
             SynchronizationContext, lesson 5).
        """;

    public override async Task RunAsync()
    {
        Log.Step("DEMO 1: rule 1 & 2 — the order of events");
        var p = Quiz.Predict(
            "What is the print order of 1, 2a, 2b, 3 below?\n" +
            "     Log(\"1\"); var t = Foo(); Log(\"3\"); await t;      // Foo: Log(\"2a\"); await Task.Delay(100); Log(\"2b\");",
            ["1, 2a, 2b, 3", "1, 2a, 3, 2b", "1, 3, 2a, 2b", "1, 2a, 3 (2b never prints)"],
            correct: 1,
            why: "Foo runs synchronously until its first incomplete await, so 2a prints inside the call. Foo then returns its Task, " +
                 "the caller prints 3, and 2b is the continuation that runs after the delay.");
        Log.Info("1  (caller before call)");
        Task t = FooAsync();
        Log.Info($"3  (caller after call; task.IsCompleted={t.IsCompleted})");
        await t;
        p.Reveal();

        Log.Step("DEMO 2: the SAME method written by hand as a state machine");
        Log.Note("This is what the compiler generates, simplified. Compare its output with the real async method.");
        Log.Info("--- real async method ---");
        int real = await ThreeStepsAsync();
        Log.Info($"--- hand-written state machine --- (real returned {real})");
        int manual = await new ManualStateMachine().StartAsync();
        Log.Good($"manual machine returned {manual}: same prints, same result, no 'async' keyword anywhere.");

        Log.Step("DEMO 3: rule 'await on a completed task does NOT yield'");
        await AlreadyDoneAsync();
        Log.Note("Both lines ran in one go without leaving the thread. await asks the awaiter `IsCompleted?`;\n" +
                 "if true, it just calls GetResult() and keeps going. This is why cached results are cheap.");

        Log.Step("DEMO 4: Task.Yield() — force a suspension even though nothing is slow");
        Task y = YieldDemoAsync();
        Log.Info("caller: YieldDemo returned already (it yielded right away)");
        await y;

        Log.Step("DEMO 5: `async` alone does nothing; only `await` creates suspension points");
        Task<int> noAwait = NoAwaitInsideAsync();
        Log.Info($"async method without await: IsCompleted={noAwait.IsCompleted}, result={noAwait.Result}");
        Log.Note("The compiler warns (CS1998). If there's no await, drop `async` and return Task.FromResult(...).");

        Log.Step("DEMO 6: anything with GetAwaiter() is awaitable (the awaitable pattern)");
        Log.Info("awaiting a TimeSpan directly (extension GetAwaiter)...");
        await TimeSpan.FromMilliseconds(150);
        Log.Info("...resumed");
        Log.Note("Pattern: awaiter needs IsCompleted, OnCompleted(Action) (INotifyCompletion), GetResult().\n" +
                 "Task, ValueTask, Task.Yield(), ConfigureAwait(...) and custom types all use this protocol.");

        Log.Step("DEMO 7: what an async method can return");
        Log.Note("Task / Task<T>    : the default, awaitable, composable\n" +
                 "ValueTask / <T>    : struct, avoids allocation when it completes synchronously (lesson 13)\n" +
                 "void               : fire-and-forget; errors are unobservable; ONLY for event handlers (lesson 17)\n" +
                 "IAsyncEnumerable<T>: async iterator with yield return (lesson 12)\n" +
                 "custom task-like   : any type with [AsyncMethodBuilder] (rare)");
    }

    private static async Task FooAsync()
    {
        Log.Info("2a (inside Foo, before its first await — still the caller's thread)");
        await Task.Delay(100);
        Log.Info("2b (continuation after the delay)");
    }

    private static async Task<int> ThreeStepsAsync()
    {
        Log.Info("part 1");
        await Task.Delay(100);
        Log.Info("part 2");
        await Task.Delay(100);
        Log.Info("part 3");
        return 42;
    }

    /// <summary>Hand-written equivalent of ThreeStepsAsync (compiler output, simplified).</summary>
    private sealed class ManualStateMachine
    {
        private int _state;
        private readonly TaskCompletionSource<int> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> StartAsync()
        {
            MoveNext();          // the caller's thread runs part 1 synchronously, exactly like a real async method
            return _result.Task; // the "receipt"
        }

        private void MoveNext()
        {
            try
            {
                switch (_state)
                {
                    case 0:
                        Log.Info("part 1");
                        _state = 1;
                        if (Suspend(Task.Delay(100))) return;   // not finished -> leave a bookmark, return
                        goto case 1;
                    case 1:
                        Log.Info("part 2");
                        _state = 2;
                        if (Suspend(Task.Delay(100))) return;
                        goto case 2;
                    case 2:
                        Log.Info("part 3");
                        _result.SetResult(42);                  // stamp the receipt DONE
                        return;
                }
            }
            catch (Exception ex)
            {
                _result.SetException(ex);                       // the real thing does this for ANY exception
            }
        }

        // 'awaiter.IsCompleted ? continue : register MoveNext as continuation and return'
        private bool Suspend(Task awaited)
        {
            if (awaited.IsCompleted) return false;
            awaited.ContinueWith(_ => MoveNext());
            return true;
        }
    }

    private static async Task AlreadyDoneAsync()
    {
        Log.Info("AlreadyDone: before await");
        await Task.CompletedTask;
        Log.Info("AlreadyDone: after await (never left the thread)");
    }

    private static async Task YieldDemoAsync()
    {
        Log.Info("Yield: before");
        await Task.Yield();
        Log.Info("Yield: after (queued to the pool even though nothing was slow)");
    }

#pragma warning disable CS1998
    private static async Task<int> NoAwaitInsideAsync() => 5;
#pragma warning restore CS1998

    public override IEnumerable<QA> Interview =>
    [
        new("Explain what the compiler does with an async method.",
            "It rewrites it into a struct/class implementing IAsyncStateMachine. Locals become fields, each await becomes a\n" +
            "numbered state, and MoveNext() runs until an await on an incomplete awaiter, where it stores the state, registers\n" +
            "itself as the continuation (AwaitUnsafeOnCompleted) and returns. An AsyncTaskMethodBuilder creates the returned\n" +
            "Task and completes it with SetResult/SetException."),
        new("On which thread does an async method start running? When does it stop being synchronous?",
            "It starts synchronously on the caller's thread, and stays synchronous until the first await of an incomplete task.\n" +
            "Then it returns a Task to the caller. If every awaited task is already complete, it never suspends at all."),
        new("What does `await` actually call?",
            "GetAwaiter() on the expression, then awaiter.IsCompleted. If true, awaiter.GetResult() immediately. If false, it\n" +
            "calls awaiter.OnCompleted/UnsafeOnCompleted(continuation) and returns; later the continuation calls GetResult(),\n" +
            "which returns the value or rethrows the stored exception."),
        new("What does Task.Yield() do and when would you use it?",
            "Returns an awaitable that is never complete, so the continuation is always queued (to the captured context or\n" +
            "pool). Used to force a method to return to its caller, to give other work a turn, or to break up long synchronous\n" +
            "stretches, e.g. avoid stack dives and starving a UI loop."),
        new("Is an `async` method that has no `await` asynchronous?",
            "No. It runs synchronously and returns an already-completed task (compiler warning CS1998). `async` only enables\n" +
            "await and wraps the return value or exception into a Task."),
        new("Why do async methods have overhead and when is it avoidable?",
            "Each call that actually suspends allocates a state-machine box and a Task. Synchronous completions are cheaper;\n" +
            "ValueTask or returning a cached Task avoids more. For trivial pass-through methods you can return the Task\n" +
            "directly (eliding async/await) but only if there's no using/try/catch around it (lesson 17)."),
    ];
}

internal static class TimeSpanAwaiterExtensions
{
    public static System.Runtime.CompilerServices.TaskAwaiter GetAwaiter(this TimeSpan delay) =>
        Task.Delay(delay).GetAwaiter();
}
