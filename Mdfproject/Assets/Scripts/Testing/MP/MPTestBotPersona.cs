#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

public enum MPTestBotPersona
{
    Balanced,
    Maze,
    Shop,
    Unit,
    Passive
}

public static class MPTestBotPersonaParser
{
    public static MPTestBotPersona Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MPTestBotPersona.Balanced;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "maze":
                return MPTestBotPersona.Maze;
            case "shop":
                return MPTestBotPersona.Shop;
            case "unit":
                return MPTestBotPersona.Unit;
            case "passive":
                return MPTestBotPersona.Passive;
            case "balanced":
            default:
                return MPTestBotPersona.Balanced;
        }
    }

    public static string ToCliValue(this MPTestBotPersona persona)
    {
        return persona.ToString().ToLowerInvariant();
    }
}
#endif
