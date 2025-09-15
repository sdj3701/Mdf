using System.Collections.Generic;
using UnityEngine;

public class CommandProcessor
{
    private Queue<ICommand> _commandQueue = new Queue<ICommand>();

    // 커맨드를 큐에 추가 (네트워크 지연 보간 등에 사용)
    public void EnqueueCommand(ICommand command)
    {
        _commandQueue.Enqueue(command);
    }

    // 커맨드를 즉시 실행 (로컬 플레이어의 즉각적인 피드백에 사용)
    public void ExecuteCommand(ICommand command)
    {
        command.Execute();
        // TODO: 네트워크로 이 커맨드를 전송하는 로직 추가
    }

    // 매 프레임 또는 고정 프레임마다 큐에 쌓인 커맨드를 처리
    public void ProcessCommands()
    {
        while (_commandQueue.Count > 0)
        {
            ICommand command = _commandQueue.Dequeue();
            command.Execute();
        }
    }
}
