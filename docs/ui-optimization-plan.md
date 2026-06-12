# MDF UI 최적화 계획

## 목적

이 문서는 현재 MDF UI 코드 분석 결과와 실행 가능한 최적화 계획을 정리한다.
대상은 특히 게임 씬에서 함께 사용 중인 uGUI 레거시 UI와 UI Toolkit 런타임 UI이다.

목표는 UI의 시각 디자인을 바꾸는 것이 아니다. 목표는 불필요한 런타임 작업을 줄이고,
UI 갱신을 이벤트 기반으로 바꾸며, 중복 비동기 에셋 로드를 줄이고, 멀티플레이 권한
규칙과 입력 동작을 보존하는 것이다.

## 범위

주요 범위:

- 게임 준비 단계 HUD, 상점, 증강, 자원 표시, 공격 시퀀스 UI.
- 플레이어 랭킹 HUD.
- UI Toolkit이 활성화된 상태에서도 남아 있는 레거시 uGUI 브리지 코드.
- 필드 입력 차단과 UI passthrough 판정.
- 현재 UI Toolkit 동작을 보호하는 테스트.

첫 번째 최적화 패스에서 제외할 범위:

- 아트 리디자인.
- 모든 레거시 uGUI 패널의 즉시 제거.
- 게임플레이 커맨드 규칙 변경.
- Photon Fusion State Authority 소유권 변경.
- vendor 패키지 또는 생성 파일 수정.

## 현재 UI 구조

### 런타임 UI Toolkit

`Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`

- 준비 단계 UI를 위한 런타임 생성 `UIDocument`를 관리한다.
- 상점 카드, 증강 카드, 자원 HUD, 벽 설치 버튼, 옵션 버튼, 공격 시퀀스 카드,
  레거시 브리지 helper를 한 파일에서 처리한다.
- UXML/USS는 `Assets/Resources/UI/GamePrepare`에서 로드한다.
- 주요 리소스:
  - `Mdfproject/Assets/Resources/UI/GamePrepare/GamePreparePanels.uxml`
  - `Mdfproject/Assets/Resources/UI/GamePrepare/GamePreparePanelsStyles.uss`

`Mdfproject/Assets/Scripts/UI/RankingUIController.cs`

- 런타임 생성 UI Toolkit 랭킹 HUD를 관리한다.
- `Assets/Resources/UI/PlayerRanking` 리소스를 사용한다.
- 주요 게임 이벤트에서 갱신되며, 추가로 주기적 polling도 수행한다.

### 레거시 uGUI

`Mdfproject/Assets/Scripts/UI/ShopUIController.cs`

- 레거시 상점 패널이다.
- 현재도 `GamePrepareUIToolkitController`로 연결되는 호환 브리지 역할을 한다.
- `OnShopRefreshed`, `OnUnitPurchaseSucceeded` 같은 권한 결과 이벤트를 받는다.

`Mdfproject/Assets/Scripts/UI/AugmentUIController.cs`

- 레거시 증강 패널이다.
- 비활성 pooled UI 상태에서도 증강 시작 이벤트를 받을 수 있도록 이벤트 구독을 유지한다.
- UI Toolkit이 사용 가능하면 Toolkit 패널로 라우팅한다.

`Mdfproject/Assets/Scripts/UI/AttackSequence/AttackSequenceUIController.cs`

- 레거시 공격 시퀀스 패널이다.
- UI Toolkit이 사용 가능하면 Toolkit 공격 시퀀스로 라우팅한다.
- 여전히 고정 개수의 레거시 슬롯 인스턴스를 만든다.

`Mdfproject/Assets/Scripts/UI/PlayerHUDController.cs`

- 레거시 자원 HUD 및 상점 토글 HUD이다.
- Toolkit HUD가 활성화되면 숨겨지지만, 프레임 갱신 코드와 브리지 동작은 남아 있다.

`Mdfproject/Assets/Scripts/UI/StatusBarUI.cs`

- 월드 스페이스 체력/마나 및 수동 스킬 버튼 UI이다.
- UI Toolkit 패널 raycaster가 필드 입력을 막는 문제를 피하기 위해 fallback 클릭 판정을 사용한다.

`Mdfproject/Assets/Scripts/UI/WallRemovePanelController.cs`

- 월드 스페이스 벽 제거 패널이다.
- Toolkit raycaster 문제 때문에 fallback 클릭 판정을 사용한다.

`Mdfproject/Assets/Scripts/UI/UnitSellPanelController.cs`

- 월드 스페이스 유닛 판매 패널이다.
- 선택된 유닛에 bind된 동안 `LateUpdate`에서 위치를 갱신한다.

### UI 생성과 풀링

`Mdfproject/Assets/Scripts/UI/UIManagers.cs`

- UI 접근점 역할을 하는 singleton 계열 매니저이다.
- 프리팹 이름 또는 Addressables key 기준으로 `UIPool`을 보유한다.
- `mainCanvas`를 lazy resolve한다.

`Mdfproject/Assets/Scripts/UI/UIPool.cs`

- UI key마다 활성 오브젝트 하나를 유지한다.
- 프리팹 instantiate 또는 Addressables를 통해 인스턴스를 생성한다.

### 입력 경계

`Mdfproject/Assets/Scripts/Managers/MdfInput.cs`

- 포인터 상태와 UI blocking 판정을 중앙화한다.
- `EventSystem.RaycastAll`을 사용한다.
- 빈 UI Toolkit 패널이 필드 클릭을 막지 않도록 Toolkit raycaster passthrough를 특수 처리한다.

## 반드시 유지해야 할 규칙

최적화 후에도 아래 규칙은 반드시 유지되어야 한다.

1. UI는 게임플레이 행동을 기존 커맨드 경로로 요청해야 한다.
   - 상점 구매는 `BuyUnitCommand`를 사용한다.
   - 상점 리롤은 `RerollShopCommand`를 사용한다.
   - 증강 선택은 `SelectAugmentCommand`를 사용한다.

2. UI는 durable gameplay state를 권한 승인 상태 또는 결과 이벤트 기준으로만 갱신해야 한다.
   - 로컬 버튼 클릭을 구매 성공이나 선택 성공으로 간주하면 안 된다.
   - persistent state를 RPC side effect만으로 만들면 안 된다.

3. UI Toolkit 패널 raycaster는 빈 공간의 보드 클릭을 막으면 안 된다.
   - 실제 카드와 버튼은 여전히 입력을 막고 클릭을 처리해야 한다.
   - 기존 field input fallback 동작은 의도된 동작이다.

4. HumanBot과 MP automation 동작은 유지되어야 한다.
   - 테스트 전용 HumanBot 경로는 보드 행동 전에 상점을 닫을 수 있다.
   - 이 동작을 일반 production gameplay 동작으로 바꾸면 안 된다.

5. Host Migration과 reconnect 복구 후 UI 참조가 갱신되어야 한다.
   - 매 프레임 reference refresh를 제거하려면 migration/rebind 이벤트로 대체되어야 한다.

## 분석 결과

### F1. GamePrepareUIToolkitController 책임이 과도하다

현재 이 컨트롤러는 아래 책임을 모두 가진다.

- `UIDocument`와 `PanelSettings` 생성.
- UXML element bind.
- 상점 카드 view 로직.
- 증강 카드 view 로직.
- 공격 몬스터 및 스크롤 카드 로직.
- HUD 상태 관리.
- safe area 및 layout scale 계산.
- 레거시 uGUI bridge 함수.
- 입력 blocking helper.
- 런타임 reference rebinding.

이 구조에서는 작은 수정도 prepare, battle, legacy UI, input, test를 동시에 건드릴 수 있다.
최적화 전에는 기능 분리 계획이 필요하다.

### F2. Prepare HUD가 아직 프레임 기반 갱신을 수행한다

`GamePrepareUIToolkitController.Update()`는 매 프레임 runtime reference와 HUD state를 갱신한다.
일부 텍스트는 cached value로 보호되어 있지만, 메서드 자체는 값이 바뀌지 않아도 반복 실행된다.

`PlayerHUDController.Update()`도 레거시 경로에서 gold, round, wall 텍스트를 매 프레임 할당한다.

### F3. Ranking UI는 주기적 polling과 scene-wide lookup을 사용한다

`RankingUIController`는 Toolkit 표시를 `0.25`초마다 갱신하며, refresh 중
`FindObjectsOfType<NetworkPlayer>()`를 호출한다. 플레이어 수가 최대 4명이라 큰 병목은 아닐 수
있지만, 항상 보이는 HUD에서 반복할 필요는 낮다.

### F4. 아이콘 로딩이 여러 UI 경로에서 중복된다

상점 유닛 아이콘은 아래 경로에서 각각 로드된다.

- 레거시 `ShopSlot`.
- Toolkit `ShopCardView`.

몬스터 아이콘은 아래 경로에서 각각 로드된다.

- 레거시 `MonsterSlotUI`.
- Toolkit `MonsterCardView`.

일부 view는 stale async 결과를 막고 있지만 패턴이 일관되지 않다. 공용 UI sprite cache도 없다.

### F5. 입력 코드는 맞지만 same-frame cache 여지가 있다

`MdfInput`의 `EventSystem.RaycastAll` 호출은 필요하다. Toolkit 패널 raycaster passthrough 처리가
없으면 필드 클릭 회귀가 난다. 따라서 raycast를 제거하는 것이 아니라, 같은 frame/같은 pointer
위치에서 중복 호출을 줄이는 방향이 맞다.

### F6. 레거시 UI 숨김이 object search에 의존한다

`GamePrepareUIToolkitController.HideLegacyContent()`는 `FindObjectOfType`와 `FindObjectsOfType`로
레거시 controller를 찾는다. 한 번만 수행된다면 허용 가능하지만, 책임이 분산되어 있어 반복 검색
회귀가 생기기 쉽다.

### F7. 테스트가 source string assertion을 많이 사용한다

현재 UI 테스트는 중요한 회귀를 막고 있지만, 구현 문자열을 직접 검사하는 경우가 많다.
큰 리팩터링에서는 동작이 같아도 테스트가 깨질 수 있다. 점진적으로 public pure method,
UXML clone 검증, runtime object 검증 중심으로 옮겨야 한다.

## 최적화 전략

작은 phase 단위로 진행한다. 각 phase는 다음 phase로 넘어가기 전에 compile과 UI 동작을 검증해야 한다.

권장 순서:

1. 측정 기준과 dirty-state helper를 먼저 추가한다.
2. 프레임 기반 텍스트/reference 갱신을 줄인다.
3. 랭킹 player/network data를 cache한다.
4. 공용 sprite cache를 추가한다.
5. 레거시 bridge reference용 UI registry를 도입한다.
6. `GamePrepareUIToolkitController`를 작은 collaborator로 나눈다.
7. brittle source-string test를 behavior 중심 테스트로 바꾼다.

## Phase 0: 기준 측정

### 목표

나중에 변경이 실제로 도움이 되었는지 비교할 수 있는 수치를 확보한다.

### 작업

- 개발 빌드 전용 counter를 추가한다.
  - prepare HUD refresh 호출 수.
  - ranking refresh 호출 수.
  - `MdfInput`에서 발생한 `EventSystem.RaycastAll` 호출 수.
  - shop unit icon load request 수.
  - monster icon load request 수.
  - UI Toolkit card family별 bind 호출 수.
- 모든 counter는 `UNITY_EDITOR || DEVELOPMENT_BUILD`로 제한한다.
- 로그는 test mode 또는 명시적 local debug flag가 있을 때만 제한적으로 출력한다.

### 후보 파일

- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`
- `Mdfproject/Assets/Scripts/UI/RankingUIController.cs`
- `Mdfproject/Assets/Scripts/Managers/MdfInput.cs`
- `Mdfproject/Assets/Scripts/UI/ShopSlot.cs`
- `Mdfproject/Assets/Scripts/UI/AttackSequence/MonsterSlotUI.cs`

### 완료 기준

- prepare smoke run 중 counter를 확인할 수 있다.
- production build에는 counter가 포함되지 않는다.
- UI 동작 변화가 없다.

## Phase 1: Prepare HUD를 이벤트 기반으로 전환

### 목표

정적이거나 드물게 바뀌는 UI 상태를 매 프레임 갱신하지 않는다.

### 작업

- `Update()`에서 무조건 수행하는 `RefreshRuntimeReferences()`를 dirty event 기반으로 대체한다.
  - `OnGameManagersReady`
  - `OnGameStateRestored`
  - `OnHostMigrationCompleted`
  - local player relink 이벤트가 있다면 해당 이벤트
- 시각적으로 시간에 따라 변하는 값만 가벼운 timer update로 남긴다.
- timer label은 표시되는 정수 값이 바뀔 때만 갱신한다.
- 아래 값을 last displayed state로 추적한다.
  - shop open/closed
  - wall mode active
  - gold
  - wall count
  - round
  - option button availability
- `UpdateHudState(force: false)`는 dirty bit가 없으면 즉시 return한다.

### 후보 파일

- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`
- `Mdfproject/Assets/Scripts/UI/PlayerHUDController.cs`
- `Mdfproject/Assets/Scripts/UI/PhaseTimerUI.cs`

### 위험

- Host Migration 이후 `GameManagers.Instance` 또는 `localPlayer`가 교체될 수 있다.
- local player relink 전에 prepare UI가 먼저 뜰 수 있다.

### 대응

- UI가 visible이고 local player가 아직 유효하지 않을 때만 낮은 빈도의 fallback rebind를 둔다.
  예: `0.5`초 또는 `1.0`초 간격.
- migration 및 restored-state 이벤트에서는 명시적으로 refresh한다.

### 완료 기준

- gold, wall count, round, shop label을 매 프레임 할당하지 않는다.
- timer label은 표시 정수 값이 바뀔 때만 갱신된다.
- Host Migration UI recovery 후에도 참조가 정상 갱신된다.

## Phase 2: Ranking UI refresh cache

### 목표

플레이어 표시 상태가 바뀌지 않았으면 ranking card를 다시 bind하지 않는다.

### 작업

- durable `playerId` 기준으로 `NetworkPlayer` display data를 cache한다.
- ranking card display signature를 만든다.
  - visible 여부.
  - playerId.
  - display name.
  - health.
  - max health.
  - battle role.
  - self/opponent/reserve slot.
- signature가 바뀐 카드만 bind한다.
- refresh 중 `FindObjectsOfType<NetworkPlayer>()` 대신 cached lookup을 사용한다.
- event coverage가 충분히 증명될 때까지 `0.25`초 polling은 fallback으로 유지한다.

### 후보 파일

- `Mdfproject/Assets/Scripts/UI/RankingUIController.cs`
- 필요 시 `Mdfproject/Assets/Scripts/Network/NetworkPlayer.cs`
- 필요 시 `Mdfproject/Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs`

### 위험

- reconnect 또는 Host Migration 후 ranking 표시가 stale해질 수 있다.
- ranking card 클릭으로 카메라를 이동하는 기능이 잘못된 tracked player를 가질 수 있다.

### 대응

아래 이벤트에서 cache를 무효화한다.

- `OnGameManagersReady`
- `OnGameStateChanged`
- `OnRoundStart`
- `OnBattleSequenceStarted`
- `OnHostMigrationCompleted`
- player registry 변경 이벤트가 있다면 해당 이벤트

### 완료 기준

- health와 role이 변하지 않으면 ranking card bind 호출이 줄어든다.
- self, opponent, reserve card 클릭으로 플레이어 필드 이동이 정상 동작한다.
- 기존 ranking layout 테스트가 통과한다.

## Phase 3: 공용 UI Sprite Cache

### 목표

같은 icon key에 대한 Addressables 또는 asset load를 반복하지 않는다.

### 제안 설계

새 내부 cache를 추가한다.

예:

`Mdfproject/Assets/Scripts/UI/UISpriteCache.cs`

책임:

- 기존 project loader 또는 Addressables를 통해 key로 `Sprite`를 로드한다.
- 반복 key 요청에는 cached sprite를 반환한다.
- 동시에 들어온 같은 key 요청은 하나의 pending load task를 공유한다.
- 필요 시 scene/state cleanup용 `ReleaseUnused()`를 제공한다.
- UI sprite에만 사용한다. gameplay asset loading을 대체하지 않는다.

### 작업

- 레거시 상점 icon load를 cache 경유로 바꾼다.
- Toolkit 상점 icon load를 cache 경유로 바꾼다.
- 레거시와 Toolkit 몬스터 icon load를 cache 경유로 바꾼다.
- 모든 async UI icon assignment에 bind version 또는 token을 넣는다.
- load 완료 전에 슬롯이 다른 데이터로 rebound된 경우 오래된 결과가 sprite를 덮어쓰지 못하게 한다.

### 후보 파일

- `Mdfproject/Assets/Scripts/UI/ShopSlot.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`
- `Mdfproject/Assets/Scripts/UI/AttackSequence/MonsterSlotUI.cs`
- `Mdfproject/Assets/Scripts/UI/AttackSequence/MagicScrollSlotUI.cs`
- 새 파일: `Mdfproject/Assets/Scripts/UI/UISpriteCache.cs`

### 위험

- 잘못된 release logic이 아직 표시 중인 sprite를 invalidate할 수 있다.
- Addressables handle ownership이 불명확해질 수 있다.

### 대응

- 첫 구현은 scene-lifetime UI cache로 단순하게 시작한다.
- view가 반환받은 sprite를 쓰는 동안 개별 handle release를 하지 않는다.
- 사용 패턴이 안정화된 뒤 cleanup 전략을 추가한다.

### 완료 기준

- 안정된 panel에서 같은 icon key는 한 번만 로드된다.
- load 완료 전에 슬롯이 rebound되어도 이전 icon이 표시되지 않는다.
- Unity console에 Addressables release error가 없다.

## Phase 4: 레거시 Bridge Reference용 UI Registry

### 목표

Toolkit bridge 경로에서 hidden object search를 제거한다.

### 제안 설계

UI runtime 쪽에 작은 registry를 추가하거나 기존 `UIManagers`를 확장한다.

가능한 선택지:

- `UIManagers`에 typed registration을 추가한다.
- 별도 `GameUiRegistry`를 만들어 typed controller reference를 관리한다.

등록 대상:

- `ShopUIController`
- `AugmentUIController`
- `PlayerHUDController`
- `GameOptionButton` collection
- `GamePrepareUIToolkitController`
- `RankingUIController`

### 작업

- controller는 `OnEnable`에서 등록하고 `OnDisable`에서 해제한다.
- bridge method의 `FindObjectOfType`를 registry lookup으로 바꾼다.
- migration 또는 legacy scene용 one-time fallback search는 유지한다.

### 후보 파일

- `Mdfproject/Assets/Scripts/UI/UIManagers.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`
- `Mdfproject/Assets/Scripts/UI/ShopUIController.cs`
- `Mdfproject/Assets/Scripts/UI/AugmentUIController.cs`
- `Mdfproject/Assets/Scripts/UI/PlayerHUDController.cs`
- `Mdfproject/Assets/Scripts/Button/GameOptionButton.cs`

### 위험

- pooled inactive UI는 이벤트 도착 시점에 active registry에 없을 수 있다.
- 일부 legacy controller는 inactive 상태에서도 이벤트를 받아야 한다.

### 대응

- "현재 active instance"와 "UIManagers가 알고 있는 pooled instance"를 구분한다.
- inactive augment subscription 동작은 대체 coverage가 확인되기 전까지 제거하지 않는다.

### 완료 기준

- 정상 prepare flow에서 Toolkit bridge가 scene-wide object search를 사용하지 않는다.
- Toolkit이 없는 legacy scene fallback은 여전히 동작한다.

## Phase 5: Same-frame Input Query Cache

### 목표

현재 입력 동작을 유지하면서 같은 frame 내 중복 raycast를 줄인다.

### 작업

- `MdfInput`에 frame cache를 추가한다.
- cache key:
  - frame count
  - pointer position
  - query mode: general UI, field-blocking UI, debug description
- 한 frame의 모든 UI blocking query가 하나의 `RaycastAll` 결과 list를 공유하게 한다.
- 기존 우선순위는 유지한다.
  - wall remove button
  - manual skill button
  - GamePrepare Toolkit blocking element
  - normal UI raycast hit
  - Toolkit passthrough check

### 후보 파일

- `Mdfproject/Assets/Scripts/Managers/MdfInput.cs`
- `Mdfproject/Assets/Scripts/UI/WallRemovePanelController.cs`
- `Mdfproject/Assets/Scripts/UI/StatusBarUI.cs`
- `Mdfproject/Assets/Scripts/UI/RankingUIController.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`

### 위험

- 일부 입력 경로에서는 같은 frame 안에서도 pointer 위치가 바뀔 수 있다.
- debug description은 gameplay check와 같은 결과를 설명해야 한다.

### 대응

- pointer position을 cache key에 포함한다.
- `PointerPosition`이 바뀌면 cache를 무효화한다.
- development-only diagnostic도 cached result와 같은 데이터를 사용한다.

### 완료 기준

- field placement, unit drag, wall remove, skill button click이 정상 동작한다.
- 같은 frame 내 반복 input check가 `RaycastAll`을 여러 번 호출하지 않는다.

## Phase 6: GamePrepareUIToolkitController 분리

### 목표

리스크를 줄이고 이후 최적화를 쉽게 만든다.

### 권장 분리

`GamePrepareUIToolkitController`의 public compatibility API는 유지하되, 내부 책임을 작은 helper로 옮긴다.

가능한 클래스:

- `GamePrepareUiReferences`
  - bind된 `VisualElement`, `Label`, `Image` reference 보관.
- `GamePrepareHudPresenter`
  - resource, wall, shop toggle, option, timer label 관리.
- `GamePrepareShopPresenter`
  - shop card binding과 reroll label 관리.
- `GamePrepareAugmentPresenter`
  - augment card binding 관리.
- `GamePrepareAttackSequencePresenter`
  - monster card와 scroll card 관리.
- `GamePrepareInputBlocker`
  - Toolkit blocking element 및 panel raycaster 판정 관리.
- `GamePrepareLegacyBridge`
  - legacy UI routing call 관리.

### 규칙

- 분리 중 command route를 바꾸지 않는다.
- 테스트가 사용하는 public helper는 테스트 갱신 전까지 유지한다.
- 외부 caller가 의존하는 static legacy bridge method는 당분간 유지한다.

### 후보 파일

기존 파일:

- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`

새 파일 후보:

- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUiReferences.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareHudPresenter.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareShopPresenter.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareAugmentPresenter.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareAttackSequencePresenter.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareInputBlocker.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareLegacyBridge.cs`

### 완료 기준

- main controller가 2,000줄 mixed-responsibility 파일이 아니라 orchestration 중심 파일이 된다.
- public behavior는 바뀌지 않는다.
- 가능한 테스트는 exact implementation text 대신 behavior를 검증한다.

## Phase 7: 테스트 현대화

### 목표

회귀 보호는 유지하면서 안전한 리팩터링이 가능하게 만든다.

### 작업

exact source-string check를 가능한 범위에서 아래 방식으로 대체한다.

- pure helper method test.
- cloned UXML element presence test.
- USS class test.
- 간단한 runtime object test.
- source check는 다른 방법으로 모델링하기 어려운 safety gate에만 유지.

계속 유지해야 할 테스트 영역:

- command route.
- Toolkit raycaster passthrough.
- ranking click behavior.
- wall remove 및 skill button fallback click path.
- fixed card count와 layout helper math.

### 후보 파일

- `Mdfproject/Assets/Scripts/Testing/MP/Editor/MPTestHarnessEditModeTests.cs`
- `Mdfproject/Assets/Scripts/Editor/WallRemovePanelInputEditModeTests.cs`
- `Mdfproject/Assets/Scripts/Editor/StatusBarSkillButtonInputEditModeTests.cs`

### 완료 기준

- 내부 helper class 이름 변경만으로 테스트가 깨지지 않는다.
- safety-critical behavior는 계속 테스트된다.

## 검증 계획

### 문서만 변경한 경우

이 문서만 수정한 경우:

- Unity compile 불필요.
- E2E 불필요.

### 코드만 바꾸는 UI 리팩터링

최소 검증:

```bash
python tools/harness/precommit.py --all
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
unity-cli --project Mdfproject test --mode EditMode
```

실제 live UI object나 입력 경로를 바꾼 경우 PlayMode test도 실행한다.

### 멀티플레이어에서 보이는 UI 동작 변경

최소 실행:

```bash
python tools/harness/mp/run_matrix.py --profile smoke
```

시각 screenshot이 assertion에 포함되지 않는 한 `--headless-player`를 사용한다.

### Prepare UI, HumanBot, 보드 입력 변경

가능하면 targeted HumanBot prepare 또는 two-humanbot/two-ai smoke case를 실행한다.

E2E PASS 증거:

- artifact path.
- `cleanupStatus=PASS`.
- `orphanedPids=[]`.
- `[MPTEST] phase=error` 없음.
- snapshot comparison success.

### Battle attack sequence UI 변경

몬스터 카드, 스크롤 카드, 공격 시퀀스 선택, battle input suppression을 바꾸면 battle profile 또는
targeted battle command case를 실행한다.

```bash
python tools/harness/mp/run_matrix.py --profile battle
```

## 실행 순서

권장 실제 작업 순서:

1. Phase 0 측정.
2. Phase 1 prepare HUD dirty update.
3. Phase 2 ranking refresh cache.
4. Phase 5 input same-frame cache.
5. Phase 3 shared sprite cache.
6. Phase 4 UI registry.
7. Phase 6 controller split.
8. Phase 7 test modernization.

모든 phase를 한 번에 합치지 않는다. 첫 번째 유효 patch는 빠르게 검증하고 안전하게 되돌릴 수 있을
정도로 작아야 한다.

## Acceptance Checklist

각 구현 PR 또는 patch에서 아래 항목을 확인한다.

- vendor 파일 변경 없음.
- UI command는 기존 command path를 사용한다.
- UI 결과 갱신은 authority-approved event 또는 networked state 기준이다.
- Toolkit panel passthrough가 빈 공간의 board click을 허용한다.
- wall remove button과 manual skill button fallback click이 유지된다.
- Host Migration 및 local player relink 후 UI reference가 갱신된다.
- 안정된 panel에서 같은 icon key의 Addressables load가 반복되지 않는다.
- stale async icon result가 rebound된 slot을 덮어쓰지 않는다.
- EditMode tests pass.
- Unity console에 user stacktrace error가 없다.
- runtime multiplayer UI behavior 변경 시 E2E artifact가 제공된다.

## 구현 전에 결정할 질문

구현 전 아래 결정을 내려야 한다.

- `UISpriteCache`는 scene-lifetime으로 둘 것인가, application-lifetime으로 둘 것인가?
- typed controller reference는 `UIManagers`가 가질 것인가, 새 `GameUiRegistry`가 가질 것인가?
- 현재 source-string test 중 어떤 것은 safety gate로 남겨야 하는가?
- per-frame reference refresh 제거 후 낮은 빈도의 migration fallback rebind를 허용할 것인가?
- UI performance counter는 MP snapshot, `[MPTEST]` log, editor-only diagnostic 중 어디에 노출할 것인가?

## 요약

가장 안전하고 효과가 큰 작업은 사용자에게 보이는 UI를 바꾸지 않고, per-frame UI update와 반복
lookup/load를 줄이는 것이다. 구조적으로는 `GamePrepareUIToolkitController`를 작게 나누는 것이
핵심이지만, 그 전에 측정값과 테스트 보호막을 먼저 확보해야 한다.

