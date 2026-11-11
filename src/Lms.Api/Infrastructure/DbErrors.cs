using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Infrastructure;

/// <summary>Maps database failures to the answers the API contract promises.</summary>
public static class DbErrors
{
    private const int MySqlDuplicateEntry = 1062;
    private const int MySqlDeadlock = 1213;

    /// <summary>
    /// True when the save lost a race with a concurrent request that writes the same unique key: the unique index fired
    /// (1062) or MySQL picked this insert as the deadlock victim (1213, concurrent inserts of the same keys take
    /// conflicting gap locks). Either way the other request won and the loser answers the task's 409 code. Read through
    /// reflection so the API project needs no reference to the MySQL driver.
    /// </summary>
    public static bool IsConcurrentKeyConflict(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner.GetType().GetProperty("Number")?.GetValue(inner) is int number && number is MySqlDuplicateEntry or MySqlDeadlock)
            {
                return true;
            }
        }

        return false;
    }
}
