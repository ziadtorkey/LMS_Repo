using Lms.Domain.Abstractions;
using Lms.Domain.Courses;
using Lms.Infrastructure.Courses;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.Academic;

/// <summary>The catalog reads ancestor-owned courses that the subtree filter hides, and nothing else (Review Focus 2).</summary>
[Collection(ApiCollection.Name)]
public sealed class CourseCatalogTests(LmsApiFactory factory)
{
    private async Task<T> WithCatalogAsync<T>(TestCurrentContext who, Func<ICourseCatalog, Task<T>> action)
    {
        await using var db = factory.CreateDbContext(who);
        return await action(new CourseCatalog(db, who));
    }

    [Fact]
    public async Task Catalog_returns_ancestor_owned_course_as_inherited_but_not_sibling_or_other_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "CC1A");
        var b = await AccessTestData.SeedAsync(factory, "CC1B");
        var rootCourse = await AcademicTestKit.AddCourseAsync(factory, a.Root);
        var ownCourse = await AcademicTestKit.AddCourseAsync(factory, a.School1);
        var siblingCourse = await AcademicTestKit.AddCourseAsync(factory, a.School2);
        var otherClient = await AcademicTestKit.AddCourseAsync(factory, b.Root);
        var school1 = TestCurrentContext.For(a.Org.Id, a.School1.Path);

        var inherited = await WithCatalogAsync(school1, c => c.FindAsync(rootCourse.Id));
        var own = await WithCatalogAsync(school1, c => c.FindAsync(ownCourse.Id));

        Assert.True(inherited!.IsInherited);
        Assert.False(own!.IsInherited);
        Assert.Null(await WithCatalogAsync(school1, c => c.FindAsync(siblingCourse.Id)));
        Assert.Null(await WithCatalogAsync(school1, c => c.FindAsync(otherClient.Id)));
        Assert.Null(await WithCatalogAsync(TestCurrentContext.For(a.Org.Id, null), c => c.FindAsync(rootCourse.Id)));
        // the parent sees descendants (not inherited) and the platform admin everything
        Assert.False((await WithCatalogAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), c => c.FindAsync(siblingCourse.Id)))!.IsInherited);
        Assert.NotNull(await WithCatalogAsync(TestCurrentContext.PlatformAdmin(), c => c.FindAsync(otherClient.Id)));
    }

    [Fact]
    public async Task Soft_deleted_course_is_not_found()
    {
        var a = await AccessTestData.SeedAsync(factory, "CC2");
        var course = await AcademicTestKit.AddCourseAsync(factory, a.Root);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Attach(course);
            db.Remove(course);
            await db.SaveChangesAsync();
        }

        Assert.Null(await WithCatalogAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), c => c.FindAsync(course.Id)));
    }

    [Fact]
    public async Task Availability_follows_allocation_at_unit_or_ancestor()
    {
        var a = await AccessTestData.SeedAsync(factory, "CC3");
        var course = await AcademicTestKit.AddCourseAsync(factory, a.Root);
        var onlySchool2 = await AcademicTestKit.AddCourseAsync(factory, a.Root);
        var notAllocated = await AcademicTestKit.AddCourseAsync(factory, a.Root);
        await AcademicTestKit.AllocateAsync(factory, course, a.Root);
        await AcademicTestKit.AllocateAsync(factory, onlySchool2, a.School2);
        var who = TestCurrentContext.For(a.Org.Id, a.School1.Path);

        // allocated at the root: available at every unit below it
        Assert.True(await WithCatalogAsync(who, c => c.IsAvailableAtAsync(course.Id, a.School1)));
        Assert.True(await WithCatalogAsync(who, c => c.IsAvailableAtAsync(course.Id, a.School2)));
        // allocated at School 2 only: not at School 1, not at the root above it
        Assert.False(await WithCatalogAsync(who, c => c.IsAvailableAtAsync(onlySchool2.Id, a.School1)));
        Assert.True(await WithCatalogAsync(who, c => c.IsAvailableAtAsync(onlySchool2.Id, a.School2)));
        Assert.False(await WithCatalogAsync(who, c => c.IsAvailableAtAsync(onlySchool2.Id, a.Root)));
        Assert.False(await WithCatalogAsync(who, c => c.IsAvailableAtAsync(notAllocated.Id, a.School1)));

        var list = await WithCatalogAsync(who, c => c.AvailableAtAsync(a.School1));
        Assert.Equal([course.Id], list.Select(x => x.Course.Id));
        Assert.True(list[0].IsInherited);
    }

    [Fact]
    public async Task Allocations_of_another_client_never_make_a_course_available()
    {
        var a = await AccessTestData.SeedAsync(factory, "CC4A");
        var b = await AccessTestData.SeedAsync(factory, "CC4B");
        var courseB = await AcademicTestKit.AddCourseAsync(factory, b.Root);
        await AcademicTestKit.AllocateAsync(factory, courseB, b.Root);
        var who = TestCurrentContext.For(a.Org.Id, a.School1.Path);

        Assert.False(await WithCatalogAsync(who, c => c.IsAvailableAtAsync(courseB.Id, a.School1)));
        Assert.Empty(await WithCatalogAsync(who, c => c.AvailableAtAsync(a.School1)));
    }
}
