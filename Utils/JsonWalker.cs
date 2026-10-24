using System.Text.Json;

namespace TwitterNotifyBot.Utils;

public static class JsonWalker
{
    public static IEnumerable<JsonElement> DescendantsByPropertyName(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.Ordinal))
                {
                    yield return prop.Value;
                }

                foreach (var item in DescendantsByPropertyName(prop.Value, propertyName))
                {
                    yield return item;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var child in DescendantsByPropertyName(item, propertyName))
                {
                    yield return child;
                }
            }
        }
    }

    public static JsonElement? TryGetProperty(JsonElement source, string name)
    {
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value))
        {
            return value;
        }

        return null;
    }

    public static string? GetString(JsonElement source, string name)
    {
        var value = TryGetProperty(source, name);
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.ToString(),
            _ => null
        };
    }

    public static long? GetLong(JsonElement source, string name)
    {
        var value = TryGetProperty(source, name);
        if (value is null)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt64(out var n))
        {
            return n;
        }

        if (value.Value.ValueKind == JsonValueKind.String && long.TryParse(value.Value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
