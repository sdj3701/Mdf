using UnityEngine;

public sealed class MdfBotProfile
{
    public const string DefaultPersona = "balanced";
    public const float DefaultDecisionIntervalSeconds = 0.7f;

    public string Persona { get; private set; }
    public int Seed { get; private set; }
    public bool PreferScrollAugment { get; private set; }
    public float DecisionIntervalSeconds { get; private set; }

    private MdfBotProfile(string persona, int seed, bool preferScrollAugment, float decisionIntervalSeconds)
    {
        Persona = NormalizePersona(persona);
        Seed = seed;
        PreferScrollAugment = preferScrollAugment;
        DecisionIntervalSeconds = Mathf.Max(0.05f, decisionIntervalSeconds);
    }

    public static MdfBotProfile Create(
        string persona = DefaultPersona,
        int seed = 0,
        bool preferScrollAugment = false,
        float decisionIntervalSeconds = DefaultDecisionIntervalSeconds)
    {
        return new MdfBotProfile(persona, seed, preferScrollAugment, decisionIntervalSeconds);
    }

    public static MdfBotProfile ServerAiDefault(int playerId)
    {
        return Create(DefaultPersona, 0, false, DefaultDecisionIntervalSeconds);
    }

    private static string NormalizePersona(string persona)
    {
        if (string.IsNullOrWhiteSpace(persona))
        {
            return DefaultPersona;
        }

        switch (persona.Trim().ToLowerInvariant())
        {
            case "maze":
            case "shop":
            case "unit":
            case "passive":
            case "balanced":
                return persona.Trim().ToLowerInvariant();
            default:
                return DefaultPersona;
        }
    }
}
