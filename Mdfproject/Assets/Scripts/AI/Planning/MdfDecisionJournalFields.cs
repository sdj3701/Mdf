using System.Collections.Generic;

public static class MdfDecisionJournalFields
{
    public static Dictionary<string, object> Build(
        MdfDecision decision,
        PlayerManager actor,
        string emitter,
        BattleCommandResult result)
    {
        var fields = decision != null && decision.JournalFields != null
            ? new Dictionary<string, object>(decision.JournalFields)
            : new Dictionary<string, object>();

        fields["playerId"] = decision != null ? decision.PlayerId : actor != null ? actor.playerId : -1;
        fields["persona"] = decision != null ? decision.Persona : "unknown";
        fields["gameState"] = decision != null ? decision.GameState : "unknown";
        fields["round"] = decision != null ? decision.Round : 0;
        fields["decisionType"] = decision != null ? decision.Kind.ToString() : "unknown";
        fields["commandType"] = decision != null ? decision.CommandTypeName : "unknown";
        fields["score"] = decision != null ? decision.Score.ToString("F1") : "0.0";
        fields["target"] = decision != null ? decision.Target ?? "none" : "none";
        fields["emitter"] = string.IsNullOrWhiteSpace(emitter) ? "unknown" : emitter;
        fields["result"] = result.Success ? "success" : result.ErrorCode;
        return fields;
    }
}
