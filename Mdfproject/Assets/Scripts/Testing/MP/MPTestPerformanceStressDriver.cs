#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// MPTest-only authority load generator. It creates real networked ground monsters through the
/// production spawn path, observes that they move together, emits evidence, and owns their cleanup.
/// </summary>
public sealed class MPTestPerformanceStressDriver : MonoBehaviour
{
    private const int MinimumMonsterCount = 60;
    private const int MaximumMonsterCount = 160;

    private readonly List<SpawnedMonster> _spawned = new List<SpawnedMonster>();
    private CancellationTokenSource _cancellation;
    private MPTestCommandLine.Options _options;
    private NetworkRunner _runner;
    private string _runId = string.Empty;
    private string _phase = "idle";
    private string _reason = string.Empty;
    private string _evidencePath = string.Empty;
    private int _requestedCount;
    private int _targetPlayerId = -1;
    private int _spawnedCount;
    private int _maxAliveCount;
    private int _maxMovedCount;
    private int _cleanedCount;
    private float _holdSeconds;
    private float _startedRealtime;
    private bool _succeeded;

    private sealed class SpawnedMonster
    {
        public NetworkId Id;
        public Vector3 InitialPosition;
    }

    public bool IsRunning => _phase == "spawning" || _phase == "holding" || _phase == "cleanup";

    public void Configure(MPTestCommandLine.Options options)
    {
        _options = options;
    }

    public bool TryStart(
        int requestedCount,
        float holdSeconds,
        int attackerPlayerId,
        int targetPlayerId,
        string monsterDataKey,
        out string reason)
    {
        if (!_options.Enabled || !MPTestCommandLine.IsEnabled)
        {
            reason = "performance_stress_requires_mptest";
            return false;
        }

        if (IsRunning)
        {
            reason = "performance_stress_already_running";
            return false;
        }

        GameManagers game = GameManagers.Instance;
        _runner = game != null ? game.Runner : null;
        if (game == null || _runner == null || !_runner.IsRunning || !_runner.IsServer ||
            game.Object == null || !game.Object.HasStateAuthority)
        {
            reason = "performance_stress_requires_state_authority";
            return false;
        }

        if ((game.GetGameState() != GameManagers.GameState.Battle1 &&
             game.GetGameState() != GameManagers.GameState.Battle2) || game.IsSequenceTransitioning)
        {
            reason = "performance_stress_requires_stable_battle";
            return false;
        }

        PlayerManager attacker = ResolvePlayer(game, attackerPlayerId, requireSpawner: true);
        PlayerManager target = ResolvePlayer(game, targetPlayerId, requireSpawner: true);
        if (attacker == null || target == null || attacker.monsterSpawner == null || target.fieldManager == null ||
            attacker.Object == null || !attacker.Object.HasStateAuthority)
        {
            reason = "performance_stress_player_context_unavailable";
            return false;
        }

        MonsterData data = ResolveGroundMonsterData(game, attacker, monsterDataKey);
        if (data == null)
        {
            reason = "performance_stress_ground_monster_data_unavailable";
            return false;
        }

        StopAndCleanup("restart_cleanup");
        _runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        _phase = "spawning";
        _reason = string.Empty;
        _evidencePath = string.Empty;
        _requestedCount = Mathf.Clamp(requestedCount, MinimumMonsterCount, MaximumMonsterCount);
        _targetPlayerId = target.playerId;
        _holdSeconds = Mathf.Clamp(holdSeconds, 2f, 120f);
        _spawnedCount = 0;
        _maxAliveCount = 0;
        _maxMovedCount = 0;
        _cleanedCount = 0;
        _succeeded = false;
        _startedRealtime = Time.realtimeSinceStartup;
        _cancellation = new CancellationTokenSource();

        RunAsync(attacker, target, data, _cancellation.Token).Forget();
        reason = null;
        return true;
    }

    public object GetStatus()
    {
        SampleTrackedMonsters();
        return new
        {
            runId = _runId,
            phase = _phase,
            success = _succeeded,
            reason = _reason,
            requestedCount = _requestedCount,
            targetPlayerId = _targetPlayerId,
            spawnedCount = _spawnedCount,
            aliveCount = CountAliveTracked(),
            maxAliveCount = _maxAliveCount,
            maxMovedCount = _maxMovedCount,
            holdSeconds = _holdSeconds,
            elapsedSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - _startedRealtime),
            cleanedCount = _cleanedCount,
            evidencePath = _evidencePath
        };
    }

    public void StopAndCleanup(string reason)
    {
        CancellationTokenSource cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        CleanupTrackedMonsters();
        if (IsRunning)
        {
            _phase = "cancelled";
            _reason = reason ?? "cancelled";
            WriteEvidence();
        }
    }

    private async UniTaskVoid RunAsync(
        PlayerManager attacker,
        PlayerManager target,
        MonsterData data,
        CancellationToken cancellationToken)
    {
        try
        {
            int battleGeneration = attacker.monsterSpawner.CaptureBattleGeneration();
            if (battleGeneration < 0)
            {
                throw new InvalidOperationException("battle_generation_unavailable");
            }

            List<Vector3> positions = BuildSpawnPositions(target.fieldManager);
            int attempt = 0;
            int maxAttempts = _requestedCount * 3;
            while (_spawnedCount < _requestedCount && attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Vector3 position = attempt < positions.Count
                    ? positions[attempt]
                    : target.fieldManager.GetFallbackOuterSpawnWorldPosition();
                attempt++;
                Monster monster = await attacker.monsterSpawner.SpawnMonsterAtPositionAsync(
                    data,
                    position,
                    target.fieldManager,
                    expectedBattleGeneration: battleGeneration,
                    allowNearbyCellFallback: true);

                if (cancellationToken.IsCancellationRequested)
                {
                    // The production spawn API cannot accept this driver's token because asset
                    // loading and Fusion spawn are shared gameplay work. If cancellation arrives
                    // during that await, reclaim the just-created untracked object before
                    // propagating cancellation so Stop/OnDisable cannot leave a test ghost.
                    TryCleanupMonster(monster);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (monster == null || monster.Object == null || !monster.Object.IsValid)
                {
                    continue;
                }

                monster.ApplyAugmentBuffs(100f, 0.2f, 1f);
                monster.SetCurrentHP(monster.MaxHealth, monster.MaxHealth);
                _spawned.Add(new SpawnedMonster
                {
                    Id = monster.Object.Id,
                    InitialPosition = monster.transform.position
                });
                _spawnedCount++;
                SampleTrackedMonsters();
                if (attempt % 8 == 0)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }

            if (_maxAliveCount < _requestedCount)
            {
                throw new InvalidOperationException($"spawn_shortfall:{_maxAliveCount}/{_requestedCount}");
            }

            _phase = "holding";
            WriteEvidence();
            float holdUntil = Time.realtimeSinceStartup + _holdSeconds;
            while (Time.realtimeSinceStartup < holdUntil)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SampleTrackedMonsters();
                await UniTask.Delay(100, DelayType.Realtime, PlayerLoopTiming.Update, cancellationToken);
            }

            SampleTrackedMonsters();
            _succeeded = _maxAliveCount >= _requestedCount && _maxMovedCount >= _requestedCount;
            _reason = _succeeded
                ? "completed"
                : $"movement_shortfall:{_maxMovedCount}/{_requestedCount}";
            _phase = _succeeded ? "completed" : "failed";
        }
        catch (OperationCanceledException)
        {
            if (_phase != "cancelled")
            {
                _phase = "cancelled";
                _reason = "cancelled";
            }
        }
        catch (Exception exception)
        {
            _phase = "failed";
            _reason = exception.Message;
            MPTestLogger.Fail("performance_stress", "run_failed", exception.Message);
        }
        finally
        {
            _phase = _phase == "completed" || _phase == "failed" ? "cleanup" : _phase;
            CleanupTrackedMonsters();
            if (_phase == "cleanup")
            {
                _phase = _succeeded ? "completed" : "failed";
            }
            WriteEvidence();
            MPTestPerformanceRecorder.RecordCount("performance_stress_monsters_spawned", _spawnedCount);
            MPTestPerformanceRecorder.FlushNow("performance_stress_complete");
        }
    }

    private void SampleTrackedMonsters()
    {
        if (_runner == null || !_runner.IsRunning)
        {
            return;
        }

        int alive = 0;
        int moved = 0;
        foreach (SpawnedMonster tracked in _spawned)
        {
            if (!_runner.TryFindObject(tracked.Id, out NetworkObject networkObject) || networkObject == null ||
                !networkObject.TryGetComponent(out Monster monster) || monster.CurrentHealth <= 0f ||
                monster.Data == null || monster.Data.monsterType != MonsterType.Ground)
            {
                continue;
            }

            alive++;
            if ((monster.transform.position - tracked.InitialPosition).sqrMagnitude >= 0.0004f)
            {
                moved++;
            }
        }

        _maxAliveCount = Mathf.Max(_maxAliveCount, alive);
        _maxMovedCount = Mathf.Max(_maxMovedCount, moved);
    }

    private int CountAliveTracked()
    {
        if (_runner == null || !_runner.IsRunning)
        {
            return 0;
        }

        int alive = 0;
        foreach (SpawnedMonster tracked in _spawned)
        {
            if (_runner.TryFindObject(tracked.Id, out NetworkObject networkObject) && networkObject != null &&
                networkObject.TryGetComponent(out Monster monster) && monster.CurrentHealth > 0f)
            {
                alive++;
            }
        }
        return alive;
    }

    private void CleanupTrackedMonsters()
    {
        if (_runner != null && _runner.IsRunning && _runner.IsServer)
        {
            foreach (SpawnedMonster tracked in _spawned)
            {
                if (_runner.TryFindObject(tracked.Id, out NetworkObject networkObject) && networkObject != null &&
                    networkObject.IsValid && networkObject.HasStateAuthority)
                {
                    try
                    {
                        _runner.Despawn(networkObject);
                        _cleanedCount++;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning(
                            $"[MPTEST][PERF] Failed to clean stress monster {tracked.Id}: {exception.Message}");
                    }
                }
            }
        }
        _spawned.Clear();
    }

    private void TryCleanupMonster(Monster monster)
    {
        NetworkObject networkObject = monster != null ? monster.Object : null;
        if (_runner == null || !_runner.IsRunning || !_runner.IsServer ||
            networkObject == null || !networkObject.IsValid || !networkObject.HasStateAuthority)
        {
            return;
        }

        try
        {
            _runner.Despawn(networkObject);
            _cleanedCount++;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[MPTEST][PERF] Failed to clean cancelled stress monster {networkObject.Id}: {exception.Message}");
        }
    }

    private void WriteEvidence()
    {
        try
        {
            string root = string.IsNullOrWhiteSpace(_options.ArtifactDir)
                ? Path.Combine(Application.persistentDataPath, "mp-test")
                : _options.ArtifactDir;
            Directory.CreateDirectory(root);
            _evidencePath = Path.Combine(root, $"performance-stress-{_options.SafeRole}-{_runId}.json");
            File.WriteAllText(_evidencePath, JsonConvert.SerializeObject(GetStatus(), Formatting.Indented));
            MPTestLogger.Log("performance_stress", _succeeded ? "pass" : "info", _phase, _reason,
                new Dictionary<string, object>
                {
                    { "requested", _requestedCount },
                    { "spawned", _spawnedCount },
                    { "maxAlive", _maxAliveCount },
                    { "maxMoved", _maxMovedCount },
                    { "evidence", _evidencePath }
                });
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MPTEST][PERF] Stress evidence write failed: {exception.Message}");
        }
    }

    private static PlayerManager ResolvePlayer(GameManagers game, int requestedId, bool requireSpawner)
    {
        if (requestedId >= 0)
        {
            PlayerManager requested = game.GetPlayer(requestedId);
            if (requested != null && (!requireSpawner || requested.monsterSpawner != null))
            {
                return requested;
            }
        }

        return game.AllPlayers.FirstOrDefault(player =>
            player != null && (!requireSpawner || player.monsterSpawner != null));
    }

    private static MonsterData ResolveGroundMonsterData(GameManagers game, PlayerManager attacker, string requestedKey)
    {
        var candidates = new List<MonsterData>();
        if (AddressablesManager.Instance != null &&
            AddressablesManager.Instance.WaveDatabase != null &&
            AddressablesManager.Instance.WaveDatabase.attackSequenceMonsterCatalog != null)
        {
            candidates.AddRange(AddressablesManager.Instance.WaveDatabase.attackSequenceMonsterCatalog);
        }
        if (attacker.AttackMonsterPool != null)
        {
            candidates.AddRange(attacker.AttackMonsterPool.Where(entry => entry != null).Select(entry => entry.MonsterData));
        }
        foreach (PlayerManager player in game.AllPlayers)
        {
            if (player?.AttackMonsterPool != null)
            {
                candidates.AddRange(player.AttackMonsterPool.Where(entry => entry != null).Select(entry => entry.MonsterData));
            }
        }

        IEnumerable<MonsterData> valid = candidates.Where(data => data != null && !data.IsBoss &&
            data.monsterType == MonsterType.Ground && !string.IsNullOrWhiteSpace(data.monsterPrefab));
        if (!string.IsNullOrWhiteSpace(requestedKey))
        {
            MonsterData exact = valid.FirstOrDefault(data =>
                string.Equals(data.name, requestedKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(data.monsterName, requestedKey, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
        }
        return valid.OrderBy(data => data.blackMagicCost).ThenBy(data => data.name).FirstOrDefault();
    }

    private static List<Vector3> BuildSpawnPositions(FieldManager field)
    {
        var positions = new List<Vector3>();
        foreach (KeyValuePair<FieldManager.BorderDirection, List<Vector3>> pair in field.GetOuterSpawnWorldPositionsByDirection())
        {
            positions.AddRange(pair.Value);
        }
        if (positions.Count == 0)
        {
            positions.Add(field.GetFallbackOuterSpawnWorldPosition());
        }
        return positions.OrderBy(position => position.x).ThenBy(position => position.z).ToList();
    }

    private void OnDisable()
    {
        StopAndCleanup("driver_disabled");
    }
}
#endif
