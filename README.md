# LMS

Multi-tenant LMS for school groups and universities. See `docs/PRD.md`, `docs/DEV-PLAN.md` and `CLAUDE.md`.

## Run locally
```
docker compose up -d          # MySQL 8.4, MinIO (9000/9001), Mailpit (8025)
dotnet run --project src/Lms.Api
dotnet test LMS.slnx          # integration tests use Testcontainers (Docker required)
```
Health check: `GET /health`.