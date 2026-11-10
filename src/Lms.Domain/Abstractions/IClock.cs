namespace Lms.Domain.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}