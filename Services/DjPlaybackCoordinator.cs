using AneedWeb.Services;
using AneedWeb.Djs;

namespace AneedWeb.Services;

public sealed record DjScheduledTurn(
    int Turn,
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
    public static DjPlaybackSnapshot Initial { get; } = new(
        new Broadcast(false, null, [], "Waiting for SAM history."), null, null,
        "Waiting for SAM", null, null, TimeSpan.FromMinutes(3), true, [], [], [], TimeSpan.Zero);
}

public sealed class DjPlaybackCoordinator : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
    private static readonly string[] Speakers = ["Kathleen", "Lexi Static", "Vesper Thorn"];
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
        UpdateSnapshot(snapshot => snapshot with { Broadcast = broadcast });

        lock (_stateSync)
        {
            if (_observedEvent == identity)
            {
                _snapshot = _snapshot with { Elapsed = _snapshot.PlaybackStarted is { } playbackStart ? MaxZero(_timeProvider.GetUtcNow() - playbackStart) : TimeSpan.Zero };
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
                startEstimated);
            _room.Reset(sessionId);
            _activeSessionTask = RunSessionAsync(track, sessionId, start, startEstimated, sessionToken);
        }
        PublishChanged();
        await Task.CompletedTask;
    }

    private async Task RunSessionAsync(Track track, Guid sessionId, DateTimeOffset playbackStarted, bool startEstimated, CancellationToken cancellationToken)
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
            var schedule = BuildSchedule(playbackStarted, duration, _timeProvider.GetUtcNow());
            UpdateSnapshotForSession(sessionId, snapshot => snapshot with
            {
                MusicIntelligence = analysis,
                PlaybackStarted = playbackStarted,
                Duration = duration,
                DurationEstimated = durationEstimated,
                Turns = schedule.Select(turn => new DjScheduledTurn(turn.Turn, Speakers[turn.Turn - 1], turn.ScheduledAt, null, "Scheduled")).ToArray(),
                Status = schedule.Count == 0 ? "No useful speaking opportunities remain" : "Scheduled",
                Message = durationEstimated
                    ? "Duration unavailable; using the configurable estimated scheduling window."
                    : startEstimated ? "SAM playback start unavailable; scheduling window begins at detection time." : null
            });

            if (schedule.Count == 0) return;
                await _room.RunScheduledTurnsAsync(context, sessionId,
                    schedule.Select(turn => new DjGroupRoom.ScheduledTurn(turn.Turn, turn.ScheduledAt)).ToArray(),
                    IsCurrentSession, TryCommitReply, cancellationToken);
            if (IsCurrentSession(sessionId))
                UpdateSnapshotForSession(sessionId, snapshot => snapshot with { Status = "Conversation complete" });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (IsCurrentSession(sessionId))
                UpdateSnapshotForSession(sessionId, snapshot => snapshot with { Status = "Canceled", Message = "Playback session was canceled." });
        }
        catch (Exception exception)
        {
            _logger.LogWarning("DJ playback session {SessionId} failed ({ErrorType})", sessionId, exception.GetType().Name);
            if (IsCurrentSession(sessionId))
                UpdateSnapshotForSession(sessionId, snapshot => snapshot with { Status = "Failed", Message = exception.Message });
        }
    }

    public static IReadOnlyList<DjGroupRoom.ScheduledTurn> BuildSchedule(DateTimeOffset playbackStarted, TimeSpan duration, DateTimeOffset now)
    {
        var positions = new[] { 0.10, 0.45, 0.75 };
        var schedule = new List<DjGroupRoom.ScheduledTurn>(3);
        for (var index = 0; index < positions.Length; index++)
        {
            var target = playbackStarted + TimeSpan.FromTicks((long)(duration.Ticks * positions[index]));
            if (target <= now || target >= playbackStarted + duration - TimeSpan.FromSeconds(5))
                continue;
            if (schedule.Count > 0 && target - schedule[^1].ScheduledAt < TimeSpan.FromSeconds(20))
                continue;
            schedule.Add(new(index + 1, target));
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
        var lines = _room.Snapshot();
        var diagnostics = _room.DiagnosticsSnapshot();
        var sessionId = _activeSessionId;
        if (sessionId is null) return;
        UpdateSnapshotForSession(sessionId.Value, snapshot => snapshot with
        {
            Lines = lines,
            Diagnostics = diagnostics,
            Turns = snapshot.Turns.Select((turn, index) =>
            {
                var line = lines.FirstOrDefault(item => item.DjId == DjProfiles.All[turn.Turn - 1].Id);
                return line is null ? turn : turn with { ActualAt = line.At, Status = "Complete" };
            }).ToArray(),
            Elapsed = snapshot.PlaybackStarted is { } start ? MaxZero(_timeProvider.GetUtcNow() - start) : TimeSpan.Zero
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
        _activeSessionCts?.Cancel();
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
