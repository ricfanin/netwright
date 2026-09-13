using System.Text.Json;
using System.Text.Json.Nodes;

namespace Netwright;

/// <summary>
/// Shrinks generated tool input schemas. Tool definitions are sent to the model on every turn, and
/// the generator spells every optional parameter as <c>"type":["string","null"],"default":null</c>.
/// Omitting a parameter already means "not set", so the null type and trivial defaults (null, false)
/// carry no information.
/// </summary>
internal static class SchemaCompactor
{
    public static JsonElement Compact(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText());
        if (node is JsonObject root)
        {
            CompactObject(root);
        }

        return JsonSerializer.SerializeToElement(node);
    }

    private static void CompactObject(JsonObject schema)
    {
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (_, value) in properties)
            {
                if (value is JsonObject property)
                {
                    CompactProperty(property);
                }
            }
        }
    }

    private static void CompactProperty(JsonObject property)
    {
        if (property["type"] is JsonArray types)
        {
            var nonNull = types.Where(t => t?.GetValue<string>() != "null").Select(t => t!.GetValue<string>()).ToList();
            property["type"] = nonNull.Count == 1 ? JsonValue.Create(nonNull[0]) : new JsonArray(nonNull.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        }

        if (property.TryGetPropertyValue("default", out var defaultValue) &&
            (defaultValue is null || (defaultValue is JsonValue v && v.TryGetValue<bool>(out var b) && !b)))
        {
            property.Remove("default");
        }

        if (property["items"] is JsonObject items)
        {
            CompactProperty(items);
        }

        CompactObject(property);
    }
}
