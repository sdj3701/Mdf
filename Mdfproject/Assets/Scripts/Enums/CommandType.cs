// Assets/Scripts/Commands/Core/CommandType.cs (새 파일)

public enum CommandType
{
    // ===== Player Action Commands (1-99) =====
    // 플레이어가 요청하는 게임 액션
    BuyUnit = 1,
    MoveUnit = 2,
    PlaceUnit = 3,
    PlaceWall = 4,
    RemoveWall = 5,
    RerollShop = 6,
    SelectAugment = 7,
    SwapUnit = 8,
    SellUnit = 9,
    ActivateSkill = 10,
    RearrangeUnits = 11,
    // Routed through GameManagers.BattleCommands as a State Authority battle action, not CommandProcessor broadcast.
    BattleSpawnMonster = 12,
    // Routed through GameManagers.BattleCommands as a State Authority battle action, not CommandProcessor broadcast.
    UseMagicScroll = 13,
    SetSkillActivationMode = 14,
    ActivateKingSkill = 15,


    // ===== Sync Commands (100-199) =====
    // 서버 → 클라이언트 상태 동기화
    SyncShopItems = 100,
    SyncPresentedAugments = 101,
    SyncPermanentBonuses = 102,
    RegisterUnitAt = 103,
    ApplyPermanentWalls = 104,
    InitializePlayer = 105,

    // ===== Notification Commands (200-299) =====
    // 서버 → 클라이언트 이벤트 알림
    NotifyPurchaseSucceeded = 200,
    NotifyAugmentSelected = 201,
    NotifyWallPlacementSucceeded = 202,
    NotifyWallRemovalSucceeded = 203,

    // ===== Request Commands (300-399) =====
    // 클라이언트 → 서버 데이터 요청
    RequestSyncData = 300,
}
