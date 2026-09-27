namespace Strata.Core.Content;

public enum IssueSeverity
{
    Warning,
    Error,
}

/// <summary>A problem found while loading or validating content.</summary>
public sealed record ContentIssue(IssueSeverity Severity, string Source, string Subject, string Message)
{
    public override string ToString() => $"{Severity}: {Source} [{Subject}] {Message}";
}

/// <summary>Accumulates issues from loading and validation.</summary>
public sealed class ContentReport
{
    private readonly List<ContentIssue> _issues = new();

    public IReadOnlyList<ContentIssue> Issues => _issues;
    public IEnumerable<ContentIssue> Errors => _issues.Where(i => i.Severity == IssueSeverity.Error);
    public IEnumerable<ContentIssue> Warnings => _issues.Where(i => i.Severity == IssueSeverity.Warning);
    public bool HasErrors => _issues.Any(i => i.Severity == IssueSeverity.Error);

    public void Error(string source, string subject, string message) =>
        _issues.Add(new ContentIssue(IssueSeverity.Error, source, subject, message));

    public void Warn(string source, string subject, string message) =>
        _issues.Add(new ContentIssue(IssueSeverity.Warning, source, subject, message));

    public void Error(ContentDef def, string message) => Error(def.SourceFile, $"{def.GetType().Name}:{def.Id}", message);

    public void Warn(ContentDef def, string message) => Warn(def.SourceFile, $"{def.GetType().Name}:{def.Id}", message);

    public override string ToString() => string.Join(Environment.NewLine, _issues);
}
