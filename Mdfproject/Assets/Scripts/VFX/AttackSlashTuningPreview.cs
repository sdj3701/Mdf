#if UNITY_EDITOR
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
    private const string TuningRootName = "AttackSlashTuning_Root";
    private const string PreviewRootName = "AttackSlashEffectRoot";

    [Header("Unit")]
    [SerializeField] private UnitData unitData;
    [SerializeField, Range(1, 3)] private int starLevel = 1;
    [SerializeField] private bool applyToAllStarLevels = true;

    [Header("VFX")]
    [SerializeField] private GameObject slashPrefab;
    [SerializeField] private string slashPrefabAddress = "VFX_AttackSlash_SwordSlash5";
    [SerializeField] private Transform targetOverride;
    [SerializeField] private Vector3 localPositionOffset = new Vector3(0f, 0.6f, 0.75f);
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;
    [SerializeField] private BasicAttackVfxRotationMode rotationMode = BasicAttackVfxRotationMode.TargetFacing;
    [SerializeField] private float scaleMultiplier = 1f;
    [SerializeField] private float playbackSpeed = 1f;
    [SerializeField] private float vfxPlaybackSpeedCap = BasicAttackVfxConfig.DefaultPlaybackSpeedCap;
    [SerializeField] private float minimumVisibleSeconds = BasicAttackVfxConfig.DefaultMinimumVisibleSeconds;
    [SerializeField] private Vector3 primaryRendererFlip = Vector3.zero;
    [SerializeField] private Transform previewRoot;

    [Header("Animation Preview")]
    [SerializeField] private float previewFinalAttackSpeed = 1f;
    [SerializeField] private bool spawnWhenAnimatorAttackStatePlays = true;
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private string attackTriggerName = "AttackTrigger";
    [SerializeField, Range(0f, 0.95f)] private float attackSpawnNormalizedTime = 0.2f;
    [SerializeField] private bool autoDestroyPreviewInstances = true;
    [SerializeField] private float previewLifetimeSeconds = 2.1f;

    [Header("Loop Preview")]
    [SerializeField] private bool loopAttackAndVfx = true;
    [SerializeField] private bool useFinalAttackSpeedForLoopInterval = true;
    [SerializeField] private float loopIntervalSeconds = 1.25f;

    [Header("Preview Runtime Match")]
    [SerializeField] private float previewAnimationSpeedCap = 3f;
    [SerializeField] private float fallbackAttackClipDuration = 1f;

    private Animator _animator;
    private int _lastAttackStateHash;
    private int _lastAttackLoop = int.MinValue;
    private float _lastAttackPhase = -1f;
    private bool _wasInAttackState;
    private float _nextLoopPreviewTime;
    private float _nextPreviewAnimationTime;

    public UnitData UnitData => unitData;
    public int StarLevel => starLevel;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void OnValidate()
    {
        starLevel = Mathf.Clamp(starLevel, 1, 3);
        scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
        playbackSpeed = Mathf.Max(0.01f, playbackSpeed);
        vfxPlaybackSpeedCap = Mathf.Max(0.01f, vfxPlaybackSpeedCap);
        minimumVisibleSeconds = Mathf.Max(0f, minimumVisibleSeconds);
        previewFinalAttackSpeed = Mathf.Max(0.01f, previewFinalAttackSpeed);
        attackSpawnNormalizedTime = Mathf.Clamp(attackSpawnNormalizedTime, 0f, 0.95f);
        previewLifetimeSeconds = Mathf.Max(0.05f, previewLifetimeSeconds);
        loopIntervalSeconds = Mathf.Max(0.05f, loopIntervalSeconds);
        previewAnimationSpeedCap = Mathf.Max(0.01f, previewAnimationSpeedCap);
        fallbackAttackClipDuration = Mathf.Max(0.01f, fallbackAttackClipDuration);
    }

    private void OnEnable()
    {
        PullFromUnitData();
        _nextLoopPreviewTime = 0f;
        _nextPreviewAnimationTime = 0f;
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
            _nextLoopPreviewTime = Time.time + ResolvePreviewAttackIntervalSeconds();
            bool triggeredAnimation = TryTriggerPreviewAttackAnimation();

            if (!ShouldSpawnPreviewFromAnimatorState() ||
                (!triggeredAnimation && (_animator == null || !_animator.isActiveAndEnabled || _animator.layerCount <= 0)))
            {
                ReplayPreview(false);
            }
        }

        if (!ShouldSpawnPreviewFromAnimatorState())
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

        ApplyPreviewAnimatorSpeed();
        _animator.ResetTrigger(attackTriggerName);
        _animator.SetTrigger(attackTriggerName);
    }

    public GameObject ReplayPreview(bool selectInstance)
    {
        return SpawnPreview(selectInstance);
    }

    public GameObject SpawnPreview(bool selectInstance)
    {
        if (slashPrefab == null)
        {
            Debug.LogWarning($"[AttackSlashTuningPreview] Slash prefab is missing on {name}.");
            return null;
        }

        TryResolvePreviewWorldPose(out _, out _, out Vector3 position, out Quaternion rotation);
        GameObject instance = InstantiatePreviewObject(position, rotation);
        if (instance == null)
        {
            return null;
        }

        instance.name = $"{slashPrefab.name}_PreviewSample_{name}";
        instance.transform.localScale = slashPrefab.transform.localScale * Mathf.Max(0.01f, scaleMultiplier);

        BasicAttackVfxRuntimeUtility.RestartParticles(instance, primaryRendererFlip, ResolvePreviewVfxPlaybackSpeed());

        if (autoDestroyPreviewInstances && Application.isPlaying)
        {
            Destroy(instance, ResolvePreviewVfxLifetimeSeconds());
        }

#if UNITY_EDITOR
        if (selectInstance)
        {
            Selection.activeGameObject = instance;
        }
#endif

        return instance;
    }

    public bool TryResolvePreviewWorldPose(out Transform origin, out Quaternion attackRotation, out Vector3 position, out Quaternion rotation)
    {
        origin = transform;
        Vector3 direction = ResolveDirection(origin);
        attackRotation = BasicAttackVfxRuntimeUtility.ResolveAttackRotation(transform, direction, rotationMode);
        position = origin.position + attackRotation * localPositionOffset;
        rotation = attackRotation * Quaternion.Euler(rotationOffsetEuler);
        return origin != null;
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

        localPositionOffset = config.localPositionOffset;
        rotationOffsetEuler = config.rotationOffsetEuler;
        rotationMode = config.rotationMode;
        scaleMultiplier = config.scaleMultiplier > 0f ? config.scaleMultiplier : 1f;
        playbackSpeed = config.playbackSpeed > 0f ? config.playbackSpeed : 1f;
        vfxPlaybackSpeedCap = config.ResolvePlaybackSpeedCap();
        minimumVisibleSeconds = config.ResolveMinimumVisibleSeconds();
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
        BasicAttackVfxProfile profile = unitData.basicAttackVfxProfile;
        if (profile == null)
        {
            Debug.LogWarning($"[AttackSlashTuningPreview] BasicAttackVfxProfile is missing on {unitData.name}.");
            return;
        }

        Undo.RecordObject(profile, "Save Attack Slash Tuning");
#else
        BasicAttackVfxProfile profile = unitData.basicAttackVfxProfile;
        if (profile == null)
        {
            Debug.LogWarning($"[AttackSlashTuningPreview] BasicAttackVfxProfile is missing on {unitData.name}.");
            return;
        }
#endif

        profile.EnsureConfigs();
        int first = applyToAllStarLevels ? 0 : Mathf.Clamp(starLevel - 1, 0, 2);
        int last = applyToAllStarLevels ? 2 : first;
        for (int i = first; i <= last; i++)
        {
            BasicAttackVfxConfig config = profile.slashConfigsByStarLevel[i] ?? BasicAttackVfxConfig.CreateDefault(slashPrefabAddress);
            WriteConfig(config);
            profile.slashConfigsByStarLevel[i] = config;
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(profile);
        if (saveAsset)
        {
            AssetDatabase.SaveAssets();
        }
#endif

        Debug.Log($"[AttackSlashTuningPreview] Saved slash tuning to {profile.name}. unit={unitData.name}, stars={(applyToAllStarLevels ? "all" : starLevel.ToString())}");
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
        config.localPositionOffset = localPositionOffset;
        config.rotationOffsetEuler = rotationOffsetEuler;
        config.rotationMode = rotationMode;
        config.scaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
        config.spawnNormalizedTime = Mathf.Clamp(attackSpawnNormalizedTime, 0f, 0.95f);
        config.playbackSpeed = Mathf.Max(0.01f, playbackSpeed);
        config.playbackSpeedCap = Mathf.Max(0.01f, vfxPlaybackSpeedCap);
        config.minimumVisibleSeconds = Mathf.Max(0f, minimumVisibleSeconds);
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

    private bool TryTriggerPreviewAttackAnimation()
    {
        if (_animator == null || !_animator.isActiveAndEnabled || _animator.layerCount <= 0)
        {
            return false;
        }

        float cappedRate = ResolvePreviewCappedAnimationRate();
        if (Time.time < _nextPreviewAnimationTime)
        {
            return false;
        }

        _nextPreviewAnimationTime = Time.time + 1f / cappedRate;
        TriggerAttack();
        return true;
    }

    private bool ShouldSpawnPreviewFromAnimatorState()
    {
        return spawnWhenAnimatorAttackStatePlays;
    }

    public float ResolvePreviewAttackIntervalSeconds()
    {
        return ResolvePreviewPresentationIntervalSeconds();
    }

    public float ResolvePreviewDamageIntervalSeconds()
    {
        return 1f / ResolvePreviewFinalAttackSpeed();
    }

    public float ResolvePreviewPresentationIntervalSeconds()
    {
        if (!useFinalAttackSpeedForLoopInterval)
        {
            return Mathf.Max(0.05f, loopIntervalSeconds);
        }

        return 1f / ResolvePreviewCappedAnimationRate();
    }

    public float ResolvePreviewAnimationPlaybackSpeed()
    {
        float animRate = ResolvePreviewCappedAnimationRate();
        float clipDuration = ResolveAttackClipDuration();
        float speed = clipDuration > 0f ? clipDuration * animRate : animRate;
        return Mathf.Max(0.01f, speed);
    }

    public float ResolvePreviewVfxPlaybackSpeed()
    {
        return BasicAttackVfxRuntimeUtility.ResolvePlaybackSpeed(playbackSpeed, ResolvePreviewAnimationPlaybackSpeed(), vfxPlaybackSpeedCap);
    }

    public float ResolvePreviewVfxLifetimeSeconds()
    {
        return BasicAttackVfxRuntimeUtility.ResolveLifetimeSeconds(previewLifetimeSeconds, ResolvePreviewVfxPlaybackSpeed(), minimumVisibleSeconds);
    }

    private float ResolvePreviewFinalAttackSpeed()
    {
        return Mathf.Max(0.01f, previewFinalAttackSpeed);
    }

    private float ResolvePreviewCappedAnimationRate()
    {
        return Mathf.Min(ResolvePreviewFinalAttackSpeed(), Mathf.Max(0.01f, previewAnimationSpeedCap));
    }

    private void ApplyPreviewAnimatorSpeed()
    {
        if (_animator != null)
        {
            _animator.speed = ResolvePreviewAnimationPlaybackSpeed();
        }
    }

    private float ResolveAttackClipDuration()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
            if (clips != null)
            {
                foreach (AnimationClip clip in clips)
                {
                    if (clip == null)
                    {
                        continue;
                    }

                    if (IsAttackClipName(clip))
                    {
                        return Mathf.Max(0.01f, clip.length);
                    }
                }
            }
        }

        return Mathf.Max(0.01f, fallbackAttackClipDuration);
    }

    private static bool IsAttackClipName(AnimationClip clip)
    {
        if (clip == null || string.IsNullOrWhiteSpace(clip.name))
        {
            return false;
        }

        return clip.name.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0
            || clip.name.IndexOf("atk", System.StringComparison.OrdinalIgnoreCase) >= 0;
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
            Transform resolvedPreviewRoot = ResolvePreviewRoot();
            GameObject editorInstance = PrefabUtility.InstantiatePrefab(slashPrefab, resolvedPreviewRoot) as GameObject;
            if (editorInstance != null)
            {
                Undo.RegisterCreatedObjectUndo(editorInstance, "Spawn Attack Slash Preview");
                editorInstance.transform.SetPositionAndRotation(position, rotation);
            }

            return editorInstance;
        }
#endif

        return Instantiate(slashPrefab, position, rotation, ResolvePreviewRoot());
    }

    private Transform ResolvePreviewRoot()
    {
        if (previewRoot != null)
        {
            return previewRoot;
        }

        Transform parent = FindAncestor(TuningRootName);
        if (parent == null)
        {
            GameObject namedRoot = GameObject.Find(TuningRootName);
            parent = namedRoot != null ? namedRoot.transform : transform.parent;
        }

        if (parent == null)
        {
            return null;
        }

        Transform existing = parent.Find(PreviewRootName);
        if (existing != null)
        {
            previewRoot = existing;
            return previewRoot;
        }

        GameObject root = new GameObject(PreviewRootName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.RegisterCreatedObjectUndo(root, "Create Attack Slash Preview Root");
        }
#endif
        root.transform.SetParent(parent, false);
        previewRoot = root.transform;
        return previewRoot;
    }

    private Transform FindAncestor(string ancestorName)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name == ancestorName)
            {
                return current;
            }

            current = current.parent;
        }

        return null;
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
#endif
