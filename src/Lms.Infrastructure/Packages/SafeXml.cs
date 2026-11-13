using System.Xml;
using System.Xml.Linq;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// XML of an untrusted package (the SCORM manifest and anything else read as XML), M4b Task 8b. There is no resolver at all: a DOCTYPE (so
/// every external or internal entity) is refused, no character may come from an entity, the document is size-capped, and
/// <c>xsi:schemaLocation</c>, <c>xml:base</c> and <c>xi:include</c> are plain text that is never fetched, never validated against and never
/// used to resolve a path. The decisions are made on the node types and byte counts, never on exception message text.
/// </summary>
public static class SafeXml
{
    public const int MaxCharacters = 1_000_000;

    public sealed record Result(XDocument? Document, string? RejectReason);

    /// <summary>Parses the stream; the reason is <c>xml_unsafe</c> for a DOCTYPE or a document over the cap and <c>manifest_invalid</c> for anything not well-formed.</summary>
    public static Result Parse(Stream content, int maxCharacters = MaxCharacters)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = content.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxCharacters)
            {
                return new Result(null, PackageRejectReasons.xml_unsafe);
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            if (HasDoctype(buffer))
            {
                return new Result(null, PackageRejectReasons.xml_unsafe);
            }

            buffer.Position = 0;
            using var reader = XmlReader.Create(buffer, Settings(maxCharacters, DtdProcessing.Prohibit));
            return new Result(XDocument.Load(reader, LoadOptions.None), null);
        }
        catch (XmlException)
        {
            return new Result(null, PackageRejectReasons.manifest_invalid);
        }
    }

    /// <summary>
    /// True when the text holds a DOCTYPE declaration. A plain text search (in the encoding the byte order mark names, UTF-8 otherwise): a
    /// DOCTYPE inside a comment is refused too, which is fine, and nothing is parsed, expanded or fetched to decide it.
    /// </summary>
    private static bool HasDoctype(MemoryStream buffer)
    {
        var bytes = buffer.ToArray();
        var encoding = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? System.Text.Encoding.Unicode
            : bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? System.Text.Encoding.BigEndianUnicode
            : System.Text.Encoding.UTF8;
        return encoding.GetString(bytes).Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
    }

    private static XmlReaderSettings Settings(int maxCharacters, DtdProcessing dtd) => new()
    {
        DtdProcessing = dtd,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = maxCharacters,
        ValidationType = ValidationType.None,
        CloseInput = false,
    };
}
