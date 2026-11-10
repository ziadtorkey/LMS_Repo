namespace Lms.Domain.Access;

public enum AccessDenialReason { None, NotAuthenticated, MissingPermission, OutOfScope, BlockedByPolicy }