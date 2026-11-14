using System.Net;
using System.Net.Sockets;
using System.Text;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Email;
using Lms.Infrastructure.Sms;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lms.IntegrationTests.Transport;

/// <summary>
/// The email and SMS adapters inside the real host (M6 head start, D66-D68): the provider follows the configuration, the SMTP adapter speaks to an SMTP server like
/// Mailpit, and a notification message with sensitive data never reaches the wire.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransportAdapterTests(LmsApiFactory factory)
{
    /// <summary>A one-connection-at-a-time SMTP server that accepts everything and keeps the DATA of each mail (the part of Mailpit the adapter needs).</summary>
    private sealed class StubSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public StubSmtpServer()
        {
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<string> Mails { get; } = [];

        public int Connections { get; private set; }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    Connections++;
                    await ServeAsync(client);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // stopped
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 stub ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                var verb = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
                switch (verb)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-stub");
                        await writer.WriteLineAsync("250 8BITMIME");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go ahead");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } part && part != ".")
                        {
                            data.AppendLine(part);
                        }

                        lock (Mails)
                        {
                            Mails.Add(data.ToString());
                        }

                        await writer.WriteLineAsync("250 queued");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return;
                    default:
                        await writer.WriteLineAsync("250 ok");
                        break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
        }
    }

    private static SmtpEmailSender SenderFor(StubSmtpServer server) =>
        new(Options.Create(new EmailOptions { SmtpHost = "127.0.0.1", SmtpPort = server.Port, From = "no-reply@lms.test", FromName = "LMS" }));

    [Fact]
    public async Task The_smtp_adapter_delivers_a_mail_to_an_smtp_server_like_mailpit()
    {
        await using var server = new StubSmtpServer();

        await SenderFor(server).SendAsync(new EmailMessage("parent@school.test", "Welcome to the LMS", "<p>Hello</p>"));

        var mail = Assert.Single(server.Mails);
        Assert.Contains("parent@school.test", mail);
        Assert.Contains("Subject: Welcome to the LMS", mail);
        Assert.Contains("no-reply@lms.test", mail);
    }

    [Fact]
    public async Task A_notification_email_with_a_grade_never_opens_a_connection_while_a_generic_one_is_delivered()
    {
        await using var server = new StubSmtpServer();
        var guarded = new GuardedEmailSender(SenderFor(server));

        await Assert.ThrowsAsync<SensitiveContentException>(() =>
            guarded.SendAsync(new EmailMessage("parent@school.test", "Result", "<p>Your child's grade is 85%</p>", IsNotification: true)));
        Assert.Equal(0, server.Connections);

        await guarded.SendAsync(new EmailMessage("parent@school.test", "New notification", "<p>Open the app to read it.</p>", IsNotification: true));
        Assert.Single(server.Mails);
    }

    [Fact]
    public void The_host_default_is_the_guarded_smtp_adapter_and_the_fake_sms_provider()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<GuardedEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        Assert.IsType<FakeSmsProvider>(scope.ServiceProvider.GetRequiredService<ISmsProvider>());
    }

    [Fact]
    public async Task The_provider_is_chosen_by_configuration_in_the_real_host()
    {
        await using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Email:Provider", "Log");
            b.UseSetting("Email:SmtpPort", "1"); // an SMTP adapter would fail here; the Log adapter must not
        });
        using var scope = host.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage("a@b.c", "Hello", "<p>Hello</p>"));

        var sms = scope.ServiceProvider.GetRequiredService<ISmsProvider>();
        await sms.SendAsync(new SmsMessage("+966501234567", SmsTemplates.GenericNotice, new Dictionary<string, string> { ["AppName"] = "LMS" }, "ar"));
        var sent = Assert.Single(scope.ServiceProvider.GetRequiredService<FakeSmsProvider>().Sent);
        Assert.DoesNotContain("501234567", sent.Text);
        Assert.Contains("LMS", sent.Text);
    }

    [Fact]
    public void A_host_configured_with_an_unknown_sms_gateway_does_not_start()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Sms:Provider", "Unifonic"));

        var thrown = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains("Sms:Provider", thrown.ToString());
    }
}
