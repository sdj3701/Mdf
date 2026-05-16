#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public sealed class AttackSlashCalibratorWindow : EditorWindow
{
    private const int SampleCount = 18;
    private const float GeneratedScaleMultiplier = 2f;

    private UnitData unitData;
    private GameObject unitPrefab;
    private GameObject slashPrefab;
    private AnimationClip attackClip;
    private int starLevel = 1;
    private bool applyToAllStars = true;
    private bool requireProbeMarkers = true;
    private bool showAdvancedActions;

    private AttackSlashCalibrationUtility.CalibrationResult lastResult;
    private string lastSpawnOriginPath;
    private string lastTrackingPath;
    private string lastMessage;
    private string resolvedUnitPrefabAddress;
    private string resolvedSlashPrefabAddress;
    private UnitData resolvedForUnitData;
    private int resolvedForStarLevel;
    private float lastStrikeSpanLength;
    private float lastScaleTargetLength;
    private readonly List<Vector3> previewTrajectory = new List<Vector3>();
    private Vector3 previewRootPosition;
    private Quaternion previewRootRotation = Quaternion.identity;
    private float previewScale = 1f;

    [MenuItem("Tools/MDF/VFX/Attack Slash Calibrator")]
    public static void Open()
    {
        GetWindow<AttackSlashCalibratorWindow>("Slash Calibrator");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += DrawScenePreview;
        TryUseSelection();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawScenePreview;
    }

    private void OnSelectionChange()
    {
        if (Selection.activeObject is UnitData selectedUnitData && selectedUnitData != unitData)
        {
            unitData = selectedUnitData;
            ResolveFromUnitData();
            Repaint();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Attack Slash Calibration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Samples a unit attack animation, estimates the weapon/hand trajectory, analyzes the slash prefab shape, and writes placement offsets to UnitData. Source VFX prefabs are not modified.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        unitData = (UnitData)EditorGUILayout.ObjectField("Unit Data", unitData, typeof(UnitData), false);
        starLevel = EditorGUILayout.IntSlider("Star Level", starLevel, 1, 3);
        if (EditorGUI.EndChangeCheck())
        {
            ResolveFromUnitData();
        }

        ResolveFromUnitDataIfStale();

        DrawResolvedReferenceStatus();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Resolved Unit Prefab", unitPrefab, typeof(GameObject), false);
            EditorGUILayout.ObjectField("Resolved Slash Wrapper Prefab", slashPrefab, typeof(GameObject), false);
            EditorGUILayout.ObjectField("Resolved Attack Clip", attackClip, typeof(AnimationClip), false);
        }

        applyToAllStars = EditorGUILayout.ToggleLeft("Apply result to all star levels", applyToAllStars);
        requireProbeMarkers = EditorGUILayout.ToggleLeft("Require AttackVfxProbe strike base/tip", requireProbeMarkers);

        DrawProbeStatus();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = unitPrefab != null && slashPrefab != null;
            if (GUILayout.Button("Analyze"))
            {
                Analyze();
            }

            GUI.enabled = lastResult.isValid && unitData != null;
            if (GUILayout.Button("Apply Calibration"))
            {
                ApplyCalibration();
            }
            GUI.enabled = true;
        }

        showAdvancedActions = EditorGUILayout.Foldout(showAdvancedActions, "Advanced", true);
        if (showAdvancedActions)
        {
            EditorGUILayout.HelpBox("Batch calibration writes multiple UnitData assets. Prefer Analyze/Apply for per-unit setup.", MessageType.Warning);
            if (GUILayout.Button("Batch Calibrate Melee UnitData"))
            {
                BatchCalibrateMeleeUnitData();
            }
        }

        if (!string.IsNullOrEmpty(lastMessage))
        {
            EditorGUILayout.HelpBox(lastMessage, lastResult.isValid ? MessageType.Info : MessageType.Warning);
        }

        if (lastResult.isValid)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.Vector3Field("Local Offset", lastResult.localPositionOffset);
            EditorGUILayout.Vector3Field("Rotation Offset", lastResult.rotationOffsetEuler);
            EditorGUILayout.FloatField("Auto Scale", lastResult.scaleMultiplier);
            EditorGUILayout.FloatField("Scale Boost", GeneratedScaleMultiplier);
            EditorGUILayout.FloatField("Strike Size", lastStrikeSpanLength);
            EditorGUILayout.FloatField("Scale Target Size", lastScaleTargetLength);
            EditorGUILayout.Slider("Quality", lastResult.quality, 0f, 1f);
            EditorGUILayout.TextField("Spawn Origin", lastSpawnOriginPath);
            EditorGUILayout.TextField("Tracking Point", lastTrackingPath);
        }
    }

    private void TryUseSelection()
    {
        if (Selection.activeObject is UnitData selectedUnitData)
        {
            unitData = selectedUnitData;
            ResolveFromUnitData();
        }
        else if (Selection.activeGameObject != null)
        {
            unitPrefab = Selection.activeGameObject;
        }
    }

    private void ResolveFromUnitData()
    {
        if (unitData == null)
        {
            ClearResolvedReferences();
            lastMessage = "Select a UnitData asset first.";
            return;
        }

        unitData.EnsureBasicAttackVfxConfigArray();
        int index = Mathf.Clamp(starLevel - 1, 0, 2);
        resolvedUnitPrefabAddress = GetArrayValue(unitData.prefabsByStarLevel, index);
        unitPrefab = ResolveAddressableGameObject(resolvedUnitPrefabAddress);

        BasicAttackVfxConfig config = unitData.GetBasicAttackVfxConfig(starLevel);
        resolvedSlashPrefabAddress = config != null && config.HasPrefabKey
            ? config.prefabKey
            : GetArrayValue(unitData.basicAttackVfxPrefabsByStarLevel, index);
        slashPrefab = ResolveAddressableGameObject(resolvedSlashPrefabAddress);
        attackClip = FindAttackClip(unitPrefab);
        resolvedForUnitData = unitData;
        resolvedForStarLevel = starLevel;
        lastResult = default;
        lastStrikeSpanLength = 0f;
        lastScaleTargetLength = 0f;
        previewTrajectory.Clear();

        var warnings = new List<string>();
        if (unitData.unitType != UnitType.Melee)
        {
            warnings.Add("selected UnitData is not Melee");
        }

        if (unitPrefab == null)
        {
            warnings.Add($"unit prefab address not found: '{resolvedUnitPrefabAddress}'");
        }

        if (slashPrefab == null)
        {
            warnings.Add($"slash prefab address not found: '{resolvedSlashPrefabAddress}'");
        }

        if (attackClip == null)
        {
            warnings.Add("attack clip not found");
        }

        lastMessage = warnings.Count == 0
            ? $"Resolved UnitData references for {unitData.name}."
            : $"Resolved {unitData.name}, but check: {string.Join(", ", warnings)}.";
    }

    private void ClearResolvedReferences()
    {
        unitPrefab = null;
        slashPrefab = null;
        attackClip = null;
        resolvedUnitPrefabAddress = string.Empty;
        resolvedSlashPrefabAddress = string.Empty;
        resolvedForUnitData = null;
        resolvedForStarLevel = 0;
        lastResult = default;
        lastStrikeSpanLength = 0f;
        lastScaleTargetLength = 0f;
        previewTrajectory.Clear();
    }

    private void ResolveFromUnitDataIfStale()
    {
        if (unitData == null || (resolvedForUnitData == unitData && resolvedForStarLevel == starLevel))
        {
            return;
        }

        ResolveFromUnitData();
    }

    private void Analyze()
    {
        ResolveFromUnitDataIfStale();
        previewTrajectory.Clear();
        lastResult = default;
        lastStrikeSpanLength = 0f;
        lastScaleTargetLength = 0f;
        lastMessage = string.Empty;

        if (unitPrefab == null)
        {
            lastMessage = "Unit prefab is required.";
            return;
        }

        if (slashPrefab == null)
        {
            lastMessage = "Slash wrapper prefab is required.";
            return;
        }

        if (attackClip == null)
        {
            attackClip = FindAttackClip(unitPrefab);
        }

        if (attackClip == null)
        {
            lastMessage = "Attack animation clip could not be found.";
            return;
        }

        GameObject unitInstance = null;
        GameObject slashInstance = null;
        bool startedAnimationMode = false;

        try
        {
            unitInstance = PrefabUtility.InstantiatePrefab(unitPrefab) as GameObject;
            slashInstance = PrefabUtility.InstantiatePrefab(slashPrefab) as GameObject;
            if (unitInstance == null || slashInstance == null)
            {
                lastMessage = "Failed to instantiate preview prefabs.";
                return;
            }

            unitInstance.hideFlags = HideFlags.HideAndDontSave;
            slashInstance.hideFlags = HideFlags.HideAndDontSave;
            unitInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            slashInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var probe = unitInstance.GetComponentInChildren<AttackVfxProbe>(true);
            Transform tracking = ResolveTrackingTransform(unitInstance.transform, probe);
            Transform spawnOrigin = ResolveSpawnOriginTransform(unitInstance.transform, probe, tracking);
            Transform strikeBase = probe != null ? probe.weaponBase : null;
            Transform strikeTip = probe != null ? probe.weaponTip : null;
            if (requireProbeMarkers && (probe == null || probe.weaponBase == null || probe.weaponTip == null))
            {
                lastMessage = "AttackVfxProbe with strike base/tip is required for this calibration mode. Use weaponBase/weaponTip fields for weapons, fists, or feet.";
                return;
            }

            lastTrackingPath = GetRelativePath(unitInstance.transform, tracking);
            lastSpawnOriginPath = GetRelativePath(unitInstance.transform, spawnOrigin);

            startedAnimationMode = !AnimationMode.InAnimationMode();
            if (startedAnimationMode)
            {
                AnimationMode.StartAnimationMode();
            }

            float impactTime = ResolveImpactTime(attackClip);
            float window = Mathf.Max(0.05f, attackClip.length * 0.22f);
            float start = Mathf.Clamp(impactTime - window * 0.5f, 0f, Mathf.Max(0f, attackClip.length - 0.001f));
            float end = Mathf.Clamp(impactTime + window * 0.5f, start + 0.001f, attackClip.length);
            Vector3 originAtImpact = Vector3.zero;
            var trajectoryPoints = new List<Vector3>(SampleCount);
            float strikeSpanTotal = 0f;
            int strikeSpanSamples = 0;

            for (int i = 0; i < SampleCount; i++)
            {
                float t = Mathf.Lerp(start, end, SampleCount == 1 ? 0f : i / (float)(SampleCount - 1));
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(unitInstance, attackClip, t);
                AnimationMode.EndSampling();
                trajectoryPoints.Add(tracking.position);
                if (strikeBase != null && strikeTip != null)
                {
                    strikeSpanTotal += Vector3.Distance(strikeBase.position, strikeTip.position);
                    strikeSpanSamples++;
                }
            }

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(unitInstance, attackClip, impactTime);
            AnimationMode.EndSampling();
            originAtImpact = spawnOrigin.position;

            previewTrajectory.AddRange(trajectoryPoints);
            var trajectory = AttackSlashCalibrationUtility.AnalyzeTrajectory(trajectoryPoints, unitInstance.transform.forward, Vector3.up);
            var slashShape = AnalyzeSlashShape(slashInstance);
            lastStrikeSpanLength = strikeSpanSamples > 0 ? strikeSpanTotal / strikeSpanSamples : 0f;
            lastScaleTargetLength = AttackSlashCalibrationUtility.ResolveTargetVisualLength(trajectory.length, lastStrikeSpanLength);
            lastResult = AttackSlashCalibrationUtility.CalculateFit(trajectory, slashShape, originAtImpact, unitInstance.transform.forward, lastStrikeSpanLength, GeneratedScaleMultiplier);

            if (lastResult.isValid)
            {
                Quaternion attackRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                previewRootRotation = attackRotation * Quaternion.Euler(lastResult.rotationOffsetEuler);
                previewRootPosition = originAtImpact + attackRotation * lastResult.localPositionOffset;
                previewScale = lastResult.scaleMultiplier;
                lastMessage = $"Calibration ready. Quality={lastResult.quality:0.00}, autoScale={lastResult.scaleMultiplier:0.###}, scaleBoost={GeneratedScaleMultiplier:0.###}, strikeSize={lastStrikeSpanLength:0.###}, targetSize={lastScaleTargetLength:0.###}, samples={trajectory.sampleCount}, slashPoints={slashShape.sampleCount}.";
            }
            else
            {
                lastMessage = lastResult.reason;
            }
        }
        finally
        {
            if (startedAnimationMode)
            {
                AnimationMode.StopAnimationMode();
            }

            if (unitInstance != null)
            {
                DestroyImmediate(unitInstance);
            }

            if (slashInstance != null)
            {
                DestroyImmediate(slashInstance);
            }
        }

        SceneView.RepaintAll();
    }

    private void ApplyCalibration()
    {
        if (!lastResult.isValid || unitData == null)
        {
            return;
        }

        string address = ResolveAddress(slashPrefab);
        if (string.IsNullOrEmpty(address))
        {
            lastMessage = "Slash prefab must be registered in Addressables before applying.";
            return;
        }

        Undo.RecordObject(unitData, "Apply Attack Slash Calibration");
        unitData.EnsureBasicAttackVfxConfigArray();
        EnsureStringArray(ref unitData.basicAttackVfxPrefabsByStarLevel, 3);

        int start = applyToAllStars ? 0 : Mathf.Clamp(starLevel - 1, 0, 2);
        int end = applyToAllStars ? 2 : start;
        string clipGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(attackClip));

        for (int i = start; i <= end; i++)
        {
            var config = unitData.basicAttackVfxConfigsByStarLevel[i] ?? BasicAttackVfxConfig.CreateDefault(address);
            config.prefabKey = address;
            config.spawnOriginPath = lastSpawnOriginPath;
            config.localPositionOffset = lastResult.localPositionOffset;
            config.rotationOffsetEuler = lastResult.rotationOffsetEuler;
            config.scaleMultiplier = lastResult.scaleMultiplier;
            config.calibrationQuality = lastResult.quality;
            config.calibratedAttackClipGuid = clipGuid;
            config.calibrationSource = "AttackSlashCalibrator";
            unitData.basicAttackVfxConfigsByStarLevel[i] = config;
            unitData.basicAttackVfxPrefabsByStarLevel[i] = address;
        }

        EditorUtility.SetDirty(unitData);
        AssetDatabase.SaveAssets();
        lastMessage = $"Applied calibration to {unitData.name}.";
    }

    private void DrawProbeStatus()
    {
        if (unitPrefab == null)
        {
            return;
        }

        var probe = unitPrefab.GetComponentInChildren<AttackVfxProbe>(true);
        if (probe == null)
        {
            EditorGUILayout.HelpBox("No AttackVfxProbe found on the selected unit prefab. Add one to the unit root, then assign strike base/tip using the weaponBase and weaponTip fields.", MessageType.Warning);
            return;
        }

        string slashOrigin = probe.slashOrigin != null ? GetRelativePath(unitPrefab.transform, probe.slashOrigin) : "(optional, falls back to weaponBase)";
        string weaponBase = probe.weaponBase != null ? GetRelativePath(unitPrefab.transform, probe.weaponBase) : "(missing)";
        string weaponTip = probe.weaponTip != null ? GetRelativePath(unitPrefab.transform, probe.weaponTip) : "(missing)";
        MessageType type = probe.weaponBase != null && probe.weaponTip != null ? MessageType.Info : MessageType.Warning;
        EditorGUILayout.HelpBox($"AttackVfxProbe\nslashOrigin: {slashOrigin}\nstrike base (weaponBase): {weaponBase}\nstrike tip (weaponTip): {weaponTip}", type);
    }

    private void DrawResolvedReferenceStatus()
    {
        if (unitData == null)
        {
            return;
        }

        string unitAssetName = unitPrefab != null ? unitPrefab.name : "(not resolved)";
        string slashAssetName = slashPrefab != null ? slashPrefab.name : "(not resolved)";
        string clipName = attackClip != null ? attackClip.name : "(not found)";
        MessageType type = unitPrefab != null && slashPrefab != null && attackClip != null ? MessageType.Info : MessageType.Warning;

        EditorGUILayout.HelpBox(
            $"Resolved from {unitData.name} star {starLevel}\n" +
            $"unit prefab: {unitAssetName} [{resolvedUnitPrefabAddress}]\n" +
            $"slash prefab: {slashAssetName} [{resolvedSlashPrefabAddress}]\n" +
            $"attack clip: {clipName}",
            type);
    }

    private void BatchCalibrateMeleeUnitData()
    {
        string[] guids = AssetDatabase.FindAssets("t:UnitData", new[] { "Assets/GameData/Units" });
        int applied = 0;
        int skipped = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var data = AssetDatabase.LoadAssetAtPath<UnitData>(path);
                if (data == null || data.unitType != UnitType.Melee)
                {
                    skipped++;
                    continue;
                }

                unitData = data;
                starLevel = 1;
                applyToAllStars = true;
                ResolveFromUnitData();
                if (unitPrefab == null || slashPrefab == null)
                {
                    skipped++;
                    continue;
                }

                Analyze();
                if (!lastResult.isValid)
                {
                    skipped++;
                    continue;
                }

                ApplyCalibration();
                applied++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        lastMessage = $"Batch calibration complete. Applied={applied}, skipped={skipped}. Review quality before committing generated asset changes.";
    }

    private static AttackSlashCalibrationUtility.ShapeAnalysis AnalyzeSlashShape(GameObject slashInstance)
    {
        var points = new List<Vector3>(256);
        Transform root = slashInstance.transform;
        var particles = slashInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int p = 0; p < particles.Length; p++)
        {
            ParticleSystem system = particles[p];
            system.gameObject.SetActive(true);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float duration = Mathf.Max(0.1f, system.main.duration);
            int maxParticles = Mathf.Clamp(system.main.maxParticles, 1, 2048);
            var buffer = new ParticleSystem.Particle[maxParticles];
            for (int i = 0; i < 5; i++)
            {
                float time = duration * (i / 4f);
                system.Simulate(time, true, true, true);
                int count = system.GetParticles(buffer);
                for (int j = 0; j < count; j++)
                {
                    Vector3 worldPosition = system.main.simulationSpace == ParticleSystemSimulationSpace.World
                        ? buffer[j].position
                        : system.transform.TransformPoint(buffer[j].position);
                    points.Add(root.InverseTransformPoint(worldPosition));
                }
            }
        }

        if (points.Count == 0)
        {
            var renderers = slashInstance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                AddBoundsPoints(points, root, renderers[i].bounds);
            }
        }

        if (points.Count == 0)
        {
            var transforms = slashInstance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                points.Add(root.InverseTransformPoint(transforms[i].position));
            }
        }

        return AttackSlashCalibrationUtility.AnalyzePointCloud(points, Vector3.forward, Vector3.up);
    }

    private static void AddBoundsPoints(List<Vector3> points, Transform root, Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        points.Add(root.InverseTransformPoint(new Vector3(min.x, min.y, min.z)));
        points.Add(root.InverseTransformPoint(new Vector3(min.x, min.y, max.z)));
        points.Add(root.InverseTransformPoint(new Vector3(min.x, max.y, min.z)));
        points.Add(root.InverseTransformPoint(new Vector3(min.x, max.y, max.z)));
        points.Add(root.InverseTransformPoint(new Vector3(max.x, min.y, min.z)));
        points.Add(root.InverseTransformPoint(new Vector3(max.x, min.y, max.z)));
        points.Add(root.InverseTransformPoint(new Vector3(max.x, max.y, min.z)));
        points.Add(root.InverseTransformPoint(new Vector3(max.x, max.y, max.z)));
    }

    private static Transform ResolveTrackingTransform(Transform root, AttackVfxProbe probe)
    {
        if (probe != null && probe.weaponTip != null)
        {
            return probe.weaponTip;
        }

        return FindByName(root, "weapontip", "bladetip", "swordtip", "tip", "weapon", "blade", "sword", "hand_r", "righthand")
            ?? root;
    }

    private static Transform ResolveSpawnOriginTransform(Transform root, AttackVfxProbe probe, Transform tracking)
    {
        if (probe != null && probe.slashOrigin != null)
        {
            return probe.slashOrigin;
        }

        if (probe != null && probe.weaponBase != null)
        {
            return probe.weaponBase;
        }

        return FindByName(root, "hand_r", "righthand", "weapon", "sword", "blade", "firepoint") ?? tracking ?? root;
    }

    private static Transform FindByName(Transform root, params string[] names)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int p = 0; p < names.Length; p++)
        {
            string needle = names[p].ToLowerInvariant();
            for (int i = 0; i < transforms.Length; i++)
            {
                string candidate = transforms[i].name.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
                if (candidate.Contains(needle.Replace("_", string.Empty)))
                {
                    return transforms[i];
                }
            }
        }

        return null;
    }

    private static string GetRelativePath(Transform root, Transform child)
    {
        if (root == null || child == null || root == child)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            parts.Push(current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", parts.ToArray()) : string.Empty;
    }

    private static float ResolveImpactTime(AnimationClip clip)
    {
        if (clip == null)
        {
            return 0f;
        }

        var events = AnimationUtility.GetAnimationEvents(clip);
        for (int i = 0; i < events.Length; i++)
        {
            if (events[i].functionName == "AnimEvent_AttackImpact")
            {
                return Mathf.Clamp(events[i].time, 0f, clip.length);
            }
        }

        return clip.length * 0.5f;
    }

    private static AnimationClip FindAttackClip(GameObject prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        var animator = prefab.GetComponentInChildren<Animator>(true);
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return null;
        }

        return animator.runtimeAnimatorController.animationClips
            .Where(clip => clip != null)
            .OrderByDescending(clip => clip.name.ToLowerInvariant().Contains("attack"))
            .ThenBy(clip => clip.length)
            .FirstOrDefault();
    }

    private static GameObject ResolveAddressableGameObject(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || AddressableAssetSettingsDefaultObject.Settings == null)
        {
            return null;
        }

        AddressableAssetEntry entry = AddressableAssetSettingsDefaultObject.Settings.groups
            .Where(group => group != null)
            .SelectMany(group => group.entries)
            .FirstOrDefault(item => item.address == address);
        return entry == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath);
    }

    private static string ResolveAddress(GameObject asset)
    {
        if (asset == null || AddressableAssetSettingsDefaultObject.Settings == null)
        {
            return string.Empty;
        }

        string path = AssetDatabase.GetAssetPath(asset);
        string guid = AssetDatabase.AssetPathToGUID(path);
        AddressableAssetEntry entry = AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(guid);
        return entry != null ? entry.address : string.Empty;
    }

    private static string GetArrayValue(string[] values, int index)
    {
        return values != null && index >= 0 && index < values.Length ? values[index] : string.Empty;
    }

    private static void EnsureStringArray(ref string[] values, int size)
    {
        if (values != null && values.Length == size)
        {
            return;
        }

        string[] resized = new string[size];
        if (values != null)
        {
            int count = Mathf.Min(values.Length, size);
            for (int i = 0; i < count; i++)
            {
                resized[i] = values[i];
            }
        }

        values = resized;
    }

    private void DrawScenePreview(SceneView sceneView)
    {
        if (!lastResult.isValid || previewTrajectory.Count == 0)
        {
            return;
        }

        Handles.color = Color.yellow;
        Handles.DrawAAPolyLine(4f, previewTrajectory.ToArray());
        Handles.color = Color.cyan;
        Handles.SphereHandleCap(0, previewRootPosition, Quaternion.identity, 0.08f, EventType.Repaint);
        Handles.ArrowHandleCap(0, previewRootPosition, previewRootRotation, Mathf.Max(0.25f, previewScale), EventType.Repaint);
    }
}
#endif
