# MDF Project Agent Guide

**Last Updated:** 2026-05-04  
**Reviewed Commit:** f73dc20f  
**Branch:** feature/ai3  
**Unity:** 2021.3.45f1

이 문서는 기존 `AGENTS.md`와 `AGENTS2.md` 내용을 통합해 현재 프로젝트 상태 기준으로 정리한 Codex/Agent용 작업 지침이다. 한국어 주석과 로그가 많은 프로젝트이므로, 기존 한국어 문맥을 유지하되 깨진 인코딩이나 오래된 구조 설명은 새로 작성한다.

## Overview

MDF는 Photon Fusion 2 기반 Unity 멀티플레이어 타워디펜스/자동전투 프로젝트다. 플레이어는 준비 단계에서 유닛 구매, 유닛 배치, 벽/미로 건설, 증강 선택을 수행하고, 전투 단계에서는 상대 필드에 몬스터와 마법 스크롤을 투입한다. 1~4인 세션과 싱글플레이 직접 실행을 모두 지원하며, 현재 코드에는 AI 플레이어, Host Migration 복구, Addressables 기반 데이터/프리팹 로딩이 포함되어 있다.

게임 상태는 `GameManagers.GameState`의 `Setup -> DataLoading -> Prepare -> Battle1 -> Battle2 -> GameOver` 흐름을 따른다. `Battle1`과 `Battle2`는 같은 라운드의 공수 교대 단계이며, 매칭별 선공자는 `GameManagers`의 매칭/공격자 테이블로 관리된다.

## Current Project Shape

```
mdf/
├── Mdfproject/                         # Unity 프로젝트 루트
│   ├── Assets/
│   │   ├── Scripts/                    # 핵심 게임 코드
│   │   ├── Scenes/                     # Title, JoinLobby, MatchingLobby, Game, Test* 등
│   │   ├── GameData/                   # Unit/Monster/Skill/Augment/Scroll/Wave ScriptableObject 데이터
│   │   ├── AddressableAssetsData/      # Addressables 설정과 그룹
│   │   ├── Prefabs/                    # Character, Dynamic, Monster, Structure, UI, VFX 등
│   │   ├── Resource/                   # Models, VFX, Shaders, Fonts, Textures 등 외부/리소스 에셋
│   │   ├── Photon/                     # Photon/Fusion SDK 에셋
│   │   ├── Firebase/                   # Firebase 플러그인
│   │   ├── Language/                   # Localization 로케일/스트링 테이블
│   │   └── Samples/, TutorialInfo/     # 샘플/튜토리얼 에셋
│   ├── Packages/manifest.json
│   ├── ProjectSettings/
│   ├── ServerData/StandaloneWindows64/ # Addressables 서버 데이터 산출물
│   └── .editorconfig                   # C# 네이밍/포맷 규칙
└── .gitignore
```

`Assets/Scripts`에는 현재 약 196개의 C# 파일이 있으며, 프로젝트 자체 `asmdef`는 따로 없고 대부분 `Assembly-CSharp`에 빌드된다. 별도 asmdef는 Photon/Fusion, Toony Colors Pro, 샘플/서드파티 쪽에 있다.

## Script Structure

```
Scripts/
├── Managers/                  # GameManagers, PlayerManager, FieldManager, ShopManager 등
├── Commands/                  # Command 패턴: Core, PlayerActions, Sync, AI
├── Game/                      # Units, Monsters, Skills, Battle, Augments, Targeting, Game Rules
├── AI/                        # BehaviorTree, UtilitySystem, Planning
├── Network/                   # NetworkManager, NetworkPlayer, HostMigrationHandler, lobby UI
├── UI/                        # UIManagers, HUD, shop/augment/ranking/attack sequence UI
├── ComponentRegistrySystem/   # 런타임 컴포넌트 등록/조회 및 정적 에셋 레지스트리
├── DB/                        # Addressable key helper, MiniJSON
├── Enums/                     # CommandType, GameEnums, Monster/Stat/Skill enums
├── Interfaces/                # ICommand, IHealth, IEnemy, IMana, IPlacementHandler
├── Button/                    # 버튼 컴포넌트
├── VFX/                       # VFX/Projectile 풀링
├── Editor/                    # Google Sheet importer, exporter, missing script finder
└── MainLobby/                 # 캐릭터 선택/로비 관련 컴포넌트
```

## Where To Look

| Task | Primary Files | Notes |
|------|---------------|-------|
| 게임 흐름/라운드/상태 | `Managers/GameManagers.cs`, `GameManagers.StateTransition.cs` | `NetworkBehaviour`, `TickTimer`, `GameState` 전환 |
| Host Migration 복구 | `Network/HostMigrationHandler.cs`, `Managers/GameManagers.MigrationRecovery.cs` | Runner 재시작, 상태 복원, UI/AI 복구 |
| 플레이어 상태 | `Managers/PlayerManager.cs` | HP/골드/벽/상점 스냅샷, 증강, 스크롤, 하위 매니저 |
| 필드/미로/배치 | `Managers/FieldManager.cs`, `Game/Game Rules/FindLoad/` | 3D 그리드, 벽, 유닛 등록, A* 경로 |
| 네트워크/로비 | `Network/NetworkManager.cs`, `Network/NetworkPlayer.cs` | Fusion Runner, 세션/로비, ready/nickname |
| 커맨드 시스템 | `Commands/Core/CommandProcessor.cs`, `Enums/CommandType.cs` | UI/AI 요청을 네트워크 직렬화 후 모든 피어에서 실행 |
| 유닛/전투 | `Game/Units/Unit.cs`, `Managers/CombatScheduler.cs`, `Game/Projectile.cs` | 공격, 스킬, 투사체, 데미지 처리 |
| 몬스터/웨이브 | `Game/Monsters/Monster.cs`, `MonsterSpawner.cs`, `MonsterReleaseScheduler.cs`, `WaveDatabase.cs` | 스폰, 이동, 공격, 웨이브/풀 |
| 공격 시퀀스 | `Game/Battle/AttackSequenceManager.cs`, `UI/AttackSequence/` | 몬스터 풀/마법 스크롤 선택과 투입 |
| 증강/스크롤 | `Managers/AugmentManager.cs`, `Game/Augments/` | 영구 보너스, 몬스터/보스/스크롤 보상 |
| AI | `Commands/AI/AIPlayerController.cs`, `AI/BehaviorTree/`, `AI/Planning/MazePlanner.cs` | 준비 단계 BT, 미로 계획, 유닛 배치 평가, 공격 스폰 전략 |
| 데이터 로딩 | `Managers/LoadManager.cs`, `AddressablesManager.cs`, `AssetLoader.cs` | UnitData 캐시, 프리팹/WaveDatabase, Addressables 키 로딩 |
| UI 풀링 | `UI/UIManagers.cs`, `UI/UIPool.cs` | `GetUIElement()`로 UI 프리팹 로드/재사용 |
| 이벤트 | `Managers/GameEvents.cs` | UI/상태/구매/증강/Host Migration 이벤트 버스 |

## GameManagers Notes

`GameManagers`는 현재 partial 클래스로 분리되어 있다.

- `GameManagers.cs`: 네트워크 상태, 타이머, Spawned/Render, 게임 플로우, 커맨드 RPC, 전투/라운드 처리
- `GameManagers.StateTransition.cs`: 상태 전환 래퍼와 전환 직전 Host Migration snapshot push
- `GameManagers.PlayerRegistry.cs`: 플레이어 등록, 로컬 플레이어 relink, 매칭/상대 복원
- `GameManagers.UIFlow.cs`: 상태 변경 UI, shop/augment UI 준비, 공격 시퀀스 UI
- `GameManagers.MigrationRecovery.cs`: Host Migration 후 상태/타이머/UI/상점/전투 복구

상태 전환이나 라운드 흐름을 수정할 때는 위 파일들을 함께 확인한다. 특히 `currentState`, `currentRound`, `phaseTimer`, `_battleOpponents`, `_matchFirstAttacker`, `FirstAttackerPlayerId`는 Host Migration과 UI 복구에 같이 사용된다.

## Command Flow

현재 주 경로는 `CommandProcessor -> PlayerManager RPC -> GameManagers broadcast`다.

```
1. UI 또는 AI가 new XxxCommand(...) 생성
2. CommandProcessor.RequestCommandExecution(command)
3. CommandProcessor.SerializeCommand()로 CommandType + int/string/vector 배열 생성
4. Host/StateAuthority라면 GameManagers.RPC_BroadcastCommandToClients(...)
5. Client라면 local/input-authority PlayerManager.RPC_RequestCommandToServer(...)
6. 서버 PlayerManager가 playerId를 authoritative 값으로 보정/검증 후 GameManagers에 broadcast
7. 모든 피어가 CommandProcessor.ReceiveAndEnqueueCommand(...)
8. DeserializeCommand() -> EnqueueCommandFromServer() -> ProcessCommands() -> Execute()
```

싱글플레이 또는 Runner가 없는 로컬 경로에서는 네트워크 RPC 없이 바로 enqueue/execute된다. `NetworkManager`에도 오래된 형태의 커맨드 RPC가 남아 있으나, 새 플레이어 액션은 현재 경로인 `PlayerManager`/`GameManagers`/`CommandProcessor` 기준으로 작업한다.

### Adding A Command

1. `Commands/PlayerActions/` 또는 `Commands/Sync/`에 `ICommand` 구현 클래스를 추가한다.
2. `Enums/CommandType.cs`에 타입을 추가한다. 현재 구간은 Player Action `1-99`, Sync `100-199`, Notification `200-299`, Request `300-399`다.
3. `CommandProcessor.SerializeCommand()`와 `DeserializeCommand()`를 함께 수정한다.
4. `Execute()`는 모든 피어에서 재생 가능한 결정적 처리를 목표로 하고, 실제 권한이 필요한 상태 변경은 StateAuthority 또는 `HasStateAuthorityOrNoNetwork()` 패턴을 확인한다.
5. Addressables 데이터가 필요한 커맨드는 `LoadManager.WaitUntilReady()` 또는 `AssetLoader.LoadAssetAsync<T>()` 대기 경로를 명시한다.
6. UI/AI 호출부와 실패 이벤트(`GameEvents`)를 같이 점검한다.

파일이 있다고 해서 네트워크 커맨드로 연결된 것은 아니다. 예를 들어 새 커맨드는 반드시 `CommandType`과 `CommandProcessor` 양쪽에 등록되어야 한다.

## Photon Fusion 2 Patterns

- 네트워크 동기화 대상은 `NetworkBehaviour`를 상속한다.
- 동기화 상태는 `[Networked]` 프로퍼티, `NetworkArray<T>`, `NetworkString<_N>`, `TickTimer`, `NetworkBool` 등을 사용한다.
- 클라이언트가 직접 authoritative 상태를 변경하지 않는다. 상태 변경은 커맨드/RPC를 통해 서버 또는 StateAuthority에서 처리한다.
- `Spawned()`에서 런타임 참조와 `ChangeDetector`를 초기화하고, `Render()`에서 변경 감지 후 UI/애니메이션 이벤트를 처리한다.
- RPC 이름은 의도에 맞춰 유지한다. 클라/입력권한 -> 서버/StateAuthority는 `RPC_Request*`, 서버 -> 전체 클라이언트는 `RPC_Broadcast*`, 결과 알림은 `RPC_Notify*` 계열을 사용한다.
- 싱글플레이 직접 실행을 지원하는 클래스는 `Object == null`, `Runner == null`, `!Runner.IsRunning`일 때 로컬 권한으로 처리하는 `HasStateAuthorityOrNoNetwork()` 패턴을 따른다.
- Host Migration 대상 NetworkObject는 프리팹 설정과 복구 경로를 함께 확인한다. 기존 주석 기준으로 `Destroy When State Authority Leaves = false`, `Allow State Authority Override = true` 설정이 중요하다.

## Data And Addressables

- `LoadManager`는 `UnitData`를 Inspector 우선, 없으면 Addressables label `"UnitData"`로 로드하고 이름 기반 캐시를 만든다.
- `AddressablesManager`는 PlayerManager/Grid/DefaultMonster/WaveDatabase 프리팹/데이터를 `AssetReference`로 캐시한다.
- `AssetLoader.LoadAssetAsync<T>(key)`는 개별 Addressables 키 fallback 로딩에 쓰인다.
- `AugmentManager`는 증강 데이터를 Addressables label `"Augment"` 기준으로 로드한다.
- 주요 ScriptableObject는 `GameData/Units`, `GameData/Monsters`, `GameData/MonsterWave`, `GameData/Skills`, `GameData/Scrolls`, `GameData/Augments`에 있다.
- 새 데이터/프리팹은 Addressables 그룹/키/label과 로딩 코드의 키 문자열을 함께 맞춘다.

## AI System

- `AIPlayerController`는 `PlayerManager`와 `CommandProcessor`로 초기화되며, `ComponentRegistry`에 playerId 문자열로 등록된다.
- 준비 단계 BehaviorTree는 증강 선택, 미로 건설, 유닛 구매, 유닛 재배치, 상점 리롤 순서로 판단한다.
- `BuildMazeAction`은 `MazePlanner`의 `Task<MazePlanResult>` 기반 비동기 계획을 사용한다.
- 유닛 배치 평가는 `AI/UtilitySystem/Considerations/`와 `Placement/` 하위 고려사항을 사용한다.
- `AIPacer`는 벽/구매/이동/리롤 등의 AI 행동 간격을 제한한다.
- 전투 중 AI 공격 스폰은 `MonsterSpawner.StartAutoSpawnFromPool()`에서 `AIAttackStrategy`가 만든 `AISpawnPlan`을 실행한다. 현재 `AIPlayerController`의 combat BehaviorTree 자체는 비어 있으므로 전투 AI를 추가할 때 기존 스폰 경로와 중복되지 않게 확인한다.

## UI And Events

- UI는 `UIManagers.GetUIElement(string uiName)`와 `UIPool` 기반으로 로드/재사용한다.
- Shop/Augment UI는 `GameManagers.UIFlow`에서 준비하며, Host Migration 후에도 재연결/재표시 경로가 있다.
- `GameEvents`는 상태 변경, 라운드 시작, 플레이어 스탯, 구매 성공/실패, 벽 배치/제거, 증강, 몬스터 풀, 마법 스크롤, Host Migration 이벤트의 중심이다.
- UI 갱신은 요청 이벤트보다 성공/동기화 이벤트를 기준으로 처리한다. 예: 구매 UI는 구매 요청이 아니라 `OnUnitPurchaseSucceeded`/sync 결과에 반응해야 한다.

## Coding Conventions

`Mdfproject/.editorconfig` 기준과 주변 코드 스타일을 우선한다.

| 대상 | 스타일 | 예시 |
|------|--------|------|
| private/protected field | `_camelCase` | `_changeDetector` |
| public field/property | PascalCase 권장 | `PlayerManagerPrefab` |
| method | PascalCase | `SpendGold()` |
| local/parameter | camelCase | `playerId` |
| constants/static readonly | PascalCase 권장 | `MaxPlayers` |

현재 코드에는 legacy public field와 소문자 `[Networked]` 프로퍼티(`playerId`, `currentState`)가 일부 남아 있다. 주변 호환성을 깨는 네이밍 전용 리팩터는 별도 요청이 없으면 하지 않는다.

## Async Conventions

- Unity 런타임 비동기 작업은 기본적으로 UniTask를 사용한다.
- Fire-and-forget은 `.Forget()` 또는 `GameManagers.RunLifecycleTask()`/`RunMigrationTask()` 같은 기존 보호 래퍼를 우선한다.
- `async void`는 Unity 이벤트, Fusion RPC, 기존 callback signature처럼 반환 타입을 바꿀 수 없는 경우에만 허용한다.
- 현재 AI 계획(`MazePlanner`)과 Host Migration 일부에는 `System.Threading.Tasks.Task`가 사용된다. 해당 경로를 수정할 때는 기존 호출자와 threading 가정을 먼저 확인한다.
- `Render()`나 `FixedUpdateNetwork()` 안에서 직접 `await`하지 않는다. 필요한 경우 별도 UniTask로 분리한다.

## Anti-Patterns

| 피할 것 | 이유/대안 |
|---------|-----------|
| 클라이언트에서 `[Networked]` 상태 직접 변경 | StateAuthority/Command/RPC 경로를 사용 |
| `MonoBehaviour`에 Networked 필드 추가 | 동기화되지 않음. `NetworkBehaviour` 필요 |
| `CommandType`만 추가하고 Serialize/Deserialize 누락 | 모든 피어에서 복원 불가 |
| `Execute()`에서 로컬 UI만 먼저 바꾸기 | 성공/동기화 이벤트 기준으로 UI 갱신 |
| Host Migration 복구 경로 무시 | `HostMigrationHandler`, `GameManagers.MigrationRecovery`, UI relink 확인 |
| Addressables 키 문자열 임의 변경 | 데이터/프리팹 label, key, loader를 함께 수정 |
| Photon/Fusion, Firebase, TMP, TCP2, Samples 폴더 직접 수정 | 서드파티/샘플 충돌 위험. 래퍼나 프로젝트 코드에서 해결 |
| `FindObjectOfType` 남용 | 런타임 fallback은 존재하지만 새 코드에서는 명시 참조, Registry, 초기화 주입 우선 |
| 무제한 컬렉션을 Networked 상태로 동기화 | `NetworkArray` capacity와 snapshot 구조를 명시 |

## Local Run Notes

- Unity Editor 버전은 `2021.3.45f1`이다.
- 싱글플레이 빠른 테스트는 `Assets/Scenes/Game.unity`를 직접 Play한다. `GameSceneInitializer`가 Runner를 `GameMode.Single`로 만들고 `GameManagers`를 spawn한다.
- 멀티플레이 흐름은 `Title`, `JoinLobby`, `MatchingLobby`, `Game` 씬과 `NetworkManager`/Fusion Runner 경로를 확인한다.
- `GameSceneInitializer.singlePlayerCount`는 1~4 범위이며, 나머지 플레이어는 AI로 채워지는 경로가 있다.
- Addressables 서버 데이터는 `Mdfproject/ServerData/StandaloneWindows64/`에 있다.
- 저장소에 명시적인 CLI 테스트 명령은 없다. 컴파일/플레이 검증은 Unity Editor 또는 프로젝트에 설치된 Unity CLI connector 환경에서 수행한다.

