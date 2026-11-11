using System.Text.Json;

namespace Lms.Client.Pages.Audit;

public sealed record AuditDiffRow(string Property, string? Before, string? After);

/// <summary>Turns the before/after JSON snapshots of an audit entry into property rows.</summary>
public static class AuditDiff
{
    /// <summary>
    /// Updates: only properties whose value changed. Creates (no before): every property with its new value.
    /// Deletes (no after): every property with its old value.
    /// </summary>
    public static IReadOnlyList<AuditDiffRow> Rows(string? beforeJson, string? afterJson)
    {
        var before = Parse(beforeJson);
        var after = Parse(afterJson);
        var names = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal);
        var onlyOneSide = beforeJson is null || afterJson is null;
        return names
            .Select(n => new AuditDiffRow(n, before.GetValueOrDefault(n), after.GetValueOrDefault(n)))
            .Where(r => onlyOneSide || r.Before != r.After)
            .ToList();
    }

    private static Dictionary<string, string?> Parse(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var p in doc.RootElement.EnumerateObject())
                result[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => p.Value.GetRawText(),
                };
        }
        catch (JsonException) { }
        return result;
    }
}
