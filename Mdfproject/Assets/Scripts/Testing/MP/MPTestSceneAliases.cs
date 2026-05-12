using System;
using System.Collections.Generic;
using GameCore.Enums;

public static class MPTestSceneAliases
{
    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "Title", SceneDefine.Title },
        { SceneDefine.Title, SceneDefine.Title },
        { "MatchingLobby", SceneDefine.MatchingLobby },
        { "TestMatching", SceneDefine.MatchingLobby },
        { SceneDefine.MatchingLobby, SceneDefine.MatchingLobby },
        { "JoinLobby", SceneDefine.JoinLobby },
        { SceneDefine.JoinLobby, SceneDefine.JoinLobby },
        { "Game", SceneDefine.Game },
        { SceneDefine.Game, SceneDefine.Game },
    };

    public static string Normalize(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return string.Empty;
        }

        return Aliases.TryGetValue(sceneName, out string normalized) ? normalized : sceneName;
    }

    public static bool Matches(string actual, string expected)
    {
        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal);
    }
}
