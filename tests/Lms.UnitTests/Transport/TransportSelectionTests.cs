using Lms.Domain.Abstractions;
using Lms.Infrastructure.Email;
using Lms.Infrastructure.Sms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.UnitTests.Transport;

/// <summary>The email and SMS providers are chosen by configuration only (D67, D68).</summary>
public sealed class TransportSelectionTests
{
    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2027, 2, 7, 6, 0, 0, DateTimeKind.Utc);
    }

    private static ServiceProvider Build(Action<IServiceCollection, IConfiguration> add, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock, FixedClock>();
        add(services, configuration);
        return services.BuildServiceProvider();
    }

    private static readonly EmailMessage Plain = new("a@b.c", "Hello", "<p>Hello</p>");

    [Fact]
    public async Task Email_provider_log_sends_nothing_and_survives_without_any_smtp_server()
    {
        using var sp = Build((s, c) => s.AddEmail(c), ("Email:Provider", "Log"), ("Email:SmtpPort", "1")); // port 1 would refuse a connection

        await sp.GetRequiredService<IEmailSender>().SendAsync(Plain);
    }

    [Fact]
    public async Task Email_provider_smtp_is_the_default_and_really_connects()
    {
        using var sp = Build((s, c) => s.AddEmail(c), ("Email:SmtpHost", "127.0.0.1"), ("Email:SmtpPort", "1")); // nothing listens: the SMTP sender must fail

        await Assert.ThrowsAnyAsync<Exception>(() => sp.GetRequiredService<IEmailSender>().SendAsync(Plain));
    }

    [Fact]
    public void An_unknown_email_provider_stops_the_startup_with_a_clear_message()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => Build((s, c) => s.AddEmail(c), ("Email:Provider", "Sendgrid")));

        Assert.Contains("Email:Provider", thrown.Message);
        Assert.Contains("D67", thrown.Message);
    }

    [Fact]
    public async Task Sms_provider_fake_is_the_default_and_the_same_instance_serves_both_registrations()
    {
        using var sp = Build((s, c) => s.AddSms(c));

        var provider = sp.GetRequiredService<ISmsProvider>();
        await provider.SendAsync(new SmsMessage("+966501234567", SmsTemplates.GenericNotice, new Dictionary<string, string> { ["AppName"] = "LMS" }));

        Assert.IsType<FakeSmsProvider>(provider);
        Assert.Same(provider, sp.GetRequiredService<FakeSmsProvider>());
        Assert.Single(sp.GetRequiredService<FakeSmsProvider>().Sent);
    }

    [Theory]
    [InlineData("Unifonic")]
    [InlineData("Taqnyat")]
    [InlineData("")]
    public void An_sms_gateway_that_has_no_adapter_yet_stops_the_startup(string provider)
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => Build((s, c) => s.AddSms(c), ("Sms:Provider", provider)));

        Assert.Contains("Sms:Provider", thrown.Message);
        Assert.Contains("D68", thrown.Message);
    }

    [Fact]
    public async Task The_email_guard_is_always_in_front_of_the_chosen_provider()
    {
        using var sp = Build((s, c) => s.AddEmail(c), ("Email:Provider", "Log"));

        await Assert.ThrowsAsync<SensitiveContentException>(() =>
            sp.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage("a@b.c", "Update", "<p>Your grade changed</p>", IsNotification: true)));
        Assert.IsType<GuardedEmailSender>(sp.GetRequiredService<IEmailSender>());
        _ = NullLogger.Instance;
    }
}
