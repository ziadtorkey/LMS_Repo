namespace Lms.Domain.Identity;

public sealed record UserDisplayNames(string Ar, string En);

/// <summary>Display names of the caller. Implemented over the user store once identity lands.</summary>
public interface IUserProfileReader
{
    Task<UserDisplayNames> GetDisplayNamesAsync(Guid userId, CancellationToken cancellationToken);
}