using System.Text.Json;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// JSON of an untrusted package (<c>h5p.json</c>, <c>content.json</c>, <c>library.json</c>), M4b Task 8b: a size cap (1 MB), a depth cap (32)
/// and caps on the number of properties and array items, so neither a wide nor a deep document can exhaust memory or the stack. Callers map a
/// problem to their own reject reason (<c>h5p_invalid</c> for H5P).
/// </summary>
public static class SafeJson
{
    public const int MaxBytes = 1024 * 1024;

    public const int MaxDepth = 32;

    public const int MaxItems = 20_000;

    public sealed record Result(JsonDocument? Document, string? Problem);

    public static Result Parse(Stream content, int maxBytes = MaxBytes, int maxDepth = MaxDepth, int maxItems = MaxItems)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = content.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return new Result(null, "too_large");
            }

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        if (Depth(bytes, maxDepth) is { } depthProblem)
        {
            return new Result(null, depthProblem);
        }

        try
        {
            var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = maxDepth + 1 });
            return Count(document.RootElement) > maxItems ? Reject(document) : new Result(document, null);
        }
        catch (JsonException)
        {
            return new Result(null, "not_json");
        }
    }

    /// <summary>Walks the tokens and measures the nesting (never trusting an exception message): "too_deep" past the cap, "not_json" for a syntax error, otherwise null.</summary>
    private static string? Depth(byte[] bytes, int maxDepth)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = maxDepth + 1 });
        var deepest = 0;
        try
        {
            while (reader.Read())
            {
                deepest = Math.Max(deepest, reader.CurrentDepth);
                if (deepest > maxDepth)
                {
                    return "too_deep";
                }
            }
        }
        catch (JsonException)
        {
            return deepest >= maxDepth ? "too_deep" : "not_json";
        }

        return null;
    }

    private static Result Reject(JsonDocument document)
    {
        document.Dispose();
        return new Result(null, "too_many_items");
    }

    /// <summary>Properties and array items of the whole tree, counted iteratively so the count itself cannot overflow the stack.</summary>
    private static long Count(JsonElement root)
    {
        long count = 0;
        var pending = new Stack<JsonElement>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        count++;
                        pending.Push(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        count++;
                        pending.Push(item);
                    }

                    break;
            }
        }

        return count;
    }
}
