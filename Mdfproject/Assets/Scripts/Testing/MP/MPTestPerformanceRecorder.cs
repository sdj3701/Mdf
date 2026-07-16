#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Fusion;
using Fusion.Statistics;
using Newtonsoft.Json;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Development/MPTest-only low-allocation performance telemetry.
/// It deliberately keeps production gameplay free of profiler collection code.
/// </summary>
public sealed class MPTestPerformanceRecorder : MonoBehaviour
{
    private const int FrameSampleCapacity = 60000;
    private const int NetworkSampleCapacity = 4096;
    private const float CheckpointIntervalSeconds = 30f;
    private const float NetworkSampleIntervalSeconds = 0.25f;
    private const float WorldSampleIntervalSeconds = 0.5f;

    private static MPTestPerformanceRecorder _instance;

    private readonly SampleBuffer _wallFrameMs = new SampleBuffer(FrameSampleCapacity);
    private readonly SampleBuffer _mainThreadMs = new SampleBuffer(FrameSampleCapacity);
    private readonly SampleBuffer _renderThreadMs = new SampleBuffer(FrameSampleCapacity);
    private readonly SampleBuffer _cpuFrameMs = new SampleBuffer(FrameSampleCapacity);
    private readonly SampleBuffer _gpuFrameMs = new SampleBuffer(FrameSampleCapacity);
    private readonly SampleBuffer _gcAllocatedBytes = new SampleBuffer(FrameSampleCapacity);
    private readonly SampleBuffer _fusionInBandwidth = new SampleBuffer(NetworkSampleCapacity);
    private readonly SampleBuffer _fusionOutBandwidth = new SampleBuffer(NetworkSampleCapacity);
    private readonly Dictionary<string, MetricAggregate> _customMetrics =
        new Dictionary<string, MetricAggregate>(StringComparer.Ordinal);
    private readonly FrameTiming[] _frameTimings = new FrameTiming[1];

    private MPTestCommandLine.Options _options;
    private ProfilerRecorder _mainThreadRecorder;
    private ProfilerRecorder _renderThreadRecorder;
    private ProfilerRecorder _gpuFrameRecorder;
    private ProfilerRecorder _gcAllocatedRecorder;
    private float _captureStartRealtime;
    private float _nextCheckpointRealtime;
    private float _nextNetworkSampleRealtime;
    private float _nextWorldSampleRealtime;
    private bool _isWriting;
    private bool _finalWritten;
    private int _skipSampleFrames;
    private int _maxAliveMonsters;
    private int _maxActiveProjectiles;
    private int _maxNetworkObjects;

    public static bool IsActive => _instance != null && _instance.isActiveAndEnabled;

    public static MPTestPerformanceRecorder Ensure(GameObject owner, MPTestCommandLine.Options options)
    {
        if (!options.Enabled || !options.PerformanceCapture || owner == null)
        {
            return null;
        }

        if (_instance != null)
        {
            return _instance;
        }

        var recorder = owner.GetComponent<MPTestPerformanceRecorder>() ?? owner.AddComponent<MPTestPerformanceRecorder>();
        recorder.Configure(options);
        return recorder;
    }

    public static long StartTimestamp()
    {
        return IsActive ? Stopwatch.GetTimestamp() : 0L;
    }

    public static void RecordDuration(string metricName, long startTimestamp, long workItems = 1L)
    {
        if (!IsActive || startTimestamp <= 0L || string.IsNullOrEmpty(metricName))
        {
            return;
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
        double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
        _instance.RecordMetric(metricName, elapsedMs, Math.Max(1L, workItems));
    }

    public static void RecordCount(string metricName, long workItems = 1L)
    {
        if (!IsActive || string.IsNullOrEmpty(metricName) || workItems <= 0L)
        {
            return;
        }

        _instance.RecordMetric(metricName, 0.0, workItems);
    }

    public static void FlushNow(string reason)
    {
        if (IsActive)
        {
            _instance.WriteSummary(false, reason);
        }
    }

    private void Configure(MPTestCommandLine.Options options)
    {
        _options = options;
        _captureStartRealtime = Time.realtimeSinceStartup;
        _nextCheckpointRealtime = _captureStartRealtime + CheckpointIntervalSeconds;
        _nextNetworkSampleRealtime = _captureStartRealtime;
        _nextWorldSampleRealtime = _captureStartRealtime;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        TryStartRecorders();
    }

    private void Update()
    {
        if (!_options.Enabled || !_options.PerformanceCapture)
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now - _captureStartRealtime < _options.PerformanceWarmupSeconds)
        {
            FrameTimingManager.CaptureFrameTimings();
            return;
        }

        if (_skipSampleFrames > 0)
        {
            _skipSampleFrames--;
        }
        else
        {
            CaptureFrameSample();
        }

        if (now >= _nextNetworkSampleRealtime)
        {
            _nextNetworkSampleRealtime = now + NetworkSampleIntervalSeconds;
            CaptureFusionSample();
        }

        if (now >= _nextWorldSampleRealtime)
        {
            _nextWorldSampleRealtime = now + WorldSampleIntervalSeconds;
            CaptureWorldSample();
        }

        if (now >= _nextCheckpointRealtime)
        {
            _nextCheckpointRealtime = now + CheckpointIntervalSeconds;
            WriteSummary(false, "periodic_checkpoint");
            _skipSampleFrames = 2;
        }
    }

    private void CaptureFrameSample()
    {
        _wallFrameMs.Add(Time.unscaledDeltaTime * 1000.0);

        if (_mainThreadRecorder.Valid)
        {
            _mainThreadMs.Add(_mainThreadRecorder.LastValue / 1000000.0);
        }

        if (_renderThreadRecorder.Valid)
        {
            _renderThreadMs.Add(_renderThreadRecorder.LastValue / 1000000.0);
        }

        if (_gcAllocatedRecorder.Valid)
        {
            _gcAllocatedBytes.Add(_gcAllocatedRecorder.LastValue);
        }

        FrameTimingManager.CaptureFrameTimings();
        bool capturedGpuTiming = false;
        if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
        {
            FrameTiming timing = _frameTimings[0];
            if (timing.cpuFrameTime > 0.0)
            {
                _cpuFrameMs.Add(timing.cpuFrameTime);
            }

            if (timing.gpuFrameTime > 0.0)
            {
                _gpuFrameMs.Add(timing.gpuFrameTime);
                capturedGpuTiming = true;
            }
        }

        if (!capturedGpuTiming && _gpuFrameRecorder.Valid && _gpuFrameRecorder.LastValue > 0L)
        {
            _gpuFrameMs.Add(_gpuFrameRecorder.LastValue / 1000000.0);
        }
    }

    private void CaptureFusionSample()
    {
        NetworkRunner runner = NetworkManager.Instance != null ? NetworkManager.Instance._runner : null;
        if (runner == null || !runner.IsRunning || !runner.TryGetFusionStatistics(out FusionStatisticsManager statistics))
        {
            return;
        }

        FusionStatisticsSnapshot snapshot = statistics.CompleteSnapshot;
        _fusionInBandwidth.Add(snapshot.InBandwidth);
        _fusionOutBandwidth.Add(snapshot.OutBandwidth);
    }

    private void CaptureWorldSample()
    {
        int aliveMonsters = 0;
        GameManagers game = GameManagers.Instance;
        if (game != null && game.AllPlayers != null)
        {
            foreach (PlayerManager player in game.AllPlayers)
            {
                Transform monsterParent = player != null && player.monsterSpawner != null
                    ? player.monsterSpawner.monsterParent
                    : null;
                if (monsterParent != null)
                {
                    aliveMonsters += monsterParent.childCount;
                }
            }
        }

        _maxAliveMonsters = Mathf.Max(_maxAliveMonsters, aliveMonsters);
        _maxActiveProjectiles = Mathf.Max(_maxActiveProjectiles, ProjectileVfxManager.ActiveProjectileCountForDiagnostics);

        NetworkRunner runner = NetworkManager.Instance != null ? NetworkManager.Instance._runner : null;
        if (runner != null && runner.IsRunning)
        {
            int objectCount = 0;
            foreach (NetworkObject _ in runner.GetAllNetworkObjects())
            {
                objectCount++;
            }
            _maxNetworkObjects = Mathf.Max(_maxNetworkObjects, objectCount);
        }
    }

    private void RecordMetric(string metricName, double elapsedMs, long workItems)
    {
        if (!_customMetrics.TryGetValue(metricName, out MetricAggregate aggregate))
        {
            aggregate = new MetricAggregate();
            _customMetrics.Add(metricName, aggregate);
        }

        aggregate.Count++;
        aggregate.WorkItems += workItems;
        aggregate.TotalMilliseconds += elapsedMs;
        if (elapsedMs > aggregate.MaxMilliseconds)
        {
            aggregate.MaxMilliseconds = elapsedMs;
        }
    }

    private void TryStartRecorders()
    {
        _mainThreadRecorder = TryStartRecorder(ProfilerCategory.Internal, "Main Thread");
        _renderThreadRecorder = TryStartRecorder(ProfilerCategory.Internal, "Render Thread");
        _gpuFrameRecorder = TryStartRecorder(ProfilerCategory.Render, "GPU Frame Time");
        _gcAllocatedRecorder = TryStartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
    }

    private static ProfilerRecorder TryStartRecorder(ProfilerCategory category, string statName)
    {
        try
        {
            return ProfilerRecorder.StartNew(category, statName, 1);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[MPTEST][PERF] ProfilerRecorder unavailable: {statName}, {e.Message}");
            return default;
        }
    }

    private void WriteSummary(bool final, string reason)
    {
        if (_isWriting || (final && _finalWritten) || string.IsNullOrWhiteSpace(_options.ArtifactDir))
        {
            return;
        }

        _isWriting = true;
        try
        {
            Directory.CreateDirectory(_options.ArtifactDir);
            int processId = Process.GetCurrentProcess().Id;
            string role = string.IsNullOrWhiteSpace(_options.SafeRole) ? "unknown" : _options.SafeRole;
            string path = Path.Combine(_options.ArtifactDir, $"performance-{role}-{processId}.json");
            var custom = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, MetricAggregate> pair in _customMetrics)
            {
                custom[pair.Key] = pair.Value.ToSerializable();
            }

            var summary = new
            {
                version = 1,
                final,
                reason,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                role,
                caseName = _options.CaseName,
                session = _options.Session,
                processId,
                warmupSeconds = _options.PerformanceWarmupSeconds,
                elapsedSeconds = Math.Max(0f, Time.realtimeSinceStartup - _captureStartRealtime),
                frames = new
                {
                    wallMs = _wallFrameMs.ToSerializable(),
                    mainThreadMs = _mainThreadMs.ToSerializable(),
                    renderThreadMs = _renderThreadMs.ToSerializable(),
                    cpuFrameMs = _cpuFrameMs.ToSerializable(),
                    gpuFrameMs = _gpuFrameMs.ToSerializable(),
                    gcAllocatedBytes = _gcAllocatedBytes.ToSerializable()
                },
                fusion = new
                {
                    inBandwidthBytesPerSecond = _fusionInBandwidth.ToSerializable(),
                    outBandwidthBytesPerSecond = _fusionOutBandwidth.ToSerializable()
                },
                world = new
                {
                    maxAliveMonsters = _maxAliveMonsters,
                    maxActiveProjectiles = _maxActiveProjectiles,
                    maxNetworkObjects = _maxNetworkObjects
                },
                metrics = custom
            };

            File.WriteAllText(path, JsonConvert.SerializeObject(summary, Formatting.Indented));
            _finalWritten |= final;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"[MPTEST][PERF] Failed to write summary: {e.Message}");
        }
        finally
        {
            _isWriting = false;
        }
    }

    private void OnApplicationQuit()
    {
        WriteSummary(true, "application_quit");
    }

    private void OnDestroy()
    {
        WriteSummary(true, "recorder_destroyed");
        DisposeRecorders();
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void DisposeRecorders()
    {
        if (_mainThreadRecorder.Valid)
        {
            _mainThreadRecorder.Dispose();
        }
        if (_renderThreadRecorder.Valid)
        {
            _renderThreadRecorder.Dispose();
        }
        if (_gcAllocatedRecorder.Valid)
        {
            _gcAllocatedRecorder.Dispose();
        }
        if (_gpuFrameRecorder.Valid)
        {
            _gpuFrameRecorder.Dispose();
        }
    }

    private sealed class MetricAggregate
    {
        public long Count;
        public long WorkItems;
        public double TotalMilliseconds;
        public double MaxMilliseconds;

        public object ToSerializable()
        {
            return new
            {
                count = Count,
                workItems = WorkItems,
                totalMs = TotalMilliseconds,
                maxMs = MaxMilliseconds,
                meanMs = Count > 0 ? TotalMilliseconds / Count : 0.0,
                meanMsPerWorkItem = WorkItems > 0 ? TotalMilliseconds / WorkItems : 0.0
            };
        }
    }

    private sealed class SampleBuffer
    {
        private readonly double[] _values;
        private int _count;
        private int _writeIndex;
        private double _sum;
        private double _max;

        public SampleBuffer(int capacity)
        {
            _values = new double[Math.Max(1, capacity)];
        }

        public void Add(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                return;
            }

            if (_count < _values.Length)
            {
                _values[_writeIndex] = value;
                _count++;
                _sum += value;
            }
            else
            {
                _sum -= _values[_writeIndex];
                _values[_writeIndex] = value;
                _sum += value;
            }

            _writeIndex = (_writeIndex + 1) % _values.Length;
            if (value > _max)
            {
                _max = value;
            }
        }

        public object ToSerializable()
        {
            if (_count <= 0)
            {
                return new { count = 0, mean = 0.0, p50 = 0.0, p95 = 0.0, p99 = 0.0, max = 0.0 };
            }

            var sorted = new double[_count];
            Array.Copy(_values, sorted, _count);
            Array.Sort(sorted);
            return new
            {
                count = _count,
                mean = _sum / _count,
                p50 = Percentile(sorted, 0.50),
                p95 = Percentile(sorted, 0.95),
                p99 = Percentile(sorted, 0.99),
                max = Math.Max(_max, sorted[sorted.Length - 1])
            };
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            if (sorted == null || sorted.Length == 0)
            {
                return 0.0;
            }

            int index = Mathf.Clamp(Mathf.CeilToInt((float)(percentile * sorted.Length)) - 1, 0, sorted.Length - 1);
            return sorted[index];
        }
    }
}
#endif
