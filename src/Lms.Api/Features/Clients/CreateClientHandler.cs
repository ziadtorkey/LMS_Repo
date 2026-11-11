using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Clients;

/// <summary>
/// Creates a client with its root level and unit, the seven built-in roles and the first Org Admin, in one
/// transaction. The invitation email is sent only after the commit, and only for a newly created user; a mail failure
/// never undoes the client (the invitation can be re-sent).
/// </summary>
public sealed class CreateClientHandler(
    LmsDbContext db,
    UserManager<User> users,
    IDefaultRoleProvisioner roles,
    IAccountEmails emails,
    ILogger<CreateClientHandler> logger)
{
    private const string InvitationCulture = "ar";

    public async Task<ClientDto> HandleAsync(CreateClientRequest request, CancellationToken ct)
    {
        var type = Enum.Parse<OrganizationType>(request.Type);
        var email = request.AdminEmail.Trim();

        User admin;
        bool newUser;
        ClientDto result;

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var organization = new Organization(Guid.CreateVersion7(), request.NameAr.Trim(), request.NameEn.Trim(), type);
            var level = new LevelType(organization.Id, request.RootLevelNameAr.Trim(), request.RootLevelNameEn.Trim(), 1, false);
            var root = OrgUnit.CreateRoot(
                organization.Id, level.Id, request.NameAr.Trim(), request.NameEn.Trim(), request.RootUnitCode.Trim());
            db.AddRange(organization, level, root);
            await db.SaveChangesAsync(ct);

            await roles.ProvisionAsync(organization.Id, type, ct);

            admin = (await users.FindByEmailAsync(email))!;
            newUser = admin is null;
            if (admin is null)
            {
                admin = new User
                {
                    UserName = email,
                    Email = email,
                    DisplayNameAr = request.AdminDisplayNameAr.Trim(),
                    DisplayNameEn = request.AdminDisplayNameEn.Trim(),
                };
                var created = await users.CreateAsync(admin);
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Could not create the first admin: " + string.Join("; ", created.Errors.Select(e => e.Code)));
                }
            }

            var orgAdminRole = await db.Roles.SingleAsync(
                r => r.OrganizationId == organization.Id && r.BuiltInKey == BuiltInRoles.OrgAdmin, ct);
            db.RoleAssignments.Add(new RoleAssignment(root, admin.Id, orgAdminRole.Id));
            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            result = ClientEndpoints.ToDto(organization, root.Id);
        }

        if (newUser)
        {
            try
            {
                await emails.SendInvitationAsync(admin, InvitationCulture, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Invitation email to the first admin of client {ClientId} failed.", result.Id);
            }
        }

        return result;
    }
}