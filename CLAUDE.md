# LMS — shared rules for every agent

Multi-tenant LMS for school groups and universities. **Read before any work:**
- `docs/PRD.md` — what we build (requirement ids FR-*, X-*, decisions D1–D14)
- `docs/DEV-PLAN.md` — milestones, team workflow, Definition of Done
- `docs/superpowers/plans/` — the detailed plan of the current milestone
- `docs/STATUS.md` — who is doing what right now
- Your role file in `.claude/agents/` (team-lead, backend-dev, frontend-dev, qa-engineer, ui-ux-designer)

## Stack
.NET 10 · ASP.NET Core minimal APIs · Blazor WebAssembly + MudBlazor · EF Core + MySQL 8.4 · MinIO · ASP.NET Identity + OpenIddict · Hangfire · SignalR · xUnit, Testcontainers, bUnit, Playwright.

## Layout
- `src/Lms.Domain` entities + abstractions (no infrastructure references)
- `src/Lms.Infrastructure` EF Core, MinIO, external providers, jobs
- `src/Lms.Api/Features/<Module>/` endpoints + handlers + validators, one folder per module
- `src/Lms.Shared` contracts (DTOs, routes, permission constants) — **changed only through Team Lead contract PRs**
- `src/Lms.Client/Pages/<Module>/` Blazor pages; `Resources/SharedResource.{ar,en}.resx`
- `tests/Lms.UnitTests`, `tests/Lms.IntegrationTests`, `tests/Lms.Client.Tests`, `tests/Lms.E2ETests`

## Non-negotiable rules
1. **Isolation:** tenant-owned tables carry `OrganizationId`; unit-scoped tables also `OrgUnitId` + `OrgUnitPath`. Never bypass global query filters (`IgnoreQueryFilters`) except in code reviewed by QA with a comment explaining why. Every new table gets an isolation test.
2. **Authorization:** every endpoint calls `.RequirePermission(Permissions.X.Y)` or `IAccessEvaluator`; never rely on the UI hiding things.
3. **TDD:** write the failing test first, then the code. No PR without tests.
4. **Bilingual:** no hard-coded UI text; add both `ar` and `en` resource entries; check the page in RTL. Business names are stored as `NameAr` / `NameEn`.
5. **Soft delete** for business entities; audit sensitive entities with `[Audited]`.
6. **Secrets** never committed; dev-only values live in `appsettings.Development.json`.
7. Warnings are errors; keep files focused (split a file that passes ~300 lines).

## Git & GitHub
- Repo: `ziadtorkey/LMS_Repo`. `gh` is at `C:\Program Files\GitHub CLI\gh.exe` if not on PATH.
- Branches: `be/<issue#>-slug`, `fe/<issue#>-slug`, `qa/<issue#>-slug`, `ux/<issue#>-slug`, `lead/<topic>`. Never push to `main` directly (Team Lead docs/contract commits excepted).
- Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`), and end every commit message with:
  `Co-Authored-By: Claude <noreply@anthropic.com>`
- PRs: use the template, `Closes #<issue>`, list PRD ids, CI must be green. Only the Team Lead merges (squash), and only with label `qa:approved`.
- Labels: `lane:be|fe|qa|ux|lead`, `area:<module>`, `M0`…`M7`, `bug`, `sev:critical|high|medium|low`, `blocked`, `needs-owner`, `qa:approved`, `qa:changes-requested`.
- Talk on GitHub (issue/PR comments) so the product owner can follow; use session messages only for quick pings.

## Local run
```
docker compose up -d
dotnet run --project src/Lms.Api
dotnet test LMS.slnx
```

## Asking the product owner
Only the Team Lead asks the product owner, using the popup question tool (AskUserQuestion) and/or an issue labelled `needs-owner`. Other agents raise questions to the Team Lead.
