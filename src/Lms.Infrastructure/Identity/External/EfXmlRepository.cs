using System.Xml.Linq;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Identity.External;

/// <summary>Stores the Data Protection key ring in MySQL so every API node reads and writes the same keys.</summary>
public sealed class EfXmlRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
        return db.Set<DataProtectionKey>().AsNoTracking().OrderBy(k => k.FriendlyName).ToList()
            .Select(k => XElement.Parse(k.Xml)).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
        db.Set<DataProtectionKey>().Add(new DataProtectionKey { FriendlyName = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });
        db.SaveChanges();
    }
}
