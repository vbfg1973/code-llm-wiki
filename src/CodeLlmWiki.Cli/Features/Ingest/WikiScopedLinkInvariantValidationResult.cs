using System.Text;

namespace CodeLlmWiki.Cli.Features.Ingest;

public sealed record WikiScopedLinkInvariantValidationResult(IReadOnlyList<WikiScopedLinkInvariantViolation> Violations)
{
    public bool IsValid => Violations.Count == 0;

    public string ToFailureMessage(int maxViolations = 20)
    {
        if (IsValid)
        {
            return "Scoped wiki link invariants passed.";
        }

        var builder = new StringBuilder();
        builder.Append("Scoped wiki link invariants failed with ");
        builder.Append(Violations.Count);
        builder.Append(" violation(s).");

        foreach (var violation in Violations.Take(Math.Max(1, maxViolations)))
        {
            builder.AppendLine();
            builder.Append("- ");
            builder.Append(violation.PageRelativePath);
            builder.Append(':');
            builder.Append(violation.LineNumber);
            builder.Append(" [");
            builder.Append(violation.SectionPath);
            builder.Append("] ");
            builder.Append(violation.Message);
            builder.Append(" | ");
            builder.Append(violation.LineText.Trim());
        }

        if (Violations.Count > maxViolations)
        {
            builder.AppendLine();
            builder.Append("+ ");
            builder.Append(Violations.Count - maxViolations);
            builder.Append(" additional violation(s).");
        }

        return builder.ToString();
    }
}
