using System.Text.RegularExpressions;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Infrastructure.People;

/// <summary>
/// Student codes (D25): one running sequence per client, taken atomically inside the caller's transaction. Concurrent
/// generators of one client queue on the counter row until the first commits; a rolled-back transaction gives its number
/// back. Codes of deleted students stay taken, so a code is never reused inside a client.
/// </summary>
public sealed partial class StudentCodeGenerator(LmsDbContext db, ISettingsResolver settings, IOrgTree tree) : IStudentCodeGenerator
{
    private const int MaxTries = 100;

    [GeneratedRegex("^[A-Za-z0-9-]{1,30}$")]
    private static partial Regex CodeRule();

    public async Task<string> GenerateAsync(OrgUnit homeUnit, CancellationToken cancellationToken)
    {
        var patternText = (await settings.ResolveAsync(SettingKeys.StudentsCodePattern, homeUnit.Path, cancellationToken)).Value;
        if (!StudentCodePattern.TryParse(patternText, out var pattern, out var error))
        {
            throw new StudentCodeInvalidException($"The student code pattern '{patternText}' is invalid ({error}).");
        }

        var root = await tree.GetRootAsync(homeUnit.OrganizationId, cancellationToken);
        var tokens = new StudentCodeTokens(root.Code);

        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            var seq = await NextValueAsync(homeUnit.OrganizationId, cancellationToken);
            if (seq > pattern!.MaxValue)
            {
                throw new StudentCodeExhaustedException(
                    $"The student code sequence of the client reached {pattern.MaxValue}; widen {{Seq:N}} in the pattern.");
            }

            var code = pattern.Render(tokens, seq);
            if (!CodeRule().IsMatch(code))
            {
                throw new StudentCodeInvalidException($"The pattern renders '{code}', which is not a valid student code.");
            }

            if (!await IsTakenAsync(homeUnit.OrganizationId, code, null, cancellationToken))
            {
                return code;
            }
        }

        throw new StudentCodeExhaustedException("No free student code found after several tries.");
    }

    public async Task<string?> ValidateManualAsync(Guid organizationId, string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (!CodeRule().IsMatch(code ?? string.Empty))
        {
            return ProblemCodes.validation_failed;
        }

        return await IsTakenAsync(organizationId, code!, excludeId, cancellationToken) ? ProblemCodes.student_code_taken : null;
    }

    private Task<bool> IsTakenAsync(Guid organizationId, string code, Guid? excludeId, CancellationToken ct) =>
        // ISOLATION-BYPASS: codes are unique per client across all schools (outside the caller's scope) and for deleted
        // students too (never reused). The client is matched explicitly; only a yes/no leaves this method.
        db.Set<StudentProfile>().IgnoreQueryFilters()
            .AnyAsync(s => s.OrganizationId == organizationId && s.StudentCode == code && s.Id != excludeId, ct);

    /// <summary>
    /// <c>INSERT ... ON DUPLICATE KEY UPDATE NextValue = LAST_INSERT_ID(NextValue + 1)</c>: the increment and the read of
    /// the new value happen on one connection, inside the caller's transaction when there is one.
    /// </summary>
    private async Task<long> NextValueAsync(Guid organizationId, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            await using (var increment = connection.CreateCommand())
            {
                increment.Transaction = transaction;
                increment.CommandText =
                    "INSERT INTO `StudentCodeCounters` (`OrganizationId`, `NextValue`) VALUES (@org, LAST_INSERT_ID(1)) " +
                    "ON DUPLICATE KEY UPDATE `NextValue` = LAST_INSERT_ID(`NextValue` + 1)";
                var parameter = increment.CreateParameter();
                parameter.ParameterName = "@org";
                parameter.Value = organizationId;
                increment.Parameters.Add(parameter);
                await increment.ExecuteNonQueryAsync(ct);
            }

            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = "SELECT LAST_INSERT_ID()";
            return Convert.ToInt64(await read.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
