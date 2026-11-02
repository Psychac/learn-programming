using practice_async.Infrastructure;
using practice_async.Lessons;

namespace practice_async
{
    internal class Program
    {
        private static readonly Lesson[] Lessons =
        [
            new L01_WhyAsync(),
            new L02_TaskBasics(),
            new L03_AwaitMechanics(),
            new L04_ThreadsAndScheduler(),
            new L05_SyncContext(),
            new L06_ExecutionContext(),
            new L07_Deadlock(),
            new L08_Exceptions(),
            new L09_Cancellation(),
            new L10_Composition(),
            new L11_TaskCompletionSource(),
            new L12_AsyncStreams(),
            new L13_ValueTask(),
            new L14_Channels(),
            new L15_Synchronization(),
            new L16_CpuVsIo(),
            new L17_Pitfalls(),
            new L18_InterviewLab(),
        ];

        // Usage: dotnet run -c Release                 -> interactive menu
        //        dotnet run -c Release -- 3            -> lesson 3
        //        dotnet run -c Release -- all --auto   -> everything, no prompts
        //        dotnet run -c Release -- interview    -> mock interview     (also: cheat)
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Contains("--auto")) Quiz.Interactive = false;
            string? choice = args.FirstOrDefault(a => !a.StartsWith("--"));

            if (choice is not null)
            {
                await RunChoiceAsync(choice);
                return;
            }

            while (true)
            {
                PrintMenu();
                Console.Write("> ");
                string? input = Console.ReadLine()?.Trim();
                if (input is null || input.Equals("q", StringComparison.OrdinalIgnoreCase)) return;
                await RunChoiceAsync(input);
                Console.WriteLine("\nPress Enter to return to the menu...");
                Console.ReadLine();
            }
        }

        private static async Task RunChoiceAsync(string input)
        {
            switch (input.ToLowerInvariant())
            {
                case "a" or "all":
                    for (int i = 0; i < Lessons.Length; i++) await RunLessonAsync(i);
                    return;
                case "i" or "interview":
                    Quiz.MockInterview(Lessons.SelectMany(l => l.Interview.Select(q => ($"{l.Title}", q))).ToList());
                    return;
                case "c" or "cheat":
                    CheatSheet.Print();
                    return;
            }

            if (int.TryParse(input, out int n) && n >= 1 && n <= Lessons.Length)
                await RunLessonAsync(n - 1);
            else
                Console.WriteLine("Unknown choice.");
        }

        private static async Task RunLessonAsync(int index)
        {
            Lesson lesson = Lessons[index];
            Log.Header($"LESSON {index + 1}/{Lessons.Length}: {lesson.Title}\n{lesson.Summary}\nSource: Lessons/{lesson.GetType().Name}.cs");
            if (lesson.Concept.Length > 0) Log.Concept(lesson.Concept);
            if (Quiz.Interactive)
            {
                Console.Write("\n  Read the idea above, then press Enter to run the demos...");
                Console.ReadLine();
            }
            try
            {
                await lesson.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Warn($"Lesson failed: {ex}");
            }
            Quiz.Interview(lesson.Interview);
        }

        private static void PrintMenu()
        {
            Log.Header("C# ASYNC / AWAIT — from first principles to interview-ready");
            Console.WriteLine("  FOUNDATIONS");
            for (int i = 0; i < Lessons.Length; i++)
            {
                if (i == 7) Console.WriteLine("\n  ERRORS, CANCELLATION & COMPOSITION");
                if (i == 10) Console.WriteLine("\n  TOOLBOX: TCS, STREAMS, VALUETASK, CHANNELS, SYNCHRONIZATION");
                if (i == 15) Console.WriteLine("\n  PRACTICE");
                Console.WriteLine($"   {i + 1,2}. {Lessons[i].Title}");
            }
            Console.WriteLine("""

               a  run everything          i  MOCK INTERVIEW (all questions, shuffled, self-graded)
               c  cheat sheet             q  quit
              Suggested path: 1 -> 18 in order. Each lesson: idea -> predict -> demo -> interview check.
            """);
        }
    }
}
