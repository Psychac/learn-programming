using practice_async.Infrastructure;

namespace practice_async.Lessons;

/// <summary>
/// KEY IDEA: cancellation in .NET is COOPERATIVE. A CancellationTokenSource signals; the work must
/// observe its CancellationToken and stop (usually by throwing OperationCanceledException).
/// </summary>
internal sealed class L09_Cancellation : Lesson
{
    public override string Title => "Cancellation & timeouts";
    public override string Summary => "CancellationToken, CancelAfter, linked tokens, cooperative checks, WaitAsync";

    public override string Concept => """
            Cancellation is a POLITE NOTE on the door, not a police raid.

              CancellationTokenSource = the person who can hang the note "please stop"
              CancellationToken       = what the worker is allowed to look at
              The worker must CHECK the note (ThrowIfCancellationRequested, pass the token to token-aware APIs)
              and stop on its own (OperationCanceledException). If the worker never looks, nothing stops.
            """;

    public override async Task RunAsync()
    {
        Log.Step("1. Basic: cancel a running operation");
        using (var cts = new CancellationTokenSource())
        {
            Task work = CountAsync("A", cts.Token);
            await Task.Delay(350);
            Log.Info("requesting cancellation");
            cts.Cancel();
            try { await work; }
            catch (OperationCanceledException) { Log.Good($"work ended via OperationCanceledException; status={work.Status}"); }
        }
        Log.Note("Canceled (not Faulted) status results when the OCE carries the task's own token.");

        Log.Step("2. Timeout with CancelAfter");
        using (var cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(250));
            try { await CountAsync("B", cts.Token); }
            catch (OperationCanceledException) { Log.Good("timed out -> cancelled automatically"); }
        }

        Log.Step("3. Linked tokens: cancel if the user cancels OR a timeout elapses");
        using (var user = new CancellationTokenSource())
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(user.Token, timeout.Token))
        {
            Task work = CountAsync("C", linked.Token);
            await Task.Delay(250);
            user.Cancel(); // propagates to 'linked'
            try { await work; } catch (OperationCanceledException) { Log.Good("linked token cancelled by the user source"); }
        }

        Log.Step("4. Cooperative: ignoring the token means NO cancellation");
        using (var cts = new CancellationTokenSource())
        {
            Task stubborn = IgnoresTokenAsync(cts.Token);
            cts.Cancel();
            Log.Info("Cancel() called. Token says IsCancellationRequested=true, but the work doesn't look.");
            await stubborn;
            Log.Warn("stubborn work ran to completion anyway.");
        }

        Log.Step("5. CPU loops: poll with token.ThrowIfCancellationRequested()");
        using (var cts = new CancellationTokenSource(100))
        {
            try
            {
                await Task.Run(() =>
                {
                    long sum = 0;
                    for (long i = 0; i < long.MaxValue; i++)
                    {
                        if (i % 1_000_000 == 0) cts.Token.ThrowIfCancellationRequested();
                        sum += i;
                    }
                    Log.Info($"unreachable in practice: {sum}");
                }, cts.Token);
            }
            catch (OperationCanceledException) { Log.Good("CPU-bound loop stopped cooperatively"); }
        }

        Log.Step("6. Callbacks on cancel: token.Register for cleanup/abort of non-token-aware APIs");
        using (var cts = new CancellationTokenSource())
        {
            using var reg = cts.Token.Register(() => Log.Info("cancel callback: e.g. close a socket / abort a request"));
            cts.Cancel();
        }

        Log.Step("7. Make a non-cancellable task 'give up waiting' with WaitAsync (.NET 6+)");
        Task slow = Task.Delay(2000);
        try { await slow.WaitAsync(TimeSpan.FromMilliseconds(200)); }
        catch (TimeoutException) { Log.Good("stopped WAITING after 200ms (the underlying task is still running!)"); }
        Log.Note("WaitAsync abandons the wait, it does not stop the work. Pass tokens into the work for real cancellation.");

        Log.Step("8. Catching cancellation vs real errors");
        using (var cts = new CancellationTokenSource(50))
        {
            try { await CountAsync("D", cts.Token); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { Log.Info("expected cancellation (filtered with 'when')"); }
            catch (Exception ex) { Log.Warn($"real failure: {ex.Message}"); }
        }

        Log.Step("Takeaways");
        Log.Note("* Accept a CancellationToken as the LAST parameter of every async API and pass it down.\n" +
                 "* Cancellation = request, not force. Work must check the token / call token-aware APIs.\n" +
                 "* Dispose CancellationTokenSource (it may hold a timer).\n" +
                 "* Use linked sources to combine user cancel + timeout.");
    }

    private static async Task CountAsync(string name, CancellationToken ct)
    {
        for (int i = 1; ; i++)
        {
            ct.ThrowIfCancellationRequested();
            Log.Info($"{name}: tick {i}");
            await Task.Delay(100, ct); // token-aware: wakes immediately on cancel
        }
    }

    private static async Task IgnoresTokenAsync(CancellationToken _)
    {
        await Task.Delay(200); // never passes the token down
    }

    public override IEnumerable<QA> Interview =>
    [
        new("""
            How does cancellation work in .NET?
            """, """
            Cooperative. A CancellationTokenSource owns the signal; CancellationTokens are passed to work, which periodically checks token.IsCancellationRequested /
            ThrowIfCancellationRequested or passes it to APIs that observe it. Cancel() requests; the work ends by throwing OperationCanceledException so the Task ends Canceled.
            """),
        new("""
            Why do you have to dispose a CancellationTokenSource?
            """, """
            It may own a timer (CancelAfter) and registration resources, and linked sources register with their parents. Dispose to release them, but not while still in use.
            """),
        new("""
            How do you combine a user cancellation and a timeout?
            """, """
            CancellationTokenSource.CreateLinkedTokenSource(userToken, timeoutCts.Token), or a linked CTS with CancelAfter. Distinguish the reason with a `when (timeoutCts.IsCancellationRequested)` filter.
            """),
        new("""
            What is the difference between cancelling and Task.WaitAsync(timeout)?
            """, """
            WaitAsync stops waiting and throws TimeoutException but the underlying operation keeps running. A token tells the operation itself to stop.
            """),
        new("""
            How do you make cancelable code that isn't token-aware?
            """, """
            Use token.Register(callback) to abort the underlying resource (close socket), or poll the token in loops. For tasks you can't stop, use WaitAsync(token) to at least abandon waiting.
            """),
        new("""
            Where should the CancellationToken parameter go?
            """, """
            Last parameter, named cancellationToken, often optional (default). Every async API that can take a long time should accept one and pass it down.
            """),
    ];
}
