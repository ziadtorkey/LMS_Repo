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
| D16 | Manager role assignments (2026-11-10) | Manager gets `Assignments.Create/Delete`, limited by an escalation guard to roles whose permissions are a subset of their own (e.g. Teacher, Counselor, Supervisor; never Org Admin or Manager). |
| D17 | Academic years visibility | All built-in roles may view academic years, terms and calendar; only Org Admin creates/edits/deletes them. |
| D18 | Client-wide configuration | Level types, custom roles and built-in role permission sets are editable only by admins assigned at the client root. |
| D19 | Deleting an OrgUnit | Also blocked while the unit has live child units or live role assignments (in addition to FR-ORG-04's rules). |
| D20 | Audit visibility | Audit entries are visible within the viewer's subtree; entries with no unit (logins, client-level changes) only to root-level viewers. |
| D21 | Assigning roles before M2 staff profiles | Assign by email; an unknown email creates a bare user and sends an invitation; no cross-client user search. |
| D22 | Academic year scope | A unit may define its own year list (replacing its parent's); at most one Active year per defining unit. |
| D23 | M2 scope / class placement (2026-11-11) | Student-facing work waits for M3 with classes: student codes, student CRUD and class placement, guardian linking and portal, change requests, student import and their pages. M2 delivers staff, the people kernel (incl. the student/guardian data model), the import framework, staff import, staff expiry and external sign-in. |
| D24 | Student accounts | A default password is provided when a student is created or imported. A student with an email receives the credentials by email; otherwise the guardian shares the account details with the student. The student must change the password at first sign-in; the default password follows the password policy and is delivered only to the student or the primary guardian (shown once, never logged or kept in import data). |
| D25 | Student code | One running sequence per client (a single number across all schools, never reset per school or year); codes are never reused, even after deletion. Auto (default) or Manual entry per client setting. |
| D26 | Duplicate people | Staff: an email, mobile or national id already used in the client blocks creation. Students: national id blocks; same name + date of birth asks for confirmation on screen and blocks the row in an import. Checks are client-wide and reveal only which field collided. |
| D27 | Student change requests | Org Admin, Manager and Counselor approve; staff with student-update rights edit directly (audited); only a student's own edits of critical fields need approval; guardians cannot request changes in v1. |
| D28 | Role-blocking policies | `CreateTeachers` blocks assigning the Teacher role; `CreateAdmins` blocks Org Admin, Manager and custom roles that can assign or edit roles; both apply on every path that creates an assignment. `CreateStaff` / `CreateStudents` block create and import. |
| D29 | Sharing staff | Done by an admin who can edit the person at the home unit and assign roles at each target unit; the target sees name, email, job title, photo and role there, not national id, mobile or status history. |
| D30 | Guardians | An exact national id or mobile match anywhere in the client is offered for linking (name and masked mobile only), so siblings in different schools share one guardian; any school with one of the guardian's children may edit the guardian; a portal account exists only when the guardian has an email and `GuardianPortal` is on for the child's unit. |
| D31 | Retiring assign-by-email (D21 follow-up) | From M2 roles are assigned to existing staff members picked from a list; unknown people are created as staff first; M1 bare users get a staff profile automatically; student and guardian roles are managed from their profiles. |
| D32 | Matrix gaps (§3.4) | Teacher gets student access in M3 with the own-classes rule; Manager keeps Staff View + Create only; Counselor may update student data and approve change requests. |
| D33 | External sign-in | "Sign in with your organization account": the email domain picks the client's provider (direct link per provider too); users are matched by email or a configured national-id claim only among that client's people; no automatic account creation; such a session opens only that client's contexts. |
| D34 | Imports | Create-only in v1 (existing people are errors); at most 5,000 rows and 10 MB per file; one role per staff row; two guardians per student row; templates in Arabic or English, either accepted. |
| D35 | Staff expiry | The expiry date is the last day of access; admins at or above the staff member's unit get a notice N days before (setting, default 14, 0 = off) and on expiry; the job runs daily at 02:00 UTC. |
| D36 | Calendar under D22 | A unit that defines its own academic-year list does not inherit its parent's holidays and calendar events. |
| D37 | Course ownership (M3) | Courses are owned by the client root or by units flagged as the course-management level, and allocated at or below the owner; edits and blueprints happen at the owner unit. |
| D38 | Course codes | A course code is unique in the whole client. |
| D39 | Course restrictions | Only Religion, Gender and Level in v1 (custom attributes later); several types on one course must all match; any listed value within a type matches; changing restrictions keeps existing enrollments and the screen warns. |
| D40 | Exceptional enrollment | The course's supervisors approve; if the course has none, Managers or Org Admins at or above the class approve. Counselors, Managers and Org Admins may raise a request. |
| D41 | Removing a course from a class | Allowed while all enrollments are automatic (they become Dropped); blocked by manual or exceptional enrollments, group members and (later) content or grades. Resolves FR-CLS-05 vs FR-ENR-01. |
| D42 | Relationship rules | The built-in Teacher sees only own ClassCourses and their homeroom class; the built-in Supervisor acts only on supervised courses; custom roles are unrestricted within their unit. |
| D43 | Transfers across schools | Allowed when level type and academic year match and the admin can see both units. |
| D44 | Deferred from M3 | FR-TCH-03 conflict warning to M5 (timetable); FR-LP-05 module authoring to M4; the class timetable tab to M5; FR-ENR-05 grade carry-over to M5 (M3 stores only the link and setting). |
| D45 | Default student password | If left blank a strong password is generated per student and shown once; for imports a credentials sheet is downloadable once within 15 minutes; students must change it at first sign-in. Class placement is optional on create and import ("Unplaced" filter). |
| D46 | Blueprint authoring | Org Admins and Managers may create and edit course blueprints at their units (amends the §3.4 view-only cells); course supervisors keep it for their courses. |
| D47 | Student sign-in names | A student signs in as `<student code>@<client short code>` (e.g. `000123@alnoor`). Every client has a short code, unique across the platform (2–20 lower-case letters, digits or hyphens; defaults to the root unit code). The full sign-in name is shown on the credentials sheet. |
| D48 | Rich text (M4) | Pages, assignment instructions and forum posts use a visual (Word-like) editor in one shared component; the server sanitises HTML on write with an allow-list. |
| D49 | Zoom account | One platform-level Zoom app for all clients (not per-client credentials); secrets live in server configuration, never in the database or the repo. |
| D50 | LTI | LTI 1.3 is out of scope for v1. |
| D51 | M4 split | M4 ships as M4a (content modules, assignments, quizzes, forums) then M4b (Zoom, SCORM, H5P). Lead technical defaults: GradeEvent outbox as the M5 grade seam; video is signed-URL streaming of the uploaded file (no HLS); a quiz pool draws N questions per attempt; group-targeted modules are visible only to that group and its teachers; SCORM/H5P content is served from a sandboxed origin with a strict CSP after a QA security spike. |
| D52 | M4a content rules | Org Admins and Managers hold Modules.Manage and the staff-side activity permissions (amends the section 3.4 "Content & activities" row, so they can assign the Teacher role under D16). One upload size limit for all files (Files.MaxUploadMb). No inline images in the rich-text editor in M4a (images are File modules). The resource library stays in M4a. |
| D53 | M4b Zoom host model | One platform Zoom account with a configured pool of licensed host users; an allocator picks a free host per session and refuses overlapping use (Z1/Z2). The licence count is configuration. |
| D54 | M4b Zoom events | The deployment exposes a public HTTPS webhook endpoint for Zoom (recordings, participants), with polling as fallback and manual recording links always allowed (Z3). |
| D55 | M4b recordings | Cloud recording is off by default; a unit admin may enable it for their unit. Teachers can always attach a recording link manually (Z4/Z6). |
| D56 | M4b package host | SCORM/H5P content is served from a separate registrable domain with a strict sandbox CSP and signed path tokens; no app-origin fallback (D51, spike #444). |
| D57 | M4b package re-attempts | When a student opens a completed SCORM/H5P package again, the teacher chooses per module which completed attempt is graded: Highest (default), Last, or First (later attempts are practice). Owner decision 2026-11-13. |
| D58 | M5 defaults | Owner accepted all M5 proposal defaults (#611 Q1–Q20): grade lock at year close stays in M7; GPA in M5 shows band points; a ClassCourse may pick a scale visible at its unit; periods per unit with override; missing grades normalised (setting); category average points-weighted (setting); no teacher-availability limits in v1; holidays skipped; Zoom attendance proposes Present, teacher confirms; guardians see published grades only; Excel export only (PDF report cards M7); LTI out of v1. Owner decision 2026-11-14. |
| D59 | Saudi standards | Grading scales, GPA and academic-year structure must follow Saudi school standards: ship Saudi presets (Ministry of Education school grade bands and descriptors in Arabic and English; university 5-point and 4-point GPA scales) and support the Saudi term structure as configured per client (two or three terms per year). Presets are data, editable per client. Owner decision 2026-11-14. |
| D60 | Timetable rooms | Rooms are a free-text label on a timetable slot in v1; no room list or clash detection. Owner decision 2026-11-14. |
| D61 | Excuses and alerts | Who may submit absence excuses is a per-client setting (default: guardians only). Absence alert default: 5 absences in 30 days, a late counts as half, both configurable per client. Owner decision 2026-11-14. |
| D62 | Saudi term default | The Saudi preset defaults to two terms per year for 2026-27; each client may switch to three. Owner decision 2026-11-14. |
| D63 | University GPA default | The pilot university uses the 5.00-point GPA scale by default; the 4.00-point scale ships as an alternative preset. Owner decision 2026-11-14. |
| D64 | Time zone | Clients default to Asia/Riyadh, with a per-client time-zone setting (`Org.TimeZone`) for clients outside Saudi Arabia. Owner decision 2026-11-14. |
| D65 | Antivirus | v1 keeps the upload scanner hook (packages, attachments) off, with strict validation and sandboxing; a real scanner (for example ClamAV) is wired before production go-live. Owner decision 2026-11-14. |
| D66 | M6 defaults | Owner accepted all proposed defaults of the consolidated M6 question list (29 items, PR #613 comment by QA2) other than D67-D69: no student data or message text in SMS/push; quiet hours per client, off by default (21:00-07:00 when on, SMS/push only); guardians own their preferences; imported contacts trusted in v1 (no OTP); mandatory events not disableable; daily caps 5,000 email / 500 SMS / 20,000 push per client; messages in the user's language; groups up to 50; read receipts off; delete own message within 15 min, no edit; one announcements store with ar+en titles; retention as listed pending legal advice. Owner decision 2026-11-14. |
| D67 | Email provider | Build email behind a provider interface with a development fake; the concrete relay (SES, SendGrid, Mailgun or client SMTP) is chosen before go-live. Owner decision 2026-11-14. |
| D68 | SMS provider | Build SMS behind `ISmsProvider` with a development fake; the owner picks a Saudi-licensed gateway (e.g. Unifonic, Taqnyat) and registers the sender ID before go-live. Owner decision 2026-11-14. |
| D69 | Messaging safeguarding | Safeguarding-first rules: students message only their own teachers and counselor, never other students; guardians message only their children's teachers and counselor; managers and org admins reach students only via groups or announcements (no private 1:1 with students); the Org Admin may tighten per client. Owner decision 2026-11-14. |
| D70 | Moderation access | Only the Org Admin (Messaging.Moderate) sees the moderation queue; Managers get no read-only view. Owner decision 2026-11-14 (M6 plan O1). |
| D71 | Communication retention | Plan defaults accepted, pending legal advice: deliveries 90 days; messages the school year + 1 year; report context and removed originals 180 days; each client may shorten. Owner decision 2026-11-14 (M6 plan O2). |
| D72 | SMS and minors | No SMS about minors: SMS goes only to adults about themselves; guardians receive news about their children by email and push. Consent wording (ar/en) drafted by the Lead for owner review before go-live. Owner decision 2026-11-14 (M6 plan O3). |
| D73 | Provider data region | Prefer in-Kingdom or GCC processing for the email relay, SMS gateway and push services where offered; otherwise any region, since bodies hold no student data (D66). Owner decision 2026-11-14 (M6 plan O5). |
| D74 | Class groups read-only for students | In class-course groups only the teacher (and staff) post; students read and reply privately to the teacher. D69 "never message other students" means never, including group posts. Owner decision 2026-11-14. |
| D75 | No SMS to guardians | Guardians never receive SMS, not even urgent school-wide announcements; they get email and push. SMS is only for adult accounts about themselves (D72). Owner decision 2026-11-14. |
| D76 | No SMS in v1 | No SMS anywhere in v1: no SMS channel, preferences, caps, consent or gateway. Email and push only. SMS may come in a later version. Supersedes D68 and the SMS parts of D66, D72, D73 and D75. Owner decision 2026-11-14. |
| D77 | Guardians may post in groups | In teacher-run groups that include guardians, guardians may post (other guardians read it). Students stay read-only (D74). Owner decision 2026-11-14. |
| D78 | Who runs promotion | Org Admin anywhere, and a Manager inside their own units. Owner decision 2026-11-14 (M7). |
| D79 | Close before promote | A year must be Closed before promotion runs; no reopen in v1 (late fixes go through grade-change approval). Closing warns, but does not block, when grades are unpublished. Owner decision 2026-11-14. |
| D80 | Grade-change approval | After close, a Manager or the Org Admin approves a grade change, never the requester (FR-GRD-06). Owner decision 2026-11-14. |
| D81 | After leaving | After withdrawal or graduation the guardian keeps read access to the child's closed years; the withdrawal reason is staff-only. Owner decision 2026-11-14. |
| D82 | Who withdraws | Org Admin, Manager and Counselor may withdraw a student; the reason comes from a client-managed list plus "Other" with a note. Owner decision 2026-11-14. |
| D83 | Promotion outcome | Promotion suggests the next level from the level mapping only (no grade pass/fail rule in v1); every row stays editable. Owner decision 2026-11-14. |
| D84 | Promotion blockers and failures | Open grade-change and excuse requests show as warnings, not blocks. A run succeeds per student with a failure list to fix and re-run; undo restores the successful ones. No bulk withdrawal in v1. Owner decision 2026-11-14. |
| D85 | SAML and LDAP scope | v1: SP-initiated SAML only; LDAP search-bind over LDAPS or StartTLS only; no group-to-role mapping (roles are assigned in the LMS). Owner decision 2026-11-14. |
| D86 | Year-end checklist | Shown on the home pages from 60 days before the year end. Owner decision 2026-11-14. |
| D87 | Accessibility target | WCAG 2.1 AA (X-10); no AAA requirement. Owner decision 2026-11-14. |

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
