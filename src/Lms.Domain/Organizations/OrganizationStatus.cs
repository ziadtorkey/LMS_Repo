namespace Lms.Domain.Organizations;

/// <summary>A suspended client keeps its data but none of its role assignments is a valid context.</summary>
public enum OrganizationStatus { Active, Suspended }