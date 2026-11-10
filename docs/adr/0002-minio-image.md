# ADR 0002 — MinIO container image

- **Status:** accepted (2026-11-10, Team Lead)
- **Context:** M0 plan Task 1 uses MinIO for local dev (`docker-compose.yml`) and integration tests (Testcontainers). The `minio/minio` image can no longer be pulled from Docker Hub, which blocked the scaffold PR (#17). QA's review of #17 flagged the unpinned `:latest` tags as a reproducibility risk.
- **Decision:**
  - Use `chainguard/minio` (S3-compatible MinIO build, also published as `cgr.dev/chainguard/minio`) for compose and Testcontainers.
  - Pin images **by digest**, not `:latest`. Digests at decision time:
    - `chainguard/minio@sha256:fb893e36fc369d41e92f678b17ec27378fa5bf0576aeef7ac15460c13629ab5a`
    - `axllent/mailpit@sha256:aef2aacc11f075ec0fb6874275ee4a421e688e11a96ebb296134d1735c7761ce`
  - Digests are bumped deliberately in their own `chore:` PR with green CI, never implicitly.
- **Consequences:** Builds and tests are reproducible. Production object storage is chosen at deployment time (any S3-compatible service works with the Minio .NET SDK); this ADR covers dev/test only. Applying the pins is tracked on #4.
- **ADR 0001** is reserved for the MySQL EF Core provider choice (plan Global Constraints), recorded by Backend in #4.
