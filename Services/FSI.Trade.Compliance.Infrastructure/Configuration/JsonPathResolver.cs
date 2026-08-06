using System.Text.Json;

namespace FSI.Trade.Compliance.Infrastructure.Configuration;

/// <summary>
/// Compact JSONPath resolver for the common dot/index forms used by provider
/// response mapping: <c>$.a.b</c>, <c>$.a[0].b</c>, <c>$.data.items[2].code</c>.
/// (Full JSONPath filter syntax — <c>$..x</c>, predicates — is a later add-on.)
/// </summary>
public static class JsonPathResolver
{
    /// <summary>Resolve a path to a scalar string, or null if absent.</summary>
    public static string? ResolveString(JsonElement root, string? path)
    {
        var el = Resolve(root, path);
        if (el is null) return null;
        return el.Value.ValueKind switch
        {
            JsonValueKind.String => el.Value.GetString(),
            JsonValueKind.Number => el.Value.GetRawText(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.Value.GetRawText(),
        };
    }

    public static JsonElement? Resolve(JsonElement root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim();
        if (p.StartsWith("$")) p = p.Substring(1);
        if (p.StartsWith(".")) p = p.Substring(1);
        if (p.Length == 0) return root;

        var current = root;
        foreach (var token in Tokenize(p))
        {
            if (token.isIndex)
            {
                if (current.ValueKind != JsonValueKind.Array || token.index < 0 || token.index >= current.GetArrayLength())
                    return null;
                current = current[token.index];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(token.name!, out var next))
                    return null;
                current = next;
            }
        }
        return current;
    }

    private static IEnumerable<(bool isIndex, int index, string? name)> Tokenize(string path)
    {
        var i = 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;
                continue;
            }
            if (path[i] == '[')
            {
                var end = path.IndexOf(']', i);
                if (end < 0) yield break;
                var inner = path.Substring(i + 1, end - i - 1).Trim().Trim('\'', '"');
                i = end + 1;
                if (int.TryParse(inner, out var idx)) yield return (true, idx, null);
                else yield return (false, 0, inner);   // bracket-quoted property name
            }
            else
            {
                var start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                yield return (false, 0, path.Substring(start, i - start));
            }
        }
    }
}
