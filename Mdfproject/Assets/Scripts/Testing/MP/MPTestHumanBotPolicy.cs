#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

[Obsolete("MPTestHumanBotDriver uses PrepareDecisionPolicy directly. This adapter exists only for legacy test callers.")]
public sealed class MPTestHumanBotPolicy
{
    private readonly PrepareDecisionPolicy _preparePolicy;
    private readonly string _persona;

    public MPTestHumanBotPolicy(MPTestBotPersona persona, int seed)
    {
        _persona = persona.ToCliValue();
        _preparePolicy = new PrepareDecisionPolicy(_persona, seed);
    }

    public bool TryChoose(PlayerManager player, out Decision decision)
    {
        decision = null;
        var gm = GameManagers.Instance;
        if (gm == null || player == null)
        {
            return false;
        }

        var context = MdfDecisionContext.Create(
            gm,
            player,
            CommandExecutionScope.ClientRequest,
            _persona,
            isHumanBot: true,
            isServerAi: false,
            isTestAutomation: true);

        bool hasDecision = _preparePolicy.TryChoose(context, out var mdfDecision);
        decision = Decision.FromMdfDecision(player, gm, mdfDecision);
        return hasDecision && mdfDecision != null && mdfDecision.HasCommandPayload;
    }

    public sealed class Decision
    {
        public ICommand Command;
        public string CommandType;
        public string Reason;
        public string Target;
        public int PlayerId;
        public string GameState;
        public int Round;
        public object Observed;

        public static Decision FromMdfDecision(PlayerManager player, GameManagers gm, MdfDecision decision)
        {
            return new Decision
            {
                Command = decision != null ? decision.Command : null,
                CommandType = decision != null ? decision.CommandTypeName : "Observe",
                Reason = decision != null ? decision.Reason : "no_decision",
                Target = decision != null ? decision.Target : null,
                PlayerId = decision != null ? decision.PlayerId : player != null ? player.playerId : -1,
                GameState = decision != null ? decision.GameState : gm != null ? gm.GetGameState().ToString() : "unknown",
                Round = decision != null ? decision.Round : gm != null ? gm.currentRound : 0,
                Observed = decision != null ? decision.Observed : null
            };
        }
    }
}
#endif
