using System.Globalization;
using System.Text;

namespace Lms.Domain.People;

/// <summary>Values the tokens of a <see cref="StudentCodePattern"/> stand for.</summary>
public sealed record StudentCodeTokens(string RootCode);

/// <summary>
/// The student code pattern of a client (D25): literal text of letters, digits and hyphens, an optional
/// <c>{RootCode}</c> (the code of the client's root unit) and <c>{Seq:N}</c> exactly once (N = 1..9 digits, zero padded).
/// Any other token is invalid. The sequence is one running counter per client, so the pattern never decides it.
/// </summary>
public sealed class StudentCodePattern
{
    /// <summary>Longest rendered code (the code column and the shared code rule).</summary>
    public const int MaxLength = 30;

    private enum Kind { Literal, Root, Seq }

    private readonly IReadOnlyList<(Kind Kind, string Text)> _segments;

    private StudentCodePattern(IReadOnlyList<(Kind, string)> segments, int seqDigits)
    {
        _segments = segments;
        SeqDigits = seqDigits;
    }

    public int SeqDigits { get; }

    /// <summary>The largest sequence value that still fits the digits (10^N - 1); the next one is "exhausted".</summary>
    public long MaxValue => (long)Math.Pow(10, SeqDigits) - 1;

    /// <summary>Parses a pattern; <paramref name="error"/> is a reason key when it is invalid.</summary>
    public static bool TryParse(string? pattern, out StudentCodePattern? parsed, out string? error)
    {
        parsed = null;
        error = null;
        var segments = new List<(Kind, string)>();
        var literal = new StringBuilder();
        var seqDigits = 0;
        var roots = 0;
        var seqs = 0;
        var text = pattern ?? string.Empty;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                var end = text.IndexOf('}', i);
                if (end < 0)
                {
                    error = "unknown_token";
                    return false;
                }

                var token = text[(i + 1)..end];
                if (literal.Length > 0)
                {
                    segments.Add((Kind.Literal, literal.ToString()));
                    literal.Clear();
                }

                if (token == "RootCode")
                {
                    roots++;
                    segments.Add((Kind.Root, string.Empty));
                }
                else if (token.StartsWith("Seq:", StringComparison.Ordinal))
                {
                    if (!int.TryParse(token[4..], NumberStyles.None, CultureInfo.InvariantCulture, out seqDigits) || seqDigits is < 1 or > 9)
                    {
                        error = "seq_digits_out_of_range";
                        return false;
                    }

                    seqs++;
                    segments.Add((Kind.Seq, string.Empty));
                }
                else
                {
                    error = "unknown_token";
                    return false;
                }

                i = end;
            }
            else if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            {
                literal.Append(c);
            }
            else
            {
                error = "invalid_literal"; // also a stray '}' or any other symbol
                return false;
            }
        }

        if (literal.Length > 0)
        {
            segments.Add((Kind.Literal, literal.ToString()));
        }

        if (seqs == 0)
        {
            error = "seq_token_required";
        }
        else if (seqs > 1)
        {
            error = "seq_token_repeated";
        }
        else if (roots > 1)
        {
            error = "root_code_repeated";
        }
        else if (segments.Where(s => s.Item1 == Kind.Literal).Sum(s => s.Item2.Length) + seqDigits > MaxLength)
        {
            error = "too_long"; // even without the root code the fixed part does not fit
        }

        if (error is not null)
        {
            return false;
        }

        parsed = new StudentCodePattern(segments, seqDigits);
        return true;
    }

    /// <summary>The code for a sequence value; <c>null</c> renders zeros (for previews).</summary>
    public string Render(StudentCodeTokens tokens, long? seq) => Build(tokens, seq ?? 0, stopAtSeq: false);

    /// <summary>Everything the pattern puts before the sequence digits.</summary>
    public string Prefix(StudentCodeTokens tokens) => Build(tokens, 0, stopAtSeq: true);

    private string Build(StudentCodeTokens tokens, long seq, bool stopAtSeq)
    {
        var result = new StringBuilder();
        foreach (var (kind, text) in _segments)
        {
            switch (kind)
            {
                case Kind.Literal:
                    result.Append(text);
                    break;
                case Kind.Root:
                    result.Append(tokens.RootCode);
                    break;
                default:
                    if (stopAtSeq)
                    {
                        return result.ToString();
                    }

                    result.Append(seq.ToString(CultureInfo.InvariantCulture).PadLeft(SeqDigits, '0'));
                    break;
            }
        }

        return result.ToString();
    }
}
