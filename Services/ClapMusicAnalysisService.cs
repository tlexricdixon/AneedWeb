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

        Guid? aneedId;
        try
        {
            aneedId = await ResolveAneedIdAsync(mariaDbConnectionString, samSongId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Read-only MariaDB identity lookup failed for SAM song ID {SamSongId} ({ErrorType})", samSongId, exception.GetType().Name);
            return MusicIntelligence.Unavailable(samSongId, "The SAM song ID could not be resolved through the read-only MariaDB identity map.");
        }

        if (aneedId is null)
            return MusicIntelligence.Unavailable(samSongId, "No valid AneedId GUID mapping exists for this SAM song ID.");

        var postgresConnectionString = Environment.GetEnvironmentVariable("ANEED_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
            return MissingAnalysis(samSongId, aneedId.Value, "ANEED_POSTGRES is not configured in the application process.");

        try
        {
            return await GetClapAnalysisAsync(postgresConnectionString, samSongId, aneedId.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Read-only PostgreSQL CLAP lookup failed for SAM song ID {SamSongId} ({ErrorType})", samSongId, exception.GetType().Name);
            return MissingAnalysis(samSongId, aneedId.Value, "The matching CLAP analysis could not be loaded from PostgreSQL.");
        }
    }

    private static async Task<Guid?> ResolveAneedIdAsync(string connectionString, long samSongId, CancellationToken cancellationToken)
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
        return result switch
        {
            Guid value => value,
            string value when Guid.TryParse(value, out var aneedId) => aneedId,
            _ => null
        };
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
            SELECT i.aneed_id, e.model, e.version, e.top_descriptions
            FROM music.inventory AS i
            LEFT JOIN music.audio_embeddings AS e ON e.aneed_id = i.aneed_id
            WHERE i.aneed_id = @aneedId
            """, connection, transaction)
        {
            CommandTimeout = CommandTimeoutSeconds
        };
        command.Parameters.Add("aneedId", NpgsqlDbType.Uuid).Value = aneedId;

        var inventoryFound = false;
        MusicIntelligence? matchedAnalysis = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                inventoryFound = true;
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
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        if (matchedAnalysis is not null)
            return matchedAnalysis;

        return MissingAnalysis(samSongId, aneedId, inventoryFound
            ? "No populated CLAP descriptions are available for this AneedId."
            : "No PostgreSQL inventory record matches this AneedId.");
    }

    private static MusicIntelligence MissingAnalysis(long samSongId, Guid aneedId, string error)
        => new(samSongId, aneedId, null, null, null, true, false, [], null, null, null, null, error);

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
