using System.Text.Json;

namespace AneedWeb.Services;

public sealed record LyricInterpretation(Guid AneedId, string Version, string Status, string Model, string? Source,
    DateTime? AnalyzedAt, decimal? Confidence, string? Themes, string? Emotions, string? Narrative,
    string? Relationships, string? Interpretation)
{
    public const string SupportedVersion = "lyric-narrative-v1";
    public const string UnverifiedVersion = "lyrics-openai-unverified-v1";
    public bool IsTentative => Version == UnverifiedVersion;

    public bool IsValidFor(Guid? id) => AneedId == id && Version is SupportedVersion or UnverifiedVersion && Status == "complete" &&
        (!IsTentative || !string.IsNullOrWhiteSpace(Narrative)) &&
        !string.IsNullOrWhiteSpace(Model) && new[] { Themes, Emotions, Narrative, Relationships, Interpretation }.Any(s => !string.IsNullOrWhiteSpace(s));

    public static LyricInterpretation? FromRow(Guid id, string version, string status, string model, string? source,
        DateTime? analyzedAt, decimal? confidence, string? themes, string? emotions, string? narrative, string? relationships, string? interpretation)
    {
        using var tags = JsonDocument.Parse(themes ?? "[]");
        using var feelings = JsonDocument.Parse(emotions ?? "[]");
        using var story = JsonDocument.Parse(narrative ?? "{}");
        using var people = JsonDocument.Parse(relationships ?? "[]");
        var relation = people.RootElement.ValueKind == JsonValueKind.Array
            ? Join(people.RootElement.EnumerateArray().Take(2).Select(x => version == UnverifiedVersion && x.ValueKind == JsonValueKind.String
                ? Short(x.GetString(), 100) : Fields(x, "type", "dynamic", "power balance"))) : null;
        var result = new LyricInterpretation(id, version, status, Short(model, 80) ?? "", Short(source, 80), analyzedAt, confidence,
            Labels(tags.RootElement), Labels(feelings.RootElement), version == UnverifiedVersion
                ? Join([Fields(story.RootElement, "perspective", "conflict", "development", "resolution", "ambiguity"),
                    story.RootElement.ValueKind == JsonValueKind.Object && story.RootElement.TryGetProperty("uncertainty", out var uncertainty) && Labels(uncertainty) is { } limits ? "uncertainty: " + limits : null])
                : Fields(story.RootElement, "perspective", "conflict", "initial_state", "turning_point", "ending_state"),
            relation, Short(interpretation, 320));
        return result.IsValidFor(id) ? result : null;
    }

    private static string? Labels(JsonElement list) => list.ValueKind == JsonValueKind.Array
        ? Join(list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Take(5).Select(x => Short(x.GetString(), 48))) : null;

    private static string? Fields(JsonElement item, params string[] keys) => item.ValueKind == JsonValueKind.Object
        ? Join(keys.Select(key => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && Short(value.GetString(), 100) is { } text
            ? $"{key}: {text}" : null)) : null;

    private static string? Join(IEnumerable<string?> values) => string.Join("; ", values.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } joined ? joined : null;

    private static string? Short(string? value, int limit)
    {
        // Do not forward quoted passages or multiline excerpts embedded in an interpretation.
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\r', '\n', '"', '“', '”']) >= 0) return null;
        var text = value.Trim();
        return text.Length <= limit ? text : text[..limit];
    }
}
