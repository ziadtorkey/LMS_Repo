using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.Shared.Authorization;
using GradebookEntity = Lms.Domain.Grading.Gradebook;
using TermEntity = Lms.Domain.AcademicYears.Term;

namespace Lms.Api.Features.Gradebook;

/// <summary>What a gradebook route needs once the caller passed the permission and the relationship: the class course and the gradebook of the term.</summary>
internal sealed record GradebookTarget(ClassCourse ClassCourse, GradebookEntity Gradebook, TermEntity Term);

internal static class GradebookAccess
{
    /// <summary>
    /// A write target: <c>Grades.Manage</c> at the class course (relationship: a teacher's own class courses, 404 otherwise) and the gradebook of
    /// <paramref name="termId"/> (the current term of the class when null), created on first use. 404 when the term does not belong to the class year or
    /// the class is closed and has no gradebook yet.
    /// </summary>
    public static async Task<(GradebookTarget? Target, IResult? Problem)> ForWriteAsync(
        HttpContext http, Guid classCourseId, Guid? termId, GradebookReader reader, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Grades.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return (null, problem);
        }

        var term = await reader.ResolveTermAsync(cc!, termId, ct);
        var gradebook = term is null ? null : await reader.GetOrCreateAsync(cc!, term, ct);
        return gradebook is null ? (null, ApiProblems.NotFound()) : (new GradebookTarget(cc!, gradebook, term!), null);
    }

    public static async Task<bool> MayAsync(HttpContext http, string permission, string orgUnitPath, CancellationToken ct) =>
        (await http.RequestServices.GetRequiredService<IAccessEvaluator>().EvaluateAsync(permission, orgUnitPath, ct)).Allowed;
}
