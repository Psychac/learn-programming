using System.Diagnostics;

namespace practice_async.Infrastructure;

/// <summary>
/// Console output helper. Every line shows elapsed time and the thread it ran on, because
/// "which thread am I on, and when?" is the question that makes async code make sense.
///   pool = ThreadPool thread, ctx = a thread owning a SynchronizationContext, main = anything else.
/// </summary>
internal static class Log
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();

    public static void Header(string text)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine(new string('=', 78));
            Console.WriteLine(text);
            Console.WriteLine(new string('=', 78));
            Console.ResetColor();
        }
    }

    /// <summary>A boxed plain-English explanation (the mental model for the lesson).</summary>
    public static void Concept(string text)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
            Console.WriteLine("  ┌─ THE IDEA " + new string('─', 64));
            foreach (var line in text.Split('\n'))
                Console.WriteLine($"  │ {line.TrimEnd()}");
            Console.WriteLine("  └" + new string('─', 75));
            Console.ResetColor();
        }
    }

    /// <summary>A numbered step inside a lesson.</summary>
    public static void Step(string text)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine($"--- {text}");
            Console.ResetColor();
        }
    }

    /// <summary>Teaching commentary (what you should notice).</summary>
    public static void Note(string text)
    {
        lock (Gate)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            foreach (var line in text.Split('\n'))
                Console.WriteLine($"  # {line.TrimEnd()}");
            Console.ResetColor();
        }
    }

    public static void Info(string text) => Write(ConsoleColor.Gray, text);
    public static void Good(string text) => Write(ConsoleColor.Green, text);
    public static void Warn(string text) => Write(ConsoleColor.Red, text);

    public static string ThreadLabel()
    {
        var kind = SynchronizationContext.Current is not null ? "ctx"
            : Thread.CurrentThread.IsThreadPoolThread ? "pool"
            : "main";
        return $"T{Environment.CurrentManagedThreadId,-2} {kind,-4}";
    }

    private static void Write(ConsoleColor color, string text)
    {
        lock (Gate)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"  [{Clock.ElapsedMilliseconds,6}ms | {ThreadLabel()}] {text}");
            Console.ResetColor();
        }
    }
}
