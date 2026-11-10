# LMS

Multi-tenant LMS for school groups and universities. See `docs/PRD.md`, `docs/DEV-PLAN.md` and `CLAUDE.md`.

## Run locally
```
docker compose up -d          # MySQL 8.4, MinIO (9000/9001), Mailpit (8025)
dotnet run --project src/Lms.Api
dotnet test LMS.slnx          # integration tests use Testcontainers (Docker required)
```
Health check: `GET /health`.
## MinIO image
Upstream `minio/minio` is no longer published on Docker Hub, so `docker-compose.yml` and the integration tests use `chainguard/minio:latest` (Docker Hub). Chainguard's free tier only serves `latest`, so it can't be pinned to a tag; to reproduce a run, record the digest with `docker image inspect chainguard/minio:latest --format '{{index .RepoDigests 0}}'`. If it ever breaks, switch both places (compose and `LmsApiFactory`) together.

## MySQL provider
Oracle `MySql.EntityFrameworkCore` (Pomelo has no EF Core 10 release); see `docs/adr/0001-mysql-provider.md`.
