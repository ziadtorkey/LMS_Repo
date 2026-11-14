using System.Text;
using System.Text.Json;
using Lms.Infrastructure.Comms.Push;

namespace Lms.UnitTests.Comms;

/// <summary>M6 Task 8: the Web Push payload carries a short title and body, a deep link and ids only (L9, L22); it never exceeds 2 KB.</summary>
public sealed class PushPayloadBuilderTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Payload_has_title_body_link_and_ids_only()
    {
        var bytes = PushPayloadBuilder.Build("Grades.Published", Id, "New grade", "Open your grades", "/grades");
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        Assert.Equal("New grade", root.GetProperty("title").GetString());
        Assert.Equal("Open your grades", root.GetProperty("body").GetString());
        Assert.Equal("/grades", root.GetProperty("url").GetString());
        Assert.Equal(["body", "data", "title", "url"], root.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["deliveryId", "eventKey"], root.GetProperty("data").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(Id, root.GetProperty("data").GetProperty("deliveryId").GetGuid());
    }

    [Fact]
    public void Payload_never_exceeds_two_kilobytes_and_stays_valid_json_with_arabic_text()
    {
        var long_ = new string('ع', 5000);

        var bytes = PushPayloadBuilder.Build("Announcements.Published", Id, long_, long_, "/announcements/" + new string('a', 3000));

        Assert.True(bytes.Length <= PushPayloadBuilder.MaxBytes, bytes.Length.ToString());
        using var doc = JsonDocument.Parse(bytes);
        Assert.StartsWith("ع", doc.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\t/evil.example")]
    [InlineData("/\n/evil.example")]
    [InlineData("/\r/evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("/%2f%2fevil")]
    [InlineData("/.//evil")]
    [InlineData("/a/../b")]
    [InlineData("/a b")]
    public void A_deep_link_that_is_not_a_local_path_is_dropped_to_the_root(string link)
    {
        var bytes = PushPayloadBuilder.Build("Grades.Published", Id, "t", "b", link);

        using var doc = JsonDocument.Parse(bytes);
        Assert.Equal("/", doc.RootElement.GetProperty("url").GetString());
        Assert.DoesNotContain("evil", Encoding.UTF8.GetString(bytes));
    }
}
