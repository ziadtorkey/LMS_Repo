namespace Lms.Shared;

// M4b wave 3a: live sessions (M4b plan Task 1). Each constant's doc comment is its row of the plan's "Route, permission
// and relationship table" (Task 20 tests every row). "Rel" = Teacher only own ClassCourses, Student only own active
// enrollments + IModuleAccess visibility, other staff their unit scope; a relationship or visibility miss is 404, a
// missing permission 403.
//
// Retry-After: every provider_unavailable answer (409 or 503) carries a Retry-After header in seconds (delta-seconds,
// never an HTTP date), and the API lists Retry-After in the CORS Access-Control-Expose-Headers so the WASM client can
// read it.
public static partial class Routes
{
    /// <summary>
    /// GET (paged, <c>LiveSessionQuery</c>) — <c>LiveSessions.View</c>; Rel, students get only visible session modules;
    /// other teacher / other class student 404, guardian 403.
    /// POST (<c>CreateLiveSessionRequest</c>) → <c>LiveSessionDto[]</c> (one per occurrence, in start order; one element
    /// for a single session) — <c>LiveSessions.Manage</c> + <c>Modules.Manage</c>; Rel, <c>Features.VirtualClasses</c>,
    /// PlacementGuard, daily create cap, host share; student 403, other teacher 404, flag off 403
    /// <c>feature_disabled</c>; invalid recurrence 400 <c>recurrence_invalid</c>; 409/503 <c>provider_unavailable</c>
    /// with <c>Retry-After</c> (seconds).
    /// </summary>
    public const string ClassCourseLiveSessionsPattern = ClassCoursePattern + "/live-sessions";

    public const string LiveSessions = "/api/live-sessions";

    /// <summary>
    /// GET → <c>LiveSessionDto</c> — <c>LiveSessions.View</c>; Rel + IModuleAccess; invisible module 404.
    /// PUT (<c>UpdateLiveSessionRequest</c>) → a single <c>LiveSessionDto</c>: the edited occurrence (for
    /// <c>ThisAndFollowing</c> too, the occurrence named by <c>{id}</c>, not the following ones).
    /// DELETE (<c>?scope=</c> from <c>SeriesEditScopes</c>) → 204 No Content.
    /// Both <c>LiveSessions.Manage</c>; Rel, PlacementGuard; student 403, other teacher 404; 409/503
    /// <c>provider_unavailable</c> with <c>Retry-After</c> (seconds).
    /// </summary>
    public const string LiveSessionPattern = LiveSessions + "/{id:guid}";

    /// <summary>
    /// POST → <c>LiveSessionJoinDto</c> — <c>LiveSessions.View</c>; Rel + IModuleAccess, join window (students),
    /// <c>Features.VirtualClasses</c>; start URL only with Manage, other staff get the join URL; outside the window 409
    /// <c>live_session_not_open</c>, invisible 404.
    /// </summary>
    public const string LiveSessionJoinPattern = LiveSessionPattern + "/join";

    /// <summary>
    /// PUT (<c>RecordingLinkRequest</c>) → <c>LiveSessionDto</c> (with the new <c>RecordingUrl</c>, source
    /// <c>Manual</c>) — <c>LiveSessions.Manage</c>; Rel; student 403, other teacher 404.
    /// </summary>
    public const string LiveSessionRecordingPattern = LiveSessionPattern + "/recording";

    /// <summary>
    /// GET (paged, <c>LiveParticipantQuery</c>) → <c>LiveParticipantDto</c> — <c>LiveSessions.Manage</c>; Rel; student /
    /// supervisor 403, other teacher 404.
    /// </summary>
    public const string LiveSessionParticipantsPattern = LiveSessionPattern + "/participants";

    /// <summary>
    /// GET → <c>UpcomingLiveSessionDto[]</c> (max 5) — <c>LiveSessions.View</c>; result filtered by Rel / unit scope,
    /// never 404; guardian 403.
    /// </summary>
    public const string LiveSessionsUpcoming = LiveSessions + "/upcoming";

    /// <summary>
    /// GET → <c>ProviderStatusDto</c> — <c>LiveSessions.Manage</c>; no relationship check (platform status, no
    /// configuration detail); student / supervisor 403.
    /// </summary>
    public const string LiveSessionsProviderStatus = LiveSessions + "/provider-status";

    /// <summary>
    /// POST — anonymous by design (Zoom calls it, D54): body cap 256 KB (413), URL-validation challenge, signature over
    /// the raw body + 5-minute timestamp window (401), failed verifications rate limited per IP (429), receipt and
    /// processing in one transaction. The only anonymous M4b <c>/api</c> route.
    /// </summary>
    public const string ZoomWebhook = "/api/integrations/zoom/webhook";

    /// <summary>
    /// POST — Development only (mapped only when the host environment is Development, 404 elsewhere): signs and posts a
    /// fake Zoom event to <see cref="ZoomWebhook"/> for the E2E demo (M4b plan Task 7). The body is private to that
    /// endpoint.
    /// </summary>
    public const string DevZoomEvents = "/api/dev/zoom/events";

    public static string ClassCourseLiveSessions(Guid id) => $"{ClassCourse(id)}/live-sessions";
    public static string LiveSession(Guid id) => $"{LiveSessions}/{id}";
    public static string LiveSession(Guid id, string scope) => $"{LiveSession(id)}?scope={scope}";
    public static string LiveSessionJoin(Guid id) => $"{LiveSession(id)}/join";
    public static string LiveSessionRecording(Guid id) => $"{LiveSession(id)}/recording";
    public static string LiveSessionParticipants(Guid id) => $"{LiveSession(id)}/participants";
}
