using System.Security.Cryptography;
using System.Text;
using Lms.Infrastructure.Zoom;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomWebhookVerifierTests
{
    private const string Secret = "webhook-test-secret";
    private static readonly DateTime Now = new(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc);

    private static ZoomWebhookVerifier Verifier(ManualClock? clock = null) => new(Secret, clock ?? new ManualClock(Now));

    private static string Timestamp(DateTime at) => new DateTimeOffset(at).ToUnixTimeSeconds().ToString();

    private static string Sign(string timestamp, byte[] body, string secret = Secret)
    {
        var message = Encoding.UTF8.GetBytes($"v0:{timestamp}:").Concat(body).ToArray();
        return "v0=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message)).ToLowerInvariant();
    }

    private static byte[] Body(string json = """{"event":"meeting.ended","payload":{"object":{"id":"1"}}}""") => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void A_correctly_signed_fresh_request_is_valid()
    {
        var ts = Timestamp(Now);
        var body = Body();

        Assert.Equal(ZoomVerification.Valid, Verifier().Verify(body, ts, Sign(ts, body)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v0=")]
    [InlineData("v0=zzzz")]
    [InlineData("nothex")]
    [InlineData("v1=00")]
    public void A_missing_or_malformed_signature_is_refused(string? signature)
    {
        var ts = Timestamp(Now);

        Assert.Equal(ZoomVerification.BadSignature, Verifier().Verify(Body(), ts, signature));
    }

    [Fact]
    public void A_signature_made_with_another_secret_is_refused()
    {
        var ts = Timestamp(Now);
        var body = Body();

        Assert.Equal(ZoomVerification.BadSignature, Verifier().Verify(body, ts, Sign(ts, body, "someone-elses-secret")));
    }

    [Fact]
    public void The_signature_covers_the_raw_bytes_so_re_serialised_json_does_not_verify()
    {
        var ts = Timestamp(Now);
        var signed = Body("""{"event":"meeting.ended","payload":{"object":{"id":"1"}}}""");
        var reformatted = Body("""{ "event": "meeting.ended", "payload": { "object": { "id": "1" } } }""");

        Assert.Equal(ZoomVerification.Valid, Verifier().Verify(signed, ts, Sign(ts, signed)));
        Assert.Equal(ZoomVerification.BadSignature, Verifier().Verify(reformatted, ts, Sign(ts, signed)));
    }

    [Fact]
    public void The_timestamp_is_part_of_the_signed_message()
    {
        var body = Body();
        var signedFor = Timestamp(Now);
        var replayedWith = Timestamp(Now.AddSeconds(30));

        Assert.Equal(ZoomVerification.BadSignature, Verifier().Verify(body, replayedWith, Sign(signedFor, body)));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    [InlineData(-86400)]
    public void A_correctly_signed_request_outside_the_five_minute_window_is_stale(int offsetSeconds)
    {
        var ts = Timestamp(Now.AddSeconds(offsetSeconds));
        var body = Body();

        Assert.Equal(ZoomVerification.StaleTimestamp, Verifier().Verify(body, ts, Sign(ts, body)));
    }

    [Theory]
    [InlineData(-299)]
    [InlineData(299)]
    public void The_window_edges_are_inclusive_of_five_minutes(int offsetSeconds)
    {
        var ts = Timestamp(Now.AddSeconds(offsetSeconds));
        var body = Body();

        Assert.Equal(ZoomVerification.Valid, Verifier().Verify(body, ts, Sign(ts, body)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    public void A_missing_or_unparseable_timestamp_is_refused_as_a_bad_signature(string? timestamp)
    {
        var body = Body();

        Assert.Equal(ZoomVerification.BadSignature, Verifier().Verify(body, timestamp, Sign("0", body)));
    }

    [Fact]
    public void A_bad_signature_is_reported_before_a_stale_timestamp()
    {
        var ts = Timestamp(Now.AddDays(-1));

        Assert.Equal(ZoomVerification.BadSignature, Verifier().Verify(Body(), ts, "v0=00"));
    }

    [Fact]
    public void Without_a_configured_secret_nothing_verifies()
    {
        var ts = Timestamp(Now);
        var body = Body();
        var verifier = new ZoomWebhookVerifier("", new ManualClock(Now));

        Assert.Equal(ZoomVerification.BadSignature, verifier.Verify(body, ts, Sign(ts, body, "")));
    }

    [Fact]
    public void The_url_validation_challenge_is_answered_with_the_hex_hmac_of_the_plain_token()
    {
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes("abc123"))).ToLowerInvariant();

        Assert.Equal(expected, Verifier().EncryptPlainToken("abc123"));
    }
}
