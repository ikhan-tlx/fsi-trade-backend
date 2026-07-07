using System.Text.Json;

namespace FSI.Trade.Compliance.Infrastructure.Flags.Metrics;

/// <summary>Shared UDF flattener. Flattens a (possibly section-nested) UDF JSON object into
/// field→values, descending into nested objects AND arrays so every repeating grid row's
/// values are collected under that field's code. Tolerant: malformed JSON → empty map.
/// Used by both the evaluator and the cross-transaction aggregate handler.</summary>
public static class UdfFlatten
{
    public static IReadOnlyDictionary<string, List<string>> Flatten(string? json)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, map);
        }
        catch { /* tolerant */ }
        return map;

        static void Add(Dictionary<string, List<string>> map, string name, string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return;
            if (!map.TryGetValue(name, out var list)) { list = new List<string>(); map[name] = list; }
            var t = val.Trim();
            if (!list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);
        }

        static void Walk(JsonElement el, Dictionary<string, List<string>> map)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject())
                    {
                        switch (p.Value.ValueKind)
                        {
                            case JsonValueKind.Object: Walk(p.Value, map); break;
                            case JsonValueKind.Array:  Walk(p.Value, map); break;
                            case JsonValueKind.String: Add(map, p.Name, p.Value.GetString()); break;
                            case JsonValueKind.Number: Add(map, p.Name, p.Value.GetRawText()); break;
                            case JsonValueKind.True:
                            case JsonValueKind.False:  Add(map, p.Name, p.Value.GetRawText()); break;
                            default: break;
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray()) Walk(item, map);
                    break;
                default: break;
            }
        }
    }
}
