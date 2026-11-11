using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Lms.Api.Features.Students.Import;

/// <summary>One issued account of an import, as it appears on the credentials sheet.</summary>
internal sealed record IssuedCredentialRow(string Code, string NameAr, string NameEn, string UserName, string? Password, string DeliveredTo);

/// <summary>
/// The one-time credentials sheet of a committed student import (D24): the issued passwords are kept for 15 minutes, encrypted with
/// a time-limited Data Protection key, in memory only (never in <c>ImportRow</c>, storage or logs), and handed to the requester
/// exactly once. The cache is per node: the download must reach the node that ran the commit job (single-node deployments, or
/// session affinity), otherwise the sheet is simply gone and the credentials were still emailed or shown at creation.
/// </summary>
internal sealed class ImportCredentialsStore(IMemoryCache cache, IDataProtectionProvider protection)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private sealed record Payload(Guid RequestedBy, string Culture, IReadOnlyList<IssuedCredentialRow> Rows);

    public sealed record Sheet(string Culture, IReadOnlyList<IssuedCredentialRow> Rows);

    private ITimeLimitedDataProtector Protector => protection.CreateProtector("Lms.ImportCredentials.v1").ToTimeLimitedDataProtector();

    private static string Key(Guid batchId) => "import-credentials:" + batchId.ToString("N");

    public void Put(Guid batchId, Guid requestedBy, string culture, IReadOnlyList<IssuedCredentialRow> rows)
    {
        var json = JsonSerializer.Serialize(new Payload(requestedBy, culture, rows));
        cache.Set(Key(batchId), Protector.Protect(json, Lifetime), Lifetime);
    }

    /// <summary>The sheet, once, for the requester only; anyone else (or a second call, or after 15 minutes) gets null.</summary>
    public Sheet? Take(Guid batchId, Guid? userId)
    {
        if (userId is null || !cache.TryGetValue(Key(batchId), out string? blob) || blob is null)
        {
            return null;
        }

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(Protector.Unprotect(blob));
        }
        catch (CryptographicException)
        {
            cache.Remove(Key(batchId)); // expired or unreadable
            return null;
        }

        if (payload is null || payload.RequestedBy != userId)
        {
            return null; // somebody else asking does not use it up
        }

        cache.Remove(Key(batchId));
        return new Sheet(payload.Culture, payload.Rows);
    }

    public static void Write(Sheet sheet, Stream output)
    {
        var arabic = sheet.Culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Credentials");
        string[] headers = arabic
            ? ["كود الطالب", "الاسم بالعربية", "الاسم بالإنجليزية", "اسم المستخدم", "كلمة المرور المؤقتة", "أُرسلت إلى"]
            : ["Student code", "Name (Arabic)", "Name (English)", "User name", "Temporary password", "Sent to"];
        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        worksheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        worksheet.RightToLeft = arabic;
        worksheet.Column(1).Style.NumberFormat.Format = "@";
        worksheet.Column(5).Style.NumberFormat.Format = "@";
        var line = 2;
        foreach (var row in sheet.Rows)
        {
            worksheet.Cell(line, 1).Value = row.Code;
            worksheet.Cell(line, 2).Value = row.NameAr;
            worksheet.Cell(line, 3).Value = row.NameEn;
            worksheet.Cell(line, 4).Value = row.UserName;
            worksheet.Cell(line, 5).Value = row.Password ?? string.Empty;
            worksheet.Cell(line, 6).Value = row.DeliveredTo;
            line++;
        }

        worksheet.Columns().AdjustToContents(1, line, 10, 60);
        workbook.SaveAs(output);
    }
}
