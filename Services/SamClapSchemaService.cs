using Npgsql;
using System.Text;

namespace AneedWeb.Services;

/// <summary>Development-only inspection of existing SAM PostgreSQL metadata.</summary>
public sealed class SamClapSchemaService(IWebHostEnvironment environment, ILogger<SamClapSchemaService> logger)
{
    public async Task<string> InspectAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) return "Schema inspection is disabled outside Development.";
        var connection = Environment.GetEnvironmentVariable("ANEED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection))
            return "ANEED_POSTGRES is not set in the AneedWeb process environment.";

        try
        {
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync(cancellationToken);
            const string sql = """
                SELECT table_name, column_name, data_type, udt_name
                FROM information_schema.columns
                WHERE table_schema = 'music'
                  AND table_name IN ('inventory', 'audio_embeddings', 'audio_features', 'audio_segments', 'lyric_analysis')
                ORDER BY table_name, ordinal_position
                """;
            await using var command = new NpgsqlCommand(sql, db);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var sb = new StringBuilder("SAM PostgreSQL schema (read-only): ");
            string? current = null;
            int count = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.GetString(0);
                if (table != current)
                {
                    current = table;
                    sb.Append("\n").Append(table).Append(": ");
                }
                if (++count > 140) { sb.Append("…"); break; }
                sb.Append(reader.GetString(1)).Append(" [").Append(reader.GetString(3)).Append("], ");
            }
            return count == 0 ? "No expected music tables were found in the configured PostgreSQL database." : sb.ToString();
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning("SAM PostgreSQL schema inspection failed ({Type}).", ex.GetType().Name);
            return "Could not inspect SAM PostgreSQL. Check server connectivity and the application logs.";
        }
    }
}
