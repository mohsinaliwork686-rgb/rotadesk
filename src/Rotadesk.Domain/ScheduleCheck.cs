namespace Rotadesk.Domain;

/// <summary>A broken hard rule. The change is refused.</summary>
public sealed record RuleViolation(string Code, string Message);

/// <summary>The result of checking one person's shifts. Violations refuse the change; warnings only inform.</summary>
public sealed record ScheduleCheck(IReadOnlyList<RuleViolation> Violations, IReadOnlyList<string> Warnings)
{
    public bool IsAllowed => Violations.Count == 0;

    public static ScheduleCheck Combine(params ScheduleCheck[] checks) =>
        new([.. checks.SelectMany(c => c.Violations)], [.. checks.SelectMany(c => c.Warnings)]);

    /// <summary>Throws the first violation, so callers can't forget to look.</summary>
    public void ThrowIfViolated()
    {
        if (Violations.Count > 0)
        {
            throw new DomainException(Violations[0].Code, Violations[0].Message);
        }
    }
}
