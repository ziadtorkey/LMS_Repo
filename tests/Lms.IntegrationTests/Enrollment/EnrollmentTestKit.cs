using Lms.Api.Infrastructure;
using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Enrollment;

/// <summary>A request scope with the platform context (like a system job): the engine, the handler and their DbContext.</summary>
internal sealed class EngineScope : IDisposable
{
    private readonly IServiceScope _scope;

    public EngineScope(IServiceProvider services)
    {
        _scope = services.CreateScope();
        _scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
    }

    public IEnrollmentEngine Engine => _scope.ServiceProvider.GetRequiredService<IEnrollmentEngine>();

    public EnrollmentEngine Concrete => _scope.ServiceProvider.GetRequiredService<EnrollmentEngine>();

    public IClassCourseLifecycleHandler Handler =>
        _scope.ServiceProvider.GetServices<IClassCourseLifecycleHandler>().OfType<EnrollmentClassCourseHandler>().Single();

    public LmsDbContext Db => _scope.ServiceProvider.GetRequiredService<LmsDbContext>();

    public ISettingsResolver Settings => _scope.ServiceProvider.GetRequiredService<ISettingsResolver>();

    public void Dispose() => _scope.Dispose();
}

/// <summary>Year at the root, a class at School1 with two mandatory courses and one optional course (all owned at the root).</summary>
internal sealed record EnrollmentWorld(
    AccessTree Tree, AcademicYear Year, Class Class, Course A, Course B, Course Optional, ClassCourse CcA, ClassCourse CcB,
    ClassCourse CcOptional)
{
    public static async Task<EnrollmentWorld> CreateAsync(LmsApiFactory factory, string name, int mandatoryExtra = 0)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var cls = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var a = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var b = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var optional = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var ccA = await AcademicTestKit.AddClassCourseAsync(factory, cls, a);
        var ccB = await AcademicTestKit.AddClassCourseAsync(factory, cls, b);
        for (var i = 0; i < mandatoryExtra; i++)
        {
            await AcademicTestKit.AddClassCourseAsync(factory, cls, await AcademicTestKit.AddCourseAsync(factory, tree.Root));
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var ccOptional = new ClassCourse(cls, optional, true);
        db.Add(ccOptional);
        await db.SaveChangesAsync();
        return new EnrollmentWorld(tree, year, cls, a, b, optional, ccA, ccB, ccOptional);
    }
}
