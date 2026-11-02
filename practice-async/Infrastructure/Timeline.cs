using System.Diagnostics;

namespace practice_async.Infrastructure;

internal enum SpanKind
{
    /// <summary>Thread is busy computing (CPU).</summary>
    Work,
    /// <summary>Thread is held but doing nothing (Thread.Sleep, .Result, sync I/O).</summary>
    Blocked,
    /// <summary>Logically waiting, NO thread held (await Task.Delay / async I/O).</summary>
    Waiting,
}

/// <summary>
/// Records "who was doing what, when" and draws an ASCII Gantt chart. This is the main tool for making
/// concurrency visible:  █ working   ▓ thread BLOCKED (held, idle)   ░ waiting, thread FREE.
/// </summary>
internal sealed class Timeline
{
    private sealed record Span(string Row, SpanKind Kind, long Start, long End, int ThreadStart, int ThreadEnd);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Span> _spans = [];
    private readonly List<string> _rowOrder = [];
    private readonly object _gate = new();

    /// <summary>Asynchronous wait: no thread is held. May resume on a different thread.</summary>
    public async Task WaitAsync(string row, int ms, CancellationToken ct = default)
    {
        long start = _clock.ElapsedMilliseconds;
        int t0 = Environment.CurrentManagedThreadId;
        try { await Task.Delay(ms, ct); }
        finally { Add(row, SpanKind.Waiting, start, t0); }
    }

    /// <summary>Synchronous wait: the thread is held and idle.</summary>
    public void Block(string row, int ms)
    {
        long start = _clock.ElapsedMilliseconds;
        int t0 = Environment.CurrentManagedThreadId;
        Thread.Sleep(ms);
        Add(row, SpanKind.Blocked, start, t0);
    }

    /// <summary>CPU work: the thread is genuinely busy.</summary>
    public void Work(string row, int ms)
    {
        long start = _clock.ElapsedMilliseconds;
        int t0 = Environment.CurrentManagedThreadId;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) Thread.SpinWait(200);
        Add(row, SpanKind.Work, start, t0);
    }

    private void Add(string row, SpanKind kind, long start, int threadStart)
    {
        lock (_gate)
        {
            if (!_rowOrder.Contains(row)) _rowOrder.Add(row);
            _spans.Add(new Span(row, kind, start, _clock.ElapsedMilliseconds, threadStart, Environment.CurrentManagedThreadId));
        }
    }

    public long ElapsedMs => _clock.ElapsedMilliseconds;

    /// <summary>Draw the chart. Each character is <paramref name="msPerChar"/> milliseconds.</summary>
    public void Render(int msPerChar = 25)
    {
        lock (_gate)
        {
            if (_spans.Count == 0) return;
            long min = _spans.Min(s => s.Start), max = _spans.Max(s => s.End);
            int width = (int)Math.Min(110, (max - min) / msPerChar + 1);
            int labelWidth = _rowOrder.Max(r => r.Length);

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            var axis = new char[width];
            Array.Fill(axis, ' ');
            var marks = new System.Text.StringBuilder();
            for (int c = 0; c < width; c += 10)
            {
                string label = $"{c * msPerChar}ms";
                marks.Append(label.PadRight(10));
            }
            Console.WriteLine($"  {new string(' ', labelWidth)}  {marks}");
            foreach (var row in _rowOrder)
            {
                var line = new char[width];
                Array.Fill(line, ' ');
                foreach (var s in _spans.Where(s => s.Row == row))
                {
                    int a = (int)((s.Start - min) / msPerChar);
                    int b = Math.Max(a + 1, (int)((s.End - min) / msPerChar));
                    char ch = s.Kind switch { SpanKind.Work => '█', SpanKind.Blocked => '▓', _ => '░' };
                    for (int i = a; i < b && i < width; i++) line[i] = ch;
                }
                var threads = _spans.Where(s => s.Row == row)
                    .Select(s => s.ThreadStart == s.ThreadEnd ? $"T{s.ThreadStart}" : $"T{s.ThreadStart}→T{s.ThreadEnd}");
                Console.WriteLine($"  {row.PadRight(labelWidth)} |{new string(line)}| {string.Join(" ", threads)}");
            }
            Console.WriteLine($"  {new string(' ', labelWidth)}  total {max - min}ms   (█ working   ▓ thread BLOCKED   ░ waiting, thread FREE)");
            Console.ResetColor();
        }
    }
}
