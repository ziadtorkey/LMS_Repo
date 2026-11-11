namespace Lms.UnitTests.Architecture;

/// <summary>
/// Roslyn-free textual check for <c>IgnoreQueryFilters()</c> calls. A call belongs to a statement; the statement starts
/// after the previous line that ends with <c>;</c>, <c>{</c> or <c>}</c> (or after a blank line). The statement is
/// marked when a line containing <c>ISOLATION-BYPASS:</c> appears inside the statement up to the call, or in the
/// comment block directly above its first line (at most <see cref="MarkerWindow"/> lines; a code line in between
/// breaks it, so every bypass statement carries its own reason).
/// </summary>
public static class IsolationBypassScanner
{
    public const string Call = "IgnoreQueryFilters(";
    public const string Marker = "ISOLATION-BYPASS:";
    public const int MarkerWindow = 3;

    public sealed record FoundCall(int Line, string Text);

    /// <summary>Every code line (not a comment) that calls IgnoreQueryFilters; 1-based line numbers.</summary>
    public static IReadOnlyList<FoundCall> FindCalls(IReadOnlyList<string> lines) =>
        lines.Select((text, index) => new FoundCall(index + 1, text))
            .Where(c => c.Text.Contains(Call, StringComparison.Ordinal) && !IsComment(c.Text))
            .ToList();

    /// <summary>Calls whose statement has no ISOLATION-BYPASS marker close enough above it.</summary>
    public static IReadOnlyList<FoundCall> FindUnmarkedCalls(IReadOnlyList<string> lines) =>
        FindCalls(lines).Where(call => !IsMarked(lines, call.Line - 1)).ToList();

    private static bool IsMarked(IReadOnlyList<string> lines, int callIndex)
    {
        var start = StatementStart(lines, callIndex);

        // Inside the statement itself (comment lines between its first line and the call).
        for (var i = start; i <= callIndex; i++)
        {
            if (lines[i].Contains(Marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // The comment block directly above the statement, at most MarkerWindow lines; a code line ends the search,
        // so a marker written for one statement never covers the next one.
        for (var i = start - 1; i >= 0 && i >= start - MarkerWindow; i--)
        {
            if (!IsComment(lines[i]))
            {
                break;
            }

            if (lines[i].Contains(Marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Index of the first line of the statement containing <paramref name="index"/> (comments included).</summary>
    private static int StatementStart(IReadOnlyList<string> lines, int index)
    {
        var start = index;
        while (start > 0)
        {
            var previous = lines[start - 1].Trim();
            if (previous.Length == 0 || (!IsComment(previous) && EndsStatement(previous)))
            {
                break;
            }

            start--;
        }

        return start;
    }

    private static bool EndsStatement(string trimmed) =>
        trimmed.EndsWith(';') || trimmed.EndsWith('{') || trimmed.EndsWith('}');

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*')
               || trimmed.StartsWith("/*", StringComparison.Ordinal);
    }
}
