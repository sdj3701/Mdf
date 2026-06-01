# 03_Game UI Toolkit Refactor Plan

## 목적

`00_Title`, `01_MatchingLobby`, `02_JoinLobby`는 UI Toolkit 기반으로 작업되어 있으므로 `03_Game` 씬도 같은 방향으로 정리할 수 있다. 다만 현재 `03_Game` UI는 단순 화면 표시가 아니라 Photon Fusion 네트워크 상태, 커맨드 처리, Addressables 로딩, Host Migration, 월드 공간 UI까지 연결되어 있다.

따라서 `03_Game` UI를 UI Toolkit으로 옮기는 것은 가능하지만, 한 번에 UGUI를 제거하는 방식은 위험하다. 먼저 화면 공간 UI부터 UI Toolkit으로 이전하고, 월드 공간에 붙는 체력바/판매/벽 제거 패널은 초기 단계에서는 UGUI를 유지하는 하이브리드 전환을 권장한다.

## 현재 03_Game UI 구조 요약

`03_Game.unity`는 `UIManager.prefab`을 사용한다. 이 프리팹은 `GameCanvas` 아래에 여러 UGUI 패널을 두고, `UIManagers`와 `UIPool`을 통해 필요한 UI를 켜고 끄는 구조다.

현재 핵심 구조는 다음과 같다.

| 구분 | 현재 방식 | 주요 역할 |
| --- | --- | --- |
| UI 루트 | `UIManager.prefab`, `GameCanvas` | UGUI Canvas 기반 전체 게임 UI 루트 |
| UI 생명주기 | `UIManagers`, `UIPool` | UI 프리팹을 풀링하고 Addressables 키로 동적 로딩 |
| HUD | `PlayerHUDController` | 골드, 라운드, 벽 개수, 상대 이름, 상점 토글, 벽 배치 버튼 |
| 타이머 | `PhaseTimerUI` | Prepare/Battle/전환 타이머 표시 |
| 랭킹 | `RankingUIController` | 플레이어 체력 기준 순위 표시 |
| 상점 | `ShopUIController`, `ShopSlot` | 상점 슬롯 표시, 리롤, 유닛 구매 커맨드 요청 |
| 증강 | `AugmentUIController`, `AugmentSlot` | 증강 선택 표시, 증강 선택 커맨드 요청 |
| 공격 시퀀스 | `AttackSequenceUIController` | 몬스터/마법 스크롤 선택 UI |
| 전투 전환 | `BattleTransitionUI` | 공격/방어/시퀀스 전환 애니메이션 |
| 유닛 상세 | `UnitDetailPanelController` | 유닛 능력치, 스킬 설명, 스킬 자동/수동 토글 |
| 판매/벽 제거 | `UnitSellPanelController`, `WallRemovePanelController` | 월드 오브젝트를 따라다니는 선택 패널 |
| 체력/마나바 | `StatusBarUI`, `UIBillboard` | 유닛/몬스터/벽 머리 위 월드 공간 상태 표시 |
| 옵션 | `GameOptionButton`, `SoundButton`, `VersionButton` | 옵션/사운드/버전 패널 이동 |
| 결과 | Victory/Defeat 패널 | 게임 종료 결과 표시 |

## 현재 GameUI 기능 상세

### 1. UIManagers / UIPool

`UIManagers`는 `DontDestroyOnLoad` 싱글톤으로 동작하며, `UILists`에 등록된 UGUI 프리팹 또는 Addressables 키를 통해 UI 오브젝트를 가져온다.

핵심 역할은 다음과 같다.

- `mainCanvas`를 찾고, 모든 동적 UI를 해당 Canvas 아래에 배치한다.
- `GetUIElement(uiName)`으로 UI를 활성화한다.
- `ReturnUIElement(uiName)`으로 UI를 비활성화하고 풀에 반환한다.
- `UILists`에 없는 UI도 Addressables 키를 기준으로 `UIPool(null, uiName)`을 만들어 로딩한다.

이 구조는 단순 표시용이 아니라 `GameManagers`, `FieldManager`, `AttackSequenceUIController`, `AugmentUIController` 등 여러 게임 로직에서 직접 호출하고 있다. UI Toolkit 전환 시에도 이 생명주기 계약을 대체하거나 호환해야 한다.

### 2. GameManagers와 UI 상태 전환

`GameManagers.UIFlow`는 네트워크 게임 상태에 맞춰 UI를 열고 닫는다.

주요 작업은 다음과 같다.

- `SetupGameUI()`에서 상점 UI와 증강 UI를 미리 로딩하고 초기화한다.
- `EnsureGameUIReadyForSyncCommands()`에서 빌드 클라이언트가 증강/상점 동기화 명령을 받기 전에 UI가 준비될 때까지 기다린다.
- Prepare 상태에서는 상점/증강 표시 조건을 관리한다.
- Battle 상태에서는 증강/상점을 숨긴다.
- GameOver 상태에서는 로컬 플레이어가 승자인지 확인하여 Victory 또는 Defeat UI를 표시한다.
- 공격 시퀀스 진입 시 `AttackSequenceUIController`를 생성하거나 표시한다.

이 부분 때문에 UI Toolkit 전환 후에도 "UI가 준비되었는지"를 알 수 있는 명확한 준비 상태가 필요하다. 단순히 `UIDocument`만 붙이면 동기화 명령이 먼저 도착하는 상황에서 문제가 생길 수 있다.

### 3. PlayerHUDController

`PlayerHUDController`는 인게임 HUD를 담당한다.

현재 표시/처리 내용은 다음과 같다.

- 로컬 플레이어 골드 표시
- 현재 라운드 표시
- 벽 개수 표시
- Prepare 상태에서만 벽 배치 버튼 표시
- Prepare 상태에서만 상점 토글 버튼 표시
- 전투 시퀀스 중 상대 이름과 공격/방어 역할 표시
- Host Migration 이후에도 로컬 플레이어와 GameManagers 참조를 다시 찾음
- 상점 토글 시 `ShopUIController.ToggleContent()` 호출

이 UI는 화면 공간 HUD이므로 UI Toolkit 이전에 적합하다. 다만 매 프레임 값을 갱신하는 현재 방식은 UI Toolkit에서는 이벤트 기반 또는 변경 감지 방식으로 바꾸는 편이 좋다.

### 4. PhaseTimerUI

`PhaseTimerUI`는 현재 게임 페이즈 타이머와 시퀀스 전환 타이머를 표시한다.

주요 특징은 다음과 같다.

- `GameManagers.Instance.GetRemainingPhaseTime()` 값을 표시한다.
- 시퀀스 전환 중이면 전환 타이머를 우선 표시한다.
- GameOver 상태에서는 표시를 중지한다.
- 네트워크 오브젝트가 무효화되는 타이밍의 예외를 방어한다.

UI Toolkit으로 옮기기 쉬운 화면 공간 UI다. 단, Host Migration이나 씬 전환 중 잘못된 네트워크 참조가 발생하지 않도록 기존 방어 코드를 유지해야 한다.

### 5. RankingUIController

`RankingUIController`는 모든 플레이어를 체력 기준으로 정렬해 좌우 슬롯에 표시한다.

주요 작업은 다음과 같다.

- `GameManagers.Instance.AllPlayers`에서 플레이어 목록을 가져온다.
- 읽을 수 없는 NetworkObject는 제외한다.
- 체력 내림차순, `playerId` 기준으로 순위를 정한다.
- Addressables로 `UI_Slot_PlayerRank` 슬롯을 생성한다.
- 플레이어 수나 체력 변화가 있으면 정렬과 슬롯 표시를 갱신한다.

현재 랭킹 분배 로직은 EditMode 테스트가 있는 것으로 확인된다. UI Toolkit 이전 시에도 정렬/분배 로직은 유지하고, 슬롯 렌더링만 VisualElement 기반으로 교체하는 것이 좋다.

### 6. ShopUIController / ShopSlot

상점 UI는 Prepare 단계의 핵심 입력 UI다.

현재 기능은 다음과 같다.

- 상점 슬롯 목록 표시
- 유닛 아이콘, 이름, 비용, 별 등급 표시
- 리롤 버튼과 리롤 비용 표시
- 상점 표시/숨김 상태 관리
- 상점 데이터 중복 갱신 방지
- `RerollShopCommand`를 통해 리롤 요청
- `BuyUnitCommand`를 통해 유닛 구매 요청
- 구매 성공 이벤트를 받으면 해당 슬롯을 구매 완료 상태로 표시
- Addressables로 유닛 아이콘 Sprite를 비동기 로딩하고 해제

상점은 UI Toolkit으로 옮겨도 기능적으로 가능하다. 중요한 점은 리롤/구매가 반드시 기존처럼 `CommandProcessor.RequestCommandExecution()` 경로를 거쳐야 한다는 것이다. UI Toolkit 버튼 콜백에서 직접 골드나 상점 상태를 변경하면 안 된다.

### 7. AugmentUIController / AugmentSlot

증강 UI는 증강 선택 이벤트를 받아 패널을 표시하고 선택 커맨드를 보낸다.

현재 기능은 다음과 같다.

- `GameEvents.OnAugmentPhaseStart`를 구독한다.
- UI 오브젝트가 비활성화 상태여도 이벤트를 받을 수 있게 설계되어 있다.
- 로컬 플레이어에게 해당하는 증강 이벤트만 처리한다.
- 중복 표시를 막기 위해 프레젠테이션 버전과 트리거 키를 사용한다.
- UI Pool 상태와 실제 GameObject 활성 상태가 어긋나지 않도록 `UIManagers.GetUIElement("UI_Pnl_Augment")`를 통해 활성화한다.
- 선택 시 `SelectAugmentCommand`를 요청한다.
- 선택 후 패널을 숨기고 풀에 반환한다.

UI Toolkit 전환 시 가장 중요한 점은 "숨겨져 있어도 이벤트를 받을 수 있는 컨트롤러" 구조를 유지하는 것이다. 단순히 VisualElement를 제거했다가 다시 만드는 방식은 증강 이벤트 타이밍과 충돌할 수 있다.

### 8. AttackSequenceUIController

공격 시퀀스 UI는 전투 중 공격자가 몬스터와 마법 스크롤을 선택하는 UI다.

현재 기능은 다음과 같다.

- `UI_Pnl_AttackSequence`를 동적으로 로드한다.
- 로컬 플레이어가 공격자일 때만 패널을 표시한다.
- 몬스터 슬롯 최대 9개를 표시한다.
- 마법 스크롤 슬롯 최대 5개를 표시한다.
- 몬스터 풀 변경, 스크롤 풀 변경, 전투 시퀀스 시작, 게임 상태 변경 이벤트를 받아 갱신한다.
- 몬스터 슬롯 선택 시 `AttackSequenceManager.SelectMonsterSlot(slotIndex)`를 호출한다.
- 스크롤 슬롯 선택 시 `AttackSequenceManager.SelectMagicScroll(scrollData)`를 호출한다.
- `AttackSequenceManager`가 선택 상태를 다시 UI에 동기화할 수 있도록 `SyncMonsterSelectionFromManager`, `SyncScrollSelectionFromManager`, `RefreshUI`를 제공한다.

UI Toolkit 이전은 가능하지만, 기존 `AttackSequenceManager`가 `AttackSequenceUIController.Instance`에 직접 접근하는 결합이 있다. 따라서 바로 삭제하지 말고 기존 클래스가 UI Toolkit 뷰를 감싸는 어댑터 역할을 하게 만드는 방식이 안전하다.

### 9. BattleTransitionUI

전투 전환 UI는 공격/방어 전환, 시퀀스 전환을 보여주는 애니메이션 UI다.

현재 기능은 다음과 같다.

- `PlayAttackTransitionAsync`
- `PlayDefenseTransitionAsync`
- `PlaySequenceTransitionAsync`
- CanvasGroup fade
- RectTransform 이동
- flash Image
- TextMeshPro 텍스트
- 선택적 오디오 재생

UI Toolkit에서도 opacity, translate, scheduled update, coroutine을 통해 구현 가능하다. 다만 기존 RectTransform 기반 애니메이션 코드를 그대로 사용할 수 없으므로 별도 애니메이션 구현이 필요하다.

### 10. UnitDetailPanelController

유닛 상세 패널은 선택한 유닛의 정보를 표시한다.

현재 표시 내용은 다음과 같다.

- 이름
- 체력
- 공격력
- 방어력
- 마법 저항
- 공격 속도
- 공격 범위
- 공격 타입
- 블록 수
- 마나 재생 타입
- 스킬 설명
- 스킬 자동/수동 발동 토글

추가로 패널 표시 시 `FieldManager.ShowRanges(unit)`를 호출하여 공격/스킬 범위를 보여주고, 패널이 꺼질 때 범위를 제거한다.

UI Toolkit으로 이전 가능하지만 주의가 필요하다. 특히 스킬 자동/수동 토글은 현재 `unit.currentSkillActivationType`을 직접 변경한다. 이 값이 네트워크로 동기화되어야 하거나 권한 검증이 필요한 값이라면 추후 커맨드 경로로 바꾸는 것이 좋다.

### 11. UnitSellPanelController / WallRemovePanelController

유닛 판매 패널과 벽 제거 패널은 월드 오브젝트 위치를 따라다니는 UI다.

현재 기능은 다음과 같다.

- 선택된 유닛 또는 벽 위치에 패널을 붙인다.
- LateUpdate에서 오브젝트 위치를 따라간다.
- 카메라를 바라보도록 billboard 처리한다.
- Prepare 상태에서만 동작한다.
- 로컬 플레이어가 제어하는 필드에서만 동작한다.
- 판매는 `SellUnitCommand`를 요청한다.
- 벽 제거는 `RemoveWallCommand`를 요청한다.
- CommandProcessor가 없을 때 직접 처리하는 fallback이 있다.

이 영역은 UI Toolkit으로 바로 옮기기 어렵다. UI Toolkit 런타임 패널은 기본적으로 화면 공간 UI에 적합하고, UGUI의 world-space Canvas처럼 오브젝트 자식으로 붙는 방식과 다르다. 초기 전환 단계에서는 UGUI 유지가 안전하다.

### 12. StatusBarUI / UIBillboard

`StatusBarUI`는 유닛, 몬스터, 벽의 체력/마나 상태를 월드 공간에 표시한다.

현재 기능은 다음과 같다.

- 체력바 표시
- 마나바 표시
- 수동 스킬 버튼 표시
- 로컬 플레이어 소유 유닛이고, 수동 스킬이고, 마나가 가득 찼고, 전투 상태일 때만 수동 스킬 버튼 표시
- 수동 스킬 버튼 클릭 시 `ActivateSkillCommand` 요청
- 몬스터/벽 체력바는 피해를 입었을 때만 표시
- `UIBillboard`가 카메라를 바라보도록 회전 처리

이 UI는 UI Toolkit 전환 위험이 가장 높다. 화면 공간으로 다시 계산해 오버레이를 띄우는 방식은 가능하지만, 좌표 변환, 카메라, 해상도, 가림 처리, 많은 개체 수에 따른 성능 이슈를 새로 해결해야 한다. 초기 리팩터링에서는 UGUI를 유지하는 것을 권장한다.

### 13. 옵션, 사운드, 버전, 결과 패널

게임 내 옵션 버튼은 옵션/사운드/버전 패널을 전환한다. GameOver 시에는 Victory 또는 Defeat 패널이 표시된다.

주의할 점은 현재 버튼 코드에서 `"OptionCanvas"`, `"SoundCanvas"`, `"VersionCanvas"` 같은 키를 사용하는 반면, `UIManager.prefab` 쪽 목록에는 `UI_Pnl_Option`, `UI_Pnl_Sound`, `UI_Pnl_Version` 이름이 보인다는 점이다. Addressables alias로 해결되고 있을 수도 있지만, 레거시 키 불일치일 가능성이 있다. UI Toolkit으로 옮기기 전에 패널 키를 정리하는 것이 좋다.

## UI Toolkit 전환 가능 여부

결론부터 말하면 `03_Game` UI Toolkit 리팩터링은 가능하다. 하지만 "모든 UI를 즉시 UI Toolkit으로 교체"하는 것은 권장하지 않는다.

### UI Toolkit으로 이전하기 좋은 영역

다음 UI는 화면 공간 UI이므로 UI Toolkit 이전에 적합하다.

- Player HUD
- Phase Timer
- Player Ranking
- Shop
- Augment Selection
- Attack Sequence
- Battle Transition
- Option / Sound / Version
- Victory / Defeat
- Unit Detail Panel

이 UI들은 주로 텍스트, 버튼, 이미지, 슬롯 리스트, 패널 표시/숨김으로 구성되어 있다. UI Toolkit의 UXML/USS, VisualElement, Button, Label, ListView 또는 커스텀 슬롯으로 충분히 대체할 수 있다.

### 초기 단계에서 UGUI 유지가 좋은 영역

다음 UI는 초기 전환에서 UGUI 유지가 안전하다.

- `StatusBarUI`
- `UnitSellPanelController`
- `WallRemovePanelController`
- 월드 오브젝트에 붙는 선택/상태 UI
- 공격/스킬 범위 표시처럼 월드 좌표와 직접 연결된 표시물

이 영역은 UI Toolkit으로도 구현은 가능하지만, 단순 포팅이 아니라 새 오버레이 시스템에 가깝다. 먼저 화면 공간 UI를 안정적으로 이전한 뒤 별도 단계로 검토하는 것이 좋다.

## 추천 전환 방식

### 추천안: 하이브리드 전환

처음부터 UGUI를 제거하지 말고, `03_Game` 씬에 UI Toolkit 루트를 추가한 뒤 화면 공간 UI부터 단계적으로 이전한다.

권장 구조는 다음과 같다.

| 영역 | 권장 방식 |
| --- | --- |
| 화면 공간 HUD/패널 | UI Toolkit으로 이전 |
| 월드 공간 체력바/판매/벽 제거 | UGUI 유지 |
| 기존 `UIManagers` | 초기에는 유지 |
| 새 UI Toolkit 루트 | `UIDocument` 기반 신규 루트 추가 |
| 기존 컨트롤러 | 바로 삭제하지 말고 어댑터 또는 브리지로 점진 교체 |
| 커맨드 요청 | 기존 `CommandProcessor` 경로 유지 |
| 네트워크 상태 반영 | 기존 `GameEvents`, `GameManagers` 흐름 유지 |

이 방식을 추천하는 이유는 다음과 같다.

1. 현재 UI는 게임 로직과 직접 연결된 부분이 많다.
2. `UIManagers.GetUIElement()` 호출이 여러 시스템에 퍼져 있다.
3. 증강/상점/공격 시퀀스는 네트워크 동기화 타이밍에 민감하다.
4. 월드 공간 UI는 UI Toolkit으로 바꿀 때 별도 좌표/성능 문제가 생긴다.
5. 화면 공간 UI부터 바꾸면 모바일 대응, 해상도 대응, UI 스타일 통일 효과를 먼저 얻을 수 있다.

## 권장 신규 구조

### 1. GameUIToolkitRoot

`03_Game` 씬에 `UIDocument` 기반 루트를 추가한다.

역할은 다음과 같다.

- UXML/USS 로드
- HUD, Shop, Augment, Ranking, AttackSequence 등 화면 공간 UI의 루트 보관
- Safe Area 적용
- 모바일/PC 레이아웃 분기
- UI Toolkit 패널 준비 완료 상태 제공

### 2. GameUIBridge 또는 GameUIService

기존 게임 코드가 직접 VisualElement를 알지 않도록 중간 계층을 둔다.

예상 역할은 다음과 같다.

- `ShowShop`
- `HideShop`
- `SetShopItems`
- `ShowAugmentChoices`
- `HideAugment`
- `ShowAttackSequence`
- `RefreshAttackSequence`
- `UpdateHud`
- `UpdateRanking`
- `ShowVictory`
- `ShowDefeat`
- `ShowOption`
- `SetPhaseTimer`

이 계층은 기존 `GameManagers`, `GameEvents`, `CommandProcessor`와 UI Toolkit View 사이를 연결한다.

### 3. 기존 컨트롤러 호환 계층

전환 중에는 기존 클래스 이름과 API를 일부 유지하는 것이 좋다.

예를 들어 `AttackSequenceManager`는 현재 `AttackSequenceUIController.Instance`에 직접 접근한다. 이 경우 `AttackSequenceUIController`를 바로 삭제하지 말고, 내부적으로 UI Toolkit View에 요청을 전달하는 어댑터로 바꾸는 편이 안전하다.

`AugmentUIController`도 이벤트 구독과 중복 표시 방지 로직이 중요하므로, 먼저 로직을 유지한 채 표시 부분만 UI Toolkit으로 분리하는 것이 좋다.

## 단계별 리팩터링 계획

### 1단계: 현재 UI 계약 정리

작업 내용:

- `UIManagers.GetUIElement()`로 열리는 모든 게임 UI 키 정리
- Addressables 키와 프리팹 이름 불일치 확인
- `UI_Pnl_Option`과 `OptionCanvas` 같은 레거시 키 확인
- 화면 공간 UI와 월드 공간 UI 분리
- UI별 입력 커맨드 경로 확인

목표:

- 어떤 UI를 먼저 옮겨도 되는지 확정한다.
- 기존 네트워크/커맨드 계약을 깨지 않도록 기준을 만든다.

### 2단계: UI Toolkit 루트 추가

작업 내용:

- `03_Game` 씬에 `UIDocument` 루트 추가
- 게임 전용 UXML/USS 생성
- PC/모바일 공통 Safe Area 적용 구조 추가
- PanelSettings 확인
- 기존 UGUI `GameCanvas`는 유지

목표:

- UGUI와 UI Toolkit이 같은 씬에서 공존하게 만든다.
- 아직 기존 UI 동작은 바꾸지 않는다.

### 3단계: HUD와 Phase Timer 이전

작업 내용:

- 골드, 라운드, 벽 개수 표시 이전
- 상점 토글 버튼 이전
- 벽 배치 버튼 이전
- 상대 이름 표시 이전
- 페이즈 타이머 표시 이전

주의점:

- Prepare 상태에서만 버튼이 보이는 조건을 유지한다.
- Host Migration 후 참조 갱신 로직을 유지한다.
- 버튼 클릭은 기존 `ShopUIController`, `FieldManager` 흐름으로 연결한다.

목표:

- 가장 노출 빈도가 높은 화면 공간 UI를 먼저 UI Toolkit으로 안정화한다.

### 4단계: 옵션/결과 패널 이전

작업 내용:

- Option 패널 이전
- Sound 패널 이전
- Version 패널 이전
- Victory/Defeat 패널 이전
- 패널 키 이름 정리

주의점:

- 기존 `"OptionCanvas"` 계열 키와 `UI_Pnl_Option` 계열 키가 실제로 어떻게 연결되어 있는지 확인한다.
- `GameManagers`의 GameOver UI 표시 흐름을 유지한다.

목표:

- 네트워크 영향이 상대적으로 낮은 패널을 UI Toolkit으로 전환한다.

### 5단계: Ranking 이전

작업 내용:

- 플레이어 랭킹 슬롯을 VisualElement 기반으로 이전
- 기존 정렬 기준 유지
- 좌우 분배 로직 유지
- 기존 EditMode 테스트 유지 또는 보강

주의점:

- 매 프레임 VisualElement를 새로 만들지 않는다.
- 플레이어 수/체력 변화가 있을 때만 필요한 범위에서 갱신한다.

목표:

- 동적 리스트 UI를 UI Toolkit 방식으로 이전한다.

### 6단계: Shop 이전

작업 내용:

- 상점 슬롯 UI Toolkit 이전
- 유닛 아이콘 로딩 처리 이전
- 리롤 버튼 이전
- 구매 버튼 이전
- 구매 완료 표시 이전

주의점:

- 리롤은 반드시 `RerollShopCommand`를 사용한다.
- 구매는 반드시 `BuyUnitCommand`를 사용한다.
- UI에서 직접 골드, 상점 목록, 유닛 소유 상태를 수정하지 않는다.
- Addressables 아이콘 로딩 핸들 해제를 관리한다.
- `EnsureGameUIReadyForSyncCommands()`가 기다릴 수 있는 준비 완료 상태를 제공한다.

목표:

- Prepare 핵심 입력 UI를 UI Toolkit으로 이전한다.

### 7단계: Augment 이전

작업 내용:

- 증강 선택 슬롯 UI Toolkit 이전
- 증강 선택 버튼 이전
- 패널 표시/숨김 이전
- 중복 이벤트 방지 로직 유지

주의점:

- UI가 숨겨져 있어도 증강 이벤트를 받을 수 있어야 한다.
- 선택은 반드시 `SelectAugmentCommand`를 사용한다.
- 풀 상태와 표시 상태가 어긋나지 않도록 UI Toolkit 전용 상태 관리가 필요하다.

목표:

- 네트워크 이벤트 기반 UI를 UI Toolkit으로 안정적으로 이전한다.

### 8단계: Attack Sequence 이전

작업 내용:

- 몬스터 슬롯 UI Toolkit 이전
- 마법 스크롤 슬롯 UI Toolkit 이전
- 선택 상태 표시 이전
- 공격자에게만 표시되는 조건 유지
- `AttackSequenceManager`와의 동기화 메서드 유지

주의점:

- 기존 `AttackSequenceUIController.Instance` 의존을 한 번에 제거하지 않는다.
- 먼저 기존 컨트롤러가 UI Toolkit View를 감싸는 구조로 만든다.
- 몬스터/스크롤 선택은 현재처럼 Manager 선택 상태만 바꾸고, 실제 게임 결과는 기존 권한 흐름을 따른다.

목표:

- 전투 중 동적 선택 UI를 UI Toolkit으로 이전한다.

### 9단계: Unit Detail 이전

작업 내용:

- 유닛 상세 정보 패널 UI Toolkit 이전
- 스킬 설명 로딩 표시 이전
- 스킬 자동/수동 토글 이전
- 범위 표시 호출 유지

주의점:

- `FieldManager.ShowRanges(unit)`와 `ClearRanges()` 호출 타이밍을 유지한다.
- 스킬 자동/수동 토글이 직접 유닛 상태를 바꾸는 현재 구조는 권한/동기화 관점에서 재검토가 필요하다.

목표:

- 선택 유닛 정보 UI를 화면 공간 UI Toolkit으로 이전한다.

### 10단계: 월드 공간 UI 검토

작업 내용:

- `StatusBarUI`를 계속 UGUI로 유지할지 결정
- `UnitSellPanelController`, `WallRemovePanelController` 유지 여부 결정
- UI Toolkit 오버레이 방식으로 대체 가능한지 별도 실험

주의점:

- 월드 좌표를 패널 좌표로 변환해야 한다.
- 카메라 변경, 해상도 변경, Safe Area, 오브젝트 가림, 많은 개체 수 성능을 확인해야 한다.
- 즉시 전환할 필요가 없다.

목표:

- 월드 공간 UI를 무리하게 이전하지 않고 안정성을 우선한다.

### 11단계: UGUI 제거 또는 축소

작업 내용:

- 화면 공간 UGUI 패널 제거
- 더 이상 쓰지 않는 Addressables 키 정리
- `UIManagers` 역할 축소
- 월드 공간 UGUI만 남길지 결정

주의점:

- E2E 테스트에서 Prepare, Battle, GameOver, Host Migration 흐름을 확인한 뒤 제거한다.

목표:

- 중복 UI를 제거하고 유지보수 범위를 줄인다.

## 전환 시 주요 위험 요소

### 1. UIManagers 생명주기와 UI Toolkit 생명주기 차이

현재 게임 코드는 `UIManagers.GetUIElement()`가 GameObject를 반환한다는 전제에 묶여 있다. UI Toolkit은 VisualElement 기반이므로 같은 방식으로 반환할 수 없다.

대응:

- 초기에는 `UIManagers`를 유지한다.
- 새 `GameUIService`가 UI Toolkit 영역을 관리한다.
- 기존 호출이 많은 UI는 어댑터를 통해 점진 이전한다.

### 2. 증강/상점 동기화 타이밍

빌드 클라이언트에서는 UI가 준비되기 전에 증강/상점 동기화 명령이 도착할 수 있다. 현재는 `EnsureGameUIReadyForSyncCommands()`가 이를 방어한다.

대응:

- UI Toolkit 루트에 준비 완료 상태를 둔다.
- Shop/Augment View가 바인딩 가능해진 뒤 준비 완료로 표시한다.
- 동기화 명령이 먼저 오면 큐잉하거나 기존 대기 로직을 유지한다.

### 3. Host Migration 중 네트워크 참조 무효화

현재 UI 코드 곳곳에서 `NetworkObject`가 무효화되는 상황을 방어한다. UI Toolkit 전환 시 단순 바인딩으로 바꾸면 죽은 참조를 계속 읽는 문제가 생길 수 있다.

대응:

- GameManagers/localPlayer/player list 참조는 필요 시 재해결한다.
- NetworkObject 유효성 체크를 유지한다.
- Host Migration 이후 UI 재바인딩 루틴을 둔다.

### 4. 월드 공간 UI

체력바, 판매 패널, 벽 제거 패널은 UGUI world-space Canvas에 최적화되어 있다.

대응:

- 초기에는 UGUI 유지.
- 나중에 별도 실험으로 UI Toolkit screen overlay 변환을 검토.
- 성능과 좌표 정확성을 확인하기 전에는 제거하지 않는다.

### 5. 입력 중복

UGUI와 UI Toolkit이 공존하면 EventSystem, GraphicRaycaster, PanelSettings 입력 처리에서 중복 입력이나 클릭 충돌이 생길 수 있다.

대응:

- 한 화면 영역은 한 UI 시스템만 입력을 소유하게 한다.
- 전환된 UGUI 패널은 비활성화하거나 raycast를 끈다.
- 모바일 터치에서 스크롤/버튼 오탭을 별도로 확인한다.

### 6. Addressables와 아이콘 로딩

현재 UGUI는 Sprite를 `Image.sprite`에 넣는다. UI Toolkit은 `Image` 또는 `style.backgroundImage`로 표시해야 한다.

대응:

- 아이콘 로딩/해제 책임을 ViewModel 또는 View Binder에 명확히 둔다.
- 슬롯 재사용 시 이전 아이콘 핸들을 해제한다.
- 슬롯이 숨겨진 뒤 늦게 완료된 비동기 로딩 결과가 잘못 들어가지 않게 버전 체크를 둔다.

### 7. 폰트와 한글 표시

기존 UGUI는 TextMeshPro를 사용한다. UI Toolkit은 폰트/텍스트 설정이 다르므로 한글 표시 품질과 줄바꿈이 달라질 수 있다.

대응:

- UI Toolkit용 TextSettings와 폰트 에셋을 확인한다.
- 모바일 해상도에서 한글 줄바꿈과 버튼 텍스트 넘침을 확인한다.

### 8. 성능

Ranking, Shop, AttackSequence는 슬롯 UI를 반복해서 갱신한다. UI Toolkit에서도 매 프레임 VisualElement를 생성/삭제하면 성능 문제가 생길 수 있다.

대응:

- 슬롯 VisualElement는 재사용한다.
- 데이터 변경 시에만 바인딩한다.
- 매 프레임 텍스트를 무조건 쓰지 않고 값 변경을 비교한다.

## 테스트 계획

UI Toolkit 전환은 화면만 바꾸는 작업처럼 보이지만 실제로는 네트워크/커맨드 흐름에 영향을 줄 수 있다. 단계별로 다음 테스트가 필요하다.

### 기본 확인

- `03_Game` 씬 진입 시 UI Toolkit 루트가 생성되는지
- 기존 UGUI와 UI Toolkit이 겹쳐 클릭을 가로채지 않는지
- PC 해상도 변경 시 레이아웃이 깨지지 않는지
- 모바일 해상도와 Safe Area에서 버튼이 눌리기 쉬운지

### Prepare 단계

- 골드/라운드/벽 개수 표시
- 상점 열기/닫기
- 리롤 버튼
- 유닛 구매 버튼
- 벽 배치 버튼
- 유닛 상세 표시
- 유닛 판매
- 벽 제거

### Augment 단계

- 증강 선택지가 표시되는지
- 중복 표시가 발생하지 않는지
- 선택 후 패널이 닫히는지
- 선택 커맨드가 정상 처리되는지

### Battle 단계

- 상점/증강 UI가 숨겨지는지
- 페이즈 타이머가 정상 표시되는지
- 공격/방어 전환 UI가 표시되는지
- 공격자에게만 Attack Sequence UI가 표시되는지
- 몬스터/스크롤 선택이 정상 반영되는지
- 체력바/마나바가 기존처럼 표시되는지
- 수동 스킬 버튼이 조건에 맞게 표시되고 동작하는지

### GameOver 단계

- 승리 플레이어에게 Victory 표시
- 패배 플레이어에게 Defeat 표시
- Spectate 버튼이나 이후 흐름이 기존과 동일한지

### Host Migration / Reconnect

- Host Migration 후 HUD가 다시 바인딩되는지
- Shop/Augment/AttackSequence가 잘못된 이전 참조를 읽지 않는지
- 죽은 NetworkObject 참조 예외가 없는지
- reconnect 이후 UI 상태가 현재 게임 상태와 일치하는지

## 최종 권장 결론

`03_Game` UI를 UI Toolkit으로 리팩터링하는 것은 가능하다. 다만 현재 UI는 게임 상태, 네트워크 명령, Addressables, Host Migration, 월드 공간 UI와 강하게 연결되어 있으므로 전체를 한 번에 교체하면 위험하다.

권장 방향은 다음과 같다.

1. `03_Game`에 UI Toolkit 루트를 추가한다.
2. 기존 UGUI는 유지한 채 HUD, Timer, Option, Result 같은 낮은 위험 UI부터 이전한다.
3. 이후 Ranking, Shop, Augment, AttackSequence를 기존 커맨드/이벤트 계약을 유지하면서 이전한다.
4. `StatusBarUI`, `UnitSellPanelController`, `WallRemovePanelController` 같은 월드 공간 UI는 초기에는 UGUI로 유지한다.
5. 모든 화면 공간 UI가 안정화된 뒤 월드 공간 UI의 UI Toolkit 대체 여부를 별도 검토한다.

간단히 요약하면, 화면에 붙어 있는 인게임 패널은 UI Toolkit으로 옮겨도 좋고, 유닛이나 벽에 붙어 따라다니는 월드 공간 UI는 당장 옮기지 않는 것이 좋다. 이 방식이 가장 안전하게 모바일 대응과 UI 구조 통일을 얻을 수 있는 방법이다.
