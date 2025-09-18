using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class CommandProcessor
{
    // 1. 서버가 실행해야 할 커맨드들을 담는 큐 (네트워크로부터 수신)
    private Queue<ICommand> _commandQueue = new Queue<ICommand>();

    /// <summary>
    /// [수정됨] 클라이언트(AI, UI)가 커맨드 실행을 '요청'할 때 호출하는 메서드입니다.
    /// 이 메서드는 커맨드를 직렬화하고, 멀티플레이어 환경에서는 네트워크로 전송합니다.
    /// </summary>
    public async void RequestCommandExecution(ICommand command)
    {
        // 1. 커맨드를 직렬화합니다.
        (CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams) = SerializeCommand(command);

        // TODO: 멀티플레이어 구현 시, 아래 주석을 해제하고 직렬화된 데이터를 RPC로 서버에 전송합니다.
        // NetworkManager.Instance.RPC_SendToServer(type, intParams, stringParams, vectorParams);

        // 현재는 싱글플레이어 테스트를 위해, 서버 역할을 시뮬레이션합니다.
        // 받은 데이터를 즉시 역직렬화하여 실행 큐에 넣습니다.
        // 이는 "요청 -> 서버(시뮬레이션) -> 실행 큐" 흐름을 따릅니다.
        await SimulateServerReceipt(type, intParams, stringParams, vectorParams);
    }

    /// <summary>
    /// [시뮬레이션용] 서버가 클라이언트로부터 커맨드 데이터를 받았다고 가정하는 메서드.
    /// 이 메서드는 받은 데이터를 역직렬화하여 모든 클라이언트에게 브로드캐스팅하는 서버의 역할을 흉내 냅니다.
    /// </summary>
    private async UniTask SimulateServerReceipt(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams)
    {
        // TODO: 멀티플레이어에서는 서버가 여기서 유효성 검사를 수행해야 합니다.
        // 예: if (!IsValid(command)) return;

        // 모든 클라이언트에게 브로드캐스팅한다고 가정하고, 로컬에서 역직렬화하여 큐에 넣습니다.
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
            case BuyUnitCommand cmd:
                return (CommandType.BuyUnit, new int[] { cmd.PlayerId, cmd.ShopSlotIndex }, null, null);
            case MoveUnitCommand cmd:
                return (CommandType.MoveUnit, new int[] { cmd.PlayerId }, null, new Vector3[] { cmd.From, cmd.To });
            case PlaceUnitCommand cmd:
                // UnitData는 ScriptableObject이므로 이름(ID)을 string으로 전송합니다.
                return (CommandType.PlaceUnit, new int[] { cmd.PlayerId }, new string[] { cmd.UnitData.name }, new Vector3[] { cmd.Position });
            case PlaceWallCommand cmd:
                return (CommandType.PlaceWall, new int[] { cmd.PlayerId }, null, new Vector3[] { cmd.Position });
            case RemoveWallCommand cmd:
                return (CommandType.RemoveWall, new int[] { cmd.PlayerId }, null, new Vector3[] { cmd.Position });
            case RerollShopCommand cmd:
                return (CommandType.RerollShop, new int[] { cmd.PlayerId }, null, null);
            case SelectAugmentCommand cmd:
                return (CommandType.SelectAugment, new int[] { cmd.PlayerId, cmd.AugmentIndex }, null, null);
            default:
                Debug.LogError($"[CommandProcessor] 직렬화할 수 없는 커맨드 타입입니다: {command.GetType().Name}");
                return (0, null, null, null);
        }
    }

    /// <summary>
    /// [신규] 네트워크로부터 받은 데이터로 ICommand 객체를 복원(역직렬화)합니다.
    /// </summary>
    private async UniTask<ICommand> DeserializeCommand(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams)
    {
        switch (type)
        {
            case CommandType.BuyUnit:
                // 생성자: BuyUnitCommand(playerId, shopSlotIndex)
                return new BuyUnitCommand(intParams[0], intParams[1]);
            case CommandType.MoveUnit:
                // 생성자: MoveUnitCommand(playerId, from, to)
                return new MoveUnitCommand(intParams[0], Vector3Int.RoundToInt(vectorParams[0]), Vector3Int.RoundToInt(vectorParams[1]));
            case CommandType.PlaceUnit:
                // 생성자: PlaceUnitCommand(playerId, unitData, position)
                // UnitData는 이름(ID)을 사용하여 에셋을 비동기적으로 로드합니다.
                UnitData unitData = await AssetLoader.LoadAssetAsync<UnitData>(stringParams[0]);
                if (unitData == null)
                {
                    Debug.LogError($"[CommandProcessor] UnitData '{stringParams[0]}'를 찾을 수 없어 PlaceUnitCommand를 생성할 수 없습니다.");
                    return null;
                }
                return new PlaceUnitCommand(intParams[0], unitData, Vector3Int.RoundToInt(vectorParams[0]));
            case CommandType.PlaceWall:
                // 생성자: PlaceWallCommand(playerId, position)
                return new PlaceWallCommand(intParams[0], Vector3Int.RoundToInt(vectorParams[0]));
            case CommandType.RemoveWall:
                // 생성자: RemoveWallCommand(playerId, position)
                return new RemoveWallCommand(intParams[0], Vector3Int.RoundToInt(vectorParams[0]));
            case CommandType.RerollShop:
                // 생성자: RerollShopCommand(playerId)
                return new RerollShopCommand(intParams[0]);
            case CommandType.SelectAugment:
                // 생성자: SelectAugmentCommand(playerId, augmentIndex)
                return new SelectAugmentCommand(intParams[0], intParams[1]);
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
