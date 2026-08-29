using System.Net;
using System.Text.Json;
using Server;
using Server.AviscribeApi;
using Xunit;

namespace Server.Tests;

public sealed class AviscribeSessionManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aviscribe-server-tests-{Guid.NewGuid():N}");
    private readonly string _statePath;
    private const string CatalogHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public AviscribeSessionManagerTests()
    {
        Directory.CreateDirectory(_directory);
        _statePath = Path.Combine(_directory, "runs.json");
        Settings.Aviscribe.Enabled = true;
        Settings.Server.MaxPlayers = 8;
        Settings.Aviscribe.MaximumEventsPerRun = 20_000;
        Settings.Aviscribe.RetainedChangeCount = 512;
        Settings.Aviscribe.RetainedEventFeedCount = 200;
        Settings.Aviscribe.WaitTimeoutSeconds = 1;
        Settings.Aviscribe.OwnerTimeoutSeconds = 45;
        Settings.Aviscribe.IdleExpirationMinutes = 30;
        Settings.Aviscribe.MaximumRunHours = null;
        Settings.Aviscribe.StateFilename = _statePath;
    }

    [Fact]
    public async Task CreateJoinPublishResumeAndPersistenceKeepRoomCodeAndHashParticipantSecrets()
    {
        var manager = new AviscribeSessionManager();
        await manager.EnsureLoadedAsync(CancellationToken.None);
        var owner = await manager.CreateRunAsync(Create("Owner"), IPAddress.Loopback, CancellationToken.None);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", owner.JoinCode!);

        var participant = await manager.JoinRunAsync(new JoinRunRequest
        {
            DisplayName = "Runner",
            JoinCode = owner.JoinCode!.Replace("-", string.Empty).ToLowerInvariant(),
            CatalogHash = CatalogHash
        }, IPAddress.Parse("127.0.0.2"), CancellationToken.None);
        Assert.Equal(2, participant.Snapshot.Participants.Count);

        var firstId = Guid.NewGuid();
        var publish = await manager.PublishAsync(Auth(participant), new PublishEventsRequest
        {
            Generation = 1,
            Events =
            [
                new RunEvent { EventId = firstId, Kind = RunEventKind.CollectionObserved, KingdomId = 2, MoonId = 7 },
                new RunEvent { EventId = Guid.NewGuid(), Kind = RunEventKind.HintObserved, KingdomId = 2, MoonId = 7 }
            ]
        }, CancellationToken.None);
        Assert.Equal(2, publish.Events.Count);
        Assert.All(publish.Events, receipt => Assert.True(receipt.Revision > 0));

        var duplicate = await manager.PublishAsync(Auth(participant), new PublishEventsRequest
        {
            Generation = 1,
            Events = [new RunEvent { EventId = firstId, Kind = RunEventKind.CollectionObserved, KingdomId = 2, MoonId = 7 }]
        }, CancellationToken.None);
        Assert.True(duplicate.Events.Single().WasDuplicate);
        Assert.Equal(publish.Events[0].Revision, duplicate.Events.Single().Revision);

        var resumed = await manager.ResumeRunAsync(Auth(participant), CancellationToken.None);
        var fact = Assert.Single(resumed.Snapshot.MoonFacts);
        Assert.True(fact.Hinted);
        Assert.True(fact.Collected);

        var persisted = await File.ReadAllTextAsync(_statePath);
        Assert.Contains(owner.JoinCode!, persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(owner.ParticipantToken, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(participant.ParticipantToken, persisted, StringComparison.Ordinal);

        var restored = new AviscribeSessionManager();
        await restored.EnsureLoadedAsync(CancellationToken.None);
        var afterRestart = await restored.ResumeRunAsync(Auth(participant), CancellationToken.None);
        Assert.Equal(owner.JoinCode, afterRestart.JoinCode);
        Assert.Single(afterRestart.Snapshot.MoonFacts);
        Assert.False(afterRestart.Snapshot.Participants.Single(item => item.ParticipantId == owner.ParticipantId).IsOnline);
        Assert.True(afterRestart.Snapshot.Participants.Single(item => item.ParticipantId == participant.ParticipantId).IsOnline);
        Assert.Contains($"room={owner.JoinCode}", Assert.Single(restored.GetOperatorSummary()));
    }

    [Fact]
    public async Task ResumeRestoresRoomCodePersistedByOlderServerBuilds()
    {
        var manager = new AviscribeSessionManager();
        await manager.EnsureLoadedAsync(CancellationToken.None);
        var owner = await manager.CreateRunAsync(
            Create("Owner"),
            IPAddress.Loopback,
            CancellationToken.None);

        var json = await File.ReadAllTextAsync(_statePath);
        var store = JsonSerializer.Deserialize<AviscribeSessionManager.PersistedStore>(
            json,
            AviscribeProtocol.JsonOptions)!;
        Assert.Single(store.Sessions).JoinCode = null;
        await File.WriteAllTextAsync(
            _statePath,
            JsonSerializer.Serialize(store, AviscribeProtocol.JsonOptions));

        var restored = new AviscribeSessionManager();
        await restored.EnsureLoadedAsync(CancellationToken.None);
        Assert.Contains("room=unavailable", Assert.Single(restored.GetOperatorSummary()));
        var request = Auth(owner);
        request.Data = JsonSerializer.SerializeToElement(
            new ResumeRunRequest { JoinCode = owner.JoinCode },
            AviscribeProtocol.JsonOptions);

        var resumed = await restored.ResumeRunAsync(request, CancellationToken.None);

        Assert.Equal(owner.JoinCode, resumed.JoinCode);
        Assert.Contains($"room={owner.JoinCode}", Assert.Single(restored.GetOperatorSummary()));
    }

    [Fact]
    public async Task ManualTransitionsResetAndOwnerTransferFollowRunRules()
    {
        var manager = new AviscribeSessionManager();
        await manager.EnsureLoadedAsync(CancellationToken.None);
        var owner = await manager.CreateRunAsync(Create("Owner"), IPAddress.Loopback, CancellationToken.None);
        var runner = await manager.JoinRunAsync(new JoinRunRequest
        {
            DisplayName = "Runner",
            JoinCode = owner.JoinCode!,
            CatalogHash = CatalogHash
        }, IPAddress.Parse("127.0.0.2"), CancellationToken.None);

        async Task<RunSnapshot> Apply(RunEventKind kind)
        {
            await manager.PublishAsync(Auth(runner), new PublishEventsRequest
            {
                Generation = 1,
                Events = [new RunEvent { EventId = Guid.NewGuid(), Kind = kind, KingdomId = 1, MoonId = 10 }]
            }, CancellationToken.None);
            return (await manager.ResumeRunAsync(Auth(runner), CancellationToken.None)).Snapshot;
        }

        var pending = Assert.Single((await Apply(RunEventKind.SetPending)).MoonFacts);
        Assert.True(pending.Hinted);
        Assert.False(pending.Collected);
        var counted = Assert.Single((await Apply(RunEventKind.SetCounted)).MoonFacts);
        Assert.Equal(ManualClassification.Counted, counted.ManualClassification);
        var wrong = Assert.Single((await Apply(RunEventKind.SetUncounted)).MoonFacts);
        Assert.Equal(ManualClassification.Uncounted, wrong.ManualClassification);
        Assert.Empty((await Apply(RunEventKind.RemoveMoon)).MoonFacts);

        await manager.LeaveRunAsync(Auth(owner), CancellationToken.None);
        var transferred = await manager.ResumeRunAsync(Auth(runner), CancellationToken.None);
        Assert.True(transferred.IsOwner);

        var reset = await manager.ResetRunAsync(Auth(runner), new ResetRunRequest
        {
            Configuration = new RunConfiguration { Category = "hardcore", IncludePostGame = true }
        }, CancellationToken.None);
        Assert.Equal(2, reset.Generation);
        Assert.Empty(reset.MoonFacts);
        var mismatch = await Assert.ThrowsAsync<AviscribeApiException>(() => manager.PublishAsync(
            Auth(runner),
            new PublishEventsRequest
            {
                Generation = 1,
                Events = [new RunEvent { EventId = Guid.NewGuid(), Kind = RunEventKind.HintObserved, KingdomId = 1, MoonId = 1 }]
            }, CancellationToken.None));
        Assert.Equal("generationMismatch", mismatch.Code);
    }

    [Fact]
    public async Task CatalogCapacityAndFailedJoinLimitsReturnStructuredCodes()
    {
        Settings.Server.MaxPlayers = 1;
        var manager = new AviscribeSessionManager();
        await manager.EnsureLoadedAsync(CancellationToken.None);
        var owner = await manager.CreateRunAsync(Create("Owner"), IPAddress.Loopback, CancellationToken.None);
        var secondRun = await Assert.ThrowsAsync<AviscribeApiException>(() => manager.CreateRunAsync(
            Create("Other owner"),
            IPAddress.Parse("127.0.0.4"),
            CancellationToken.None));
        Assert.Equal("capacityReached", secondRun.Code);
        var capacity = await Assert.ThrowsAsync<AviscribeApiException>(() => manager.JoinRunAsync(new JoinRunRequest
        {
            DisplayName = "Runner",
            JoinCode = owner.JoinCode!,
            CatalogHash = CatalogHash
        }, IPAddress.Parse("127.0.0.2"), CancellationToken.None));
        Assert.Equal("capacityReached", capacity.Code);

        Settings.Server.MaxPlayers = 8;
        var joinedAfterLimitChange = await manager.JoinRunAsync(new JoinRunRequest
        {
            DisplayName = "Runner",
            JoinCode = owner.JoinCode!,
            CatalogHash = CatalogHash
        }, IPAddress.Parse("127.0.0.2"), CancellationToken.None);
        Assert.Equal(2, joinedAfterLimitChange.Snapshot.Participants.Count);
        for (var index = 0; index < 5; index++)
        {
            var invalid = await Assert.ThrowsAsync<AviscribeApiException>(() => manager.JoinRunAsync(new JoinRunRequest
            {
                DisplayName = "Runner",
                JoinCode = "0000-0000",
                CatalogHash = CatalogHash
            }, IPAddress.Parse("127.0.0.3"), CancellationToken.None));
            Assert.Equal("invalidJoinCode", invalid.Code);
        }
        var limited = await Assert.ThrowsAsync<AviscribeApiException>(() => manager.JoinRunAsync(new JoinRunRequest
        {
            DisplayName = "Runner",
            JoinCode = "0000-0000",
            CatalogHash = CatalogHash
        }, IPAddress.Parse("127.0.0.3"), CancellationToken.None));
        Assert.Equal("rateLimited", limited.Code);
    }

    [Fact]
    public async Task EndingOrEmptyingRoomImmediatelyAllowsAnotherRoom()
    {
        var manager = new AviscribeSessionManager();
        await manager.EnsureLoadedAsync(CancellationToken.None);

        var ended = await manager.CreateRunAsync(Create("First owner"), IPAddress.Loopback, CancellationToken.None);
        await manager.EndRunAsync(Auth(ended), CancellationToken.None);
        var afterEnd = await manager.CreateRunAsync(
            Create("Second owner"),
            IPAddress.Parse("127.0.0.2"),
            CancellationToken.None);

        await manager.LeaveRunAsync(Auth(afterEnd), CancellationToken.None);
        var afterEmpty = await manager.CreateRunAsync(
            Create("Third owner"),
            IPAddress.Parse("127.0.0.3"),
            CancellationToken.None);

        Assert.NotEqual(ended.SessionId, afterEnd.SessionId);
        Assert.NotEqual(afterEnd.SessionId, afterEmpty.SessionId);
        Assert.Contains(afterEmpty.JoinCode!, Assert.Single(manager.GetOperatorSummary()));
    }

    [Fact]
    public async Task IdleExpirationUsesLastRoomEventAndReleasesCapacity()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        var manager = new AviscribeSessionManager(clock);
        await manager.EnsureLoadedAsync(CancellationToken.None);
        var owner = await manager.CreateRunAsync(Create("Idle owner"), IPAddress.Loopback, CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(31));
        await manager.WaitForChangesAsync(Auth(owner), new WaitForChangesRequest
        {
            Generation = owner.Generation,
            AfterRevision = 0
        }, CancellationToken.None);
        await manager.SweepAsync(CancellationToken.None);

        Assert.Equal("No active Aviscribe multiplayer rooms.", Assert.Single(manager.GetOperatorSummary()));
        var replacement = await manager.CreateRunAsync(
            Create("Replacement owner"),
            IPAddress.Parse("127.0.0.2"),
            CancellationToken.None);
        Assert.NotNull(replacement.JoinCode);
    }

    [Fact]
    public async Task OperatorCommandsExposeRoomDetailsAndSharedRunState()
    {
        var manager = new AviscribeSessionManager();
        await manager.EnsureLoadedAsync(CancellationToken.None);
        var owner = await manager.CreateRunAsync(Create("Owner"), IPAddress.Loopback, CancellationToken.None);
        await manager.PublishAsync(Auth(owner), new PublishEventsRequest
        {
            Generation = owner.Generation,
            Events =
            [
                new RunEvent
                {
                    EventId = Guid.NewGuid(),
                    Kind = RunEventKind.SetCounted,
                    KingdomId = 3,
                    MoonId = 12
                }
            ]
        }, CancellationToken.None);

        var summary = Assert.Single(manager.GetOperatorSummary());
        Assert.Contains($"room={owner.JoinCode}", summary);
        Assert.Contains($"session={owner.SessionId}", summary);
        Assert.Contains("category=standard", summary);
        Assert.Contains("players=1/1", summary);

        var details = manager.GetOperatorDetails();
        Assert.Contains(details, line => line.Contains($"Room {owner.JoinCode}", StringComparison.Ordinal));
        var state = manager.GetOperatorGameState();
        Assert.Contains(state, line => line.Contains("kingdom=3 moon=12", StringComparison.Ordinal));
        Assert.Contains(state, line => line.Contains("classification=Counted", StringComparison.Ordinal));
        Assert.True(await manager.EndByOperatorAsync(CancellationToken.None));
        Assert.Equal("No active Aviscribe multiplayer room.", Assert.Single(manager.GetOperatorDetails()));
        Assert.Equal("No active Aviscribe multiplayer room.", Assert.Single(manager.GetOperatorGameState()));
        Assert.False(await manager.EndByOperatorAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static CreateRunRequest Create(string name) => new()
    {
        DisplayName = name,
        CatalogHash = CatalogHash,
        Configuration = new RunConfiguration { Category = "standard" }
    };

    private static AviscribeRequest Auth(SessionConnectionResult result) => new()
    {
        Version = 1,
        RequestId = Guid.NewGuid(),
        SessionId = result.SessionId,
        ParticipantId = result.ParticipantId,
        ParticipantToken = result.ParticipantToken
    };

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
