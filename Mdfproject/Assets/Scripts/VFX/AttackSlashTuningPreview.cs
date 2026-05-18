using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

[DisallowMultipleComponent]
public sealed class AttackSlashTuningPreview : MonoBehaviour
{
    private const string ManualTuningSource = "AttackSlashTuningScene";

    [Header("Unit")]
    [SerializeField] private UnitData unitData;
    [SerializeField, Range(1, 3)] private int starLevel = 1;
    [SerializeField] private bool applyToAllStarLevels = true;

    [Header("VFX")]
    [SerializeField] private GameObject slashPrefab;
    [SerializeField] private string slashPrefabAddress = "VFX_AttackSlash_SwordSlash5";
    [SerializeField] private Transform spawnOrigin;
    [SerializeField] private Transform targetOverride;
    [SerializeField] private Vector3 localPositionOffset = new Vector3(0f, 0.6f, 0.75f);
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;
    [SerializeField] private BasicAttackVfxRotationMode rotationMode = BasicAttackVfxRotationMode.TargetFacing;
    [SerializeField] private float scaleMultiplier = 1f;
    [SerializeField] private float playbackSpeed = 1f;
    [SerializeField] private Vector3 primaryRendererFlip = Vector3.zero;

    [Header("Animation Preview")]
    [SerializeField] private bool spawnWhenAnimatorAttackStatePlays = true;
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private string attackTriggerName = "AttackTrigger";
    [SerializeField, Range(0f, 0.95f)] private float attackSpawnNormalizedTime = 0.2f;
    [SerializeField] private bool replacePreviousPreview = true;
    [SerializeField] private bool autoDestroyPreviewInstances;
    [SerializeField] private float previewLifetimeSeconds = 2.1f;

    [Header("Loop Preview")]
    [SerializeField] private bool loopAttackAndVfx = true;
    [SerializeField] private float loopIntervalSeconds = 1.25f;
    [SerializeField] private bool restartExistingPreviewInstance = true;

    private Animator _animator;
    private int _lastAttackStateHash;
    private int _lastAttackLoop = int.MinValue;
    private float _lastAttackPhase = -1f;
    private bool _wasInAttackState;
    private float _nextLoopPreviewTime;
    private GameObject _lastPreviewInstance;

    public UnitData UnitData => unitData;
    public int StarLevel => starLevel;
    public GameObject LastPreviewInstance => _lastPreviewInstance;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void OnValidate()
    {
        starLevel = Mathf.Clamp(starLevel, 1, 3);
        scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
        playbackSpeed = Mathf.Max(0.01f, playbackSpeed);
        attackSpawnNormalizedTime = Mathf.Clamp(attackSpawnNormalizedTime, 0f, 0.95f);
        previewLifetimeSeconds = Mathf.Max(0.05f, previewLifetimeSeconds);
        loopIntervalSeconds = Mathf.Max(0.05f, loopIntervalSeconds);
    }

    private void OnEnable()
    {
        PullFromUnitData();
        _nextLoopPreviewTime = 0f;
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (_animator == null)
        {
            _animator = GetComponent<Animator>();
        }

        if (loopAttackAndVfx && Time.time >= _nextLoopPreviewTime)
        {
            _nextLoopPreviewTime = Time.time + Mathf.Max(0.05f, loopIntervalSeconds);
            TriggerAttack();

            if (!spawnWhenAnimatorAttackStatePlays || _animator == null || !_animator.isActiveAndEnabled || _animator.layerCount <= 0)
            {
                ReplayPreview(false);
            }
        }

        if (!spawnWhenAnimatorAttackStatePlays)
        {
            return;
        }

        if (_animator == null || !_animator.isActiveAndEnabled || _animator.layerCount <= 0)
        {
            return;
        }

        if (!TryGetAttackState(out AnimatorStateInfo attackState))
        {
            _wasInAttackState = false;
            _lastAttackStateHash = 0;
            _lastAttackLoop = int.MinValue;
            _lastAttackPhase = -1f;
            return;
        }

        int stateHash = attackState.shortNameHash;
        int loop = Mathf.FloorToInt(Mathf.Max(attackState.normalizedTime, 0f));
        float phase = Mathf.Repeat(attackState.normalizedTime, 1f);
        if (!_wasInAttackState || stateHash != _lastAttackStateHash || phase + 0.05f < _lastAttackPhase)
        {
            _lastAttackLoop = int.MinValue;
        }

        _wasInAttackState = true;
        _lastAttackStateHash = stateHash;
        _lastAttackPhase = phase;

        if (loop == _lastAttackLoop || phase < attackSpawnNormalizedTime)
        {
            return;
        }

        _lastAttackLoop = loop;
        ReplayPreview(false);
    }

    public void Configure(UnitData data, int resolvedStarLevel, GameObject resolvedSlashPrefab, string resolvedSlashAddress, Transform target)
    {
        unitData = data;
        starLevel = Mathf.Clamp(resolvedStarLevel, 1, 3);
        slashPrefab = resolvedSlashPrefab;
        slashPrefabAddress = resolvedSlashAddress ?? string.Empty;
        targetOverride = target;
        PullFromUnitData();
    }

    public void TriggerAttack()
    {
        if (_animator == null)
        {
            _animator = GetComponent<Animator>();
        }

        if (_animator == null || string.IsNullOrWhiteSpace(attackTriggerName))
        {
            return;
        }

        _animator.ResetTrigger(attackTriggerName);
        _animator.SetTrigger(attackTriggerName);
    }

    public GameObject ReplayPreview(bool selectInstance)
    {
        if (restartExistingPreviewInstance && _lastPreviewInstance != null)
        {
            BasicAttackVfxRuntimeUtility.RestartParticles(_lastPreviewInstance, primaryRendererFlip, playbackSpeed);

#if UNITY_EDITOR
            if (selectInstance)
            {
                Selection.activeGameObject = _lastPreviewInstance;
            }
#endif

            return _lastPreviewInstance;
        }

        return SpawnPreview(selectInstance);
    }

    public GameObject SpawnPreview(bool selectInstance)
    {
        if (slashPrefab == null)
        {
            Debug.LogWarning($"[AttackSlashTuningPreview] Slash prefab is missing on {name}.");
            return null;
        }

        if (replacePreviousPreview)
        {
            DestroyPreviewInstance(_lastPreviewInstance);
            _lastPreviewInstance = null;
        }

        Transform origin = ResolveSpawnOrigin();
        Vector3 direction = ResolveDirection(origin);
        Quaternion attackRotation = BasicAttackVfxRuntimeUtility.ResolveAttackRotation(transform, direction, rotationMode);
        Vector3 position = origin.position + attackRotation * localPositionOffset;
        Quaternion rotation = attackRotation * Quaternion.Euler(rotationOffsetEuler);
        GameObject instance = InstantiatePreviewObject(position, rotation);
        if (instance == null)
        {
            return null;
        }

        instance.name = $"{slashPrefab.name}_Preview_{name}";
        instance.transform.localScale = slashPrefab.transform.localScale * Mathf.Max(0.01f, scaleMultiplier);

        var marker = instance.GetComponent<AttackSlashTuningPreviewInstance>();
        if (marker == null)
        {
            marker = instance.AddComponent<AttackSlashTuningPreviewInstance>();
        }

        marker.Initialize(this, origin, attackRotation, slashPrefab.transform.localScale);
        BasicAttackVfxRuntimeUtility.RestartParticles(instance, primaryRendererFlip, playbackSpeed);
        _lastPreviewInstance = instance;

        if (autoDestroyPreviewInstances && Application.isPlaying)
        {
            Destroy(instance, previewLifetimeSeconds);
        }

#if UNITY_EDITOR
        if (selectInstance)
        {
            Selection.activeGameObject = instance;
        }
#endif

        return instance;
    }

    public void CaptureFromPreviewInstance(AttackSlashTuningPreviewInstance instance)
    {
        if (instance == null || instance.transform == null)
        {
            return;
        }

        Transform origin = instance.Origin != null ? instance.Origin : ResolveSpawnOrigin();
        Quaternion inverseAttackRotation = Quaternion.Inverse(instance.AttackRotation);
        localPositionOffset = inverseAttackRotation * (instance.transform.position - origin.position);
        rotationOffsetEuler = (inverseAttackRotation * instance.transform.rotation).eulerAngles;
        scaleMultiplier = ResolveUniformScaleMultiplier(instance.transform.localScale, instance.PrefabBaseScale);

#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    public void PullFromUnitData()
    {
        BasicAttackVfxConfig config = ResolveConfig();
        if (config == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.prefabKey))
        {
            slashPrefabAddress = config.prefabKey;
        }

#if UNITY_EDITOR
        GameObject resolvedSlashPrefab = ResolveEditorAddressableGameObject(slashPrefabAddress);
        if (resolvedSlashPrefab != null)
        {
            slashPrefab = resolvedSlashPrefab;
        }
#endif

        spawnOrigin = !string.IsNullOrWhiteSpace(config.spawnOriginPath)
            ? transform.Find(config.spawnOriginPath)
            : null;
        localPositionOffset = config.localPositionOffset;
        rotationOffsetEuler = config.rotationOffsetEuler;
        rotationMode = config.rotationMode;
        scaleMultiplier = config.scaleMultiplier > 0f ? config.scaleMultiplier : 1f;
        playbackSpeed = config.playbackSpeed > 0f ? config.playbackSpeed : 1f;
        attackSpawnNormalizedTime = Mathf.Clamp(config.spawnNormalizedTime, 0f, 0.95f);
        primaryRendererFlip = config.primaryRendererFlip;
    }

    public void CopySettingsToUnitData(bool saveAsset)
    {
        if (unitData == null)
        {
            Debug.LogWarning($"[AttackSlashTuningPreview] UnitData is missing on {name}.");
            return;
        }

#if UNITY_EDITOR
        Undo.RecordObject(unitData, "Save Attack Slash Tuning");
#endif

        unitData.EnsureBasicAttackVfxConfigArray();
        int first = applyToAllStarLevels ? 0 : Mathf.Clamp(starLevel - 1, 0, 2);
        int last = applyToAllStarLevels ? 2 : first;
        for (int i = first; i <= last; i++)
        {
            BasicAttackVfxConfig config = unitData.basicAttackVfxConfigsByStarLevel[i] ?? BasicAttackVfxConfig.CreateDefault(slashPrefabAddress);
            WriteConfig(config);
            unitData.basicAttackVfxConfigsByStarLevel[i] = config;
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(unitData);
        if (saveAsset)
        {
            AssetDatabase.SaveAssets();
        }
#endif

        Debug.Log($"[AttackSlashTuningPreview] Saved slash tuning to {unitData.name}. stars={(applyToAllStarLevels ? "all" : starLevel.ToString())}");
    }

    private BasicAttackVfxConfig ResolveConfig()
    {
        if (unitData == null)
        {
            return null;
        }

        return unitData.GetBasicAttackVfxConfig(starLevel);
    }

    private void WriteConfig(BasicAttackVfxConfig config)
    {
        config.prefabKey = slashPrefabAddress ?? string.Empty;
        config.spawnOriginPath = spawnOrigin != null ? GetRelativePath(transform, spawnOrigin) : string.Empty;
        config.localPositionOffset = localPositionOffset;
        config.rotationOffsetEuler = rotationOffsetEuler;
        config.rotationMode = rotationMode;
        config.scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
        config.spawnNormalizedTime = Mathf.Clamp(attackSpawnNormalizedTime, 0f, 0.95f);
        config.playbackSpeed = Mathf.Max(0.01f, playbackSpeed);
        config.primaryRendererFlip = primaryRendererFlip;
        config.calibrationQuality = 1f;
        config.calibratedAttackClipGuid = string.Empty;
        config.calibrationSource = ManualTuningSource;
    }

    private bool TryGetAttackState(out AnimatorStateInfo attackState)
    {
        attackState = _animator.GetCurrentAnimatorStateInfo(0);
        if (MatchesAttackState(attackState))
        {
            return true;
        }

        if (_animator.IsInTransition(0))
        {
            AnimatorStateInfo nextState = _animator.GetNextAnimatorStateInfo(0);
            if (MatchesAttackState(nextState))
            {
                attackState = nextState;
                return true;
            }
        }

        return false;
    }

    private bool MatchesAttackState(AnimatorStateInfo state)
    {
        if (string.IsNullOrWhiteSpace(attackStateName))
        {
            return state.IsTag("Attack");
        }

        return state.IsName(attackStateName) || state.IsName($"Base Layer.{attackStateName}") || state.IsTag("Attack");
    }

    private Transform ResolveSpawnOrigin()
    {
        return spawnOrigin != null ? spawnOrigin : transform;
    }

    private Vector3 ResolveDirection(Transform origin)
    {
        Vector3 direction = targetOverride != null ? targetOverride.position - origin.position : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 1e-6f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
    }

    private GameObject InstantiatePreviewObject(Vector3 position, Quaternion rotation)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            GameObject editorInstance = PrefabUtility.InstantiatePrefab(slashPrefab) as GameObject;
            if (editorInstance != null)
            {
                Undo.RegisterCreatedObjectUndo(editorInstance, "Spawn Attack Slash Preview");
                editorInstance.transform.SetPositionAndRotation(position, rotation);
            }

            return editorInstance;
        }
#endif

        return Instantiate(slashPrefab, position, rotation);
    }

    private static void DestroyPreviewInstance(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(instance);
            return;
        }
#endif

        Destroy(instance);
    }

    private static float ResolveUniformScaleMultiplier(Vector3 instanceScale, Vector3 prefabScale)
    {
        float x = Mathf.Abs(prefabScale.x) > 1e-6f ? instanceScale.x / prefabScale.x : 0f;
        float y = Mathf.Abs(prefabScale.y) > 1e-6f ? instanceScale.y / prefabScale.y : 0f;
        float z = Mathf.Abs(prefabScale.z) > 1e-6f ? instanceScale.z / prefabScale.z : 0f;
        float sum = 0f;
        int count = 0;
        if (Mathf.Abs(x) > 1e-6f)
        {
            sum += x;
            count++;
        }

        if (Mathf.Abs(y) > 1e-6f)
        {
            sum += y;
            count++;
        }

        if (Mathf.Abs(z) > 1e-6f)
        {
            sum += z;
            count++;
        }

        return Mathf.Max(0.01f, count > 0 ? sum / count : 1f);
    }

    private static string GetRelativePath(Transform root, Transform child)
    {
        if (root == null || child == null || root == child)
        {
            return string.Empty;
        }

        string path = child.name;
        Transform cursor = child.parent;
        while (cursor != null && cursor != root)
        {
            path = $"{cursor.name}/{path}";
            cursor = cursor.parent;
        }

        return cursor == root ? path : string.Empty;
    }

#if UNITY_EDITOR
    private static GameObject ResolveEditorAddressableGameObject(string address)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        foreach (AddressableAssetGroup group in settings.groups)
        {
            if (group == null)
            {
                continue;
            }

            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry == null || entry.address != address)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }
        }

        return null;
    }
#endif
}
