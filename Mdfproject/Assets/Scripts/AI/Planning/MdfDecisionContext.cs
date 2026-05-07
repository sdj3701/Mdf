using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public sealed class MdfDecisionContext
{
    public GameManagers GameManagers { get; private set; }
    public PlayerManager Actor { get; private set; }
    public PlayerManager Opponent { get; private set; }
    public FieldManager OwnField { get; private set; }
    public FieldManager OpponentField { get; private set; }
    public CommandExecutionScope PreferredScope { get; private set; }
    public string Persona { get; private set; }
    public bool IsHumanBot { get; private set; }
    public bool IsServerAi { get; private set; }
    public bool IsTestAutomation { get; private set; }
    public object Observed { get; private set; }

    public int PlayerId => Actor != null ? Actor.playerId : -1;
    public GameManagers.GameState GameState =>
        GameManagers != null ? GameManagers.GetGameState() : GameManagers.GameState.Setup;
    public bool IsBattlePhase =>
        GameState == GameManagers.GameState.Battle1 ||
        GameState == GameManagers.GameState.Battle2;
    public bool IsCurrentBattleAttacker => Actor != null && BattleCommandValidator.IsCurrentBattleAttacker(Actor);
    public bool IsCurrentBattleDefender => Actor != null && BattleCommandValidator.IsCurrentBattleDefender(Actor);

    private MdfDecisionContext()
    {
    }

    public static MdfDecisionContext Create(
        GameManagers gameManagers,
        PlayerManager actor,
        CommandExecutionScope preferredScope,
        string persona = "balanced",
        bool isHumanBot = false,
        bool isServerAi = false,
        bool isTestAutomation = false)
    {
        PlayerManager opponent = ResolveOpponent(gameManagers, actor);
        var context = new MdfDecisionContext
        {
            GameManagers = gameManagers,
            Actor = actor,
            Opponent = opponent,
            OwnField = actor != null ? actor.fieldManager : null,
            OpponentField = opponent != null ? opponent.fieldManager : null,
            PreferredScope = preferredScope,
            Persona = string.IsNullOrWhiteSpace(persona) ? "balanced" : persona.Trim().ToLowerInvariant(),
            IsHumanBot = isHumanBot,
            IsServerAi = isServerAi,
            IsTestAutomation = isTestAutomation
        };
        context.Observed = BuildObserved(context);
        return context;
    }

    private static PlayerManager ResolveOpponent(GameManagers gameManagers, PlayerManager actor)
    {
        if (gameManagers == null || actor == null)
        {
            return null;
        }

        if (gameManagers.TryGetBattleOpponentSnapshot(actor.playerId, out int opponentId))
        {
            return gameManagers.GetPlayer(opponentId);
        }

        return null;
    }

    private static object BuildObserved(MdfDecisionContext context)
    {
        var player = context != null ? context.Actor : null;
        if (player == null)
        {
            return null;
        }

        var shop = CaptureShopObserved(player);
        return new
        {
            gold = SafeInt(player.GetGold, 0),
            wallCount = SafeInt(player.GetWallCount, 0),
            shopRevision = shop.revision,
            shopItemsHash = shop.itemsHash,
            wallHash = player.fieldManager != null
                ? HashStableString(SafeWallSignature(player.fieldManager))
                : "unknown",
            presentedAugmentsHash = CapturePresentedAugmentHash(player),
            attackMonsterPoolHash = CaptureAttackMonsterPoolHash(player),
            ownedScrollsHash = CaptureOwnedScrollsHash(player),
            battleRole = context.IsCurrentBattleAttacker ? "attacker" : context.IsCurrentBattleDefender ? "defender" : "none"
        };
    }

    private static (int revision, string itemsHash) CaptureShopObserved(PlayerManager player)
    {
        string[] unitKeys;
        int[] starLevels;
        bool[] soldFlags;
        int revision;
        int round;
        if (player.TryGetShopSnapshot(out unitKeys, out starLevels, out soldFlags, out revision, out round))
        {
            var parts = new List<string>();
            for (int i = 0; i < unitKeys.Length; i++)
            {
                int star = i < starLevels.Length ? starLevels[i] : 0;
                bool sold = i < soldFlags.Length && soldFlags[i];
                parts.Add($"{i}:{unitKeys[i]}:{star}:{sold}");
            }

            return (revision, HashStableParts(parts));
        }

        return (0, "unknown");
    }

    private static string CapturePresentedAugmentHash(PlayerManager player)
    {
        var snapshotNames = player.GetPresentedAugmentSnapshotNames();
        if (snapshotNames != null && snapshotNames.Length > 0)
        {
            return HashStableParts(snapshotNames);
        }

        return "unknown";
    }

    private static string CaptureAttackMonsterPoolHash(PlayerManager player)
    {
        if (player.AttackMonsterPool == null || player.AttackMonsterPool.Count == 0)
        {
            return HashStableParts(new[] { $"revision={player.AttackMonsterPoolRevision}", "pool=empty" });
        }

        return HashStableParts(player.AttackMonsterPool.Select((entry, index) =>
        {
            string key = entry != null && entry.MonsterData != null ? entry.MonsterData.name : "null";
            int count = entry != null ? entry.RemainingCount : 0;
            bool boss = entry != null && entry.IsBoss;
            return $"{index}:{key}:count={count}:boss={boss}:revision={player.AttackMonsterPoolRevision}";
        }));
    }

    private static string CaptureOwnedScrollsHash(PlayerManager player)
    {
        var scrolls = player.OwnedScrolls;
        if (scrolls == null || scrolls.Count == 0)
        {
            return HashStableParts(new[] { $"revision={player.OwnedMagicScrollRevision}", "scrolls=empty" });
        }

        return HashStableParts(scrolls.Select((scroll, index) =>
        {
            string key = scroll != null ? scroll.name : "null";
            return $"{index}:{key}:revision={player.OwnedMagicScrollRevision}";
        }));
    }

    private static string SafeWallSignature(FieldManager field)
    {
        if (field == null)
        {
            return "no-field";
        }

        try
        {
            return field.BuildWallCellHash();
        }
        catch
        {
            return "wall-signature-error";
        }
    }

    private static int SafeInt(Func<int> getter, int fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }

    private static string HashStableParts(IEnumerable<string> parts)
    {
        return HashStableString(string.Join("|", (parts ?? Array.Empty<string>()).OrderBy(part => part, StringComparer.Ordinal)));
    }

    private static string HashStableString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "unknown";
        }

        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder("sha256:");
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
