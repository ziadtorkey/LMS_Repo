namespace Lms.Api.Features.Guardians;

/// <summary>
/// Routes of the guardian endpoints (FR-STU-06). Kept here until the M3 contract PR adds them to <c>Lms.Shared.Routes</c>;
/// the strings are exactly the contract's (M2 plan appendix, Task 2).
/// </summary>
public static class GuardianRoutes
{
    public const string Guardians = "/api/guardians";
    public const string GuardianPattern = Guardians + "/{id:guid}";
    public const string GuardianMatch = Guardians + "/match";
    public const string StudentGuardiansPattern = "/api/students/{id:guid}/guardians";
    public const string StudentGuardianPattern = StudentGuardiansPattern + "/{guardianId:guid}";

    public static string Guardian(Guid id) => $"{Guardians}/{id}";
    public static string StudentGuardians(Guid studentId) => $"/api/students/{studentId}/guardians";
    public static string StudentGuardian(Guid studentId, Guid guardianId) => $"{StudentGuardians(studentId)}/{guardianId}";
}
