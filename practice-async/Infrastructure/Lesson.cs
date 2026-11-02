namespace practice_async.Infrastructure;

/// <summary>An interview question with a model answer.</summary>
internal sealed record QA(string Question, string Answer);

/// <summary>One self-contained lesson. Read the source file alongside its console output.</summary>
internal abstract class Lesson
{
    public abstract string Title { get; }
    public abstract string Summary { get; }

    /// <summary>Plain-English mental model / analogy, printed BEFORE the demos run.</summary>
    public virtual string Concept => "";

    /// <summary>Interview questions (with model answers) printed AFTER the demos and fed to the mock interview.</summary>
    public virtual IEnumerable<QA> Interview => [];

    public abstract Task RunAsync();
}
