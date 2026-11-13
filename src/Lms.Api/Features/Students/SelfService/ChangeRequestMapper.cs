using System.Text.Json;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>Builds <see cref="StudentChangeRequestDto"/>s; every lookup goes through the caller's scope.</summary>
internal sealed class ChangeRequestMapper(LmsDbContext db)
{
    public static IReadOnlyList<FieldChangeDto> ChangesOf(StudentChangeRequest r) =>
        JsonSerializer.Deserialize<List<FieldChangeDto>>(r.ChangesJson) ?? [];

    public async Task<IReadOnlyList<StudentChangeRequestDto>> ToDtosAsync(IReadOnlyList<StudentChangeRequest> requests, CancellationToken ct)
    {
        var studentIds = requests.Select(r => r.StudentId).Distinct().ToList();
        var unitIds = requests.Select(r => r.OrgUnitId).Distinct().ToList();
        var deciders = requests.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value).Distinct().ToList();
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var units = await db.Set<OrgUnit>().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var users = await db.Set<User>().Where(u => deciders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return requests.Select(r =>
        {
            var student = students.GetValueOrDefault(r.StudentId);
            var unit = units.GetValueOrDefault(r.OrgUnitId);
            var by = r.DecidedBy is { } id ? users.GetValueOrDefault(id) : null;
            return new StudentChangeRequestDto(
                r.Id, r.StudentId, student?.StudentCode ?? string.Empty, student?.NameAr ?? string.Empty, student?.NameEn ?? string.Empty,
                unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty, r.Status.ToString(), ChangesOf(r), r.Note, r.CreatedAt,
                by?.DisplayNameAr, by?.DisplayNameEn, r.DecidedAt, r.DecisionComment);
        }).ToList();
    }
}
