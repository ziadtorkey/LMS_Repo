using System.Text;
using Lms.Domain.Common;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace Lms.Infrastructure.Seeding;

/// <summary>
/// Inserts many entities with multi-row <c>INSERT</c> statements built from the EF model (table, column names and value conversions), without the change
/// tracker, the audit trail or one round trip per row: about 50 to 100 times faster than <c>SaveChanges</c> for the 50k-student performance seed
/// (M7 X-09). Development seeding only: it bypasses every domain rule on purpose, so it is for generated demo data and never for requests.
/// </summary>
internal sealed class BulkInserter(LmsDbContext db, DateTime nowUtc)
{
    private const int MaxParameters = 30_000;
    private const int MaxRowsPerStatement = 500;

    public long Inserted { get; private set; }

    public async Task InsertAsync<T>(IReadOnlyList<T> entities, CancellationToken ct)
        where T : class
    {
        if (entities.Count == 0)
        {
            return;
        }

        var type = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not in the model.");
        var table = StoreObjectIdentifier.Create(type, StoreObjectType.Table) ?? throw new InvalidOperationException($"{typeof(T).Name} has no table.");
        var properties = type.GetProperties().Where(p => !p.IsShadowProperty()).ToList();
        var columns = properties.Select(p => $"`{p.GetColumnName(table)}`").ToList();
        var rowsPerStatement = Math.Min(MaxRowsPerStatement, MaxParameters / properties.Count);

        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var chunk in entities.Chunk(rowsPerStatement))
            {
                var sql = new StringBuilder($"INSERT INTO `{table.Name}` ({string.Join(", ", columns)}) VALUES ");
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 600;
                for (var row = 0; row < chunk.Length; row++)
                {
                    if (row > 0)
                    {
                        sql.Append(", ");
                    }

                    sql.Append('(');
                    for (var col = 0; col < properties.Count; col++)
                    {
                        var name = $"@p{row}_{col}";
                        sql.Append(col == 0 ? name : ", " + name);
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = name;
                        parameter.Value = ValueOf(properties[col], chunk[row]);
                        command.Parameters.Add(parameter);
                    }

                    sql.Append(')');
                }

                command.CommandText = sql.ToString();
                await command.ExecuteNonQueryAsync(ct);
                Inserted += chunk.Length;
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    private object ValueOf(IProperty property, object entity)
    {
        if (entity is AuditableEntity auditable && auditable.CreatedAt == default)
        {
            auditable.StampCreated(nowUtc, null);
        }

        var value = property.GetGetter().GetClrValue(entity);
        var converter = property.GetTypeMapping().Converter;
        if (value is not null && converter is not null)
        {
            value = converter.ConvertToProvider(value);
        }

        return value ?? DBNull.Value;
    }
}
