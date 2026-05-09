using UnityEngine;
using Fusion;

public class AIPlayerController : MonoBehaviour
{
    private PlayerManager _playerManager;
    private int _registeredPlayerId = -1;
    private float _nextDecisionAt;
    private MdfBotProfile _profile = MdfBotProfile.Create();

    private PrepareDecisionPolicy _prepareDecisionPolicy;
    private BattleDecisionPolicy _battleDecisionPolicy;
    private ServerAiCommandEmitter _serverAiCommandEmitter;

    public void Initialize(PlayerManager playerManager, CommandProcessor commandProcessor, MdfBotProfile profile = null)
    {
        if (playerManager == null || commandProcessor == null)
        {
            Debug.LogWarning("[AIPlayerController] Initialize failed: missing references.");
            return;
        }

        if (_registeredPlayerId >= 0)
        {
            ComponentRegistry.Unregister<AIPlayerController>(_registeredPlayerId.ToString());
        }

        _playerManager = playerManager;
        _profile = profile ?? MdfBotProfile.ServerAiDefault(playerManager.playerId);
        _prepareDecisionPolicy = new PrepareDecisionPolicy(_profile);
        _battleDecisionPolicy = new BattleDecisionPolicy();
        _serverAiCommandEmitter = new ServerAiCommandEmitter(GameManagers.Instance, playerManager, "server_ai_controller");
        _nextDecisionAt = 0f;

        _registeredPlayerId = playerManager.playerId;
        ComponentRegistry.Unregister<AIPlayerController>(_registeredPlayerId.ToString());
        ComponentRegistry.Register(_registeredPlayerId.ToString(), this);
    }

    private void OnDestroy()
    {
        if (_registeredPlayerId >= 0)
        {
            ComponentRegistry.Unregister<AIPlayerController>(_registeredPlayerId.ToString());
            _registeredPlayerId = -1;
        }
    }

    private void Update()
    {
        if (_playerManager == null || GameManagers.Instance == null)
        {
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var mpOptions = MPTestCommandLine.GetOptions();
        if (mpOptions.Enabled && mpOptions.FreezeGameFlow)
        {
            return;
        }
#endif

        var migrationHandler = HostMigrationHandler.Instance;
        if (migrationHandler != null && migrationHandler.IsMigrating && !migrationHandler.IsAiTakeoverReady)
        {
            return;
        }

        if (Time.realtimeSinceStartup < _nextDecisionAt)
        {
            return;
        }

        _nextDecisionAt = Time.realtimeSinceStartup + _profile.DecisionIntervalSeconds;

        var gm = GameManagers.Instance;
        if (gm.Object == null || !gm.Object.HasStateAuthority)
        {
            return;
        }

        if (gm.IsSequenceTransitioning)
        {
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (mpOptions.Enabled &&
            mpOptions.Scenario == "human_bot_prepare_progression" &&
            _playerManager.Object != null &&
            _playerManager.Object.InputAuthority == PlayerRef.None)
        {
            return;
        }
#endif

        switch (gm.GetGameState())
        {
            case GameManagers.GameState.Prepare:
                TryRunPolicy(_prepareDecisionPolicy, gm);
                break;
            case GameManagers.GameState.Battle1:
            case GameManagers.GameState.Battle2:
                TryRunPolicy(_battleDecisionPolicy, gm);
                break;
        }
    }

    private void TryRunPolicy(IMdfDecisionPolicy policy, GameManagers gm)
    {
        if (policy == null || gm == null || _playerManager == null)
        {
            return;
        }

        _serverAiCommandEmitter = new ServerAiCommandEmitter(gm, _playerManager, "server_ai_controller");
        var context = MdfDecisionContext.Create(
            gm,
            _playerManager,
            CommandExecutionScope.ServerAuthorityOnly,
            _profile.Persona,
            isHumanBot: false,
            isServerAi: true,
            isTestAutomation: false);

        if (!policy.TryChoose(context, out var decision) || decision == null || !decision.HasCommandPayload)
        {
            return;
        }

        _serverAiCommandEmitter.TryEmit(decision, out _);
    }
}
