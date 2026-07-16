using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Warms the meshes, materials, and shader presentation used by an Addressable prefab without
/// instantiating any of the prefab's gameplay or networking components. The source renderers are
/// inspected as assets and their shared presentation data is rendered on disposable mesh-only
/// proxies into a tiny, off-screen render texture.
/// </summary>
public static class FirstSpawnPresentationPrewarmer
{
    private const int WarmupLayer = 31;
    private const int WarmupTextureSize = 32;
    private const float WarmupWorldY = -10000f;
#if UNITY_EDITOR
    private const float EditorShaderCompileTotalBudgetSeconds = 30f;
    private static float editorShaderCompileBudgetRemaining = EditorShaderCompileTotalBudgetSeconds;
    private static bool editorShaderBudgetWarningLogged;
#endif

    private sealed class WarmupEntry
    {
        public readonly UniTaskCompletionSource<bool> Completion = new UniTaskCompletionSource<bool>();
        public readonly string SemanticKey;
        public bool Succeeded;

        public WarmupEntry(string semanticKey)
        {
            SemanticKey = semanticKey;
        }
    }

    private sealed class MeshPresentation
    {
        public Mesh Mesh;
        public Material[] Materials;
        public bool IsSkinned;
    }

    private struct PresentationWarmupReport
    {
        public int MaterialCount;
        public int MeshCount;
        public bool GraphicsSkipped;
        public bool Hdr;
        public int MsaaSamples;
        public bool ShaderIdle;
    }

    private static readonly Dictionary<string, WarmupEntry> Entries =
        new Dictionary<string, WarmupEntry>(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> LatestGenerationKeyBySemanticKey =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForSubsystemRegistration()
    {
        Entries.Clear();
        LatestGenerationKeyBySemanticKey.Clear();
#if UNITY_EDITOR
        editorShaderCompileBudgetRemaining = EditorShaderCompileTotalBudgetSeconds;
        editorShaderBudgetWarningLogged = false;
#endif
    }

    /// <summary>
    /// Single-flight presentation warmup for one prefab. Callers that provide the same stable key
    /// share the in-flight work. Cancellation stops only the caller's wait; the shared warmup keeps
    /// running so another owner cannot invalidate it halfway through.
    /// </summary>
    public static async UniTask WarmPrefabAsync(
        GameObject prefab,
        string stableKey = null,
        CancellationToken cancellationToken = default)
    {
        if (prefab == null)
        {
            return;
        }

        string semanticKey = BuildSemanticKey(prefab, stableKey);
        string key = BuildStableKey(prefab, semanticKey);
        if (!Entries.TryGetValue(key, out WarmupEntry entry))
        {
            LatestGenerationKeyBySemanticKey[semanticKey] = key;
            PruneCompletedOlderGenerations(semanticKey, key);
            entry = new WarmupEntry(semanticKey);
            Entries.Add(key, entry);
            PublishWarmupAsync(prefab, key, entry).Forget();
        }

        UniTask<bool> waitTask = entry.Completion.Task;
        if (cancellationToken.CanBeCanceled)
        {
            await waitTask.AttachExternalCancellation(cancellationToken);
        }
        else
        {
            await waitTask;
        }
    }

    private static string BuildSemanticKey(GameObject prefab, string stableKey)
    {
        if (!string.IsNullOrWhiteSpace(stableKey))
        {
            return stableKey.Trim();
        }

        return $"prefab:{prefab.name}";
    }

    private static string BuildStableKey(GameObject prefab, string semanticKey)
    {
        // The same Addressables key can produce a new prefab object after every lease was
        // released. Keep concurrent callers single-flight while allowing a reloaded asset
        // generation to rebuild its mesh/material GPU state.
        return $"{semanticKey}@{prefab.GetInstanceID()}";
    }

    private static void PruneCompletedOlderGenerations(string semanticKey, string keyToKeep)
    {
        if (Entries.Count == 0)
        {
            return;
        }

        List<string> staleKeys = null;
        foreach (KeyValuePair<string, WarmupEntry> pair in Entries)
        {
            WarmupEntry candidate = pair.Value;
            if (pair.Key == keyToKeep || candidate == null || !candidate.Succeeded ||
                !string.Equals(candidate.SemanticKey, semanticKey, StringComparison.Ordinal))
            {
                continue;
            }

            staleKeys ??= new List<string>();
            staleKeys.Add(pair.Key);
        }

        if (staleKeys == null)
        {
            return;
        }

        foreach (string staleKey in staleKeys)
        {
            Entries.Remove(staleKey);
        }
    }

    private static async UniTaskVoid PublishWarmupAsync(
        GameObject prefab,
        string key,
        WarmupEntry entry)
    {
        try
        {
            float startedAt = Time.realtimeSinceStartup;
            PresentationWarmupReport report = await WarmPrefabCoreAsync(prefab);
            float durationMs = Mathf.Max(0f, Time.realtimeSinceStartup - startedAt) * 1000f;
            Debug.Log(
                $"[PREWARM] presentation complete key={key} prefab={prefab.name} " +
                $"materials={report.MaterialCount} meshes={report.MeshCount} " +
                $"graphicsSkipped={report.GraphicsSkipped} hdr={report.Hdr} " +
                $"msaa={report.MsaaSamples} shaderIdle={report.ShaderIdle} durationMs={durationMs:0.0}");
            entry.Succeeded = true;
            if (!LatestGenerationKeyBySemanticKey.TryGetValue(entry.SemanticKey, out string latestKey))
            {
                latestKey = key;
                LatestGenerationKeyBySemanticKey[entry.SemanticKey] = key;
            }
            PruneCompletedOlderGenerations(entry.SemanticKey, latestKey);
            entry.Completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            if (Entries.TryGetValue(key, out WarmupEntry current) && ReferenceEquals(current, entry))
            {
                Entries.Remove(key);
            }
            if (LatestGenerationKeyBySemanticKey.TryGetValue(entry.SemanticKey, out string latestKey) &&
                string.Equals(latestKey, key, StringComparison.Ordinal))
            {
                LatestGenerationKeyBySemanticKey.Remove(entry.SemanticKey);
            }

            entry.Completion.TrySetException(exception);
        }
    }

    private static async UniTask<PresentationWarmupReport> WarmPrefabCoreAsync(GameObject prefab)
    {
        List<Material> materials = CollectUniqueMaterials(prefab);
        List<MeshPresentation> meshes = CollectUniqueMeshPresentations(prefab);
        var report = new PresentationWarmupReport
        {
            MaterialCount = materials.Count,
            MeshCount = meshes.Count,
            GraphicsSkipped = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null,
            Hdr = false,
            MsaaSamples = 1,
            ShaderIdle = true
        };
        if ((materials.Count == 0 && meshes.Count == 0) || report.GraphicsSkipped)
        {
            return report;
        }

        GameObject warmupRoot = null;
        Mesh proxyMesh = null;
        RenderTexture renderTexture = null;

        try
        {
            warmupRoot = new GameObject("[FirstSpawnPresentationWarmup]")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = WarmupLayer
            };
            warmupRoot.transform.position = new Vector3(0f, WarmupWorldY, 0f);

            proxyMesh = CreateProxyQuad();
            int proxyCount = materials.Count + meshes.Count;
            CreateMaterialProxies(warmupRoot.transform, proxyMesh, materials, proxyCount);
            CreateMeshProxies(warmupRoot.transform, meshes, materials, proxyCount);

            Camera warmupCamera = CreateWarmupCamera(warmupRoot.transform);
            CreateWarmupLight(warmupRoot.transform);

            ResolveActiveGraphicsPath(out bool useHdr, out int requestedMsaaSamples);
            RenderTextureFormat renderTextureFormat = useHdr
                ? RenderTextureFormat.DefaultHDR
                : RenderTextureFormat.ARGB32;
            renderTexture = new RenderTexture(
                WarmupTextureSize,
                WarmupTextureSize,
                16,
                renderTextureFormat,
                RenderTextureReadWrite.Default)
            {
                name = "FirstSpawnPresentationWarmupRT",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            RenderTextureDescriptor descriptor = renderTexture.descriptor;
            descriptor.msaaSamples = requestedMsaaSamples;
            int supportedMsaaSamples = requestedMsaaSamples > 1
                ? SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor)
                : 1;
            renderTexture.antiAliasing = Mathf.Max(1, supportedMsaaSamples);
            warmupCamera.allowHDR = useHdr;
            warmupCamera.allowMSAA = renderTexture.antiAliasing > 1;
            report.Hdr = useHdr;
            report.MsaaSamples = renderTexture.antiAliasing;
            renderTexture.Create();
            warmupCamera.targetTexture = renderTexture;

            // Let the active render pipeline render this camera normally. Camera.Render() is not
            // supported reliably by URP/SRP and may leave the requested variants completely cold.
            // Any Editor cyan loading shader remains contained in this off-screen render texture.
            warmupCamera.enabled = true;
            await WaitForPipelineRenderFramesAsync(2);

#if UNITY_EDITOR
            // Async shader compilation can start one frame after the render request. Require two
            // consecutive idle frames before the final render so no cyan placeholder can escape
            // when the real gameplay object becomes visible.
            int idleFrames = 0;
            while (idleFrames < 2)
            {
                float frameStartedAt = Time.realtimeSinceStartup;
                if (ShaderUtil.anythingCompiling)
                {
                    idleFrames = 0;
                }
                else
                {
                    idleFrames++;
                }

                if (editorShaderCompileBudgetRemaining <= 0f)
                {
                    report.ShaderIdle = !ShaderUtil.anythingCompiling;
                    if (!editorShaderBudgetWarningLogged)
                    {
                        editorShaderBudgetWarningLogged = true;
                        Debug.LogWarning(
                            $"[FirstSpawnPresentationPrewarmer] Shared Editor shader wait budget " +
                            $"({EditorShaderCompileTotalBudgetSeconds:0}s) was exhausted. " +
                            "Prepare will continue; remaining keys report shaderIdle=false.");
                    }
                    break;
                }

                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                editorShaderCompileBudgetRemaining = Mathf.Max(
                    0f,
                    editorShaderCompileBudgetRemaining -
                    Mathf.Max(0f, Time.realtimeSinceStartup - frameStartedAt));
            }
            report.ShaderIdle = idleFrames >= 2 && !ShaderUtil.anythingCompiling;
#endif

            // Keep the camera alive for two more pipeline frames after compilation. In a Player
            // this also creates the GPU representation before the first visible spawn.
            await WaitForPipelineRenderFramesAsync(2);
            warmupCamera.enabled = false;
            warmupCamera.targetTexture = null;
            return report;
        }
        finally
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyObject(renderTexture);
            }

            if (warmupRoot != null)
            {
                DestroyObject(warmupRoot);
            }

            if (proxyMesh != null)
            {
                DestroyObject(proxyMesh);
            }
        }
    }

    private static void ResolveActiveGraphicsPath(out bool useHdr, out int msaaSamples)
    {
        RenderPipelineAsset renderPipelineAsset = GraphicsSettings.currentRenderPipeline;
        if (renderPipelineAsset != null)
        {
            // Keep MDF.Runtime.Assets pipeline-agnostic. URP exposes these public properties, but
            // a direct assembly reference would make the shared asset layer depend on one SRP.
            useHdr = ReadPipelineProperty(renderPipelineAsset, "supportsHDR", true);
            int configuredMsaa = ReadPipelineProperty(renderPipelineAsset, "msaaSampleCount", 1);
            msaaSamples = NormalizeMsaaSamples(configuredMsaa);
            return;
        }

        Camera gameplayCamera = Camera.main;
        useHdr = gameplayCamera == null || gameplayCamera.allowHDR;
        bool allowMsaa = gameplayCamera == null || gameplayCamera.allowMSAA;
        msaaSamples = allowMsaa ? NormalizeMsaaSamples(QualitySettings.antiAliasing) : 1;
    }

    private static T ReadPipelineProperty<T>(RenderPipelineAsset asset, string propertyName, T fallback)
    {
        if (asset == null || string.IsNullOrEmpty(propertyName))
        {
            return fallback;
        }

        PropertyInfo property = asset.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        if (property == null || property.PropertyType != typeof(T))
        {
            return fallback;
        }

        object value = property.GetValue(asset);
        return value is T typedValue ? typedValue : fallback;
    }

    private static int NormalizeMsaaSamples(int samples)
    {
        if (samples >= 8) return 8;
        if (samples >= 4) return 4;
        if (samples >= 2) return 2;
        return 1;
    }

    private static List<Material> CollectUniqueMaterials(GameObject prefab)
    {
        var materials = new List<Material>();
        var materialIds = new HashSet<int>();
        Renderer[] sourceRenderers = prefab.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < sourceRenderers.Length; rendererIndex++)
        {
            Renderer sourceRenderer = sourceRenderers[rendererIndex];
            if (sourceRenderer == null)
            {
                continue;
            }

            Material[] sharedMaterials = sourceRenderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
            {
                Material material = sharedMaterials[materialIndex];
                if (material == null || material.shader == null || !materialIds.Add(material.GetInstanceID()))
                {
                    continue;
                }

                materials.Add(material);
            }
        }

        return materials;
    }

    private static List<MeshPresentation> CollectUniqueMeshPresentations(GameObject prefab)
    {
        var presentations = new List<MeshPresentation>();
        var meshIds = new HashSet<int>();
        Renderer[] sourceRenderers = prefab.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < sourceRenderers.Length; rendererIndex++)
        {
            Renderer sourceRenderer = sourceRenderers[rendererIndex];
            if (sourceRenderer == null)
            {
                continue;
            }

            Mesh mesh = null;
            if (sourceRenderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                mesh = skinnedMeshRenderer.sharedMesh;
            }
            else if (sourceRenderer is MeshRenderer)
            {
                MeshFilter sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
                mesh = sourceFilter != null ? sourceFilter.sharedMesh : null;
            }

            if (mesh == null || !meshIds.Add(mesh.GetInstanceID()))
            {
                continue;
            }

            Material[] sharedMaterials = sourceRenderer.sharedMaterials;
            presentations.Add(new MeshPresentation
            {
                Mesh = mesh,
                Materials = sharedMaterials,
                IsSkinned = sourceRenderer is SkinnedMeshRenderer
            });
        }

        return presentations;
    }

    private static Mesh CreateProxyQuad()
    {
        var mesh = new Mesh
        {
            name = "FirstSpawnPresentationWarmupQuad",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            },
            normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
            tangents = new[]
            {
                new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 0f, 0f, 1f),
                new Vector4(1f, 0f, 0f, 1f)
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            },
            colors = new[] { Color.white, Color.white, Color.white, Color.white },
            triangles = new[] { 0, 2, 1, 0, 3, 2 }
        };
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        return mesh;
    }

    private static void CreateMaterialProxies(
        Transform parent,
        Mesh proxyMesh,
        IReadOnlyList<Material> materials,
        int totalProxyCount)
    {
        GetProxyGrid(totalProxyCount, out int columns, out int rows, out float scale);

        for (int index = 0; index < materials.Count; index++)
        {
            var proxy = new GameObject($"MaterialProxy_{index}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = WarmupLayer
            };
            proxy.transform.SetParent(parent, false);
            proxy.transform.localPosition = GetProxyPosition(index, columns, rows, scale);
            proxy.transform.localScale = Vector3.one * scale;

            MeshFilter filter = proxy.AddComponent<MeshFilter>();
            filter.sharedMesh = proxyMesh;

            MeshRenderer renderer = proxy.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materials[index];
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }
    }

    private static void CreateMeshProxies(
        Transform parent,
        IReadOnlyList<MeshPresentation> presentations,
        IReadOnlyList<Material> fallbackMaterials,
        int totalProxyCount)
    {
        GetProxyGrid(totalProxyCount, out int columns, out int rows, out float cellScale);
        for (int index = 0; index < presentations.Count; index++)
        {
            MeshPresentation presentation = presentations[index];
            if (presentation?.Mesh == null)
            {
                continue;
            }

            Material fallbackMaterial = presentation.Materials != null
                ? presentation.Materials.FirstOrDefault(material => material != null && material.shader != null)
                : null;
            if (fallbackMaterial == null && fallbackMaterials.Count > 0)
            {
                fallbackMaterial = fallbackMaterials[0];
            }

            if (fallbackMaterial == null)
            {
                continue;
            }

            var proxy = new GameObject($"MeshProxy_{index}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = WarmupLayer
            };
            proxy.transform.SetParent(parent, false);

            int gridIndex = fallbackMaterials.Count + index;
            Vector3 cellPosition = GetProxyPosition(gridIndex, columns, rows, cellScale);
            Bounds bounds = presentation.Mesh.bounds;
            float maxDimension = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float normalization = IsFinitePositive(maxDimension) ? cellScale / maxDimension : cellScale;
            proxy.transform.localScale = Vector3.one * normalization;
            proxy.transform.localPosition = cellPosition - bounds.center * normalization;

            int subMeshCount = Mathf.Max(1, presentation.Mesh.subMeshCount);
            var proxyMaterials = new Material[subMeshCount];
            for (int materialIndex = 0; materialIndex < proxyMaterials.Length; materialIndex++)
            {
                Material sourceMaterial = presentation.Materials != null &&
                                          materialIndex < presentation.Materials.Length
                    ? presentation.Materials[materialIndex]
                    : null;
                proxyMaterials[materialIndex] = sourceMaterial != null ? sourceMaterial : fallbackMaterial;
            }

            Renderer renderer;
            if (presentation.IsSkinned)
            {
                var skinnedRenderer = proxy.AddComponent<SkinnedMeshRenderer>();
                skinnedRenderer.sharedMesh = presentation.Mesh;
                Transform[] proxyBones = CreateSkinnedProxyBones(proxy.transform, presentation.Mesh);
                skinnedRenderer.bones = proxyBones;
                skinnedRenderer.rootBone = proxyBones[0];
                skinnedRenderer.localBounds = presentation.Mesh.bounds;
                skinnedRenderer.updateWhenOffscreen = true;
                skinnedRenderer.quality = SkinQuality.Bone4;
                renderer = skinnedRenderer;
            }
            else
            {
                MeshFilter filter = proxy.AddComponent<MeshFilter>();
                filter.sharedMesh = presentation.Mesh;
                renderer = proxy.AddComponent<MeshRenderer>();
            }

            renderer.sharedMaterials = proxyMaterials;
            ConfigureProxyRenderer(renderer);
        }
    }

    private static Transform[] CreateSkinnedProxyBones(Transform parent, Mesh mesh)
    {
        int boneCount = Mathf.Max(1, mesh != null ? mesh.bindposes.Length : 0);
        var bones = new Transform[boneCount];
        for (int index = 0; index < boneCount; index++)
        {
            var boneObject = new GameObject($"WarmupBone_{index}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = WarmupLayer
            };
            boneObject.transform.SetParent(parent, false);
            bones[index] = boneObject.transform;
        }

        return bones;
    }

    private static void ConfigureProxyRenderer(Renderer renderer)
    {
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.allowOcclusionWhenDynamic = false;
    }

    private static void GetProxyGrid(
        int proxyCount,
        out int columns,
        out int rows,
        out float scale)
    {
        columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, proxyCount))));
        rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, proxyCount) / (float)columns));
        scale = 0.8f / Mathf.Max(columns, rows);
    }

    private static Vector3 GetProxyPosition(int index, int columns, int rows, float scale)
    {
        int column = index % columns;
        int row = index / columns;
        return new Vector3(
            (column - (columns - 1) * 0.5f) * scale * 1.15f,
            ((rows - 1) * 0.5f - row) * scale * 1.15f,
            0f);
    }

    private static bool IsFinitePositive(float value)
    {
        return value > Mathf.Epsilon && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static async UniTask WaitForPipelineRenderFramesAsync(int frameCount)
    {
        for (int frame = 0; frame < Mathf.Max(1, frameCount); frame++)
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        }
    }

    private static Camera CreateWarmupCamera(Transform parent)
    {
        var cameraObject = new GameObject("WarmupCamera")
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = WarmupLayer
        };
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0f, -3f);
        cameraObject.transform.localRotation = Quaternion.identity;

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << WarmupLayer;
        camera.orthographic = true;
        camera.orthographicSize = 0.65f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 10f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.useOcclusionCulling = false;
        return camera;
    }

    private static void CreateWarmupLight(Transform parent)
    {
        var lightObject = new GameObject("WarmupDirectionalLight")
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = WarmupLayer
        };
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localRotation = Quaternion.Euler(45f, -30f, 0f);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1f;
        light.cullingMask = 1 << WarmupLayer;
        light.shadows = LightShadows.Soft;
    }

    private static void DestroyObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
