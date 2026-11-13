using System.Net;
using System.Text;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;

namespace Lms.Client.Tests.Services;

/// <summary>"My class courses" is answered with the server's own shape (<c>classCourseId</c>), not the class-course DTO of a class.</summary>
public sealed class LessonPlansApiTests
{
    private sealed class Json(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task The_class_course_id_of_the_server_shape_is_kept()
    {
        var id = Guid.NewGuid();
        var classId = Guid.NewGuid();
        var body = $$"""
            [{"classCourseId":"{{id}}","classId":"{{classId}}","classNameAr":"فصل","classNameEn":"Class","courseNameAr":"مادة","courseNameEn":"Course",
              "orgUnitNameAr":"وحدة","orgUnitNameEn":"Unit","teacherRole":null,"studentCount":7}]
            """;
        var api = new LessonPlansApi(new ApiHttp(new HttpClient(new Json(body)) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()));

        var courses = await api.GetMyClassCoursesAsync();

        var course = Assert.Single(courses);
        Assert.Equal(id, course.Id);
        Assert.Equal(classId, course.ClassId);
        Assert.Equal("Course", course.CourseNameEn);
        Assert.Equal("Class", course.ClassNameEn);
        Assert.Equal(7, course.EnrolledCount);
    }

    [Fact]
    public async Task The_optional_read_keeps_the_class_course_id_of_the_server_shape_too()
    {
        var id = Guid.NewGuid();
        var body = $$"""
            [{"classCourseId":"{{id}}","classId":"{{Guid.NewGuid()}}","classNameAr":"فصل","classNameEn":"Class","courseNameAr":"مادة","courseNameEn":"Course",
              "orgUnitNameAr":"وحدة","orgUnitNameEn":"Unit","teacherRole":null,"studentCount":3}]
            """;
        var api = new LessonPlansApi(new ApiHttp(new HttpClient(new Json(body)) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()));

        var course = Assert.Single(await api.GetMyClassCoursesIfAllowedAsync());

        Assert.Equal(id, course.Id);
        Assert.Equal("Course", course.CourseNameEn);
        Assert.Equal(3, course.EnrolledCount);
    }
}
