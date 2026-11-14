namespace Lms.Infrastructure.Attendance.Live;

/// <summary>One stay of a participant in the room.</summary>
public sealed record PresenceSegment(DateTime JoinedAtUtc, DateTime? LeftAtUtc);

/// <summary>The proposed status of one student with the merged minutes in the room and the first join (L11 item 6).</summary>
public sealed record PresenceResult(string Status, int Minutes, DateTime? FirstJoinedAtUtc);

/// <summary>
/// Turns the stays of a student in a live session into a proposed status (FR-ATT-01, L11 item 6): overlapping and touching stays are merged into
/// non-overlapping segments and clipped to the scheduled window (a stay without a leave time runs to the end), the merged length against the scheduled length decides
/// Present or Absent, and Late comes from the first join against the scheduled start. Pure: no storage, no clock.
/// </summary>
public static class PresenceCalculator
{
    public static PresenceResult Evaluate(
        DateTime startsAtUtc, DateTime endsAtUtc, IEnumerable<PresenceSegment> stays, int minPercent, int lateAfterMinutes)
    {
        var clipped = stays
            .Select(s => (From: s.JoinedAtUtc < startsAtUtc ? startsAtUtc : s.JoinedAtUtc, To: s.LeftAtUtc is { } left && left < endsAtUtc ? left : endsAtUtc))
            .Where(s => s.To > s.From)
            .OrderBy(s => s.From)
            .ToList();
        var merged = new List<(DateTime From, DateTime To)>();
        foreach (var stay in clipped)
        {
            if (merged.Count > 0 && stay.From <= merged[^1].To)
            {
                merged[^1] = (merged[^1].From, stay.To > merged[^1].To ? stay.To : merged[^1].To);
            }
            else
            {
                merged.Add(stay);
            }
        }

        var scheduled = (endsAtUtc - startsAtUtc).TotalSeconds;
        var present = merged.Sum(m => (m.To - m.From).TotalSeconds);
        var minutes = (int)Math.Round(present / 60);
        DateTime? first = merged.Count > 0 ? merged[0].From : null;
        if (scheduled <= 0 || first is null || present * 100 < scheduled * minPercent)
        {
            return new PresenceResult("Absent", minutes, first);
        }

        return new PresenceResult(first.Value > startsAtUtc.AddMinutes(lateAfterMinutes) ? "Late" : "Present", minutes, first);
    }
}
