namespace Lms.Domain.Auditing;

/// <summary>Property is never written to audit snapshots or diffs (password hashes, security stamps, tokens).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;
