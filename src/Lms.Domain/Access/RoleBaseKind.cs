namespace Lms.Domain.Access;

/// <summary>Decides which relationship rules apply (PRD §3.2). Names match Lms.Shared RoleBaseKinds.</summary>
public enum RoleBaseKind { Staff, Student, Guardian }