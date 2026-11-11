using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Staff;

[Collection(ApiCollection.Name)]
public sealed class StaffListTests
{
    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;

    public StaffListTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IEmailSender>(new FakeEmailSender())));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RoleAssignment caller, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _host.CreateClient().SendAsync(request);
    }

    private async Task<StaffDetailDto> CreateAsync(
        RoleAssignment admin, AccessTree tree, Guid home, string nameAr, string nameEn, string roleKey = BuiltInRoles.Teacher,
        Guid? roleUnit = null, string? mobile = null)
    {
        var request = new CreateStaffRequest(
            nameAr, nameEn, $"s{Guid.NewGuid():N}@lms.test", mobile, null, null, null, null, home, null,
            [new StaffRoleInput(tree.Roles[roleKey].Id, roleUnit ?? home, null, null)]);
        var response = await SendAsync(HttpMethod.Post, Routes.Staff, admin, request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StaffDetailDto>())!;
    }

    private async Task<PagedResult<StaffSummaryDto>> ListAsync(RoleAssignment caller, string query = "")
    {
        var response = await SendAsync(HttpMethod.Get, Routes.Staff + query, caller);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<StaffSummaryDto>>())!;
    }

    private async Task<(AccessTree Tree, RoleAssignment Admin)> AdminAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        return (tree, await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()));
    }

    [Fact]
    public async Task List_filters_by_unit_role_status_and_arabic_name_search()
    {
        var (tree, admin) = await AdminAsync("SL1");
        var ahmed = await CreateAsync(admin, tree, tree.School1.Id, "أحمد علي", "Ahmed Ali");
        await CreateAsync(admin, tree, tree.School1.Id, "فاطمة", "Fatma", BuiltInRoles.Supervisor);
        await CreateAsync(admin, tree, tree.School2.Id, "خالد", "Khaled");

        var school1 = await ListAsync(admin, $"?orgUnitId={tree.School1.Id}");
        var teachers = await ListAsync(admin, $"?roleId={tree.Roles[BuiltInRoles.Teacher].Id}&orgUnitId={tree.Root.Id}");
        var byArabic = await ListAsync(admin, $"?orgUnitId={tree.Root.Id}&search={Uri.EscapeDataString("احمد")}"); // alef-normalized
        var deactivated = await ListAsync(admin, $"?orgUnitId={tree.Root.Id}&status={PersonStatuses.Deactivated}");

        Assert.Equal(2, school1.Total);
        Assert.Equal(2, teachers.Total);
        Assert.Equal(ahmed.Summary.Id, Assert.Single(byArabic.Items).Id);
        Assert.Empty(deactivated.Items);
    }

    [Fact]
    public async Task Search_by_email_prefix_and_exact_mobile()
    {
        var (tree, admin) = await AdminAsync("SL2");
        var created = await CreateAsync(admin, tree, tree.School1.Id, "س", "S", mobile: "+201001112223");
        await CreateAsync(admin, tree, tree.School1.Id, "ص", "P");

        var byEmail = await ListAsync(admin, $"?orgUnitId={tree.Root.Id}&search={created.Summary.Email[..8]}");
        var byMobile = await ListAsync(admin, $"?orgUnitId={tree.Root.Id}&search={Uri.EscapeDataString("+20 100 111 2223")}");

        Assert.Equal(created.Summary.Id, Assert.Single(byEmail.Items).Id);
        Assert.Equal(created.Summary.Id, Assert.Single(byMobile.Items).Id);
    }

    [Fact]
    public async Task Shared_in_staff_appear_with_limited_view()
    {
        var (tree, admin) = await AdminAsync("SL3");
        var shared = await CreateAsync(admin, tree, tree.School1.Id, "مشترك", "Shared", roleUnit: tree.School2.Id);
        var school2Admin = await AccessTestData.AssignAsync(_factory, tree, tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var list = await ListAsync(school2Admin);
        var detail = await SendAsync(HttpMethod.Get, Routes.StaffMember(shared.Summary.Id), school2Admin);
        var withoutShared = await ListAsync(school2Admin, "?includeShared=false");

        var row = Assert.Single(list.Items);
        Assert.True(row.IsSharedIn);
        Assert.Single(row.Roles);
        Assert.Empty(withoutShared.Items);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var dto = (await detail.Content.ReadFromJsonAsync<StaffDetailDto>())!;
        Assert.True(dto.IsLimitedView);
        Assert.Null(dto.NationalId);
        Assert.Null(dto.Summary.Mobile);
        Assert.Empty(dto.StatusHistory);
    }

    [Fact]
    public async Task Profile_of_staff_homed_outside_the_callers_scope_is_404()
    {
        var (tree, admin) = await AdminAsync("SL4");
        var created = await CreateAsync(admin, tree, tree.School1.Id, "س", "S"); // no role at School 2
        var school2Admin = await AccessTestData.AssignAsync(_factory, tree, tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, Routes.StaffMember(created.Summary.Id), school2Admin)).StatusCode);
        Assert.Empty((await ListAsync(school2Admin)).Items);
    }

    [Fact]
    public async Task List_is_paged_and_sorted_and_roles_show_only_assignments_in_scope()
    {
        var (tree, admin) = await AdminAsync("SL5");
        foreach (var (ar, en) in new[] { ("ج", "Charlie"), ("أ", "Alpha"), ("ب", "Bravo") })
        {
            await CreateAsync(admin, tree, tree.School1.Id, ar, en);
        }

        var first = await ListAsync(admin, $"?orgUnitId={tree.Root.Id}&sort=name&pageSize=2");
        var second = await ListAsync(admin, $"?orgUnitId={tree.Root.Id}&sort=name&pageSize=2&page=2");
        var desc = await ListAsync(admin, $"?orgUnitId={tree.Root.Id}&sort=-name&pageSize=1");

        Assert.Equal(3, first.Total);
        Assert.Equal(["Alpha", "Bravo"], first.Items.Select(x => x.NameEn));
        Assert.Equal(["Charlie"], second.Items.Select(x => x.NameEn));
        Assert.Equal(["Charlie"], desc.Items.Select(x => x.NameEn));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, Routes.Staff + "?pageSize=201", admin)).StatusCode);
    }
}
