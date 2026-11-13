using System.Net;
using Lms.Client.Services.Api;
using Lms.Client.Services.Packages;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.Client.Tests.Services;

public sealed class PackagesApiTests
{
    private sealed record Seen(HttpMethod Method, string Path, string? ContentType, string Body);

    private sealed class Recorder : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(request.Method, request.RequestUri!.PathAndQuery, request.Content?.Headers.ContentType?.MediaType, body));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private static readonly Guid Cc = Guid.NewGuid(), Lesson = Guid.NewGuid(), Id = Guid.NewGuid();

    private static (PackagesApi Api, Recorder Recorder) Create()
    {
        var recorder = new Recorder();
        return (new PackagesApi(new ApiHttp(new HttpClient(recorder) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore())), recorder);
    }

    [Fact]
    public async Task Http_client_uses_the_contract_routes()
    {
        var (api, recorder) = Create();

        await api.GetAsync(Id).ContinueWith(_ => { }); // empty body: only the request matters
        await api.DeleteAsync(Id);
        await api.AttachAsync(Id, new AttachPackageModuleRequest(new ModuleSettingsInput("ع", "E", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, CompletionRules.Submit, null))).ContinueWith(_ => { });
        await api.ListPendingAsync(Cc, Lesson, PackageUploadStatuses.Validating).ContinueWith(_ => { });
        await api.ListPendingAsync(Cc).ContinueWith(_ => { });

        Assert.Contains(recorder.Requests, r => r.Method == HttpMethod.Get && r.Path == Routes.Package(Id));
        Assert.Contains(recorder.Requests, r => r.Method == HttpMethod.Delete && r.Path == Routes.Package(Id));
        Assert.Contains(recorder.Requests, r => r.Method == HttpMethod.Post && r.Path == Routes.PackageModule(Id));
        Assert.Contains(recorder.Requests, r => r.Method == HttpMethod.Get
            && r.Path == $"{Routes.ClassCoursePackagesOfLesson(Cc, Lesson)}&status={PackageUploadStatuses.Validating}");
        Assert.Contains(recorder.Requests, r => r.Method == HttpMethod.Get && r.Path == Routes.ClassCoursePackages(Cc));
    }

    [Theory]
    [InlineData(PackageKinds.Scorm, "application/zip", "lesson.zip")]
    [InlineData(PackageKinds.H5p, "application/octet-stream", "lesson.h5p")]
    public async Task Upload_sends_the_lesson_and_kind_before_the_file_with_the_content_type_of_the_kind(string kind, string contentType, string name)
    {
        var (api, recorder) = Create();

        await api.UploadAsync(Cc, kind, Lesson, name, new MemoryStream([1, 2, 3])).ContinueWith(_ => { });

        var sent = Assert.Single(recorder.Requests);
        Assert.Equal(Routes.ClassCoursePackages(Cc), sent.Path);
        Assert.Contains($"name={PackageUploadForm.LessonKey}", sent.Body.Replace("\"", ""));
        Assert.Contains(Lesson.ToString(), sent.Body);
        Assert.Contains($"name={PackageUploadForm.Kind}", sent.Body.Replace("\"", ""));
        Assert.Contains($"Content-Type: {contentType}", sent.Body);
        Assert.True(sent.Body.IndexOf(PackageUploadForm.LessonKey, StringComparison.Ordinal) < sent.Body.IndexOf(name, StringComparison.Ordinal));
    }
}
