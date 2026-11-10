# M0 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. In this project each task = one GitHub issue per lane; the Team Lead assigns lanes (BE / FE / QA) as marked on each task.

**Goal:** A running .NET 10 solution where a user logs in, picks an active context (role at an org unit), and sees a bilingual (Arabic RTL / English) Blazor WASM shell whose menu reflects their permissions — on top of a persistence kernel that enforces client and subtree isolation, auditing, soft delete, inheritable settings/policies and file storage.

**Architecture:** Modular monolith. `Lms.Api` (ASP.NET Core minimal APIs grouped per feature folder, OpenIddict server, serves the WASM app) → `Lms.Infrastructure` (EF Core MySQL, MinIO, interceptors) → `Lms.Domain` (entities, abstractions). `Lms.Shared` holds contracts and permission constants used by both API and `Lms.Client` (Blazor WebAssembly + MudBlazor).

**Tech Stack:** .NET 10, EF Core 10 + Pomelo MySQL provider, MySQL 8.4, MinIO, ASP.NET Identity, OpenIddict 7, MudBlazor 8, Serilog, Hangfire (registered, used from M1), xUnit, Testcontainers (MySql, Minio), bUnit, Microsoft.Playwright.

**Spec:** `docs/PRD.md` (§1.3, §3.3, §4.2, §4.3, §5 X-01/X-02/X-03/X-05/X-08/X-11, FR-SEC-01/03/04/05). Master plan: `docs/DEV-PLAN.md`.

## Global Constraints
- Target framework `net10.0` for every project; `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>` in `Directory.Build.props`.
- Central package versions in `Directory.Packages.props`; no version attributes in `.csproj` files.
- If Pomelo has no EF Core 10–compatible release, use `MySql.EntityFrameworkCore` (Oracle) instead and record it in `docs/adr/0001-mysql-provider.md`.
- Database charset `utf8mb4`, collation `utf8mb4_0900_ai_ci`; path columns ASCII (`ascii_general_ci`).
- Primary keys: `Guid` created with `Guid.CreateVersion7()`.
- Every tenant-owned table has `OrganizationId`; every unit-scoped table also has `OrgUnitId` and `OrgUnitPath` (PRD §4.2).
- OrgUnit path format: `/` + segments of `Id.ToString("N")` each followed by `/` — e.g. `/{rootN}/{childN}/`.
- Active context header: `X-Lms-Context: {roleAssignmentId}`.
- Policy-block message (exact copy, en): `Blocked by parent organization policy`; ar: `محظور بسياسة الوحدة الأعلى`.
- Default UI culture `ar`; supported `ar`, `en`. RTL when culture is `ar`.
- Account lockout: 5 failed attempts → 15 minutes.
- All UI strings come from resources (`Lms.Client/Resources/SharedResource.{ar,en}.resx`).
- Dev seed runs only when `ASPNETCORE_ENVIRONMENT=Development`; seed credentials live in `src/Lms.Api/appsettings.Development.json` under `Seed:` (dev-only values, never production).

## Review Focus
1. **No context / bad context:** a request with no `X-Lms-Context`, an assignment of another user, or an assignment past `ValidTo` must get 403 and never data — the isolation filter with a null scope returns zero rows, not all rows. (Tests in Task 2 and Task 5.)
2. **Ancestor policy vs. local admin:** an Org Admin assigned at a child unit is still blocked by a policy set on an ancestor. (Test in Task 7.)
3. **Locked setting override attempt:** setting a value under a locked ancestor fails with `SettingLockedException`, and resolution ignores pre-existing lower values. (Test in Task 6.)
4. **Arabic and emoji round-trip:** names and audit JSON containing Arabic text and emoji survive save/load unchanged (utf8mb4). (Test in Task 3.)
5. **Soft-deleted unit:** a soft-deleted OrgUnit disappears from queries while a sibling stays visible; restoring brings it back. (Test in Task 2.)

---

### Task 1: Solution scaffold, local infrastructure, CI — lane: Lead/BE

**Files:**
- Create: `LMS.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` (SDK 10.0.x), `.editorconfig`
- Create projects: `src/Lms.Api`, `src/Lms.Domain`, `src/Lms.Infrastructure`, `src/Lms.Shared`, `src/Lms.Client` (blazorwasm standalone), `tests/Lms.UnitTests`, `tests/Lms.IntegrationTests`, `tests/Lms.Client.Tests`, `tests/Lms.E2ETests`
- Create: `docker-compose.yml` (mysql:8.4 port 3306, minio port 9000/9001, mailpit 1025/8025, named volumes)
- Create: `.github/workflows/ci.yml`
- Create: `src/Lms.Api/Program.cs` (health checks, Serilog, hosts WASM via `UseBlazorFrameworkFiles` + `MapFallbackToFile("index.html")`)
- Test: `tests/Lms.IntegrationTests/Infrastructure/LmsApiFactory.cs`, `tests/Lms.IntegrationTests/HealthTests.cs`

**Interfaces:**
- Produces: `LmsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` starting MySQL + MinIO Testcontainers and overriding connection strings `ConnectionStrings:Lms` and `Storage:Endpoint`; `public partial class Program` exposed.
- Project references: Api → Infrastructure, Shared; Infrastructure → Domain, Shared; Client → Shared; Domain → nothing.

- [ ] **Step 1: Write the failing test** `HealthTests.Health_endpoint_returns_healthy` — `GET /health` → 200 and body `Healthy`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.IntegrationTests --filter HealthTests` → FAIL (no endpoint).
- [ ] **Step 3: Implement** `Program.cs` with `AddHealthChecks()` (+ MySQL check) and `MapHealthChecks("/health")`; Serilog console sink with correlation id enricher.
- [ ] **Step 4: Run** the test → PASS. Run `dotnet build LMS.slnx` → 0 warnings.
- [ ] **Step 5: CI** — `ci.yml` on `pull_request` and `push` to `main`: setup-dotnet 10, `dotnet build -c Release`, `dotnet test` for Unit, Integration, Client.Tests (Docker available on ubuntu-latest), then E2E job (compose up, run API, `pwsh tests/Lms.E2ETests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium`, run E2E).
- [ ] **Step 6: Commit** `chore: scaffold solution, docker compose and CI`.

### Task 2: Persistence kernel + org tree entities + isolation filters — lane: BE

**Files:**
- Create: `src/Lms.Domain/Common/Entity.cs`, `AuditableEntity.cs`, `ITenantOwned.cs`, `IOrgUnitScoped.cs`, `ISoftDelete.cs`
- Create: `src/Lms.Domain/Abstractions/ICurrentContext.cs`, `IClock.cs`
- Create: `src/Lms.Domain/Organizations/Organization.cs`, `OrganizationType.cs`, `LevelType.cs`, `OrgUnit.cs`, `OrgUnitStatus.cs`, `OrgUnitPath.cs`
- Create: `src/Lms.Infrastructure/Persistence/LmsDbContext.cs`, `Configurations/*.cs`, `Interceptors/StampingInterceptor.cs`, `DependencyInjection.cs`
- Create: migration `InitialKernel`
- Test: `tests/Lms.UnitTests/Organizations/OrgUnitPathTests.cs`, `tests/Lms.IntegrationTests/Persistence/IsolationTests.cs`, `tests/Lms.IntegrationTests/Infrastructure/TestCurrentContext.cs`

**Interfaces:**
- Produces:
  - `abstract class Entity { Guid Id }` (init `Guid.CreateVersion7()`); `abstract class AuditableEntity : Entity, ISoftDelete` with `CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, DeletedAt`.
  - `interface ITenantOwned { Guid OrganizationId { get; } }`; `interface IOrgUnitScoped : ITenantOwned { Guid OrgUnitId { get; } string OrgUnitPath { get; } }`.
  - `interface ICurrentContext { Guid? UserId; Guid? OrganizationId; Guid? AssignmentId; string? ScopePath; bool IsPlatformAdmin; }`
  - `Organization(Guid id, string nameAr, string nameEn, OrganizationType type)`; `enum OrganizationType { SchoolGroup, University }`.
  - `LevelType : AuditableEntity, ITenantOwned { NameAr, NameEn, int Order, bool CanHostClasses }`.
  - `OrgUnit : AuditableEntity, IOrgUnitScoped` with `ParentId, LevelTypeId, NameAr, NameEn, Code, Path, int Depth, OrgUnitStatus Status, bool IsLevelTransfer, Guid? NextTransferUnitId, bool IsCourseManagementLevel`; `OrgUnitPath => Path`, `OrgUnitId => Id`; factories `static OrgUnit CreateRoot(Guid organizationId, Guid levelTypeId, string nameAr, string nameEn, string code)` and `static OrgUnit CreateChild(OrgUnit parent, Guid levelTypeId, string nameAr, string nameEn, string code)`.
  - `static class OrgUnitPath { string Append(string parentPath, Guid id); IReadOnlyList<string> AncestorsAndSelf(string path); bool IsWithin(string path, string scopePath); }`
  - `LmsDbContext(DbContextOptions<LmsDbContext>, ICurrentContext)` applying global filters to every `ITenantOwned` (`ctx.IsPlatformAdmin || e.OrganizationId == ctx.OrganizationId`), every `IOrgUnitScoped` (`ctx.IsPlatformAdmin || (ctx.ScopePath != null && e.OrgUnitPath.StartsWith(ctx.ScopePath))`), and every `ISoftDelete` (`!e.IsDeleted`).
  - `StampingInterceptor` sets `OrganizationId` from context when empty on insert, sets audit stamps, converts deletes of `ISoftDelete` into updates.

- [ ] **Step 1: Unit tests** `OrgUnitPathTests`: `Append("/a/", id)` == `"/a/" + id.ToString("N") + "/"`; `AncestorsAndSelf("/a/b/c/")` == `["/a/", "/a/b/", "/a/b/c/"]`; `IsWithin("/a/b/", "/a/")` true, `IsWithin("/a/", "/a/b/")` false.
- [ ] **Step 2: Integration tests** `IsolationTests` (seed client A tree root→School1, School2 and client B tree via `IgnoreQueryFilters` setup):
  - `Client_A_context_never_sees_client_B_units`
  - `School1_scope_sees_own_subtree_but_not_School2`
  - `Root_scope_sees_all_units_of_its_client`
  - `Null_scope_returns_no_scoped_rows` (Review Focus 1)
  - `Soft_deleted_unit_is_hidden_and_sibling_visible` + restore brings it back (Review Focus 5)
  - `Insert_stamps_OrganizationId_from_context`
- [ ] **Step 3: Run** → FAIL. **Step 4: Implement** entities, configurations (path column `varchar(400)` ascii, index on `(OrganizationId, Path)`), filters, interceptor, migration. **Step 5: Run** → PASS.
- [ ] **Step 6: Commit** `feat(kernel): persistence kernel with client and subtree isolation`.

### Task 3: Audit log capture — lane: BE

**Files:**
- Create: `src/Lms.Domain/Auditing/AuditEntry.cs`, `AuditedAttribute.cs`, `AuditAction.cs`
- Create: `src/Lms.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs`
- Modify: `LmsDbContext` (DbSet + config), `OrgUnit` gets `[Audited]`
- Test: `tests/Lms.IntegrationTests/Persistence/AuditTests.cs`

**Interfaces:**
- Consumes: `ICurrentContext`, `IClock`.
- Produces: `AuditEntry { Guid? OrganizationId, Guid? UserId, Guid? AssignmentId, AuditAction Action, string EntityType, Guid EntityId, string? BeforeJson, string? AfterJson, DateTime TimestampUtc, string? IpAddress }`; `enum AuditAction { Created, Updated, Deleted, SecurityEvent }`; `IAuditWriter.WriteSecurityEventAsync(string eventName, Guid? userId, string? details, CancellationToken)` for login events (used by Task 4).

- [ ] **Step 1: Tests** `Create_update_delete_of_audited_entity_writes_entries` (3 entries; Updated has before/after with only changed properties); `Unaudited_entity_writes_nothing`; `Arabic_and_emoji_round_trip_in_names_and_audit_json` with name `"مدرسة النور 🌟"` (Review Focus 4).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** interceptor (collect in `SavingChanges`, persist in same transaction). **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(audit): capture audit entries for audited entities`.

### Task 4: Identity, OpenIddict and login page — lane: BE (+FE for page styling)

**Files:**
- Create: `src/Lms.Domain/Identity/User.cs` (`IdentityUser<Guid>` + `DisplayNameAr, DisplayNameEn, IsPlatformAdmin, UserStatus Status`)
- Create: `src/Lms.Infrastructure/Identity/IdentitySetup.cs`, `OpenIddictSetup.cs`, `OpenIddictClientSeeder.cs`
- Create: `src/Lms.Api/Features/Auth/AuthorizationEndpoints.cs` (`/connect/authorize`, `/connect/token`, `/connect/logout`, `/connect/userinfo`), `src/Lms.Api/Pages/Account/Login.cshtml(.cs)` (bilingual, RTL-aware), `Logout.cshtml`
- Test: `tests/Lms.IntegrationTests/Auth/AuthTests.cs`

**Interfaces:**
- Produces: OpenIddict client `lms-wasm` (public, authorization code + PKCE, scopes `openid profile lms_api`, redirect `{origin}/authentication/login-callback`, post-logout `{origin}/authentication/logout-callback`); access token claims `sub`, `name`, `platform_admin` (when true). API protected by `OpenIddict.Validation.AspNetCore` default scheme.
- Consumes: `IAuditWriter` (login success/failure security events).

- [ ] **Step 1: Tests** `Password_grant_is_rejected` (POST `/connect/token` grant_type=password → 400 `unsupported_grant_type`); `Login_with_valid_credentials_redirects_back_to_authorize`; `Five_failed_logins_lock_account_for_15_minutes`; `Deactivated_user_cannot_log_in_and_sees_blocked_message`; `Login_writes_security_audit_events`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** Identity (lockout 5/15 min, password policy: 8+ chars, upper, lower, digit), OpenIddict server with ephemeral dev keys / configured certs in prod, login page. **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(auth): identity with OpenIddict authorization code + PKCE`.

### Task 5: Roles, permission catalog, assignments, current context — lane: BE (contract by Lead)

**Files:**
- Create: `src/Lms.Shared/Authorization/Permissions.cs`, `BuiltInRoles.cs`, `PolicyCatalog.cs`, `ContextHeader.cs`
- Create: `src/Lms.Domain/Access/Role.cs`, `RolePermission.cs`, `RoleAssignment.cs`, `RoleBaseKind.cs`
- Create: `src/Lms.Infrastructure/Access/DefaultRoleProvisioner.cs`, `src/Lms.Api/Infrastructure/CurrentContextMiddleware.cs`, `HttpCurrentContext.cs`
- Test: `tests/Lms.UnitTests/Access/PermissionCatalogTests.cs`, `tests/Lms.IntegrationTests/Access/CurrentContextTests.cs`

**Interfaces:**
- Produces:
  - `Permissions` nested static classes per module with `const string` values `"<Module>.<Action>"` — M0 set: `OrgUnits.{View,Create,Update,Delete}`, `Roles.{View,Create,Update,Delete}`, `Assignments.{View,Create,Delete}`, `Settings.{View,Update}`, `Policies.{View,Update}`, `Audit.{View,Export}`, `Files.{Upload,Download}`; `static IReadOnlyList<string> Permissions.All` (reflection, cached).
  - `BuiltInRoles` keys: `OrgAdmin, Manager, Supervisor, Counselor, Teacher, Student, Guardian`, each with default permission set and default labels ar/en per `OrganizationType` (PRD §3.1, §3.4).
  - `PolicyCatalog`: `IReadOnlyDictionary<string, string[]> BlockedPermissions` — M0 entries `CreateOrgUnits → [OrgUnits.Create]`; later milestones add theirs.
  - `Role : AuditableEntity, ITenantOwned { string? BuiltInKey, NameAr, NameEn, RoleBaseKind BaseKind, bool IsBuiltIn }`; `RolePermission { Guid RoleId, string Permission }`; `RoleAssignment : AuditableEntity, IOrgUnitScoped { Guid UserId, Guid RoleId, DateOnly? ValidFrom, DateOnly? ValidTo }`.
  - `IDefaultRoleProvisioner.ProvisionAsync(Guid organizationId, OrganizationType type, CancellationToken)` creates the 7 built-in roles for a client.
  - `CurrentContextMiddleware`: authenticated request + `X-Lms-Context` → loads assignment (ignoring filters) owned by `sub`, valid today, not deleted → populates `HttpCurrentContext` (`OrganizationId`, `ScopePath = assignment.OrgUnitPath`, `AssignmentId`). Invalid header → 403 ProblemDetails `invalid_context`. Platform admins without header → `IsPlatformAdmin = true`.

- [ ] **Step 1: Tests** (scoped-endpoint tests use a test-only endpoint `GET /test/scoped` that `LmsApiFactory` maps and that returns `ICurrentContext.ScopePath`) `Every_permission_constant_matches_Module_Action_format_and_is_unique`; `Every_policy_entry_references_existing_permissions`; `Provisioner_creates_seven_built_in_roles_with_labels_per_type` (University → Manager label `Dean`); `Request_without_context_header_gets_403_on_scoped_endpoint`; `Header_with_other_users_assignment_gets_403`; `Expired_assignment_gets_403` (Review Focus 1); `Valid_header_sets_scope_to_assignment_unit_path`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(access): roles, permission catalog and active context`.

### Task 6: Settings and policy inheritance engine — lane: BE

**Files:**
- Create: `src/Lms.Domain/Configuration/OrgUnitSetting.cs`, `OrgUnitPolicy.cs`, `SettingKey.cs`, `SettingLockedException.cs`
- Create: `src/Lms.Shared/Configuration/SettingKeys.cs` (M0 keys: `Ui.DefaultCulture` = `"ar"`, `Files.MaxUploadMb` = `200`)
- Create: `src/Lms.Infrastructure/Configuration/SettingsResolver.cs`, `PolicyEvaluator.cs`
- Test: `tests/Lms.IntegrationTests/Configuration/SettingsResolverTests.cs`, `PolicyEvaluatorTests.cs`

**Interfaces:**
- Produces:
  - `sealed record SettingKey<T>(string Name, T Default)`; `sealed record ResolvedSetting<T>(T Value, Guid? SourceOrgUnitId, bool IsLocked)`.
  - `ISettingsResolver.ResolveAsync<T>(SettingKey<T> key, string orgUnitPath, CancellationToken) : Task<ResolvedSetting<T>>`
  - `ISettingsResolver.SetAsync<T>(SettingKey<T> key, Guid orgUnitId, T value, bool isLocked, CancellationToken)` — throws `SettingLockedException(Guid lockingOrgUnitId)` when any strict ancestor has the key locked.
  - `IPolicyEvaluator.FindBlockingUnitAsync(string permission, string orgUnitPath, CancellationToken) : Task<Guid?>` — returns the highest ancestor-or-self unit holding a policy whose `PolicyCatalog` entry contains the permission.
  - Rows: `OrgUnitSetting : AuditableEntity, IOrgUnitScoped { string Key, string ValueJson, bool IsLocked }` `[Audited]`; `OrgUnitPolicy : AuditableEntity, IOrgUnitScoped { string PolicyKey }` `[Audited]`.
- Resolution algorithm: load rows for `OrgUnitPath.AncestorsAndSelf(path)` with `Key == name` (query with `IgnoreQueryFilters` + explicit `OrganizationId` filter, since ancestors are outside the caller's scope). If any row is locked, the **highest** locked row wins; otherwise the **deepest** row wins; no rows → `Default` with `SourceOrgUnitId = null`.

- [ ] **Step 1: Tests** `No_value_returns_default`; `Deepest_value_wins`; `Locked_ancestor_wins_over_deeper_values` and `Set_below_locked_ancestor_throws_SettingLockedException` (Review Focus 3); `Policy_on_ancestor_blocks_permission_in_descendant`; `Policy_on_sibling_does_not_block`; `Policy_without_catalog_mapping_does_not_block_unrelated_permission`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(config): inheritable settings with locks and policy evaluator`.

### Task 7: Access evaluator and endpoint permission filter — lane: BE

**Files:**
- Create: `src/Lms.Domain/Access/AccessDecision.cs`, `AccessDenialReason.cs`, `IAccessEvaluator.cs`
- Create: `src/Lms.Infrastructure/Access/AccessEvaluator.cs`, `src/Lms.Api/Infrastructure/PermissionEndpointFilter.cs`, `EndpointAuthorizationExtensions.cs`
- Test: `tests/Lms.IntegrationTests/Access/AccessEvaluatorTests.cs`, `PermissionFilterTests.cs`

**Interfaces:**
- Consumes: `ICurrentContext`, `IPolicyEvaluator`, roles/assignments from Task 5.
- Produces:
  - `sealed record AccessDecision(bool Allowed, AccessDenialReason Reason, Guid? BlockingOrgUnitId)`; `enum AccessDenialReason { None, NotAuthenticated, MissingPermission, OutOfScope, BlockedByPolicy }`.
  - `IAccessEvaluator.EvaluateAsync(string permission, string? resourceOrgUnitPath, CancellationToken) : Task<AccessDecision>` — resource path null ⇒ use context scope path. Order: authenticated → active role grants permission (cached per assignment for 60 s) → `OrgUnitPath.IsWithin(resource, scope)` → `IPolicyEvaluator`. Platform admin bypasses role/scope but **not** policies inside a client.
  - `RouteHandlerBuilder.RequirePermission(string permission)` and `RouteGroupBuilder.RequirePermission(string permission)`; denial → 403 ProblemDetails with extension `reason` (enum name) and, for `BlockedByPolicy`, `detail` = localized policy-block message (Global Constraints) and `blockingOrgUnitId`.
  - Relationship rules (teacher/student/guardian) are **not** in M0; modules add them by calling `IAccessEvaluator` and then their own check.

- [ ] **Step 1: Tests** `Granted_permission_within_scope_is_allowed`; `Missing_permission_is_denied`; `Resource_outside_scope_is_denied_OutOfScope`; `OrgAdmin_at_child_is_blocked_by_ancestor_policy` (Review Focus 2) with `detail == "Blocked by parent organization policy"` for `Accept-Language: en`; `Arabic_request_gets_arabic_block_message`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(access): access evaluator and RequirePermission endpoint filter`.

### Task 8: File storage on MinIO — lane: BE

**Files:**
- Create: `src/Lms.Domain/Files/IFileStorage.cs`, `StoredFile.cs`, `FileUploadRequest.cs`
- Create: `src/Lms.Infrastructure/Files/MinioFileStorage.cs`, `StorageOptions.cs`
- Create: `src/Lms.Api/Features/Files/FileEndpoints.cs` (`POST /api/files` multipart, `GET /api/files/{id}/url`)
- Test: `tests/Lms.IntegrationTests/Files/FileStorageTests.cs`

**Interfaces:**
- Produces:
  - `StoredFile : AuditableEntity, IOrgUnitScoped { string ObjectKey, string FileName, string ContentType, long SizeBytes }`.
  - `IFileStorage.UploadAsync(FileUploadRequest request, Stream content, CancellationToken) : Task<StoredFile>` where `FileUploadRequest(string FileName, string ContentType, long SizeBytes, Guid OrgUnitId)`; bucket `lms-{organizationId:N}` created on demand; object key `{orgUnitId:N}/{yyyy}/{MM}/{fileId:N}{ext}`.
  - `IFileStorage.GetDownloadUrlAsync(Guid fileId, TimeSpan ttl, CancellationToken) : Task<Uri>` (presigned GET, default ttl 10 min); `DeleteAsync(Guid fileId, CancellationToken)` (soft delete record; object removed by later retention job).
  - Max size from setting `Files.MaxUploadMb`; allowed extensions option `Storage:AllowedExtensions` (default `.pdf,.docx,.pptx,.xlsx,.jpg,.jpeg,.png,.mp4,.mp3,.zip`).
  - Endpoints require `Files.Upload` / `Files.Download`.

- [ ] **Step 1: Tests** `Upload_then_presigned_url_downloads_same_bytes`; `Upload_over_max_size_is_rejected_413`; `Disallowed_extension_is_rejected_415`; `Other_client_cannot_get_url_for_file` (404).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** (Minio .NET SDK). **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(files): MinIO file storage with presigned downloads`.

### Task 9: "Me" endpoints (contract for the shell) — lane: Lead contract → BE

**Files:**
- Create: `src/Lms.Shared/Me/MeDto.cs`, `UserContextDto.cs`, `EffectivePermissionsDto.cs`, `src/Lms.Shared/Routes.cs`
- Create: `src/Lms.Api/Features/Me/MeEndpoints.cs`
- Test: `tests/Lms.IntegrationTests/Me/MeEndpointsTests.cs`

**Interfaces:**
- Produces:
  - `sealed record MeDto(Guid UserId, string DisplayNameAr, string DisplayNameEn, bool IsPlatformAdmin, IReadOnlyList<UserContextDto> Contexts)`
  - `sealed record UserContextDto(Guid AssignmentId, Guid OrganizationId, string OrganizationNameAr, string OrganizationNameEn, Guid OrgUnitId, string OrgUnitNameAr, string OrgUnitNameEn, string RoleNameAr, string RoleNameEn)`
  - `sealed record EffectivePermissionsDto(Guid? AssignmentId, IReadOnlyList<string> Permissions)` — permissions granted by the active role **minus** those blocked by policies at the context unit.
  - `Routes.Me = "/api/me"`, `Routes.MyPermissions = "/api/me/permissions"`.
  - `/api/me` needs authentication only (no context header); `/api/me/permissions` needs a valid context header.

- [ ] **Step 1: Tests** `Me_lists_only_valid_assignments_of_caller`; `Permissions_reflect_role_and_exclude_policy_blocked`; `Me_requires_authentication_401`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(me): current user contexts and effective permissions`.

### Task 10: Blazor client shell (MudBlazor, auth, localization, RTL) — lane: FE

**Files:**
- Create/modify: `src/Lms.Client/Program.cs`, `App.razor`, `Layout/MainLayout.razor`, `Layout/LanguageSwitcher.razor`, `Services/CultureService.cs`, `Services/LmsApiClient.cs`, `Services/ContextStore.cs`, `Resources/SharedResource.cs`, `SharedResource.ar.resx`, `SharedResource.en.resx`, `wwwroot/index.html`, `Pages/Authentication.razor`, `Pages/Home.razor`
- Test: `tests/Lms.Client.Tests/Layout/MainLayoutTests.cs`, `Services/LmsApiClientTests.cs`

**Interfaces:**
- Consumes: `Routes`, `MeDto`, `EffectivePermissionsDto` (Task 9), `ContextHeader.Name` (Task 5).
- Produces:
  - `ICultureService { CultureInfo Current; bool IsRightToLeft; Task SetAsync(string culture) }` — persists `lms.culture` in localStorage, reloads the app; `BlazorWebAssemblyLoadAllGlobalizationData` enabled.
  - `IContextStore { Guid? ActiveAssignmentId; Task SetAsync(Guid assignmentId); event Action? Changed }` persisted as `lms.context`.
  - `LmsApiClient` (typed `HttpClient` with `BaseAddressAuthorizationMessageHandler`) adding header `X-Lms-Context` when `ActiveAssignmentId` is set; methods `GetMeAsync()`, `GetMyPermissionsAsync()`.
  - OIDC via `AddOidcAuthentication` (authority = app origin, client `lms-wasm`, scopes `openid profile lms_api`).
  - `MainLayout` wraps content in `MudRTLProvider RightToLeft="@Culture.IsRightToLeft"`, app bar with language switcher, user menu, context name.

- [ ] **Step 1: bUnit tests** `MainLayout_is_rtl_when_culture_is_ar`; `MainLayout_is_ltr_when_culture_is_en`; `Api_client_adds_context_header_when_context_selected`; `Api_client_omits_header_without_context`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.Client.Tests` → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(client): MudBlazor shell with OIDC auth and ar/en RTL`.

### Task 11: Context chooser and permission-driven navigation — lane: FE

**Files:**
- Create: `src/Lms.Client/Navigation/NavCatalog.cs`, `NavItem.cs`, `Layout/NavMenu.razor`, `Pages/ChooseContext.razor`, `Layout/ContextSwitcher.razor`, `Services/PermissionState.cs`
- Test: `tests/Lms.Client.Tests/Navigation/NavMenuTests.cs`, `Pages/ChooseContextTests.cs`

**Interfaces:**
- Consumes: `LmsApiClient`, `IContextStore`, `Permissions` constants.
- Produces:
  - `sealed record NavItem(string ResourceKey, string Href, string Icon, string? RequiredPermission, bool PlatformAdminOnly = false)`; `NavCatalog.Items` (M0: Home, Organization tree → `OrgUnits.View`, Roles → `Roles.View`, Settings → `Settings.View`, Audit → `Audit.View`, Clients → platform admin only) — later milestones append items.
  - `PermissionState { bool Has(string permission); Task RefreshAsync(); }` loaded after context selection and on `IContextStore.Changed`.
  - Flow: after login, if `MeDto.Contexts.Count == 1` auto-select; if > 1 and no stored valid context → redirect to `/choose-context`; header `ContextSwitcher` lets the user switch at any time.

- [ ] **Step 1: bUnit tests** `NavMenu_hides_items_without_permission`; `NavMenu_shows_platform_items_only_to_platform_admin`; `Single_context_is_auto_selected`; `Multiple_contexts_show_chooser_and_store_selection`; `Stored_context_not_in_me_list_is_discarded`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(client): context chooser and permission-driven navigation`.

### Task 12: Dev seed and E2E smoke tests — lane: BE (seed) + QA (E2E)

**Files:**
- Create: `src/Lms.Infrastructure/Seeding/DevSeeder.cs`, `src/Lms.Api/appsettings.Development.json` (`Seed:PlatformAdminEmail`, `Seed:DefaultPassword` — dev-only values)
- Create: `tests/Lms.E2ETests/Infrastructure/E2EFixture.cs`, `tests/Lms.E2ETests/ShellTests.cs`, `tests/Lms.E2ETests/README.md`
- Modify: `README.md` (run instructions)

**Interfaces:**
- Produces: seeded data mirroring the backlog sheets —
  - Platform admin user.
  - Client "مجموعة مدارس التجمع / Altagamo3 Schools" (SchoolGroup): level types School › Stage › Section; tree School Altagamo3 → KG (`IsLevelTransfer`) → Section 1, Section 2; Primary (`IsLevelTransfer`) → Section 1..3.
  - Client "جامعة MNF / MNF University" (University): University › Faculty › Year › Specialization; MNF → FCI → First (`IsLevelTransfer`) → general, Soft, Bio; Second (`IsLevelTransfer`) → general, soft, bio; F-E → First → general; Second → Electric.
  - One user per built-in role in the school client (`orgadmin@`, `manager@`, `supervisor@`, `counselor@`, `teacher@`, `student@`, `guardian@` + domain `altagamo3.test`), plus a user holding two assignments (Teacher in KG and Supervisor in Primary) to exercise the chooser.
  - Seeder is idempotent.

- [ ] **Step 1: E2E tests** `Platform_admin_logs_in_and_sees_clients_menu`; `Multi_context_user_chooses_context_then_sees_arabic_rtl_shell` (assert `dir="rtl"` on root and Arabic menu labels); `Teacher_does_not_see_roles_or_audit_menu`; `Switching_language_to_english_renders_ltr`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.E2ETests` (with compose up and API running) → FAIL. **Step 3: Implement** seeder. **Step 4: Run** → PASS; run full `dotnet test LMS.slnx` → all green.
- [ ] **Step 5: Commit** `feat(seed): dev seed from backlog taxonomies and E2E smoke tests`.

---

## Execution notes for the Team Lead
- Order / parallelism: T1 → T2 → (T3 ∥ T4 ∥ T6 ∥ T8) → T5 → T7 → T9 → (T10 ∥ T11 by FE; FE may start T10 right after T1 using fakes) → T12.
- Contract PRs (Lead): `Permissions`/`BuiltInRoles`/`PolicyCatalog`/`ContextHeader` before T5; `MeDto`/`Routes` before T9 and T10.
- QA reviews every PR against this plan's test list and Review Focus; QA owns the E2E part of T12.
