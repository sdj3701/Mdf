# 씬별 클래스 간단 정리

작성 기준: 2026-05-13

발표나 코드 설명 전에 빠르게 보는 요약 문서다. 자세한 설명은 `docs/ai-design/scene-class-detailed.md`를 보면 된다.

## 전체 구조

```text
00_Title
  - 부트스트랩, 로그인, 언어 선택, 기본 데이터 로딩
  - NetworkManager / AddressablesManager / LoadManager 준비

01_MatchingLobby
  - Fusion 로비 접속
  - 방 목록 표시
  - 방 생성 또는 직접 입장

02_JoinLobby
  - 방 안 플레이어 표시
  - 준비 상태 동기화
  - 호스트가 게임 시작

03_Game
  - GameManagers 스폰
  - 플레이어/필드/상점/UI 준비
  - Prepare -> Battle1 -> Battle2 -> GameOver 진행
```

## `00_Title`

| 클래스 | 한 줄 설명 | 왜 필요한가 |
| --- | --- | --- |
| `AppBootstrapper` | 타이틀에서 필수 매니저와 데이터 로딩을 보장 | 다음 씬에서 매니저/데이터 누락을 막기 위해 |
| `NextScenes` | 기존 UGUI 로그인 버튼 처리 | 기존 버튼 구조를 유지하며 매칭 로비 이동을 처리 |
| `TitleLoginEntryFlow` | 부트 준비 확인, 로그인, 로비 이동을 묶은 flow | 버튼 클래스에 로그인/이동 로직이 너무 몰리지 않게 하기 위해 |
| `ReLoginUIToolkitController` | 새 로그인 UI 컨트롤러 | 새 타이틀 UI와 모바일 입력 대응을 위해 |
| `LanguageSelector` | 언어 선택과 PlayerPrefs 저장 | Localization 선택을 유지하기 위해 |
| `NetworkManager` | Fusion runner와 로비/세션 관리 | 씬 이동 후에도 네트워크 상태를 유지하기 위해 |
| `LoadManager` | UnitData 로딩/캐시 | 상점/배치/복구에서 UnitData를 안정적으로 찾기 위해 |
| `AddressablesManager` | Addressables preload와 prefab/UI 로딩 | 동적 자산 로딩을 통일하기 위해 |
| `BuildDebugGUI` | 빌드 클라이언트 로그 UI | Editor 밖 멀티플레이 디버깅을 위해 |

추천 개선:

- `NextScenes`와 `ReLoginUIToolkitController` 중 최종 로그인 경로를 하나로 정리한다.
- 로그인/언어 UI 문자열을 Localization StringTable로 옮긴다.
- 부트 실패를 사용자에게 보여주는 상태 UI를 추가한다.

## `01_MatchingLobby`

| 클래스 | 한 줄 설명 | 왜 필요한가 |
| --- | --- | --- |
| `TestMatchingUIToolkitController` | 방 목록, 방 만들기, 직접 입장 UI | Fusion session list를 사용자가 선택할 수 있게 하기 위해 |
| `NetworkManager` | 로비 연결과 `StartGame` 실행 | 방 생성/입장을 실제 Fusion 세션으로 연결하기 위해 |

추천 개선:

- 방 카드 tap과 scroll drag를 분리해 모바일 오탭을 막는다.
- `NetworkManager._sessionList` 직접 접근 대신 read-only snapshot을 제공한다.
- 방 생성/입장 실패를 UI에 알려주는 결과 이벤트를 추가한다.

## `02_JoinLobby`

| 클래스 | 한 줄 설명 | 왜 필요한가 |
| --- | --- | --- |
| `JoinLobbyUI` | 대기방 슬롯, 준비, 시작, 나가기 UI | 플레이어 준비 상태를 보고 게임 시작을 제어하기 위해 |
| `NetworkPlayer` | 닉네임과 준비 상태를 `[Networked]`로 보관 | 모든 피어가 같은 대기방 상태를 보게 하기 위해 |
| `NetworkManager` | runner와 씬 이동 관리 | 방을 나가거나 게임 씬으로 이동하기 위해 |

추천 개선:

- 기존 UGUI 오브젝트와 새 UI Toolkit 오브젝트에 같은 `JoinLobbyUI`가 붙어 있는 문제를 먼저 정리한다.
- `JoinLobbyUI`를 `JoinLobbyUIToolkitController`처럼 이름 분리한다.
- 게임 시작은 `NetworkManager`의 중앙 scene load 경로로 통일한다.

## `03_Game`

### 씬 직접 클래스

| 클래스 | 한 줄 설명 | 왜 필요한가 |
| --- | --- | --- |
| `GameSceneInitializer` | Single/Multi 모드를 판단하고 `GameManagers`를 스폰 | 게임 씬 직접 실행과 로비 진입을 모두 지원하기 위해 |
| `CameraManager` | 플레이어 필드 간 카메라 이동 | 전투/관전 중 다른 필드를 보기 위해 |
| `GetPlayerCamera` | 로컬 플레이어 기준 카메라 초기 배치 | 간단한 카메라 보정용 |
| `ComponentAutoRegister` | 카메라 등 기본 컴포넌트 registry 등록 | 런타임 검색과 참조 결합을 줄이기 위해 |
| `AddressablesManager` | 수명 추적 캐시로 필요한 에셋을 로드 | 중복 로드와 해제 누락 없이 공통 자산을 준비하기 위해 |
| `ProjectileVfxManager` | 전투 투사체 VFX 생성/이동 | 전투 이벤트를 시각 효과로 보여주기 위해 |
| `VfxPoolManager` | VFX pooling | 전투 중 반복 생성 비용을 줄이기 위해 |

### 런타임 핵심 클래스

| 클래스 | 한 줄 설명 | 왜 필요한가 |
| --- | --- | --- |
| `GameManagers` | 게임 상태, 라운드, 타이머, 전투 흐름 관리 | State Authority가 match flow를 결정해야 하기 때문에 |
| `PlayerManager` | 각 플레이어의 HP/gold/wall/shop/augment/attack pool 관리 | 플레이어별 durable state를 네트워크로 동기화하기 위해 |
| `FieldManager` | 그리드, 벽, 유닛, pathfinding 관리 | 배치/전투 경로/복구가 모두 필드 상태에 의존하기 때문에 |
| `ShopManager` | 상점 슬롯, 리롤, sold state 관리 | 상점 결과를 authority 기준으로 유지하기 위해 |
| `UIManagers` | UGUI 패널 pool과 Addressables UI 로딩 | 필요한 UI를 동적으로 열고 재사용하기 위해 |
| `GameUIToolkitHudController` | 새 HUD, safe area, shop/wall 버튼 | 모바일 대응 HUD를 런타임 보장하기 위해 |
| `PlayerHUDController` | 기존 HUD | 기존 UGUI HUD 흐름 유지 |
| `PhaseTimerUI` | 기존 phase timer | 현재 phase 남은 시간을 표시 |
| `RankingUIController` | HP 기준 플레이어 랭킹 표시 | 전투 상황에서 순위를 보여주기 위해 |
| `ShopUIController` | 상점 UI와 리롤 command 요청 | UI가 직접 상태를 바꾸지 않고 command를 보내기 위해 |
| `AugmentUIController` | 증강 선택 UI와 command 요청 | 증강 선택도 UI/AI가 같은 command path를 타게 하기 위해 |
| `AttackSequenceUIController` | 공격자 몬스터/마법 스크롤 선택 UI | 전투 중 공격자가 사용할 자원을 선택하기 위해 |

추천 개선:

- `GameManagers`, `PlayerManager`, `FieldManager`는 크기가 크므로 기능별 service/partial 분리를 계속 유지한다.
- UI는 직접 gameplay state를 변경하지 않고 command path를 유지한다.
- `CameraManager`와 `GetPlayerCamera`의 중복 역할을 정리한다.
- 기존 UGUI HUD와 새 UI Toolkit HUD 중 최종 방향을 정한다.
- Host Migration/reconnect 관련 reference 재바인딩 로직은 계속 유지하고 테스트로 보호한다.

## 설명할 때 핵심 문장

- 타이틀은 단순 첫 화면이 아니라 전체 매니저와 데이터 준비 지점이다.
- 매칭 로비는 Fusion session list를 UI로 보여주는 씬이다.
- 참가 로비의 ready 상태는 UI 값이 아니라 `NetworkPlayer`의 `[Networked]` 상태다.
- 게임 씬의 핵심 상태는 `GameManagers`와 `PlayerManager`가 authority 기준으로 관리한다.
- 상점, 증강, 전투 선택 UI는 직접 결과를 바꾸지 않고 command를 요청한다.
- Host Migration 때문에 여러 클래스가 런타임 참조를 다시 찾는 방어 코드를 갖고 있다.
