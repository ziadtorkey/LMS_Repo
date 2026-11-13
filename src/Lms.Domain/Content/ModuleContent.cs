using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Content;

/// <summary>
/// The payload of a content-kind module (File, Video, Audio: the asset; Page: sanitised bodies; Link: the URL). One live
/// row per module. <see cref="AssetId"/> is a plain id (the asset table arrives with the asset service).
/// </summary>
[Audited]
public sealed class ModuleContent : UnitOwnedEntity, ILiveKeyed
{
    private ModuleContent()
    {
    }

    public ModuleContent(LessonModule module)
    {
        PlaceLike(module);
        ModuleId = module.Id;
        ModuleKey = module.Id;
        ClassCourseId = module.ClassCourseId;
    }

    public Guid ModuleId { get; private set; }

    /// <summary>The module id while live; makes "one content row per module" unique.</summary>
    public Guid? ModuleKey { get; private set; }

    public Guid ClassCourseId { get; private set; }
    public Guid? AssetId { get; private set; }
    public string? BodyHtmlAr { get; private set; }
    public string? BodyHtmlEn { get; private set; }
    public string? Url { get; private set; }

    public void Set(Guid? assetId, string? bodyHtmlAr, string? bodyHtmlEn, string? url)
    {
        AssetId = assetId;
        BodyHtmlAr = bodyHtmlAr;
        BodyHtmlEn = bodyHtmlEn;
        Url = url;
    }

    public void ClearLiveKeys() => ModuleKey = null;
}
