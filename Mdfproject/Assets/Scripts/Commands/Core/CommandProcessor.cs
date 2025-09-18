using System.Collections.Generic;
using UnityEngine;

public class CommandProcessor
{
    // 1. 서버가 실행해야 할 커맨드들을 담는 큐 (네트워크로부터 수신)
    private Queue<ICommand> _commandQueue = new Queue<ICommand>();

    /// <summary>
    /// [수정됨] 클라이언트(AI, UI)가 커맨드 실행을 '요청'할 때 호출하는 메서드입니다.
    /// 이 메서드는 커맨드를 즉시 실행하지 않고 네트워크를 통해 서버로 전송합니다.
    /// </summary>
    public void RequestCommandExecution(ICommand command)
    {
        // TODO: 여기서 command 객체를 직렬화하여 서버로 전송하는 네트워크 로직을 구현해야 합니다.
        // ex) NetworkManager.Instance.SendToServer(command);
        Debug.Log($"[CommandProcessor] Command requested: {command.GetType().Name}. In multiplayer, this is sent to the server.");

        // 현재는 서버가 없으므로, 로컬에서 바로 큐에 넣는 방식으로 싱글플레이어처럼 동작하게 합니다.
        // 멀티플레이어 구현 시, 이 라인은 제거하고 실제 네트워크 요청만 남겨야 합니다.
        // 서버로부터 응답이 오면 EnqueueCommandFromServer가 호출될 것입니다.
        _commandQueue.Enqueue(command);
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
