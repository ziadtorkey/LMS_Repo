namespace Lms.Domain.Auditing;

/// <summary>Marks an entity type whose creates, updates and deletes are written to the audit log.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AuditedAttribute : Attribute;
