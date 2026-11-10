# LMS

Multi-tenant LMS for school groups and universities. See `docs/PRD.md`, `docs/DEV-PLAN.md` and `CLAUDE.md`.

## Run locally
```
docker compose up -d          # MySQL 8.4, MinIO (9000/9001), Mailpit (8025)
dotnet run --project src/Lms.Api
dotnet test LMS.slnx          # integration tests use Testcontainers (Docker required)
```
Health check: `GET /health`.

**MySQL port:** compose publishes MySQL on host port 3306 by default. If that port is taken, run e.g. `LMS_MYSQL_PORT=3307 docker compose up -d` (PowerShell: `$env:LMS_MYSQL_PORT=3307; docker compose up -d`) and point the API at it: `ConnectionStrings__Lms="Server=localhost;Port=3307;Database=lms;User=lms;Password=lms_dev_pw"`.

In the `Development` environment the API migrates the local database and loads sample data on startup (idempotent): the *Altagamo3 Schools* school group (School › KG / Primary › Sections) and *MNF University* (FCI / F-E › First / Second › specializations), each with the seven built-in roles. When `Seed:DefaultPassword` is set (dev-only values live in `src/Lms.Api/appsettings.Development.json`) it also creates `admin@lms.test` (platform admin) and, in the school group, `orgadmin@`, `manager@`, `supervisor@`, `counselor@`, `teacher@`, `student@`, `guardian@`, `multi@` (Teacher in KG + Supervisor in Primary) and `lockout-e2e@` (reserved for the lockout E2E) `altagamo3.test`, all with that password. Other environments never seed. Set `Seed:RunOnStartup=false` to skip seeding in Development.
## Container images
MySQL, MinIO (`chainguard/minio`) and Mailpit are pinned by digest in `docker-compose.yml` and `LmsApiFactory`; see `docs/adr/0002-minio-image.md`. Bump digests in a dedicated `chore:` PR.

## MySQL provider
Oracle `MySql.EntityFrameworkCore` (Pomelo has no EF Core 10 release); see `docs/adr/0001-mysql-provider.md`.
