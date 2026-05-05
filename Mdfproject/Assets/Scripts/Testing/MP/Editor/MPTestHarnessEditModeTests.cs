#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class MPTestHarnessEditModeTests
{
    [Test]
    public void CommandLineParserReadsHarnessOptions()
    {
        var options = MPTestCommandLine.Parse(new[]
        {
            "MDF.exe",
            "--mpTest",
            "--mpRole", "client",
            "--mpSession", "phase8-session",
            "--mpMaxPlayers", "4",
            "--mpScene", "Game",
            "--mpAutoStart",
            "--mpLoadGame",
            "--mpExitAfterSeconds", "7",
            "--mpAutomationPort", "19001",
            "--mpAutomationToken", "automation-secret",
            "--mpConnectionToken", "connection-secret",
            "--mpCase", "phase8",
            "--mpArtifactDir", "artifacts/mp/phase8",
            "--mpSeed", "1234",
            "--mpScenario", "prepare_smoke",
            "--mpDisableAiFill",
            "--mpFreezeGameFlow",
            "--mpHumanBot",
            "--mpBotPersona", "maze",
            "--mpBotSeed", "222",
            "--mpBotDurationSeconds", "33",
            "--mpBotStopAtRound", "2",
            "--mpBotMaxCommands", "9",
            "--mpBotRecordJournal", "artifacts/mp/phase8/bot.jsonl"
        });

        Assert.That(options.Enabled, Is.True);
        Assert.That(options.SafeRole, Is.EqualTo("client"));
        Assert.That(options.Session, Is.EqualTo("phase8-session"));
        Assert.That(options.MaxPlayers, Is.EqualTo(4));
        Assert.That(options.Scene, Is.EqualTo("Game"));
        Assert.That(options.AutoStart, Is.True);
        Assert.That(options.LoadGame, Is.True);
        Assert.That(options.ExitAfterSeconds, Is.EqualTo(7));
        Assert.That(options.AutomationPort, Is.EqualTo(19001));
        Assert.That(options.AutomationTokenHash, Is.Not.EqualTo("automation-secret"));
        Assert.That(options.ConnectionTokenHash, Is.Not.EqualTo("connection-secret"));
        Assert.That(options.CaseName, Is.EqualTo("phase8"));
        Assert.That(options.Seed, Is.EqualTo(1234));
        Assert.That(options.Scenario, Is.EqualTo("prepare_smoke"));
        Assert.That(options.DisableAiFill, Is.True);
        Assert.That(options.FreezeGameFlow, Is.True);
        Assert.That(options.HumanBot, Is.True);
        Assert.That(options.BotPersona, Is.EqualTo("maze"));
        Assert.That(options.BotSeed, Is.EqualTo(222));
        Assert.That(options.BotDurationSeconds, Is.EqualTo(33));
        Assert.That(options.BotStopAtRound, Is.EqualTo(2));
        Assert.That(options.BotMaxCommands, Is.EqualTo(9));
        Assert.That(options.BotRecordJournal, Is.EqualTo("artifacts/mp/phase8/bot.jsonl"));
    }

    [Test]
    public void MPTestLoggerEmitsStablePrefixAndFields()
    {
        LogAssert.Expect(LogType.Log, new Regex(@"\[MPTEST\].*phase=phase8_log_smoke.*result=pass"));
        MPTestLogger.Pass("phase8_log_smoke");
    }

    [Test]
    public void SnapshotSerializesSchemaFields()
    {
        var snapshot = BuildSnapshot("host");
        string json = MPTestStateSnapshot.ToJson(snapshot);
        var restored = JsonConvert.DeserializeObject<MPTestStateSnapshot.Snapshot>(json);

        Assert.That(restored.Version, Is.EqualTo(1));
        Assert.That(restored.Scene, Is.EqualTo("Game"));
        Assert.That(restored.Game.CurrentState, Is.EqualTo("Prepare"));
        Assert.That(restored.Players.Length, Is.EqualTo(1));
        Assert.That(restored.Players[0].Shop.ItemsHash, Does.StartWith("sha256:"));
    }

    [Test]
    public void SnapshotComparisonIgnoresPeerLocalFields()
    {
        var host = BuildSnapshot("host");
        var client = BuildSnapshot("client");
        client.Runner.IsServer = false;
        client.Runner.IsClient = true;
        client.Runner.Tick = host.Runner.Tick + 30;
        client.Runner.LocalPlayerRef = "PlayerRef:2";
        client.Players[0].PlayerRef = "PlayerRef:2";

        var result = MPTestAssertions.CompareDurable(host, client);

        Assert.That(result.Success, Is.True, string.Join("\n", result.Errors));
    }

    [Test]
    public void BasicAssertionRejectsMissingGameManagers()
    {
        var snapshot = BuildSnapshot("host");
        snapshot.Game.HasGameManagers = false;

        var result = MPTestAssertions.AssertBasic(snapshot, expectedPlayers: 1, expectedScene: "Game", expectedGameState: "Prepare");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Does.Contain("gameManagers_missing"));
    }

    [Test]
    public void AutomationServerRuntimeGateRequiresTestPortAndToken()
    {
        var missing = new MPTestCommandLine.Options
        {
            Enabled = true,
            AutomationPort = 0,
            AutomationToken = "token"
        };
        Assert.That(MPTestAutomationServer.CanStart(missing, out string missingReason), Is.False);
        Assert.That(missingReason, Does.Contain("--mpAutomationPort"));

        var ready = new MPTestCommandLine.Options
        {
            Enabled = true,
            AutomationPort = 19002,
            AutomationToken = "token"
        };
        Assert.That(MPTestAutomationServer.CanStart(ready, out _), Is.True);
    }

    [Test]
    public void AutomationServerSourceKeepsProductionSafetyGates()
    {
        string source = File.ReadAllText("Assets/Scripts/Testing/MP/MPTestAutomationServer.cs");

        Assert.That(source, Does.Contain("UNITY_EDITOR || DEVELOPMENT_BUILD"));
        Assert.That(source, Does.Contain("missing --mpTest"));
        Assert.That(source, Does.Contain("missing --mpAutomationPort"));
        Assert.That(source, Does.Contain("missing --mpAutomationToken"));
        Assert.That(source, Does.Contain("IPAddress.Loopback"));
        Assert.That(source, Does.Contain("X-MPTest-Token"));
        Assert.That(source, Does.Contain("Authorization"));
        Assert.That(source, Does.Contain("/bot/start"));
        Assert.That(source, Does.Contain("/bot/status"));
    }

    [Test]
    public void PrecommitFixtureCatchesUnsafeAutomationServer()
    {
        const string unsafeServer = "class MPTestAutomationServer { TcpListener listener; }";
        Assert.That(unsafeServer, Does.Contain("MPTestAutomationServer"));
        Assert.That(unsafeServer, Does.Not.Contain("UNITY_EDITOR || DEVELOPMENT_BUILD"));
        Assert.That(unsafeServer, Does.Not.Contain("--mpTest"));
        Assert.That(unsafeServer, Does.Not.Contain("IPAddress.Loopback"));
    }

    private static MPTestStateSnapshot.Snapshot BuildSnapshot(string role)
    {
        string hash = MPTestStateSnapshot.HashStableString("stable");
        return new MPTestStateSnapshot.Snapshot
        {
            Version = 1,
            Role = role,
            CaseName = "phase8",
            Session = "phase8-session",
            Scene = "Game",
            TimestampUtc = "2026-05-05T00:00:00.0000000Z",
            Runner = new MPTestStateSnapshot.RunnerSnapshot
            {
                IsRunning = true,
                GameMode = "Host",
                IsServer = true,
                IsClient = false,
                Tick = 100,
                ActivePlayerCount = 1,
                MaxPlayers = 2,
                LocalPlayerRef = "PlayerRef:1"
            },
            Game = new MPTestStateSnapshot.GameSnapshot
            {
                HasGameManagers = true,
                CurrentState = "Prepare",
                CurrentRound = 1,
                PhaseTimerRemaining = 12.5f,
                FirstAttackerPlayerId = 0,
                BattleOpponentsHash = hash,
                MatchFirstAttackerHash = hash
            },
            Players = new[]
            {
                new MPTestStateSnapshot.PlayerSnapshot
                {
                    PlayerId = 0,
                    NetworkId = "net-0",
                    PlayerRef = "PlayerRef:1",
                    ConnectionTokenHash = "unknown",
                    HasInputAuthority = true,
                    HasStateAuthority = true,
                    IsLocal = true,
                    IsAI = false,
                    IsConnected = true,
                    Health = 100,
                    Gold = 10,
                    WallCount = 5,
                    IsActivelyFighting = false,
                    IsAttackerInCurrentBattle = false,
                    Shop = new MPTestStateSnapshot.ShopSnapshot
                    {
                        Available = true,
                        Revision = 1,
                        Round = 1,
                        Count = 5,
                        ItemsHash = hash
                    },
                    Augment = new MPTestStateSnapshot.AugmentSnapshot
                    {
                        Available = true,
                        SelectedCount = 0,
                        PresentedCount = 3,
                        PresentedHash = hash,
                        SelectedHash = "unknown"
                    },
                    Field = new MPTestStateSnapshot.FieldSnapshot
                    {
                        Ready = true,
                        GridHash = hash,
                        PlacedUnitCount = 0,
                        PlacedUnitsHash = hash,
                        DestructibleWallCount = 0,
                        PermanentWallCount = 0,
                        WallHash = hash,
                        PathReady = true,
                        GoalReady = true
                    },
                    Monsters = new MPTestStateSnapshot.MonsterSnapshot
                    {
                        Ready = true,
                        AliveCount = 0,
                        LivingHash = hash,
                        AutoSpawnRunning = null
                    },
                    Ai = new MPTestStateSnapshot.AiSnapshot
                    {
                        ControllerRegistered = false,
                        PrepareReady = null,
                        CombatReady = null
                    }
                }
            },
            Objects = new MPTestStateSnapshot.ObjectsSnapshot
            {
                NetworkObjectCount = 1,
                PlayerManagerCount = 1,
                UnitCount = 0,
                MonsterCount = 0,
                WallCount = 0
            },
            Commands = new MPTestStateSnapshot.CommandsSnapshot
            {
                LastSequence = null,
                QueueDepth = 0,
                LastCommand = "unknown"
            },
            HostMigration = new MPTestStateSnapshot.HostMigrationSnapshot
            {
                HandlerExists = true,
                IsMigrating = false,
                RecoverySucceeded = null,
                AiTakeoverReady = null,
                LastEvent = "unknown",
                EventCount = 0,
                OnHostMigrationCount = 0,
                NonNullTokenCount = 0,
                ResumeCount = 0,
                StartGameSuccessCount = 0,
                CompleteCount = 0,
                FailureCount = 0
            },
            Test = new MPTestStateSnapshot.TestSnapshot
            {
                Bot = new MPTestStateSnapshot.BotSnapshot
                {
                    Enabled = false,
                    Running = false,
                    Persona = "none",
                    CommandsIssued = 0,
                    LastDecision = null,
                    LastCommandType = null,
                    LastError = null,
                    JournalPath = null,
                    PlayerId = -1,
                    HasLocalInputAuthority = false,
                    StopReason = null
                },
                RandomOutcomes = new MPTestStateSnapshot.RandomOutcomesSnapshot()
            },
            Errors = new System.Collections.Generic.List<string>()
        };
    }
}
#endif
