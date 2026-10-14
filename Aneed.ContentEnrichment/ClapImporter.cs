using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

internal static class ClapImporter
{
    public static async Task<int> Run(string[] args)
    {
        var pathIndex = Array.FindIndex(args, a => a.Equals("--import-clap", StringComparison.OrdinalIgnoreCase));
        if (pathIndex < 0 || pathIndex + 1 >= args.Length || args[pathIndex + 1].StartsWith("--"))
        {
            Console.Error.WriteLine("Usage: --import-clap <results.jsonl> [--dry-run]");
            return 2;
        }

        var path = args[pathIndex + 1];
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 2;
        }
        var connectionString = Environment.GetEnvironmentVariable("ANEED_POSTGRES");
        if (!dryRun && string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Set ANEED_POSTGRES first.");
            return 2;
        }

        await using NpgsqlConnection? db = dryRun ? null : new NpgsqlConnection(connectionString);
        string? vectorType = null;
        if (db is not null)
        {
            await db.OpenAsync();
            // Discover the actual schema of the target column's pgvector type.
            // PostgreSQL extensions need not be installed in the current search_path.
            await using var typeCommand = new NpgsqlCommand("""
                SELECT format('%I.%I', ns.nspname, typ.typname)
                FROM pg_attribute attr
                JOIN pg_type typ ON typ.oid = attr.atttypid
                JOIN pg_namespace ns ON ns.oid = typ.typnamespace
                WHERE attr.attrelid = 'music.audio_embeddings'::regclass
                  AND attr.attname = 'embedding'
                  AND NOT attr.attisdropped
                  AND typ.typname = 'vector'
                """, db);
            vectorType = (string?)await typeCommand.ExecuteScalarAsync()
                ?? throw new InvalidOperationException(
                    "music.audio_embeddings.embedding is not a pgvector column.");
            Console.WriteLine($"Resolved pgvector column type: {vectorType}");
        }

        var imported = 0;
        var invalid = 0;
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var identity = RequiredString(root, "identity");
                var sourcePath = RequiredString(root, "relative_path");
                var model = RequiredString(root, "model_id");
                var version = RequiredString(root, "analysis_version");
                var vector = root.GetProperty("embedding");
                if (vector.ValueKind != JsonValueKind.Array || vector.GetArrayLength() != 512)
                    throw new FormatException("Embedding must contain exactly 512 numbers.");

                var components = vector.EnumerateArray().Select(x =>
                {
                    var n = x.GetDouble();
                    if (!double.IsFinite(n)) throw new FormatException("Embedding contains a nonfinite value.");
                    return n.ToString("R", CultureInfo.InvariantCulture);
                });
                var vectorLiteral = "[" + string.Join(",", components) + "]";
                var sections = JsonSerializer.Serialize(new
                {
                    duration_seconds = root.TryGetProperty("duration_seconds", out var duration) ? duration : default(JsonElement?),
                    sample_rate = root.TryGetProperty("sample_rate", out var sampleRate) ? sampleRate : default(JsonElement?),
                    clip_seconds = root.TryGetProperty("clip_seconds", out var clipSeconds) ? clipSeconds : default(JsonElement?),
                    clip_offsets_seconds = root.TryGetProperty("clip_offsets_seconds", out var offsets) ? offsets : default(JsonElement?),
                    segment_embeddings = root.GetProperty("segment_embeddings"),
                    top_descriptions = root.TryGetProperty("top_descriptions", out var descriptions) ? descriptions : default(JsonElement?)
                });

                if (db is not null)
                {
                    await using var cmd = new NpgsqlCommand($"""
                        INSERT INTO music.audio_embeddings
                            (source_identity, source_path, model_name, analysis_version, embedding, sections)
                        VALUES (@identity, @path, @model, @version, @embedding::{vectorType}, @sections)
                        ON CONFLICT (source_identity, model_name, analysis_version) DO UPDATE SET
                            source_path = EXCLUDED.source_path,
                            embedding = EXCLUDED.embedding,
                            sections = EXCLUDED.sections
                        """, db);
                    cmd.Parameters.AddWithValue("identity", identity);
                    cmd.Parameters.AddWithValue("path", sourcePath);
                    cmd.Parameters.AddWithValue("model", model);
                    cmd.Parameters.AddWithValue("version", version);
                    cmd.Parameters.AddWithValue("embedding", vectorLiteral);
                    cmd.Parameters.Add("sections", NpgsqlDbType.Jsonb).Value = sections;
                    await cmd.ExecuteNonQueryAsync();
                }
                imported++;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                invalid++;
                Console.Error.WriteLine($"Line {lineNumber}: {ex.Message}");
            }
        }
        Console.WriteLine($"{(dryRun ? "Validated" : "Imported/updated")}: {imported}; invalid: {invalid}");
        Console.WriteLine("Catalog linking remains pending: no SAM song ID is assumed from filenames.");
        return invalid == 0 ? 0 : 1;
    }

    private static string RequiredString(JsonElement root, string property)
    {
        var value = root.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value) ? value
            : throw new FormatException($"Missing {property}.");
    }
}
