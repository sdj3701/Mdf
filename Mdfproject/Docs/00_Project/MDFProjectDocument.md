# MDFProjectDocument

작성일: 2026-04-07  
최종 수정일: 2026-04-07 (Step 4 정리/회귀 반영)

## 0) 문서 목적

이 문서는 현재 프로젝트에서 `Define`과 `NetworkManager`를 빠르게 파악하기 위한 함수 중심 문서다.  
목표는 "함수 이름만 보고 역할을 바로 이해"하는 것이다.

---

## 1) 최근 반영 요약 (Step 1 ~ Step 4)

1. 로그인 계층 분리: `IAuthService`, `LoginUseCase`, `AuthServiceFactory` 도입
2. Firebase 연결 준비: `FirebaseAuthService` 추가 (현재 비활성 모드)
3. Title 부트스트랩: `AppBootstrapper` 도입
4. Step 4 정리: `NextScenes`는 입력 브리지 역할로 축소, 실제 로그인/이동은 `TitleLoginEntryFlow`로 분리
5. Step 4 회귀 수정: `Assets/Scenes/Title.unity`의 `NextScenes.OnClick` 누락 타겟(`m_Target: {fileID: 0}`) 연결 복구

---

## 2) Define 정리

## 2-1. Define 파일 목록

- `SceneDefine`
- `PlayerPrefsDefine`
- `NetworkDefine`
- `BootstrapDefine`
- `AuthDefine`
- `UiTextDefine`

## 2-2. Define 함수 목록

모든 Define 클래스는 함수가 없고 상수만 가진다.

- `SceneDefine`: 함수 없음
- `PlayerPrefsDefine`: 함수 없음
- `NetworkDefine`: 함수 없음
- `BootstrapDefine`: 함수 없음
- `AuthDefine`: 함수 없음
- `UiTextDefine`: 함수 없음

## 2-3. Define 상수 요약

| 클래스 | 핵심 상수 | 용도 |
|---|---|---|
| `SceneDefine` | `Title`, `MatchingLobby`, `JoinLobby`, `Game`, `MainLobby` | 씬 이름 하드코딩 제거 |
| `PlayerPrefsDefine` | `NicknameKey`, `PlayerUuidKey`, `LastAuthProviderKey`, `LastLoginUserIdKey` | PlayerPrefs 키 일원화 |
| `NetworkDefine` | `DefaultRoomName`, `DefaultNickname`, `DefaultHostName` | 네트워크 기본 문자열 일원화 |
| `BootstrapDefine` | `AddressablesPreloadTimeoutMs`, `LoadManagerInitTimeoutMs` | 부트스트랩 타임아웃 기준 |
| `AuthDefine` | `EnableFirebaseAuth`, `DefaultProviderMode`, `FallbackToLocalOnFirebaseUnavailable`, `Error*` | 인증 모드/오류 코드 정책 |
| `UiTextDefine` | `RoomJoin`, `RoomJoinSuffix`, `RoomFullSuffix` | 공용 UI 텍스트 기준 |

---

## 3) NetworkManager 함수 문서

기준 파일: `Assets/Scripts/Network/NetworkManager.cs`

## 3-1. 초기화/기본 상태

| 함수 시그니처 | 기능 |
|---|---|
| `private void Awake()` | 싱글톤 보장, `DontDestroyOnLoad`, migration 준비, cloud disconnect 핸들러 등록 |
| `private void OnDestroy()` | pending fallback 취소, cloud disconnect 핸들러 해제 |
| `public void SetRoomNameInput(string roomname)` | 생성할 방 이름 저장 |
| `public string GetRoomNameInput()` | 저장된 방 이름 반환 |
| `public void SetRunnerAfterMigration(NetworkRunner newRunner)` | host migration 후 새 runner 재등록 |
| `public int GetPlayerCount()` | 현재 세션 플레이어 수 반환 |

## 3-2. 로비/세션 제어

| 함수 시그니처 | 기능 |
|---|---|
| `public async void JoinLobby()` | Shared Lobby 입장 시도, 성공/실패 상태 전이 처리 |
| `public async void StartGame(GameMode mode, string sessionName, string sceneName = null)` | Host/Client 세션 시작, 씬/토큰 포함 StartGame 실행 |
| `private void LeaveGame()` | runner shutdown |
| `public void LoadSceneSmart(string sceneName)` | runner 활성 여부에 따라 Fusion 또는 Unity 씬 로드 |
| `public void LeaveAndLoad(string sceneName)` | 세션 종료 후 지정 씬 이동 |

## 3-3. RPC

| 함수 시그니처 | 기능 |
|---|---|
| `[Rpc] public void RPC_RequestCommandToServer(...)` | 클라이언트 커맨드 요청을 서버에서 승인/거부 판단 |
| `[Rpc] private void RPC_BroadcastCommandToClients(...)` | 승인된 커맨드 브로드캐스트 |

## 3-4. Fusion 콜백 (`INetworkRunnerCallbacks`)

| 함수 시그니처 | 기능 |
|---|---|
| `OnSessionListUpdated(...)` | 세션 목록 갱신 이벤트 전달 |
| `OnPlayerJoined(...)` | 입장 처리, 스폰/재연결 재매핑 |
| `OnPlayerLeft(...)` | 이탈 처리, migration 캐시/정리 |
| `OnShutdown(...)` | 종료 사유별 상태 리셋/runner 정리 |
| `OnConnectedToServer(...)` | pending fallback 취소 |
| `OnDisconnectedFromServer(...)` | 연결 손실 정책 적용 |
| `OnHostMigration(...)` | migration 핸들러 위임 또는 fallback |
| `OnConnectFailed(...)` | 빈 콜백 |
| `OnConnectRequest(...)` | 빈 콜백 |
| `OnCustomAuthenticationResponse(...)` | 빈 콜백 |
| `OnInput(...)` | 빈 콜백 |
| `OnInputMissing(...)` | 빈 콜백 |
| `OnObjectEnterAOI(...)` | 빈 콜백 |
| `OnObjectExitAOI(...)` | 빈 콜백 |
| `OnReliableDataProgress(...)` | 빈 콜백 |
| `OnReliableDataReceived(...)` | 빈 콜백 |
| `OnSceneLoadDone(...)` | 빈 콜백 |
| `OnSceneLoadStart(...)` | 빈 콜백 |
| `OnUserSimulationMessage(...)` | 빈 콜백 |

## 3-5. Host Migration/재연결 보조

| 함수 시그니처 | 기능 |
|---|---|
| `private string TryGetConnectionTokenString(...)` | player connection token 조회/문자열 변환 |
| `private void CacheDisconnectedPlayerData(...)` | 이탈 플레이어 상태 캐시 |
| `private bool TryReassociateDisconnectedPlayer(...)` | 재접속 플레이어와 기존 오브젝트 재연결 |
| `private static bool IsActivePlayer(...)` | player active 여부 확인 |

## 3-6. 연결 손실 정책

| 함수 시그니처 | 기능 |
|---|---|
| `private void RegisterCloudConnectionLostHandlerIfAvailable()` | cloud disconnect 이벤트 등록 |
| `private void UnregisterCloudConnectionLostHandlerIfAvailable()` | cloud disconnect 이벤트 해제 |
| `private void OnCloudConnectionLostCompat(...)` | cloud disconnect 콜백 라우팅 |
| `private void ApplyConnectionLossPolicy(...)` | 정책 모드별 fallback 분기 |
| `private void ScheduleConnectionLossFallback(...)` | 지연 fallback 예약 |
| `private IEnumerator ConnectionLossFallbackCoroutine(...)` | 지연 후 연결 상태 확인 및 fallback 실행 |
| `private void CancelPendingConnectionLossFallback()` | 예약 fallback 취소 |
| `private void ExecuteConnectionLossFallback(...)` | 연결 해제 처리 및 `MatchingLobby` 복귀 |
| `private string BuildConnectionLossTrace(...)` | 정책 추적 로그 생성 |

## 3-7. 기타

| 함수 시그니처 | 기능 |
|---|---|
| `private byte[] GetConnectionToken()` | UUID 기반 connection token 생성/조회 |
| `private void OnGUI()` | 디버그 GUI 출력 |

---

## 4) Step 2~4 연관 함수 (로그인/부트스트랩)

## 4-1. 인증

| 클래스 | 함수 | 기능 |
|---|---|---|
| `AuthServiceFactory` | `CreateFromDefine()` | 기본 모드 기반 인증 서비스 생성 |
| `AuthServiceFactory` | `Create(AuthProviderMode mode)` | Local/Firebase 분기 및 fallback |
| `LocalStringAuthService` | `SignInWithDisplayName(string)` | 닉네임 보정 + PlayerPrefs 저장 |
| `FirebaseAuthService` | `SignInWithDisplayName(string)` | 현재 비활성/미구현 상태 반환 |
| `LoginUseCase` | `Execute(string)` | 인증 실행 진입점 |
| `AuthErrorMapper` | `ToUserMessage(AuthResult)` | 에러코드 -> 사용자 메시지 변환 |

## 4-2. Title 진입

| 클래스 | 함수 | 기능 |
|---|---|---|
| `AppBootstrapper` | `EnsureExistsInScene()` | 부트스트래퍼 존재 보장 |
| `AppBootstrapper` | `BootIfNeeded()` | Network/Addressables/Load 초기화 |
| `NextScenes` | `OnClick()` | 입력 수집 후 로그인 플로우 호출 |
| `TitleLoginEntryFlow` | `TryLoginAndMoveToMatchingLobby(string)` | 로그인 성공 시 `JoinLobby -> MatchingLobby` 실행 |

---

## 5) 유지보수 규칙

1. `NetworkManager` 함수 추가/삭제 시 3장을 먼저 갱신한다.
2. Define 상수 추가 시 2장 표를 즉시 갱신한다.
3. 로그인 흐름 변경 시 4장을 함께 갱신한다.
4. 씬 이벤트 연결 변경 시 `Title.unity`의 `m_OnClick` 타겟 null 여부를 함께 점검한다.
