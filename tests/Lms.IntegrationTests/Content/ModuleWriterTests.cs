using System.Net.Http.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Content;

/// <summary>M4a Task 3: the seam feature kinds use to put their activity into a lesson.</summary>
[Collection(ApiCollection.Name)]
public sealed class ModuleWriterTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static ModuleMetadata Metadata(string visibility = ModuleVisibilities.Published, DateTime? from = null, DateTime? to = null,
        string target = ModuleTargets.ClassCourse, Guid? groupId = null) =>
        new("واجب", "Homework", visibility, from, to, target, groupId, CompletionRules.Submit, null);

    private IServiceScope PlatformScope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        return scope;
    }

    [Fact]
    public async Task CreateForRef_creates_module_in_callers_transaction()
    {
        var w = await _t.SetUpAsync("MW1");
        var refId = Guid.NewGuid();
        Guid kept, rolledBack;
        using (var scope = PlatformScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IModuleWriter>();
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                rolledBack = await writer.CreateForRefAsync(w.ClassCourse.Id, Lesson1, ModuleKinds.Assignment, Guid.NewGuid(), Metadata());
                await tx.RollbackAsync();
            }

            db.ChangeTracker.Clear();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                kept = await writer.CreateForRefAsync(w.ClassCourse.Id, Lesson1, ModuleKinds.Assignment, refId, Metadata());
                await tx.CommitAsync();
            }
        }

        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await check.Set<LessonModule>().AnyAsync(m => m.Id == rolledBack));
        var module = await check.Set<LessonModule>().SingleAsync(m => m.Id == kept);
        Assert.Equal((ModuleKinds.Assignment, refId, 1, w.ClassCourse.Id), (module.Kind, module.RefId, module.Order, module.ClassCourseId));
    }

    [Fact]
    public async Task CreateForRef_rejects_content_kinds_bad_ranges_foreign_groups_and_closed_classes()
    {
        var w = await _t.SetUpAsync("MW2");
        using var scope = PlatformScope();
        var writer = scope.ServiceProvider.GetRequiredService<IModuleWriter>();

        var content = await Assert.ThrowsAsync<ModuleRuleException>(() =>
            writer.CreateForRefAsync(w.ClassCourse.Id, Lesson1, ModuleKinds.Page, Guid.NewGuid(), Metadata()));
        var range = await Assert.ThrowsAsync<ModuleRuleException>(() =>
            writer.CreateForRefAsync(w.ClassCourse.Id, Lesson1, ModuleKinds.Quiz, Guid.NewGuid(), Metadata(ModuleVisibilities.Scheduled)));
        var group = await Assert.ThrowsAsync<ModuleRuleException>(() =>
            writer.CreateForRefAsync(w.ClassCourse.Id, Lesson1, ModuleKinds.Quiz, Guid.NewGuid(), Metadata(target: ModuleTargets.Group, groupId: Guid.NewGuid())));
        var lesson = await Assert.ThrowsAsync<ModuleRuleException>(() =>
            writer.CreateForRefAsync(w.ClassCourse.Id, Guid.NewGuid(), ModuleKinds.Quiz, Guid.NewGuid(), Metadata()));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Lms.Domain.Classes.Class>().SingleAsync(c => c.Id == w.Class.Id)).SetStatus(Lms.Domain.Classes.Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        using var closedScope = PlatformScope(); // a fresh context: the first one already tracks the class as Active
        var closedWriter = closedScope.ServiceProvider.GetRequiredService<IModuleWriter>();
        await Assert.ThrowsAsync<EnrollmentRuleException>(() =>
            closedWriter.CreateForRefAsync(w.ClassCourse.Id, Lesson1, ModuleKinds.Quiz, Guid.NewGuid(), Metadata()));
        Assert.Equal(["validation_failed", "module_visibility_range_invalid", "module_target_invalid", "validation_failed"],
            new[] { content.Code, range.Code, group.Code, lesson.Code });
    }

    [Fact]
    public async Task UpdateMetadata_and_DeleteForRef_change_and_soft_delete_the_module()
    {
        var w = await _t.SetUpAsync("MW3");
        var refId = Guid.NewGuid();
        Guid id;
        using (var scope = PlatformScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IModuleWriter>();
            id = await writer.CreateForRefAsync(w.ClassCourse.Id, Lesson1, ModuleKinds.Quiz, refId, Metadata());
            await writer.UpdateMetadataAsync(id, Metadata(ModuleVisibilities.Hidden) with { TitleEn = "Renamed" });
        }

        var renamed = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Lms.Shared.Routes.Module(id)))
            .Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        using (var scope = PlatformScope())
        {
            await scope.ServiceProvider.GetRequiredService<IModuleWriter>().DeleteForRefAsync(ModuleKinds.Quiz, refId);
            await scope.ServiceProvider.GetRequiredService<IModuleWriter>().DeleteForRefAsync(ModuleKinds.Quiz, refId); // idempotent
        }

        Assert.Equal(("Renamed", ModuleVisibilities.Hidden, refId), (renamed.Node.TitleEn, renamed.Node.Visibility, renamed.Content.RefId));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Lms.Shared.Routes.Module(id))).StatusCode);
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await check.Set<LessonModule>().AnyAsync(m => m.Id == id));
    }
}
