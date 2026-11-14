using Lms.Domain.Abstractions;
using Lms.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace Lms.UnitTests.Transport;

/// <summary>The outbound content policy, the email guard and the log email provider (M6 head start, D66, D67).</summary>
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

}
