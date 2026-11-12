using System.Text;
using Lms.Infrastructure.Content;

namespace Lms.UnitTests.Content;

public sealed class AssetSignaturesTests
{
    [Theory]
    [InlineData(".pdf", "%PDF-1.7 x")]
    [InlineData(".gif", "GIF89a....")]
    [InlineData(".ogg", "OggS....")]
    [InlineData(".mp3", "ID3....")]
    [InlineData(".txt", "plain text, with العربية")]
    public void Real_headers_match(string extension, string header) =>
        Assert.True(AssetSignatures.Matches(extension, Encoding.UTF8.GetBytes(header)));

    [Fact]
    public void Binary_formats_match_their_signature()
    {
        Assert.True(AssetSignatures.Matches(".png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]));
        Assert.True(AssetSignatures.Matches(".JPG", [0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.True(AssetSignatures.Matches(".docx", [0x50, 0x4B, 0x03, 0x04, 0]));
        Assert.True(AssetSignatures.Matches(".webm", [0x1A, 0x45, 0xDF, 0xA3]));
        Assert.True(AssetSignatures.Matches(".mp4", [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0]));
        Assert.True(AssetSignatures.Matches(".wav", "RIFF\0\0\0\0WAVEfmt "u8));
        Assert.True(AssetSignatures.Matches(".webp", "RIFF\0\0\0\0WEBPVP8 "u8));
    }

    [Theory]
    [InlineData(".png", "<html><script>alert(1)</script></html>")]
    [InlineData(".pdf", "<!doctype html>")]
    [InlineData(".jpg", "GIF89a")]
    [InlineData(".txt", "<html>x</html>")]
    [InlineData(".txt", "  \n<svg onload=x>")]
    [InlineData(".mp4", "short")]
    [InlineData(".exe", "MZ")]
    public void Mismatched_content_is_refused(string extension, string header) =>
        Assert.False(AssetSignatures.Matches(extension, Encoding.UTF8.GetBytes(header)));

    [Fact]
    public void Text_with_a_nul_byte_is_refused() =>
        Assert.False(AssetSignatures.Matches(".txt", [(byte)'a', 0, (byte)'b']));
}
