using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.LiveSessions;

/// <summary>The client created as many meetings today as its daily cap allows (409 <c>provider_unavailable</c> with a retry hint).</summary>
public sealed class LiveSessionCapException(DateTime resetsAtUtc) : Exception("The daily live session cap of the client is reached.")
{
    public DateTime ResetsAtUtc { get; } = resetsAtUtc;
}

/// <summary>A create request was refused by the provider (no capacity, or not configured): nothing was created.</summary>
public sealed class LiveSessionProviderException(MeetingFailure failure, int? retryAfterSeconds) : Exception("The meeting provider refused the request.")
{
    public MeetingFailure Failure { get; } = failure;

    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// Live session authoring (M4b Task 4, FR-VC-01): creates, updates and deletes sessions, their series and their lesson modules.
/// The database part (rows and modules) is one transaction; the provider calls come after it and never roll it back, so a
/// provider failure keeps the row in <c>SyncFailed</c> (no silent loss; the sweep of Task 5 retries). The caller has already
/// checked permission, relationship, the feature flag and the closed class guard.
/// </summary>
public sealed class LiveSessionService(
    LmsDbContext db, IMeetingProvider provider, IModuleWriter writer, IDataProtectionProvider protection, IClock clock,
    ISettingsResolver settings, IBackgroundJobs jobs, ILogger<LiveSessionService> logger)
{
    private const string ProtectorPurpose = "Lms.LiveSessions.Urls";

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    /// <exception cref="RecurrenceInvalidException">The recurrence is not valid.</exception>
    /// <exception cref="LiveSessionCapException">The daily create cap would be crossed (nothing is created).</exception>
    /// <exception cref="ModuleRuleException">The module settings are not valid (nothing is created).</exception>
    public async Task<IReadOnlyList<LiveSession>> CreateAsync(
        ClassCourse classCourse, Guid lessonKey, ModuleMetadata metadata, DateTime startsAtUtc, int durationMinutes,
        RecurrenceRule? rule, CancellationToken ct)
    {
        var times = rule is null ? [startsAtUtc] : RecurrenceExpander.Expand(startsAtUtc, rule);
        await EnsureCapAsync(classCourse, times.Count, ct);

        var sessions = new List<LiveSession>();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var series = rule is null ? null : new LiveSessionSeries(classCourse, lessonKey, rule);
            if (series is not null)
            {
                db.Add(series);
            }

            sessions.AddRange(times.Select((t, i) => new LiveSession(
                classCourse, lessonKey, series?.Id, rule is null ? null : i + 1, provider.Name, t, durationMinutes)));
            db.AddRange(sessions);
            await db.SaveChangesAsync(ct);
            await AddModulesAsync(classCourse, lessonKey, metadata, sessions, ct);
            await transaction.CommitAsync(ct);
        }

        await SyncNewAsync(classCourse, metadata, sessions, rule?.TimeZoneId ?? "UTC", strict: true, ct);
        await ScheduleRemindersAsync(classCourse, sessions, ct);
        return sessions;
    }

    /// <summary>
    /// Creates or updates the provider meeting of each row. A plain failure keeps the row in <c>SyncFailed</c> (the sweep retries).
    /// With <paramref name="strict"/> (a create request) a provider that has no capacity or is not configured undoes the whole
    /// request instead: nothing stays behind and <see cref="LiveSessionProviderException"/> carries the answer.
    /// </summary>
    public async Task SyncNewAsync(
        ClassCourse classCourse, ModuleMetadata metadata, IReadOnlyList<LiveSession> sessions, string timeZoneId, bool strict, CancellationToken ct)
    {
        var cloud = (await settings.ResolveAsync(SettingKeys.LiveCloudRecordingEnabled, classCourse.OrgUnitPath, ct)).Value;
        var synced = new List<LiveSession>();
        foreach (var session in sessions)
        {
            try
            {
                if (session.ExternalMeetingId is null)
                {
                    var request = RequestOf(session, classCourse, metadata, timeZoneId, cloud) with
                    {
                        ReserveHostAsync = async host =>
                        {
                            session.SetHost(host); // the pool slot is committed while the provider's allocation lock is held
                            await db.SaveChangesAsync(ct);
                        },
                    };
                    var meeting = await provider.CreateMeetingAsync(request, ct);
                    session.Synced(
                        meeting.ExternalMeetingId, meeting.HostUserKey ?? session.HostUserKey, Protector.Protect(meeting.JoinUrl),
                        Protector.Protect(meeting.StartUrl));
                    synced.Add(session);
                }
                else
                {
                    await provider.UpdateMeetingAsync(
                        session.ExternalMeetingId, RequestOf(session, classCourse, metadata, timeZoneId, cloud) with { HostUserKey = session.HostUserKey }, ct);
                    session.Synced(
                        session.ExternalMeetingId, session.HostUserKey, session.JoinUrlProtected ?? string.Empty,
                        session.StartUrlProtected ?? string.Empty);
                }
            }
            catch (MeetingProviderException ex) when (strict && ex.Failure != MeetingFailure.Failed)
            {
                await UndoCreateAsync(sessions, synced, ct);
                throw new LiveSessionProviderException(ex.Failure, ex.RetryAfterSeconds);
            }
            catch (MeetingProviderException ex)
            {
                logger.LogWarning(ex, "Live session {SessionId} could not be synced with the provider", session.Id);
                session.SyncFailed();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Whatever a provider throws (a timeout, a network error) is a plain failure: the row is kept for the sweep.
                logger.LogWarning(ex, "Live session {SessionId} could not be synced with the provider (unexpected error)", session.Id);
                session.SyncFailed();
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static MeetingRequest RequestOf(LiveSession session, ClassCourse classCourse, ModuleMetadata metadata, string timeZoneId, bool cloud) =>
        new(session.Id, Topic(metadata), session.StartsAtUtc, session.DurationMinutes, timeZoneId, classCourse.OrganizationId, classCourse.Id, cloud);

    /// <summary>A create request the provider refused for lack of capacity: the meetings it did create, the rows, the modules and the series all go.</summary>
    private async Task UndoCreateAsync(IReadOnlyList<LiveSession> sessions, List<LiveSession> synced, CancellationToken ct)
    {
        await DeleteMeetingsAsync(synced.Select(s => s.ExternalMeetingId!), ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var session in sessions)
        {
            session.Cancel();
            db.Remove(session);
            await writer.DeleteForRefAsync(ModuleKinds.LiveSession, session.Id, ct);
        }

        var seriesIds = sessions.Where(s => s.SeriesId is not null).Select(s => s.SeriesId!.Value).Distinct().ToList();
        foreach (var series in await db.Set<LiveSessionSeries>().Where(x => seriesIds.Contains(x.Id)).ToListAsync(ct))
        {
            db.Remove(series);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <exception cref="RecurrenceInvalidException">The recurrence is not valid.</exception>
    /// <exception cref="LiveSessionCapException">Re-creating the following occurrences would cross the daily cap.</exception>
    /// <exception cref="ModuleRuleException">The module settings are not valid.</exception>
    public async Task UpdateAsync(
        LiveSession session, ClassCourse classCourse, ModuleMetadata metadata, DateTime startsAtUtc, int durationMinutes, string scope,
        RecurrenceRule? rule, CancellationToken ct)
    {
        var series = session.SeriesId is { } seriesId ? await db.Set<LiveSessionSeries>().FirstAsync(s => s.Id == seriesId, ct) : null;
        var following = scope == SeriesEditScopes.ThisAndFollowing && series is not null
            ? await db.Set<LiveSession>().Where(s => s.SeriesId == series.Id && s.OccurrenceIndex > session.OccurrenceIndex)
                .OrderBy(s => s.OccurrenceIndex).ToListAsync(ct)
            : [];
        var oldStart = session.StartsAtUtc;
        var removedMeetings = new List<string>();
        var touched = new List<LiveSession> { session };
        var created = new List<LiveSession>();

        if (rule is not null && series is not null && scope == SeriesEditScopes.ThisAndFollowing)
        {
            var times = RecurrenceExpander.Expand(startsAtUtc, rule);
            var earlier = await db.Set<LiveSession>().CountAsync(s => s.SeriesId == series.Id && s.OccurrenceIndex < session.OccurrenceIndex, ct);
            if (earlier + times.Count > LiveSessionRules.MaxOccurrences)
            {
                throw new RecurrenceInvalidException($"A series has at most {LiveSessionRules.MaxOccurrences} occurrences.");
            }

            await EnsureCapAsync(classCourse, times.Count - 1, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var old in following)
            {
                if (old.ExternalMeetingId is not null)
                {
                    removedMeetings.Add(old.ExternalMeetingId);
                }

                old.Cancel();
                db.Remove(old);
                await writer.DeleteForRefAsync(ModuleKinds.LiveSession, old.Id, ct);
            }

            series.Apply(rule);
            session.Reschedule(startsAtUtc, durationMinutes);
            await writer.UpdateMetadataAsync(session.ModuleId!.Value, metadata, ct);
            created.AddRange(times.Skip(1).Select((t, i) => new LiveSession(
                classCourse, session.LessonKey, series.Id, session.OccurrenceIndex + 1 + i, provider.Name, t, durationMinutes)));
            db.AddRange(created);
            await db.SaveChangesAsync(ct);
            await AddModulesAsync(classCourse, session.LessonKey, metadata, created, ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            session.Reschedule(startsAtUtc, durationMinutes);
            await writer.UpdateMetadataAsync(session.ModuleId!.Value, metadata, ct);
            foreach (var next in following)
            {
                next.Reschedule(RecurrenceExpander.Shift(next.StartsAtUtc, oldStart, startsAtUtc, series!.TimeZoneId), durationMinutes);
                await writer.UpdateMetadataAsync(next.ModuleId!.Value, metadata, ct);
                touched.Add(next);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await DeleteMeetingsAsync(removedMeetings, ct);
        foreach (var old in following.Where(_ => created.Count > 0 || rule is not null))
        {
            CancelReminder(old.Id);
        }

        await SyncNewAsync(classCourse, metadata, [.. touched, .. created], series?.TimeZoneId ?? "UTC", strict: false, ct);
        await ScheduleRemindersAsync(classCourse, [.. touched, .. created], ct);
    }

    public async Task DeleteAsync(LiveSession session, string scope, CancellationToken ct)
    {
        var targets = new List<LiveSession> { session };
        if (scope == SeriesEditScopes.ThisAndFollowing && session.SeriesId is { } seriesId)
        {
            targets.AddRange(await db.Set<LiveSession>().Where(s => s.SeriesId == seriesId && s.OccurrenceIndex > session.OccurrenceIndex).ToListAsync(ct));
        }

        var meetings = targets.Where(t => t.ExternalMeetingId is not null).Select(t => t.ExternalMeetingId!).ToList();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var target in targets)
            {
                target.Cancel();
                db.Remove(target);
                await writer.DeleteForRefAsync(ModuleKinds.LiveSession, target.Id, ct);
            }

            if (session.SeriesId is { } id)
            {
                var targetIds = targets.Select(t => t.Id).ToList();
                if (!await db.Set<LiveSession>().AnyAsync(s => s.SeriesId == id && !targetIds.Contains(s.Id), ct))
                {
                    db.Remove(await db.Set<LiveSessionSeries>().FirstAsync(s => s.Id == id, ct));
                }
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await DeleteMeetingsAsync(meetings, ct);
        foreach (var target in targets)
        {
            CancelReminder(target.Id);
        }
    }

    /// <summary>The join URL, or the host start URL, of a session (decrypted); null when the provider never confirmed the meeting.</summary>
    public string? UrlOf(LiveSession session, bool host)
    {
        var protectedUrl = host ? session.StartUrlProtected : session.JoinUrlProtected;
        return string.IsNullOrEmpty(protectedUrl) ? null : Protector.Unprotect(protectedUrl);
    }

    private static string ReminderKey(Guid sessionId) => "live-reminder:" + sessionId;

    /// <summary>
    /// One reminder job per scheduled session, <c>Live.ReminderMinutes</c> before the start (at once when that moment has passed
    /// and the session is still ahead); the key makes scheduling again move the same job. A Hangfire failure never undoes the change.
    /// </summary>
    private async Task ScheduleRemindersAsync(ClassCourse classCourse, IEnumerable<LiveSession> sessions, CancellationToken ct)
    {
        var minutes = (await settings.ResolveAsync(SettingKeys.LiveReminderMinutes, classCourse.OrgUnitPath, ct)).Value;
        var now = clock.UtcNow;
        foreach (var session in sessions)
        {
            try
            {
                if (session.Status != LiveSessionRules.Scheduled || session.StartsAtUtc <= now)
                {
                    jobs.Cancel(ReminderKey(session.Id));
                    continue;
                }

                var runAt = session.StartsAtUtc.AddMinutes(-minutes);
                if (runAt < now)
                {
                    runAt = now.AddSeconds(5);
                }

                var id = session.Id;
                var organizationId = session.OrganizationId;
                jobs.ScheduleAsSystem<LiveReminderJob>(
                    ReminderKey(id), new DateTimeOffset(DateTime.SpecifyKind(runAt, DateTimeKind.Utc)), j => j.RunAsync(id, organizationId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The reminder of live session {SessionId} could not be scheduled", session.Id);
            }
        }
    }

    private void CancelReminder(Guid sessionId)
    {
        try
        {
            jobs.Cancel(ReminderKey(sessionId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The reminder of live session {SessionId} could not be removed", sessionId);
        }
    }

    private async Task AddModulesAsync(
        ClassCourse classCourse, Guid lessonKey, ModuleMetadata metadata, IEnumerable<LiveSession> sessions, CancellationToken ct)
    {
        foreach (var session in sessions)
        {
            session.AttachModule(await writer.CreateForRefAsync(classCourse.Id, lessonKey, ModuleKinds.LiveSession, session.Id, metadata, ct));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Best effort: a meeting the provider could not delete is an orphan the sweep of Task 5 reports; the row is gone either way.</summary>
    private async Task DeleteMeetingsAsync(IEnumerable<string> externalIds, CancellationToken ct)
    {
        foreach (var id in externalIds)
        {
            try
            {
                await provider.DeleteMeetingAsync(id, ct);
            }
            catch (MeetingProviderException ex)
            {
                logger.LogWarning(ex, "Provider meeting {MeetingId} could not be deleted", id);
            }
        }
    }

    /// <summary>The topic Zoom shows is the teacher's title only, never markup, client or student data (Decision 5).</summary>
    private static string Topic(ModuleMetadata metadata) =>
        (string.IsNullOrWhiteSpace(metadata.TitleEn) ? metadata.TitleAr : metadata.TitleEn).Trim();

    private async Task EnsureCapAsync(ClassCourse classCourse, int newMeetings, CancellationToken ct)
    {
        if (newMeetings <= 0)
        {
            return;
        }

        var cap = (await settings.ResolveAsync(SettingKeys.LiveMaxCreatesPerDay, classCourse.OrgUnitPath, ct)).Value;
        var dayStart = clock.UtcNow.Date;

        // Rows undone after a refused create keep their ExternalMeetingId on purpose: the meeting was created (and deleted) at the
        // provider, so it still counts against the day.
        // ISOLATION-BYPASS: the daily cap counts the meetings the whole client created today, in every unit and including
        // rows deleted since (a delete does not give the quota back), so neither the subtree nor the soft-delete filter may
        // apply. Only the client's rows and only a count are read.
        var used = await db.Set<LiveSession>().IgnoreQueryFilters()
            .CountAsync(s => s.OrganizationId == classCourse.OrganizationId && s.ExternalMeetingId != null && s.CreatedAt >= dayStart, ct);
        if (used + newMeetings > cap)
        {
            throw new LiveSessionCapException(dayStart.AddDays(1));
        }
    }
}
