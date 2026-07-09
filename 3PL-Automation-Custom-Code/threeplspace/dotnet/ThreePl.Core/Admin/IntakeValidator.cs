namespace ThreePl.Core.Admin;

public sealed class ValidationResult
{
    public required IReadOnlyList<string> MissingLabels { get; init; }
    /// <summary>Field names (incl. table pseudo-fields) to highlight as invalid.</summary>
    public required IReadOnlySet<string> InvalidFields { get; init; }
    public bool IsValid => MissingLabels.Count == 0;
}

/// <summary>
/// Save-time validation, porting the HTML's validateDomain(): every
/// currently-required field must be non-empty (direction-aware via the
/// effective admin levels), email-flagged fields must contain '@' when
/// filled, and required tables need at least one row.
/// </summary>
public static class IntakeValidator
{
    public static ValidationResult Validate(
        string domain,
        string? direction,
        IReadOnlyDictionary<string, string?> fieldValues,
        IReadOnlyDictionary<string, int> tableCounts,
        IReadOnlyDictionary<(string Domain, string Field), RequirementLevel> levels)
    {
        var def = FieldDefinitions.Get(domain);
        var missing = new List<string>();
        var invalid = new HashSet<string>();

        foreach (var field in def.Fields)
        {
            var level = levels.TryGetValue((def.Domain, field.Name), out var l) ? l : field.DefaultLevel;
            var required = FieldRequirementService.IsRequired(level, def.HasDirection ? direction : "Outbound");

            if (field.IsTable)
            {
                if (required && tableCounts.TryGetValue(field.Name, out var count) && count == 0)
                {
                    missing.Add(field.Label);
                    invalid.Add(field.Name);
                }
                continue;
            }

            if (!fieldValues.TryGetValue(field.Name, out var raw)) continue;
            var value = raw?.Trim() ?? "";
            if (required && value.Length == 0)
            {
                missing.Add(field.Label);
                invalid.Add(field.Name);
            }
            else if (field.Email && value.Length > 0 && !value.Contains('@'))
            {
                missing.Add($"{field.Label} (invalid email)");
                invalid.Add(field.Name);
            }
        }

        return new ValidationResult { MissingLabels = missing, InvalidFields = invalid };
    }
}
