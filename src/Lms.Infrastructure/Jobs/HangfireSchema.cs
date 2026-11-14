using MySqlConnector;

namespace Lms.Infrastructure.Jobs;

/// <summary>
/// Fixes a defect in the schema Hangfire.MySqlStorage creates: <c>HangfireSet.Score</c> is a single-precision <c>FLOAT</c>. Hangfire stores the
/// due time of a delayed or scheduled job there as Unix seconds (about 1.79e9), and a float holds only 24 bits of mantissa, so the value is
/// rounded to a multiple of 128 s: a job fires up to about 64 s early or late and jobs scheduled seconds apart get the same score (#581).
/// The column is widened to <c>DOUBLE</c>, which is exact for a timestamp. Rows written before the change keep their rounded score.
/// </summary>
public static class HangfireSchema
{
    /// <summary>Idempotent: does nothing when the table is missing or the column is already a double. Safe to run on every node at start.</summary>
    public static void EnsureScoreIsDouble(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        using var read = connection.CreateCommand();
        read.CommandText =
            "SELECT DATA_TYPE, IS_NULLABLE FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'HangfireSet' AND COLUMN_NAME = 'Score'";
        string? type = null;
        var nullable = false;
        using (var reader = read.ExecuteReader())
        {
            if (reader.Read())
            {
                type = reader.GetString(0);
                nullable = reader.GetString(1) == "YES";
            }
        }

        if (type is null || !string.Equals(type, "float", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE `HangfireSet` MODIFY COLUMN `Score` DOUBLE {(nullable ? "NULL" : "NOT NULL")}";
        alter.ExecuteNonQuery();
    }
}
