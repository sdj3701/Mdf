# MDFLoginPlan (v1)

작성일: 2026-04-07  
상위 문서: `MDFNetworkPlan.md`  
적용 범위: Title 진입/로그인/초기 부트스트랩/네트워크 로비 진입

---

## 0) 문서 목적

이 문서는 `MDFNetworkPlan`을 기반으로 로그인 영역만 별도로 분리한 실행 문서다.

1. 현재 "문자열 입력 즉시 로그인" 구조를 명확히 정의한다.
2. 추후 Firebase Auth 기반 로그인으로 확장 가능한 구조를 먼저 만든다.
3. `Title` 씬의 `NetworkManager`, `AddressablesManager`, `LoadManager`를 중심으로 부트스트랩 구조를 재정의한다.
4. 로그인/부트스트랩/네트워크 공용 상수를 `Define`로 먼저 정리해 후속 리팩토링에 재사용한다.

---

## 1) 현재 구조 (As-Is)

## 1-1. 로그인 동작

현재 로그인은 인증이 아니라 닉네임 저장/사용 흐름이다.

1. `NextScenes.OnClick()`
2. `NetworkManager.NickNameInput.text` 읽기
3. 비어 있으면 `Player{난수}`로 보정
4. `PlayerPrefs["PlayerNickname"]` 저장
5. `NetworkManager.JoinLobby()`
6. `NetworkManager.LoadSceneSmart("MatchingLobby")`

근거 코드:
- `Assets/Scripts/Network/NextScenes.cs`
- `Assets/Scripts/Network/NetworkManager.cs`
- `Assets/Scripts/Network/NetworkPlayer.cs`

## 1-2. Title 씬 매니저 배치 상태

`Assets/Scenes/Title.unity` 기준:

1. `NetworkManager`: 활성
2. `AddressablesManager`: 활성 (`autoPreloadAllOnStart = true`)
3. `LoadManager`: 비활성 (`m_IsActive: 0`)

## 1-3. 현재 리스크

1. `NetworkManager`가 UI 입력 필드(`NickNameInput`, `PassWordInput`)를 직접 보유
2. `PassWordInput`은 실제 인증 흐름에서 사용되지 않음
3. `NextScenes`에서 `_networkManager` null 체크 전에 `_networkManager.NickNameInput` 접근 (NRE 가능)
4. 로그인 상태 머신 부재(중복 클릭, 진행중 재요청, 실패 상태 관리 없음)
5. `LoadManager` 오브젝트가 Title에 있지만 비활성이라 `Instance`가 늦게/다르게 생성될 수 있음
6. 버튼 이벤트 중 `NextScenes` 타겟이 비어있는 항목(`m_Target: {fileID: 0}`)이 존재해 점검 필요

---

## 2) 목표 구조 (To-Be)

핵심 원칙:
- 지금은 Local String 로그인 유지
- 코드 구조는 Firebase로 즉시 교체 가능하게 설계

## 2-1. 로그인 정책

1. `IAuthService` 추상화 도입
2. 현재 구현: `LocalStringAuthService`
3. 추후 구현: `FirebaseAuthService`
4. UI/UseCase는 `IAuthService`만 의존

## 2-2. Title 부트스트랩 정책

`Title` 씬은 "화면 + 부트스트랩 오케스트레이션"만 담당하고, 매니저 내부 결합을 끊는다.

권장 순서:
1. `AppBootstrapper` 시작
2. `AddressablesManager.PreloadAllAsync` (옵션)
3. `LoadManager.InitializeAsync`
4. `AuthService.InitializeAsync`
5. 로그인 UI 인터랙션 활성화
6. 로그인 성공 시 `NetworkManager.JoinLobby` + `LoadSceneSmart(MatchingLobby)`

---

## 3) 클래스별 리팩토링 설계

## 3-1. 기존 클래스

| 클래스 | 현재 역할 | 리팩토링 방향 |
|---|---|---|
| `NetworkManager` | 네트워크/세션/복구 + 로그인 입력필드 참조 | 로그인 UI 의존 제거, 네트워크 세션 오케스트레이터로 축소 |
| `AddressablesManager` | 전역 Addressables preload/load | 부트 단계 서비스로 명시화, preload 정책 Define화 |
| `LoadManager` | UnitData 로드/캐시 | Title에서 항상 활성/단일 인스턴스 보장, 초기화 실패 전달 표준화 |
| `NextScenes` | 로그인 버튼에서 닉네임 처리 + 씬 이동 | `LoginButtonController` + `LoginUseCase`로 분리 |
| `NetworkPlayer` | PlayerPrefs 닉네임 읽어 네트워크 반영 | 인증/프로필 source를 `IPlayerProfileStore`로 교체 |

## 3-2. 신규 권장 클래스

1. `AppBootstrapper`
2. `LoginPresenter`
3. `LoginUseCase`
4. `IAuthService`
5. `LocalStringAuthService`
6. `FirebaseAuthService` (추후)
7. `IPlayerProfileStore` (`PlayerPrefsProfileStore` 구현)
8. `AuthErrorMapper`

## 3-3. 책임 분리 기준 (SOLID)

1. `LoginPresenter`: View 이벤트 -> UseCase 호출, 결과 표시
2. `LoginUseCase`: 입력 검증 + 인증 호출 + 프로필 저장 + 다음 단계 트리거
3. `IAuthService`: 인증 성공/실패 결과만 반환
4. `NetworkManager`: 인증 이후 네트워크 접속만 담당

---

## 4) Firebase 확장 계획

## 4-1. 단계 전략

### Phase A (지금)

1. `LocalStringAuthService`로 현재 동작 100% 유지
2. 반환 타입만 Firebase 대응형으로 설계

```csharp
public readonly struct AuthResult
{
    public bool Success { get; }
    public string UserId { get; }      // Local: generated/local id, Firebase: uid
    public string DisplayName { get; } // 닉네임
    public string ErrorCode { get; }
    public string ErrorMessage { get; }
}
```

### Phase B (Firebase 연결)

1. Firebase SDK 초기화 (`FirebaseApp.CheckAndFixDependenciesAsync`)
2. `FirebaseAuthService` 구현
3. 로그인 방식 우선순위
4. 이메일/비밀번호
5. 익명 로그인 (옵션)
6. Custom token (백엔드 연동 시)

### Phase C (전환)

1. `AuthMode` 설정으로 Local/Firebase 선택
2. 기본값을 Local에서 Firebase로 전환
3. 실패 시 로컬 fallback 허용 여부는 정책으로 분리

## 4-2. Firebase 도입 시 유지 규칙

1. `IAuthService` 인터페이스 시그니처는 변경하지 않는다.
2. UI는 Firebase 타입을 직접 참조하지 않는다.
3. Firebase 에러 코드는 `AuthErrorMapper`에서 UI 문구로 변환한다.

---

## 5) Title 씬 매니저 리팩토링 상세

## 5-1. 문제 정리

1. 로그인/부트/네트워크 책임이 `NetworkManager`와 `NextScenes`에 혼재
2. `LoadManager`가 씬에 비활성으로 존재
3. 매니저 생명주기 규칙(누가 생성/파괴/보존하는지)이 코드로 명시되지 않음

## 5-2. 목표 구조

`Title` 씬에는 아래 2개만 남긴다.

1. `AppBootstrapper` (씬 오케스트레이션)
2. `LoginView` (입력/버튼/에러 표시)

매니저는 `AppRoot`(DontDestroyOnLoad)에서 보장:

1. `NetworkManager`
2. `AddressablesManager`
3. `LoadManager`

## 5-3. 권장 처리 규칙

1. `LoadManager`는 항상 활성 상태로 1개만 유지
2. `AddressablesManager` preload는 타임아웃/취소 토큰 적용
3. 로그인 버튼은 인증 진행 중 비활성
4. 인증 성공 전에 `JoinLobby`를 호출하지 않음

## 5-4. 매니저별 상세 리팩토링 (메서드 단위)

### A) NetworkManager

목표:
1. 로그인 UI 의존 제거
2. 네트워크 세션 API만 제공
3. `async void` 최소화

작업:
1. 필드 제거: `NickNameInput`, `PassWordInput`
2. `JoinLobby()`를 `UniTask<Result>`로 변경하여 호출측에서 실패 처리
3. `StartGame(...)` 호출 전 조건 검사를 별도 validator로 분리
4. 로그인 성공 후 진입용 API를 `ConnectLobbyAsync(LoginSession session)` 형태로 명시
5. 커맨드 RPC 중복 경로(`NetworkManager`쪽 RPC)는 사용 중단 표시 후 제거 대상 분리

### B) AddressablesManager

목표:
1. preload/게임프리팹 로드를 상태 기반으로 관리
2. 재진입/중복호출 안정화
3. 실패 원인 표준화

작업:
1. `PreloadAllAsync`에 취소 토큰/타임아웃 인자 추가
2. `LoadGamePrefabsAsync` 반환값을 `bool`에서 `LoadResult` 타입으로 확장
3. `EnsureGamePrefabsReadyAsync` 공개 메서드 추가 (중복 호출 안전)
4. preload 실패 시 fallback 정책(`retry`, `skip`)을 설정값으로 분리
5. 앱 종료 또는 씬 전환 시 release 전략 문서화 (`Addressables.Release` 기준)

### C) LoadManager

목표:
1. 인스턴스 수명주기 명확화
2. 초기화 완료 신호 표준화
3. 게임 진입 전 준비 보장

작업:
1. Title 씬의 비활성 오브젝트 방식 제거, `AppBootstrapper`가 명시적으로 생성/초기화
2. `InitializeAsync`에 재시도 정책 추가(데이터 소스 실패 대비)
3. `WaitUntilReady` timeout 버전 제공 (`WaitUntilReadyAsync(timeoutMs)`)
4. `GetUnitData` 미존재 키 접근 로깅 규칙 통일
5. UnitData source(Inspector/Addressables) 우선순위를 `LoadDefine`로 고정

## 5-5. AppBootstrapper 의사코드

```csharp
public async UniTask BootAsync(CancellationToken ct)
{
    await _addressables.PreloadAllAsync(ct);
    await _loadManager.InitializeAsync(ct);
    await _auth.InitializeAsync(ct);
    _loginView.SetInteractable(true);
}
```

---

## 6) Define 설계 (우선 작업)

## 6-1. 파일 구조

`Assets/Scripts/Defines/`

1. `SceneDefine.cs`
2. `PlayerPrefsDefine.cs`
3. `AuthDefine.cs`
4. `BootstrapDefine.cs`
5. `NetworkDefine.cs` (MDFNetworkPlan과 동일 기준)
6. `UiTextDefine.cs`

## 6-2. Define 초안

```csharp
public static class SceneDefine
{
    public const string Title = "Title";
    public const string MatchingLobby = "MatchingLobby";
    public const string JoinLobby = "JoinLobby";
    public const string Game = "Game";
    public const string MainLobby = "MainLobby";
}

public static class PlayerPrefsDefine
{
    public const string NicknameKey = "PlayerNickname";
    public const string PlayerUuidKey = "PlayerUUID";
    public const string LastAuthProviderKey = "LastAuthProvider";
    public const string LastLoginUserIdKey = "LastLoginUserId";
}

public static class AuthDefine
{
    public const int MinNicknameLength = 2;
    public const int MaxNicknameLength = 16;
    public const string DefaultNicknamePrefix = "Player";
    public const int RandomSuffixMin = 1000;
    public const int RandomSuffixMax = 9999;

    public const int LoginDebounceMs = 500;
    public const int LoginTimeoutMs = 15000;
}

public static class BootstrapDefine
{
    public const int AddressablesPreloadTimeoutMs = 20000;
    public const int LoadManagerInitTimeoutMs = 10000;
}
```

## 6-3. 적용 우선순위

1. 씬 이름 literal 제거
2. PlayerPrefs 키 literal 제거
3. 닉네임 기본값/길이 규칙 통합
4. preload/timeout 숫자 literal 제거

---

## 7) 구현 단계 (실행 플랜)

## Step 1: 무중단 구조화

1. `Define` 파일 추가
2. `NextScenes`의 literal 제거
3. `IAuthService + LocalStringAuthService + LoginUseCase` 추가
4. 기존 동작 동일성 유지

## Step 2: Title 부트스트랩 정리

1. `AppBootstrapper` 도입
2. `NetworkManager`에서 `NickNameInput/PassWordInput` 참조 제거
3. `LoadManager` 활성/생명주기 정리

## Step 3: Firebase 연결

1. `FirebaseAuthService` 구현
2. `AuthMode` 스위치 적용
3. 에러 매핑/UI 문구 정리

## Step 4: 정리/회귀

1. `NextScenes` 역할 축소 또는 제거
2. 로그인 버튼 이벤트 누락 타겟(Title 씬) 점검/수정
3. 플레이모드 회귀 테스트

---

## 8) 테스트 체크리스트

1. 닉네임 빈값 입력 시 기본 닉네임 생성
2. 로그인 연타 시 중복 실행 방지
3. 로그인 실패 시 UI/버튼 상태 정상 복귀
4. 로그인 성공 시 `JoinLobby -> MatchingLobby` 순서 보장
5. Title 재진입 시 매니저 중복 인스턴스 미발생
6. `LoadManager`/`AddressablesManager` 초기화 완료 전 게임 진입 차단
7. Firebase 모드 전환 시 동일 UI로 정상 동작

---

## 9) 완료 정의 (DoD)

1. 로그인 계층이 `NetworkManager`와 분리됨
2. `IAuthService` 기반으로 Local/Firebase 교체 가능
3. Title 부트스트랩 순서가 코드로 명확히 보장됨
4. `Define`가 도입되어 문자열/숫자 하드코딩이 제거됨
5. MDFNetworkPlan과 충돌 없이 동일 네트워크 흐름 기준을 유지함

---

## 10) 후속 문서 연결

1. `MDFRoomCreatePlan.md`: 방 생성 유스케이스 상세화
2. `MDFNetworkPlan.md`: 네트워크 세션/마이그레이션/클래스 분리 상위 기준
3. 본 문서(`MDFLoginPlan.md`): 인증/부트스트랩/타이틀 구조 기준
