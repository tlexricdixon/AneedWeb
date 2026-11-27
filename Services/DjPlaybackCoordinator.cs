using AneedWeb.Services;
using AneedWeb.Djs;

namespace AneedWeb.Services;

public sealed record DjScheduledTurn(
    int Turn,
    string DjId,
    string Speaker,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? ActualAt,
    string Status);

public sealed record DjPlaybackSnapshot(
    Broadcast Broadcast,
    MusicIntelligence? MusicIntelligence,
    Guid? SessionId,
    string Status,
    string? Message,
    DateTimeOffset? PlaybackStarted,
    TimeSpan Duration,
    bool DurationEstimated,
    IReadOnlyList<DjScheduledTurn> Turns,
    IReadOnlyList<DjGroupRoom.GroupLine> Lines,
    IReadOnlyList<DjGroupRoom.DjTurnDiagnostic> Diagnostics,
    TimeSpan Elapsed,
    bool PlaybackStartEstimated = false)
{
    public IReadOnlyList<string> SelectedDjIds { get; init; } = [];

    public static DjPlaybackSnapshot Initial { get; } = new(
        new Broadcast(false, null, [], "Waiting for SAM history."), null, null,
        "Waiting for SAM", null, null, TimeSpan.FromMinutes(3), true, [], [], [], TimeSpan.Zero);
}

public sealed class DjPlaybackCoordinator : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private readonly object _stateSync = new();
    private readonly BroadcastService _broadcastService;
    private readonly ClapMusicAnalysisService _analysisService;
    private readonly DjGroupRoom _room;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DjPlaybackCoordinator> _logger;
    private readonly TimeProvider _timeProvider;
    private DjPlaybackSnapshot _snapshot = DjPlaybackSnapshot.Initial;
    private PlaybackEventIdentity? _observedEvent;
    private CancellationTokenSource? _activeSessionCts;
    private Task? _activeSessionTask;
    private Guid? _activeSessionId;
    private string? _previousSessionMessage;

    public event Action? Changed;
    public bool Enabled => _environment.IsDevelopment();
    public bool IsDevelopment => _environment.IsDevelopment();
    public IReadOnlyList<DjGroupRoom.DjTurnDiagnostic> RoomDiagnosticsSnapshot() => _room.DiagnosticsSnapshot();

    public DjPlaybackCoordinator(
        BroadcastService broadcastService,
        ClapMusicAnalysisService analysisService,
        DjGroupRoom room,
        IWebHostEnvironment environment,
        ILogger<DjPlaybackCoordinator> logger,
        TimeProvider timeProvider)
    {
        _broadcastService = broadcastService;
        _analysisService = analysisService;
        _room = room;
        _environment = environment;
        _logger = logger;
        _timeProvider = timeProvider;
        _room.Changed += OnRoomChanged;
    }

    public DjPlaybackSnapshot Snapshot()
    {
        lock (_stateSync) return _snapshot;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_environment.IsDevelopment())
        {
            UpdateSnapshot(snapshot => snapshot with { Status = "Autonomous DJ mode disabled outside Development." });
            return;
        }

        UpdateSnapshot(snapshot => snapshot with { Status = "Monitoring SAM playback." });
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var broadcast = await _broadcastService.Get();
                    await ObserveBroadcastAsync(broadcast, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning("SAM polling failed ({ErrorType})", exception.GetType().Name);
                }

                await Task.Delay(PollInterval, _timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            CancelActiveSession("Application is shutting down.");
            var activeTask = _activeSessionTask;
            if (activeTask is not null)
            {
                try { await activeTask; }
                catch (OperationCanceledException) { }
                catch (Exception exception) { _logger.LogWarning("DJ session shutdown ended with {ErrorType}", exception.GetType().Name); }
            }
        }
    }

    public async Task ObserveBroadcastAsync(Broadcast broadcast, CancellationToken stoppingToken = default)
    {
        if (!_environment.IsDevelopment() || broadcast.IsStale || !broadcast.Playing ||
            broadcast.Current?.SamSongId is not > 0)
        {
            UpdateSnapshot(snapshot => snapshot with { Broadcast = broadcast });
            return;
        }

        var track = broadcast.Current;
        var identity = PlaybackEventIdentity.From(track!);
        string[] selectedDjIds;

        lock (_stateSync)
        {
            if (_observedEvent == identity)
            {
                _snapshot = _snapshot with { Broadcast = broadcast, Elapsed = _snapshot.PlaybackStarted is { } playbackStart ? MaxZero(_timeProvider.GetUtcNow() - playbackStart) : TimeSpan.Zero };
                PublishChanged();
                return;
            }

            var previousId = _activeSessionId;
            if (previousId is not null)
            {
                _previousSessionMessage = "Previous playback session canceled because a new SAM playback event was detected.";
                _activeSessionCts?.Cancel();
            }
            else
            {
                _previousSessionMessage = null;
            }

            _observedEvent = identity;
            _activeSessionId = Guid.NewGuid();
            selectedDjIds = SelectRoster(DjProfiles.All, Random.Shared);
            _activeSessionCts?.Dispose();
            _activeSessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var sessionId = _activeSessionId.Value;
            var sessionToken = _activeSessionCts.Token;
            var start = track!.Started ?? _timeProvider.GetUtcNow();
            var startEstimated = track.Started is null;
            _snapshot = new DjPlaybackSnapshot(
                broadcast,
                null,
                sessionId,
                "Resolving song context",
                _previousSessionMessage,
                start,
                TimeSpan.FromMinutes(3),
                true,
                [],
                [],
                [],
                MaxZero(_timeProvider.GetUtcNow() - start),
                startEstimated)
            {
                SelectedDjIds = selectedDjIds
            };
            _room.Reset(sessionId);
            _activeSessionTask = RunSessionAsync(track, selectedDjIds, sessionId, start, startEstimated, sessionToken);
        }
        PublishChanged();
        await Task.CompletedTask;
    }

    private async Task RunSessionAsync(Track track, IReadOnlyList<string> selectedDjIds, Guid sessionId, DateTimeOffset playbackStarted, bool startEstimated, CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await _analysisService.GetForSamSongAsync(track.SamSongId!.Value, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentSession(sessionId)) return;

            var context = VerifiedMusicContext.Create(track, analysis);
            var actualDuration = context.DurationSeconds is > 0 ? TimeSpan.FromSeconds((double)context.DurationSeconds.Value) : (TimeSpan?)null;
            var duration = actualDuration ?? ReadEstimatedDuration();
            var durationEstimated = actualDuration is null;
            var schedule = BuildSchedule(playbackStarted, duration, _timeProvider.GetUtcNow(), selectedDjIds);
            UpdateSnapshotForSession(sessionId, snapshot => snapshot with
            {
                MusicIntelligence = analysis,
                PlaybackStarted = playbackStarted,
                Duration = duration,
                DurationEstimated = durationEstimated,
                Turns = schedule,
                Status = schedule.All(turn => turn.Status == "Skipped") ? "No useful speaking opportunities remain" : "Scheduled",
                Message = durationEstimated
                    ? "Duration unavailable; using the configurable estimated scheduling window."
                    : startEstimated ? "SAM playback start unavailable; scheduling window begins at detection time." : null
            });

            if (schedule.All(turn => turn.Status == "Skipped")) return;
            if (!_room.ApiKeyConfigured) throw new InvalidOperationException(_room.ConfigurationDiagnostic);
            await _room.RunScheduledTurnsAsync(context, sessionId, schedule, playbackStarted + duration - TimeSpan.FromSeconds(5),
                IsCurrentSession, TryCommitReply, (id, status) => UpdateSnapshotForSession(sessionId, snapshot => snapshot with
                {
                    Turns = snapshot.Turns.Select(turn => turn.DjId == id ? turn with { Status = status } : turn).ToArray()
                }), cancellationToken);
            if (IsCurrentSession(sessionId))
                UpdateSnapshotForSession(sessionId, snapshot => snapshot with { Status = "Conversation complete" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (IsCurrentSession(sessionId))
                UpdateSnapshotForSession(sessionId, snapshot => EndSession(snapshot, "Canceled", "Playback session was canceled."));
        }
        catch (Exception exception)
        {
            _logger.LogWarning("DJ playback session {SessionId} failed ({ErrorType})", sessionId, exception.GetType().Name);
            if (IsCurrentSession(sessionId))
                UpdateSnapshotForSession(sessionId, snapshot => EndSession(snapshot, "Failed", exception.Message));
        }
    }

    private static DjPlaybackSnapshot EndSession(DjPlaybackSnapshot snapshot, string status, string message) => snapshot with
    {
        Status = status, Message = message,
        Turns = snapshot.Turns.Select(turn => turn.Status == "Pending" ? turn with { Status = "Canceled" } : turn).ToArray()
    };

    public static string[] SelectRoster(IReadOnlyList<DjProfile> profiles, Random random)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count < 5) throw new ArgumentException("At least five DJ profiles are required.", nameof(profiles));
        var ids = profiles.Select(profile => profile.Id).ToArray();
        random.Shuffle(ids);
        return ids[..random.Next(5, Math.Min(8, ids.Length) + 1)];
    }

    public static IReadOnlyList<DjScheduledTurn> BuildSchedule(
        DateTimeOffset playbackStarted,
        TimeSpan duration,
        DateTimeOffset now,
        IReadOnlyList<string> selectedDjIds)
    {
        ArgumentNullException.ThrowIfNull(selectedDjIds);
        if (selectedDjIds.Count is < 5 or > 8 || selectedDjIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedDjIds.Count)
            throw new ArgumentException("Select five through eight distinct DJ IDs.", nameof(selectedDjIds));

        var schedule = new List<DjScheduledTurn>(selectedDjIds.Count);
        DateTimeOffset? previous = null;
        for (var index = 0; index < selectedDjIds.Count; index++)
        {
            var fraction = 0.05m + 0.80m * index / (selectedDjIds.Count - 1);
            var target = playbackStarted + TimeSpan.FromTicks((long)(duration.Ticks * fraction));
            var fits = target > now && target < playbackStarted + duration - TimeSpan.FromSeconds(5) &&
                (previous is null || target - previous.Value >= TimeSpan.FromSeconds(20));
            var id = selectedDjIds[index];
            schedule.Add(new(index + 1, id, DjProfiles.Get(id).Name, target, null, fits ? "Pending" : "Skipped"));
            if (fits) previous = target;
        }
        return schedule;
    }

    private static TimeSpan ReadEstimatedDuration()
    {
        var configuredSeconds = Environment.GetEnvironmentVariable("ANEED_DJ_ESTIMATED_DURATION_SECONDS");
        return int.TryParse(configuredSeconds, out var seconds) && seconds >= 30 && seconds <= 1800
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(3);
    }

    private bool IsCurrentSession(Guid sessionId)
    {
        lock (_stateSync) return _activeSessionId == sessionId;
    }

    private bool TryCommitReply(Guid sessionId, Action commit)
    {
        lock (_stateSync)
        {
            if (_activeSessionId != sessionId) return false;
            commit();
            return true;
        }
    }

    private void CancelActiveSession(string reason)
    {
        lock (_stateSync)
        {
            _previousSessionMessage = reason;
            _activeSessionCts?.Cancel();
        }
    }

    private void OnRoomChanged()
    {
        var sessionId = Snapshot().SessionId;
        if (sessionId is null) return;
        UpdateSnapshotForSession(sessionId.Value, snapshot =>
        {
            var lines = _room.Snapshot().Where(line => line.SessionId == sessionId).ToArray();
            var diagnostics = _room.DiagnosticsSnapshot();
            return snapshot with {
            Lines = lines,
            Diagnostics = diagnostics,
            Turns = snapshot.Turns.Select(turn =>
            {
                var line = lines.FirstOrDefault(item => item.SessionId == sessionId && item.DjId == turn.DjId);
                return line is null ? turn : turn with { ActualAt = line.At, Status = "Complete" };
            }).ToArray(),
            Elapsed = snapshot.PlaybackStarted is { } start ? MaxZero(_timeProvider.GetUtcNow() - start) : TimeSpan.Zero
            };
        });
    }

    private void UpdateSnapshotForSession(Guid sessionId, Func<DjPlaybackSnapshot, DjPlaybackSnapshot> update)
    {
        lock (_stateSync)
        {
            if (_activeSessionId != sessionId) return;
            _snapshot = update(_snapshot);
        }
        PublishChanged();
    }

    private void UpdateSnapshot(Func<DjPlaybackSnapshot, DjPlaybackSnapshot> update)
    {
        lock (_stateSync) _snapshot = update(_snapshot);
        PublishChanged();
    }

    private void PublishChanged() => Changed?.Invoke();
    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        CancelActiveSession("Application is shutting down.");
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _room.Changed -= OnRoomChanged;
        try { _activeSessionCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        _activeSessionCts?.Dispose();
        _room.Reset(Guid.Empty);
        base.Dispose();
    }

    public readonly record struct PlaybackEventIdentity(long SamSongId, long? StartedUtcTicks)
    {
        public static PlaybackEventIdentity From(Track track)
            => new(track.SamSongId!.Value, track.Started?.ToUniversalTime().Ticks);
    }
}
