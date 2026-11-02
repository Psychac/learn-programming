using practice_async.Infrastructure;

namespace practice_async.Lessons;

internal sealed class L02_TaskBasics : Lesson
{
    public override string Title => "Task & Task<T>: the claim ticket";
    public override string Summary => "A Task is a promise object, not a thread: states, results, many awaiters, hot vs cold";

    public override string Concept => """
        A Task is the CLAIM TICKET / pizza-shop buzzer you get when you place an order.
          * The ticket is not the pizza and it is not the cook.
          * You can look at it any time: "ready yet? did the oven explode?" (Status / Exception)
          * You can trade it in for the pizza (await / .Result)
          * Several people can hold copies and all be told when it's ready (multiple continuations)

        Internally a Task is just an object: { Status, Result or Exception, list of continuations }.
        Whoever owns the work (a timer, the OS, a pool thread running your delegate) flips it to a final
        state, and that fires the continuations. NO THREAD IS "INSIDE" A TASK.

        Task      = promise of completion (no value)       Task<T> = promise of a T
        Final states: RanToCompletion, Faulted (exception), Canceled
        """;

    public override async Task RunAsync()
    {
        Log.Step("DEMO 1: watch a Task change state (polling every 100ms)");
        var tcs = new TaskCompletionSource<int>();
        Task<int> ticket = tcs.Task;
        _ = Task.Run(async () => { await Task.Delay(350); tcs.SetResult(42); });
        for (int i = 0; i < 5; i++)
        {
            Log.Info($"poll {i}: Status={ticket.Status,-20} IsCompleted={ticket.IsCompleted}");
            await Task.Delay(100);
        }
        Log.Note("WaitingForActivation = 'a promise nobody has fulfilled yet'. No thread was running for this ticket\n" +
                 "between polls — a Task from a TaskCompletionSource has no code attached at all.");

        Log.Step("DEMO 2: the three final states");
        Task ok = Task.FromResult(1);
        Task bad = Task.FromException(new InvalidOperationException("kitchen fire"));
        Task cancelled = Task.FromCanceled(new CancellationToken(true));
        Log.Info($"ok        -> {ok.Status}");
        Log.Info($"bad       -> {bad.Status}   Exception={bad.Exception!.InnerException!.Message}");
        Log.Info($"cancelled -> {cancelled.Status}");
        Log.Note("Task.FromResult/CompletedTask are 'already done' tickets: perfect for sync fast paths and tests.");

        Log.Step("DEMO 3: who runs the work? Task.Run uses a thread, Task.Delay does not");
        Task running = Task.Run(() => { Log.Info("Task.Run delegate executing ON a pool thread"); Thread.Sleep(150); });
        Task delay = Task.Delay(150);
        Log.Info($"Task.Run : Status={running.Status}   (a thread is executing the delegate)");
        Log.Info($"Task.Delay: Status={delay.Status}  (just a timer registration — no thread at all)");
        await Task.WhenAll(running, delay);

        Log.Step("DEMO 4: a Task can be awaited by MANY consumers (and many times)");
        Task<string> shared = Task.Run(async () => { await Task.Delay(100); return "pizza"; });
        Task a = Consumer("A", shared), b = Consumer("B", shared);
        await Task.WhenAll(a, b);
        Log.Info($"awaiting again after completion returns instantly: {await shared}");
        Log.Note("Awaiting does not START or CONSUME anything. The work was already running; await just subscribes.");

        Log.Step("DEMO 5: hot vs cold");
        var cold = new Task<string>(() => "cold");
        Log.Info($"new Task<string>(...)  Status={cold.Status}  <- nothing runs until Start()");
        cold.Start();
        Log.Info($"after Start: {await cold}");
        Log.Note("Every Task you get back from a method (Task.Run, async methods, HttpClient...) is HOT: already started.\n" +
                 "So `var t = DoAsync();` starts the work immediately; `await t` only waits for it.");

        Log.Step("DEMO 6: consuming a task — await vs .Result vs ContinueWith");
        Task<int> job = Task.Run(() => { Thread.Sleep(100); return 5; });
        Log.Note("await t        : releases the thread; resumes later; throws the ORIGINAL exception\n" +
                 ".Result/.Wait(): BLOCKS this thread until done; wraps errors in AggregateException; deadlock risk (lesson 7)\n" +
                 "ContinueWith   : manual callback, no state-machine niceties, defaults to TaskScheduler.Current (a trap)");
        Log.Info($"await gives {await job}");
    }

    private static async Task Consumer(string name, Task<string> t)
    {
        string result = await t;
        Log.Info($"consumer {name} got '{result}'");
    }

    public override IEnumerable<QA> Interview =>
    [
        new("What is a Task? Is it a thread?",
            "A Task is a reference-type object representing an operation's eventual outcome: status, result/exception and\n" +
            "continuations. It is not a thread. Some tasks are backed by a pool thread running a delegate (Task.Run); many\n" +
            "(I/O, Task.Delay, TaskCompletionSource) have no thread at all while pending."),
        new("What are the possible Task states?",
            "Created, WaitingForActivation, WaitingToRun, Running, WaitingForChildrenToComplete, and final states\n" +
            "RanToCompletion, Faulted, Canceled. IsCompleted is true for all three final states."),
        new("Can you await the same Task more than once, or from multiple places?",
            "Yes. A Task is a shared result holder; each await registers its own continuation or sees the stored outcome.\n" +
            "(ValueTask is different: it must be awaited once.)"),
        new("What does `await` do to a task that is already running?",
            "It doesn't start or restart anything. It only registers a continuation, or continues synchronously if the task\n" +
            "is already complete."),
        new("Task.FromResult vs Task.Run(() => value)?",
            "FromResult returns an already-completed task with no scheduling cost, for values you already have. Task.Run queues\n" +
            "work to the thread pool. Using Task.Run to 'return a value' wastes a thread hop."),
        new("Why is `new Task(...)` almost never used?",
            "It creates a cold task that you must Start(). All modern APIs return hot tasks; cold ones cause confusing bugs\n" +
            "(awaiting a never-started task hangs forever)."),
    ];
}
