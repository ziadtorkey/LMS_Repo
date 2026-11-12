using System.Text.Json;
using Lms.Domain.People;
using Lms.Shared.Configuration;

namespace Lms.Infrastructure.Configuration;

/// <summary>Checks a JSON value against the definition of a catalog setting before it is stored.</summary>
public static class SettingValueValidator
{
    public const int MaxStringLength = 500;

    /// <summary>A message key (for the client to translate) when the value is not acceptable, otherwise null.</summary>
    public static string? Validate(SettingDefinition definition, string valueJson)
    {
        JsonElement value;
        try
        {
            using var document = JsonDocument.Parse(valueJson);
            value = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return "invalid_json";
        }

        return definition.Kind switch
        {
            SettingValueKinds.Bool => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "must_be_boolean",
            SettingValueKinds.Int => ValidateInt(definition, value),
            SettingValueKinds.String => ValidateString(value) ?? ValidateKnownString(definition, value),
            SettingValueKinds.Enum => ValidateEnum(definition, value),
            SettingValueKinds.StringSet => ValidateStringSet(definition, value),
            _ => "unsupported_kind",
        };
    }

    private static string? ValidateInt(SettingDefinition definition, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            return "must_be_integer";
        }

        return (definition.Min is { } min && number < min) || (definition.Max is { } max && number > max)
            ? "out_of_range"
            : null;
    }

    private static string? ValidateString(JsonElement value) =>
        value.ValueKind != JsonValueKind.String ? "must_be_string"
        : value.GetString()!.Length > MaxStringLength ? "too_long"
        : null;

    /// <summary>Strings with their own grammar: the student code pattern must parse (D25).</summary>
    private static string? ValidateKnownString(SettingDefinition definition, JsonElement value) =>
        definition.Name == SettingKeys.StudentsCodePattern.Name
        && !StudentCodePattern.TryParse(value.GetString(), out _, out _)
            ? "invalid_code_pattern"
            : null;

    private static string? ValidateEnum(SettingDefinition definition, JsonElement value) =>
        value.ValueKind != JsonValueKind.String ? "must_be_string"
        : definition.AllowedValues?.Contains(value.GetString(), StringComparer.Ordinal) == true ? null
        : "not_allowed";

    private static string? ValidateStringSet(SettingDefinition definition, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return "must_be_array";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return "must_be_string";
            }

            var text = item.GetString()!;
            if (definition.AllowedValues?.Contains(text, StringComparer.Ordinal) != true)
            {
                return "not_allowed";
            }

            if (!seen.Add(text))
            {
                return "duplicate_value";
            }
        }

        return null;
    }
}
