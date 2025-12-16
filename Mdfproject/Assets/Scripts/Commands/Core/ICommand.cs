public interface ICommand
{
    // 이 커맨드를 요청한 플레이어의 ID
    int PlayerId { get; set; }

    // 커맨드를 실행하는 메서드
    void Execute();

    // (선택적) 되돌리기 기능을 위한 메서드
    // void Undo();
}
