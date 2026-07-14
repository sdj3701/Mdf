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
/// MPTest-only local presentation load. Each peer schedules real pooled projectile visuals with
/// one shared hit tick, then proves the burst reached its target and returned to its baseline.
/// </summary>
public sealed class MPTestProjectileExpiryStressDriver : MonoBehaviour
{
    private const int MaximumProjectileCount = 256;

    private CancellationTokenSource _cancellation;
    private MPTestCommandLine.Options _options;
    private string _runId = string.Empty;
    private string _phase = "idle";
    private string _reason = string.Empty;
    private string _evidencePath = string.Empty;
    private int _requestedCount;
    private int _baselineActive;
    private int _maxActive;
    private int _hitTick;
    private float _lifetimeSeconds;
    private float _startedRealtime;
    private bool _succeeded;

    public bool IsRunning => _phase == "loading" || _phase == "holding" || _phase == "expiring";

    public void Configure(MPTestCommandLine.Options options)
    {
        _options = options;
    }

    public bool TryStart(int requestedCount, float lifetimeSeconds, out string reason)
    {
        if (!_options.Enabled || !MPTestCommandLine.IsEnabled)
        {
            reason = "projectile_expiry_stress_requires_mptest";
            return false;
        }

        if (IsRunning)
        {
            reason = "projectile_expiry_stress_already_running";
            return false;
        }

        GameManagers game = GameManagers.Instance;
        NetworkRunner runner = game != null ? game.Runner : null;
        if (runner == null || !runner.IsRunning)
        {
            reason = "projectile_expiry_stress_runner_unavailable";
            return false;
        }

        if (!TryResolveProjectileSource(runner, out NetworkObject attacker, out ProjectileVfxConfig config))
        {
            reason = "projectile_expiry_stress_ranged_source_unavailable";
            return false;
        }

        Stop("restart_cleanup");
        _runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        _phase = "loading";
        _reason = string.Empty;
        _evidencePath = string.Empty;
        _requestedCount = Mathf.Clamp(requestedCount, 1, MaximumProjectileCount);
        _lifetimeSeconds = Mathf.Clamp(lifetimeSeconds, 2f, 30f);
        _baselineActive = ProjectileVfxManager.ActiveProjectileCountForDiagnostics;
        _maxActive = _baselineActive;
        _hitTick = 0;
        _succeeded = false;
        _startedRealtime = Time.realtimeSinceStartup;
        _cancellation = new CancellationTokenSource();

        Vector3 firePosition = attacker.transform.position + Vector3.up * 0.75f;
        Vector3 targetPosition = firePosition + new Vector3(0f, 0.25f, 6f);
        if (!ProjectileVfxManager.MPTestScheduleExpiryBurst(
                runner,
                attacker,
                config,
                firePosition,
                targetPosition,
                _requestedCount,
                _lifetimeSeconds,
                out _hitTick,
                out reason))
        {
            _phase = "failed";
            _reason = reason;
            WriteEvidence();
            return false;
        }

        RunAsync(runner, _cancellation.Token).Forget();
        reason = null;
        return true;
    }

    public object GetStatus()
    {
        SampleActive();
        return new
        {
            runId = _runId,
            phase = _phase,
            success = _succeeded,
            reason = _reason,
            requestedCount = _requestedCount,
            baselineActive = _baselineActive,
            maxActive = _maxActive,
            peakAdded = Mathf.Max(0, _maxActive - _baselineActive),
            currentActive = ProjectileVfxManager.ActiveProjectileCountForDiagnostics,
            hitTick = _hitTick,
            lifetimeSeconds = _lifetimeSeconds,
            elapsedSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - _startedRealtime),
            evidencePath = _evidencePath
        };
    }

    public void Stop(string reason)
    {
        CancellationTokenSource cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (IsRunning)
        {
            _phase = "cancelled";
            _reason = reason ?? "cancelled";
            WriteEvidence();
        }
    }

    private async UniTaskVoid RunAsync(NetworkRunner runner, CancellationToken cancellationToken)
    {
        try
        {
            float loadDeadline = Time.realtimeSinceStartup + Mathf.Max(10f, _lifetimeSeconds - 0.5f);
            while (Time.realtimeSinceStartup < loadDeadline &&
                   _maxActive - _baselineActive < _requestedCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SampleActive();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            if (_maxActive - _baselineActive < _requestedCount)
            {
                throw new InvalidOperationException(
                    $"projectile_peak_shortfall:{_maxActive - _baselineActive}/{_requestedCount}");
            }

            _phase = "holding";
            WriteEvidence();
            while (runner != null && runner.IsRunning && runner.Tick < _hitTick)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SampleActive();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            _phase = "expiring";
            float cleanupDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < cleanupDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SampleActive();
                if (ProjectileVfxManager.ActiveProjectileCountForDiagnostics <= _baselineActive)
                {
                    _succeeded = true;
                    _reason = "completed";
                    _phase = "completed";
                    break;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            if (!_succeeded)
            {
                throw new InvalidOperationException(
                    $"projectile_cleanup_timeout:{ProjectileVfxManager.ActiveProjectileCountForDiagnostics}/{_baselineActive}");
            }
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
            MPTestLogger.Fail("projectile_expiry_stress", "run_failed", exception.Message);
        }
        finally
        {
            WriteEvidence();
            MPTestPerformanceRecorder.RecordCount(
                "projectile_expiry_stress_peak_added",
                Mathf.Max(0, _maxActive - _baselineActive));
            MPTestPerformanceRecorder.FlushNow("projectile_expiry_stress_complete");
        }
    }

    private void SampleActive()
    {
        _maxActive = Mathf.Max(_maxActive, ProjectileVfxManager.ActiveProjectileCountForDiagnostics);
    }

    private static bool TryResolveProjectileSource(
        NetworkRunner runner,
        out NetworkObject attacker,
        out ProjectileVfxConfig config)
    {
        attacker = null;
        config = null;
        foreach (Unit unit in UnityEngine.Object.FindObjectsOfType<Unit>())
        {
            if (unit == null || unit.Data == null || unit.Object == null || !unit.Object.IsValid || unit.Runner != runner)
            {
                continue;
            }

            ProjectileVfxConfig candidate = unit.Data.GetProjectileVfxConfig();
            if (candidate != null && candidate.HasProjectileKey)
            {
                attacker = unit.Object;
                config = candidate;
                return true;
            }
        }

        foreach (PlayerManager player in UnityEngine.Object.FindObjectsOfType<PlayerManager>())
        {
            if (player == null || player.Object == null || !player.Object.IsValid || player.Runner != runner)
            {
                continue;
            }

            UnitData baseData = player.SelectedKingBaseUnitData;
            ProjectileVfxConfig candidate = baseData != null ? baseData.GetProjectileVfxConfig() : null;
            if (candidate != null && candidate.HasProjectileKey)
            {
                attacker = player.Object;
                config = candidate;
                return true;
            }
        }

        NetworkObject fallbackAttacker = UnityEngine.Object.FindObjectsOfType<PlayerManager>()
            .Where(player => player != null &&
                             player.Object != null &&
                             player.Object.IsValid &&
                             player.Runner == runner)
            .Select(player => player.Object)
            .FirstOrDefault();
        if (fallbackAttacker != null && LoadManager.Instance != null && LoadManager.Instance.IsReady)
        {
            IEnumerable<UnitData> allUnits = LoadManager.Instance.GetAllUnitData();
            if (allUnits != null)
            {
                foreach (UnitData unitData in allUnits)
                {
                    ProjectileVfxConfig candidate = unitData != null ? unitData.GetProjectileVfxConfig() : null;
                    if (candidate != null && candidate.HasProjectileKey)
                    {
                        attacker = fallbackAttacker;
                        config = candidate;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void WriteEvidence()
    {
        try
        {
            string root = string.IsNullOrWhiteSpace(_options.ArtifactDir)
                ? Path.Combine(Application.persistentDataPath, "mp-test")
                : _options.ArtifactDir;
            Directory.CreateDirectory(root);
            _evidencePath = Path.Combine(root, $"projectile-expiry-stress-{_options.SafeRole}-{_runId}.json");
            File.WriteAllText(_evidencePath, JsonConvert.SerializeObject(GetStatus(), Formatting.Indented));
            MPTestLogger.Log(
                "projectile_expiry_stress",
                _succeeded ? "pass" : "info",
                _phase,
                _reason,
                new Dictionary<string, object>
                {
                    { "requested", _requestedCount },
                    { "peakAdded", Mathf.Max(0, _maxActive - _baselineActive) },
                    { "current", ProjectileVfxManager.ActiveProjectileCountForDiagnostics },
                    { "evidence", _evidencePath }
                });
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MPTEST][PERF] Projectile expiry evidence write failed: {exception.Message}");
        }
    }

    private void OnDisable()
    {
        Stop("driver_disabled");
    }
}
#endif
