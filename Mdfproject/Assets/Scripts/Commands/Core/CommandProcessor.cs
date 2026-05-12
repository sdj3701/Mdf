using System.Collections.Generic;
using System; 
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Linq;
using Fusion;

public class CommandProcessor
{
    // 1. 서버가 실행해야 할 커맨드들을 담는 큐 (네트워크로부터 수신)
    private Queue<ICommand> _commandQueue = new Queue<ICommand>();

    /// <summary>
    /// [수정됨] 클라이언트(AI, UI)가 커맨드 실행을 '요청'할 때 호출하는 메서드입니다.
    /// 이 메서드는 커맨드를 직렬화하고, 멀티플레이어 환경에서는 네트워크로 전송합니다.
    /// </summary>
    public void RequestCommandExecution(ICommand command)
    {
        // 1. 커맨드를 직렬화합니다.
        (CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams) = SerializeCommand(command);

        // 네트워크 세션이 활성 상태라면 Fusion 네트워크 경로로 전송합니다.
        if (GameManagers.Instance != null && GameManagers.Instance.Runner != null && GameManagers.Instance.Runner.IsRunning)
        {
            var gm = GameManagers.Instance;

            // 서버(호스트)라면 곧장 브로드캐스트 실행
            if (gm.Object != null && gm.Object.HasStateAuthority)
            {
                if (command is ActivateSkillCommand)
                {
                    command.Execute();
                    return;
                }

                ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
                gm.RPC_BroadcastCommandToClients(type, intParams, stringParams, vectorParams);
                return;
            }

            // 클라이언트라면 자신의 PlayerManager로 서버에 요청
            var lp = gm.localPlayer;
            if (lp == null || lp.Object == null || !lp.Object.HasInputAuthority)
            {
                // 폴백: AllPlayers에서 InputAuthority 보유 플레이어 탐색
                var resolved = gm.AllPlayers.FirstOrDefault(p => p != null && p.Object != null && p.Object.HasInputAuthority);
                if (resolved != null)
                {
                    lp = resolved;
                }
            }

            if (lp != null && lp.Object != null && lp.Object.HasInputAuthority)
            {
                if (gm.Runner != null && gm.Runner.IsRunning && !gm.Runner.IsServer)
                {
                    Debug.Log($"<color=#3399FF>[ClientFlow] Send RPC Request -> {type}</color>");
                }
                lp.RPC_RequestCommandToServer(type, intParams, stringParams, vectorParams);
                return;
            }

            return;
        }

        // 싱글플레이/비네트워크 폴백: 로컬에서 즉시 실행
        ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
    }

    /// <summary>
    /// [수정됨] 서버로부터 브로드캐스팅된 커맨드 데이터 또는 싱글플레이어용 데이터를 받아
    /// 역직렬화하고 실행 큐에 추가합니다.
    /// </summary>
    public async void ReceiveAndEnqueueCommand(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams)
    {
        var gm = GameManagers.Instance;
        bool isClient = gm != null && gm.Runner != null && gm.Runner.IsRunning && !gm.Runner.IsServer;
        if (isClient && (type == CommandType.MoveUnit || type == CommandType.SwapUnit))
        {
            Debug.Log($"<color=#3399FF>[ClientFlow] Enqueue {type}</color>");
        }
        ICommand command = await DeserializeCommand(type, intParams, stringParams, vectorParams);
        if (command != null)
        {
            EnqueueCommandFromServer(command);
        }
    }

    /// <summary>
    /// [신규] ICommand 객체를 네트워크로 전송 가능한 데이터로 직렬화합니다.
    /// </summary>
    private (CommandType, int[], string[], Vector3[]) SerializeCommand(ICommand command)
    {
        switch (command)
        {
            // ===== Player Action Commands =====
            case BuyUnitCommand cmd:
                return (CommandType.BuyUnit, new int[] { cmd.PlayerId, cmd.ShopSlotIndex }, Array.Empty<string>(), Array.Empty<Vector3>());
            case MoveUnitCommand cmd:
                return (CommandType.MoveUnit, new int[] { cmd.PlayerId }, Array.Empty<string>(), new Vector3[] { cmd.From, cmd.To });
            case SwapUnitCommand cmd:
                return (CommandType.SwapUnit, new int[] { cmd.PlayerId }, Array.Empty<string>(), new Vector3[] { cmd.PosA, cmd.PosB });
            case SellUnitCommand cmd:
                return (CommandType.SellUnit, new int[] { cmd.PlayerId }, Array.Empty<string>(), new Vector3[] { cmd.Position });
            case PlaceUnitCommand cmd:
                return (CommandType.PlaceUnit, new int[] { cmd.PlayerId }, new string[] { cmd.UnitData.name }, new Vector3[] { cmd.Position });
            case PlaceWallCommand cmd:
                return (CommandType.PlaceWall, new int[] { cmd.PlayerId }, Array.Empty<string>(), new Vector3[] { cmd.Position });
            case RemoveWallCommand cmd:
                return (CommandType.RemoveWall, new int[] { cmd.PlayerId }, Array.Empty<string>(), new Vector3[] { cmd.Position });
            case RerollShopCommand cmd:
                return (CommandType.RerollShop, new int[] { cmd.PlayerId }, Array.Empty<string>(), Array.Empty<Vector3>());
            case SelectAugmentCommand cmd:
                return (CommandType.SelectAugment, new int[] { cmd.PlayerId, cmd.AugmentIndex }, Array.Empty<string>(), Array.Empty<Vector3>());
            case ActivateSkillCommand cmd:
                return (CommandType.ActivateSkill, new int[] { cmd.PlayerId, (int)cmd.UnitNetworkId }, Array.Empty<string>(), Array.Empty<Vector3>());
            case RearrangeUnitsCommand cmd:
                return (CommandType.RearrangeUnits, new int[] { cmd.PlayerId }, Array.Empty<string>(), Array.Empty<Vector3>());

            // BattleSpawnMonster is intentionally routed through GameManagers.BattleCommands,
            // not the broadcast command stream.
            // UseMagicScroll is intentionally routed through GameManagers.BattleCommands,
            // not the broadcast command stream.

            // ===== Sync Commands (서버 → 클라이언트) =====
            case InitializePlayerCommand cmd:
                return (CommandType.InitializePlayer, new int[] { cmd.PlayerId }, Array.Empty<string>(), Array.Empty<Vector3>());
            case SyncShopItemsCommand cmd:
                // intParams: [playerId], stringParams: [unitDataNames..., starLevels as strings...]
                var shopStrings = cmd.UnitDataNames.Concat(cmd.StarLevels.Select(s => s.ToString())).ToArray();
                return (CommandType.SyncShopItems, new int[] { cmd.PlayerId, cmd.UnitDataNames.Length }, shopStrings, Array.Empty<Vector3>());
            
            case SyncAugmentsCommand cmd:
                return (CommandType.SyncPresentedAugments, new int[] { cmd.PlayerId }, cmd.AugmentNames, Array.Empty<Vector3>());
            
            case SyncPermanentBonusesCommand cmd:
                // float를 int로 변환 (100배하여 정수로 전송)
                return (CommandType.SyncPermanentBonuses, new int[] { cmd.PlayerId, (int)(cmd.AttackDamagePercent * 10000), (int)(cmd.AttackSpeedPercent * 10000) }, Array.Empty<string>(), Array.Empty<Vector3>());
            
            case RegisterUnitAtCommand cmd:
                // NetworkIdRaw를 그대로 int[]에 저장
                return (CommandType.RegisterUnitAt, new int[] { cmd.PlayerId, (int)cmd.UnitNetworkIdRaw, cmd.X, cmd.Y, cmd.StarLevel }, new string[] { cmd.UnitDataKey }, Array.Empty<Vector3>());
            
            case ApplyPermanentWallsCommand cmd:
                // intParams: [playerId, ...flatPositions]
                var wallInts = new int[cmd.FlatPositions.Length + 1];
                wallInts[0] = cmd.PlayerId;
                Array.Copy(cmd.FlatPositions, 0, wallInts, 1, cmd.FlatPositions.Length);
                return (CommandType.ApplyPermanentWalls, wallInts, Array.Empty<string>(), Array.Empty<Vector3>());

            // ===== Notification Commands =====
            case NotifyPurchaseSucceededCommand cmd:
                return (CommandType.NotifyPurchaseSucceeded, new int[] { cmd.PlayerId, cmd.SlotIndex }, Array.Empty<string>(), Array.Empty<Vector3>());
            
            case NotifyAugmentSelectedCommand cmd:
                return (CommandType.NotifyAugmentSelected, new int[] { cmd.PlayerId }, new string[] { cmd.AugmentName }, Array.Empty<Vector3>());
            
            case NotifyWallPlacementCommand cmd:
                return (CommandType.NotifyWallPlacementSucceeded, new int[] { cmd.PlayerId, cmd.X, cmd.Y }, Array.Empty<string>(), Array.Empty<Vector3>());
            
            case NotifyWallRemovalCommand cmd:
                return (CommandType.NotifyWallRemovalSucceeded, new int[] { cmd.PlayerId, cmd.X, cmd.Y }, Array.Empty<string>(), Array.Empty<Vector3>());

            // ===== Request Commands (클라이언트 → 서버) =====
            case RequestSyncDataCommand cmd:
                return (CommandType.RequestSyncData, new int[] { cmd.PlayerId }, Array.Empty<string>(), Array.Empty<Vector3>());

            default:
                Debug.LogError($"[CommandProcessor] 직렬화할 수 없는 커맨드 타입입니다: {command.GetType().Name}");
                return (0, Array.Empty<int>(), Array.Empty<string>(), Array.Empty<Vector3>());
        }
    }

    /// <summary>
    /// [신규] 네트워크로부터 받은 데이터로 ICommand 객체를 복원(역직렬화)합니다.
    /// </summary>
    private async UniTask<ICommand> DeserializeCommand(CommandType type, int[] ints, string[] texts, Vector3[] vectors)
    {
        switch (type)
        {
            // ===== Player Action Commands =====
            case CommandType.BuyUnit:
                return new BuyUnitCommand(ints[0], ints[1]);
            
            case CommandType.MoveUnit:
                return new MoveUnitCommand(ints[0], Vector3Int.RoundToInt(vectors[0]), Vector3Int.RoundToInt(vectors[1]));
            
            case CommandType.SwapUnit:
                return new SwapUnitCommand(ints[0], Vector3Int.RoundToInt(vectors[0]), Vector3Int.RoundToInt(vectors[1]));
            
            case CommandType.SellUnit:
                return new SellUnitCommand(ints[0], Vector3Int.RoundToInt(vectors[0]));
            
            case CommandType.PlaceUnit:
                if (LoadManager.Instance == null)
                {
                    await UniTask.WaitUntil(() => LoadManager.Instance != null);
                }
                await LoadManager.Instance.WaitUntilReady();
                UnitData unitData = LoadManager.Instance.GetUnitData(texts[0]);
                if (unitData == null)
                {
                    Debug.LogError($"[CommandProcessor] UnitData '{texts[0]}'를 찾을 수 없습니다.");
                    return null;
                }
                return new PlaceUnitCommand(ints[0], unitData, Vector3Int.RoundToInt(vectors[0]));
            
            case CommandType.PlaceWall:
                return new PlaceWallCommand(ints[0], Vector3Int.RoundToInt(vectors[0]));
            
            case CommandType.RemoveWall:
                return new RemoveWallCommand(ints[0], Vector3Int.RoundToInt(vectors[0]));
            
            case CommandType.RerollShop:
                return new RerollShopCommand(ints[0]);
            
            case CommandType.SelectAugment:
                return new SelectAugmentCommand(ints[0], ints[1]);
            case CommandType.ActivateSkill:
                return new ActivateSkillCommand(ints[0], (uint)ints[1]);
            case CommandType.RearrangeUnits:
                return new RearrangeUnitsCommand(ints[0]);

            case CommandType.BattleSpawnMonster:
                Debug.LogError("[CommandProcessor] BattleSpawnMonster uses the State Authority battle command route.");
                return null;
            case CommandType.UseMagicScroll:
                Debug.LogError("[CommandProcessor] UseMagicScroll uses the State Authority battle command route.");
                return null;

            // ===== Sync Commands (서버 → 클라이언트) =====
            case CommandType.InitializePlayer:
                return new InitializePlayerCommand(ints[0], default);
            case CommandType.SyncShopItems:
                // ints: [playerId, unitDataNamesCount], texts: [unitDataNames..., starLevels as strings...]
                int namesCount = ints[1];
                string[] unitNames = texts.Take(namesCount).ToArray();
                int[] stars = texts.Skip(namesCount).Select(s => int.TryParse(s, out int v) ? v : 1).ToArray();
                return new SyncShopItemsCommand(ints[0], unitNames, stars);
            
            case CommandType.SyncPresentedAugments:
                return new SyncAugmentsCommand(ints[0], texts);
            
            case CommandType.SyncPermanentBonuses:
                // int를 float로 복원 (10000으로 나눔)
                float attackDmg = ints[1] / 10000f;
                float attackSpd = ints[2] / 10000f;
                return new SyncPermanentBonusesCommand(ints[0], attackDmg, attackSpd);
            
            case CommandType.RegisterUnitAt:
                // ints: [playerId, networkIdRaw, x, y, starLevel], texts: [unitDataKey]
                uint networkIdRaw = (uint)ints[1];
                return new RegisterUnitAtCommand(ints[0], networkIdRaw, ints[2], ints[3], texts.Length > 0 ? texts[0] : "", ints[4]);
            
            case CommandType.ApplyPermanentWalls:
                // ints: [playerId, ...flatPositions]
                int[] flatPositions = new int[ints.Length - 1];
                Array.Copy(ints, 1, flatPositions, 0, flatPositions.Length);
                return new ApplyPermanentWallsCommand(ints[0], flatPositions);

            // ===== Notification Commands =====
            case CommandType.NotifyPurchaseSucceeded:
                return new NotifyPurchaseSucceededCommand(ints[0], ints[1]);
            
            case CommandType.NotifyAugmentSelected:
                return new NotifyAugmentSelectedCommand(ints[0], texts.Length > 0 ? texts[0] : "");
            
            case CommandType.NotifyWallPlacementSucceeded:
                return new NotifyWallPlacementCommand(ints[0], ints[1], ints[2]);
            
            case CommandType.NotifyWallRemovalSucceeded:
                return new NotifyWallRemovalCommand(ints[0], ints[1], ints[2]);

            // ===== Request Commands (클라이언트 → 서버) =====
            case CommandType.RequestSyncData:
                return new RequestSyncDataCommand(ints[0]);

            default:
                Debug.LogError($"[CommandProcessor] 역직렬화할 수 없는 커맨드 타입입니다: {type}");
                return null;
        }
    }

    /// <summary>
    /// [신규] 서버로부터 브로드캐스팅된 커맨드를 받았을 때 호출되는 메서드입니다.
    /// 받은 커맨드를 실행 큐에 추가합니다.
    /// </summary>
    public void EnqueueCommandFromServer(ICommand command)
    {
        _commandQueue.Enqueue(command);
    }

    /// <summary>
    /// [역할 변경] 매 프레임 또는 고정된 틱마다 호출되어, 서버로부터 받은 커맨드들을 순서대로 '실행'합니다.
    /// 이 메서드는 게임의 메인 루프(예: GameManagers.Update)에서 호출되어야 합니다.
    /// </summary>
    public void ProcessCommands()
    {
        while (_commandQueue.Count > 0)
        {
            ICommand command = _commandQueue.Dequeue();
            // 서버가 승인한 커맨드이므로, 검증 없이 그대로 실행하여 게임 상태를 변경합니다.
            command.Execute();
        }
    }
}
