using System.Text.Json;
using System.Collections.Immutable;
using System.Data;
using MySqlConnector;
using Npgsql;
using NpgsqlTypes;

namespace AneedWeb.Services;

public sealed record ClapDescription(string Text, decimal Similarity);

public sealed record ClapCandidate(string Label, decimal? Cosine, decimal? LibraryRelativeZ);

public sealed record ClapFacet(string Name, IReadOnlyList<ClapCandidate> Top, decimal? TopGapZ, bool? Ambiguous);

public sealed record ClapMoodAxis(
    string Name,
    decimal? RelativeSignal,
    decimal? PositiveCosine,
    decimal? NegativeCosine,
    bool? Ambiguous);

public sealed record MeasuredAudioFeatures(
    decimal? DurationSeconds,
    decimal? EstimatedBpm,
    string? EstimatedKey,
    decimal? KeyCorrelation,
    decimal? RmsMean,
    decimal? RmsOpening20Seconds,
    decimal? RmsClosing20Seconds,
    decimal? SpectralCentroidMeanHz,
    decimal? OnsetsPerSecond);

public sealed record LyricReadStatus(string State, string? Version, string? Reason);

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
    string? ClapVersion = null,
    IReadOnlyList<ClapFacet>? Facets = null,
    IReadOnlyList<ClapMoodAxis>? MoodAxes = null,
    MeasuredAudioFeatures? AudioFeatures = null,
    string? ClapDataSource = null,
    string? ClapSourceAnalysisVersion = null,
    bool ClapProvenanceVerified = false,
    LyricInterpretation? Lyrics = null,
    LyricReadStatus? LyricStatus = null)
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
        string? clapDataSource,
        string? clapSourceAnalysisVersion,
        bool clapProvenanceVerified,
        ImmutableArray<ClapFacet> facets,
        ImmutableArray<ClapMoodAxis> moodAxes,
        MeasuredAudioFeatures? audioFeatures,
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
        ClapDataSource = clapDataSource;
        ClapSourceAnalysisVersion = clapSourceAnalysisVersion;
        ClapProvenanceVerified = clapProvenanceVerified;
        Facets = facets;
        MoodAxes = moodAxes;
        AudioFeatures = audioFeatures;
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
    public string? ClapDataSource { get; }
    public string? ClapSourceAnalysisVersion { get; }
    public bool ClapProvenanceVerified { get; }
    public ImmutableArray<ClapFacet> Facets { get; }
    public ImmutableArray<ClapMoodAxis> MoodAxes { get; }
    public MeasuredAudioFeatures? AudioFeatures { get; }
    public string? AnalysisStatus { get; }
    public LyricInterpretation? Lyrics { get; private init; }

    public static VerifiedMusicContext Create(Track track, MusicIntelligence analysis)
    {
        if (track.SamSongId is null or <= 0 ||
            string.IsNullOrWhiteSpace(track.Artist) || string.IsNullOrWhiteSpace(track.Title))
            throw new InvalidOperationException("Verified SAM song metadata is required to create music context.");

        var samSongId = track.SamSongId.Value;
        var sameSong = analysis.SamSongId == samSongId;
        var identityVerified = sameSong && analysis.IdentityMappingFound && analysis.AneedId.HasValue;
        var clapVerified = identityVerified && analysis.ClapMatchFound;
        var audioFeatures = identityVerified ? analysis.AudioFeatures : null;
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
            audioFeatures?.DurationSeconds ?? (identityVerified ? analysis.DurationSeconds : null),
            clapVerified ? analysis.SampleRate : null,
            audioFeatures?.EstimatedBpm ?? (identityVerified ? analysis.Tempo : null),
            audioFeatures?.EstimatedKey ?? (identityVerified ? analysis.MusicalKey : null),
            clapVerified ? analysis.ClapModel : null,
            clapVerified ? analysis.ClapVersion : null,
            clapVerified ? analysis.ClapDataSource : null,
            clapVerified ? analysis.ClapSourceAnalysisVersion : null,
            clapVerified && analysis.ClapProvenanceVerified,
            clapVerified && analysis.Facets is not null ? ImmutableArray.CreateRange(analysis.Facets) : [],
            clapVerified && analysis.MoodAxes is not null ? ImmutableArray.CreateRange(analysis.MoodAxes) : [],
            audioFeatures,
            analysisStatus)
        {
            Lyrics = identityVerified && analysis.Lyrics?.IsValidFor(analysis.AneedId) == true ? analysis.Lyrics : null
        };
    }
}

public sealed class ClapMusicAnalysisService(
    IWebHostEnvironment environment,
    ILogger<ClapMusicAnalysisService> logger)
{
    public const string V22AnalysisVersion = "clap-descriptors-v2.2-deep-prompt-debias-v1";
    public const string V22SourceModel = "laion/clap-htsat-unfused";
    public const string V22SourceAnalysisVersion = "clap-htsat-unfused-3clips-v1";
    private static readonly string[] FacetNames = ["style", "instrumentation", "rhythm", "vocal", "texture", "energy"];
    private static readonly string[] MoodAxisNames = ["valence", "arousal", "tension", "aggression", "hope", "nostalgia", "intimacy", "mystery"];
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

    private async Task<MusicIntelligence> GetClapAnalysisAsync(
        string connectionString,
        long samSongId,
        Guid aneedId,
        CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = ConnectionTimeoutSeconds,
            CommandTimeout = CommandTimeoutSeconds,
            Database = "postgres"
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        {
            readOnly.CommandTimeout = CommandTimeoutSeconds;
            await readOnly.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var inventoryCommand = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM music.inventory
            WHERE aneed_id = @aneedId
            """, connection, transaction)
        {
            CommandTimeout = CommandTimeoutSeconds
        };
        inventoryCommand.Parameters.Add("aneedId", NpgsqlDbType.Uuid).Value = aneedId;
        var inventoryCount = Convert.ToInt32(await inventoryCommand.ExecuteScalarAsync(cancellationToken));
        if (inventoryCount != 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return MissingAnalysis(samSongId, aneedId, inventoryCount == 0
                ? "No PostgreSQL inventory record matches this AneedId."
                : "Multiple PostgreSQL inventory records match this AneedId; no analysis was selected.");
        }

        var facets = ImmutableArray<ClapFacet>.Empty;
        var moodAxes = ImmutableArray<ClapMoodAxis>.Empty;
        string? clapModel = null;
        string? clapVersion = null;
        string? sourceAnalysisVersion = null;
        string? dataSource = null;
        var provenanceVerified = false;
        IReadOnlyList<ClapDescription> descriptions = [];

        await using (var clapCommand = new NpgsqlCommand("""
            SELECT d.analysis_version, d.source_model_id, d.source_analysis_version,
                   d.source_identity, d.facets::text, d.mood_axes::text,
                   e.source_identity, e.model_id, e.analysis_version
            FROM music.clap_descriptors AS d
            LEFT JOIN music.audio_embeddings AS e ON e.aneed_id = d.aneed_id
            WHERE d.aneed_id = @aneedId AND d.analysis_version = @analysisVersion
            """, connection, transaction)
        {
            CommandTimeout = CommandTimeoutSeconds
        })
        {
            clapCommand.Parameters.Add("aneedId", NpgsqlDbType.Uuid).Value = aneedId;
            clapCommand.Parameters.Add("analysisVersion", NpgsqlDbType.Text).Value = V22AnalysisVersion;
            await using var reader = await clapCommand.ExecuteReaderAsync(cancellationToken);
            var rowCount = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowCount++;
                if (rowCount != 1) continue;

                var descriptorVersion = ReadNullableString(reader, 0);
                var descriptorModel = ReadNullableString(reader, 1);
                var descriptorSourceVersion = ReadNullableString(reader, 2);
                var descriptorIdentity = ReadNullableString(reader, 3);
                var embeddingIdentity = ReadNullableString(reader, 6);
                var embeddingModel = ReadNullableString(reader, 7);
                var embeddingVersion = ReadNullableString(reader, 8);
                if (!IsV22ProvenanceVerified(descriptorVersion, descriptorModel, descriptorSourceVersion,
                        descriptorIdentity, embeddingIdentity, embeddingModel, embeddingVersion))
                    continue;

                if (!TryParseFacets(reader.GetValue(4).ToString() ?? string.Empty, out var parsedFacets) ||
                    !TryParseMoodAxes(reader.GetValue(5).ToString() ?? string.Empty, out var parsedMoodAxes))
                    continue;

                facets = ImmutableArray.CreateRange(parsedFacets);
                moodAxes = ImmutableArray.CreateRange(parsedMoodAxes);
                clapModel = descriptorModel;
                clapVersion = descriptorVersion;
                sourceAnalysisVersion = descriptorSourceVersion;
                dataSource = "CLAP v2.2 descriptors";
                provenanceVerified = true;
            }
        }

        if (!provenanceVerified)
        {
            await using var legacyCommand = new NpgsqlCommand("""
                SELECT model_id, analysis_version, top_descriptions
                FROM music.audio_embeddings
                WHERE aneed_id = @aneedId
                """, connection, transaction)
            {
                CommandTimeout = CommandTimeoutSeconds
            };
            legacyCommand.Parameters.Add("aneedId", NpgsqlDbType.Uuid).Value = aneedId;
            await using var reader = await legacyCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var legacyModel = ReadNullableString(reader, 0);
                var legacyVersion = ReadNullableString(reader, 1);
                var rawDescriptions = reader.IsDBNull(2) ? null : reader.GetValue(2).ToString();
                var multipleRows = await reader.ReadAsync(cancellationToken);
                if (!multipleRows && !string.IsNullOrWhiteSpace(rawDescriptions))
                {
                    try { descriptions = ParseDescriptions(rawDescriptions); }
                    catch (JsonException) { descriptions = []; }
                    if (descriptions.Count > 0)
                    {
                        clapModel = legacyModel;
                        clapVersion = legacyVersion;
                        dataSource = "Legacy music.audio_embeddings.top_descriptions fallback";
                    }
                }
            }
        }

        MeasuredAudioFeatures? audioFeatures = null;
        await using (var featureCommand = new NpgsqlCommand("""
            SELECT duration_seconds, estimated_bpm, estimated_key, key_correlation,
                   rms_mean, rms_opening_20s, rms_closing_20s,
                   spectral_centroid_mean_hz, onsets_per_second
            FROM music.audio_features
            WHERE aneed_id = @aneedId
            """, connection, transaction)
        {
            CommandTimeout = CommandTimeoutSeconds
        })
        {
            featureCommand.Parameters.Add("aneedId", NpgsqlDbType.Uuid).Value = aneedId;
            await using var reader = await featureCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var rowFeatures = new MeasuredAudioFeatures(
                    ReadNullableDecimal(reader, 0), ReadNullableDecimal(reader, 1), ReadNullableString(reader, 2),
                    ReadNullableDecimal(reader, 3), ReadNullableDecimal(reader, 4), ReadNullableDecimal(reader, 5),
                    ReadNullableDecimal(reader, 6), ReadNullableDecimal(reader, 7), ReadNullableDecimal(reader, 8));
                var multipleRows = await reader.ReadAsync(cancellationToken);
                if (!multipleRows)
                    audioFeatures = rowFeatures;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var clapFound = provenanceVerified || descriptions.Count > 0;
        LyricInterpretation? lyrics = null;
        LyricReadStatus lyricStatus;
        try
        {
            (lyrics, lyricStatus) = await ReadLyricsAsync(connection, aneedId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is NpgsqlException or JsonException or OverflowException)
        {
            logger.LogWarning("Optional lyric analysis unavailable for {AneedId} ({ErrorType})", aneedId, ex.GetType().Name);
            lyricStatus = new("Error", null, "The lyric analysis could not be read.");
        }
        var error = clapFound ? null :
            $"No valid {V22AnalysisVersion} descriptor or unambiguous legacy CLAP descriptions were available for this AneedId.";

        return new MusicIntelligence(
            samSongId, aneedId, null, null, null, true, clapFound, descriptions,
            audioFeatures?.DurationSeconds, null, audioFeatures?.EstimatedBpm, audioFeatures?.EstimatedKey,
            error, clapModel, clapVersion,
            provenanceVerified ? facets : null,
            provenanceVerified ? moodAxes : null,
            audioFeatures,
            dataSource,
            sourceAnalysisVersion,
            provenanceVerified, lyrics, lyricStatus);
    }

    private static async Task<(LyricInterpretation?, LyricReadStatus)> ReadLyricsAsync(NpgsqlConnection connection, Guid aneedId, CancellationToken token)
    {
        // Independent SELECT after the audio transaction: a lyric query failure must not roll back audio context.
        await using var command = new NpgsqlCommand("""
            SELECT aneed_id, analysis_version, status, model_id, lyrics_source, analyzed_at, confidence,
                   themes::text, emotions::text, narrative::text, relationships::text, interpretation,
                   lyrics_available IS TRUE, NULLIF(btrim(error_message), '') IS NOT NULL
            FROM music.lyric_analysis
            WHERE aneed_id = @id
            ORDER BY analyzed_at DESC NULLS LAST, analysis_version
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = aneedId;
        await using var row = await command.ExecuteReaderAsync(token);
        LyricReadStatus? latest = null;
        while (await row.ReadAsync(token))
        {
            var version = row.GetString(1);
            var status = row.GetString(2);
            LyricReadStatus? excluded = row.GetBoolean(13) || status is "failed" or "error" ? new("Error", version, "Stored lyric analysis reports an error.")
                : status != "complete" || !row.GetBoolean(12) ? new("Not available", version, "No completed interpretation with lyrics available.")
                : version is not (LyricInterpretation.SupportedVersion or LyricInterpretation.UnverifiedVersion) ? new("Rejected", version, "Unsupported lyric analysis version.") : null;
            if (excluded is not null) { latest ??= excluded; continue; }
            var lyrics = LyricInterpretation.FromRow(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetString(3),
                ReadNullableString(row, 4), row.IsDBNull(5) ? null : row.GetDateTime(5), row.IsDBNull(6) ? null : row.GetDecimal(6),
                ReadNullableString(row, 7), ReadNullableString(row, 8), ReadNullableString(row, 9), ReadNullableString(row, 10), ReadNullableString(row, 11));
            if (lyrics is not null) return (lyrics, new("Loaded", version, null));
            latest ??= new("Rejected", version, "No usable narrative for this analysis version.");
        }
        return (null, latest ?? new("Not available", null, "No lyric analysis for this AneedId."));
    }

    public static bool IsV22ProvenanceVerified(
        string? descriptorVersion,
        string? descriptorModel,
        string? descriptorSourceVersion,
        string? descriptorIdentity,
        string? embeddingIdentity,
        string? embeddingModel,
        string? embeddingVersion)
        => string.Equals(descriptorVersion, V22AnalysisVersion, StringComparison.Ordinal) &&
           string.Equals(descriptorModel, V22SourceModel, StringComparison.Ordinal) &&
           string.Equals(descriptorSourceVersion, V22SourceAnalysisVersion, StringComparison.Ordinal) &&
           !string.IsNullOrWhiteSpace(descriptorIdentity) &&
           string.Equals(descriptorIdentity, embeddingIdentity, StringComparison.Ordinal) &&
           string.Equals(embeddingModel, V22SourceModel, StringComparison.Ordinal) &&
           string.Equals(embeddingVersion, V22SourceAnalysisVersion, StringComparison.Ordinal);

    public static bool TryParseFacets(string json, out IReadOnlyList<ClapFacet> facets)
    {
        facets = [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var parsed = new List<ClapFacet>(FacetNames.Length);
            foreach (var name in FacetNames)
            {
                if (!TryGetPropertyIgnoreCase(document.RootElement, name, out var facet) ||
                    facet.ValueKind != JsonValueKind.Object ||
                    !TryGetPropertyIgnoreCase(facet, "top", out var top) || top.ValueKind != JsonValueKind.Array)
                    return false;

                var candidates = new List<ClapCandidate>();
                foreach (var item in top.EnumerateArray().Take(3))
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !TryGetPropertyIgnoreCase(item, "label", out var label) || label.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(label.GetString()))
                        return false;

                    var candidateLabel = label.GetString()!.Trim();
                    candidates.Add(new ClapCandidate(
                        candidateLabel[..Math.Min(candidateLabel.Length, 120)],
                        ReadJsonDecimal(item, "cosine"),
                        ReadJsonDecimal(item, "library_relative_z")));
                }

                parsed.Add(new ClapFacet(name, candidates,
                    ReadJsonDecimal(facet, "top_gap_z"), ReadJsonBoolean(facet, "ambiguous")));
            }
            facets = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static bool TryParseMoodAxes(string json, out IReadOnlyList<ClapMoodAxis> moodAxes)
    {
        moodAxes = [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var parsed = new List<ClapMoodAxis>(MoodAxisNames.Length);
            foreach (var name in MoodAxisNames)
            {
                if (!TryGetPropertyIgnoreCase(document.RootElement, name, out var axis) ||
                    axis.ValueKind != JsonValueKind.Object ||
                    ReadJsonDecimal(axis, "relative_signal") is not { } signal)
                    return false;

                parsed.Add(new ClapMoodAxis(name, signal, ReadJsonDecimal(axis, "positive_cosine"),
                    ReadJsonDecimal(axis, "negative_cosine"), ReadJsonBoolean(axis, "ambiguous")));
            }
            moodAxes = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static decimal? ReadJsonDecimal(JsonElement element, string propertyName)
        => TryGetPropertyIgnoreCase(element, propertyName, out var value) &&
           value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;

    private static bool? ReadJsonBoolean(JsonElement element, string propertyName)
        => TryGetPropertyIgnoreCase(element, propertyName, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal).ToString();

    private static decimal? ReadNullableDecimal(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue when double.IsFinite(doubleValue) => (decimal)doubleValue,
            float floatValue when float.IsFinite(floatValue) => (decimal)floatValue,
            _ when decimal.TryParse(value.ToString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
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
