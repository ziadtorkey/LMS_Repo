using System.Net;
using System.Net.Http.Json;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Content;

namespace Lms.Client.Tests.Services;

/// <summary>The upload endpoint streams the multipart body and reads the owner (classCourseId or orgUnitId) before the file.</summary>
public sealed class UploadFormOrderTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AssetDto(Guid.NewGuid(), Guid.NewGuid(), AssetKinds.Document, "a.pdf", "application/pdf", 3, null, true, DateTime.UtcNow)),
            };
        }
    }

    private static async Task<string> UploadAsync(Func<AssetsApi, Task> upload)
    {
        var capture = new Capture();
        var api = new AssetsApi(new ApiHttp(new HttpClient(capture) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()));
        await upload(api);
        return capture.Body;
    }

    [Fact]
    public async Task Class_course_id_comes_before_the_file_part()
    {
        var body = await UploadAsync(a => a.UploadAsync(Guid.NewGuid(), "a.pdf", "application/pdf", new MemoryStream([1, 2, 3])));

        var owner = body.IndexOf("name=classCourseId", StringComparison.Ordinal);
        var file = body.IndexOf("name=file", StringComparison.Ordinal);
        Assert.True(owner >= 0 && file >= 0);
        Assert.True(owner < file, "classCourseId must precede the file part");
    }
}
