namespace practice_async.Infrastructure;

/// <summary>
/// Interactive learning helpers: "predict before you run" questions and interview Q&amp;A.
/// When input is redirected or --auto is passed, nothing waits for the keyboard.
/// </summary>
internal static class Quiz
{
    public static bool Interactive { get; set; } = !Console.IsInputRedirected;

    public static Prediction Predict(string question, string[] options, int correct, string why)
    {
        Log.Step("PREDICT before you look: " + question);
        for (int i = 0; i < options.Length; i++)
            Console.WriteLine($"     {(char)('A' + i)}) {options[i]}");

        string? guess = null;
        if (Interactive)
        {
            Console.Write("  Your guess (letter, or Enter to skip) > ");
            guess = Console.ReadLine()?.Trim().ToUpperInvariant();
        }
        return new Prediction(options, correct, why, guess);
    }

    /// <summary>Show each question, wait for Enter, then show the model answer.</summary>
    public static void Interview(IEnumerable<QA> questions)
    {
        var list = questions.ToList();
        if (list.Count == 0) return;
        Log.Header("INTERVIEW CHECK — answer out loud first, then compare");
        int n = 1;
        foreach (var qa in list)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\nQ{n++}. {qa.Question}");
            Console.ResetColor();
            if (Interactive)
            {
                Console.Write("   (press Enter to reveal a model answer) ");
                Console.ReadLine();
            }
            PrintAnswer(qa.Answer);
        }
    }

    /// <summary>Shuffled self-graded drill across every lesson's questions.</summary>
    public static void MockInterview(IReadOnlyList<(string Topic, QA Qa)> all)
    {
        var rng = new Random();
        var deck = all.OrderBy(_ => rng.Next()).ToList();
        int known = 0, asked = 0;
        var weak = new SortedSet<string>();
        Log.Header($"MOCK INTERVIEW — {deck.Count} questions, shuffled. Answer aloud, reveal, grade yourself honestly.");
        foreach (var (topic, qa) in deck)
        {
            asked++;
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\n[{asked}/{deck.Count}] ({topic})  {qa.Question}");
            Console.ResetColor();
            if (!Interactive) { PrintAnswer(qa.Answer); continue; }
            Console.Write("   (Enter = reveal, q = quit) ");
            if (Console.ReadLine()?.Trim().Equals("q", StringComparison.OrdinalIgnoreCase) == true) { asked--; break; }
            PrintAnswer(qa.Answer);
            Console.Write("   Did you get it right? (y/n/q) > ");
            string? r = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (r == "q") break;
            if (r == "y") known++; else weak.Add(topic);
        }
        if (Interactive)
        {
            Log.Header($"Score: {known}/{asked}");
            if (weak.Count > 0) Console.WriteLine("Revisit these lessons: " + string.Join(" | ", weak));
        }
    }

    private static void PrintAnswer(string answer)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        foreach (var line in answer.Split('\n'))
            Console.WriteLine($"   A: {line.TrimEnd()}");
        Console.ResetColor();
    }
}

internal sealed class Prediction(string[] options, int correct, string why, string? guess)
{
    public void Reveal()
    {
        Log.Step("REVEAL");
        string right = $"{(char)('A' + correct)}) {options[correct]}";
        if (string.IsNullOrEmpty(guess))
            Console.WriteLine($"  Correct answer: {right}");
        else if (guess[0] == (char)('A' + correct))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  You said {guess}: CORRECT — {right}");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  You said {guess}: not quite. Correct answer: {right}");
        }
        Console.ResetColor();
        Log.Note("Why: " + why);
    }
}
