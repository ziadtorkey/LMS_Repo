# ADR 0003: Background jobs on Hangfire with MySQL storage

Status: accepted (2026-11-12)

## Context
The stack names Hangfire for background jobs (PRD §1.3). Hangfire has no first-party MySQL storage. The candidates on nuget.org: `Hangfire.MySqlStorage` 2.0.3 (most used, ~5.6M downloads, last release 2018, built on MySqlConnector), `Hangfire.MySql.Core` (stulzq, 2.2.x) and a few smaller forks. None is actively maintained.

## Decision
Use `Hangfire.Core`/`Hangfire.AspNetCore` 1.8.22 with `Hangfire.MySqlStorage` 2.0.3, tables prefixed `Hangfire` in the application database (the storage creates its own schema; EF migrations do not manage it). The integration tests run jobs against the MySQL 8.4 test container (enqueue, system and recurring jobs), which is the verification that this old package works with MySQL 8.4.

`Hangfire.MySqlStorage` and `Hangfire.NetCore` pull in vulnerable old transitives (`Newtonsoft.Json` 11.0.2, `System.Data.SqlClient` 4.4.0); `Lms.Api` references patched versions directly so the build stays free of NU190x warnings.

## Consequences
- Jobs carry the enqueuing caller's context (client, assignment, subtree) as a job parameter, restored in a job activator scope, so global query filters apply inside jobs. A job enqueued without the `IBackgroundJobs` facade has no context and sees no scoped rows; recurring and system jobs run with an explicit platform context.
- The dashboard is mapped in Development only, behind the platform-admin check.
- Fallback if the package breaks on a future MySQL or Hangfire release: Hangfire with a different community storage, or a `BackgroundService` plus a jobs table owned by us. Revisit when a maintained MySQL storage appears.
