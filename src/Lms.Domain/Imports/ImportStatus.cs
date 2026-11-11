namespace Lms.Domain.Imports;

/// <summary>Lifecycle of an import batch (X-06). Stored as its name; the names equal <c>Lms.Shared.Imports.ImportStatuses</c>.</summary>
public enum ImportStatus { Uploaded, Validating, Valid, Invalid, Committing, Committed, Failed }
