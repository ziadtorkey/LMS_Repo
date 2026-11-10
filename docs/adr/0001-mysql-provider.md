# ADR 0001: MySQL EF Core provider

Status: accepted (2026-11-10)

## Context
The M0 plan prefers Pomelo.EntityFrameworkCore.MySql. Its latest stable release (9.0.0) targets EF Core 9; no EF Core 10 compatible release exists. The solution targets .NET 10 / EF Core 10.

## Decision
Use Oracle's `MySql.EntityFrameworkCore` 10.0.12 (EF Core 10). `Microsoft.EntityFrameworkCore.Design` is pinned to 10.0.12 to match the provider's EF Core version (mixing 10.0.12 and 10.0.15 produces assembly conflicts).

## Consequences
- Use `UseMySQL(...)` (via `UseLmsMySql`), not `UseMySql`.
- Column types needing a charset (OrgUnit path) are declared as raw store types (`varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci`).
- Revisit when Pomelo ships an EF Core 10 release.