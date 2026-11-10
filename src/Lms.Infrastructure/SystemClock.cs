using Lms.Domain.Abstractions;

namespace Lms.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}