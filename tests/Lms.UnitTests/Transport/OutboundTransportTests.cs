using Lms.Domain.Abstractions;
using Lms.Infrastructure.Email;
using Lms.Infrastructure.Sms;
using Microsoft.Extensions.Logging;

namespace Lms.UnitTests.Transport;

/// <summary>The outbound content policy, the SMS templates and the email guard (M6 head start, D66, D67, D68).</summary>
public sealed class OutboundTransportTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2027, 2, 7, 6, 0, 0, DateTimeKind.Utc);
    }

    private static SmsMessage Sms(string to = "+966501234567", string? app = "LMS", string language = "en", string key = SmsTemplates.GenericNotice) =>
        new(to, key, app is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["AppName"] = app }, language);

    [Theory]
    [InlineData("Your child's grade is 85")]
    [InlineData("Final score: 7/10")]
    [InlineData("Ahmed was ABSENT today")]
    [InlineData("Attendance report attached")]
    [InlineData("Result 92.5 %")]
    [InlineData("درجة الطالب في الرياضيات")]
    [InlineData("تم تسجيل غياب الطالب")]
    [InlineData("نسبة الحضور منخفضة")]
    public void The_policy_catches_grades_scores_and_attendance_in_english_and_arabic(string text)
    {
        Assert.NotNull(OutboundContentPolicy.FindSensitive(text));
        var thrown = Assert.Throws<SensitiveContentException>(() => OutboundContentPolicy.EnsureSafe("hello", text));
        Assert.DoesNotContain(text, thrown.Message); // the refusal never echoes the text
    }

    [Theory]
    [InlineData("You have a new notification. Open the app to read it.")]
    [InlineData("لديك إشعار جديد. افتح التطبيق للاطلاع عليه.")]
    [InlineData("Your password was reset")]
    [InlineData("")]
    [InlineData(null)]
    public void The_policy_lets_generic_text_through(string? text) => OutboundContentPolicy.EnsureSafe(text);

    [Fact]
    public void Every_sms_template_is_in_both_languages_and_passes_the_policy_itself()
    {
        foreach (var key in SmsTemplates.Keys)
        {
            foreach (var language in new[] { "ar", "en" })
            {
                var text = SmsTemplates.Render(Sms(app: "LMS", language: language, key: key));
                Assert.Contains("LMS", text);
                Assert.Null(OutboundContentPolicy.FindSensitive(text));
            }
        }

        Assert.NotEqual(SmsTemplates.Render(Sms(language: "ar")), SmsTemplates.Render(Sms(language: "en")));
    }

    [Fact]
    public void An_sms_that_breaks_the_template_rules_never_renders()
    {
        Assert.Throws<ArgumentException>(() => SmsTemplates.Render(Sms(key: "free-text")));
        Assert.Throws<ArgumentException>(() => SmsTemplates.Render(Sms(to: "0501234567")));
        Assert.Throws<ArgumentException>(() => SmsTemplates.Render(Sms(to: "+9665012345678901")));
        Assert.Throws<ArgumentException>(() => SmsTemplates.Render(Sms(app: null)));
        Assert.Throws<ArgumentException>(() => SmsTemplates.Render(Sms(app: new string('x', SmsTemplates.MaxParameterLength + 1))));
        Assert.Throws<ArgumentException>(() => SmsTemplates.Render(Sms(app: "LMS\nline")));
        Assert.Throws<ArgumentException>(() => SmsTemplates.Render(
            new SmsMessage("+966501234567", SmsTemplates.GenericNotice, new Dictionary<string, string> { ["AppName"] = "LMS", ["StudentName"] = "Ahmed" })));
    }

    [Fact]
    public void A_parameter_value_cannot_smuggle_a_grade_into_the_text()
    {
        Assert.Throws<SensitiveContentException>(() => SmsTemplates.Render(Sms(app: "grade 85%")));
        Assert.Throws<SensitiveContentException>(() => SmsTemplates.Render(Sms(app: "الغياب")));
    }

    [Fact]
    public async Task The_fake_sms_provider_renders_like_a_gateway_keeps_the_text_and_logs_a_masked_number()
    {
        var logger = new ListLogger<FakeSmsProvider>();
        var provider = new FakeSmsProvider(logger, new FixedClock());

        var result = await provider.SendAsync(Sms(language: "ar"));

        var sent = Assert.Single(provider.Sent);
        Assert.StartsWith("fake-", result.MessageId);
        Assert.Equal(("+966501234567", SmsTemplates.GenericNotice), (sent.To, sent.TemplateKey));
        Assert.Contains("إشعار", sent.Text);
        var line = Assert.Single(logger.Lines);
        Assert.Contains("***67", line);
        Assert.DoesNotContain("501234567", line);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.SendAsync(Sms(key: "nope")));
        Assert.Single(provider.Sent); // a refused message is not recorded
    }

    [Fact]
    public async Task A_notification_email_with_a_grade_is_blocked_before_the_transport_and_an_account_email_passes()
    {
        var inner = new RecordingEmailSender();
        var guarded = new GuardedEmailSender(inner);

        await Assert.ThrowsAsync<SensitiveContentException>(() => guarded.SendAsync(new EmailMessage("a@b.c", "New grade", "<p>Your score is 9</p>", IsNotification: true)));
        await Assert.ThrowsAsync<SensitiveContentException>(() => guarded.SendAsync(new EmailMessage("a@b.c", "Update", "<p>تم تسجيل غياب</p>", IsNotification: true)));
        Assert.Empty(inner.Sent);

        await guarded.SendAsync(new EmailMessage("a@b.c", "You have a new notification", "<p>Open the app.</p>", IsNotification: true));
        await guarded.SendAsync(new EmailMessage("a@b.c", "Reset your password", "<p>Follow the link; your grade level is shown after sign-in.</p>")); // not a notification
        Assert.Equal(2, inner.Sent.Count);
    }

    [Fact]
    public async Task The_log_email_provider_sends_nothing_and_logs_neither_the_subject_nor_the_body()
    {
        var logger = new ListLogger<LogEmailSender>();

        await new LogEmailSender(logger).SendAsync(new EmailMessage("student@school.test", "Reset code 123456", "<a href=\"https://x/reset?token=secret\">link</a>"));

        var line = Assert.Single(logger.Lines);
        Assert.Contains("s***@school.test", line);
        Assert.DoesNotContain("123456", line);
        Assert.DoesNotContain("secret", line);
        Assert.DoesNotContain("student@", line);
    }

    [Theory]
    [InlineData("student@school.test", "s***@school.test")]
    [InlineData("not-an-address", "***")]
    [InlineData(null, "***")]
    public void Email_addresses_are_masked_for_logs(string? address, string expected) => Assert.Equal(expected, Masking.Email(address));

    [Theory]
    [InlineData("+966501234567", "***67")]
    [InlineData("123", "***")]
    [InlineData(null, "***")]
    public void Phone_numbers_are_masked_for_logs(string? number, string expected) => Assert.Equal(expected, Masking.Phone(number));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task No_sms_to_a_minor_or_about_another_person_and_the_provider_is_never_called(bool recipientIsMinor, bool concernsAnotherPerson)
    {
        var inner = new FakeSmsProvider(new ListLogger<FakeSmsProvider>(), new FixedClock());
        var guarded = new GuardedSmsProvider(inner);

        var refused = await Assert.ThrowsAsync<SmsRefusedException>(() => guarded.SendAsync(Sms() with { RecipientIsMinor = recipientIsMinor, ConcernsAnotherPerson = concernsAnotherPerson }));

        Assert.Equal("sms_not_allowed_for_minor", refused.Code);
        Assert.Equal(SmsRefusal.NotAllowedForMinor, refused.Code);
        Assert.Empty(inner.Sent);
        await guarded.SendAsync(Sms()); // an adult, about themselves
        Assert.Single(inner.Sent);
    }

    [Fact]
    public async Task No_sms_to_a_guardian_even_for_an_urgent_announcement_with_its_own_code()
    {
        var inner = new FakeSmsProvider(new ListLogger<FakeSmsProvider>(), new FixedClock());
        var guarded = new GuardedSmsProvider(inner);

        var refused = await Assert.ThrowsAsync<SmsRefusedException>(() => guarded.SendAsync(Sms() with { RecipientIsGuardian = true }));
        var minorFirst = await Assert.ThrowsAsync<SmsRefusedException>(() => guarded.SendAsync(Sms() with { RecipientIsGuardian = true, ConcernsAnotherPerson = true }));

        Assert.Equal("sms_not_allowed_for_guardian", refused.Code);
        Assert.Equal(SmsRefusal.NotAllowedForGuardian, refused.Code);
        Assert.Equal(SmsRefusal.NotAllowedForMinor, minorFirst.Code); // news about a child is refused as such, whoever the recipient is
        Assert.Empty(inner.Sent);
    }
}
