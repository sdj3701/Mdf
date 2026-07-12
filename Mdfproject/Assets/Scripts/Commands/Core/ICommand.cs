using System.Threading;
using Cysharp.Threading.Tasks;

public interface ICommand
{
    // 이 커맨드를 요청한 플레이어의 ID
    int PlayerId { get; set; }

    // 커맨드를 실행하는 메서드
    void Execute();

    // (선택적) 되돌리기 기능을 위한 메서드
    // void Undo();
}

/// <summary>
/// Commands that need to await data/UI readiness implement this in addition to
/// <see cref="ICommand"/>. Keeping the synchronous interface avoids forcing
/// simple deterministic commands to allocate an async state machine.
/// </summary>
public interface IAsyncCommand
{
    UniTask<CommandExecutionResult> ExecuteAsync(CancellationToken cancellationToken);
}

public readonly struct CommandExecutionResult
{
    public bool Success { get; }
    public bool Cancelled { get; }
    public string Error { get; }

    private CommandExecutionResult(bool success, bool cancelled, string error)
    {
        Success = success;
        Cancelled = cancelled;
        Error = error;
    }

    public static CommandExecutionResult Completed() => new CommandExecutionResult(true, false, null);
    public static CommandExecutionResult Failed(string error) => new CommandExecutionResult(false, false, error);
    public static CommandExecutionResult Canceled() => new CommandExecutionResult(false, true, "cancelled");
}
