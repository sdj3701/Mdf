# UI Cross-Platform Plan

## 범위

PC 중심으로 개발된 MDF 초기 UI를 모바일에서도 사용할 수 있게 전환하기 위한 계획이다.

작업 범위는 사용자가 직접 작업할 공간인 다음 3개 씬으로 제한한다.

- `00_Title`
- `01_MatchingLobby`
- `02_JoinLobby`

`03_Game` 인게임 HUD, 전투 UI, 배치/상점/전투 조작 UI는 이 문서의 범위에서 제외한다.

## 목표

- PC와 모바일을 실행 시점에 안정적으로 구분한다.
- 모바일에서는 해상도 설정을 직접 바꾸지 못하게 하고, PC에서만 해상도 설정을 허용한다.
- 모바일에서는 safe area, 터치 hitbox, 소프트 키보드, 터치 스크롤을 고려한다.
- 기존 UI Toolkit 구조를 최대한 유지하면서 USS class 분기로 모바일 레이아웃을 적용한다.
- 세 씬의 UI 동작을 바꿀 때 네트워크 권한/게임플레이 로직은 건드리지 않는다.

## 현재 기준

### `00_Title`

- `ReLogin UI Toolkit` 흐름을 주 UI로 본다.
- 로그인 계정/비밀번호 입력창, 로그인/게스트 로그인/서버/설정/언어 버튼이 핵심이다.
- 모바일에서는 입력창 focus와 소프트 키보드가 가장 큰 위험이다.

### `01_MatchingLobby`

- `TestMatching UI Toolkit` 흐름을 주 UI로 본다.
- 방 목록 `ScrollView`, 방 카드 탭, 방 만들기, 직접 입장, 새로고침, 타이틀 복귀가 핵심이다.
- 모바일에서는 방 목록 스크롤과 방 카드 탭이 충돌하지 않아야 한다.

### `02_JoinLobby`

- `JoinLobby UI Toolkit` 흐름을 주 UI로 본다.
- 기존 UGUI 대기방 UI가 남아 있다면 최종적으로 비활성 상태를 유지하거나 제거 검토한다.
- 4개 플레이어 슬롯, 준비 버튼, 게임 시작 버튼, 방 나가기 버튼이 핵심이다.
- 모바일에서는 슬롯 텍스트 가독성과 버튼 hitbox가 가장 중요하다.

## 추천 구조

씬마다 별도 분기 코드를 넣지 말고 공통 정책을 만든 뒤, 각 UI Toolkit 컨트롤러가 공통 정책을 호출하는 방식이 좋다.

```text
MdfRuntimePlatformProfile
  - Desktop / Mobile / Tablet 판별
  - Editor ForceDesktop / ForceMobile / ForceTablet preview 지원

MdfCrossPlatformUIRoot
  - UIDocument root와 실제 UXML root에 platform class 적용
  - safe area를 panel 좌표로 변환
  - design-space를 safe area 안에 맞춰 스케일
  - 모바일 키보드 여유 공간 반영

MdfCrossPlatformPointer
  - 마우스 좌클릭, 터치, 펜을 activation pointer로 통합
  - 방 카드 tap과 scroll drag를 구분하는 threshold 제공

MdfResolutionSettingsPolicy
  - PC에서만 해상도 변경 허용
  - 모바일/태블릿은 기기 기본 해상도 사용
```

## 왜 이 방식을 추천하는가

### 1. 화면 크기만으로 모바일을 판단하면 오판이 많다

PC 창모드, 고해상도 태블릿, 폴더블 기기, Steam Deck 같은 환경은 해상도만으로 PC/모바일을 구분하기 어렵다.

따라서 1차 기준은 다음 값이어야 한다.

- `Application.isMobilePlatform`
- `Application.platform == RuntimePlatform.Android`
- `Application.platform == RuntimePlatform.IPhonePlayer`
- 보조 기준: `SystemInfo.deviceType == DeviceType.Handheld`

화면 크기와 종횡비는 PC/모바일 판별 기준이 아니라, 이미 판별된 플랫폼 안에서 phone/tablet/wide/narrow 레이아웃을 고르는 보조 기준으로만 쓴다.

### 2. 모바일 해상도는 사용자가 직접 고르는 대상이 아니다

PC에서는 창모드/전체화면/모니터 해상도 선택이 자연스럽다.

반면 모바일은 OS와 기기가 실제 디스플레이 해상도, 노치, 홈 인디케이터, 회전, 렌더 버퍼를 관리한다. 모바일에서 PC식 해상도 드롭다운을 열어두면 safe area와 터치 좌표 체감이 꼬일 수 있다.

모바일 성능 옵션이 필요하면 해상도 변경 대신 다음 옵션으로 제공하는 것이 안전하다.

- 품질 프리셋: 낮음 / 보통 / 높음
- FPS 제한: 30 / 60
- 배터리 절약 모드
- 내부 렌더 스케일

### 3. 현재 UI Toolkit 구조와 잘 맞는다

세 씬의 신규 UI는 이미 UI Toolkit 기반이다. 각 컨트롤러가 `1672 x 941` 계열 design-space를 화면에 맞춰 스케일하는 구조라, 공통 safe area와 모바일 class만 붙여도 단계적으로 대응할 수 있다.

## PC/모바일 해상도 정책

### PC

- `Screen.resolutions` 기반 해상도 목록 제공
- 전체화면/창모드 선택 제공
- `Screen.SetResolution(width, height, fullscreenMode)` 허용
- 마지막 선택값은 `PlayerPrefs` 저장
- 최소 지원 해상도는 `1280 x 720` 이상 권장

### 모바일

- 해상도 드롭다운 숨김 또는 비활성화
- 전체화면/창모드 옵션 숨김
- 기기 기본 해상도와 OS 전체화면 정책 사용
- 설정 문구는 짧게 표시

권장 문구:

```text
모바일에서는 기기 기본 해상도를 사용합니다.
품질/FPS 옵션으로 성능을 조정할 수 있습니다.
```

## 공통 UI 크기 기준

현재 px 기준은 `1672 x 941` design-space의 논리 크기다.

| 항목 | PC | 모바일 |
| --- | ---: | ---: |
| 최소 버튼 높이 | 44 px 이상 | 64 px 이상 |
| 주요 버튼 높이 | 56-72 px | 80-96 px |
| 작은 아이콘 hitbox | 40 x 40 px | 56 x 56 px 이상 |
| 입력창 높이 | 44-56 px | 64-80 px |
| 버튼 간 간격 | 8-12 px | 12-20 px |
| 상태 메시지 폰트 | 15-18 px | 18-22 px |
| 본문/목록 폰트 | 18-24 px | 20-26 px |

## 터치 입력 정책

- UI Toolkit pointer event 흐름을 유지한다.
- 버튼 실행은 `PointerUpEvent` 중심으로 처리한다.
- 입력창 focus는 명확한 hitbox의 `PointerDownEvent`로 처리한다.
- 터치, 펜, 마우스 좌클릭을 activation pointer로 인정한다.
- 방 목록은 스크롤이 우선이다.
- 방 카드 입장은 `PointerDown` 위치와 `PointerUp` 위치 차이가 threshold 이내일 때만 실행한다.
- 더블 탭, 롱프레스 같은 모바일 전용 제스처는 이 3개 씬에서는 넣지 않는다.

## 씬별 계획

## `00_Title`

### 목표

모바일 첫 진입 화면에서 safe area, 키보드, 계정 입력 hitbox를 안정화한다.

### 구현 항목

- 공통 platform profile 적용
- `relogin-root`에 `mdf-platform-mobile-like` class 적용
- 계정/비밀번호 입력 hitbox 확대
- 입력창 focus 시 모바일 키보드 여유 공간 반영
- 로그인/게스트/서버/설정/언어 버튼 hitbox 확대
- 모바일 설정에서는 해상도 설정을 숨기거나 안내 문구만 표시

### 확인할 위험

- 모바일 키보드가 입력창 또는 로그인 버튼을 가릴 수 있다.
- UI Toolkit TextField focus/blur 타이밍이 PC와 다를 수 있다.
- 기존 UGUI와 UI Toolkit이 동시에 raycast/picking을 받을 수 있다.

## `01_MatchingLobby`

### 목표

방 목록을 터치로 스크롤하고, 짧은 탭일 때만 방 입장을 실행한다.

### 구현 항목

- 공통 platform profile 적용
- 좌측 메뉴와 장식성 HUD는 모바일에서 숨김 또는 축소
- 방 목록 영역 확대
- 방 카드 전체를 터치 대상으로 사용
- 방 카드 scroll drag와 tap 분리
- 방 이름 입력창 hitbox 확대
- 입력창 focus 시 키보드 여유 공간 반영
- 직접 입장 버튼과 입력창 간격 확보
- 네트워크 처리 overlay로 중복 방 생성/중복 입장 방지

### 확인할 위험

- `ScrollView`의 scroller가 숨겨져 있으면 스크롤 가능 여부가 덜 보일 수 있다.
- 방 카드 탭과 스크롤 드래그가 충돌할 수 있다.
- 방 이름 입력 직후 직접 입장 버튼 오탭이 발생할 수 있다.

## `02_JoinLobby`

### 목표

대기방에서 준비/시작/나가기 버튼을 모바일에서도 명확하게 누르고, 슬롯 상태를 읽을 수 있게 한다.

### 구현 항목

- 공통 platform profile 적용
- 좌측 메뉴와 상단 HUD는 모바일에서 숨김 또는 축소
- 슬롯 텍스트가 작으면 4열 대신 `2 x 2` 배치 적용
- 준비/시작/나가기 버튼 hitbox 확대
- 호스트만 시작 가능하다는 상태 메시지 유지
- 네트워크 처리 overlay로 중복 입력 차단
- 기존 UGUI 대기방 UI와 UI Toolkit 중 최종 사용 UI 확정

### 확인할 위험

- 기존 UGUI `JoinLobbyUI`와 새 `JoinLobby UI Toolkit`이 함께 있으면 중복 입력 또는 시각 중복이 발생할 수 있다.
- 호스트/클라이언트 권한 UI는 실제 네트워크 상태와 일치해야 한다.
- 2x2 슬롯 배치는 작은 화면에서 가독성은 좋아지지만 PC와 시각 흐름이 달라질 수 있다.

## 구현 단계

### 1단계: 공통 판별과 정책

- `MdfRuntimePlatformProfile` 추가
- `MdfCrossPlatformUIRoot` 추가
- `MdfCrossPlatformPointer` 추가
- `MdfResolutionSettingsPolicy` 추가
- Editor preview override 추가

### 2단계: `00_Title` 모바일 입력 안정화

- 로그인 입력창 hitbox 확대
- 모바일 키보드 여유 공간 반영
- 모바일 설정에서 해상도 설정 차단
- safe area 안으로 주요 버튼 보정

### 3단계: `01_MatchingLobby` 터치 스크롤과 방 입장

- 방 목록 scroll drag와 방 카드 tap 분리
- 방 만들기/직접 입장 중복 입력 차단
- 모바일 class로 방 목록과 입력 영역 재배치

### 4단계: `02_JoinLobby` 대기방 정리

- UGUI/UI Toolkit 최종 UI 확정
- 준비/시작/나가기 버튼 hitbox 확대
- 슬롯 텍스트와 준비 상태 모바일 가독성 확인

### 5단계: 검증

기본 검증:

```text
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
unity-cli --project Mdfproject test --mode EditMode
```

씬 또는 UXML/USS/asset을 수정했다면 필요한 파일을 reserialize한다.

```text
unity-cli --project Mdfproject reserialize --path Assets/Scenes/00_Title.unity
unity-cli --project Mdfproject reserialize --path Assets/Scenes/01_MatchingLobby.unity
unity-cli --project Mdfproject reserialize --path Assets/Scenes/02_JoinLobby.unity
```

## 모바일 테스트 방법

### Unity Editor 1차 확인

1. 각 씬을 연다.
2. UI Toolkit 컨트롤러의 `Platform Preview Mode`를 `ForceMobile`로 설정한다.
3. Game View를 가로 모바일 비율로 맞춘다.
   - `2340 x 1080`
   - `2400 x 1080`
   - `2532 x 1170`
4. 버튼 겹침, 텍스트 잘림, safe area 여백을 확인한다.
5. 테스트 후 `Platform Preview Mode`를 `Auto`로 돌린다.

### Unity Device Simulator

1차 safe area와 노치 대응 확인에 사용한다.

확인 항목:

- 주요 버튼이 화면 밖으로 나가지 않는지
- 노치/홈 인디케이터와 버튼이 겹치지 않는지
- `01_MatchingLobby` 방 목록과 직접 입장 버튼이 충분히 큰지
- `02_JoinLobby` 2x2 슬롯 텍스트가 읽히는지

### Android Emulator 또는 Nox

Nox, BlueStacks, LDPlayer, Android Studio Emulator에 APK를 설치해 테스트할 수 있다.

권장 흐름:

1. Unity Hub에서 Android Build Support 설치
   - Android SDK
   - Android NDK
   - OpenJDK
2. Unity Build Settings에서 Android로 Switch Platform
3. Player Settings 확인
   - Orientation: Landscape 고정 권장
   - Target Architecture: ARM64 권장
   - Development Build 체크 권장
4. APK 빌드
5. Nox 또는 Android Emulator에 APK 설치
6. 터치, 소프트 키보드, 방 목록 스크롤 확인

Nox는 실제 기기와 safe area/노치가 다를 수 있으므로 최종 검증은 실제 Android 기기에서도 한 번 더 하는 것이 좋다.

### 수동 확인 체크리스트

| 씬 | 확인 항목 | 권장 환경 |
| --- | --- | --- |
| `00_Title` | 계정/비밀번호 터치 시 모바일 키보드 표시 | Android Emulator 또는 실기 |
| `00_Title` | 키보드가 입력창/주요 버튼을 가리지 않는지 | Android Emulator 또는 실기 |
| `00_Title` | 모바일 설정에서 해상도 설정이 노출되지 않는지 | Editor ForceMobile + Emulator |
| `01_MatchingLobby` | 방 목록 터치 스크롤 중 카드 오탭이 없는지 | Emulator 또는 실기 |
| `01_MatchingLobby` | 방 만들기/직접 입장 중복 입력이 막히는지 | 네트워크 연결 환경 |
| `02_JoinLobby` | 2x2 슬롯 텍스트가 실제 기기에서 읽히는지 | 2340 x 1080 이상 우선 |
| `02_JoinLobby` | 준비/시작/나가기 버튼 터치 영역이 충분한지 | Emulator 또는 실기 |

## 결과 기록 양식

테스트 중 문제가 있으면 다음 형식으로 기록한다.

| 번호 | 기기/환경 | 씬 | 안 되는 내용 | 재현 순서 | 기대 동작 | 실제 동작 | 스크린샷/로그 | 우선순위 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 |  | `00_Title` |  |  |  |  |  |  |
| 2 |  | `01_MatchingLobby` |  |  |  |  |  |  |
| 3 |  | `02_JoinLobby` |  |  |  |  |  |  |

## 요약

- PC/모바일 구분은 해상도가 아니라 실행 플랫폼 기준으로 한다.
- 모바일에서는 해상도 설정을 막고 기기 기본 해상도를 사용한다.
- 모바일 성능 조정은 품질/FPS/렌더 스케일로 다룬다.
- 세 씬 모두 UI Toolkit root에 platform class를 붙이고 USS 분기로 모바일 레이아웃을 적용한다.
- `00_Title`은 키보드, `01_MatchingLobby`는 스크롤/방 카드 탭, `02_JoinLobby`는 슬롯 가독성과 버튼 hitbox가 핵심이다.
