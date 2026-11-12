using Lms.Domain.Classes;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Enrollment;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Transfer between classes (M3 Task 28, #236): both classes are at School1, the student is placed in the first one.
/// Enrollment.Transfer (admins, manager; not the Teacher) is checked at both class units; School 2 and the other client
/// get 404. The history needs Enrollment.View; a Teacher sees only students they teach (the probe's class has no teacher).
/// </summary>
public sealed class TransfersProbeRows : IProbeRowModule
{
    private static readonly DateOnly On = new(2041, 11, 1);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("transfers preview", HttpMethod.Post, Routes.TransfersPreview,
            async (w, _) => new ProbeRequest(Routes.TransfersPreview, (await NewAsync(w)).Request),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("transfers create", HttpMethod.Post, Routes.Transfers,
            async (w, _) => new ProbeRequest(Routes.Transfers, (await NewAsync(w)).Request),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("student transfers history", HttpMethod.Get, Routes.StudentTransfersPattern,
            async (w, _) => new ProbeRequest(Routes.StudentTransfers((await NewAsync(w)).Request.StudentId)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
    ];

    private sealed record Seeded(TransferStudentRequest Request);

    private static async Task<Seeded> NewAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var from = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var to = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var student = await PeopleTestKit.AddStudentAsync(factory, school1, PeopleTestKit.NewCode());
        await AcademicTestKit.PlaceAsync(factory, from, student);
        return new Seeded(new TransferStudentRequest(student.Id, to.Id, On, "probe", false));
    }
}
