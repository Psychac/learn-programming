using System.Diagnostics;
using practice_async.Infrastructure;

namespace practice_async.Lessons;

internal sealed class L04_ThreadsAndScheduler : Lesson
{
    public override string Title => "Threads, the ThreadPool and TaskScheduler";
    public override string Summary => "Thread vs pool vs Task, Task.Run vs StartNew, LongRunning, custom schedulers";

    public override string Concept => """
        THREAD        = an employee. Expensive to hire (≈1MB stack, OS scheduling), so you don't hire one per job.
        THREAD POOL   = a staffing agency with a small team of reusable employees and a shared job queue.
                        If every employee is stuck staring at something (blocked), new ones are added SLOWLY.
        TASK          = a job ticket (lesson 2).
        TASKSCHEDULER = the manager deciding WHICH employee runs a job, and when.
                        Default scheduler = "put it on the thread pool queue".

        Task.Run(work)  = "put this job on the pool queue".
        await           = "when my ticket is done, schedule the rest of my method" (via SyncContext/scheduler).
        """;

    public override async Task RunAsync()
    {
        Log.Step("DEMO 1: pool threads are REUSED — 30 jobs, how many distinct threads?");
        var ids = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() =>
        {
            ids.AddOrUpdate(Environment.CurrentManagedThreadId, 1, (_, c) => c + 1);
            Thread.Sleep(5);
        })));
        Log.Info($"30 jobs ran on {ids.Count} distinct threads (cores={Environment.ProcessorCount}): {string.Join(", ", ids.Select(k => $"T{k.Key}x{k.Value}"))}");

        Log.Step("DEMO 2: cost of a dedicated Thread vs a queued pool job (200 trivial jobs)");
        var sw = Stopwatch.StartNew();
        var threads = Enumerable.Range(0, 200).Select(_ => new Thread(() => { }) { IsBackground = true }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        long threadMs = sw.ElapsedMilliseconds;
        sw.Restart();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => { })));
        Log.Info($"200 new Threads: {threadMs}ms    200 Task.Run: {sw.ElapsedMilliseconds}ms   (pool wins by reuse)");

        Log.Step("DEMO 3: Task.Run vs Task.Factory.StartNew with an async lambda (classic gotcha)");
        var viaRun = Task.Run(async () => { await Task.Delay(50); return 1; });
        var viaStartNew = Task.Factory.StartNew(async () => { await Task.Delay(50); return 1; });
        Log.Info($"Task.Run     returns a Task<int> (runtime type {Pretty(viaRun.GetType())}): awaiting it waits for the async lambda to FINISH");
        Log.Warn($"StartNew     returns {Pretty(viaStartNew.GetType())}   <- the outer task completes when the lambda RETURNS ITS TASK, not when it finishes");
        await viaStartNew;
        Log.Info($"after `await viaStartNew` the inner task is {(viaStartNew.Result.IsCompleted ? "done" : "STILL RUNNING")}  (we awaited the wrong layer)");
        Log.Good($"fix: `.Unwrap()` or just use Task.Run -> {await viaStartNew.Unwrap()}");

        Log.Step("DEMO 4: TaskCreationOptions.LongRunning gets a DEDICATED thread (not from the pool)");
        await Task.Factory.StartNew(
            () => Log.Info($"LongRunning: IsThreadPoolThread={Thread.CurrentThread.IsThreadPoolThread}"),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await Task.Run(() => Log.Info($"Task.Run   : IsThreadPoolThread={Thread.CurrentThread.IsThreadPoolThread}"));
        Log.Note("Use LongRunning for a loop that lives for the app's lifetime so it doesn't permanently tie up a pool worker.");

        Log.Step("DEMO 5: a TaskScheduler decides HOW MANY run at once");
        var normal = new Timeline();
        await Task.WhenAll(Enumerable.Range(1, 4).Select(i => Task.Run(() => normal.Work($"job {i}", 150))));
        Log.Info("Default scheduler (pool): jobs overlap on different threads");
        normal.Render();

        var pair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrencyLevel: 2);
        var limited = new Timeline();
        await Task.WhenAll(Enumerable.Range(1, 4).Select(i => Task.Factory.StartNew(
            () => limited.Work($"job {i}", 150), CancellationToken.None, TaskCreationOptions.None, pair.ConcurrentScheduler)));
        Log.Info("ConcurrentExclusiveSchedulerPair(maxConcurrency: 2): only two at a time");
        limited.Render();

        Log.Step("DEMO 6: the ContinueWith trap — it uses TaskScheduler.Current, not Default");
        Log.Note("If ContinueWith is called from code running on a custom scheduler (or inside a StartNew task), the\n" +
                 "continuation inherits THAT scheduler. Always pass TaskScheduler.Default explicitly, or just use await.");
        await Task.Factory.StartNew(() =>
        {
            Log.Info($"inside custom scheduler task: TaskScheduler.Current = {(TaskScheduler.Current == pair.ConcurrentScheduler ? "the custom one" : "default")}");
            return Task.CompletedTask.ContinueWith(_ =>
                Log.Warn($"continuation silently ran on: {(TaskScheduler.Current == pair.ConcurrentScheduler ? "the custom scheduler" : "default")}"));
        }, CancellationToken.None, TaskCreationOptions.None, pair.ConcurrentScheduler).Unwrap();
    }

    private static string Pretty(Type t) =>
        t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(Pretty))}>" : t.Name;

    public override IEnumerable<QA> Interview =>
    [
        new("Thread vs ThreadPool thread vs Task?",
            "A Thread is an OS-level execution unit (heavy). A pool thread is a reusable thread managed by the CLR to run queued\n" +
            "work items cheaply. A Task is an abstraction for an operation's outcome; it may be run by a pool thread, or by no\n" +
            "thread at all (I/O, timers)."),
        new("How does the .NET ThreadPool scale its threads?",
            "It starts with a minimum (≈ core count) and creates threads immediately up to that. Beyond it, the hill-climbing\n" +
            "algorithm injects threads slowly (about 1-2 per second) based on throughput. Blocking pool threads therefore causes\n" +
            "thread-pool starvation: queued work waits while CPUs idle."),
        new("Task.Run vs Task.Factory.StartNew?",
            "Task.Run = StartNew with safe defaults (TaskScheduler.Default, DenyChildAttach) and it unwraps async lambdas,\n" +
            "so Task.Run(async () => ...) gives you a Task that completes when the async work does. StartNew with an async lambda\n" +
            "returns Task<Task> unless you .Unwrap(), and can inherit TaskScheduler.Current. Prefer Task.Run; StartNew only for\n" +
            "LongRunning or custom schedulers."),
        new("What does TaskCreationOptions.LongRunning do?",
            "Hints the default scheduler to use a dedicated (non-pool) thread, so a long-lived loop doesn't occupy a pool worker."),
        new("What is a TaskScheduler and when would you write one?",
            "It decides where/when a Task's delegate executes. Default = thread pool. SynchronizationContext-based schedulers\n" +
            "(TaskScheduler.FromCurrentSynchronizationContext) run on a UI thread. Custom ones are rare: limiting concurrency,\n" +
            "priorities, or running on a specific thread (ConcurrentExclusiveSchedulerPair covers many cases)."),
        new("Why is ContinueWith considered dangerous compared with await?",
            "It uses TaskScheduler.Current (can be a surprising custom scheduler), doesn't unwrap AggregateException, doesn't\n" +
            "capture SynchronizationContext the same way, needs manual error/cancellation handling and nested Unwrap for async\n" +
            "continuations. await handles all of these."),
    ];
}
