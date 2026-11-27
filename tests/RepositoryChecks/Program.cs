using AneedWeb.Data;
using AneedWeb.Djs;
using AneedWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

DjGroupRoom CreateTestRoom(HttpMessageHandler handler)
{
    var environment = new TestWebHostEnvironment();
    var configuration = new ConfigurationBuilder().Build();
    var loggerFactory = LoggerFactory.Create(_ => { });
    var httpClientFactory = new TestHttpClientFactory(handler);
    var broadcast = new BroadcastService(environment, loggerFactory.CreateLogger<BroadcastService>(), configuration, httpClientFactory);
    return new DjGroupRoom(httpClientFactory, environment, configuration, broadcast, loggerFactory.CreateLogger<DjGroupRoom>());
}

var rng = new Random(731);
var draws = Enumerable.Range(0, 256).Select(_ => DjPlaybackCoordinator.SelectRoster(DjProfiles.All, rng)).ToArray();
Check(draws.All(ids => ids.Length is >= 5 and <= 8 && ids.Distinct().Count() == ids.Length), "Each lineup needs 5–8 distinct DJs.");
Check(draws.Select(ids => ids.Length).Distinct().Count() == 4 && draws.Select(ids => ids[0]).Distinct().Count() == 8,
    "All four sizes and all eight opening speakers must be possible.");
var fullRoster = draws.First(ids => ids.Length == 8);
var start = DateTimeOffset.UtcNow;
foreach (var seconds in new[] { 4, 30, 120, 300 })
{
    var slots = DjPlaybackCoordinator.BuildSchedule(start, TimeSpan.FromSeconds(seconds), start, fullRoster);
    Check(slots.Select(t => t.DjId).SequenceEqual(fullRoster), "Even skipped slots must retain lineup order.");
    Check(slots[0].ScheduledAt == start.AddMilliseconds(50 * seconds) && slots[^1].ScheduledAt == start.AddMilliseconds(850 * seconds), "Slots must span 5% to 85%.");
    var pending = slots.Where(t => t.Status == "Pending").ToArray();
    Check(pending.Zip(pending.Skip(1)).All(pair => (pair.Second.ScheduledAt - pair.First.ScheduledAt).TotalSeconds >= 20), "Unsafe spacing.");
    Check(seconds switch { 4 => pending.Length == 0, 300 => pending.Length == 8, _ => pending.Length is > 0 and < 8 }, "Unexpected number of safe turns.");
}

var originalConnection = Environment.GetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION");
var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var originalLimit = Environment.GetEnvironmentVariable("ANEED_DJ_MAX_OUTPUT_TOKENS");
try
{
    Environment.SetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION", null);
    var configuration = new ConfigurationBuilder().Build();
    var unconfigured = new AzureSqlCatalogRepository(configuration);
    Check(!unconfigured.Configured, "Catalog must report missing Azure SQL configuration.");

    var artistId = ArtistIdentity.FromName("  The Aneed Signal  ");
    Check(artistId > 0, "Artist identity must be a positive stable route ID.");
    Check(artistId == ArtistIdentity.FromName("the aneed signal"), "Artist identity must be case and whitespace stable.");
    Check(artistId != ArtistIdentity.FromName("Another Artist"), "Distinct artist names should have distinct identities in this fixture.");

    Check(AzureSqlCatalogRepository.EscapeSearchTerm("  50%_\\  ") == "50\\%\\_\\\\", "Search terms must escape SQL LIKE wildcards.");
    Check(AzureSqlCatalogRepository.NormalizePage(0) == 1, "Pagination must clamp page numbers to one.");
    Check(AzureSqlCatalogRepository.NormalizePageSize(0) == 1, "Pagination must clamp page size to one.");
    Check(AzureSqlCatalogRepository.NormalizePageSize(500) == 100, "Pagination must cap page size at 100.");

    const string orderedHistory = """
    {
      "history": [
        { "id": 1001, "artist": "A", "title": "Now", "album": "Alpha", "picture": "now.jpg" },
        { "id": 1002, "artist": "B", "title": "Recent1", "album": "Beta", "picture": "r1.jpg" },
        { "id": 1003, "artist": "C", "title": "Recent2", "album": "Gamma", "picture": "r2.jpg" },
        { "id": 1004, "artist": "D", "title": "Recent3", "album": "Delta", "picture": "r3.jpg" },
        { "id": 1005, "artist": "E", "title": "Recent4", "album": "Epsilon", "picture": "r4.jpg" },
        { "id": 1006, "artist": "F", "title": "Ignored", "album": "Zeta", "picture": "r5.jpg" }
      ]
    }
    """;

    var ordered = BroadcastService.ParseHistorySnapshot(orderedHistory);
    Check(ordered.Playing, "Broadcast should be marked playing when SAM history has entries.");
    Check(ordered.Current?.SamSongId == 1001, "history[0] must be used as current song.");
    Check(ordered.Recent.Count == 4, "Only history[1..4] should be shown as recently played.");
    Check(ordered.Recent[0].SamSongId == 1002 && ordered.Recent[3].SamSongId == 1005, "Recent ordering must follow SAM history order.");

    var empty = BroadcastService.ParseHistorySnapshot("{\"history\":[]}");
    Check(!empty.Playing && empty.Current is null && empty.Recent.Count == 0, "Empty SAM history should yield an empty, not-playing snapshot.");

    const string arrayRootHistory = """
    [
      { "id": 2001, "artist": "Array Artist", "title": "Array Current", "album": "Array Album", "picture": "arr.jpg" },
      { "id": 2002, "artist": "Array Next", "title": "Array Recent", "album": "Array Album", "picture": "arr2.jpg" }
    ]
    """;

    var arrayRoot = BroadcastService.ParseHistorySnapshot(arrayRootHistory);
    Check(arrayRoot.Playing, "Array-root SAM history payload should be accepted.");
    Check(arrayRoot.Current?.SamSongId == 2001 && arrayRoot.Current.Artist == "Array Artist" && arrayRoot.Current.Title == "Array Current", "Array-root payload must preserve current SAM metadata.");

    var missingHistory = BroadcastService.ParseHistorySnapshot("{}");
    Check(!missingHistory.Playing && missingHistory.Error is not null, "Missing SAM history array should produce an explicit parsing error.");

    var malformedThrown = false;
    try
    {
        _ = BroadcastService.ParseHistorySnapshot("{\"history\":");
    }
    catch (JsonException)
    {
        malformedThrown = true;
    }

    Check(malformedThrown, "Malformed SAM history JSON should throw JsonException for caller-level handling.");

    var clapDescriptions = ClapMusicAnalysisService.ParseDescriptions("""
        [{"text":"an upbeat dance rock song with a steady club rhythm","similarity":0.4362}]
        """);
    Check(clapDescriptions.Count == 1, "CLAP top_descriptions JSONB array should parse to one observation.");
    Check(clapDescriptions[0].Text == "an upbeat dance rock song with a steady club rhythm" && clapDescriptions[0].Similarity == 0.4362m,
        "CLAP observation text and similarity should be preserved exactly.");

    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANEED_SAM_MARIADB")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANEED_POSTGRES")))
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var analysisService = new ClapMusicAnalysisService(new TestWebHostEnvironment(), loggerFactory.CreateLogger<ClapMusicAnalysisService>());
        try
        {
            var controlAnalysis = await analysisService.GetForSamSongAsync(11724);
            Check(controlAnalysis.AneedId == Guid.Parse("95978eb4-ffa5-4aa8-b061-1a8c087c8df8"),
                $"SAM 11724 must resolve to the verified AneedId control GUID. Service result: {controlAnalysis.Error ?? "A different AneedId was returned."}");
            Check(controlAnalysis.ClapMatchFound && controlAnalysis.Descriptions.Count > 0,
                $"SAM 11724 must return its matching PostgreSQL CLAP descriptions. Service result: {controlAnalysis.Error ?? "No CLAP descriptions were returned."}");
            Console.WriteLine("PASS: Live read-only SAM 11724 identity and PostgreSQL CLAP control-record check.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL: Live read-only SAM 11724 control-record check: {exception.Message}");
        }
    }
    else
    {
        Console.WriteLine("SKIP: Live SAM/MariaDB and PostgreSQL verification requires ANEED_SAM_MARIADB and ANEED_POSTGRES in this process.");
    }

    Environment.SetEnvironmentVariable("OPENAI_API_KEY", "repository-check-test-key");
    Environment.SetEnvironmentVariable("ANEED_DJ_MAX_OUTPUT_TOKENS", "800");
    var rehearsalTrack = new Track(43210, "Verified Title", "Verified Artist", "Verified Album", null, null, null);
    var rehearsalAneedId = Guid.Parse("d04f8517-6aa8-4875-8119-a5970708b1e6");
    var rehearsalAnalysis = new MusicIntelligence(
        43210,
        rehearsalAneedId,
        rehearsalTrack.Artist,
        rehearsalTrack.Title,
        rehearsalTrack.Album,
        true,
        true,
        [new ClapDescription("a warm synth bass and crisp hand percussion", 0.8123m)],
        214.5m,
        44100,
        118.25m,
        "E minor",
        null) with
    {
        AudioFeatures = new MeasuredAudioFeatures(214.5m, 118.25m, "E minor", null, null, null, null, null, null),
        ClapDataSource = "Legacy test fixture"
    };
    var lyric = LyricInterpretation.FromRow(rehearsalAneedId, LyricInterpretation.SupportedVersion, "complete", "test-model", "fixture",
        DateTime.UnixEpoch, 0.8m, "[\"departures\"]", "[\"ambivalence\"]", """{"perspective":"observer","conflict":"stay or leave","lyrics":"excluded material"}""",
        """[{"dynamic":"estrangement","quotation":"excluded material"}]""", new string('z', 5000)) ?? throw new Exception("Valid lyric projection rejected.");
    Check(lyric.Interpretation?.Length == 320 && lyric.Narrative!.Contains("observer") && !lyric.Narrative.Contains("excluded material"), "Interpretation must be bounded and allowlisted.");
    rehearsalAnalysis = rehearsalAnalysis with { Lyrics = lyric };
    foreach (var invalid in new[] { rehearsalAnalysis with { Lyrics = null }, rehearsalAnalysis with { Lyrics = lyric with { Status = "pending" } },
        rehearsalAnalysis with { Lyrics = lyric with { Version = "unsupported" } }, rehearsalAnalysis with { Lyrics = lyric with { AneedId = Guid.Empty } },
        rehearsalAnalysis with { SamSongId = 12345 } })
        Check(VerifiedMusicContext.Create(rehearsalTrack, invalid).Lyrics is null, "Missing, incomplete, unrelated or unsupported interpretation must not reach a DJ.");
    var sharedMusicContext = VerifiedMusicContext.Create(rehearsalTrack, rehearsalAnalysis);
    var originalMaria = Environment.GetEnvironmentVariable("ANEED_SAM_MARIADB");
    var originalDuration = Environment.GetEnvironmentVariable("ANEED_DJ_ESTIMATED_DURATION_SECONDS");
    try
    {
        Environment.SetEnvironmentVariable("ANEED_SAM_MARIADB", null);
        Environment.SetEnvironmentVariable("ANEED_DJ_ESTIMATED_DURATION_SECONDS", "30");
        using var handler = new DelayedReplyHandler();
        using var logs = LoggerFactory.Create(_ => { });
        var host = new TestWebHostEnvironment();
        var broadcast = new BroadcastService(host, logs.CreateLogger<BroadcastService>(), configuration, new TestHttpClientFactory(handler));
        using var room = CreateTestRoom(handler);
        using var playback = new DjPlaybackCoordinator(broadcast, new ClapMusicAnalysisService(host, logs.CreateLogger<ClapMusicAnalysisService>()), room,
            host, logs.CreateLogger<DjPlaybackCoordinator>(), TimeProvider.System);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstEvent = new Broadcast(true, rehearsalTrack, [], null);
        await playback.ObserveBroadcastAsync(firstEvent, stop.Token);
        var first = playback.Snapshot();
        await playback.ObserveBroadcastAsync(firstEvent, stop.Token);
        Check(ReferenceEquals(first.SelectedDjIds, playback.Snapshot().SelectedDjIds) && first.SessionId == playback.Snapshot().SessionId, "Repeated polls must reuse the session lineup.");
        await handler.Started.Task.WaitAsync(stop.Token);
        await playback.ObserveBroadcastAsync(firstEvent with { Current = rehearsalTrack with { Started = DateTimeOffset.UtcNow, Title = "Next playback" } }, stop.Token);
        var next = playback.Snapshot();
        Check(next.SessionId != first.SessionId && !ReferenceEquals(next.SelectedDjIds, first.SelectedDjIds), "A replay needs a fresh lineup draw.");
        await handler.Canceled.Task.WaitAsync(stop.Token);
        handler.Release.TrySetResult();
        while (playback.Snapshot().Lines.Count == 0) await Task.Delay(20, stop.Token);
        var result = playback.Snapshot();
        Check(handler.MaxActive == 1 && result.Lines.All(line => line.SessionId == next.SessionId && line.Text == "New reply") &&
            result.Turns.Any(turn => turn.Status == "Complete" && turn.ActualAt.HasValue), "Requests must not overlap or publish a stale reply.");
        await stop.CancelAsync();
    }
    finally
    {
        Environment.SetEnvironmentVariable("ANEED_SAM_MARIADB", originalMaria);
        Environment.SetEnvironmentVariable("ANEED_DJ_ESTIMATED_DURATION_SECONDS", originalDuration);
    }
    var scheduledHandler = new RecordingOpenAiHandler();
    using (var room = CreateTestRoom(scheduledHandler))
    {
        var session = Guid.NewGuid();
        room.Reset(session);
        var due = fullRoster.Select((id, i) => new DjScheduledTurn(i + 1, id, DjProfiles.Get(id).Name, DateTimeOffset.UtcNow, null, "Pending")).ToArray();
        await room.RunScheduledTurnsAsync(sharedMusicContext, session, due, DateTimeOffset.UtcNow.AddMinutes(1),
            id => id == session, (id, commit) => { if (id != session) return false; commit(); return true; }, (_, _) => { }, default);
        Check(room.Snapshot().Select(line => line.DjId).SequenceEqual(fullRoster) && room.DiagnosticsSnapshot().Select(d => d.DjId).SequenceEqual(fullRoster), "Scheduled speakers must match replies and diagnostics.");
        for (var i = 0; i < due.Length; i++)
        {
            var request = scheduledHandler.Requests[i];
            Check(request.GetProperty("instructions").GetString()!.Contains(DjProfiles.BuildSystemPrompt(DjProfiles.Get(due[i].DjId))), "Wrong character instructions.");
            var input = request.GetProperty("input").GetString()!;
            Check(input.Contains("SAM ID: 43210") && due.Take(i).All(t => input.Contains($"{t.Speaker}: Rehearsal reply {t.Turn}.")), "Missing shared song or conversation history.");
            Check(new[] { "Lyrical Interpretation:", "departures", "ambivalence", "observer", "stay or leave", "estrangement", lyric.Version, lyric.Model, lyric.Status }.All(input.Contains) &&
                !input.Contains("excluded material"), "Every scheduled DJ must receive only the verified interpretation and its provenance.");
        }

    }
    Check(sharedMusicContext.Descriptions.Length == 1, "Verified context must snapshot the matched CLAP description.");
    var mismatchedAnalysisContext = VerifiedMusicContext.Create(rehearsalTrack, rehearsalAnalysis with { SamSongId = 43211 });
    Check(mismatchedAnalysisContext.AneedId is null && !mismatchedAnalysisContext.ClapMatchFound &&
          mismatchedAnalysisContext.Descriptions.IsEmpty && mismatchedAnalysisContext.Tempo is null,
        "A CLAP analysis with a different SAM ID must not be attributed to the current song.");

    var rehearsalHandler = new RecordingOpenAiHandler();
    using (var room = CreateTestRoom(rehearsalHandler))
    {
        await room.RunThreeTurnsAsync(sharedMusicContext);
        Check(rehearsalHandler.Requests.Count == 3, "A rehearsal should issue exactly three OpenAI requests.");

        var requestInputs = rehearsalHandler.Requests.Select(request => request.GetProperty("input").GetString()!).ToArray();
        var sharedContextPrefixes = requestInputs.Select(input => input.Split("\n\n", 2)[0]).ToArray();
        Check(sharedContextPrefixes.All(prefix => prefix == sharedContextPrefixes[0]), "All three requests must receive the identical song and CLAP context.");
        foreach (var request in rehearsalHandler.Requests)
        {
            var input = request.GetProperty("input").GetString()!;
            Check(input.Contains("SAM ID: 43210", StringComparison.Ordinal), "Every DJ request must include the SAM song ID.");
            Check(input.Contains("Artist: Verified Artist", StringComparison.Ordinal) && input.Contains("Title: Verified Title", StringComparison.Ordinal),
                "Every DJ request must include the verified artist/title.");
            Check(input.Contains(rehearsalAneedId.ToString(), StringComparison.Ordinal), "Every DJ request must include the permanent AneedId GUID.");
            Check(input.Contains("Legacy CLAP fallback (unversioned descriptions; tentative): a warm synth bass and crisp hand percussion", StringComparison.Ordinal),
                "Every DJ request must include the same legacy CLAP fallback description with its uncertainty label.");
            Check(input.Contains("BPM estimate 118.25", StringComparison.Ordinal) && input.Contains("key estimate E minor", StringComparison.Ordinal),
                "Every DJ request must include the measured audio BPM and key estimates.");
            Check(input.Contains("duration 214.5s", StringComparison.Ordinal),
                "Every DJ request must include the available measured audio duration.");
        }

        Check(!requestInputs[0].Contains("Recent studio conversation:", StringComparison.Ordinal), "The first DJ should start without prior turns.");
        Check(requestInputs[1].Contains("Recent studio conversation:\nKathleen:", StringComparison.Ordinal),
            "The second DJ should receive Kathleen's preceding turn.");
        Check(requestInputs[2].Contains("Recent studio conversation:\nKathleen:", StringComparison.Ordinal) &&
              requestInputs[2].Contains("\nLexi Static:", StringComparison.Ordinal),
            "The third DJ should receive both preceding turns.");
        var expectedProfiles = DjProfiles.All.Take(3).ToArray();
        for (var turn = 0; turn < expectedProfiles.Length; turn++)
            Check(rehearsalHandler.Requests[turn].GetProperty("instructions").GetString()!.Contains(expectedProfiles[turn].CharacterPrompt, StringComparison.Ordinal),
                $"Turn {turn + 1} must retain the selected DJ's character prompt.");
        Check(rehearsalHandler.Requests.All(request => request.GetProperty("max_output_tokens").GetInt32() == 800),
            "Every DJ request must use the requested 800-token output budget.");

        var diagnostics = room.DiagnosticsSnapshot();
        Check(diagnostics.Count == 3 && diagnostics.All(diagnostic => diagnostic.MusicContextReceived),
            "Development diagnostics must confirm context delivery for each DJ turn.");
        Check(diagnostics.All(diagnostic => diagnostic.SamSongId == 43210 && diagnostic.AneedId == rehearsalAneedId && diagnostic.AnalysisMatchStatus == "Verified"),
            "Development diagnostics must report the same song identity and verified analysis on all turns.");
    }

    var missingAnalysis = MusicIntelligence.Unavailable(43210, "No verified CLAP analysis match.");
    var metadataOnlyContext = VerifiedMusicContext.Create(rehearsalTrack, missingAnalysis);
    Check(!metadataOnlyContext.ClapMatchFound && metadataOnlyContext.Tempo is null && metadataOnlyContext.MusicalKey is null,
        "Missing analysis must not retain or invent audio measurements.");
    var metadataHandler = new RecordingOpenAiHandler();
    using (var room = CreateTestRoom(metadataHandler))
    {
        await room.RunThreeTurnsAsync(metadataOnlyContext);
        Check(metadataHandler.Requests.Count == 3, "Metadata-only rehearsal should still issue three requests.");
        foreach (var request in metadataHandler.Requests)
        {
            var input = request.GetProperty("input").GetString()!;
            Check(input.Contains("Verified Artist", StringComparison.Ordinal) && input.Contains("Verified Title", StringComparison.Ordinal),
                "All DJs must receive verified metadata when analysis is missing.");
            Check(input.Contains("Lyrical Interpretation: unavailable", StringComparison.Ordinal) && input.Contains("CLAP facets: missing", StringComparison.Ordinal) &&
                  input.Contains("Measured audio: duration missing; BPM estimate missing; key estimate missing", StringComparison.Ordinal),
                "All DJs must be told analysis is missing without fabricated measurements.");
        }
        var metadataDiagnostics = room.DiagnosticsSnapshot();
        Check(metadataDiagnostics.Count == 3 && metadataDiagnostics.All(diagnostic => diagnostic.MusicContextReceived && diagnostic.AnalysisMatchStatus == "Missing"),
            "Development diagnostics must confirm metadata context was received while reporting missing analysis.");
    }

    var partialHandler = new RecordingOpenAiHandler { IncompleteOnRequestNumber = 2 };
    using (var room = CreateTestRoom(partialHandler))
    {
        var failedAfterFirstReply = false;
        try
        {
            await room.RunThreeTurnsAsync(sharedMusicContext);
        }
        catch (InvalidOperationException exception)
        {
            failedAfterFirstReply = exception.Message.Contains("OpenAI response incomplete (max_output_tokens)", StringComparison.Ordinal) &&
                                    exception.Message.Contains("usable text returned: no", StringComparison.Ordinal);
        }

        Check(failedAfterFirstReply, "An incomplete response without text must report its response status and reason.");
        Check(room.Snapshot().Count == 1 && room.Snapshot()[0].Text == "Rehearsal reply 1.",
            "A failed later turn must leave earlier successful DJ replies visible.");
        var partialDiagnostic = room.DiagnosticsSnapshot().Single(diagnostic => diagnostic.Turn == 2);
        Check(partialDiagnostic.ResponseStatus == "incomplete" && partialDiagnostic.IncompleteReason == "max_output_tokens" &&
              partialDiagnostic.OutputTokens == 800 && partialDiagnostic.UsableTextReturned == false,
            "The failed turn diagnostics must include status, incomplete reason, output token usage, and no usable text.");
    }

    var incompleteWithTextHandler = new RecordingOpenAiHandler
    {
        IncompleteOnRequestNumber = 2,
        IncludeTextInIncompleteResponse = true
    };
    using (var room = CreateTestRoom(incompleteWithTextHandler))
    {
        try
        {
            await room.RunThreeTurnsAsync(sharedMusicContext);
        }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains("OpenAI response incomplete (max_output_tokens)", StringComparison.Ordinal) &&
                  exception.Message.Contains("usable text returned: yes", StringComparison.Ordinal),
                "An incomplete response that includes text must say so accurately.");
        }

        var incompleteWithTextDiagnostic = room.DiagnosticsSnapshot().Single(diagnostic => diagnostic.Turn == 2);
        Check(incompleteWithTextDiagnostic.ResponseStatus == "incomplete" && incompleteWithTextDiagnostic.UsableTextReturned == true,
            "The failed turn diagnostics must record when an incomplete response included usable text.");
        Check(room.Snapshot().Count == 1, "A response marked incomplete must not replace or remove successful earlier turns.");
    }

    Check(BroadcastService.BuildArtworkFallback("https://cdn.example/cover.jpg", "sam.jpg") == "https://cdn.example/cover.jpg", "Mapped SQL artwork must win over SAM picture fallback.");
    Check(BroadcastService.BuildArtworkFallback(null, "sam cover.jpg") == "/media/covers/sam%20cover.jpg", "Valid SAM picture should map to /media/covers fallback.");
    Check(BroadcastService.BuildArtworkFallback(null, "../sam.jpg") == "/images/station.png", "Invalid SAM picture filenames must fall back to station artwork.");

    Console.WriteLine("PASS: Azure SQL catalog identity/search/pagination plus SAM history ordering and artwork fallback checks.");
    Console.WriteLine("PASS: Three-turn DJ rehearsal shares one verified context, retains per-DJ prompts, grows transcript, and handles missing analysis safely.");
}
finally
{
    Environment.SetEnvironmentVariable("ANEED_AZURE_SQL_CONNECTION", originalConnection);
    Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
    Environment.SetEnvironmentVariable("ANEED_DJ_MAX_OUTPUT_TOKENS", originalLimit);
}

sealed class DelayedReplyHandler : HttpMessageHandler
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int active, requests;
    public int MaxActive { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        MaxActive = Math.Max(MaxActive, Interlocked.Increment(ref active));
        var first = Interlocked.Increment(ref requests) == 1;
        try
        {
            if (first)
            {
                using var registration = token.Register(() => Canceled.TrySetResult());
                Started.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "completed", output_text = first ? "Stale reply" : "New reply" }) };
        }
        finally { Interlocked.Decrement(ref active); }
    }
}

sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "RepositoryChecks";
    public string EnvironmentName { get; set; } = "Development";
    public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class RecordingOpenAiHandler : HttpMessageHandler
{
    public List<JsonElement> Requests { get; } = [];
    public int? IncompleteOnRequestNumber { get; init; }
    public bool IncludeTextInIncompleteResponse { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        Requests.Add(document.RootElement.Clone());
        var reply = $"Rehearsal reply {Requests.Count}.";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                status = IncompleteOnRequestNumber == Requests.Count ? "incomplete" : "completed",
                incomplete_details = IncompleteOnRequestNumber == Requests.Count ? new { reason = "max_output_tokens" } : null,
                usage = new { output_tokens = IncompleteOnRequestNumber == Requests.Count ? 800 : 24 },
                output = new[]
                {
                    new
                    {
                        type = "message",
                        content = IncompleteOnRequestNumber == Requests.Count && !IncludeTextInIncompleteResponse
                            ? Array.Empty<object>()
                            : [new { type = "output_text", text = reply }]
                    }
                }
            })
        };
    }
}
