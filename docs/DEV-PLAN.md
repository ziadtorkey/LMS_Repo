# LMS v1 — Development Plan (master)

| | |
|---|---|
| Spec | `docs/PRD.md` (approved 2026-11-10) |
| Team | Team Lead (Opus), Backend (Sonnet), Frontend (Sonnet), QA (Sonnet) — Lead may add extra BE/FE/QA agents |
| Tracking | GitHub Issues + Milestones + Project board "LMS v1" on `ziadtorkey/LMS_Repo` |
| Status | `docs/STATUS.md` (updated by the Lead after every merge cycle) |

This document is the **map**. Each milestone gets a **detailed TDD plan** in `docs/superpowers/plans/` written by the Team Lead (using the `superpowers:writing-plans` skill) *before* its issues are opened. Milestone 0's detailed plan is already written: `docs/superpowers/plans/2026-11-10-m0-foundation.md`.

---

## 1. Milestones

Each milestone ends with a demo-able, fully tested increment merged to `main`.

| # | Milestone | PRD coverage | Depends on | Exit demo |
|---|---|---|---|---|
| M0 | **Foundation** | §1.3, §4.2, §4.3 engine, X-01..X-03, X-05, X-08, X-11, FR-SEC-01/03/04/05 | — | Log in, pick a context, see an Arabic RTL shell whose menu reflects permissions; isolation tests green. |
| M1 | **Organization & Access** | FR-ORG-*, FR-YEAR-01..03, FR-SEC-02/06/07, FR-AUD-*, §4.4, FR-NOT-01/02 (in-app core) | M0 | Platform Admin creates the sample school group and university; admins build trees, policies, settings, roles, years; audit viewer works. |
| M2 | **People** | FR-STF-*, X-06 import framework, FR-SEC-08 (OIDC/Azure AD); student/guardian data model (D23) | M1 | Staff created, shared and imported from Excel; expiry job runs; external sign-in works. |
| M3 | **Academic Structure** | FR-CRS-*, FR-LP-*, FR-CLS-*, FR-GRP-*, FR-ENR-*, FR-TCH-*, FR-STU-01..06 (from M2, D23) | M2 | Courses allocated in the tree, blueprint published, classes created, students auto-enrolled, teachers assigned, groups formed, a transfer performed. |
| M4 | **Teaching & Learning** | FR-CNT-*, FR-ASG-*, FR-QZ-*, FR-LIB-*, FR-FOR-*, FR-VC-*, FR-STD-* | M3 | Teacher builds lessons with every module type; student completes content, submits assignment, takes quiz. |
| M5 | **Assessment, Timetable & Attendance** | FR-GRD-*, FR-TT-*, FR-ATT-*, FR-EXC-* | M4 | Gradebook with weights and publishing; timetable published; attendance generated and taken; excuse workflow end to end. |
| M6 | **Communication** | FR-NOT-03, FR-MSG-* | M1 (can run in parallel with M3–M5 once M2 is done) | Email/SMS/push delivery, messaging with rules, targeted announcements, moderation. |
| M7 | **Year-End & Release** | FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06, SAML/LDAP part of FR-SEC-08, X-09, X-10, PRD §9 | M5, M6 | Full PRD §9 acceptance suite green in ar + en; performance and security review signed off. |

Parallelism: within a milestone, BE and FE lanes run in parallel per feature (contract-first, see §3). Across milestones, M6 may start in parallel with M3 once M2 is merged, staffed by an extra BE/FE pair if the Lead adds agents.

---

## 2. Issue breakdown per milestone

Each line becomes one GitHub issue per lane (`[BE]`, `[FE]`, `[QA]`), labelled `milestone`, `area:*`, `lane:*`. The detailed milestone plan adds files, interfaces and tests per issue.

### M0 Foundation — see detailed plan (12 tasks)

### M1 Organization & Access
1. Platform Admin: clients CRUD + first Org Admin (FR-ORG-01)
2. Level types CRUD (FR-ORG-02)
3. OrgUnit CRUD, tree view, move, delete rules (FR-ORG-03/04)
4. Node settings page: Level Transfer flag + path, course-management level (FR-ORG-05/09)
5. Policies catalog, control panel, effective-rules view (FR-ORG-10) — engine from M0
6. Settings catalog UI with lock + feature flags (FR-ORG-11, §4.4)
7. Copy branch / templates / apply template with placeholders (FR-ORG-06/07/08)
8. Roles admin: edit built-in permission sets, custom roles (FR-SEC-06)
9. Role assignments UI (assign/remove per unit, validity dates)
10. Forgot/reset password; blocked login messages (FR-SEC-02/07)
11. Audit viewer + export (FR-AUD-03/04)
12. Academic years, terms, calendar (FR-YEAR-01..03) + global year selector (X-04)
13. Notification core: event catalog, in-app center, SignalR (FR-NOT-01/02)

### M2 People (re-scoped by the owner 2026-11-11, D23: students move to M3)
1. Import framework: template → upload → validate → preview → all-or-nothing commit → error report (X-06), Hangfire job (jobs kernel delivered in M1, #144)
2. Staff CRUD + duplicate checks + activation email (FR-STF-01..03)
3. Staff import (FR-STF-04)
4. Share user across units (FR-STF-05)
5. Staff expiry job + pre-expiry notification (FR-STF-06)
6. People kernel incl. the guardian/student data model (groundwork for M3)
7. External identity providers: config UI + OIDC/Azure AD (FR-SEC-08 part 1)

### M3 Academic Structure
0. (from M2, D23) Student CRUD + duplicate detection + code generator (FR-STU-01..03), self-edit + change requests (FR-STU-04), guardians linking/portal (FR-STU-06), student import incl. guardians (FR-STU-05); see the M2 plan appendix "Deferred to M3"
1. Courses CRUD + import (FR-CRS-01)
2. Course allocations in the tree (FR-CRS-02)
3. Course restrictions + exceptional enrollment approval (FR-CRS-03)
4. Course supervisors + "My courses" (FR-CRS-04)
5. Blueprint model, versioning, builder UI, copy (FR-LP-01/02)
6. Classes CRUD, bulk create, copy to next year, close (FR-CLS-01..04)
7. ClassCourses + distribution matrix (FR-CLS-05)
8. Class lesson plan copy + lock + publish propagation choice (FR-LP-03/04/05)
9. Teacher eligibility + ClassCourse teacher assignment + history (FR-TCH-01..03)
10. Enrollment engine: auto, manual optional, unenroll, views (FR-ENR-01/02/04)
11. Manual/bulk add to class incl. Excel (FR-ENR-03)
12. Groups per ClassCourse with one-group rule (FR-GRP-01..03)
13. Transfer between classes (FR-ENR-05)

### M4 Teaching & Learning
1. Asset service UI (upload, preview, signed streaming) (FR-CNT-03)
2. Lesson modules: types, metadata, visibility, targeting, ordering (FR-CNT-01/02)
3. Student content view + completion/progress (FR-CNT-04)
4. Assignments: create, publish, submit, grade, late policy, notifications (FR-ASG-*)
5. Quizzes: builder, settings, attempt, auto/manual grading (FR-QZ-*)
6. Digital library (FR-LIB-01)
7. Forums + course announcements (FR-FOR-01)
8. Virtual classroom: provider abstraction + Zoom (FR-VC-01)
9. SCORM player + tracking (FR-STD-01)
10. H5P (FR-STD-02)
11. LTI 1.3 launch + grade passback (FR-STD-03)

### M5 Assessment, Timetable & Attendance
1. Grading scales + templates (FR-GRD-02)
2. Gradebook model, categories, weights, calculation engine (FR-GRD-01/03)
3. Gradebook grid, student view, reports (FR-GRD-04)
4. Grade publishing + notifications (FR-GRD-05)
5. Periods + working days (FR-TT-01)
6. Timetable model + builder + conflicts + views + export + copy (FR-TT-02..04)
7. Substitutes / temporary changes (FR-TT-05)
8. Attendance sessions from timetable + taking attendance + summaries (FR-TT-06, FR-ATT-01)
9. Absence notifications + excuse workflow with escalation + threshold alerts (FR-EXC-*)

### M6 Communication
1. Email channel (MailKit, queue, retries) + preferences (FR-NOT-03)
2. SMS provider abstraction + one provider; Web Push (FR-NOT-03)
3. Messaging: conversations, rules, UI, receipts (FR-MSG-01/02)
4. Announcements targeting + scheduling (FR-MSG-03)
5. Moderation & reporting (FR-MSG-04)

### M7 Year-End & Release
1. Close year (locks) + grade change approval (FR-YEAR-04, FR-GRD-06)
2. Promotion run with preview, execute job, undo window (FR-STU-07)
3. Withdrawal / graduation / archive / re-admission (FR-STU-08)
4. Open new year from copy/template (FR-YEAR-05)
5. SAML 2.0 + LDAP providers (FR-SEC-08 part 2)
6. Performance pass on seeded 50k-student client (X-09)
7. Accessibility pass (X-10) + security review (X-08)
8. PRD §9 acceptance E2E suite in ar + en

---

## 3. How the team works

### 3.1 Roles
| Agent | Model | Workspace | Owns |
|---|---|---|---|
| Team Lead | Opus | main checkout `D:\AI\LMS` | Milestone plans, issues, API contracts (`Lms.Shared`), assignment of work, merging, `docs/STATUS.md`, ADRs, unblocking |
| Backend | Sonnet | own git worktree | `Lms.Api`, `Lms.Domain`, `Lms.Infrastructure`, migrations, unit + integration tests |
| Frontend | Sonnet | own git worktree | `Lms.Client`, resources (ar/en), bUnit tests |
| QA | Sonnet | own git worktree | PR reviews, isolation/permission tests, Playwright E2E (`tests/Lms.E2ETests`), bug issues |

Agent definitions: `.claude/agents/*.md`. Shared rules: `CLAUDE.md`.

### 3.2 Feature flow (contract-first)
1. **Lead** writes the milestone plan, opens issues (BE/FE/QA per feature) with acceptance criteria copied from the PRD.
2. **Lead** opens a small **contract PR** into `main`: DTOs, request/response records, endpoint route constants and permission constants in `Lms.Shared`. Merged quickly.
3. **Backend** and **Frontend** work in parallel on their issues on branches `be/<issue#>-slug` and `fe/<issue#>-slug`. FE uses a fake `I<Module>Api` implementation until BE merges.
4. Each opens a PR with `Closes #<issue>`; CI must be green.
5. **QA** reviews against the acceptance criteria, runs the isolation/permission checklist, adds E2E tests in a `qa/<issue#>-slug` PR, and posts *Approve* or *Request changes* as a PR comment (one GitHub account — approvals are recorded as comments with the `qa:approved` label).
6. **Lead** merges (squash) when CI is green and the PR has `qa:approved`, then updates `docs/STATUS.md`.
7. Bugs found by QA → new issue labelled `bug` + severity, assigned to the owning lane.

### 3.3 Communication between agents
- Durable conversation lives **on GitHub** (issue and PR comments), so you can read it later.
- Quick pings use session messages (`SendMessage` to the agent's `local_…` session id; ids are listed in `docs/STATUS.md`).
- An agent that is blocked comments on the issue with `blocked:` + reason, adds label `blocked`, and messages the Lead.
- Decisions that change the PRD are escalated by the Lead to the product owner (you) — never decided silently.

### 3.4 Definition of Done (every PR)
- Acceptance criteria of the issue met; PRD requirement ids referenced in the PR body.
- Tests written first (TDD) and passing: unit/integration (BE), bUnit (FE), E2E where the issue says so (QA).
- Isolation: any new tenant-owned table carries `OrganizationId` (+ `OrgUnitId`, `OrgUnitPath` if unit-scoped) and has an isolation test.
- Every endpoint declares its permission; no UI-only authorization.
- All UI strings in ar + en resources; screen checked in RTL.
- No new warnings; migrations included and reversible.
- `docs/STATUS.md` updated by the Lead after merge.

### 3.5 Monitoring (for the product owner)
| Where | What you see |
|---|---|
| Claude app sidebar | The four live sessions — open any to watch or steer it |
| GitHub Project "LMS v1" | Board: Todo → In Progress → In Review → Done, per milestone |
| `docs/STATUS.md` | Lead's summary: milestone progress, in-flight work per agent, blockers, decisions needed from you |
| Pull requests | Code, CI results, QA review comments |
| Issues labelled `needs-owner` | Questions waiting for you |

---

## 4. Branching, CI and environments
- `main` is always releasable; no direct pushes except the Lead's contract and docs commits.
- Branch names: `be/`, `fe/`, `qa/`, `lead/` + `<issue#>-slug`. Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- CI (`.github/workflows/ci.yml`): restore → build (warnings as errors) → unit → integration (Testcontainers MySQL + MinIO) → bUnit → E2E (docker compose up, Playwright). Required for merge.
- Local dev: `docker compose up -d` (mysql, minio, mailpit) then `dotnet run --project src/Lms.Api`.
