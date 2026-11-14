using System.Security.Cryptography;
using System.Text.Json;
using Lms.Shared.Grading;
using Microsoft.AspNetCore.DataProtection;

namespace Lms.Infrastructure.Grading.Exchange;

/// <summary>One row a preview accepted: the student and the points to write (rows with an error or no points are not in the token).</summary>
public sealed record TokenRow(Guid StudentId, decimal Points);

/// <summary>What a preview token carries: who may apply it, to which gradebook and item, for which file (SHA-256), and the accepted rows.</summary>
public sealed record ImportTokenPayload(Guid UploaderUserId, Guid GradebookId, Guid ItemId, string FileSha256, IReadOnlyList<TokenRow> Rows);

/// <summary>
/// The preview token of a column import (M5 Task 10, L9): Data Protection, time limited to <see cref="GradingLimits.ImportTokenMinutes"/> minutes, bound to
/// (uploader, gradebook, item, file hash). A token that is expired, tampered with or made by another application key is the same answer: refused.
/// </summary>
public sealed class ImportPreviewToken(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("Lms.Grading.ImportPreview.v1").ToTimeLimitedDataProtector();

    public static string Sha256(byte[] file) => Convert.ToHexString(SHA256.HashData(file));

    public (string Token, DateTime ExpiresAtUtc) Issue(ImportTokenPayload payload)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(GradingLimits.ImportTokenMinutes);
        return (_protector.Protect(JsonSerializer.Serialize(payload), expires), expires.UtcDateTime);
    }

    /// <summary>The payload, or null when the token is expired, forged or malformed.</summary>
    public ImportTokenPayload? Read(string token)
    {
        try
        {
            return JsonSerializer.Deserialize<ImportTokenPayload>(_protector.Unprotect(token));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }
}
