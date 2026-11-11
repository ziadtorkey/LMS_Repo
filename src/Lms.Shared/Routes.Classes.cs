namespace Lms.Shared;

// M3 classes, class courses, teachers, enrollment, exceptions, groups, transfers and lesson plans (M3 plan Task 4).
public static partial class Routes
{
    public const string Classes = "/api/classes";
    public const string ClassesBulk = Classes + "/bulk";
    public const string ClassesCopy = Classes + "/copy";
    public const string ClassesMatrix = Classes + "/matrix";
    public const string ClassPattern = Classes + "/{id:guid}";
    public const string ClassClosePattern = ClassPattern + "/close";
    public const string ClassStudentsPattern = ClassPattern + "/students";
    public const string ClassStudentRemovePattern = ClassStudentsPattern + "/{classStudentId:guid}/remove";
    public const string ClassCoursesOfClassPattern = ClassPattern + "/courses";

    public static string Class(Guid id) => $"{Classes}/{id}";
    public static string ClassClose(Guid id) => $"{Class(id)}/close";
    public static string ClassStudents(Guid id) => $"{Class(id)}/students";
    public static string ClassStudentRemove(Guid id, Guid classStudentId) => $"{ClassStudents(id)}/{classStudentId}/remove";
    public static string ClassCoursesOfClass(Guid id) => $"{Class(id)}/courses";
    public static string ClassesMatrixFor(Guid orgUnitId, Guid yearId) => $"{ClassesMatrix}?orgUnitId={orgUnitId}&yearId={yearId}";

    public const string ClassCourses = "/api/class-courses";
    public const string ClassCoursesBulkAssign = ClassCourses + "/bulk-assign";
    public const string ClassCoursePattern = ClassCourses + "/{id:guid}";
    public const string ClassCourseTeachersPattern = ClassCoursePattern + "/teachers";
    public const string ClassCourseTeacherReplacePattern = ClassCourseTeachersPattern + "/{assignmentId:guid}/replace";
    public const string ClassCourseTeacherEndPattern = ClassCourseTeachersPattern + "/{assignmentId:guid}/end";
    public const string ClassCourseEnrollmentsPattern = ClassCoursePattern + "/enrollments";
    public const string ClassCourseExceptionsPattern = ClassCoursePattern + "/exceptions";
    public const string ClassCourseGroupsPattern = ClassCoursePattern + "/groups";
    public const string ClassCourseLessonPlanPattern = ClassCoursePattern + "/lesson-plan";

    public static string ClassCourse(Guid id) => $"{ClassCourses}/{id}";
    public static string ClassCourseTeachers(Guid id) => $"{ClassCourse(id)}/teachers";
    public static string ClassCourseTeacherReplace(Guid id, Guid assignmentId) => $"{ClassCourseTeachers(id)}/{assignmentId}/replace";
    public static string ClassCourseTeacherEnd(Guid id, Guid assignmentId) => $"{ClassCourseTeachers(id)}/{assignmentId}/end";
    public static string ClassCourseEnrollments(Guid id) => $"{ClassCourse(id)}/enrollments";
    public static string ClassCourseExceptions(Guid id) => $"{ClassCourse(id)}/exceptions";
    public static string ClassCourseGroups(Guid id) => $"{ClassCourse(id)}/groups";
    public static string ClassCourseLessonPlan(Guid id) => $"{ClassCourse(id)}/lesson-plan";

    public const string TeacherEligibility = "/api/teacher-eligibility";
    public const string TeacherEligibilityItemPattern = TeacherEligibility + "/{id:guid}";
    public const string TeacherAssignmentsBulk = "/api/teacher-assignments/bulk";
    public const string MyClassCourses = Me + "/class-courses";

    public static string TeacherEligibilityItem(Guid id) => $"{TeacherEligibility}/{id}";

    public const string Enrollments = "/api/enrollments";
    public const string EnrollmentDropPattern = Enrollments + "/{id:guid}/drop";

    public static string EnrollmentDrop(Guid id) => $"{Enrollments}/{id}/drop";

    public const string EnrollmentExceptions = "/api/enrollment-exceptions";
    public const string EnrollmentExceptionPattern = EnrollmentExceptions + "/{id:guid}";
    public const string EnrollmentExceptionApprovePattern = EnrollmentExceptionPattern + "/approve";
    public const string EnrollmentExceptionRejectPattern = EnrollmentExceptionPattern + "/reject";
    public const string EnrollmentExceptionCancelPattern = EnrollmentExceptionPattern + "/cancel";

    public static string EnrollmentException(Guid id) => $"{EnrollmentExceptions}/{id}";
    public static string EnrollmentExceptionApprove(Guid id) => $"{EnrollmentException(id)}/approve";
    public static string EnrollmentExceptionReject(Guid id) => $"{EnrollmentException(id)}/reject";
    public static string EnrollmentExceptionCancel(Guid id) => $"{EnrollmentException(id)}/cancel";

    public const string Groups = "/api/groups";
    public const string GroupPattern = Groups + "/{id:guid}";
    public const string GroupMembersPattern = GroupPattern + "/members";
    public const string GroupMembersRemovePattern = GroupMembersPattern + "/remove";
    public const string GroupSuggestionsPattern = GroupPattern + "/suggestions";

    public static string Group(Guid id) => $"{Groups}/{id}";
    public static string GroupMembers(Guid id) => $"{Group(id)}/members";
    public static string GroupMembersRemove(Guid id) => $"{GroupMembers(id)}/remove";
    public static string GroupSuggestions(Guid id) => $"{Group(id)}/suggestions";

    public const string Transfers = "/api/transfers";
    public const string TransfersPreview = Transfers + "/preview";
    public const string StudentTransfersPattern = StudentPattern + "/transfers";
    public const string StudentEnrollmentsPattern = StudentPattern + "/enrollments";

    public static string StudentTransfers(Guid id) => $"{Student(id)}/transfers";
    public static string StudentEnrollments(Guid id) => $"{Student(id)}/enrollments";
}
