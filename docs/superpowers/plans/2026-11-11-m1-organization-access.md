# M1 Organization & Access Implementation Plan

> **Status: APPROVED by the product owner (2026-11-10).** Owner answers are recorded as PRD decisions D16–D22 (see "Owner decisions" below). Team: Lead, BE1, BE2, FE1, FE2, QA1, QA2.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. In this project each task = one GitHub issue and one PR; the lane (Lead contract / BE1 / BE2 / FE / QA) is marked on each task.

**Goal:** A Platform Admin creates a school-group client and a university client with their first Org Admins; those admins build level types and trees (create, move, deactivate, delete, copy, templates), mark level-transfer and course-management levels, set policies and locked settings and feature flags, edit roles and assign them, define academic years, terms and calendars, read and export the audit log, and receive in-app notifications in real time — all bilingual (ar RTL / en), isolated per client and subtree.

**Architecture:** Same modular monolith as M0. Each module is a folder `src/Lms.Api/Features/<Module>/` holding endpoints, handlers and validators, plus `<Module>Module.cs` with `Add<Module>()` / `Map<Module>Endpoints()`. Domain entities in `src/Lms.Domain/<Module>/`, persistence config in `src/Lms.Infrastructure/Persistence/Configurations/<Module>/`, services in `src/Lms.Infrastructure/<Module>/`. Contracts (DTOs, routes, permission constants, catalogs) in `src/Lms.Shared` via Lead contract PRs. Unit-targeted endpoints authorize against the target unit's path through one helper (`UnitAccess`), client-wide resources against the client root's path. The client gets one typed API interface per module with a fake for bUnit/standalone.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs + Razor Pages (account pages), EF Core 10 + `MySql.EntityFrameworkCore` (ADR 0001), MySQL 8.4, OpenIddict, SignalR, MailKit (SMTP; Mailpit in dev), ClosedXML (Excel export), Blazor WebAssembly + MudBlazor 9, `Microsoft.AspNetCore.SignalR.Client`, xUnit, Testcontainers, bUnit, Playwright.

**Spec:** `docs/PRD.md` — §3 (roles, permissions, matrix §3.4), §4 (org structure, isolation, settings/policies §4.3, feature flags §4.4), §5 X-01/X-02/X-04/X-08, §6.1 FR-SEC-02/06/07, FR-AUD-01..04, §6.2 FR-ORG-01..11, FR-YEAR-01..03, §6.11 FR-NOT-01/02, decisions D1–D15. Master plan: `docs/DEV-PLAN.md` §2 "M1 Organization & Access", DoD §3.4. Previous plan: `docs/superpowers/plans/2026-11-10-m0-foundation.md` (its Global Constraints still apply).

## Global Constraints
- Every M0 Global Constraint still applies (path format, `X-Lms-Context`, policy-block copy, utf8mb4, Guid v7 keys, default culture `ar`, lockout 5 / 15 min).
- **Explicit permissions (DoD rule, from bug #21):** every new permission constant is added explicitly to the list of *each* built-in role it belongs to in `BuiltInRoles` per PRD §3.4 — never granted implicitly (no "OrgAdmin = all"). A permission no client role may hold goes into `Permissions.PlatformOnly`; no built-in or custom role may hold a `PlatformOnly` permission (enforced by a unit test and by the roles API).
- `Permissions.PlatformOnly = [Clients.View, Clients.Create, Clients.Update]`.
- **Unit-targeted endpoints** (anything acting on a specific OrgUnit or a row hanging off one) authorize with `UnitAccess.CheckUnitAsync(permission, orgUnitId)` (Task 5), which passes the *target unit's path* to `IAccessEvaluator`. A unit outside the caller's scope or in another client answers **404** `not_found` (never 403, never data). The route-level `.RequirePermission(...)` stays on every endpoint as the first gate.
- **Client-wide resources** (level types, custom roles, built-in role permission sets and labels) are writable only from the client root: authorize with `UnitAccess.CheckClientRootAsync(permission)`; an admin assigned below the root gets 403 `OutOfScope`.
- Errors are RFC 7807 ProblemDetails with extension `code` taken from `Lms.Shared.ProblemCodes` (Task 1); validation errors use `Results.ValidationProblem` (code `validation_failed`). The client shows `Error_{code}` from resources; for `BlockedByPolicy` it shows the server `detail` (already localized).
- `IgnoreQueryFilters()` is allowed only in `OrgTree`, `OrgUnitRepather`, the settings/years/calendar ancestor readers and the notification recipient resolver; each use carries the comment `// ISOLATION-BYPASS: <reason>` and re-applies `OrganizationId == <caller org>` explicitly. QA greps for this marker in every PR.
- New modules add **no** `DbSet` properties to `LmsDbContext` (use `db.Set<T>()`; configurations are picked up by `ApplyConfigurationsFromAssembly`). `Program.cs` gets exactly two lines per module (`builder.Services.Add<Module>()`, `app.Map<Module>Endpoints()`).
- One migration per PR, named after the task (e.g. `AddAcademicYears`). If another migration merged first, delete yours and the snapshot change, rebase, regenerate. `Down()` must work (`dotnet ef migrations script <prev> 0` reviewed by QA).
- Every new business entity: `AuditableEntity` (soft delete), `NameAr`/`NameEn` (or `TitleAr`/`TitleEn`), `[Audited]`, isolation test.
- OrgUnit `Code`: `^[A-Za-z0-9_-]{1,50}$`, unique among live units of one client (409 `code_taken`). Names: 1–200 chars after trim, both languages required.
- Paging: `page` ≥ 1, `pageSize` default 50, max 200.
- Password reset token lifetime **60 minutes**; invitation (set first password) lifetime **72 hours**; both single-use (security stamp rotates on success). Forgot-password and login POSTs are rate-limited to **5 requests / minute / IP** (429).
- Blocked-login copy (FR-SEC-07), exact: Expired — en `Your account has expired. Please contact your administrator.` / ar `انتهت صلاحية حسابك. يرجى التواصل مع مسؤول النظام.`; Withdrawn — en `This account is no longer active. Please contact your school or university.` / ar `هذا الحساب لم يعد نشطًا. يرجى التواصل مع المدرسة أو الجامعة.`; Deactivated — unchanged M0 `AccountDeactivated` resource.
- Forgot-password confirmation (same for known and unknown emails), exact: en `If an account exists for this email, we have sent a password reset link.` / ar `إذا كان هناك حساب بهذا البريد الإلكتروني، فقد أرسلنا رابط إعادة تعيين كلمة المرور.` Invalid/expired link: en `This link is invalid or has expired. Request a new one.` / ar `هذا الرابط غير صالح أو منتهي الصلاحية. اطلب رابطًا جديدًا.`
- Template placeholders: tokens `{Name}` matching `\{([A-Za-z]+)\}`, case-sensitive; applied to NameAr, NameEn and Code.
- Audit export: max **50,000** rows per export (400 `export_too_large`); CSV is UTF-8 **with BOM** (Arabic in Excel); XLSX via ClosedXML.
- SignalR hub path `/hubs/notifications`; client method name `notification`; WASM passes the token in `access_token` query string for that path only.
- New packages (pinned in `Directory.Packages.props` by the first task that needs them): `MailKit` (Task 6), `ClosedXML` (Task 18), `Microsoft.AspNetCore.SignalR.Client` (Task 10 tests, Task 34 client).
- FE: one API interface per module (`IClientsApi`, `IOrgApi`, `IConfigApi`, `ITemplatesApi`, `IAccessAdminApi`, `IAuditApi`, `IYearsApi`, `INotificationsApi`) with an HTTP implementation and a fake registered when `Lms:UseFakeApi=true`. Every page is checked in Arabic RTL at 360 px and desktop, and in English, with screenshots in `docs/screenshots/pr<N>/`. Every resource key exists in both `SharedResource.ar.resx` and `SharedResource.en.resx` (the parity test enforces it).

## Review Focus
1. **Unit-targeted action checked at the caller's scope instead of the target unit:** a root Org Admin creating a child under a School that has `CreateOrgUnits` set must get 403 with the policy message, and a School-1 admin passing a School-2 unit id anywhere must get 404. (Tests in Task 5, Task 9, Task 15, Task 17, Task 21; matrix in Task 35.)
2. **Privilege escalation through roles:** a caller assigning a role, or editing/creating a role, with permissions they do not hold themselves, or putting a `PlatformOnly` permission in a client role, must be refused; an Org Admin must not be able to remove `Roles.View`/`Roles.Update` from the built-in OrgAdmin role and lock everyone out. (Tests in Task 1, Task 19, Task 21.)
3. **Moving a unit:** moving the root, moving a unit under itself or a descendant must be refused; a valid move must re-path every row on every table with an `OrgUnitPath` column in one transaction, so users assigned inside the moved branch keep exactly their subtree. (Tests in Task 11, Task 18.)
4. **Sub-unit admin touching client-wide data:** an Org Admin assigned at a School editing level types, creating custom roles or editing built-in permission sets must get 403 `OutOfScope`. (Tests in Task 8, Task 19.)
5. **Account recovery abuse:** unknown email gets the same answer as a known one and sends nothing; a reset link works once; an expired link shows the expired copy; the 6th request in a minute gets 429. (Tests in Task 6.)

---

### Task 1: Contract — M1 permissions, built-in matrix, problem codes, organization DTOs — lane: Lead contract

**PRD:** §3.1, §3.4, FR-SEC-05, FR-ORG-01..04, FR-ORG-09. **Depends on:** M0 merged contracts (#18, #19, #22).

**Files:**
- Modify: `src/Lms.Shared/Authorization/Permissions.cs`, `BuiltInRoles.cs`, `src/Lms.Shared/Routes.cs`
- Create: `src/Lms.Shared/ProblemCodes.cs`, `src/Lms.Shared/Paging.cs`, `src/Lms.Shared/Organizations/OrganizationTypes.cs`, `ClientDtos.cs`, `LevelTypeDtos.cs`, `OrgUnitDtos.cs`
- Test: `tests/Lms.UnitTests/Contracts/AuthorizationContractTests.cs` (extend), `tests/Lms.UnitTests/Contracts/BuiltInRoleMatrixTests.cs`

**Interfaces:**
- Produces permissions (append; never rename): `Clients.{View,Create,Update}`; `LevelTypes.{View,Create,Update,Delete}`; `OrgUnits.Move`; `Templates.{View,Create,Delete,Apply}`; `Assignments.Update`; `AcademicYears.{View,Create,Update,Delete}`. `PlatformOnly = [Clients.View, Clients.Create, Clients.Update]`; `Permissions.All` and `PolicyCatalog.BlockedPermissions` become immutable (`FrozenSet`/`FrozenDictionary`).
- Built-in matrix (explicit lists; M0 permissions unchanged):
  - OrgAdmin: + `LevelTypes.{View,Create,Update,Delete}`, `OrgUnits.Move`, `Templates.{View,Create,Delete,Apply}`, `Assignments.Update`, `AcademicYears.{View,Create,Update,Delete}`.
  - Manager: + `LevelTypes.View`, `Templates.View`, `AcademicYears.View`, `Assignments.Create`, `Assignments.Delete` (D16: assignable roles limited by Task 21's escalation guard).
  - Supervisor: + `LevelTypes.View`, `Templates.View`, `AcademicYears.View`.
  - Counselor, Teacher, Student, Guardian: + `AcademicYears.View` (owner question Q2).
- `public static class ProblemCodes` — `const string` values equal to their names: `not_found, validation_failed, code_taken, name_taken, level_type_in_use, invalid_level_type, unit_has_children, unit_has_assignments, unit_in_use, root_unit, invalid_move, invalid_transfer_target, transfer_target_in_use, setting_locked, policy_inherited, built_in_role, role_in_use, platform_only_permission, permission_escalation, last_admin_permission, assignment_exists, own_assignment, branch_too_large, placeholder_missing, placeholder_unknown, export_too_large, year_overlap, year_closed, year_active, active_year_exists, term_overlap, term_outside_year, event_outside_year, in_app_required`.
- `sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)`.
- `OrganizationTypes { SchoolGroup, University }`, `OrganizationStatuses { Active, Suspended }`, `OrgUnitStatuses { Active, Inactive }` (string constants, like `RoleBaseKinds`).
- `ClientDto(Guid Id, string NameAr, string NameEn, string Type, string Status, Guid RootOrgUnitId, DateTime CreatedAt)`; `CreateClientRequest(string NameAr, string NameEn, string Type, string RootLevelNameAr, string RootLevelNameEn, string RootUnitCode, string AdminEmail, string AdminDisplayNameAr, string AdminDisplayNameEn)`; `UpdateClientRequest(string NameAr, string NameEn, string Status)`.
- `LevelTypeDto(Guid Id, string NameAr, string NameEn, int Order, bool CanHostClasses, int UnitCount)`; `SaveLevelTypeRequest(string NameAr, string NameEn, bool CanHostClasses)`; `ReorderLevelTypesRequest(IReadOnlyList<Guid> OrderedIds)`.
- `OrgUnitNodeDto(Guid Id, Guid? ParentId, Guid LevelTypeId, string NameAr, string NameEn, string Code, int Depth, string Status, bool IsLevelTransfer, Guid? NextTransferUnitId, bool IsCourseManagementLevel, bool HasChildren, IReadOnlyList<string> BlockedPermissions)`; `CreateOrgUnitRequest(Guid ParentId, Guid LevelTypeId, string NameAr, string NameEn, string Code)`; `UpdateOrgUnitRequest(Guid LevelTypeId, string NameAr, string NameEn, string Code)`; `SetOrgUnitStatusRequest(string Status)`; `MoveOrgUnitRequest(Guid NewParentId)`; `OrgUnitAcademicFlagsRequest(bool IsLevelTransfer, Guid? NextTransferUnitId, bool IsCourseManagementLevel)`.
- Routes: `Clients = "/api/clients"`, `LevelTypes = "/api/level-types"`, `LevelTypesOrder = "/api/level-types/order"`, `OrgUnits = "/api/org-units"`; item routes are `{base}/{id:guid}` plus suffixes `/status`, `/move`, `/academic-flags`; static helpers `Routes.OrgUnit(Guid id)` etc. for the client.

- [ ] **Step 1: Write the failing tests** in `BuiltInRoleMatrixTests`: `Built_in_roles_match_the_M1_matrix_exactly` (expected sets per role written out literally in the test, from the list above); `No_built_in_role_holds_a_platform_only_permission`; `Every_permission_is_held_by_some_built_in_role_or_is_platform_only`; `PlatformOnly_is_exactly_Clients_View_Create_Update`. In `AuthorizationContractTests`: `Permissions_All_is_immutable`; `ProblemCodes_values_equal_their_names_and_are_unique`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.UnitTests --filter "BuiltInRoleMatrixTests|AuthorizationContractTests"` → FAIL.
- [ ] **Step 3: Implement** the constants, matrix, records and routes above.
- [ ] **Step 4: Run** → PASS; `dotnet build LMS.slnx` → 0 warnings.
- [ ] **Step 5: Commit** `feat(contracts): M1 permissions, built-in matrix, problem codes and organization DTOs`.

### Task 2: Contract — settings catalog, feature flags, policies view, branch copy and templates — lane: Lead contract

**PRD:** FR-ORG-06/07/08/10/11, §4.3, §4.4, X-01. **Depends on:** Task 1.

**Files:**
- Modify: `src/Lms.Shared/Configuration/SettingKeys.cs`, `src/Lms.Shared/Routes.cs`
- Create: `src/Lms.Shared/Configuration/SettingCatalog.cs`, `FeatureFlags.cs`, `SettingDtos.cs`, `PolicyDtos.cs`, `src/Lms.Shared/Organizations/BranchDtos.cs`, `TemplateDtos.cs`
- Test: `tests/Lms.UnitTests/Contracts/ConfigurationContractTests.cs`

**Interfaces:**
- `SettingKeys` + `UiShowHijriDates = new SettingKey<bool>("Ui.ShowHijriDates", false)`.
- `FeatureFlags`: `SettingKey<bool>` fields `VirtualClasses ("Features.VirtualClasses")`, `DigitalLibrary`, `ScormContent`, `Messaging`, `GuardianPortal`, all default `false`; `FeatureFlags.All`.
- `SettingValueKinds { Bool, Int, String, Enum }`; `SettingCategories { General, Files, Features }`.
- `sealed record SettingDefinition(string Name, string Kind, string Category, string DefaultJson, IReadOnlyList<string>? AllowedValues = null, int? Min = null, int? Max = null)`; `SettingCatalog.All` = `Ui.DefaultCulture` (Enum `["ar","en"]`, General), `Ui.ShowHijriDates` (Bool, General), `Files.MaxUploadMb` (Int 1..2048, Files), the five feature flags (Bool, Features). `SettingCatalog.Find(string name)`.
- `EffectiveSettingDto(string Name, string ValueJson, bool IsLocked, bool DefinedHere, bool LockedByAncestor, Guid? SourceOrgUnitId, string? SourceNameAr, string? SourceNameEn)`; `SetSettingRequest(string ValueJson, bool IsLocked)`.
- `PolicyStates { Off, SetHere, Inherited }`; `PolicyDefinitionDto(string Key, IReadOnlyList<string> BlockedPermissions)`; `EffectivePolicyDto(string Key, string State, Guid? SourceOrgUnitId, string? SourceNameAr, string? SourceNameEn)`.
- `CopyBranchRequest(Guid SourceOrgUnitId, Guid TargetParentId, string CodeSuffix, bool IncludePolicies, bool IncludeSettings, string? NewRootNameAr, string? NewRootNameEn)`; `CopyBranchResultDto(Guid NewRootId, int UnitsCreated, IReadOnlyList<string> SkippedLockedSettings)`.
- `TemplateVisibilities { Private, Shared }`; `TemplateNodeDto(int Key, int? ParentKey, Guid LevelTypeId, string NameAr, string NameEn, string Code, bool IsLevelTransfer, int? NextTransferKey, bool IsCourseManagementLevel, IReadOnlyList<string> PolicyKeys, int SettingCount)`; `OrgTemplateSummaryDto(Guid Id, string NameAr, string NameEn, string Visibility, Guid OwnerOrgUnitId, string OwnerNameAr, string OwnerNameEn, int UnitCount, IReadOnlyList<string> Placeholders, DateTime CreatedAt)`; `OrgTemplateDetailDto(OrgTemplateSummaryDto Summary, IReadOnlyList<TemplateNodeDto> Nodes)`; `SaveTemplateRequest(Guid SourceOrgUnitId, string NameAr, string NameEn, string Visibility, bool IncludePolicies, bool IncludeSettings, IReadOnlyDictionary<string, string> Parameterize)` (literal text → placeholder, e.g. `{"2026": "{Year}"}`); `ApplyTemplateRequest(Guid TargetParentId, IReadOnlyDictionary<string, string> Placeholders)`; `ApplyTemplateResultDto(Guid NewRootId, int UnitsCreated, IReadOnlyList<string> SkippedLockedSettings)`.
- Routes: `PoliciesCatalog = "/api/policies/catalog"`, `SettingsCatalog = "/api/settings/catalog"`; unit sub-routes `/api/org-units/{id}/settings[/{name}]`, `/api/org-units/{id}/policies[/{key}]`; `OrgUnitsCopy = "/api/org-units/copy"`; `Templates = "/api/templates"` + `/{id}` + `/{id}/apply`.

- [ ] **Step 1: Tests** `Every_SettingKeys_and_FeatureFlags_field_is_in_the_catalog_with_matching_default_json`; `Catalog_names_are_unique`; `Enum_definitions_list_their_default_among_allowed_values`; `Every_policy_key_has_at_least_one_blocked_permission`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(contracts): settings catalog, feature flags, policy views, branch copy and templates`.

### Task 3: Contract — roles admin, assignments, audit — lane: Lead contract

**PRD:** FR-SEC-06, FR-AUD-01/03/04, X-02, §3.2. **Depends on:** Task 1.

**Files:**
- Modify: `src/Lms.Shared/Routes.cs`
- Create: `src/Lms.Shared/Access/RoleDtos.cs`, `AssignmentDtos.cs`, `src/Lms.Shared/Auditing/AuditCatalog.cs`, `AuditDtos.cs`
- Test: `tests/Lms.UnitTests/Contracts/AccessContractTests.cs`

**Interfaces:**
- `PermissionGroupDto(string Module, IReadOnlyList<string> Permissions)` (all non-PlatformOnly permissions grouped by the part before `.`).
- `RoleDto(Guid Id, string? BuiltInKey, string NameAr, string NameEn, string BaseKind, bool IsBuiltIn, IReadOnlyList<string> Permissions, int AssignmentCount)`; `CreateRoleRequest(string NameAr, string NameEn, string BaseKind, IReadOnlyList<string> Permissions)`; `UpdateRoleRequest(string NameAr, string NameEn, IReadOnlyList<string> Permissions)`.
- `AssignmentDto(Guid Id, Guid UserId, string UserEmail, string UserDisplayNameAr, string UserDisplayNameEn, Guid RoleId, string RoleNameAr, string RoleNameEn, Guid OrgUnitId, string OrgUnitNameAr, string OrgUnitNameEn, DateOnly? ValidFrom, DateOnly? ValidTo, bool IsCurrentlyValid)`; `CreateAssignmentRequest(string Email, string? DisplayNameAr, string? DisplayNameEn, Guid RoleId, Guid OrgUnitId, DateOnly? ValidFrom, DateOnly? ValidTo)`; `UpdateAssignmentRequest(DateOnly? ValidFrom, DateOnly? ValidTo)`.
- `AuditCatalog.EntityTypes` (`Organization, LevelType, OrgUnit, Role, RolePermission, RoleAssignment, OrgUnitSetting, OrgUnitPolicy, User, OrgTemplate, AcademicYear, Term, CalendarEvent`) and `AuditCatalog.SecurityEvents` (`LoginSucceeded, LoginFailed, PasswordResetRequested, PasswordReset, InvitationAccepted`); `AuditActions { Created, Updated, Deleted, SecurityEvent }`.
- `AuditEntryDto(Guid Id, DateTime TimestampUtc, Guid? UserId, string? UserEmail, string? UserDisplayNameAr, string? UserDisplayNameEn, string? RoleNameAr, string? RoleNameEn, string? OrgUnitNameAr, string? OrgUnitNameEn, string Action, string EntityType, Guid EntityId, string? IpAddress)`; `AuditEntryDetailDto(AuditEntryDto Entry, string? BeforeJson, string? AfterJson)`; `AuditQuery(Guid? OrganizationId, Guid? UserId, string? EntityType, Guid? EntityId, string? Action, DateTime? FromUtc, DateTime? ToUtc, int Page = 1, int PageSize = 50)` bound from the query string; `AuditExportFormats { Csv, Xlsx }`.
- Routes: `Permissions = "/api/permissions"`, `Roles = "/api/roles"` + `/{id}`, `Assignments = "/api/assignments"` + `/{id}`, unit list `/api/org-units/{id}/assignments`, `Audit = "/api/audit"` + `/{id}` + `/export`.

- [ ] **Step 1: Tests** `Audit_catalog_entity_types_are_unique`; `Security_events_include_the_M0_login_events`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(contracts): roles admin, assignments and audit viewer DTOs`.

### Task 4: Contract — academic years, terms, calendar, notifications — lane: Lead contract

**PRD:** FR-YEAR-01..03, X-04, FR-NOT-01/02, X-07. **Depends on:** Task 1.

**Files:**
- Modify: `src/Lms.Shared/Routes.cs`
- Create: `src/Lms.Shared/AcademicYears/AcademicYearDtos.cs`, `CalendarDtos.cs`, `src/Lms.Shared/Notifications/NotificationEvents.cs`, `NotificationDtos.cs`, `NotificationHub.cs`
- Test: `tests/Lms.UnitTests/Contracts/YearsAndNotificationsContractTests.cs`

**Interfaces:**
- `AcademicYearStatuses { Planned, Active, Closed }`; `TermDto(Guid Id, string NameAr, string NameEn, int Order, DateOnly StartDate, DateOnly EndDate, bool IsCurrent)`; `AcademicYearDto(Guid Id, Guid OrgUnitId, string OrgUnitNameAr, string OrgUnitNameEn, string NameAr, string NameEn, DateOnly StartDate, DateOnly EndDate, string Status, bool IsInherited, IReadOnlyList<TermDto> Terms)`; `YearContextDto(IReadOnlyList<AcademicYearDto> Years, Guid? CurrentYearId, Guid DefiningOrgUnitId)`; `SaveAcademicYearRequest(Guid OrgUnitId, string NameAr, string NameEn, DateOnly StartDate, DateOnly EndDate)` (OrgUnitId ignored on update); `SaveTermRequest(string NameAr, string NameEn, DateOnly StartDate, DateOnly EndDate)`.
- `CalendarEventKinds { Holiday, Event }`; `CalendarEventDto(Guid Id, Guid AcademicYearId, Guid OrgUnitId, string OrgUnitNameAr, string OrgUnitNameEn, string TitleAr, string TitleEn, string Kind, DateOnly StartDate, DateOnly EndDate, bool IsInherited)`; `SaveCalendarEventRequest(Guid AcademicYearId, Guid OrgUnitId, string TitleAr, string TitleEn, string Kind, DateOnly StartDate, DateOnly EndDate)`.
- `NotificationChannels { InApp, Email, Sms, Push }`; `sealed record NotificationEventDefinition(string Key, IReadOnlyList<string> DefaultRecipientRoles, IReadOnlyList<string> Parameters)`; `NotificationEvents`: `AccessRoleAssigned = "Access.RoleAssigned"` and `AccessRoleRemoved = "Access.RoleRemoved"` (recipient: the assignee; parameters `RoleNameAr, RoleNameEn, OrgUnitNameAr, OrgUnitNameEn`), `OrganizationPolicyChanged = "Organization.PolicyChanged"` (default roles `[OrgAdmin, Manager]`; parameters `PolicyKey, OrgUnitNameAr, OrgUnitNameEn, State`); `NotificationEvents.All`, `Find(string key)`.
- `NotificationDto(Guid Id, string EventKey, IReadOnlyDictionary<string, string> Parameters, string? DeepLink, DateTime CreatedAt, bool IsRead)`; `NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Total, int UnreadCount)`; `NotificationPreferenceDto(string EventKey, string Channel, bool Enabled, bool Locked)`; `SetNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceDto> Preferences)`.
- `NotificationHub { Path = "/hubs/notifications", ReceiveMethod = "notification" }`.
- Routes: `AcademicYears = "/api/academic-years"` + `/{id}`, `/{id}/activate`, `/{id}/terms`, `/{id}/calendar`, `AcademicYearsEffective = "/api/academic-years/effective"`, `Terms = "/api/terms"` + `/{id}`, `CalendarEvents = "/api/calendar-events"` + `/{id}`, `MyNotifications = "/api/me/notifications"` + `/{id}/read`, `/read-all`, `MyNotificationPreferences = "/api/me/notification-preferences"`.

- [ ] **Step 1: Tests** `Notification_event_keys_are_unique_and_Module_Event_shaped`; `Default_recipient_roles_are_built_in_keys`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(contracts): academic years, calendar and notification DTOs`.

### Task 5: Unit access helper, org tree service, problem helpers — lane: BE1

**PRD:** FR-SEC-05, §3.3, §4.2. **Depends on:** Task 1, M0 #27 (access evaluator), #26.

**Files:**
- Create: `src/Lms.Domain/Organizations/IOrgTree.cs`, `src/Lms.Infrastructure/Organizations/OrgTree.cs`, `OrganizationsServiceCollectionExtensions.cs`
- Create: `src/Lms.Api/Infrastructure/UnitAccess.cs`, `AccessProblem.cs`, `ApiProblems.cs`
- Modify: `src/Lms.Api/Infrastructure/PermissionEndpointFilter.cs` (use `AccessProblem`)
- Test: `tests/Lms.IntegrationTests/Access/UnitAccessTests.cs`, `tests/Lms.IntegrationTests/Infrastructure/TestEndpoints.cs` (test-only routes `/test/units/{id}/create-child`, `/test/client-root/level-types`)

**Interfaces:**
- `IOrgTree.FindVisibleAsync(Guid orgUnitId, CancellationToken) : Task<OrgUnit?>` (normal filters); `IOrgTree.GetRootAsync(Guid organizationId, CancellationToken) : Task<OrgUnit>` and `IOrgTree.GetAncestorsAndSelfAsync(string path, Guid organizationId, CancellationToken) : Task<IReadOnlyList<OrgUnit>>` (both ISOLATION-BYPASS: ancestors are above the caller's scope; explicit org filter; never returns deleted units).
- `sealed record UnitAccessResult(OrgUnit? Unit, IResult? Problem)`; `static Task<UnitAccessResult> CheckUnitAsync(this HttpContext http, string permission, Guid orgUnitId, CancellationToken ct)` — unit not visible → `ApiProblems.NotFound()`; else `IAccessEvaluator.EvaluateAsync(permission, unit.Path)`; deny → `AccessProblem.From(decision, http)`. `static Task<IResult?> CheckClientRootAsync(this HttpContext http, string permission, CancellationToken ct)` — evaluates at `GetRootAsync(context.OrganizationId)`.Path; platform admin with no org → 403 `MissingPermission` (platform admin must act through a client endpoint that takes an org id).
- `AccessProblem.From(AccessDecision decision, HttpContext http) : IResult` — the M0 401/403 body (reason, blockingOrgUnitId, localized policy detail) moved out of `PermissionEndpointFilter` unchanged.
- `ApiProblems.NotFound()`, `ApiProblems.Conflict(string code, string? detail = null, object? extra = null)`, `ApiProblems.BadRequest(string code, string? detail = null)` — all set extension `code`.

- [ ] **Step 1: Write the failing tests** `Unit_inside_scope_is_allowed`; `Unit_in_sibling_subtree_returns_404_not_403`; `Unit_of_other_client_returns_404`; `Policy_on_target_unit_blocks_root_admin` (policy `CreateOrgUnits` at School1, root OrgAdmin → 403, `reason == "BlockedByPolicy"`, `blockingOrgUnitId == School1`); `Client_root_check_denies_admin_assigned_below_root_with_OutOfScope`; `Client_root_check_allows_root_admin`; `Permission_filter_body_is_unchanged_after_refactor` (M0 `PermissionFilterTests` still green).
- [ ] **Step 2: Run** `dotnet test tests/Lms.IntegrationTests --filter UnitAccessTests` → FAIL.
- [ ] **Step 3: Implement** the interfaces above.
- [ ] **Step 4: Run** → PASS; full integration suite green.
- [ ] **Step 5: Commit** `feat(access): unit-targeted access checks and org tree service`.

### Task 6: Forgot/reset password, invitations, blocked-login messages — lane: BE2

**PRD:** FR-SEC-02, FR-SEC-07, X-02, X-08. **Depends on:** M0 #31 (identity). DEV-PLAN item 10.

**Files:**
- Create: `src/Lms.Domain/Abstractions/IEmailSender.cs`, `src/Lms.Infrastructure/Email/SmtpEmailSender.cs`, `EmailOptions.cs`, `src/Lms.Infrastructure/Identity/AccountEmails.cs`, `InvitationTokenProvider.cs`
- Create: `src/Lms.Api/Pages/Account/ForgotPassword.cshtml(.cs)`, `ResetPassword.cshtml(.cs)`, `src/Lms.Api/Features/Account/AccountModule.cs` (rate limiter policy `auth`)
- Modify: `src/Lms.Domain/Identity/UserStatus.cs` (append `Expired, Withdrawn`), `src/Lms.Infrastructure/Identity/IdentityServiceCollectionExtensions.cs` (`AddDefaultTokenProviders`, lifespans), `src/Lms.Api/Pages/Account/Login.cshtml(.cs)` (status messages, "Forgot password?" link, rate limit), `src/Lms.Api/Resources/AccountResource{,.ar,.en}.resx`, `appsettings.json` / `appsettings.Development.json` (`Email:SmtpHost=localhost`, `Email:SmtpPort=1025`, `Email:From`, `App:PublicBaseUrl`)
- Test: `tests/Lms.IntegrationTests/Auth/PasswordResetTests.cs`, `BlockedLoginTests.cs`, `tests/Lms.IntegrationTests/Infrastructure/FakeEmailSender.cs`

**Interfaces:**
- `sealed record EmailMessage(string To, string Subject, string HtmlBody)`; `IEmailSender.SendAsync(EmailMessage message, CancellationToken)` (MailKit SMTP; M6 replaces with the queued channel).
- `IAccountEmails.SendPasswordResetAsync(User user, string culture, CancellationToken)`; `IAccountEmails.SendInvitationAsync(User user, string culture, CancellationToken)` — links `{App:PublicBaseUrl}/Account/ResetPassword?userId={id}&token={urlEncoded}` (`&invite=1` for invitations); email subject/body from `AccountResource` in the given culture.
- Security events via `IAuditWriter`: `PasswordResetRequested` (known email only), `PasswordReset`, `InvitationAccepted`.
- Resource keys (AccountResource, en / ar): `ForgotPassword` Forgot password? / نسيت كلمة المرور؟; `ForgotHeading` Reset your password / إعادة تعيين كلمة المرور; `SendLink` Send reset link / إرسال رابط إعادة التعيين; `ResetSent` (Global Constraints copy); `NewPassword` New password / كلمة المرور الجديدة; `ConfirmPassword` Confirm password / تأكيد كلمة المرور; `PasswordsDiffer` Passwords do not match. / كلمتا المرور غير متطابقتين.; `SetPassword` Set password / تعيين كلمة المرور; `InviteHeading` Welcome — choose your password / مرحبًا — اختر كلمة المرور; `LinkInvalid` (Global Constraints copy); `PasswordChanged` Your password was changed. You can sign in now. / تم تغيير كلمة المرور. يمكنك تسجيل الدخول الآن.; `AccountExpired`, `AccountWithdrawn` (Global Constraints copy); `TooManyRequests` Too many attempts. Try again in a minute. / محاولات كثيرة. حاول مرة أخرى بعد دقيقة.; `ResetEmailSubject` Reset your LMS password / إعادة تعيين كلمة مرور المنصة; `InviteEmailSubject` Your LMS account is ready / حسابك على المنصة جاهز.

- [ ] **Step 1: Write the failing tests** `Forgot_password_for_known_email_sends_one_reset_mail`; `Forgot_password_for_unknown_email_shows_same_confirmation_and_sends_nothing`; `Reset_with_valid_token_changes_password_and_link_cannot_be_reused`; `Reset_with_expired_token_shows_LinkInvalid` (test host sets reset lifespan to 1 s); `Reset_clears_lockout`; `Invitation_link_sets_first_password_and_user_can_log_in`; `Reset_writes_PasswordReset_security_event`; `Expired_user_sees_AccountExpired_message`; `Withdrawn_user_sees_AccountWithdrawn_message`; `Sixth_forgot_password_post_in_a_minute_gets_429`; `Reset_page_renders_rtl_in_arabic`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.IntegrationTests --filter "PasswordResetTests|BlockedLoginTests"` → FAIL.
- [ ] **Step 3: Implement** (token lifespans per Global Constraints; reset lifespan bound from `Identity:ResetTokenLifespanMinutes`, default 60).
- [ ] **Step 4: Run** → PASS; check pages manually in ar and en at 360 px; screenshots in PR.
- [ ] **Step 5: Commit** `feat(auth): forgot/reset password, invitations and blocked-login messages`.

### Task 7: Clients (Platform Admin) + first Org Admin — lane: BE1

**PRD:** FR-ORG-01, §3.1, §4.1, D1, D2. **Depends on:** Tasks 1, 5, 6; M0 #26 (provisioner). DEV-PLAN item 1 (BE).

**Files:**
- Create: `src/Lms.Api/Features/Clients/ClientsModule.cs`, `ClientEndpoints.cs`, `CreateClientHandler.cs`, `ClientValidators.cs`
- Modify: `src/Lms.Domain/Organizations/Organization.cs` (+ `OrganizationStatus Status`, `Rename`, `SetStatus`), create `OrganizationStatus.cs`; `src/Lms.Api/Infrastructure/CurrentContextMiddleware.cs` (suspended client → context invalid); migration `AddOrganizationStatus`
- Test: `tests/Lms.IntegrationTests/Clients/ClientEndpointsTests.cs`

**Interfaces:**
- `GET /api/clients?page&pageSize&search` → `PagedResult<ClientDto>` — `Clients.View`. `GET /api/clients/{id}` → `ClientDto` — `Clients.View`. `POST /api/clients` → 201 `ClientDto` — `Clients.Create`. `PUT /api/clients/{id}` → `ClientDto` — `Clients.Update`.
- `CreateClientHandler.HandleAsync(CreateClientRequest request, CancellationToken) : Task<ClientDto>` — one transaction: `Organization` → root `LevelType` (Order 1, `CanHostClasses=false`) → `OrgUnit.CreateRoot(org, levelType, NameAr, NameEn, RootUnitCode)` → `IDefaultRoleProvisioner.ProvisionAsync` → find user by email or create (`UserManager`, no password, names from request) → `RoleAssignment` OrgAdmin at root. After commit, `IAccountEmails.SendInvitationAsync` only for a newly created user (culture `ar` for SchoolGroup and University alike — `Ui.DefaultCulture` default).

- [ ] **Step 1: Write the failing tests** `Platform_admin_creates_school_group_with_root_unit_roles_and_org_admin`; `University_client_gets_university_role_labels` (Manager → `Dean`); `New_admin_receives_invitation_email`; `Existing_user_email_is_reused_and_gets_no_invitation`; `Org_admin_cannot_list_or_create_clients_403`; `Client_list_is_paged_and_searchable_in_arabic` (`"مدارس"`); `Invalid_type_or_code_returns_validation_problem`; `Suspended_client_contexts_are_rejected_403`; `Created_client_is_invisible_from_another_clients_context` (isolation).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(clients): platform admin creates clients with first org admin`.

### Task 8: Level types CRUD and ordering — lane: BE2

**PRD:** FR-ORG-02, §4.1. **Depends on:** Tasks 1, 5. DEV-PLAN item 2 (BE).

**Files:**
- Create: `src/Lms.Api/Features/LevelTypes/LevelTypesModule.cs`, `LevelTypeEndpoints.cs`, `LevelTypeValidators.cs`
- Modify: `src/Lms.Domain/Organizations/LevelType.cs` (`Update(nameAr, nameEn, canHostClasses)`, `MoveTo(int order)`)
- Test: `tests/Lms.IntegrationTests/LevelTypes/LevelTypeEndpointsTests.cs`

**Interfaces:**
- `GET /api/level-types` → `IReadOnlyList<LevelTypeDto>` ordered by `Order` — `LevelTypes.View` (any scope in the client).
- `POST /api/level-types` (Order = max + 1) — `LevelTypes.Create` + `CheckClientRootAsync`. `PUT /api/level-types/{id}` — `LevelTypes.Update` + root. `PUT /api/level-types/order` — `LevelTypes.Update` + root; `OrderedIds` must be exactly the client's live ids (else 400 `validation_failed`). `DELETE /api/level-types/{id}` — `LevelTypes.Delete` + root; 409 `level_type_in_use` when a live unit uses it; soft delete.
- Names unique per client per language → 409 `name_taken`.

- [ ] **Step 1: Write the failing tests** `Root_admin_creates_updates_reorders_and_deletes_level_types`; `School_admin_cannot_edit_level_types_OutOfScope` (Review Focus 4); `Manager_can_view_but_not_create_403`; `Delete_used_level_type_returns_409_level_type_in_use`; `Reorder_with_missing_id_returns_400`; `Client_B_level_types_are_invisible_and_unknown_id_is_404`; `Level_type_change_is_audited`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(level-types): level types CRUD and ordering`.

### Task 9: OrgUnit CRUD, tree and search, deactivate, delete rules — lane: BE1

**PRD:** FR-ORG-03, FR-ORG-04, FR-ORG-10 (UI hiding data), D15, X-03. **Depends on:** Tasks 1, 5. DEV-PLAN item 3 (BE, part 1 of 2 — split: move is Task 11).

**Files:**
- Create: `src/Lms.Api/Features/OrgUnits/OrgUnitsModule.cs`, `OrgUnitEndpoints.cs`, `OrgUnitQueries.cs`, `OrgUnitValidators.cs`, `DeleteOrgUnitHandler.cs`
- Create: `src/Lms.Domain/Organizations/IOrgUnitUsageCheck.cs`, `src/Lms.Infrastructure/Organizations/AssignmentUsageCheck.cs`
- Modify: `src/Lms.Domain/Organizations/OrgUnit.cs` (`Update(levelTypeId, nameAr, nameEn, code)`, `Deactivate()`, `Activate()`)
- Test: `tests/Lms.IntegrationTests/OrgUnits/OrgUnitEndpointsTests.cs`, `OrgUnitDeleteRulesTests.cs`

**Interfaces:**
- `GET /api/org-units?search&includeInactive=true` → `IReadOnlyList<OrgUnitNodeDto>`: all visible units (scope filter); with `search`, units whose NameAr/NameEn/Code contain it (case-insensitive) **plus their visible ancestors**; `BlockedPermissions` = permissions blocked at that unit by its own or ancestor policies (one policy query per request). — `OrgUnits.View`.
- `GET /api/org-units/{id}` — `OrgUnits.View` + `CheckUnitAsync`. `POST /api/org-units` — `OrgUnits.Create` + `CheckUnitAsync(parentId)`; child level type `Order` must be greater than the parent's (400 `invalid_level_type`). `PUT /api/org-units/{id}` — `OrgUnits.Update` + unit. `PUT /api/org-units/{id}/status` — `OrgUnits.Update` + unit; root cannot be deactivated (409 `root_unit`). `DELETE /api/org-units/{id}` — `OrgUnits.Delete` + unit.
- Delete rules (`DeleteOrgUnitHandler`): root → 409 `root_unit`; live children → 409 `unit_has_children`; live role assignments at the unit → 409 `unit_has_assignments`; any `IOrgUnitUsageCheck` returning a reason → 409 `unit_in_use` with `detail` = reason key. Otherwise soft delete.
- `interface IOrgUnitUsageCheck { Task<string?> FindUsageAsync(OrgUnit unit, CancellationToken ct); }` — M2/M3 register checks for students, classes, courses (FR-ORG-04).

- [ ] **Step 1: Write the failing tests** `Admin_creates_child_and_tree_lists_it_with_depth_and_path`; `Create_under_unit_with_CreateOrgUnits_policy_is_blocked_with_policy_message` (Review Focus 1); `School1_admin_creating_under_School2_gets_404` (Review Focus 1); `Duplicate_code_in_client_returns_409_code_taken_but_same_code_in_other_client_is_ok`; `Child_level_type_must_be_deeper_than_parent`; `Search_by_arabic_name_returns_match_and_ancestors`; `Node_carries_blocked_permissions_from_ancestor_policy`; `Deactivated_unit_hides_contexts_below_and_admin_above_can_reactivate` (D15); `Delete_with_children_409`; `Delete_with_assignments_409`; `Registered_usage_check_blocks_delete_unit_in_use`; `Delete_leaf_soft_deletes_and_writes_audit`; `Root_cannot_be_deleted_or_deactivated`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(org-units): org unit CRUD, tree search and delete rules`.

### Task 10: Notification core — catalog, in-app store, SignalR, recipient resolver — lane: BE2

**PRD:** FR-NOT-01 (catalog, default recipients), FR-NOT-02, X-07. **Depends on:** Task 4; M0 #31 (token validation). DEV-PLAN item 13 (BE, part 1 of 2 — split: preferences are Task 16).

**Files:**
- Create: `src/Lms.Domain/Notifications/Notification.cs`, `INotificationPublisher.cs`, `NotificationRequest.cs`
- Create: `src/Lms.Infrastructure/Notifications/NotificationPublisher.cs`, `NotificationRecipientResolver.cs`, `NotificationsServiceCollectionExtensions.cs`, `Persistence/Configurations/Notifications/NotificationConfiguration.cs`, migration `AddNotifications`
- Create: `src/Lms.Api/Features/Notifications/NotificationsModule.cs`, `MyNotificationEndpoints.cs`, `NotificationsHub.cs`, `SubUserIdProvider.cs`
- Modify: `tests/Lms.IntegrationTests/Access/EndpointCoverageTests.cs` (exempt `/api/me/notifications*` — "self-scoped: caller's own rows")
- Test: `tests/Lms.IntegrationTests/Notifications/NotificationTests.cs`, `NotificationIsolationTests.cs`

**Interfaces:**
- `Notification : Entity, IOptionalTenantOwned { Guid UserId, string EventKey, string ParametersJson, string? DeepLink, DateTime CreatedAt, DateTime? ReadAt }` (history row, not soft-deleted; index `(UserId, ReadAt, CreatedAt)`).
- `sealed record NotificationRequest(string EventKey, Guid? OrganizationId, IReadOnlyList<Guid> UserIds, IReadOnlyDictionary<string, string> Parameters, string? DeepLink = null, string? OrgUnitPath = null)`; `INotificationPublisher.PublishAsync(NotificationRequest request, CancellationToken)` — unknown key or missing parameter → `ArgumentException`; empty `UserIds` + `OrgUnitPath` → resolver; never notifies the acting user; stores rows then pushes `NotificationDto` to `Clients.User(userId)` on `NotificationHub.ReceiveMethod`.
- `INotificationRecipientResolver.ResolveAsync(string eventKey, Guid organizationId, string orgUnitPath, CancellationToken) : Task<IReadOnlyList<Guid>>` — users with a currently valid assignment of a role whose `BuiltInKey` is in `DefaultRecipientRoles`, at the unit or any descendant (ISOLATION-BYPASS: event raiser may be above or beside recipients; explicit org filter).
- `GET /api/me/notifications?unreadOnly&page&pageSize` → `NotificationPageDto`; `POST /api/me/notifications/{id}/read` (404 when not the caller's); `POST /api/me/notifications/read-all`. Rows filtered by `UserId == context.UserId` and the active client.
- Hub `[Authorize]` at `/hubs/notifications`; `IUserIdProvider` returns `sub`; OpenIddict validation reads `access_token` from the query for that path only.

- [ ] **Step 1: Write the failing tests** `Publish_stores_row_and_pushes_dto_over_signalr` (`HubConnection` over `factory.Server.CreateHandler()`); `User_sees_only_own_notifications`; `Notifications_of_client_B_are_invisible_in_client_A_context` (isolation); `Mark_read_of_other_users_notification_returns_404`; `Unread_count_and_read_all`; `Role_based_event_reaches_managers_at_and_below_unit_but_not_actor_or_sibling`; `Unknown_event_or_missing_parameter_throws`; `Hub_rejects_anonymous_connection`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(notifications): notification catalog, in-app center API and SignalR push`.

### Task 11: Move OrgUnit and re-path every unit-scoped table — lane: BE1

**PRD:** FR-ORG-03 (move, re-compute paths, admin only), §4.2. **Depends on:** Task 9. DEV-PLAN item 3 (BE, part 2 of 2).

**Files:**
- Create: `src/Lms.Api/Features/OrgUnits/MoveOrgUnitHandler.cs`; `src/Lms.Domain/Organizations/IOrgUnitRepather.cs`; `src/Lms.Infrastructure/Organizations/OrgUnitRepather.cs`
- Modify: `src/Lms.Api/Features/OrgUnits/OrgUnitEndpoints.cs` (one route), `src/Lms.Domain/Organizations/OrgUnit.cs` (`MoveUnder(OrgUnit newParent)` sets ParentId/Path/Depth)
- Test: `tests/Lms.IntegrationTests/OrgUnits/MoveOrgUnitTests.cs`, `tests/Lms.IntegrationTests/OrgUnits/RepatherCoverageTests.cs`

**Interfaces:**
- `PUT /api/org-units/{id}/move` body `MoveOrgUnitRequest` — `OrgUnits.Move`, plus `CheckUnitAsync(OrgUnits.Move, id)` **and** `CheckUnitAsync(OrgUnits.Move, newParentId)`; new parent's level type order must stay below the unit's (400 `invalid_level_type`). Root, self, or a descendant as new parent → 409 `invalid_move`.
- `IOrgUnitRepather.RepathAsync(Guid organizationId, string oldPrefix, string newPrefix, int depthDelta, CancellationToken)` — inside the caller's transaction: updates `OrgUnits.Path`/`Depth` and **every mapped entity type with a string property named `OrgUnitPath`** (discovered from `db.Model`, so future tables are covered automatically) with one `UPDATE … SET col = CONCAT(@new, SUBSTRING(col, @oldLen + 1)) WHERE OrganizationId = @org AND col LIKE CONCAT(@old, '%')` per table (ISOLATION-BYPASS: raw SQL, explicit org filter). The moved unit itself is updated through EF so the move is audited (ParentId before/after).
- Handler runs `db.Database.BeginTransactionAsync()` → domain move → `SaveChanges` → repath → commit.

- [ ] **Step 1: Write the failing tests** `Move_repaths_units_assignments_settings_policies_in_one_transaction`; `Depths_of_moved_subtree_are_updated`; `User_assigned_inside_moved_branch_keeps_exactly_the_new_subtree`; `Move_root_or_under_own_descendant_returns_409_invalid_move` (Review Focus 3); `Move_needs_permission_at_source_and_target_404_for_other_subtree`; `Move_writes_audit_entry_with_old_and_new_parent`; `Repather_covers_every_entity_with_OrgUnitPath_in_the_model` (asserts the discovered set ⊇ every `IOrgUnitScoped` CLR type in the model).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(org-units): move units and re-path unit-scoped tables`.

### Task 12: Policies control panel API — lane: BE2

**PRD:** FR-ORG-10, §4.3. **Depends on:** Tasks 2, 5, 10; M0 #25. DEV-PLAN item 5 (BE).

**Files:**
- Create: `src/Lms.Api/Features/Policies/PoliciesModule.cs`, `PolicyEndpoints.cs`, `src/Lms.Infrastructure/Configuration/EffectivePolicyReader.cs`
- Test: `tests/Lms.IntegrationTests/Policies/PolicyEndpointsTests.cs`

**Interfaces:**
- `GET /api/policies/catalog` → `IReadOnlyList<PolicyDefinitionDto>` from `PolicyCatalog` — `Policies.View`.
- `GET /api/org-units/{id}/policies` → one `EffectivePolicyDto` per catalog key — `Policies.View` + unit. State: `SetHere` if a live row at this unit, else `Inherited` (highest ancestor holding it, with its names), else `Off`.
- `PUT /api/org-units/{id}/policies/{key}` (enable, idempotent) and `DELETE /api/org-units/{id}/policies/{key}` (disable) — `Policies.Update` + unit; unknown key → 404; disable when `Inherited` → 409 `policy_inherited` ("children can add, never remove"); disable when `Off` → 404. Uses `IPolicyManager`. On change publishes `Organization.PolicyChanged` with `OrgUnitPath` = unit path.
- `IEffectivePolicyReader.ReadAsync(OrgUnit unit, CancellationToken) : Task<IReadOnlyList<EffectivePolicyDto>>`.

- [ ] **Step 1: Write the failing tests** `Effective_view_shows_inherited_policy_with_source_unit`; `Enable_at_child_then_disable_works`; `Disable_inherited_policy_returns_409_policy_inherited`; `Manager_can_view_but_not_update_403`; `Policy_change_notifies_managers_below`; `Other_subtree_unit_returns_404`; `Policy_toggle_is_audited`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(policies): policy control panel and effective rules API`.

### Task 13: Level Transfer flag, transfer mapping, course-management level — lane: BE1

**PRD:** FR-ORG-05, FR-ORG-09. **Depends on:** Task 9. DEV-PLAN item 4 (BE).

**Files:**
- Create: `src/Lms.Api/Features/OrgUnits/AcademicFlagsHandler.cs`
- Modify: `src/Lms.Api/Features/OrgUnits/OrgUnitEndpoints.cs` (one route), `src/Lms.Domain/Organizations/OrgUnit.cs` (`SetAcademicFlags(bool isLevelTransfer, Guid? nextTransferUnitId, bool isCourseManagementLevel)`; replaces `MarkAsLevelTransfer`/`MarkAsCourseManagementLevel`, callers updated)
- Test: `tests/Lms.IntegrationTests/OrgUnits/AcademicFlagsTests.cs`

**Interfaces:**
- `PUT /api/org-units/{id}/academic-flags` body `OrgUnitAcademicFlagsRequest` — `OrgUnits.Update` + unit. Rules: `NextTransferUnitId` requires `IsLevelTransfer`; target must be a live unit of the same client with `IsLevelTransfer`, the **same LevelTypeId**, not itself, and following the chain must not loop (400 `invalid_transfer_target`); target may be outside the caller's scope only if visible (else 404). Clearing `IsLevelTransfer` on a unit other units point to → 409 `transfer_target_in_use`.

- [ ] **Step 1: Write the failing tests** `KG_maps_to_Primary_as_next_transfer_level`; `Next_without_flag_is_400`; `Target_of_other_level_type_is_400`; `Cycle_KG_Primary_KG_is_400`; `Clearing_flag_on_referenced_target_is_409`; `Course_management_flag_round_trips_and_is_audited`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(org-units): level transfer mapping and course-management level`.

### Task 14: Settings catalog API with locks and feature flags — lane: BE2

**PRD:** FR-ORG-11, §4.3, §4.4, D5, X-01 (Hijri display setting). **Depends on:** Tasks 2, 5; M0 #25. DEV-PLAN item 6 (BE).

**Files:**
- Create: `src/Lms.Api/Features/Settings/SettingsModule.cs`, `SettingEndpoints.cs`, `SettingValueValidator.cs`, `src/Lms.Infrastructure/Configuration/EffectiveSettingsReader.cs`
- Modify: `src/Lms.Infrastructure/Configuration/ISettingsResolver.cs`, `SettingsResolver.cs` (+ untyped `SetJsonAsync`, `RemoveAsync`)
- Test: `tests/Lms.IntegrationTests/Settings/SettingEndpointsTests.cs`, `tests/Lms.UnitTests/Configuration/SettingValueValidatorTests.cs`

**Interfaces:**
- `ISettingsResolver.SetJsonAsync(string name, Guid orgUnitId, string valueJson, bool isLocked, CancellationToken)` (throws `SettingLockedException`); `ISettingsResolver.RemoveAsync(string name, Guid orgUnitId, CancellationToken)` (soft delete own row; false when none).
- `IEffectiveSettingsReader.ReadAsync(OrgUnit unit, CancellationToken) : Task<IReadOnlyList<EffectiveSettingDto>>` — one row per `SettingCatalog.All` entry; source names from `IOrgTree.GetAncestorsAndSelfAsync`.
- `SettingValueValidator.Validate(SettingDefinition definition, string valueJson) : string?` (error message key or null): Bool/Int/String/Enum JSON kind; Int within Min/Max; Enum in AllowedValues.
- `GET /api/settings/catalog` → `IReadOnlyList<SettingDefinition>` — `Settings.View`. `GET /api/org-units/{id}/settings` — `Settings.View` + unit. `PUT /api/org-units/{id}/settings/{name}` body `SetSettingRequest` — `Settings.Update` + unit; unknown name 404; invalid value 400 `validation_failed`; locked by a strict ancestor → 409 `setting_locked` with `lockingOrgUnitId`. `DELETE /api/org-units/{id}/settings/{name}` — `Settings.Update` + unit; 404 when not defined here.

- [ ] **Step 1: Write the failing tests** (unit) `Int_outside_range_is_invalid`, `Enum_value_not_allowed_is_invalid`, `Bool_accepts_only_true_false`; (integration) `Effective_view_lists_every_catalog_key_with_default_source_null`; `Value_at_root_is_inherited_by_school_with_source`; `Locked_root_value_blocks_school_override_409_setting_locked`; `Feature_flag_enabled_at_school_resolves_true_for_its_sections_only`; `Remove_override_falls_back_to_inherited`; `Manager_can_view_not_update`; `Setting_change_is_audited`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(settings): settings catalog API with locks and feature flags`.

### Task 15: Branch snapshot and copy branch — lane: BE1

**PRD:** FR-ORG-06. **Depends on:** Tasks 2, 9, 12, 14. DEV-PLAN item 7 (BE, part 1 of 2 — split: templates are Task 17).

**Files:**
- Create: `src/Lms.Infrastructure/Branches/BranchSnapshot.cs`, `BranchSnapshotter.cs`, `BranchMaterializer.cs`, `BranchesServiceCollectionExtensions.cs`
- Create: `src/Lms.Api/Features/Branches/BranchesModule.cs`, `CopyBranchEndpoint.cs`
- Test: `tests/Lms.IntegrationTests/Branches/CopyBranchTests.cs`

**Interfaces:**
- `sealed record BranchSetting(string Name, string ValueJson, bool IsLocked)`; `sealed record BranchNode(int Key, int? ParentKey, Guid LevelTypeId, string NameAr, string NameEn, string Code, bool IsLevelTransfer, int? NextTransferKey, bool IsCourseManagementLevel, IReadOnlyList<string> PolicyKeys, IReadOnlyList<BranchSetting> Settings)`; `sealed record BranchSnapshot(IReadOnlyList<BranchNode> Nodes)` (Key 0 = branch root; JSON-serializable).
- `IBranchSnapshotter.CaptureAsync(OrgUnit root, bool includePolicies, bool includeSettings, CancellationToken) : Task<BranchSnapshot>` — live units only; transfer pointers outside the branch dropped; > 500 nodes → `BranchTooLargeException`.
- `IBranchMaterializer.MaterializeAsync(BranchSnapshot snapshot, OrgUnit parent, Func<string, string> transformName, Func<string, string> transformCode, CancellationToken) : Task<MaterializeResult>`; `sealed record MaterializeResult(OrgUnit NewRoot, int UnitsCreated, IReadOnlyList<string> SkippedLockedSettings)` — new ids, remapped transfer pointers, policies and settings written at the new units; a setting locked by an ancestor of `parent` is skipped and reported; code conflicts → 409 `code_taken` listing the codes; one transaction.
- `POST /api/org-units/copy` body `CopyBranchRequest` → `CopyBranchResultDto` — `OrgUnits.Create` + `CheckUnitAsync(OrgUnits.Create, TargetParentId)` + `CheckUnitAsync(OrgUnits.View, SourceOrgUnitId)`. Codes: `code + CodeSuffix`; root names overridden when given. Target inside the source branch → 409 `invalid_move`. Courses/allocations are not copied in M1 (added with FR-CRS-02 in M3).

- [ ] **Step 1: Write the failing tests** `Copy_structure_only_creates_new_units_with_suffixed_codes`; `Copy_with_policies_and_settings_copies_rows_to_new_units`; `Transfer_mapping_inside_branch_is_remapped`; `Setting_locked_above_target_is_skipped_and_reported`; `Target_with_CreateOrgUnits_policy_is_blocked` (Review Focus 1); `Source_in_other_subtree_is_404`; `Copy_into_own_branch_is_409`; `Code_conflict_returns_409_and_creates_nothing`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(branches): copy branch with optional policies and settings`.

### Task 16: Notification preferences — lane: BE2

**PRD:** FR-NOT-01 (per-user channel preferences). **Depends on:** Task 10. DEV-PLAN item 13 (BE, part 2 of 2).

**Files:**
- Create: `src/Lms.Domain/Notifications/NotificationPreference.cs`, `INotificationPreferences.cs`; `src/Lms.Infrastructure/Notifications/NotificationPreferences.cs`; configuration; migration `AddNotificationPreferences`
- Create: `src/Lms.Api/Features/Notifications/MyPreferenceEndpoints.cs`
- Modify: `EndpointCoverageTests.cs` (exempt `/api/me/notification-preferences`)
- Test: `tests/Lms.IntegrationTests/Notifications/NotificationPreferenceTests.cs`

**Interfaces:**
- `NotificationPreference : Entity { Guid UserId, string EventKey, string Channel, bool Enabled }` — user-level (users are platform-level, so **not** tenant-owned; reason recorded in the class comment); unique `(UserId, EventKey, Channel)`.
- Defaults when no row: InApp on (locked), Email on, Sms off, Push off.
- `INotificationPreferences.IsEnabledAsync(Guid userId, string eventKey, string channel, CancellationToken) : Task<bool>` (used by M6 delivery).
- `GET /api/me/notification-preferences` → every catalog event × channel as `NotificationPreferenceDto`; `PUT` replaces the caller's rows; InApp disabled → 400 `in_app_required`; unknown event/channel → 400.

- [ ] **Step 1: Write the failing tests** `Defaults_are_returned_without_rows`; `Put_then_get_round_trips`; `Disabling_in_app_is_400`; `User_cannot_see_or_change_another_users_preferences` (isolation); `IsEnabled_reflects_saved_value`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(notifications): per-user channel preferences`.

### Task 17: Branch templates — save, library, apply with placeholders — lane: BE1

**PRD:** FR-ORG-07, FR-ORG-08. **Depends on:** Task 15. DEV-PLAN item 7 (BE, part 2 of 2).

**Files:**
- Create: `src/Lms.Domain/Organizations/OrgTemplate.cs`, `TemplateVisibility.cs`; `src/Lms.Infrastructure/Branches/TemplatePlaceholders.cs`; configuration; migration `AddOrgTemplates`
- Create: `src/Lms.Api/Features/Templates/TemplatesModule.cs`, `TemplateEndpoints.cs`
- Test: `tests/Lms.UnitTests/Branches/TemplatePlaceholdersTests.cs`, `tests/Lms.IntegrationTests/Templates/TemplateEndpointsTests.cs`, `TemplateIsolationTests.cs`

**Interfaces:**
- `[Audited] OrgTemplate : AuditableEntity, ITenantOwned { Guid OrgUnitId (owner), string OrgUnitPath, string NameAr, string NameEn, TemplateVisibility Visibility, string SnapshotJson, int UnitCount, string Placeholders (comma-separated) }` — not `IOrgUnitScoped` because shared templates are visible across sibling subtrees; the `OrgUnitPath` name makes Task 11's repather cover it.
- Visibility rule (explicit in queries): `Shared` → whole client; `Private` → callers whose scope contains the owner path or lies inside it.
- `TemplatePlaceholders.Parameterize(string text, IReadOnlyDictionary<string, string> map) : string`; `Find(string text) : IReadOnlySet<string>`; `Apply(string text, IReadOnlyDictionary<string, string> values) : string`.
- `GET /api/templates` → `IReadOnlyList<OrgTemplateSummaryDto>` — `Templates.View`. `GET /api/templates/{id}` → `OrgTemplateDetailDto` — `Templates.View` (invisible → 404). `POST /api/templates` body `SaveTemplateRequest` — `Templates.Create` + `CheckUnitAsync(Templates.Create, SourceOrgUnitId)`. `DELETE /api/templates/{id}` — `Templates.Delete` + `CheckUnitAsync(Templates.Delete, owner)`. `POST /api/templates/{id}/apply` body `ApplyTemplateRequest` → `ApplyTemplateResultDto` — `Templates.Apply` + `CheckUnitAsync(Templates.Apply, TargetParentId)` + `CheckUnitAsync(OrgUnits.Create, TargetParentId)`; missing placeholder → 400 `placeholder_missing`, extra → 400 `placeholder_unknown`; level types of the snapshot must still exist (400 `invalid_level_type`).

- [ ] **Step 1: Write the failing tests** (unit) `Parameterize_then_apply_restores_text`, `Find_returns_distinct_tokens`, `Apply_leaves_text_without_tokens_untouched`; (integration) `Save_branch_with_year_placeholder_and_apply_as_2027` (`"Section 1 2026"` → `"Section 1 {Year}"` → `"Section 1 2027"`); `Missing_placeholder_is_400`; `Private_template_of_School1_is_invisible_to_School2_admin_404`; `Shared_template_is_visible_client_wide`; `Template_of_client_B_is_invisible` (isolation); `Apply_under_policy_blocked_unit_is_403`; `Delete_is_soft_and_audited`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(templates): branch templates with placeholders`.

### Task 18: Audit viewer, subtree visibility and export — lane: BE2

**PRD:** FR-AUD-01, FR-AUD-03, FR-AUD-04, X-02, X-09 (paging). **Depends on:** Task 3; M0 #23. DEV-PLAN item 11 (BE).

**Files:**
- Modify: `src/Lms.Domain/Auditing/AuditEntry.cs` (+ `string? OrgUnitPath`), `src/Lms.Infrastructure/Persistence/Interceptors/AuditInterceptor.cs` (path = entity's `OrgUnitPath` when `IOrgUnitScoped`, `Path` for `OrgUnit`, else context scope), `AuditEntryConfiguration.cs` (ascii 400, index `(OrganizationId, OrgUnitPath, TimestampUtc)`); `src/Lms.Infrastructure/Auditing/AuditWriter.cs` (scope path for security events); migration `AddAuditOrgUnitPath`
- Create: `src/Lms.Api/Features/Audit/AuditModule.cs`, `AuditEndpoints.cs`, `AuditQueryBuilder.cs`, `AuditExporter.cs`
- Test: `tests/Lms.IntegrationTests/Audit/AuditViewerTests.cs`, `AuditExportTests.cs`, `tests/Lms.UnitTests/Contracts/AuditCatalogCoverageTests.cs`

**Interfaces:**
- Visibility: platform admin → all (optionally `OrganizationId` filter); others → own client (filter) **and** (`OrgUnitPath` starts with caller scope, or `OrgUnitPath` is null and the caller's scope is the client root).
- `GET /api/audit` (`AuditQuery`) → `PagedResult<AuditEntryDto>` newest first — `Audit.View`. `GET /api/audit/{id}` → `AuditEntryDetailDto` — `Audit.View` (invisible → 404). `GET /api/audit/export?format=csv|xlsx&…filters` → file `audit-{yyyyMMdd-HHmm}.{csv|xlsx}` — `Audit.Export`; > 50,000 rows → 400 `export_too_large`.
- `AuditQueryBuilder.Build(IQueryable<AuditEntry> source, AuditQuery query, ICurrentContext context) : IQueryable<AuditEntry>`; `AuditExporter.WriteCsvAsync(...)` / `WriteXlsxAsync(...)` over `AuditEntryDto`.

- [ ] **Step 1: Write the failing tests** `Every_Audited_domain_type_is_in_AuditCatalog` (unit, reflection over the Domain assembly); `Viewer_filters_by_user_entity_action_and_date`; `School_manager_sees_only_entries_of_own_subtree`; `Client_A_never_sees_client_B_entries` (isolation); `Detail_returns_before_and_after_json_with_arabic_intact`; `Csv_export_has_bom_and_arabic_names`; `Xlsx_export_opens_with_closedxml_and_has_header_row`; `Export_over_limit_is_400`; `Teacher_without_Audit_View_gets_403`; `Move_repaths_audit_entries` (with Task 11's repather).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(audit): audit viewer with subtree visibility and export`.

### Task 19: Roles admin — built-in permission sets, labels, custom roles — lane: BE1

**PRD:** FR-SEC-06, §3.1 (renameable labels), §3.2, X-02. **Depends on:** Tasks 3, 5; M0 #30. DEV-PLAN item 8 (BE).

**Files:**
- Create: `src/Lms.Api/Features/Roles/RolesModule.cs`, `RoleEndpoints.cs`, `RolePermissionRules.cs`
- Create: `src/Lms.Domain/Access/IRolePermissionCache.cs`
- Modify: `src/Lms.Infrastructure/Access/AccessEvaluator.cs` (cache key `role-permissions:{roleId}`; assignment → role read per request; implements `IRolePermissionCache.Invalidate(Guid roleId)`), `src/Lms.Domain/Access/Role.cs` (`Rename`, `SetPermissions` helper returning added/removed)
- Test: `tests/Lms.IntegrationTests/Roles/RoleEndpointsTests.cs`, `tests/Lms.UnitTests/Access/RolePermissionRulesTests.cs`

**Interfaces:**
- `GET /api/permissions` → `IReadOnlyList<PermissionGroupDto>` (no PlatformOnly) — `Roles.View`. `GET /api/roles` → `IReadOnlyList<RoleDto>` — `Roles.View`. `POST /api/roles` (custom) — `Roles.Create` + `CheckClientRootAsync`. `PUT /api/roles/{id}` — `Roles.Update` + root (built-in: labels and permissions editable, base kind fixed). `DELETE /api/roles/{id}` — `Roles.Delete` + root; built-in → 409 `built_in_role`; live assignments → 409 `role_in_use`; else soft delete.
- `RolePermissionRules.Validate(IReadOnlyCollection<string> requested, IReadOnlySet<string> callerPermissions, bool callerIsPlatformAdmin, string? builtInKey) : string?` → `validation_failed` (unknown), `platform_only_permission`, `permission_escalation` (requested ⊄ caller's, unless platform admin), `last_admin_permission` (OrgAdmin built-in losing `Roles.View` or `Roles.Update`), else null.
- Role names unique per client per language → 409 `name_taken`. After save → `IRolePermissionCache.Invalidate(roleId)`.

- [ ] **Step 1: Write the failing tests** (unit) one per rule outcome above; (integration) `Root_admin_creates_custom_Exam_Officer_role_with_base_kind`; `Built_in_role_label_and_permissions_are_editable_per_client_only`; `Custom_role_with_platform_only_permission_is_400` (Review Focus 2); `Granting_permission_caller_lacks_is_403_permission_escalation`; `Removing_Roles_Update_from_OrgAdmin_is_409`; `School_admin_cannot_create_roles_OutOfScope` (Review Focus 4); `Delete_built_in_is_409_and_in_use_custom_is_409`; `Removed_permission_is_denied_on_next_request` (no 60 s cache window); `Role_permission_change_is_audited`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(roles): role and permission admin with custom roles`.

### Task 20: Academic years and terms — lane: BE2

**PRD:** FR-YEAR-01, FR-YEAR-02, X-04 (data side). **Depends on:** Tasks 4, 5. DEV-PLAN item 12 (BE, part 1 of 2 — split: calendar is Task 22).

**Files:**
- Create: `src/Lms.Domain/AcademicYears/AcademicYear.cs`, `AcademicYearStatus.cs`, `Term.cs`, `IAcademicYearResolver.cs`; `src/Lms.Infrastructure/AcademicYears/AcademicYearResolver.cs`, `AcademicYearsServiceCollectionExtensions.cs`; configurations; migration `AddAcademicYears`
- Create: `src/Lms.Api/Features/AcademicYears/AcademicYearsModule.cs`, `AcademicYearEndpoints.cs`, `TermEndpoints.cs`, `AcademicYearValidators.cs`
- Test: `tests/Lms.IntegrationTests/AcademicYears/AcademicYearTests.cs`, `TermTests.cs`, `AcademicYearIsolationTests.cs`

**Interfaces:**
- `[Audited] AcademicYear : AuditableEntity, IOrgUnitScoped { NameAr, NameEn, DateOnly StartDate, DateOnly EndDate, AcademicYearStatus Status }`; `[Audited] Term : AuditableEntity, IOrgUnitScoped { Guid AcademicYearId, NameAr, NameEn, int Order, DateOnly StartDate, DateOnly EndDate }` (unit fields copied from its year).
- Inheritance: a unit's effective years are those defined at the **nearest ancestor-or-self unit that defines any year** (a unit defining its own years overrides the parent's whole list) — ISOLATION-BYPASS read with explicit org filter.
- `IAcademicYearResolver.GetEffectiveAsync(string orgUnitPath, CancellationToken) : Task<(IReadOnlyList<AcademicYear> Years, Guid DefiningOrgUnitId)>`; `GetCurrentAsync(string orgUnitPath, CancellationToken) : Task<AcademicYear?>` (the Active one).
- `GET /api/academic-years/effective?orgUnitId` (default: context unit) → `YearContextDto` — `AcademicYears.View` + unit. `POST /api/academic-years` — `AcademicYears.Create` + unit. `PUT /api/academic-years/{id}` — `AcademicYears.Update` + year unit; Closed → 409 `year_closed`. `POST /api/academic-years/{id}/activate` — `AcademicYears.Update`; another Active year at the same unit → 409 `active_year_exists` (closing is FR-YEAR-04, M7). `DELETE /api/academic-years/{id}` — `AcademicYears.Delete`; only Planned (409 `year_active` / `year_closed`); soft delete with its terms.
- Years at one unit must not overlap (409 `year_overlap`); `StartDate < EndDate` (400).
- Terms: `POST /api/academic-years/{id}/terms`, `PUT /api/terms/{id}`, `DELETE /api/terms/{id}` — `AcademicYears.Update` + year unit; within the year (409 `term_outside_year`); no overlap (409 `term_overlap`); `Order` recomputed by start date; `IsCurrent` = today (IClock) within the term; Closed year → 409 `year_closed`.

- [ ] **Step 1: Write the failing tests** `Root_year_is_inherited_by_school_and_marked_IsInherited`; `Faculty_with_own_years_overrides_university_years`; `Only_one_active_year_per_unit`; `Overlapping_years_at_same_unit_409`; `Term_outside_year_409_and_overlapping_term_409`; `Terms_are_ordered_and_current_term_follows_clock`; `Closed_year_is_read_only` (seed a Closed year directly); `Delete_active_year_409`; `Teacher_can_view_effective_years_but_not_create`; `School2_years_invisible_to_School1_and_client_B_invisible` (isolation for both tables).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(years): academic years and terms with inheritance`.

### Task 21: Role assignments admin — lane: BE1

**PRD:** §3.3 (assignment = user, role, unit, validity), FR-SEC-06, X-02. **Depends on:** Tasks 6, 10, 19. DEV-PLAN item 9 (BE).

**Files:**
- Create: `src/Lms.Api/Features/Assignments/AssignmentsModule.cs`, `AssignmentEndpoints.cs`, `CreateAssignmentHandler.cs`
- Modify: `src/Lms.Domain/Access/RoleAssignment.cs` (`ChangeValidity(DateOnly? from, DateOnly? to)`)
- Test: `tests/Lms.IntegrationTests/Assignments/AssignmentEndpointsTests.cs`

**Interfaces:**
- `GET /api/org-units/{id}/assignments?includeDescendants=false&page&pageSize` → `PagedResult<AssignmentDto>` — `Assignments.View` + unit.
- `POST /api/assignments` body `CreateAssignmentRequest` → 201 `AssignmentDto` — `Assignments.Create` + `CheckUnitAsync(Assignments.Create, OrgUnitId)`. Role must be live and in the client; the role's permissions must be ⊆ the caller's (403 `permission_escalation`, platform admin exempt); `ValidTo ≥ ValidFrom` (400); same user + role + unit with overlapping validity → 409 `assignment_exists`. Unknown email → create user (names required, 400 otherwise) and `IAccountEmails.SendInvitationAsync`. No user-lookup endpoint exists (no enumeration of other clients' users).
- `PUT /api/assignments/{id}` body `UpdateAssignmentRequest` — `Assignments.Update` + unit. `DELETE /api/assignments/{id}` — `Assignments.Delete` + unit; the caller's own active assignment → 409 `own_assignment`; soft delete.
- Publishes `Access.RoleAssigned` / `Access.RoleRemoved` to the assignee.

- [ ] **Step 1: Write the failing tests** `Admin_assigns_existing_user_teacher_role_at_section`; `Unknown_email_creates_user_and_sends_invitation`; `Assigning_OrgAdmin_role_by_caller_without_its_permissions_is_403` (Review Focus 2); `Assignment_in_other_subtree_unit_is_404`; `Overlapping_duplicate_is_409`; `ValidTo_before_ValidFrom_is_400`; `Updated_validity_controls_context_validity`; `Removing_own_assignment_is_409`; `Assignee_gets_role_assigned_notification`; `Assignment_create_and_delete_are_audited`; `List_with_descendants_is_paged`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(assignments): role assignment admin with validity dates`.

### Task 22: Academic calendar — holidays and events — lane: BE2

**PRD:** FR-YEAR-03. **Depends on:** Task 20. DEV-PLAN item 12 (BE, part 2 of 2).

**Files:**
- Create: `src/Lms.Domain/AcademicYears/CalendarEvent.cs`, `CalendarEventKind.cs`, `IAcademicCalendar.cs`; `src/Lms.Infrastructure/AcademicYears/AcademicCalendar.cs`; configuration; migration `AddCalendarEvents`
- Create: `src/Lms.Api/Features/AcademicYears/CalendarEndpoints.cs`
- Test: `tests/Lms.IntegrationTests/AcademicYears/CalendarTests.cs`, `CalendarIsolationTests.cs`

**Interfaces:**
- `[Audited] CalendarEvent : AuditableEntity, IOrgUnitScoped { Guid AcademicYearId, TitleAr, TitleEn, CalendarEventKind Kind, DateOnly StartDate, DateOnly EndDate }` — at the year's unit or any descendant; dates inside the year (409 `event_outside_year`).
- `GET /api/academic-years/{id}/calendar?orgUnitId` → events at ancestors-or-self of the unit for that year (`IsInherited` when not at the unit) — `AcademicYears.View` + unit. `POST /api/calendar-events`, `PUT /api/calendar-events/{id}`, `DELETE /api/calendar-events/{id}` — `AcademicYears.Update` + event unit; Closed year → 409 `year_closed`.
- `IAcademicCalendar.IsNonWorkingDayAsync(string orgUnitPath, DateOnly date, CancellationToken) : Task<bool>` — true when a Holiday covers the date at the unit or an ancestor (used by M5 timetable/attendance).

- [ ] **Step 1: Write the failing tests** `Root_holiday_applies_to_school_and_is_inherited`; `School_event_does_not_appear_for_sibling_school`; `Event_outside_year_is_409`; `IsNonWorkingDay_true_only_for_holidays`; `Closed_year_calendar_is_read_only`; `Client_B_events_invisible` (isolation).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(years): academic calendar with holidays and events`.

### Task 23: Client API plumbing and shared admin components — lane: FE

**PRD:** X-01, X-10, FR-ORG-10 (clear block message). **Depends on:** Task 1; M0 #28.

**Files:**
- Create: `src/Lms.Client/Services/Api/ApiHttp.cs`, `ApiProblemException.cs`, `src/Lms.Client/Services/ProblemText.cs`, `src/Lms.Client/Components/BilingualNameFields.razor`, `ConfirmDialog.razor`, `ServerTable.razor` (MudTable with `ServerData`, page size 50), `LocalizedName.cs` (`Culture.Pick(ar, en)`)
- Modify: `src/Lms.Client/Services/LmsApiClient.cs` (`ForbiddenRedirectHandler` redirects only for GET; non-GET 403 becomes `ApiProblemException`), resources
- Test: `tests/Lms.Client.Tests/Services/ApiHttpTests.cs`, `tests/Lms.Client.Tests/Components/BilingualNameFieldsTests.cs`, `ConfirmDialogTests.cs`

**Interfaces:**
- `ApiHttp(HttpClient http, IContextStore context)` with `GetAsync<T>(string route, CancellationToken)`, `SendAsync<TResponse>(HttpMethod method, string route, object? body, CancellationToken)`, `SendAsync(HttpMethod, string, object?, CancellationToken)`, `DownloadAsync(string route, CancellationToken) : Task<(Stream Content, string FileName)>` — adds `X-Lms-Context`; non-success → `ApiProblemException(int Status, string? Code, string? Detail, IReadOnlyDictionary<string, string[]>? Errors)`.
- `ProblemText.For(ApiProblemException ex, IStringLocalizer<SharedResource> l) : string` — `BlockedByPolicy` → `ex.Detail`; known code → `Error_{code}`; else `Error_Generic`.
- `BilingualNameFields` parameters `NameAr`, `NameEn` (two-way), `Required=true`; Arabic input always `dir="rtl"`, English always `dir="ltr"`.
- Resource keys (en / ar): `Common_Save` Save / حفظ; `Common_Cancel` Cancel / إلغاء; `Common_Delete` Delete / حذف; `Common_Edit` Edit / تعديل; `Common_Add` Add / إضافة; `Common_Confirm` Confirm / تأكيد; `Common_Search` Search / بحث; `Common_NameAr` Name (Arabic) / الاسم بالعربية; `Common_NameEn` Name (English) / الاسم بالإنجليزية; `Common_Code` Code / الرمز; `Common_Status` Status / الحالة; `Common_Actions` Actions / الإجراءات; `Common_Empty` Nothing here yet. / لا توجد بيانات بعد.; `Common_Saved` Saved. / تم الحفظ.; `Common_Required` Required / مطلوب; `Confirm_DeleteTitle` Delete? / تأكيد الحذف؟; `Error_Generic` Something went wrong. Try again. / حدث خطأ. حاول مرة أخرى.; and one `Error_{code}` per `ProblemCodes` value — e.g. `Error_code_taken` This code is already used. / هذا الرمز مستخدم بالفعل.; `Error_unit_has_children` Remove or move the child units first. / احذف الوحدات الفرعية أو انقلها أولًا.; `Error_setting_locked` A parent unit has locked this setting. / قامت وحدة أعلى بقفل هذا الإعداد.; `Error_policy_inherited` This policy is set by a parent unit and cannot be removed here. / هذه السياسة مفروضة من وحدة أعلى ولا يمكن إزالتها هنا.; `Error_permission_escalation` You cannot grant permissions you do not have. / لا يمكنك منح صلاحيات لا تملكها. (FE writes the remaining codes in the same plain style; QA checks every code has both.)

- [ ] **Step 1: Write the failing bUnit/unit tests** `Problem_with_code_maps_to_Error_code_resource`; `Blocked_by_policy_shows_server_detail`; `Post_403_does_not_redirect_but_throws_ApiProblemException`; `Get_403_still_redirects_to_forbidden`; `Arabic_name_field_is_rtl_even_in_english_ui`; `Confirm_dialog_returns_true_only_on_confirm`; `Every_problem_code_has_an_error_resource` (reflection over `ProblemCodes`).
- [ ] **Step 2: Run** `dotnet test tests/Lms.Client.Tests` → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(client): API plumbing, problem messages and shared admin components`.

### Task 24: Clients and level types pages — lane: FE

**PRD:** FR-ORG-01, FR-ORG-02. **Depends on:** Tasks 1, 23 (fakes until Tasks 7, 8 merge). DEV-PLAN items 1–2 (FE).

**Files:**
- Create: `src/Lms.Client/Services/Api/IClientsApi.cs`, `ClientsApi.cs`, `FakeClientsApi.cs`, `IOrgApi.cs` (level types + org units, later tasks append), `OrgApi.cs`, `FakeOrgApi.cs`
- Create: `src/Lms.Client/Pages/Clients/ClientsPage.razor` (`/clients`), `CreateClientDialog.razor`, `src/Lms.Client/Pages/Organization/LevelTypesPage.razor` (`/level-types`)
- Modify: `Navigation/NavCatalog.cs` (+ `Nav_LevelTypes` → `level-types`, `LevelTypes.View`), `Services/ClientServices.cs`, resources
- Test: `tests/Lms.Client.Tests/Pages/Clients/ClientsPageTests.cs`, `tests/Lms.Client.Tests/Pages/Organization/LevelTypesPageTests.cs`

**Interfaces:**
- `IClientsApi`: `ListAsync(int page, int pageSize, string? search, CancellationToken) : Task<PagedResult<ClientDto>>`, `CreateAsync(CreateClientRequest, CancellationToken) : Task<ClientDto>`, `UpdateAsync(Guid id, UpdateClientRequest, CancellationToken) : Task<ClientDto>`.
- `IOrgApi` (this task): `GetLevelTypesAsync`, `CreateLevelTypeAsync`, `UpdateLevelTypeAsync`, `ReorderLevelTypesAsync`, `DeleteLevelTypeAsync`.
- Level types page: ordered list with up/down buttons (keyboard accessible, no drag-only), edit dialog, delete with confirm; add/edit/delete hidden without `LevelTypes.Create/Update/Delete`.
- Resource keys (en / ar): `Clients_Title` Clients / العملاء; `Clients_New` New client / عميل جديد; `Clients_Type` Type / النوع; `ClientType_SchoolGroup` School group / مجموعة مدارس; `ClientType_University` University / جامعة; `ClientStatus_Active` Active / نشط; `ClientStatus_Suspended` Suspended / موقوف; `Clients_RootLevel` First level name / اسم المستوى الأول; `Clients_RootCode` Root unit code / رمز الوحدة الرئيسية; `Clients_AdminEmail` Admin email / بريد المسؤول; `Clients_AdminName` Admin name / اسم المسؤول; `Clients_Created` Client created. An invitation was sent to the admin. / تم إنشاء العميل وإرسال دعوة إلى المسؤول.; `Nav_LevelTypes` Level types / أنواع المستويات; `LevelTypes_Title` Level types / أنواع المستويات; `LevelTypes_CanHostClasses` Can host classes / يمكن أن تحتوي على فصول; `LevelTypes_MoveUp` Move up / تحريك لأعلى; `LevelTypes_MoveDown` Move down / تحريك لأسفل; `LevelTypes_Units` Units / الوحدات.

- [ ] **Step 1: Write the failing bUnit tests** `Clients_page_lists_and_searches_clients`; `Create_client_dialog_validates_required_fields_and_posts_request`; `Clients_nav_hidden_for_non_platform_admin`; `Level_types_reorder_sends_new_order`; `Delete_used_level_type_shows_level_type_in_use_message`; `Level_type_actions_hidden_without_permissions`; `Pages_render_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots ar 360 px / ar desktop / en desktop.
- [ ] **Step 5: Commit** `feat(client): clients and level types pages`.

### Task 25: Organization tree page — lane: FE

**PRD:** FR-ORG-03, FR-ORG-04, FR-ORG-10 (UI hiding), D15. **Depends on:** Tasks 1, 23, 24. DEV-PLAN item 3 (FE, part 1 of 2).

**Files:**
- Create: `src/Lms.Client/Pages/Organization/OrgTreePage.razor` (`/org-units`), `OrgUnitDialog.razor`, `OrgTreeBuilder.cs` (flat `OrgUnitNodeDto` list → `TreeItemData`)
- Modify: `IOrgApi.cs`/`OrgApi.cs`/`FakeOrgApi.cs` (+ `GetOrgUnitsAsync(string? search, CancellationToken)`, `CreateOrgUnitAsync`, `UpdateOrgUnitAsync`, `SetOrgUnitStatusAsync`, `DeleteOrgUnitAsync`), resources
- Test: `tests/Lms.Client.Tests/Pages/Organization/OrgTreePageTests.cs`, `OrgTreeBuilderTests.cs`

**Interfaces:**
- MudTreeView, expand/collapse, search box (debounced 300 ms, server search, matches highlighted, ancestors expanded); per node: add child, edit, deactivate/activate, delete, open settings (`/org-units/{id}`). "Add child" hidden when the user lacks `OrgUnits.Create` **or** the node's `BlockedPermissions` contains it (FR-ORG-10 UI hiding); inactive nodes greyed with an "Inactive" chip.
- Resource keys (en / ar): `OrgTree_Title` Organization tree / الهيكل التنظيمي; `OrgTree_AddChild` Add unit / إضافة وحدة; `OrgTree_Search` Search units / البحث في الوحدات; `OrgTree_LevelType` Level type / نوع المستوى; `OrgTree_Deactivate` Deactivate / إيقاف; `OrgTree_Activate` Activate / تفعيل; `OrgTree_Inactive` Inactive / غير نشط; `OrgTree_Settings` Unit settings / إعدادات الوحدة; `OrgTree_DeleteConfirm` Delete this unit? It can be restored by support only. / حذف هذه الوحدة؟ لا يمكن استعادتها إلا عن طريق الدعم الفني.; `OrgTree_NoMatches` No units match your search. / لا توجد وحدات مطابقة للبحث.

- [ ] **Step 1: Write the failing tests** `Builder_nests_children_and_sorts_by_name_of_current_culture`; `Search_expands_ancestors_of_matches`; `Add_child_hidden_when_policy_blocks_it`; `Create_dialog_posts_parent_and_level_type`; `Delete_with_children_shows_error_message`; `Inactive_node_shows_chip_and_activate_action`; `Tree_renders_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): organization tree with search and unit actions`.

### Task 26: Move unit and unit settings page (general, level transfer, course-management) — lane: FE

**PRD:** FR-ORG-03 (move), FR-ORG-05, FR-ORG-09. **Depends on:** Task 25 (BE Tasks 11, 13 or fakes). DEV-PLAN item 3 (FE, part 2 of 2) and item 4 (FE).

**Files:**
- Create: `src/Lms.Client/Pages/Organization/MoveUnitDialog.razor`, `UnitSettingsPage.razor` (`/org-units/{Id:guid}` with tabs General · Academic · Settings · Policies; Settings/Policies tab bodies come in Task 27), `AcademicFlagsTab.razor`
- Modify: `IOrgApi` (+ `MoveOrgUnitAsync`, `SetAcademicFlagsAsync`, `GetOrgUnitAsync`), `OrgTreePage.razor` (Move action), resources
- Test: `tests/Lms.Client.Tests/Pages/Organization/MoveUnitDialogTests.cs`, `UnitSettingsPageTests.cs`

**Interfaces:**
- Move dialog: pick new parent from a tree that excludes the unit's own branch; Move shown only with `OrgUnits.Move`; confirm text names old and new parent.
- Academic tab: switch "Level transfer"; when on, picker "Next level" listing transfer units of the same level type (excluding self); switch "Course management level"; a read-only "Transfer path" chain (e.g. KG → Primary).
- Resource keys (en / ar): `Move_Title` Move unit / نقل الوحدة; `Move_NewParent` New parent / الوحدة الأم الجديدة; `Move_Confirm` Move "{0}" from "{1}" to "{2}"? / نقل "{0}" من "{1}" إلى "{2}"؟; `UnitSettings_General` General / عام; `UnitSettings_Academic` Academic / أكاديمي; `UnitSettings_Settings` Settings / الإعدادات; `UnitSettings_Policies` Policies / السياسات; `Academic_LevelTransfer` Level transfer (promotion level) / مستوى انتقال (ترفيع); `Academic_NextLevel` Next level / المستوى التالي; `Academic_TransferPath` Transfer path / مسار الانتقال; `Academic_CourseManagement` Course management level / مستوى إدارة المقررات.

- [ ] **Step 1: Write the failing tests** `Move_dialog_excludes_own_branch_from_targets`; `Move_hidden_without_OrgUnits_Move`; `Next_level_picker_lists_same_level_type_transfer_units_only`; `Turning_off_transfer_clears_next_level`; `Transfer_path_shows_chain`; `Settings_page_tabs_render_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): move unit and unit academic settings`.

### Task 27: Policies and settings tabs with feature flags — lane: FE

**PRD:** FR-ORG-10, FR-ORG-11, §4.3, §4.4. **Depends on:** Tasks 2, 26 (BE Tasks 12, 14 or fakes). DEV-PLAN items 5–6 (FE).

**Files:**
- Create: `src/Lms.Client/Services/Api/IConfigApi.cs`, `ConfigApi.cs`, `FakeConfigApi.cs`; `src/Lms.Client/Pages/Organization/PoliciesTab.razor`, `SettingsTab.razor`, `SettingEditor.razor` (renders by `SettingDefinition.Kind`)
- Modify: `UnitSettingsPage.razor` (tab bodies), resources
- Test: `tests/Lms.Client.Tests/Pages/Organization/PoliciesTabTests.cs`, `SettingsTabTests.cs`

**Interfaces:**
- `IConfigApi`: `GetPolicyCatalogAsync`, `GetPoliciesAsync(Guid unitId)`, `EnablePolicyAsync(Guid unitId, string key)`, `DisablePolicyAsync(Guid unitId, string key)`, `GetSettingCatalogAsync`, `GetSettingsAsync(Guid unitId)`, `SetSettingAsync(Guid unitId, string name, SetSettingRequest)`, `RemoveSettingAsync(Guid unitId, string name)`.
- Policies tab: one row per policy: switch (disabled with "Set by {unit}" when inherited), label `Policy_{Key}`, list of blocked actions. Settings tab grouped by category (General, Files, Features = feature flags): value editor, "Lock for units below" checkbox, source line "Inherited from {unit}" / "Default" / "Set here", "Reset to inherited" action, editor disabled with lock icon when `LockedByAncestor`.
- Resource keys (en / ar): `Policy_CreateOrgUnits` Block creating units / منع إنشاء الوحدات; `Policies_SetBy` Set by {0} / مفروضة من {0}; `Policies_Blocks` Blocks: / تمنع:; `Settings_Lock` Lock for units below / قفل للوحدات الأدنى; `Settings_LockedBy` Locked by {0} / مقفل من {0}; `Settings_InheritedFrom` Inherited from {0} / موروث من {0}; `Settings_Default` Default / افتراضي; `Settings_SetHere` Set here / محدد هنا; `Settings_Reset` Reset to inherited / إعادة إلى الموروث; `SettingCategory_General` General / عام; `SettingCategory_Files` Files / الملفات; `SettingCategory_Features` Features / الميزات; `Setting_Ui_DefaultCulture` Default language / اللغة الافتراضية; `Setting_Ui_ShowHijriDates` Show Hijri dates / عرض التاريخ الهجري; `Setting_Files_MaxUploadMb` Max upload size (MB) / الحد الأقصى لحجم الرفع (ميجابايت); `Setting_Features_VirtualClasses` Virtual classes / الفصول الافتراضية; `Setting_Features_DigitalLibrary` Digital library / المكتبة الرقمية; `Setting_Features_ScormContent` SCORM content / محتوى SCORM; `Setting_Features_Messaging` Messaging / المراسلات; `Setting_Features_GuardianPortal` Guardian portal / بوابة ولي الأمر. (Setting key → resource key: `Setting_` + name with `.` → `_`.)

- [ ] **Step 1: Write the failing tests** `Inherited_policy_switch_is_disabled_and_names_source`; `Toggling_policy_calls_enable_then_disable`; `Every_catalog_setting_and_policy_has_ar_and_en_label`; `Locked_by_ancestor_setting_is_read_only`; `Int_setting_out_of_range_shows_validation_error`; `Feature_flags_render_as_switches_in_features_group`; `Tabs_render_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): policies and settings tabs with feature flags`.

### Task 28: Copy branch, save as template, template library and apply — lane: FE

**PRD:** FR-ORG-06, FR-ORG-07, FR-ORG-08. **Depends on:** Tasks 2, 25 (BE Tasks 15, 17 or fakes). DEV-PLAN item 7 (FE).

**Files:**
- Create: `src/Lms.Client/Services/Api/ITemplatesApi.cs`, `TemplatesApi.cs`, `FakeTemplatesApi.cs`; `src/Lms.Client/Pages/Organization/CopyBranchDialog.razor`, `SaveTemplateDialog.razor`, `src/Lms.Client/Pages/Templates/TemplatesPage.razor` (`/templates`), `ApplyTemplateDialog.razor`, `TemplatePreview.razor`
- Modify: `OrgTreePage.razor` (Copy / Save as template actions), `IOrgApi` (+ `CopyBranchAsync`), `NavCatalog.cs` (+ `Nav_Templates` → `templates`, `Templates.View`), resources
- Test: `tests/Lms.Client.Tests/Pages/Templates/TemplatesPageTests.cs`, `ApplyTemplateDialogTests.cs`, `tests/Lms.Client.Tests/Pages/Organization/CopyBranchDialogTests.cs`

**Interfaces:**
- `ITemplatesApi`: `ListAsync`, `GetAsync(Guid id)`, `SaveAsync(SaveTemplateRequest)`, `DeleteAsync(Guid id)`, `ApplyAsync(Guid id, ApplyTemplateRequest)`.
- Save dialog has a "Replace text with placeholder" list (literal → `{Token}`); apply dialog renders one required input per template placeholder, a target-parent tree picker and a live preview of resulting names; results show units created and skipped locked settings.
- Resource keys (en / ar): `Copy_Title` Copy branch / نسخ الفرع; `Copy_Target` Copy under / النسخ تحت; `Copy_CodeSuffix` Code suffix / لاحقة الرمز; `Copy_IncludePolicies` Include policies / تضمين السياسات; `Copy_IncludeSettings` Include settings / تضمين الإعدادات; `Copy_Done` {0} units created. / تم إنشاء {0} وحدة.; `Copy_SkippedSettings` Skipped settings locked above: {0} / إعدادات تم تخطيها لأنها مقفلة من الأعلى: {0}; `Templates_Title` Templates / القوالب; `Templates_SaveAs` Save as template / حفظ كقالب; `Templates_Visibility` Visibility / الظهور; `Visibility_Private` Private to this unit / خاص بهذه الوحدة; `Visibility_Shared` Shared across the client / مشترك على مستوى العميل; `Templates_Placeholder` Replace "{0}" with placeholder / استبدال "{0}" بعنصر نائب; `Templates_Apply` Apply template / تطبيق القالب; `Templates_Preview` Preview / معاينة; `Templates_Units` {0} units / {0} وحدة.

- [ ] **Step 1: Write the failing tests** `Copy_dialog_requires_suffix_and_target`; `Copy_result_lists_skipped_settings`; `Save_template_sends_parameterize_map`; `Apply_dialog_requires_every_placeholder_and_previews_names`; `Templates_page_hides_delete_without_permission`; `Pages_render_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): copy branch and template library`.

### Task 29: Roles admin page — lane: FE

**PRD:** FR-SEC-06, §3.1, §3.2. **Depends on:** Tasks 3, 23 (BE Task 19 or fake). DEV-PLAN item 8 (FE).

**Files:**
- Create: `src/Lms.Client/Services/Api/IAccessAdminApi.cs`, `AccessAdminApi.cs`, `FakeAccessAdminApi.cs`; `src/Lms.Client/Pages/Access/RolesPage.razor` (`/roles`), `RoleEditor.razor`, `PermissionMatrix.razor`
- Modify: resources
- Test: `tests/Lms.Client.Tests/Pages/Access/RolesPageTests.cs`, `PermissionMatrixTests.cs`

**Interfaces:**
- `IAccessAdminApi` (this task): `GetPermissionGroupsAsync`, `GetRolesAsync`, `CreateRoleAsync`, `UpdateRoleAsync`, `DeleteRoleAsync`.
- Roles list (built-in badge, assignment count); editor: bilingual name, base kind (create only), permission matrix grouped by module with "select all in module"; permission labels `Perm_{Module}_{Action}`, module labels `PermModule_{Module}`.
- Resource keys (en / ar): `Roles_Title` Roles and permissions / الأدوار والصلاحيات; `Roles_New` New role / دور جديد; `Roles_BuiltIn` Built-in / أساسي; `Roles_BaseKind` Base kind / النوع الأساسي; `BaseKind_Staff` Staff / موظف; `BaseKind_Student` Student / طالب; `BaseKind_Guardian` Guardian / ولي أمر; `Roles_Assignments` Assignments / التعيينات; `Roles_SelectModule` Select all / تحديد الكل; plus `PermModule_*` and `Perm_*` for every non-PlatformOnly permission (e.g. `Perm_OrgUnits_Create` Create units / إنشاء الوحدات; `Perm_Audit_Export` Export audit log / تصدير سجل التدقيق).

- [ ] **Step 1: Write the failing tests** `Every_non_platform_permission_has_ar_and_en_label` (reflection over `Permissions.All` minus `PlatformOnly`); `Built_in_role_base_kind_and_delete_are_locked`; `Select_all_in_module_toggles_group`; `Escalation_error_is_shown_from_problem_code`; `Page_renders_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): roles and permissions admin`.

### Task 30: Role assignments page — lane: FE

**PRD:** §3.3, FR-SEC-06. **Depends on:** Task 29 (BE Task 21 or fake). DEV-PLAN item 9 (FE).

**Files:**
- Create: `src/Lms.Client/Pages/Access/AssignmentsPage.razor` (`/assignments`, unit picker + optional `?unit=`), `AssignRoleDialog.razor`
- Modify: `IAccessAdminApi` (+ `GetAssignmentsAsync(Guid unitId, bool includeDescendants, int page, int pageSize)`, `CreateAssignmentAsync`, `UpdateAssignmentAsync`, `DeleteAssignmentAsync`), `UnitSettingsPage.razor` (link "People with roles here"), `NavCatalog.cs` (+ `Nav_Assignments` → `assignments`, `Assignments.View`), resources
- Test: `tests/Lms.Client.Tests/Pages/Access/AssignmentsPageTests.cs`, `AssignRoleDialogTests.cs`

**Interfaces:**
- Server-paged table (user, email, role, unit, valid from/to, status chip Valid/Not yet valid/Expired); assign dialog: email, names (shown when the API answers 400 for a new user — or always, marked "only for new users"), role select, unit picker, MudDatePicker for validity (Gregorian; Hijri display when `Ui.ShowHijriDates` is on is out of scope in M1 pages).
- Resource keys (en / ar): `Nav_Assignments` Role assignments / تعيين الأدوار; `Assignments_Title` Role assignments / تعيين الأدوار; `Assignments_Assign` Assign role / تعيين دور; `Assignments_Email` Email / البريد الإلكتروني; `Assignments_NewUserHint` Names are used only if this email has no account yet; the person gets an invitation. / تُستخدم الأسماء فقط إذا لم يكن لهذا البريد حساب، وسيتلقى الشخص دعوة.; `Assignments_ValidFrom` Valid from / صالح من; `Assignments_ValidTo` Valid to / صالح حتى; `Assignments_IncludeBelow` Include units below / تضمين الوحدات الأدنى; `AssignmentStatus_Valid` Valid / ساري; `AssignmentStatus_Future` Not yet valid / لم يبدأ بعد; `AssignmentStatus_Expired` Expired / منتهي; `Assignments_RemoveConfirm` Remove this role from {0}? / إزالة هذا الدور من {0}؟.

- [ ] **Step 1: Write the failing tests** `Table_requests_server_pages`; `Assign_dialog_rejects_valid_to_before_valid_from`; `Assign_posts_request_and_refreshes_table`; `Status_chip_reflects_dates`; `Actions_hidden_without_permissions`; `Page_renders_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): role assignments page`.

### Task 31: Audit viewer page — lane: FE

**PRD:** FR-AUD-03, FR-AUD-04. **Depends on:** Tasks 3, 23 (BE Task 18 or fake). DEV-PLAN item 11 (FE).

**Files:**
- Create: `src/Lms.Client/Services/Api/IAuditApi.cs`, `AuditApi.cs`, `FakeAuditApi.cs`; `src/Lms.Client/Pages/Audit/AuditPage.razor` (`/audit`), `AuditDetailDrawer.razor`, `AuditDiff.cs` (before/after JSON → rows `(Property, Before, After)`); `wwwroot/js/download.js` (save blob)
- Modify: resources
- Test: `tests/Lms.Client.Tests/Pages/Audit/AuditPageTests.cs`, `AuditDiffTests.cs`

**Interfaces:**
- `IAuditApi`: `QueryAsync(AuditQuery)`, `GetAsync(Guid id)`, `ExportAsync(AuditQuery, string format)`.
- `AuditDiff.Rows(string? beforeJson, string? afterJson) : IReadOnlyList<AuditDiffRow>`; `sealed record AuditDiffRow(string Property, string? Before, string? After)`.
- Filters: user (id from row click), entity type (from `AuditCatalog`), action, date range; server-paged table; detail drawer with diff table (changed values highlighted); Export CSV / Excel buttons shown only with `Audit.Export`.
- Resource keys (en / ar): `Audit_Title` Audit log / سجل التدقيق; `Audit_When` When / الوقت; `Audit_Who` Who / المستخدم; `Audit_Role` Role / الدور; `Audit_Unit` Unit / الوحدة; `Audit_Action` Action / الإجراء; `Audit_Entity` Entity / الكيان; `Audit_From` From / من; `Audit_To` To / إلى; `Audit_Before` Before / قبل; `Audit_After` After / بعد; `Audit_ExportCsv` Export CSV / تصدير CSV; `Audit_ExportExcel` Export Excel / تصدير Excel; `AuditAction_Created` Created / إنشاء; `AuditAction_Updated` Updated / تعديل; `AuditAction_Deleted` Deleted / حذف; `AuditAction_SecurityEvent` Security event / حدث أمني; `AuditEntity_{Type}` and `AuditEvent_{Name}` for every `AuditCatalog` entry (e.g. `AuditEntity_OrgUnit` Organization unit / وحدة تنظيمية; `AuditEvent_LoginFailed` Failed sign-in / فشل تسجيل الدخول).

- [ ] **Step 1: Write the failing tests** `Diff_lists_changed_properties_only_for_updates`; `Diff_of_create_shows_after_only`; `Filters_are_sent_in_query`; `Export_buttons_hidden_without_Audit_Export`; `Every_audit_catalog_entry_has_ar_and_en_label`; `Page_renders_rtl_in_arabic_with_arabic_values_intact`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): audit viewer with diff and export`.

### Task 32: Academic years, terms and calendar pages — lane: FE

**PRD:** FR-YEAR-01..03. **Depends on:** Tasks 4, 23 (BE Tasks 20, 22 or fakes). DEV-PLAN item 12 (FE, part 1 of 2).

**Files:**
- Create: `src/Lms.Client/Services/Api/IYearsApi.cs`, `YearsApi.cs`, `FakeYearsApi.cs`; `src/Lms.Client/Pages/AcademicYears/AcademicYearsPage.razor` (`/academic-years`, unit picker), `YearDialog.razor`, `TermsPanel.razor`, `CalendarPanel.razor` (list + month view with MudCalendar-free simple grid), `CalendarEventDialog.razor`
- Modify: `NavCatalog.cs` (+ `Nav_AcademicYears` → `academic-years`, `AcademicYears.View`), resources
- Test: `tests/Lms.Client.Tests/Pages/AcademicYears/AcademicYearsPageTests.cs`, `TermsPanelTests.cs`, `CalendarPanelTests.cs`

**Interfaces:**
- `IYearsApi`: `GetEffectiveAsync(Guid? orgUnitId)`, `CreateYearAsync`, `UpdateYearAsync`, `ActivateYearAsync`, `DeleteYearAsync`, `CreateTermAsync`, `UpdateTermAsync`, `DeleteTermAsync`, `GetCalendarAsync(Guid yearId, Guid orgUnitId)`, `CreateEventAsync`, `UpdateEventAsync`, `DeleteEventAsync`.
- Inherited years shown read-only with "Defined at {unit}" and an "Define own years for this unit" action; closed years read-only with lock icon; current term badge.
- Resource keys (en / ar): `Nav_AcademicYears` Academic years / الأعوام الدراسية; `Years_Title` Academic years / الأعوام الدراسية; `Years_New` New year / عام جديد; `Years_Start` Start date / تاريخ البدء; `Years_End` End date / تاريخ الانتهاء; `Years_Activate` Set as active / تعيين كعام نشط; `YearStatus_Planned` Planned / مخطط; `YearStatus_Active` Active / نشط; `YearStatus_Closed` Closed / مغلق; `Years_DefinedAt` Defined at {0} / محدد في {0}; `Years_DefineOwn` Define own years for this unit / تحديد أعوام خاصة بهذه الوحدة; `Terms_Title` Terms / الفصول الدراسية; `Terms_Current` Current / الحالي; `Calendar_Title` Calendar / التقويم; `Calendar_Holiday` Holiday / عطلة; `Calendar_Event` Event / مناسبة; `Calendar_InheritedFrom` From {0} / من {0}.

- [ ] **Step 1: Write the failing tests** `Inherited_years_are_read_only_and_name_source`; `Closed_year_disables_editing`; `Term_dates_outside_year_show_error_from_problem_code`; `Current_term_badge_shown`; `Calendar_marks_holidays_and_inherited_events`; `Page_renders_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): academic years, terms and calendar pages`.

### Task 33: Global academic year selector — lane: FE

**PRD:** X-04. **Depends on:** Task 32. DEV-PLAN item 12 (FE, part 2 of 2).

**Files:**
- Create: `src/Lms.Client/Services/YearStore.cs`, `src/Lms.Client/Layout/YearSelector.razor`
- Modify: `src/Lms.Client/Layout/MainLayout.razor` (selector in app bar after context switcher), `Services/ClientServices.cs`, resources
- Test: `tests/Lms.Client.Tests/Layout/YearSelectorTests.cs`, `tests/Lms.Client.Tests/Services/YearStoreTests.cs`

**Interfaces:**
- `IYearStore { Guid? SelectedYearId { get; } AcademicYearDto? Selected { get; } bool IsReadOnly { get; } Task LoadAsync(Guid orgUnitId); Task SelectAsync(Guid yearId); event Action? Changed; }` — persisted as `lms.year.{assignmentId}` in localStorage; default = `CurrentYearId`; reloads on `IContextStore.Changed`; `IsReadOnly` when the selected year is Closed. Later milestones' lists pass `SelectedYearId` as `yearId` query parameter.
- Resource keys (en / ar): `Year_Selector` Academic year / العام الدراسي; `Year_None` No academic year / لا يوجد عام دراسي; `Year_ReadOnly` Closed year — read only / عام مغلق — للقراءة فقط.

- [ ] **Step 1: Write the failing tests** `Defaults_to_current_year`; `Selection_persists_per_context`; `Context_change_reloads_years`; `Closed_year_sets_read_only_and_shows_lock`; `Selector_hidden_without_AcademicYears_View`; `Selector_renders_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): global academic year selector`.

### Task 34: Notification center and preferences — lane: FE

**PRD:** FR-NOT-01, FR-NOT-02. **Depends on:** Tasks 4, 23 (BE Tasks 10, 16 or fakes). DEV-PLAN item 13 (FE).

**Files:**
- Create: `src/Lms.Client/Services/Api/INotificationsApi.cs`, `NotificationsApi.cs`, `FakeNotificationsApi.cs`; `src/Lms.Client/Services/NotificationConnection.cs` (SignalR client, auto-reconnect); `src/Lms.Client/Layout/NotificationBell.razor`; `src/Lms.Client/Pages/Notifications/NotificationsPage.razor` (`/notifications`), `NotificationPreferencesPage.razor` (`/notifications/preferences`); `NotificationText.cs`
- Modify: `MainLayout.razor` (bell), `ClientServices.cs`, resources
- Test: `tests/Lms.Client.Tests/Layout/NotificationBellTests.cs`, `tests/Lms.Client.Tests/Pages/Notifications/NotificationsPageTests.cs`, `PreferencesPageTests.cs`

**Interfaces:**
- `INotificationsApi`: `GetPageAsync(bool unreadOnly, int page, int pageSize)`, `MarkReadAsync(Guid id)`, `MarkAllReadAsync()`, `GetPreferencesAsync()`, `SavePreferencesAsync(SetNotificationPreferencesRequest)`.
- `INotificationConnection { event Action<NotificationDto>? Received; Task StartAsync(); Task StopAsync(); }` — token via `IAccessTokenProvider` as `AccessTokenProvider`.
- `NotificationText.Format(NotificationDto n, IStringLocalizer l, CultureInfo c) : string` — template `Notif_{EventKey with . → _}` with `{Name}` parameters; `*Ar`/`*En` parameter pairs pick the current culture.
- Bell: unread badge, last 10 in a popover, click → mark read + navigate to `DeepLink`; snackbar on realtime arrival.
- Resource keys (en / ar): `Notif_Title` Notifications / الإشعارات; `Notif_MarkAllRead` Mark all as read / تعيين الكل كمقروء; `Notif_Empty` No notifications. / لا توجد إشعارات.; `Notif_Preferences` Notification preferences / تفضيلات الإشعارات; `Channel_InApp` In-app / داخل المنصة; `Channel_Email` Email / البريد الإلكتروني; `Channel_Sms` SMS / رسائل نصية; `Channel_Push` Push / إشعارات فورية; `Notif_Access_RoleAssigned` You were given the role {RoleName} at {OrgUnitName}. / تم منحك دور {RoleName} في {OrgUnitName}.; `Notif_Access_RoleRemoved` Your role {RoleName} at {OrgUnitName} was removed. / تمت إزالة دورك {RoleName} في {OrgUnitName}.; `Notif_Organization_PolicyChanged` A policy changed at {OrgUnitName}. / تم تغيير سياسة في {OrgUnitName}.; `NotifEvent_{Key}` short event names for the preferences grid.

- [ ] **Step 1: Write the failing tests** `Bell_shows_unread_count_and_updates_on_realtime_message`; `Click_marks_read_and_navigates_to_deep_link`; `Text_uses_arabic_parameters_in_arabic_culture`; `Every_catalog_event_has_ar_and_en_template`; `In_app_preference_switch_is_locked_on`; `Pages_render_rtl_in_arabic`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS; screenshots.
- [ ] **Step 5: Commit** `feat(client): notification center with realtime bell and preferences`.

### Task 35: M1 isolation and permission probe suite — lane: QA (QA2)

**PRD:** §4.2.3, FR-SEC-05, PRD §9.2. **Depends on:** Tasks 7–22 (grows as they merge; first PR after Task 9).

**Files:**
- Create: `tests/Lms.IntegrationTests/Security/M1Callers.cs` (seeds per test class: client A root admin, School1 admin, School2 admin, School1 teacher, client B admin, platform admin), `M1EndpointMatrixTests.cs`, `M1IsolationTests.cs`, `IsolationBypassMarkerTests.cs`

**Interfaces:**
- `M1EndpointMatrixTests`: table-driven `[Theory]` over every M1 route with the expected status per caller (e.g. School2 admin on a School1 unit → 404; teacher → 403; client B admin → 404; platform admin on `/api/clients` → 200, org admin → 403).
- `Every_M1_api_route_is_in_the_probe_matrix` — enumerates `EndpointDataSource` routes under `/api` (except M0 and `/api/me/*`) and fails if any is missing from the matrix (new endpoints cannot skip probes).
- `M1IsolationTests`: for each new table (`Notification`, `NotificationPreference`, `OrgTemplate`, `AcademicYear`, `Term`, `CalendarEvent`, plus `AuditEntry` path visibility): client A never sees client B; sibling never sees sibling; parent sees descendants (except user-level tables: user never sees another user's rows).
- `IsolationBypassMarkerTests.Every_IgnoreQueryFilters_call_has_the_marker_comment` — scans `src/**/*.cs`.

- [ ] **Step 1: Write the tests** for the merged endpoints; **Step 2: Run** → expected FAILs filed as `bug` issues (`sev:high` for any data leak) against the owning lane.
- [ ] **Step 3:** Extend the matrix with each later BE PR (QA review blocks a BE PR whose routes are missing).
- [ ] **Step 4: Run** → PASS on `main` at milestone end.
- [ ] **Step 5: Commit** `test(security): M1 endpoint permission matrix and isolation probes`.

### Task 36: E2E exit demo 1 — clients, invitation, level types, trees — lane: QA (QA1)

**PRD:** FR-ORG-01..05, FR-ORG-09, FR-SEC-02, PRD §9.1. **Depends on:** Tasks 6, 7, 8, 9, 11, 13, 24, 25, 26; M0 #14 seed, #15 E2E fixture.

**Files:**
- Create: `tests/Lms.E2ETests/Infrastructure/MailpitClient.cs` (`GET http://localhost:8025/api/v1/search?query=to:"{email}"`, extract the reset link), `tests/Lms.E2ETests/M1/ClientsAndTreesTests.cs`, `tests/Lms.E2ETests/M1/M1Names.cs` (unique run suffix so reruns do not collide)

**Interfaces / scenarios (each `[Theory]` with `"ar"` and `"en"`; Arabic asserts `dir="rtl"`):**
- `Platform_admin_creates_school_group_and_university_with_first_admins` — both PRD sample taxonomies.
- `Org_admin_sets_password_from_invitation_email_and_logs_in`.
- `School_admin_builds_level_types_and_tree` — School › Stage › Section; tree School → KG (transfer) → Section 1, 2; Primary (transfer) → Section 1..3; KG next level = Primary.
- `University_admin_builds_faculty_tree_and_moves_a_specialization` — University › Faculty › Year › Specialization (FCI → First → general, Soft, Bio; Second …), move "Bio" from First to Second and see it under Second.
- `Delete_unit_with_children_is_refused_with_message`.

- [ ] **Step 1: Write the E2E tests.** **Step 2: Run** with compose up and the API running → FAIL until the BE/FE tasks merge. **Step 3:** Fix test-side issues; file bugs for product issues. **Step 4: Run** → PASS in CI.
- [ ] **Step 5: Commit** `test(e2e): M1 clients, invitations and organization trees`.

### Task 37: E2E exit demo 2 — policies, settings, flags, copy/templates, roles, assignments — lane: QA (QA1)

**PRD:** FR-ORG-06/07/08/10/11, FR-SEC-06, §3.3, §4.4. **Depends on:** Task 36; Tasks 12, 14, 15, 17, 19, 21, 27, 28, 29, 30.

**Files:**
- Create: `tests/Lms.E2ETests/M1/ConfigurationTests.cs`, `tests/Lms.E2ETests/M1/AccessAdminTests.cs`

**Interfaces / scenarios (ar + en):**
- `Root_admin_sets_CreateOrgUnits_on_KG_and_KG_admin_sees_add_hidden_and_api_message` (assign a KG admin first).
- `Locked_root_setting_is_read_only_at_school`; `Feature_flag_enabled_at_KG_only`.
- `Copy_Primary_branch_with_suffix_and_save_as_template_with_Year_placeholder_then_apply_for_2027`.
- `Root_admin_creates_Exam_Officer_custom_role_and_renames_Manager_label`.
- `Admin_assigns_teacher_role_to_new_email_teacher_accepts_invitation_and_sees_only_section_context`.

- [ ] **Step 1–4** as Task 36. **Step 5: Commit** `test(e2e): M1 policies, settings, templates, roles and assignments`.

### Task 38: E2E exit demo 3 — years, calendar, selector, audit viewer, notifications — lane: QA (QA2)

**PRD:** FR-YEAR-01..03, X-04, FR-AUD-03/04, FR-NOT-02, FR-SEC-07. **Depends on:** Task 36; Tasks 18, 20, 22, 31, 32, 33, 34.

**Files:**
- Create: `tests/Lms.E2ETests/M1/YearsTests.cs`, `tests/Lms.E2ETests/M1/AuditAndNotificationsTests.cs`

**Interfaces / scenarios (ar + en):**
- `Root_admin_defines_2026_year_with_two_terms_and_a_holiday_school_inherits_it`; `Year_selector_defaults_to_active_year_and_switches`.
- `Audit_viewer_shows_unit_move_with_before_after_and_exports_csv_and_xlsx` (download event; CSV starts with BOM).
- `Assignee_sees_realtime_bell_notification_without_reload` (two browser contexts).
- `Deactivated_user_sees_blocked_message_on_login`.

- [ ] **Step 1–4** as Task 36. **Step 5: Commit** `test(e2e): M1 years, audit viewer and notifications`.

---

## Owner decisions (answered 2026-11-10; recorded in PRD as D16–D22)
All seven recommendations below were accepted as written: Q1 → D16, Q2 → D17, Q3 → D18, Q4 → D19, Q5 → D20, Q6 → D21, Q7 → D22. Q1 is applied to Task 1's matrix above.

1. **Does Manager get `Assignments.Create` (and `Delete`)?** PRD §3.4 gives Manager "Staff users: View, Create", and FR-STF-02 creates staff *with* role assignments, but M0 shipped Manager with `Assignments.View` only. *Recommendation:* yes — grant Manager `Assignments.Create` and `Assignments.Delete` in M1, relying on Task 21's escalation guard so a Manager can only assign roles whose permissions are a subset of their own (Teacher, Counselor, Supervisor — never OrgAdmin or Manager). This plan keeps the M0 state until the answer; a "yes" changes one list in Task 1 and the matrix test. Affects Tasks 1, 21, 37.
2. **Who may see academic years?** §3.4 has no row for academic years/terms/calendar. *Recommendation:* `AcademicYears.View` for all seven roles (everyone needs the year selector and holidays); Create/Update/Delete for Org Admin only (Manager view, like "Org tree, policies, settings"). Plan uses this. Affects Tasks 1, 20, 22.
3. **Client-wide edits only from the root?** PRD §3.2 says custom roles are created by "Org Admins at client root"; level types and built-in permission sets are silent. *Recommendation:* all three are root-only (they change the whole client). Plan uses this. Affects Tasks 8, 19.
4. **Delete rules beyond FR-ORG-04:** should a unit with live child units or live role assignments also be undeletable (FR-ORG-04 lists only classes/students/courses)? *Recommendation:* yes, block both (409) — soft-deleting a parent would otherwise leave orphaned children and people with a context nobody can see. Affects Task 9.
5. **Audit visibility inside a client:** may a school Manager see audit entries of the whole client or only of their subtree? *Recommendation:* only their subtree (consistent with §4.2); entries without a unit (logins, client-level changes) only to root-level viewers. Plan adds `OrgUnitPath` to audit entries for this. Affects Task 18.
6. **Assigning roles to people who have no account yet (before M2 Staff):** *Recommendation:* the assignment form takes an email; an unknown email creates a bare user and sends an invitation; there is no user search (so one client cannot browse another client's users). M2 replaces this with full staff profiles. Affects Task 21.
7. **"One active year per unit scope":** *Recommendation:* each unit may define its own year list (overriding its parent's entirely); at most one Active year per defining unit; a second Active year is refused until the first is closed (closing arrives with FR-YEAR-04 in M7). Affects Task 20.

## Execution notes for the Team Lead
- **Contract PRs first (Lead, `lead/m1-contracts-*`):** Task 1 before anything; Tasks 2, 3, 4 right after (each small; can be one day). Any later DTO change goes through a Lead contract PR, not a BE/FE PR.
- **Prerequisites from M0:** open PRs #25, #26, #27, #29, #31, #30, #32 and seed #14 / E2E fixture #15 must be merged before M1 BE work starts (Task 5 needs #27; Task 6 needs #31; Task 21 needs #30).
- **BE1 (Organization & Access folders: `Features/{Clients,OrgUnits,Branches,Templates,Roles,Assignments}`, `Infrastructure/{Organizations,Branches,Access}`):** 5 → 7 → 9 → 11 → 13 → 15 → 17 → 19 → 21.
- **BE2 (Account & Configuration folders: `Pages/Account`, `Features/{Account,LevelTypes,Notifications,Policies,Settings,Audit,AcademicYears}`, `Infrastructure/{Email,Identity,Notifications,Configuration,Auditing,AcademicYears}`):** 6 → 8 → 10 → 12 → 14 → 16 → 18 → 20 → 22. The two BE devs never edit the same file except `Program.cs` (two lines per module) and `EndpointCoverageTests.cs` (BE2 only adds exemptions). `OrgUnit.cs` is BE1-only; `LevelType.cs`, `AuditEntry.cs`, `UserStatus.cs` are BE2-only.
- **Migrations:** Tasks 7, 10, 16, 17, 18, 20, 22 each add exactly one; whoever merges second regenerates on top of `main`.
- **FE1:** 23 → 24 → 25 → 26 → 27 → 28. **FE2 (owner approved a second FE agent):** 29 → 30 → 31 → 32 → 33 → 34 (FE2 starts after Task 23 merges). Each starts on fakes as soon as its contract merges. FE2's tasks are (they touch separate folders: `Pages/Access`, `Pages/Audit`, `Pages/AcademicYears`, `Pages/Notifications`); shared files are `NavCatalog.cs`, `ClientServices.cs`, `MainLayout.razor` and the two resx files — serialize merges of those.
- **QA:** QA1 reviews BE1/FE PRs and owns Tasks 36–37; QA2 reviews BE2 PRs and owns Tasks 35 and 38. Every review checks the Review Focus items, the `ISOLATION-BYPASS` marker, the explicit-permission rule, the resource parity test, and RTL screenshots.
- **Splits made from DEV-PLAN §2 M1:** item 3 → Tasks 9 + 11 (BE) and 25 + 26 (FE); item 7 → Tasks 15 + 17 (BE); item 12 → Tasks 20 + 22 (BE) and 32 + 33 (FE); item 13 → Tasks 10 + 16 (BE). Items 1+2 share FE Task 24 and items 5+6 share FE Task 27; item 4's FE lives in Task 26. Item 10 has no FE task (server-rendered account pages).
- **Exit demo:** Tasks 36–38 green in CI in ar and en = M1 done; then update `docs/STATUS.md` and write the M2 plan.
