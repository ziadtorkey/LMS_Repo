namespace Lms.Client.Components.YearEnd;

/// <summary>What a polled job reports: how far it is, and whether it ended (and how). Built from a run DTO by the page.</summary>
public sealed record JobSnapshot(int Processed, int Total, bool Finished, bool Failed = false)
{
    public int Percent => Total <= 0 ? (Finished ? 100 : 0) : Math.Clamp((int)Math.Round(Processed * 100d / Total), 0, 100);
}
