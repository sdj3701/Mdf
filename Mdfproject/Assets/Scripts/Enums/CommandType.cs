// Assets/Scripts/Commands/Core/CommandType.cs (새 파일)

public enum CommandType
{
    // 값을 명시적으로 지정하여 나중에 순서가 바뀌어도 문제없게 합니다.
    BuyUnit = 1,
    MoveUnit = 2,
    PlaceUnit = 3,
    PlaceWall = 4,
    RemoveWall = 5,
    RerollShop = 6,
    SelectAugment = 7,
    SwapUnit = 8,
    // 필요에 따라 다른 커맨드들도 추가...
}// Assets/Scripts/Enums/CommandType.cs (새 파일)
