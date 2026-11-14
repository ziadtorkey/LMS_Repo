# Runbook: performance pass (X-09)

**Target (PRD X-09):** list pages under 2 s at p95 with 50,000 students per client; all lists server-paged, filterable, sortable and exportable. The same 2 s p95 budget is applied to every key read path below until the UX review sets stricter ones.

## 1. Seed the 50k-student client (Development only)

The seed is off unless asked for. It is idempotent (the client has a fixed id; a second run does nothing) and bulk-inserts the volume tables (multi-row `INSERT`s built from the EF model, no change tracker).

```bash
docker compose up -d
Seed__DefaultPassword=<a dev password> Seed__Perf__Students=50000 dotnet run --project src/Lms.Api
```

Or set `Seed:Perf:Students` (and `Seed:DefaultPassword`) in `src/Lms.Api/appsettings.Development.json` locally; do not commit a password. It only runs when the environment is `Development`.

What it creates, in its own client **Performance Test Group** (`@perf.test` accounts, the dev seed password):

| Data | Volume at 50,000 students |
|---|---|
| Units | 1 group, 10 schools, up to 3 stages each, sections (180 classes of about 280 students) |
| Students, guardians, guardian links | 50,000 each |
| Courses, class courses, enrollments | 60 courses, 1,080 class courses, 300,000 enrollments |
| Gradebooks, items, grade entries | 1,080 term gradebooks, 3 published items each, 900,000 entries |
| Notifications | 2,000 per sample account |
| Accounts | `perf-admin` (Org Admin), `perf-teacher` (teaches class 1), `perf-student`, `perf-guardian` |

Not seeded yet (their milestones are not on every branch): attendance, timetable, messages. Add them to `PerfSeeder` when merged and set `LMS_PERF_PENDING=1` for the harness.

The seed time is measured by `PerfSeedTests.Full_size_seed_reports_its_time` (`LMS_PERF_SEED_STUDENTS=50000 dotnet test tests/Lms.IntegrationTests --filter Full_size_seed`).

## 2. Run the load harness

`tests/Lms.IntegrationTests/Perf/PerfHarnessTests.cs` runs the application in process (test authentication, no tokens) against the real MySQL database that holds the perf client, with concurrent virtual users, and asserts p95 per path.

```bash
LMS_PERF_CONNECTION="Server=localhost;Port=3306;Database=lms;User=lms;Password=<dev password>" \
LMS_PERF_USERS=20 LMS_PERF_ITERATIONS=200 LMS_PERF_REPORT=perf-report.md \
dotnet test tests/Lms.IntegrationTests --filter Key_read_paths_meet_the_p95_target
```

Without `LMS_PERF_CONNECTION` the test returns at once (CI and ordinary runs skip it). `LMS_PERF_USERS` is the number of concurrent users, `LMS_PERF_ITERATIONS` the requests per path, `LMS_PERF_REPORT` a file for the markdown table.

| Path | Who | Notes |
|---|---|---|
| home: my permissions | student | what the home page loads (my grades joins with M5) |
| student list: page 1, search and sort, deep page | org admin | the X-09 list pages |
| class list, class students | admin, teacher | |
| gradebook grid | teacher | pending (M5 endpoints): 280 rows by 3 items; the data is already seeded |
| inbox: notifications | student | message inbox joins when messages merge |
| my grades, attendance today, timetable, messages inbox | student, teacher | pending: run with `LMS_PERF_PENDING=1` once their milestones are merged |

## 3. Record the result

Paste the table of a run into the PR or issue that changes a path (date, machine, user count). A path over its target is a bug: open an issue with the path, the p95 and the query plan (`EXPLAIN`) of the slow query; the usual fixes are a covering index, keyset instead of offset paging on deep pages, and no per-row queries.

## 4. Baseline (2026-11-14)

50,000 students, MySQL 8.4 in Docker on the same laptop as the in-process host, 20 concurrent users, 200 requests per path. The load generator and the server share the machine, so these numbers include that contention (an idle-server request of the lighter paths is far faster; the heavier ones are limited by the database).

| Path | p50 ms | p95 ms | p99 ms | Target p95 |
|---|---|---|---|---|
| home: my permissions | 964 | 1126 | 1142 | 2000 |
| student list, page 1 | 887 | 1223 | 1437 | 2000 |
| student list, search + sort | 838 | 917 | 933 | 2000 |
| student list, deep page (400) | 912 | 994 | 1011 | 2000 |
| class list | 26 | 33 | 34 | 2000 |
| class students (280 rows) | 131 | 228 | 240 | 2000 |
| inbox: notifications (2,000 rows) | 755 | 906 | 993 | 2000 |

Every measured path is inside the 2 s budget with room to spare, but the student list, home and inbox sit near 1 s at 20 users: watch them as the other milestones add joins (class, guardian and status filters on the list are the first candidates for an index review).
Seed time for 50,000 students (about 1.6 million rows): about 3 minutes 15 seconds; a second run takes under a second.
