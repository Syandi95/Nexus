using System.Text;
using System.Text.Json;

namespace Nexus.App.Services.Agent.Tools;

// Deliberately small closed schema dialect: object root, scalar string/integer/
// boolean properties, required keys, bounded strings/enums/ranges, no extra keys.
// Unsupported schema keywords/types fail registration rather than being ignored.
public static class ToolArgumentValidator
{
    public const int MaxArgumentBytes = 4096;

    public static void ValidateSchema(JsonElement schema)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > MaxArgumentBytes) throw new ArgumentException("Schema limit exceeded.");
            RequireObjectKeys(schema, "type", "properties", "required", "additionalProperties");
            if (schema.GetProperty("type").GetString() != "object" || schema.GetProperty("additionalProperties").ValueKind != JsonValueKind.False)
                throw new ArgumentException("Closed object schema required.");
            var properties = schema.GetProperty("properties");
            if (properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Count() > 8)
                throw new ArgumentException("Invalid schema properties.");
            foreach (var property in properties.EnumerateObject())
            {
                if (!ToolRegistry.IsValidName(property.Name)) throw new ArgumentException("Invalid argument property name.");
                var definition = property.Value;
                var type = definition.GetProperty("type").GetString();
                if (type == "string")
                {
                    RequireObjectKeys(definition, "type", "maxLength", "enum");
                    var max = definition.GetProperty("maxLength").GetInt32();
                    if (max is < 1 or > 256) throw new ArgumentException("String limit required.");
                    if (definition.TryGetProperty("enum", out var values))
                    {
                        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 16)
                            throw new ArgumentException("Invalid enum.");
                        var names = values.EnumerateArray().Select(v => v.GetString()!).ToArray();
                        if (names.Any(n => n is null || n.Length > max) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                            throw new ArgumentException("Invalid enum values.");
                    }
                }
                else if (type == "integer")
                {
                    RequireObjectKeys(definition, "type", "minimum", "maximum");
                    if (definition.GetProperty("minimum").GetInt64() > definition.GetProperty("maximum").GetInt64())
                        throw new ArgumentException("Invalid integer range.");
                }
                else if (type == "boolean") RequireObjectKeys(definition, "type");
                else throw new ArgumentException("Unsupported schema type.");
            }
            var required = schema.GetProperty("required");
            if (required.ValueKind != JsonValueKind.Array) throw new ArgumentException("Required array missing.");
            var keys = required.EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Any(k => !properties.TryGetProperty(k, out _)))
                throw new ArgumentException("Invalid required keys.");
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new ArgumentException("Invalid tool argument schema.", error);
        }
    }

    public static bool TryValidate(string raw, JsonElement schema, out JsonElement arguments)
    {
        arguments = default;
        if (raw is null || Encoding.UTF8.GetByteCount(raw) > MaxArgumentBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var properties = root.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return false;
            var definitions = schema.GetProperty("properties");
            foreach (var property in properties)
            {
                if (!definitions.TryGetProperty(property.Name, out var definition)) return false;
                var value = property.Value;
                switch (definition.GetProperty("type").GetString())
                {
                    case "string":
                        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > definition.GetProperty("maxLength").GetInt32()) return false;
                        if (definition.TryGetProperty("enum", out var values) && !values.EnumerateArray().Any(e => e.GetString() == value.GetString())) return false;
                        break;
                    case "integer":
                        if (!value.TryGetInt64(out var number) || number < definition.GetProperty("minimum").GetInt64() || number > definition.GetProperty("maximum").GetInt64()) return false;
                        break;
                    case "boolean":
                        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                        break;
                    default: return false;
                }
            }
            foreach (var required in schema.GetProperty("required").EnumerateArray())
                if (!root.TryGetProperty(required.GetString()!, out _)) return false;
            arguments = root.Clone();
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return false; }
    }

    private static void RequireObjectKeys(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Object required.");
        var properties = value.EnumerateObject().ToArray();
        if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length || properties.Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new ArgumentException("Unknown or duplicate schema keyword.");
    }
}
