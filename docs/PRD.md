# LMS — Product Requirements Document (v1)

| | |
|---|---|
| Status | Draft for review |
| Date | 2026-11-10 |
| Source | `LMS Backlog.xlsx` (Backlog, Features, Sample Tables, Lesson Plan, School/University Taxonomy sheets) |
| Repo | https://github.com/ziadtorkey/LMS_Repo |

---

## 1. Overview

A multi-tenant Learning Management System. The first customer is a **school group**; the same product must serve **universities** without code changes, by configuring the organization tree, level types and role labels.

### 1.1 Goals (v1)
1. One platform hosting many clients, each fully isolated, each with its own organization tree.
2. Inside a client, every unit (school / faculty) sees only its own subtree; higher levels see everything below them.
3. Complete academic cycle: structure → people → courses → enrollment → teaching content → assessment → grading → attendance → timetable → communication → year-end promotion.
4. Bilingual Arabic / English UI with full RTL support.
5. Built by a team of AI agents (see `docs/DEV-PLAN.md`) with automated tests as the quality gate.

### 1.2 Out of scope for v1
| Item | Backlog ref | Planned |
|---|---|---|
| All AI features (platform, teacher, student, insights) | 16–19 | Phase 2 |
| External exam cycle (EB / CES / PH integration) | 15 | Later — company systems, separate integration project |
| Native mobile apps | — | Later phase (v1 is responsive web) |
| Billing / subscription / self-service client signup | — | Later |
| Plagiarism / AI-content checks | 19.4 | Phase 2 (with AI) |

### 1.3 Technology (approved)
.NET 10 · ASP.NET Core API · **Blazor WebAssembly** (standalone SPA served by the API) · MudBlazor (+ other free/OSS libraries where they improve UX) · **MySQL 8.4** (EF Core, Pomelo provider) · **MinIO** (S3-compatible file storage) · ASP.NET Identity + **OpenIddict** · Hangfire (background jobs) · SignalR (realtime) · ClosedXML (Excel) · Docker Compose (dev) · GitHub Actions (CI) · xUnit + Testcontainers + bUnit + Playwright (tests).

---

## 2. Glossary

| Term | Meaning |
|---|---|
| **Organization (Client)** | A customer, e.g. "Altagamo3 Schools" or "Fayoum University". Its id is `OrganizationId`, stamped on every tenant-owned row. Clients never see each other. |
| **Level Type** | A client-defined tier in its tree, e.g. School › Stage › Grade, or Faculty › Year › Specialization. |
| **OrgUnit** | A node in the client's tree (a school, a faculty, KG, Primary, Section 1…). |
| **Class** | A teaching group for one academic year attached to an OrgUnit, e.g. "Class 1 2026" (school) or "Sec 1 2026" (university). |
| **Course** | A subject, e.g. Arabic, Math, Islamic Religion. |
| **Course Allocation** | A course attached to an OrgUnit; classes beneath inherit it. |
| **ClassCourse** | A course taught in a specific class (the "course section"). Teachers, enrollments, content, grades and attendance hang off it. |
| **Blueprint** | The supervisor-owned, versioned lesson plan of a course. |
| **Class Lesson Plan** | A class's locked copy of the blueprint, on which teachers add content and activities. |
| **Lesson Module** | A content item or activity (file, video, page, quiz, assignment, live session, SCORM/H5P/LTI, library resource) placed in a lesson. |
| **Level Transfer** | Flag on an OrgUnit marking a promotion level (KG → Primary, Year 1 → Year 2). |
| **Policy** | A restriction a parent unit imposes on its subtree (e.g. "cannot create courses"). |
| **Setting** | An inheritable configuration value (e.g. guardian data mandatory, grading scale). |

---

## 3. Roles and Permissions

### 3.1 Built-in roles
| Role | School label | University label | Typical scope |
|---|---|---|---|
| Platform Admin | — | — | Above all clients (operator). Creates clients and their first Org Admin. |
| Org Admin | Admin | Admin | Any OrgUnit; at the client root = client super admin |
| Manager | School Manager | Dean / Vice Dean | School / Faculty |
| Supervisor | Academic Supervisor | Head of Dept / Course Coordinator | OrgUnit + supervised courses |
| Counselor | Social Worker | Student Affairs | School / Faculty |
| Teacher | Teacher | Instructor / TA | OrgUnit + assigned ClassCourses |
| Student | Student | Student | Own class |
| Guardian | Parent | Parent (can be disabled per client) | Linked children |

Labels are defaults per client type and are renameable per client (Arabic and English).

### 3.2 Custom roles
Org Admins at client root may create **custom roles** (e.g. "Exam Officer") by picking permissions from the permission catalog. A custom role has a *base kind* (Staff / Student / Guardian) that determines which relationship rules apply. Built-in roles cannot be deleted; their permission sets can be edited per client.

### 3.3 Permission model
- **Permission** = `Module.Action` string from a fixed catalog (e.g. `Courses.Create`, `Grades.Publish`, `Excuses.Approve`, `Audit.Export`). Actions: View, Create, Update, Delete, Approve, Import, Export, plus module-specific ones.
- **Role assignment** = (User, Role, OrgUnit, optional ValidFrom/ValidTo). Applies to that unit **and all descendants**.
- A user can hold several assignments in different units (and in different clients). After login, a user with more than one assignment picks the **active context** (organization chooser); they can switch later without logging out.
- **Authorization decision** for an action on a resource:
  1. The active assignment's role grants the permission, **and** the resource's OrgUnit is the assignment's unit or a descendant;
  2. **and** the relationship rule passes (Teacher → only own ClassCourses; Student → own records; Guardian → linked children; Supervisor → blueprints of supervised courses);
  3. **and** no Policy on the resource's unit or any ancestor blocks the action.
- The UI builds menus and buttons from the effective permissions; the API enforces the same check on every endpoint (never UI-only).

### 3.4 Default permission matrix (summary; full matrix seeded in code)
| Module | Org Admin | Manager | Supervisor | Counselor | Teacher | Student | Guardian |
|---|---|---|---|---|---|---|---|
| Org tree, policies, settings | CRUD | View | View | – | – | – | – |
| Staff users | CRUD, Import | View, Create | View | View | – | – | – |
| Students & guardians | CRUD, Import | CRUD | View | View, Update | View (own classes) | Self (non-critical) | Own children (view) |
| Classes, groups | CRUD | CRUD | View | View | Groups (own) | – | – |
| Courses, allocations, restrictions | CRUD | CRUD | CRUD (supervised) | – | View | – | – |
| Blueprints | View | View | CRUD (supervised) | – | View | – | – |
| Enrollment, transfers, promotion | CRUD | CRUD, Approve | View | View | View | – | – |
| Teacher assignment | CRUD | CRUD | CRUD | – | View own | – | – |
| Content & activities | View | View | View | – | CRUD (own) | View/Submit | – |
| Gradebook | View | View | View, Export | View | CRUD, Publish (own) | Own published | Children published |
| Attendance | View | View | View | View, Update | Take (own) | Own | Children |
| Excuses | View | Approve (escalated) | – | Approve | View | Submit | Submit |
| Timetable | View | CRUD | CRUD | View | Own | Own | Children |
| Messaging / announcements | Moderate | Announce | Announce | Message | Message, Announce (own courses) | Message (rules) | Message (rules) |
| Audit log | View, Export | View | – | – | – | – | – |

---

## 4. Organization Structure and Tenancy

### 4.1 Model
- `Organization`: Id, Name (ar/en), Type (SchoolGroup | University), Status, logo, default language, storage bucket, root OrgUnit.
- `LevelType`: per Organization, ordered list (Name ar/en, Order, flags such as `CanHostClasses`).
- `OrgUnit`: Id, OrganizationId, ParentId, LevelTypeId, Name (ar/en), Code, **Path** (materialized, e.g. `/1/5/9/`), Depth, Status (Active/Inactive/Deleted), `IsLevelTransfer`, `NextTransferUnitId`, `IsCourseManagementLevel`.

### 4.2 Data isolation (non-negotiable)
1. **Client isolation:** every tenant-owned table has `OrganizationId`; EF Core global query filter restricts all queries to the current client. Cross-client access is impossible except for Platform Admin endpoints.
2. **Subtree isolation:** every unit-scoped table stores `OrgUnitId` and `OrgUnitPath`; a second global filter restricts rows to `OrgUnitPath LIKE '<active assignment path>%'`. School 1 cannot see School 2; Medicine cannot see Engineering; the group / university root sees all.
3. Automated tests must prove both isolation rules for every module (QA gate).

### 4.3 Settings and policies (one inheritance engine)
- A **Setting** resolves to the value on the nearest unit (self or ancestor) that defines it; client root holds defaults.
- A parent may **lock** a setting — descendants cannot override it.
- A **Policy** (catalog: CreateCourses, CreateClasses, CreateOrgUnits, CreateStaff, CreateAdmins, CreateStudents, CreateTeachers, …) blocks actions in the subtree. Children can add restrictions, never remove a parent's.
- "Effective settings / policies" screen per unit shows each value and **where it is inherited from**.

### 4.4 Feature flags ("premium features")
A catalog of feature flags (e.g. VirtualClasses, DigitalLibrary, ScormContent, Messaging, GuardianPortal) enabled per OrgUnit (inherited) and optionally per student. No billing in v1.

---

## 5. Cross-Cutting Requirements

| ID | Requirement |
|---|---|
| X-01 | **Localization:** all UI text in resource files (ar, en). User selects language; Arabic switches the layout to RTL. Dates support Gregorian (Hijri display optional setting). Business names stored bilingual (`NameAr`, `NameEn`). |
| X-02 | **Audit log:** every create/update/delete on audited entities (profiles, roles, grades, enrollment, attendance, policies, settings) and security events (login, failed login, role change) is recorded: who, active role/unit, action, entity, before/after JSON, timestamp, IP. |
| X-03 | **Soft delete** everywhere for business entities; history is never lost. Hard delete only via retention jobs (not v1). |
| X-04 | **Academic year context:** a global year selector; lists default to the current year; closed years are read-only. |
| X-05 | **Files:** stored in MinIO, one bucket per client, path includes OrgUnit; access via short-lived signed URLs after permission check; configurable allowed types and max size per client. |
| X-06 | **Excel import** (staff, students, enrollment, courses): downloadable template → upload → validation → preview of all rows with errors → **all-or-nothing commit** (any invalid row blocks the file; user downloads the error report, fixes, re-uploads). Runs as a background job for large files. |
| X-07 | **Notifications:** every domain event goes through one notification service (in-app always; email/SMS/push per event & user preference). |
| X-08 | **Security:** OWASP ASVS L2 baseline; password policy; lockout; HTTPS only; rate limiting on auth; secrets never in repo. |
| X-09 | **Performance:** list pages < 2 s at p95 with 50,000 students per client; all lists server-paged, filterable, sortable, exportable to Excel. |
| X-10 | **Accessibility:** WCAG 2.1 AA for core flows; keyboard navigable; responsive down to 360 px width. |
| X-11 | **Observability:** structured logging (Serilog), health checks, correlation ids. |

---

## 6. Functional Requirements

Requirement ids map to backlog ids (B-x) so issues can be traced to the original sheet.

### 6.1 Identity and Security (B-1, B-1.2, B-4.2, B-5.3)
- **FR-SEC-01** Login with username/email + password; tokens via OpenIddict (authorization code + PKCE for the WASM client).
- **FR-SEC-02** Forgot/reset password by email link with expiry.
- **FR-SEC-03** Organization/context chooser after login when the user has more than one assignment; switch context from the header.
- **FR-SEC-04** Dynamic menu: only links the active context permits.
- **FR-SEC-05** API authorization guard on every endpoint (permission + scope + relationship + policy), as §3.3.
- **FR-SEC-06** Role & permission admin: edit built-in role permission sets per client; create/edit/delete custom roles.
- **FR-SEC-07** Blocked login message for expired / deactivated / withdrawn users.
- **FR-SEC-08** External identity providers (Azure AD, Windows AD/LDAP, SAML 2.0, OpenID Connect) configurable per client; users matched by email/national id; first login links the account. *(Provider config UI + OIDC/Azure AD in v1; SAML and LDAP behind the same abstraction, last in the plan.)*
- **FR-AUD-01..04** Audit events catalog; audit capture; viewer (filter by user, entity, date; before/after diff); export to Excel/CSV.

### 6.2 Organization (B-2, B-2.1, B-2.2, B-3, B-14)
- **FR-ORG-01** Platform Admin creates a client (type, names, first Org Admin).
- **FR-ORG-02** Level types CRUD per client (ordered).
- **FR-ORG-03** OrgUnit CRUD under a parent; expandable tree view with search; move node (re-computes paths, admin only).
- **FR-ORG-04** Delete rules: block if the node has classes/students/courses; otherwise soft delete; deactivate always allowed.
- **FR-ORG-05** Node settings page hosting Level Transfer flag, transfer path, course-management level, settings, policies.
- **FR-ORG-06** Copy branch (structure only, or with courses/allocations/policies/settings).
- **FR-ORG-07** Save branch as named template; template library (list, preview, delete); visibility private-to-unit or shared across the client.
- **FR-ORG-08** Apply template under a chosen unit with placeholders (`{Year}`, `{Name}` → "Class 1 2026").
- **FR-ORG-09** Level Transfer flag + mapping of each transfer level to its next level.
- **FR-ORG-10** Policies catalog, control panel on unit create/edit, inheritance, API enforcement with clear message, UI hiding, effective-rules view (§4.3).
- **FR-ORG-11** Settings catalog with inheritance and lock (§4.3), feature flags (§4.4).
- **FR-YEAR-01** Academic year per client (inherited, overridable per unit): name, start/end, status Planned/Active/Closed; one active year per unit scope.
- **FR-YEAR-02** Terms inside a year: ordered, non-overlapping, within the year range; current term.
- **FR-YEAR-03** Academic calendar: holidays and events (excluded from timetable/attendance).
- **FR-YEAR-04** Close year: locks grades/attendance read-only and opens the promotion workflow.
- **FR-YEAR-05** Open new year: copy class structure from last year and/or apply a taxonomy template.

### 6.3 Staff (B-4, B-4.1, B-4.3, B-4.4)
- **FR-STF-01** Staff profile: name (ar/en), email, mobile, national id, job title, photo, status, home unit, optional expiry date.
- **FR-STF-02** Create staff with role assignments; duplicate check on email / mobile / national id; activation email with set-password link.
- **FR-STF-03** List with filters (unit, role, status), profile page, edit, soft delete, deactivate/reactivate with reason (history kept).
- **FR-STF-04** Excel import per X-06 (name, email, mobile, national id, role, unit, expiry).
- **FR-STF-05** Share user across units: owner picks user, target units and role in each; user appears there with that role.
- **FR-STF-06** Daily job auto-deactivates staff after expiry; admins notified N days before expiry (setting).

### 6.4 Students and Guardians (B-5.x)
- **FR-STU-01** Student profile: name (ar/en), DOB, gender, religion, nationality, national id, photo, student code, status, feature flags.
- **FR-STU-02** Create student with class placement; duplicate detection (national id, or name + DOB).
- **FR-STU-03** Student code generation: unique per client, configurable pattern (e.g. `{SchoolCode}{Year}{Seq:5}`), auto or manual.
- **FR-STU-04** Field classification setting: non-critical fields (photo, nickname, mobile) self-editable; critical fields (name, DOB, national id, code) require a change request → approval queue → apply + notify.
- **FR-STU-05** Excel import per X-06 (student data, class, guardian data; guardian rules per setting).
- **FR-STU-06** Guardian: data model (name, relation, mobile, email, national id); multiple guardians per student with one primary; one guardian for many siblings; guardian portal account; setting "guardian data mandatory/optional" (inherited) enforced on create and import.
- **FR-STU-07** Promotion run (year end): choose source year/unit and target year (uses Level Transfer path); per student outcome promote / repeat / graduate / transfer-out; preview; execute as a job creating target ClassStudents and closing old ones; **undo available for a short window** (configurable, default 10 minutes) and only before any activity in the new year; history stays readable.
- **FR-STU-08** Withdrawal / graduation with reason and effective date → inactive, login blocked, removed from active lists, history retained; archive search (read-only); re-admission.

### 6.5 Classes and Groups (B-6.x)
- **FR-CLS-01** Class: name (ar/en), OrgUnit, academic year, capacity, homeroom teacher (optional), status.
- **FR-CLS-02** CRUD, list/search (unit, year, status), capacity warning (setting: warn or block).
- **FR-CLS-03** Class view: students, teachers, courses, timetable.
- **FR-CLS-04** Bulk create classes; copy last year's classes to the new year; close/archive at year end.
- **FR-CLS-05** ClassCourses: available courses are those allocated to the class's unit or its ancestors; select/bulk-assign to all classes in a unit; block removal if enrollments or grades exist; distribution matrix (classes × courses).
- **FR-GRP-01** Student groups per ClassCourse (e.g. Religion → "Islamic", "Christian"); CRUD by Admin/Teacher; add/remove members (bulk); suggestion of members by course restriction.
- **FR-GRP-02** Only students enrolled in the ClassCourse can be grouped; **a student belongs to at most one group per ClassCourse**.
- **FR-GRP-03** Content, assignments, quizzes and announcements can target a group.

### 6.6 Courses and Lesson Plans (B-7.x, B-6.3)
- **FR-CRS-01** Course: code, name (ar/en), description, language, weekly periods, owner unit, status; CRUD; list/search; activate/deactivate; block delete if used; import from Excel. Create permission: Manager, Supervisor, or as granted, subject to `CreateCourses` policy.
- **FR-CRS-02** Course allocation: choose an OrgUnit and courses; classes beneath inherit; courses view per node; block removal if used by classes. A course can also be allocated at creation time.
- **FR-CRS-03** Course restrictions: catalog of restriction types (religion, gender, level, custom attribute); set on a course (e.g. Islamic Religion → Muslim students); enforced in enrollment and grouping; exceptional enrollment requires Supervisor approval.
- **FR-CRS-04** Course supervisor(s): assign Supervisors to a course; "My courses" view; only assigned supervisors edit the blueprint.
- **FR-LP-01** Blueprint per course: ordered **sections** (label configurable per client: Week / Unit / Chapter) → ordered **lessons** (title ar/en, objectives, planned date offset). Versioned: edits create a draft; publishing creates a new version.
- **FR-LP-02** Blueprint builder UI (tree with drag-and-drop ordering); copy blueprint from another course or year.
- **FR-LP-03** When a ClassCourse is created, the current published blueprint is copied into a **Class Lesson Plan**; blueprint sections/lessons are locked at class level.
- **FR-LP-04** On publishing a new blueprint version the supervisor chooses: **apply to new classes only** or **push to existing classes**. Push merges by lesson identity: new lessons added, removed lessons hidden (not deleted if they hold teacher content), teacher modules always kept.
- **FR-LP-05** Teachers add Lesson Modules under lessons of their own ClassCourses; teachers and students view the plan as a weeks/units → lessons → modules tree.

### 6.7 Enrollment and Teachers (B-8.x, B-9.x)
- **FR-ENR-01** Auto-enrollment: student added to a class → enrolled in all its ClassCourses (respecting restrictions and groups); course added to a class → current students enrolled.
- **FR-ENR-02** Manual course-level enrollment for optional/elective courses (flagged manual).
- **FR-ENR-03** Manual and bulk add of students to a class (from list or Excel per X-06) with validation (capacity, duplicates, same year).
- **FR-ENR-04** Unenroll (status Dropped, history kept); views per student and per course.
- **FR-ENR-05** Transfer between classes mid-year: student, from, to, date, reason; validation (same level/year, capacity); re-map enrollments; old class history kept; grades of courses common to both classes carry over (setting, default on); group membership updated; audit + notify guardians and teachers.
- **FR-TCH-01** Teacher–course eligibility (which courses each teacher may teach); assign per teacher or per course; bulk.
- **FR-TCH-02** Assign eligible teachers to ClassCourses: primary + assistants; replace mid-year with dated history; grants access to content, grading and attendance of that ClassCourse.
- **FR-TCH-03** Teacher sees only own ClassCourses; schedule-conflict warning using the timetable.

### 6.8 Content and Activities (B-10.x)
- **FR-CNT-01** Module types: File, Video, Audio, Page (rich text), Link, Quiz, Assignment, Live Session, SCORM, H5P, LTI tool, Library resource.
- **FR-CNT-02** Common metadata: title, order, visibility (published / hidden / scheduled from–to), target (whole ClassCourse or a group), completion rule (view / submit / score).
- **FR-CNT-03** Asset service: upload to MinIO with type/size limits, video streaming via signed URL, preview for PDF/images/audio/video.
- **FR-CNT-04** Students open content and mark completion (manual or automatic per rule); progress per lesson/course.
- **FR-VC-01** Meeting provider abstraction; Zoom (OAuth server-to-server) in v1, Teams later. Teacher schedules a session (single or recurring) from a ClassCourse; join links in the platform; reminders; participants imported into attendance; recording link attached after the session.
- **FR-ASG-01** Assignment: instructions, attachments, due date, max grade, late policy (allow / allow with penalty / block), resubmission rule, target class or group; publish/schedule.
- **FR-ASG-02** Student submission (file and/or text); teacher grading with feedback and annotated file upload; grade flows to the gradebook; notifications (new, due soon, graded).
- **FR-QZ-01** Quiz builder: MCQ (single/multi), true/false, essay; question pool per quiz; points per question.
- **FR-QZ-02** Settings: time limit, attempts, grading method (highest/last/average), shuffle, open/close dates, review policy; unfinished attempts auto-submitted at close.
- **FR-QZ-03** Attempt with timer and autosave; objective questions auto-graded; essays graded manually; score to gradebook.
- **FR-ATT-01** Attendance sessions generated from the timetable (or created ad hoc / from live sessions); statuses Present, Absent, Late, Excused; "mark all present"; edit window (setting); summaries per student and class; guardian view.
- **FR-LIB-01** Digital library: resources (title, type, level, subject, tags, cover, file/link), managed by Supervisor/Admin/Teacher; search and filter; access by unit and level; in-browser reader (download can be disabled per resource); add to a lesson as a module.
- **FR-FOR-01** Forum per ClassCourse: threads, replies, attachments; teacher moderation (delete, lock, pin); announcements per course (pin, notify).
- **FR-STD-01** SCORM 1.2 / 2004 upload with manifest validation, player with completion and score tracking.
- **FR-STD-02** H5P package upload and embedded player with xAPI score capture.
- **FR-STD-03** LTI 1.3 tool registration by Admin, launch from a module, Assignment & Grade Services passback to gradebook.

### 6.9 Grading (B-11.x)
- **FR-GRD-01** Gradebook per ClassCourse and term: grade items (manual or auto-created by assignments, quizzes, SCORM/H5P/LTI) in categories.
- **FR-GRD-02** Grading scales: percentage, letter ranges, GPA; configured per unit with inheritance; scale templates; one default per client.
- **FR-GRD-03** Category weights per ClassCourse must total 100%; weight templates reusable across the client; calculation engine with rounding rules recalculates on every change.
- **FR-GRD-04** Teacher gradebook grid (inline editing, Excel export/import of a column); student view of own grades; reports per student, class and course for Supervisor.
- **FR-GRD-05** Publish state per grade item: hidden / published / scheduled; students and guardians see only published grades; notify on publish.
- **FR-GRD-06** Grades lock when the term/year closes; later changes require Manager approval and are audited.

### 6.10 Timetable (B-12, B-12.1)
- **FR-TT-01** Periods per unit (name, start, end) and configurable working days.
- **FR-TT-02** Timetable slot = ClassCourse + teacher + day + period (+ optional room), valid within a term; draft and published states.
- **FR-TT-03** Weekly grid builder per class with drag-and-drop; conflict validation (teacher double-booked, class double-booked, room double-booked) — block or warn per setting.
- **FR-TT-04** Views for class, teacher, student, guardian (own data only); export to Excel; copy from previous term.
- **FR-TT-05** Temporary change / substitute teacher for a date without altering the base timetable.
- **FR-TT-06** Published slots generate attendance sessions and can host live sessions.
- **FR-EXC-01** Absence/late event → notification to guardians (student, course, date).
- **FR-EXC-02** Excuse submission by guardian or student: reason, date range or sessions, attachment; policy: max days after absence, allowed reasons, mandatory attachment.
- **FR-EXC-03** Counselor review queue: approve, reject (with comment) or **escalate** to a Manager (or another staff member); approved → status Excused; rejected → stays Absent; status tracking for guardian/student; decision notifications.
- **FR-EXC-04** Absence threshold alerts to Counselor (setting).

### 6.11 Communication (B-13, B-13.1)
- **FR-NOT-01** Notification event catalog with default recipients per role; per-user channel preferences.
- **FR-NOT-02** In-app notification center (bell, unread count, mark read, deep link), realtime via SignalR.
- **FR-NOT-03** Email (SMTP via MailKit, queued, retries, bounce logging), SMS (provider abstraction, one provider in v1), Web Push (VAPID) for installed PWA/browsers.
- **FR-MSG-01** Conversations 1:1 and group with attachments, search, unread counters, optional read receipts.
- **FR-MSG-02** Messaging rules per client (who may message whom; student-to-student off by default).
- **FR-MSG-03** Announcements targeted to organization / unit / class / course / group / role; publish now or schedule; pin; expiry.
- **FR-MSG-04** Report message / block user; Admin moderation review.

---

## 7. Core Data Model (logical)

```
Organization 1─* LevelType
Organization 1─* OrgUnit (tree via ParentId, Path)
OrgUnit 1─* Setting / Policy / FeatureFlag / CourseAllocation / Period
User 1─* RoleAssignment *─1 Role ; RoleAssignment *─1 OrgUnit
Role 1─* RolePermission
User 1─1 StaffProfile | StudentProfile | GuardianProfile
GuardianProfile *─* StudentProfile (StudentGuardian: relation, IsPrimary)
AcademicYear 1─* Term ; AcademicYear 1─* CalendarEvent
Class (OrgUnit, AcademicYear) 1─* ClassStudent ; Class 1─* ClassCourse
Course 1─* CourseAllocation ; Course 1─* CourseRestriction ; Course 1─* CourseSupervisor
Course 1─* Blueprint(version) 1─* BlueprintSection 1─* BlueprintLesson
ClassCourse 1─1 ClassLessonPlan 1─* ClassSection 1─* ClassLesson 1─* LessonModule
ClassCourse 1─* ClassCourseTeacher ; 1─* CourseEnrollment ; 1─* StudentGroup 1─* GroupMember
LessonModule → Asset | Quiz | Assignment | LiveSession | ScormPackage | H5PContent | LtiLink | LibraryResource
ClassCourse 1─* GradeCategory 1─* GradeItem 1─* GradeEntry
TimetableSlot (ClassCourse, Teacher, Day, Period, Room, Term) → AttendanceSession 1─* AttendanceRecord
AttendanceRecord 1─* Excuse
Conversation 1─* Message ; Announcement ; Notification ; AuditEntry
```
All tenant-owned tables: `OrganizationId`; unit-scoped tables: `OrgUnitId`, `OrgUnitPath`; all business tables: `CreatedAt/By`, `UpdatedAt/By`, `IsDeleted`.

---

## 8. Decisions Log (ambiguities in the backlog, resolved)

| # | Topic | Decision |
|---|---|---|
| D1 | Tenancy | Shared database; `OrganizationId` = client; subtree isolation by `OrgUnitPath`. |
| D2 | Roles | 7 built-in roles + Platform Admin; labels per client type; **custom roles allowed**. |
| D3 | Database / files | MySQL 8.4 + MinIO. |
| D4 | Render mode | Blazor WebAssembly only. |
| D5 | Premium features | Generic feature flags per unit/student; no billing. |
| D6 | Groups | A student is in at most one group **per ClassCourse**. |
| D7 | Blueprint updates | Supervisor chooses per publish: new classes only, or push to existing (teacher content kept). |
| D8 | Imports | All-or-nothing commit after preview. |
| D9 | Enrollment (backlog note "classes not courses") | Default enrollment is class-based (automatic to ClassCourses); manual course-level enrollment exists only for optional courses. |
| D10 | Attendance location | Attendance lives in Content & Activities and is fed by the Timetable. |
| D11 | Excuse workflow | Guardian or student submits; Counselor approves/rejects or escalates to a Manager. |
| D12 | Promotion undo | Short undo window (default 10 min) before activity in the new year. |
| D13 | Library "use dhcp" | Interpreted as in-browser protected reader with download disabled per resource. |
| D14 | External exam cycle, AI, native mobile | Out of v1 scope. |
| D15 | Access at an Inactive OrgUnit (2026-11-10) | An assignment at an Inactive unit, or below one, is not a valid active context (403, hidden from the context chooser). Admins assigned above the unit keep access to view and reactivate it. |

---

## 9. v1 Acceptance (release criteria)

1. A school group client and a university client can each be configured **without code changes** (level types, labels, tree) — demonstrated with the two sample taxonomies from the backlog sheets.
2. Isolation tests pass for every module: client A never sees client B; sibling units never see each other; parents see descendants.
3. End-to-end Playwright scenarios pass in Arabic (RTL) and English:
   - Admin builds the tree, academic year, classes, courses, allocations and blueprint.
   - Students and staff imported from Excel; teachers assigned; students auto-enrolled.
   - Teacher publishes content, an assignment and a quiz; student completes them; grades flow to gradebook; teacher publishes grades; guardian sees them.
   - Timetable published → attendance taken → absence notification → guardian excuse → counselor approves.
   - Year close → promotion run → new year opened.
4. All FRs in §6 implemented or explicitly deferred by the product owner.
5. CI green; no open Critical/High bugs; performance target X-09 met on seeded data.
