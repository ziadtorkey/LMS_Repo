# ADR 0004: Grade events as an outbox for the M5 gradebook

Status: accepted (2026-11-12)

## Context
Assignments and quizzes (M4a) score students; the gradebook (M5) must show those scores without M4a knowing the gradebook's shape (PRD D51). Reading the assignment and quiz tables directly would couple M5 to both modules and lose regrade history.

## Decision
`GradeEvent` is an append-only table written through `IGradeEvents.RecordAsync` in the same transaction as the grade itself (the writer only adds to the caller's unit of work; the caller saves). One row per `(SourceKind, SourceId, StudentProfileId, Revision)`, enforced by a unique index; recording the same revision again is a no-op returning false, a regrade records revision + 1. Rows copy the unit fields of the class course (isolated and re-pathed like every unit-owned row) and are `[Audited]`.

## Consequences
- M4a has no read API; M5 reads the table (latest revision per source and student is the current grade).
- Events are never updated or hard-deleted by M4a code; deleting a source does not remove its history.
- If a gradebook needs push semantics later, a dispatcher can read unprocessed rows by id order; no schema change is needed beyond a cursor.
