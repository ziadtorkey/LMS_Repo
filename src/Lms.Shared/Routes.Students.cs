namespace Lms.Shared;

// M3 students, guardians, change requests and self profile (M3 plan Task 2; deferred from M2 by D23).
public static partial class Routes
{
    public const string Students = "/api/students";
    public const string StudentPattern = Students + "/{id:guid}";
    public const string StudentStatusPattern = StudentPattern + "/status";
    public const string StudentFeatureFlagsPattern = StudentPattern + "/feature-flags";
    public const string StudentGuardiansPattern = StudentPattern + "/guardians";
    public const string StudentGuardianPattern = StudentGuardiansPattern + "/{guardianId:guid}";
    public const string StudentResetPasswordPattern = StudentPattern + "/reset-password";

    public static string Student(Guid id) => $"{Students}/{id}";
    public static string StudentStatus(Guid id) => $"{Student(id)}/status";
    public static string StudentFeatureFlags(Guid id) => $"{Student(id)}/feature-flags";
    public static string StudentGuardians(Guid id) => $"{Student(id)}/guardians";
    public static string StudentGuardian(Guid id, Guid guardianId) => $"{StudentGuardians(id)}/{guardianId}";
    public static string StudentResetPassword(Guid id) => $"{Student(id)}/reset-password";

    public const string Guardians = "/api/guardians";
    public const string GuardiansMatch = Guardians + "/match";
    public const string GuardianPattern = Guardians + "/{id:guid}";
    public const string GuardianPortalInvitationPattern = GuardianPattern + "/portal-invitation";

    public static string Guardian(Guid id) => $"{Guardians}/{id}";
    public static string GuardianPortalInvitation(Guid id) => $"{Guardian(id)}/portal-invitation";

    public const string StudentChangeRequests = "/api/student-change-requests";
    public const string StudentChangeRequestPattern = StudentChangeRequests + "/{id:guid}";
    public const string StudentChangeRequestApprovePattern = StudentChangeRequestPattern + "/approve";
    public const string StudentChangeRequestRejectPattern = StudentChangeRequestPattern + "/reject";

    public static string StudentChangeRequest(Guid id) => $"{StudentChangeRequests}/{id}";
    public static string StudentChangeRequestApprove(Guid id) => $"{StudentChangeRequest(id)}/approve";
    public static string StudentChangeRequestReject(Guid id) => $"{StudentChangeRequest(id)}/reject";

    public const string MyStudentProfile = Me + "/student-profile";
    public const string MyChangeRequests = MyStudentProfile + "/change-requests";
    public const string MyChangeRequestPattern = MyChangeRequests + "/{id:guid}";
    public const string MyChildren = Me + "/children";

    public static string MyChangeRequest(Guid id) => $"{MyChangeRequests}/{id}";
}
