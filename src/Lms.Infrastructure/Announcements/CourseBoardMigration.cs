namespace Lms.Infrastructure.Announcements;

/// <summary>
/// The data move of the M4a course board onto the announcements store (M6 Task 17, L15, site 9). A board post becomes a <b>published</b> announcement with the <b>same id</b>
/// (so every existing link and the board's PUT and DELETE keep working), one <c>ClassCourse</c> target, the Staff and Students chips (Guardians off), no notification, the title
/// copied to both languages and the body to both bodies (a board post is written in one language and must read in either). Every statement is set-based, per row and idempotent
/// (re-running changes nothing), keeps each row in its own client and unit, and keeps soft-deleted posts deleted. The old table stays one more release (the drop is a follow-up).
/// <see cref="Down"/> writes the board back: it updates the old rows from their announcements, restores the ones deleted since, and adds the posts written after the move.
/// </summary>
public static class CourseBoardMigration
{
    /// <summary>Statements to run in order (the migration runs them in one go; the tests run them against the real database).</summary>
    public static IReadOnlyList<string> Up { get; } =
    [
        """
        INSERT INTO Announcements (Id, AuthorUserId, TitleAr, TitleEn, BodyAr, BodyEn, Status, RoleFiltersCsv, PublishAtUtc, PublishedAtUtc, ExpiresAtUtc, IsPinned, Notify, IsUrgent,
            AudienceCount, EditedAtUtc, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, DeletedAt, OrganizationId, OrgUnitId, OrgUnitPath)
        SELECT c.Id, c.AuthorUserId, c.Title, c.Title, c.BodyHtml, c.BodyHtml, 'Published', 'Staff,Students', NULL, c.CreatedAt, NULL, c.IsPinned, 0, 0,
            NULL, c.UpdatedAt, c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy, c.IsDeleted, c.DeletedAt, c.OrganizationId, c.OrgUnitId, c.OrgUnitPath
        FROM CourseAnnouncements c
        WHERE NOT EXISTS (SELECT 1 FROM Announcements a WHERE a.Id = c.Id)
        """,
        """
        INSERT INTO AnnouncementTargets (Id, AnnouncementId, Kind, TargetId, IncludeSubtree, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, DeletedAt, OrganizationId, OrgUnitId, OrgUnitPath)
        SELECT UUID(), c.Id, 'ClassCourse', c.ClassCourseId, 0, c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy, c.IsDeleted, c.DeletedAt, c.OrganizationId, c.OrgUnitId, c.OrgUnitPath
        FROM CourseAnnouncements c
        WHERE NOT EXISTS (SELECT 1 FROM AnnouncementTargets t WHERE t.AnnouncementId = c.Id)
        """,
    ];

    public static IReadOnlyList<string> Down { get; } =
    [
        // The board rows that still exist take back what the announcements say now (title, body, pin, deletion).
        """
        UPDATE CourseAnnouncements c JOIN Announcements a ON a.Id = c.Id
        SET c.Title = LEFT(a.TitleEn, 200), c.BodyHtml = COALESCE(a.BodyEn, a.BodyAr, ''), c.IsPinned = a.IsPinned, c.IsDeleted = a.IsDeleted, c.DeletedAt = a.DeletedAt, c.UpdatedAt = a.UpdatedAt
        """,
        // The posts written after the move (one ClassCourse target, no board row yet) become board rows again.
        """
        INSERT INTO CourseAnnouncements (Id, ClassCourseId, Title, BodyHtml, IsPinned, AuthorUserId, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, DeletedAt, OrganizationId, OrgUnitId, OrgUnitPath)
        SELECT a.Id, t.TargetId, LEFT(a.TitleEn, 200), COALESCE(a.BodyEn, a.BodyAr, ''), a.IsPinned, a.AuthorUserId, a.CreatedAt, a.CreatedBy, a.UpdatedAt, a.UpdatedBy, a.IsDeleted, a.DeletedAt,
            a.OrganizationId, a.OrgUnitId, a.OrgUnitPath
        FROM Announcements a JOIN AnnouncementTargets t ON t.AnnouncementId = a.Id AND t.Kind = 'ClassCourse' AND t.TargetId IS NOT NULL
        WHERE a.Status IN ('Published', 'Expired', 'Withdrawn')
          AND (SELECT COUNT(*) FROM AnnouncementTargets x WHERE x.AnnouncementId = a.Id) = 1
          AND NOT EXISTS (SELECT 1 FROM CourseAnnouncements c WHERE c.Id = a.Id)
        """,
    ];
}
