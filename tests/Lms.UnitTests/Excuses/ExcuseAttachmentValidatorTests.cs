using System.Text;
using Lms.Domain.Files;
using Lms.Infrastructure.Excuses;
using Lms.Shared.Excuses;

namespace Lms.UnitTests.Excuses;

public sealed class ExcuseAttachmentValidatorTests
{
    private sealed class Scanner(bool clean) : IFileScanner
    {
        public int Calls { get; private set; }

        public Task<FileScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new FileScanResult(clean));
        }
    }

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] Webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj");

    private static Task<ExcuseAttachmentCheck> CheckAsync(
        byte[] bytes, string name = "x.bin", long max = 5 * 1024 * 1024, IFileScanner? scanner = null, bool scan = false) =>
        new ExcuseAttachmentValidator(scanner ?? new NoOpFileScanner(), scan).CheckAsync(new MemoryStream(bytes), name, max);

    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    [InlineData("pdf", "application/pdf")]
    public async Task The_type_comes_from_the_content_and_is_in_the_allow_list(string kind, string expected)
    {
        var bytes = kind switch { "jpeg" => Jpeg, "png" => Png, "webp" => Webp, _ => Pdf };

        var check = await CheckAsync(bytes, "renamed.exe"); // the file name is irrelevant

        Assert.True(check.IsValid);
        Assert.Equal(expected, check.ContentType);
        Assert.Contains(expected, ExcuseLimits.AttachmentContentTypes);
    }

    [Theory]
    [InlineData("MZ\u0090\0\u0003\0\0\0")] // a Windows executable
    [InlineData("<html><script>alert(1)</script>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>")]
    [InlineData("GIF89a....")]
    [InlineData("PK\u0003\u0004zip")]
    public async Task Other_content_is_refused_even_with_an_allowed_name(string text)
    {
        var check = await CheckAsync(Encoding.Latin1.GetBytes(text), "scan.pdf");

        Assert.False(check.IsValid);
        Assert.Equal(ExcuseAttachmentValidator.TypeNotAllowed, check.Refusal);
    }

    [Fact]
    public async Task Empty_and_oversized_files_are_refused()
    {
        Assert.Equal(ExcuseAttachmentValidator.Empty, (await CheckAsync([])).Refusal);
        Assert.Equal(ExcuseAttachmentValidator.TooLarge, (await CheckAsync(Pdf, max: Pdf.Length - 1)).Refusal);
        Assert.True((await CheckAsync(Pdf, max: Pdf.Length)).IsValid);
    }

    [Fact]
    public async Task The_scanner_runs_only_when_enabled_and_can_refuse()
    {
        var off = new Scanner(clean: false);
        Assert.True((await CheckAsync(Pdf, scanner: off, scan: false)).IsValid); // off by default: not asked
        Assert.Equal(0, off.Calls);

        var on = new Scanner(clean: false);
        var check = await CheckAsync(Pdf, scanner: on, scan: true);
        Assert.Equal(ExcuseAttachmentValidator.Infected, check.Refusal);
        Assert.Equal(1, on.Calls);

        Assert.True((await CheckAsync(Pdf, scanner: new Scanner(clean: true), scan: true)).IsValid);
        Assert.True((await CheckAsync(Pdf, scan: true)).IsValid); // the no-op scanner accepts everything
    }

    [Fact]
    public void The_scan_option_defaults_to_off()
    {
        Assert.False(new ExcuseAttachmentOptions().ScanAttachments);
    }
}
