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
    public void RequestCommandExecution(ICommand command)
    {
        // 1. 커맨드를 직렬화합니다.
        (CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams) = SerializeCommand(command);

        // NetworkManager가 있고, 게임 세션이 활성화 상태일 때만 RPC를 호출합니다.
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsGameRunnerActive)
        {
            // 2. 직렬화된 데이터를 RPC로 서버에 전송합니다.
            Debug.Log("<color=red>test RPC RequestCommandExecution </color>");
            NetworkManager.Instance.RPC_RequestCommandToServer(type, intParams, stringParams, vectorParams);
        }
        else
        {
            // 싱글플레이어 또는 네트워크가 연결되지 않은 환경을 위한 폴백(Fallback)
            // 서버 역할을 로컬에서 즉시 시뮬레이션합니다.
            ReceiveAndEnqueueCommand(type, intParams, stringParams, vectorParams);
        }
    }

    /// <summary>
    /// [수정됨] 서버로부터 브로드캐스팅된 커맨드 데이터 또는 싱글플레이어용 데이터를 받아
    /// 역직렬화하고 실행 큐에 추가합니다.
    /// </summary>
    public async void ReceiveAndEnqueueCommand(CommandType type, int[] intParams, string[] stringParams, Vector3[] vectorParams)
    {
        ICommand command = await DeserializeCommand(type, intParams, stringParams, vectorParams);
        if (command != null)
        {
            Debug.Log("<color=red>test RPC ReceiveAndEnqueueCommand </color>");
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
