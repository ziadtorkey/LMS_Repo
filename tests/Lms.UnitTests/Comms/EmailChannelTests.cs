using System.Net.Sockets;
using System.Text;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Lms.UnitTests.Comms;

/// <summary>M6 Task 6 (provider half): rendering, the SMTP provider's error mapping, the fake and the relay event parser.</summary>
public sealed class EmailChannelTests
{
    private sealed class Texts(Dictionary<string, string> map) : IEmailTemplateText
    {
        public string? Get(string key, string language) => map.GetValueOrDefault($"{language}:{key}");
    }

    private static EmailTemplateRenderer Renderer() => new(new Texts(new Dictionary<string, string>
    {
        ["ar:Email_Thing_Subject"] = "شيء جديد في {Course}",
        ["en:Email_Thing_Subject"] = "Something new in {Course}",
        ["ar:Email_Thing_Body"] = "مرحبا {Name}\nهناك جديد في {Course}.",
        ["en:Email_Thing_Body"] = "Hello {Name}\nThere is news in {Course}.",
    }));

    private static Dictionary<string, string> Parameters() => new()
    {
        ["Name"] = "Sara",
        ["CourseAr"] = "الرياضيات",
        ["CourseEn"] = "Maths",
        [EmailTemplateRenderer.LinkParameter] = "https://lms.example/inbox",
    };

    [Fact]
    public void Email_renders_in_the_recipients_language_with_a_text_alternative()
    {
        var ar = Renderer().Render("Thing", "ar-SA", Parameters());
        var en = Renderer().Render("Thing", "en-GB", Parameters());

        Assert.Equal("شيء جديد في الرياضيات", ar.Subject);
        Assert.Contains("dir=\"rtl\"", ar.HtmlBody);
        Assert.Contains("lang=\"ar\"", ar.HtmlBody);
        Assert.Equal("Something new in Maths", en.Subject);
        Assert.Contains("dir=\"ltr\"", en.HtmlBody);
        Assert.Contains("<p>Hello Sara</p>", en.HtmlBody);
        Assert.Equal("Hello Sara\nThere is news in Maths.\n\nhttps://lms.example/inbox", en.TextBody);
        Assert.Contains("href=\"https://lms.example/inbox\"", en.HtmlBody);
    }

    [Fact]
    public void Values_are_html_encoded_and_a_subject_is_one_line()
    {
        var p = Parameters();
        p["Name"] = "<script>alert(1)</script>";
        p["CourseEn"] = "Maths\r\nBcc: x@y.z";

        var en = Renderer().Render("Thing", "en", p);

        Assert.DoesNotContain("<script>", en.HtmlBody);
        Assert.Contains("&lt;script&gt;", en.HtmlBody);
        Assert.DoesNotContain('\n', en.Subject);
        Assert.DoesNotContain('\r', en.Subject);
    }

    [Theory]
    [InlineData("Body")]
    [InlineData("MessageText")]
    [InlineData("content")]
    public void Email_body_never_contains_message_text(string parameter)
    {
        var p = Parameters();
        p[parameter] = "the private message";

        Assert.Throws<ArgumentException>(() => Renderer().Render("Thing", "en", p));
    }

    [Fact]
    public void A_missing_template_is_an_error_not_an_empty_email()
    {
        Assert.Throws<EmailTemplateMissingException>(() => Renderer().Render("Nope", "en", Parameters()));
    }

    [Fact]
    public async Task The_fake_records_sends_and_answers_the_scripted_results_in_order()
    {
        var fake = new FakeEmailProvider();
        fake.ScriptNext(EmailSendResult.Transient("network"), EmailSendResult.Permanent("smtp_5xx"));
        var envelope = new EmailEnvelope("a@b.c", "s", "<p>h</p>", "h");

        var first = await fake.SendAsync(envelope, default);
        var second = await fake.SendAsync(envelope, default);
        var third = await fake.SendAsync(envelope, default);

        Assert.Equal([EmailSendStatus.Transient, EmailSendStatus.Permanent, EmailSendStatus.Accepted], [first.Status, second.Status, third.Status]);
        Assert.Equal(3, fake.Sent.Count);
        Assert.NotNull(third.ProviderMessageId);
    }

    [Fact]
    public void Smtp_errors_map_to_retry_stop_or_setup_with_a_code_only()
    {
        static SmtpCommandException Command(SmtpStatusCode code) => new(SmtpErrorCode.UnexpectedStatusCode, code, "secret address a@b.c");

        Assert.Equal(EmailSendStatus.Permanent, SmtpErrorMapper.Map(Command(SmtpStatusCode.MailboxUnavailable)).Status);
        Assert.Equal(EmailSendStatus.Transient, SmtpErrorMapper.Map(Command(SmtpStatusCode.MailboxBusy)).Status);
        Assert.Equal(EmailSendStatus.Transient, SmtpErrorMapper.Map(new SocketException()).Status);
        Assert.Equal(EmailSendStatus.Transient, SmtpErrorMapper.Map(new IOException()).Status);
        Assert.Equal(EmailSendStatus.NotConfigured, SmtpErrorMapper.Map(new AuthenticationException("bad password hunter2")).Status);
        Assert.Equal(EmailSendStatus.Permanent, SmtpErrorMapper.Map(new ParseException("x", 0, 1)).Status);
        var result = SmtpErrorMapper.Map(Command(SmtpStatusCode.MailboxUnavailable));
        Assert.Equal("smtp_5xx", result.ErrorCode);
        Assert.DoesNotContain("a@b.c", result.ErrorCode);
    }

    [Fact]
    public async Task Provider_without_a_host_is_not_configured_and_never_connects()
    {
        var provider = new MailKitEmailProvider(Options.Create(new Lms.Infrastructure.Email.EmailOptions { SmtpHost = "" }), NullLogger<MailKitEmailProvider>.Instance);

        var result = await provider.SendAsync(new EmailEnvelope("a@b.c", "s", "h", "h"), default);

        Assert.Equal(EmailSendStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task Provider_against_a_closed_port_is_a_transient_network_failure_without_leaking_the_password()
    {
        var options = new Lms.Infrastructure.Email.EmailOptions { SmtpHost = "127.0.0.1", SmtpPort = 1, UserName = "u", Password = "hunter2-secret" };
        var provider = new MailKitEmailProvider(Options.Create(options), NullLogger<MailKitEmailProvider>.Instance);

        var result = await provider.SendAsync(new EmailEnvelope("a@b.c", "s", "h", "h"), default);

        Assert.Equal(EmailSendStatus.Transient, result.Status);
        Assert.DoesNotContain("hunter2", result.ErrorCode ?? string.Empty);
    }

    // ---- relay events -------------------------------------------------------------------------------------------------

    private const string Secret = "test-relay-secret";
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static string Stamp(DateTime at) => new DateTimeOffset(at).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static (string Ts, string Sig, byte[] Body) Signed(string json, DateTime? at = null)
    {
        var ts = Stamp(at ?? Now);
        var body = Encoding.UTF8.GetBytes(json);
        return (ts, FakeRelayEventParser.Sign(Secret, ts, body), body);
    }

    private const string TwoEvents =
        """{"events":[{"id":"e1","type":"hard_bounce","messageId":"m1","address":"x@y.z","at":"2026-11-20T11:59:00Z"},{"id":"e2","type":"mystery","messageId":"m2","at":"2026-11-20T11:59:00Z"},{"id":"e3","type":"complaint","messageId":"m3","at":"2026-11-20T11:59:30Z"}]}""";

    [Fact]
    public void A_valid_event_body_is_read_and_unknown_types_are_skipped()
    {
        var (ts, sig, body) = Signed(TwoEvents);

        var result = new FakeRelayEventParser(Secret).Parse(ts, sig, body, Now);

        Assert.Equal(EmailEventParseStatus.Ok, result.Status);
        Assert.Equal([EmailEventKind.HardBounce, EmailEventKind.Complaint], result.Events.Select(e => e.Kind));
        Assert.Equal("m1", result.Events[0].ProviderMessageId);
    }

    [Fact]
    public void Bad_or_missing_signature_reads_nothing()
    {
        var (ts, _, body) = Signed(TwoEvents);
        var parser = new FakeRelayEventParser(Secret);

        Assert.Equal(EmailEventParseStatus.BadSignature, parser.Parse(ts, "deadbeef", body, Now).Status);
        Assert.Equal(EmailEventParseStatus.BadSignature, parser.Parse(ts, null, body, Now).Status);
        Assert.Equal(EmailEventParseStatus.BadSignature, parser.Parse(null, "x", body, Now).Status);
        Assert.Empty(parser.Parse(ts, "deadbeef", body, Now).Events);
    }

    [Fact]
    public void A_tampered_body_fails_the_signature()
    {
        var (ts, sig, body) = Signed(TwoEvents);
        body[^5] ^= 1;

        Assert.Equal(EmailEventParseStatus.BadSignature, new FakeRelayEventParser(Secret).Parse(ts, sig, body, Now).Status);
    }

    [Fact]
    public void A_stale_timestamp_is_refused_even_with_a_valid_signature()
    {
        var (ts, sig, body) = Signed(TwoEvents, Now.AddMinutes(-10));

        Assert.Equal(EmailEventParseStatus.Stale, new FakeRelayEventParser(Secret).Parse(ts, sig, body, Now).Status);
        var (futureTs, futureSig, futureBody) = Signed(TwoEvents, Now.AddMinutes(10));
        Assert.Equal(EmailEventParseStatus.Stale, new FakeRelayEventParser(Secret).Parse(futureTs, futureSig, futureBody, Now).Status);
    }

    [Fact]
    public void An_oversize_body_is_refused_before_anything_else()
    {
        var parser = new FakeRelayEventParser(Secret);

        Assert.Equal(EmailEventParseStatus.TooLarge, parser.Parse("1", "x", new byte[parser.MaxBodyBytes + 1], Now).Status);
    }

    [Fact]
    public void With_no_secret_configured_nothing_is_accepted()
    {
        var (ts, sig, body) = Signed(TwoEvents);

        Assert.Equal(EmailEventParseStatus.BadSignature, new FakeRelayEventParser(null).Parse(ts, sig, body, Now).Status);
        Assert.Equal(EmailEventParseStatus.BadSignature, new FakeRelayEventParser("").Parse(ts, sig, body, Now).Status);
    }

    [Fact]
    public void A_signed_body_of_the_wrong_shape_is_malformed()
    {
        var (ts, sig, body) = Signed("""{"nothing":true}""");

        Assert.Equal(EmailEventParseStatus.Malformed, new FakeRelayEventParser(Secret).Parse(ts, sig, body, Now).Status);
    }
}
