using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Students;

[Collection(ApiCollection.Name)]
public sealed class StudentCodeGeneratorTests(LmsApiFactory factory)
{
    /// <summary>One request scope with the platform context, like a system job: the generator and its DbContext.</summary>
    private sealed class Scope : IDisposable
    {
        private readonly IServiceScope _scope;

        public Scope(IServiceProvider services)
        {
            _scope = services.CreateScope();
            _scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        }

        public IStudentCodeGenerator Generator => _scope.ServiceProvider.GetRequiredService<IStudentCodeGenerator>();

        public LmsDbContext Db => _scope.ServiceProvider.GetRequiredService<LmsDbContext>();

        public ISettingsResolver Settings => _scope.ServiceProvider.GetRequiredService<ISettingsResolver>();

        public void Dispose() => _scope.Dispose();
    }

    private async Task<string> NextAsync(OrgUnit unit)
    {
        using var scope = new Scope(factory.Services);
        return await scope.Generator.GenerateAsync(unit, default);
    }

    private async Task SetPatternAsync(OrgUnit unit, string pattern)
    {
        using var scope = new Scope(factory.Services);
        await scope.Settings.SetAsync(SettingKeys.StudentsCodePattern, unit.Id, pattern, false);
    }

    [Fact]
    public async Task One_sequence_runs_across_all_schools_of_the_client()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC1");

        Assert.Equal("000001", await NextAsync(tree.School1));
        Assert.Equal("000002", await NextAsync(tree.School2));
        Assert.Equal("000003", await NextAsync(tree.School1));
        Assert.Equal("000004", await NextAsync(tree.Root));
    }

    [Fact]
    public async Task Sequence_does_not_reset_on_pattern_change_and_is_not_per_school()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC2");
        Assert.Equal("000001", await NextAsync(tree.School1));
        Assert.Equal("000002", await NextAsync(tree.School1));

        await SetPatternAsync(tree.Root, "S-{Seq:4}");
        await SetPatternAsync(tree.School2, "B{Seq:3}"); // a school may use its own pattern, the counter stays the client's

        Assert.Equal("S-0003", await NextAsync(tree.School1));
        Assert.Equal("B004", await NextAsync(tree.School2));
        await SetPatternAsync(tree.Root, "{Seq:6}");
        Assert.Equal("000005", await NextAsync(tree.School1));
    }

    [Fact]
    public async Task RootCode_token_uses_the_code_of_the_client_root()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC3");
        await SetPatternAsync(tree.Root, "G-{RootCode}-{Seq:3}");

        Assert.Equal($"G-{tree.Root.Code}-001", await NextAsync(tree.School1));
    }

    [Fact]
    public async Task Rollback_does_not_consume_a_number()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC4");
        using (var scope = new Scope(factory.Services))
        {
            await using var transaction = await scope.Db.Database.BeginTransactionAsync();
            Assert.Equal("000001", await scope.Generator.GenerateAsync(tree.School1, default));
            await transaction.RollbackAsync();
        }

        Assert.Equal("000001", await NextAsync(tree.School1));

        using (var scope = new Scope(factory.Services))
        {
            await using var transaction = await scope.Db.Database.BeginTransactionAsync();
            Assert.Equal("000002", await scope.Generator.GenerateAsync(tree.School1, default));
            await transaction.CommitAsync();
        }

        Assert.Equal("000003", await NextAsync(tree.School1));
    }

    [Fact]
    public async Task Concurrent_generation_gives_distinct_codes()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC5");

        var codes = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            using var scope = new Scope(factory.Services);
            await using var transaction = await scope.Db.Database.BeginTransactionAsync();
            var code = await scope.Generator.GenerateAsync(i % 2 == 0 ? tree.School1 : tree.School2, default);
            await transaction.CommitAsync();
            return code;
        }));

        Assert.Equal(20, codes.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 20).Select(n => n.ToString("D6")).Order(), codes.Order());
    }

    [Fact]
    public async Task Exhausted_seq_is_reported_not_wrapped()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC6");
        await SetPatternAsync(tree.Root, "{Seq:1}");
        for (var i = 1; i <= 9; i++)
        {
            Assert.Equal(i.ToString(), await NextAsync(tree.School1));
        }

        await Assert.ThrowsAsync<StudentCodeExhaustedException>(() => NextAsync(tree.School1));
        await Assert.ThrowsAsync<StudentCodeExhaustedException>(() => NextAsync(tree.School2)); // and it stays exhausted
    }

    [Fact]
    public async Task Code_taken_manually_is_skipped_even_for_a_deleted_student()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC7");
        await PeopleTestKit.AddStudentAsync(factory, tree.School2, "000001"); // a manual entry
        var deleted = await PeopleTestKit.AddStudentAsync(factory, tree.School1, "000002");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Set<StudentProfile>().Remove(await db.Set<StudentProfile>().SingleAsync(s => s.Id == deleted.Id));
            await db.SaveChangesAsync();
        }

        Assert.Equal("000003", await NextAsync(tree.School1));
        Assert.Equal("000004", await NextAsync(tree.School1));
    }

    [Fact]
    public async Task Counter_rows_are_isolated_per_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "SC8A");
        var b = await AccessTestData.SeedAsync(factory, "SC8B");

        Assert.Equal("000001", await NextAsync(a.School1));
        Assert.Equal("000002", await NextAsync(a.School1));
        Assert.Equal("000001", await NextAsync(b.School1)); // another client starts at 1

        await using var asB = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        var visible = await asB.Set<StudentCodeCounter>().ToListAsync();
        Assert.Equal(b.Org.Id, Assert.Single(visible).OrganizationId);
        Assert.Equal(1, visible[0].NextValue);
    }

    [Fact]
    public async Task Manual_code_is_validated_for_shape_and_uniqueness_per_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "SC9A");
        var b = await AccessTestData.SeedAsync(factory, "SC9B");
        var student = await PeopleTestKit.AddStudentAsync(factory, a.School1, "MAN-001");
        using var scope = new Scope(factory.Services);

        Assert.Equal(ProblemCodes.student_code_taken, await scope.Generator.ValidateManualAsync(a.Org.Id, "MAN-001", null, default));
        Assert.Equal(ProblemCodes.student_code_taken, await scope.Generator.ValidateManualAsync(a.Org.Id, "man-001", null, default)); // case-insensitive
        Assert.Null(await scope.Generator.ValidateManualAsync(a.Org.Id, "MAN-001", student.Id, default)); // editing the same student
        Assert.Null(await scope.Generator.ValidateManualAsync(b.Org.Id, "MAN-001", null, default)); // another client
        Assert.Null(await scope.Generator.ValidateManualAsync(a.Org.Id, "MAN-002", null, default));
        foreach (var bad in new[] { string.Empty, "has space", "under_score", "طالب1", new string('A', 31) })
        {
            Assert.Equal(ProblemCodes.validation_failed, await scope.Generator.ValidateManualAsync(a.Org.Id, bad, null, default));
        }
    }

    [Fact]
    public async Task Invalid_pattern_setting_is_400_invalid_code_pattern()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC10");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        async Task<HttpResponseMessage> PutAsync(string pattern)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, Routes.OrgUnitSetting(tree.Root.Id, SettingKeys.StudentsCodePattern.Name))
            {
                Content = JsonContent.Create(new SetSettingRequest(JsonSerializer.Serialize(pattern), false)),
            };
            request.Headers.Add(TestAuthHandler.UserHeader, admin.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, admin.Id.ToString());
            return await factory.CreateClient().SendAsync(request);
        }

        foreach (var bad in new[] { "{SchoolCode}-{Seq:4}", "{Seq:3}{Seq:3}", "no-seq", "S_{Seq:4}" })
        {
            var response = await PutAsync(bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.invalid_code_pattern, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await PutAsync("S-{RootCode}-{Seq:5}")).StatusCode);
    }
}
