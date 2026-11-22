using System.Text.Json;
using System.Collections.Immutable;
using System.Data;
using MySqlConnector;
using Npgsql;
using NpgsqlTypes;

namespace AneedWeb.Services;

public sealed record ClapDescription(string Text, decimal Similarity);

public sealed record MusicIntelligence(
    long? SamSongId,
    Guid? AneedId,
    string? Artist,
    string? Title,
    string? Album,
    bool IdentityMappingFound,
    bool ClapMatchFound,
    IReadOnlyList<ClapDescription> Descriptions,
    decimal? DurationSeconds,
    int? SampleRate,
    decimal? Tempo,
    string? MusicalKey,
    string? Error,
    string? ClapModel = null,
    string? ClapVersion = null)
{
    public static MusicIntelligence Unavailable(long? samSongId, string error)
        => new(samSongId, null, null, null, null, false, false, [], null, null, null, null, error);
}

public sealed class VerifiedMusicContext
{
    private VerifiedMusicContext(
        long samSongId,
        string artist,
        string title,
        string? album,
        Guid? aneedId,
        bool clapMatchFound,
        ImmutableArray<ClapDescription> descriptions,
        decimal? durationSeconds,
        int? sampleRate,
        decimal? tempo,
        string? musicalKey,
        string? clapModel,
        string? clapVersion,
        string? analysisStatus)
    {
        SamSongId = samSongId;
        Artist = artist;
        Title = title;
        Album = album;
        AneedId = aneedId;
        ClapMatchFound = clapMatchFound;
        Descriptions = descriptions;
        DurationSeconds = durationSeconds;
        SampleRate = sampleRate;
        Tempo = tempo;
        MusicalKey = musicalKey;
        ClapModel = clapModel;
        ClapVersion = clapVersion;
        AnalysisStatus = analysisStatus;
    }

    public long SamSongId { get; }
    public string Artist { get; }
    public string Title { get; }
    public string? Album { get; }
    public Guid? AneedId { get; }
    public bool ClapMatchFound { get; }
    public ImmutableArray<ClapDescription> Descriptions { get; }
    public decimal? DurationSeconds { get; }
    public int? SampleRate { get; }
    public decimal? Tempo { get; }
    public string? MusicalKey { get; }
    public string? ClapModel { get; }
    public string? ClapVersion { get; }
    public string? AnalysisStatus { get; }

    public static VerifiedMusicContext Create(Track track, MusicIntelligence analysis)
    {
        if (track.SamSongId is null or <= 0 ||
            string.IsNullOrWhiteSpace(track.Artist) || string.IsNullOrWhiteSpace(track.Title))
            throw new InvalidOperationException("Verified SAM song metadata is required to create music context.");

        var samSongId = track.SamSongId.Value;
        var sameSong = analysis.SamSongId == samSongId;
        var identityVerified = sameSong && analysis.IdentityMappingFound && analysis.AneedId.HasValue;
        var clapVerified = identityVerified && analysis.ClapMatchFound;
        var analysisStatus = clapVerified
            ? null
            : sameSong
                ? analysis.Error ?? "Audio analysis is missing for this verified song."
                : "Analysis was omitted because its SAM song ID did not match the current song.";

        return new VerifiedMusicContext(
            samSongId,
            track.Artist,
            track.Title,
            string.IsNullOrWhiteSpace(track.Album) ? null : track.Album,
            identityVerified ? analysis.AneedId : null,
            clapVerified,
            clapVerified ? ImmutableArray.CreateRange(analysis.Descriptions) : [],
            clapVerified ? analysis.DurationSeconds : null,
            clapVerified ? analysis.SampleRate : null,
            clapVerified ? analysis.Tempo : null,
            clapVerified ? analysis.MusicalKey : null,
            clapVerified ? analysis.ClapModel : null,
            clapVerified ? analysis.ClapVersion : null,
            analysisStatus);
    }
}

public sealed class ClapMusicAnalysisService(
    IWebHostEnvironment environment,
    ILogger<ClapMusicAnalysisService> logger)
{
    private const int ConnectionTimeoutSeconds = 5;
    private const int CommandTimeoutSeconds = 10;

    public async Task<MusicIntelligence> GetForSamSongAsync(long samSongId, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
            return MusicIntelligence.Unavailable(samSongId, "Music analysis lookup is available only in Development.");

        if (samSongId <= 0)
            return MusicIntelligence.Unavailable(samSongId, "SAM song ID must be positive.");

        var mariaDbConnectionString = Environment.GetEnvironmentVariable("ANEED_SAM_MARIADB");
        if (string.IsNullOrWhiteSpace(mariaDbConnectionString))
            return MusicIntelligence.Unavailable(samSongId, "ANEED_SAM_MARIADB is not configured in the application process.");

        AneedIdLookup lookup;
        try
        {
            lookup = await ResolveAneedIdAsync(mariaDbConnectionString, samSongId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MySqlException exception)
        {
            var failure = exception.Number is 1044 or 1045 or 1698
                ? "MariaDB authentication or authorization failed"
                : exception.Number is 0 or 2002 or 2003 or 2005 or 2006 or 2013
                    ? "MariaDB connection failed"
                    : "MariaDB identity query failed";
            logger.LogWarning("{Failure} for SAM song ID {SamSongId} (MariaDB error {ErrorNumber})", failure, samSongId, exception.Number);
            return MusicIntelligence.Unavailable(samSongId, $"{failure}; no mapping result was obtained.");
        }
        catch (InvalidAneedIdException)
        {
            logger.LogWarning("MariaDB returned an invalid AneedId GUID for SAM song ID {SamSongId}", samSongId);
            return MusicIntelligence.Unavailable(samSongId, "MariaDB returned an AneedId value that could not be converted to a GUID.");
        }

        if (!lookup.Found)
            return MusicIntelligence.Unavailable(samSongId, "No AneedId mapping exists for this SAM song ID.");

        var aneedId = lookup.AneedId;
        var postgresConnectionString = Environment.GetEnvironmentVariable("ANEED_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
            return MissingAnalysis(samSongId, aneedId, "ANEED_POSTGRES is not configured in the application process.");

        try
        {
            return await GetClapAnalysisAsync(postgresConnectionString, samSongId, aneedId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException exception)
        {
            logger.LogError("PostgreSQL CLAP query failed for SAM song ID {SamSongId}; SQLSTATE {SqlState}, category {Category}",
                samSongId, exception.SqlState, GetPostgresErrorCategory(exception.SqlState));
            return MissingAnalysis(samSongId, aneedId,
                $"PostgreSQL CLAP query failed ({GetPostgresErrorCategory(exception.SqlState)}, SQLSTATE {exception.SqlState}).");
        }
        catch (NpgsqlException exception)
        {
            logger.LogError("PostgreSQL CLAP connection/provider failure for SAM song ID {SamSongId}; SQLSTATE {SqlState}, category {Category}",
                samSongId, exception.SqlState, GetPostgresErrorCategory(exception.SqlState));
            return MissingAnalysis(samSongId, aneedId,
                $"PostgreSQL CLAP connection/provider failure ({GetPostgresErrorCategory(exception.SqlState)}, SQLSTATE {exception.SqlState}).");
        }
        catch (Exception exception)
        {
            logger.LogError("PostgreSQL CLAP processing failed for SAM song ID {SamSongId} ({ErrorType})", samSongId, exception.GetType().Name);
            return MissingAnalysis(samSongId, aneedId, "PostgreSQL CLAP result processing failed.");
        }
    }

    private readonly record struct AneedIdLookup(bool Found, Guid AneedId);
    private sealed class InvalidAneedIdException : Exception { }

    private static async Task<AneedIdLookup> ResolveAneedIdAsync(string connectionString, long samSongId, CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            ConnectionTimeout = ConnectionTimeoutSeconds,
            DefaultCommandTimeout = CommandTimeoutSeconds
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var readOnly = new MySqlCommand("SET SESSION TRANSACTION READ ONLY", connection)
        {
            CommandTimeout = CommandTimeoutSeconds
        })
        {
            await readOnly.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new MySqlCommand("""
            SELECT aneed_id
            FROM samdb.aneed_song_map
            WHERE sam_song_id = @samSongId
            LIMIT 1
            """, connection)
        {
            CommandTimeout = CommandTimeoutSeconds
        };
        command.Parameters.Add("@samSongId", MySqlDbType.Int64).Value = samSongId;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            return new(false, Guid.Empty);

        var aneedId = result switch
        {
            Guid value => value,
            string value when Guid.TryParse(value, out var parsedAneedId) => parsedAneedId,
            _ => throw new InvalidAneedIdException()
        };
        return new(true, aneedId);
    }

    private static async Task<MusicIntelligence> GetClapAnalysisAsync(
        string connectionString,
        long samSongId,
        Guid aneedId,
        CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = ConnectionTimeoutSeconds,
            CommandTimeout = CommandTimeoutSeconds
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        {
            readOnly.CommandTimeout = CommandTimeoutSeconds;
            await readOnly.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand("""
            SELECT i.aneed_id, e.model_id, e.analysis_version, e.top_descriptions
            FROM music.inventory AS i
            LEFT JOIN music.audio_embeddings AS e ON e.aneed_id = i.aneed_id
            WHERE i.aneed_id = @aneedId
            """, connection, transaction)
        {
            CommandTimeout = CommandTimeoutSeconds
        };
        command.Parameters.Add("aneedId", NpgsqlDbType.Uuid).Value = aneedId;

        var inventoryFound = false;
        var analysisRecordFound = false;
        MusicIntelligence? matchedAnalysis = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                inventoryFound = true;
                analysisRecordFound |= !reader.IsDBNull(1) || !reader.IsDBNull(2) || !reader.IsDBNull(3);
                if (reader.IsDBNull(0) || reader.GetGuid(0) != aneedId || reader.IsDBNull(3))
                    continue;

                IReadOnlyList<ClapDescription> descriptions;
                try
                {
                    descriptions = ParseDescriptions(reader.GetValue(3).ToString() ?? string.Empty);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (descriptions.Count == 0)
                    continue;

                if (matchedAnalysis is not null)
                    return MissingAnalysis(samSongId, aneedId, "Multiple populated CLAP analyses match this AneedId; no analysis was selected.");

                matchedAnalysis = new MusicIntelligence(
                    samSongId,
                    aneedId,
                    null,
                    null,
                    null,
                    true,
                    true,
                    descriptions,
                    null,
                    null,
                    null,
                    null,
                    null,
                    reader.IsDBNull(1) ? null : reader.GetValue(1).ToString(),
                    reader.IsDBNull(2) ? null : reader.GetValue(2).ToString());
            }
        }

        await transaction.CommitAsync(cancellationToken);
        if (matchedAnalysis is not null)
            return matchedAnalysis;

        return MissingAnalysis(samSongId, aneedId, !inventoryFound
            ? "No PostgreSQL inventory record matches this AneedId."
            : !analysisRecordFound
                ? "No PostgreSQL CLAP analysis record matches this AneedId."
                : "A PostgreSQL CLAP analysis record exists, but it contains no usable top_descriptions.");
    }

    private static MusicIntelligence MissingAnalysis(long samSongId, Guid aneedId, string error)
        => new(samSongId, aneedId, null, null, null, true, false, [], null, null, null, null, error);

    private static string GetPostgresErrorCategory(string? sqlState)
        => sqlState switch
        {
            "3D000" => "invalid database name",
            "3F000" => "invalid schema name",
            "42P01" => "undefined table",
            "42703" => "undefined column",
            null => "connection/provider error",
            _ when sqlState.StartsWith("08", StringComparison.Ordinal) => "connection exception",
            _ when sqlState.StartsWith("28", StringComparison.Ordinal) => "authentication/authorization exception",
            _ when sqlState.StartsWith("42", StringComparison.Ordinal) => "SQL syntax/schema exception",
            _ when sqlState.StartsWith("23", StringComparison.Ordinal) => "integrity constraint exception",
            _ when sqlState.StartsWith("22", StringComparison.Ordinal) => "data exception",
            _ when sqlState.StartsWith("53", StringComparison.Ordinal) => "insufficient resources",
            _ when sqlState.StartsWith("57", StringComparison.Ordinal) => "operator intervention",
            _ => "PostgreSQL server exception"
        };

    public static IReadOnlyList<ClapDescription> ParseDescriptions(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var descriptions = new List<ClapDescription>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("similarity", out var similarity) || !similarity.TryGetDecimal(out var score))
            {
                continue;
            }

            var description = text.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(description))
                descriptions.Add(new ClapDescription(description, score));
        }

        return descriptions;
    }
}
