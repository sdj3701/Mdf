# HostMigration 오류 점검 문서

작성일: 2026-05-08  
대상 프로젝트: `D:\Unity\Mdf\Mdf\Mdfproject`  
주요 코드:
- `Assets/Scripts/Network/NetworkManager.cs`
- `Assets/Scripts/Network/HostMigrationHandler.cs`
- `Assets/Scripts/Managers/GameManagers.cs`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs`
- `Assets/Scripts/Managers/GameManagers.PlayerRegistry.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`
- `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`

---

## 1. 결론

현재 코드 기준으로 HostMigration은 **컴파일 관점에서는 정상**이다. `dotnet build .\Assembly-CSharp.csproj --no-restore` 결과는 `오류 0개`, `경고 19개`였다. 경고 대부분은 기존 미사용 변수, Unity Analyzer 경고, 외부 Fusion Editor 경고이며 HostMigration 코드를 즉시 막는 컴파일 오류는 아니었다.

또한 Fusion 설정 파일 `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`에는 아래 설정이 존재한다.

```json
"HostMigration": {
  "EnableAutoUpdate": true,
  "UpdateDelay": 2
}
```

따라서 HostMigration snapshot 자동 업데이트는 켜져 있다.

다만 실제 멀티플레이 런타임에서 Host를 끊고 새 Host가 이어받는 시나리오는 이 환경에서 끝까지 실행하지 못했다. Unity 배치모드 검증은 기존 Unity Editor가 같은 프로젝트를 열고 있어서 `It looks like another Unity instance is running with this project open.` 로그와 함께 중단되었다. 그래서 최종 판단은 다음과 같다.

**정적 분석과 C# 빌드 기준으로는 HostMigration 구조가 동작 가능한 상태다.**  
**하지만 실제 세션에서 완전히 정상이라고 단정하려면, Play Mode 또는 빌드 2개 이상으로 Host 종료 테스트를 해야 한다.**

---

## 2. 현재 HostMigration 흐름

### 2-1. 콜백 진입점

`NetworkManager.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)`에서 HostMigration이 시작된다.

위치:
- `Assets/Scripts/Network/NetworkManager.cs:646`

동작:
1. Fusion이 HostMigration 콜백을 호출한다.
2. `HostMigrationHandler.Instance.StartMigration(runner, hostMigrationToken)`으로 위임한다.
3. `HostMigrationHandler`가 없으면 `MatchingLobby`로 되돌아가는 fallback을 실행한다.

이 부분은 구조적으로 맞다. `NetworkManager.Awake()`에서 `HostMigrationHandler`가 없으면 같은 GameObject에 붙여 주기 때문에 일반적인 진입 흐름에서는 핸들러가 존재한다.

### 2-2. 마이그레이션 시작

위치:
- `Assets/Scripts/Network/HostMigrationHandler.cs:128`

`StartMigration(...)`은 아래 처리를 한다.

1. `_isMigrating = true`
2. `_migrationRecoverySucceeded = false`
3. `_aiTakeoverReady = false`
4. `GameEvents.TriggerHostMigrationStarted()` 호출
5. 마이그레이션 UI 표시
6. 현재 `GameManagers` 상태 캐싱
7. `RestartAsNewHostCoroutine(...)` 시작

이 단계에서 중요한 점은 `NetworkManager.OnShutdown(...)`, `NetworkManager.OnDisconnectedFromServer(...)`, connection loss fallback이 `_isMigrating` 상태를 보고 기본 disconnect 처리를 건너뛰도록 되어 있다는 것이다. 즉 HostMigration 중에 로비 fallback이 먼저 실행되어 복원 흐름을 끊는 문제를 피하려는 구조다.

### 2-3. 새 Runner 재시작

위치:
- `Assets/Scripts/Network/HostMigrationHandler.cs:207`
- `Assets/Scripts/Network/HostMigrationHandler.cs:396`

현재 구현은 "기존 Runner 유지" 전략이 아니라 **새 Runner를 만들고 HostMigrationToken으로 snapshot을 복원하는 전략**이다.

핵심 흐름:
1. 기존 Runner를 `Shutdown(false, ShutdownReason.HostMigration, false)`로 정리한다.
2. `NetworkRunner_Migrated` GameObject를 새로 만든다.
3. 새 `NetworkRunner`를 붙인다.
4. `PooledNetworkObjectProvider`, `NetworkSceneManagerDefault`를 붙인다.
5. `StartGameArgs.HostMigrationToken`에 Fusion이 준 토큰을 넣는다.
6. `StartGameArgs.HostMigrationResume = HostMigrationResume`를 연결한다.
7. 시작 성공 후 `NetworkManager.SetRunnerAfterMigration(newRunner)`로 active runner를 교체한다.

이 선택은 현재 코드베이스와 맞다. `GameManagers.Instance` 재바인딩, runtime object 재스폰, UI/상점/전투 복구, AI takeover가 모두 새 Runner 기준으로 작성되어 있기 때문이다.

---

## 3. 오류가 날 수 있는 지점과 해결 방법

## 오류 1. HostMigrationResume에서 snapshot 객체 spawn 실패

오류 위치:
- `Assets/Scripts/Network/HostMigrationHandler.cs:471`
- `Assets/Scripts/Network/HostMigrationHandler.cs:518`
- `Assets/Scripts/Network/HostMigrationHandler.cs:525`
- `Assets/Scripts/Network/HostMigrationHandler.cs:530`

증상 로그:
- `[HostMigrationHandler] Resume Spawn 실패`
- `Spawn 결과 null`
- `Scene 오브젝트 복원 실패`
- 새 Runner는 시작됐지만 `GameManagers` 또는 `PlayerManager`가 복원되지 않음

원인:
- `runner.GetResumeSnapshotNetworkObjects()`에서 받은 snapshot object를 새 Runner에 `runner.Spawn(...)`으로 다시 만들 때 실패할 수 있다.
- prefab 등록, object provider, scene object 매칭, snapshot source 유효성 중 하나가 틀어지면 `CopyStateFrom(...)` 또는 `Spawn(...)`에서 실패한다.
- scene `NetworkObject`는 runtime object처럼 spawn하면 안 되므로 `GetResumeSnapshotNetworkSceneObjects()`를 통해 기존 scene object에 상태만 복사해야 한다.

현재 코드의 해결 방식:
- runtime object는 `runner.Spawn(...)`으로 새로 만들고 `CopyStateFrom(...)`으로 snapshot state를 복사한다.
- scene object는 `RestoreSceneObjectsFromSnapshot(...)`에서 spawn하지 않고 `sceneNO.CopyStateFrom(resumeSource)`만 수행한다.
- `NetworkTRSP`가 있으면 snapshot의 위치/회전을 먼저 읽어 spawn 위치를 맞춘다.

왜 이 방법을 선택했는가:
- 현재 프로젝트는 이미 새 Runner를 만드는 전략으로 기울어져 있다.
- 이 상태에서 기존 object를 재사용하려 하면 `GameManagers`, `PlayerManager`, `FieldManager`, `MonsterSpawner`, `CommandProcessor` 참조를 모두 old/new Runner 사이에서 다시 설계해야 한다.
- 반대로 새 Runner + snapshot spawn 방식은 Fusion HostMigrationToken 흐름과 맞고, 이후 복구 게이트도 새 Runner 기준으로 검증할 수 있다.

추가 확인 방법:
- HostMigration 발생 후 로그에서 `[HostMigrationHandler] Resume Snapshot 오브젝트 수`가 0인지 확인한다.
- `[HostMigrationHandler] GameManagers 복원됨` 로그가 찍히는지 확인한다.
- `[HostMigrationHandler] Scene 오브젝트 복원 완료: copied=..., skipped=...`의 skipped가 비정상적으로 높은지 확인한다.

---

## 오류 2. old Runner 객체가 남아 더미 캐릭터가 보이는 문제

오류 위치:
- 과거 문제 위치: `HostMigrationHandler.HostMigrationResume(...)`에서 새 객체를 spawn하면서 old object를 정리하지 않는 구조
- 현재 보정 위치:
  - `Assets/Scripts/Network/HostMigrationHandler.cs:605`
  - `Assets/Scripts/Network/HostMigrationHandler.cs:724`
  - `Assets/Scripts/Network/HostMigrationHandler.cs:778`

증상:
- HostMigration 후 같은 캐릭터가 두 개 보임
- 하나는 움직이고 공격하지만, 하나는 기존 위치에 멈춰 있음
- 멈춘 캐릭터는 입력, 공격, AI, 전투 흐름에 참여하지 않음

원인:
- 현재 전략은 새 Runner에서 snapshot 기준으로 object를 다시 만든다.
- old Runner에 붙어 있던 `PlayerManager`, `Unit`, `Monster`, `GameManagers`가 장면에 남으면 화면상 중복으로 보인다.
- 게임 로직은 새 Runner 객체만 참조하므로 old object는 더미처럼 남는다.

현재 코드의 해결 방식:
- `HostMigrationResume(...)` 후 `CleanupStaleRuntimeObjects(runner)`를 호출한다.
- active runner 소속 object와 scene object는 보존한다.
- active runner가 아닌 old runner 소속 runtime gameplay object는 destroy한다.
- `GameManagers`, `PlayerManager`, `NetworkPlayer`, `Unit`, `Monster`, `CombatScheduler`가 붙은 object를 gameplay runtime marker로 본다.

왜 이 방법을 선택했는가:
- 새 Runner 복원 전략에서는 old object를 남기면 시각적 중복과 참조 혼선이 반드시 생긴다.
- scene object까지 무조건 지우면 씬 루트, grid, static object가 깨질 수 있다.
- 그래서 "runtime gameplay object만 제거하고, scene object는 snapshot state만 복사"하는 분리 방식이 가장 안전하다.

남은 리스크:
- `HasGameplayRuntimeMarker(...)`에 포함되지 않은 gameplay object가 old Runner에 남으면 cleanup 대상에서 빠질 수 있다.
- smoke check는 stale gameplay object를 탐지하지만, 모든 종류의 누락 object를 자동 복구하지는 않는다.

추가 해결 제안:
- HostMigration 이후 더미가 남으면 해당 object에 붙은 컴포넌트를 확인하고 `HasGameplayRuntimeMarker(...)` 대상에 추가한다.
- 로그 `[HM-SMOKE] FAIL ... staleGameplayObjects=...`가 찍히면 sample object 이름을 기준으로 marker 누락 여부를 확인한다.

---

## 오류 3. GameManagers 복원 게이트 실패

오류 위치:
- `Assets/Scripts/Network/HostMigrationHandler.cs:942`
- `Assets/Scripts/Network/HostMigrationHandler.cs:995`
- `Assets/Scripts/Network/HostMigrationHandler.cs:1031`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:98`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:123`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:131`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:139`

증상 로그:
- `[STEP 5] GameManagers 대기 시간 초과`
- `복원 중단: runnerMatched=false`
- `복원 중단: hasAuthority=false`
- `복원 중단: isReady=false`
- `playersReady=false`
- `RestoreAfterHostMigration:ABORT_NOT_READY`
- `RestoreAfterHostMigration:ABORT_RUNNER_MISMATCH`
- `RestoreAfterHostMigration:ABORT_NO_STATE_AUTH`

원인:
- `GameManagers.Instance`가 old Runner 객체를 가리키고 있을 수 있다.
- 새 Host가 `GameManagers.Object.HasStateAuthority`를 아직 얻지 못했을 수 있다.
- `GameManagers.Spawned()`가 아직 호출되지 않아 `IsReadyForNetworkAccess`가 false일 수 있다.
- `PlayerManager` 런타임 참조가 재결선되지 않아 `playersReady`가 false일 수 있다.

현재 코드의 해결 방식:
- `ResolveGameManagersForRunner(expectedRunner)`로 새 Runner 소속 `GameManagers`를 찾고 `GameManagers.Instance`를 재바인딩한다.
- 새 Host이면 `GameManagers.Object.RequestStateAuthority()`를 요청한다.
- `EnsurePlayersRuntimeReady(...)`에서 각 `PlayerManager.RebindRuntimeReferencesAfterMigration(...)`를 호출한다.
- 조건이 모두 만족될 때만 `gm.RestoreAfterHostMigration()`를 호출한다.

왜 이 방법을 선택했는가:
- `GameManagers`는 게임 상태, 라운드, 타이머, 플레이어 목록, CommandProcessor의 중심이다.
- 이 객체가 old Runner에 묶인 상태에서 flow를 재개하면 이후 모든 RPC, timer, command, UI 이벤트가 잘못된 Runner 기준으로 실행될 수 있다.
- 따라서 강제로 진행하지 않고 `Runner`, `StateAuthority`, `Spawned`, `PlayerManager runtime readiness`를 모두 게이트로 확인하는 것이 맞다.

추가 확인 방법:
- `[STEP 5.2][DETAIL]` 로그에서 `resolved`, `staticInstance`, `restoredCandidate`의 runner가 모두 `NetworkRunner_Migrated`인지 확인한다.
- `GameManagers.Object.HasStateAuthority`가 새 Host에서 true인지 확인한다.

---

## 오류 4. Game flow 재개 조건 대기에서 timeout

오류 위치:
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:269`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:293`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:335`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:354`

증상 로그:
- `[STEP 6] 조건 대기 중`
- `playersReady=false`
- `mappingReady=false`
- `timerReady=false`
- `wallMapReady=false`
- `aiTakeoverReady=false`
- `[STEP 6] timeout fallback 차단`

원인:
- HostMigration 직후 UI 복원, PlayerManager 참조 복구, battle mapping, phase timer, wall map, AI takeover 중 하나라도 준비되지 않으면 flow를 재개하지 않는다.
- 이 조건 중 하나가 8초 안에 true가 되지 않으면 timeout이 발생한다.

현재 코드의 해결 방식:
- `RestoreAfterHostMigration()`에서 phase timer를 먼저 일시정지한다.
- UI 복원은 `RestoreLocalUIAfterMigrationAsync()`로 비동기 실행한다.
- flow 재개는 `WaitForRestoreDependenciesAndResumeFlow()`에서 아래 조건을 모두 통과한 뒤에만 한다.
  - 새 Host의 StateAuthority
  - active Runner 일치
  - UI 복원 완료
  - PlayerManager runtime ready
  - Battle mapping ready
  - Timer ready
  - Wall map ready
  - AI takeover ready

왜 이 방법을 선택했는가:
- HostMigration 직후 가장 위험한 버그는 "겉으로는 이어졌지만 내부 참조가 old object를 가리키는 상태"다.
- 특히 Prepare와 Battle은 UI, timer, monster spawn, command 처리 순서가 맞지 않으면 중복 구매, 중복 스폰, 전투 멈춤이 생긴다.
- 그래서 재개 조건을 엄격하게 둔 것이 맞다.

추가 해결 제안:
- timeout 로그가 발생하면 `playersReason`, `mappingReason`, `timerReason`, `wallReason`, `aiReason` 중 어떤 값이 false인지 먼저 본다.
- 반복적으로 같은 reason이 나오면 해당 readiness 함수에 self-heal을 추가한다.
- 현재 smoke check는 탐지 중심이므로, 복구 가능한 항목은 smoke check가 아니라 gate 내부에서 재시도하도록 두는 것이 좋다.

---

## 오류 5. PlayerManager 런타임 참조 복구 실패

오류 위치:
- `Assets/Scripts/Managers/PlayerManager.cs:400`
- `Assets/Scripts/Managers/PlayerManager.cs:531`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:369`
- `Assets/Scripts/Network/HostMigrationHandler.cs:1067`

증상 로그:
- `fieldManager=null`
- `fieldManager.ground3D=null`
- `fieldManager.wallMapNotReady`
- `fieldManager.unitMapNotReady`
- `monsterSpawner` runtime not ready
- `runnerPlayers=0(valid)`

원인:
- snapshot spawn 직후에는 `PlayerManager` 하위의 `FieldManager`, `AstarGrid`, `MonsterSpawner`, `ShopManager`, `AugmentManager`, unit map, wall map이 아직 완전히 재연결되지 않을 수 있다.
- 특히 old/new Runner object가 겹쳤던 경우에는 `FindObjectsOfType` 결과가 old object와 new object를 함께 반환할 수 있다.

현재 코드의 해결 방식:
- `PlayerManager.RebindRuntimeReferencesAfterMigration(...)`에서 하위 참조를 다시 찾는다.
- `FieldManager.RebuildWallMapsAfterMigration(...)`와 `RebuildUnitMapAfterMigration(...)`를 호출한다.
- `GameManagers.PlayerRegistry.RebuildNetworkPlayersAfterMigration(...)`에서 `player.Runner == Runner`인 PlayerManager만 다시 등록한다.

왜 이 방법을 선택했는가:
- HostMigration 이후에는 serialized reference보다 runtime object의 현재 Runner 소속 여부가 더 중요하다.
- `playerId`만 믿으면 old/new 중복 객체 중 잘못된 쪽을 잡을 수 있다.
- 그래서 `Runner == expectedRunner`, `Object.IsValid`, `playerId >= 0`을 기준으로 재등록하는 것이 안정적이다.

---

## 오류 6. Battle 중 HostMigration 후 전투가 멈추거나 몬스터가 다시 안 나오는 문제

오류 위치:
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:528`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:598`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:607`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:626`
- `Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs:641`

증상 로그:
- `BATTLE-REBOOTSTRAP:SKIP_NO_ATTACKER`
- `BATTLE-REBOOTSTRAP:SKIP_RUNTIME_NOT_READY`
- `BATTLE-REBOOTSTRAP:FAIL_POOL_EMPTY`
- `[HM-SMOKE] FAIL ... battleSpawnPending`

원인:
- Battle1/Battle2 도중 HostMigration이 발생하면 공격자/수비자 매핑, 공격 몬스터 풀, 수비자 필드 몬스터 상태, 전투 UI 이벤트가 중간 상태로 끊길 수 있다.
- AI 공격자는 자동 spawn 경로가 있지만, 사람 공격자는 UI/manual input 경로라 smoke check가 상대적으로 약하다.
- late-phase migration에서는 `allowPoolRefresh`가 false가 될 수 있어 공격 몬스터 풀을 새로 채우지 않는다.

현재 코드의 해결 방식:
- `EnsureBattleMappingAfterMigration()`으로 전투 매핑을 복구한다.
- `TryAcquireBattleRebootstrapKey(...)`로 같은 전투를 중복 재부트스트랩하지 않는다.
- defender 필드에 이미 살아 있는 몬스터가 있으면 중복 spawn을 하지 않는다.
- AI 공격자는 `SpawnAllMonstersToTargetField(...)`를 다시 시도한다.
- 사람 공격자는 `RPC_NotifyBattleStart(...)`를 재발행해 UI/카메라/입력 경로를 다시 열도록 한다.

왜 이 방법을 선택했는가:
- Battle 재개는 "무조건 다시 spawn"하면 중복 몬스터가 생기는 위험이 크다.
- 그래서 먼저 매핑과 runtime readiness를 확인하고, 이미 전투가 진행 중이면 건너뛰며, 필요한 경우에만 key 기반으로 한 번 재부트스트랩하는 방식이 맞다.

남은 리스크:
- 사람 공격자 경로의 UI readiness는 AI attacker보다 검증이 약하다.
- 전투 종료 직전 HostMigration이 발생했을 때 pool refresh를 하지 않는 정책이 모든 케이스에 맞는지는 추가 테스트가 필요하다.

추가 해결 제안:
- human attacker용 smoke check를 추가한다.
- 전투 UI 표시 여부, 카메라 전환 여부, 수동 몬스터 소환 입력 가능 여부를 확인하는 테스트를 만든다.
- late-phase migration에서는 "전투를 유지할지, no-op 후 다음 phase로 넘길지" 정책을 명확히 정한다.

---

## 오류 7. migration 직후 재접속 플레이어 reassociation 실패

오류 위치:
- `Assets/Scripts/Network/NetworkManager.cs:728`
- `Assets/Scripts/Network/NetworkManager.cs:758`
- `Assets/Scripts/Network/HostMigrationHandler.cs:1377`
- `Assets/Scripts/Network/HostMigrationHandler.cs:1588`

증상:
- HostMigration 직후 늦게 재접속한 플레이어가 기존 `PlayerManager`에 다시 붙지 못함
- `TryReassociateDisconnectedPlayer(...)`가 false를 반환함
- 새 player object가 생기거나, 입력 권한이 돌아오지 않음

원인:
- 이탈 플레이어 데이터는 `_cachedPlayerData`에 저장된다.
- 그런데 `OnMigrationComplete()`에서 `_cachedPlayerData.Clear()`를 바로 호출한다.
- 즉 migration 완료 직후 조금 늦게 들어오는 재접속 플레이어는 캐시를 못 찾을 수 있다.

해결 방법:
- `_cachedPlayerData`를 즉시 비우지 말고 TTL 방식으로 유지한다.
- 예: migration 완료 후 30초 또는 60초 동안 유지하고, 재접속 성공 시 해당 token만 제거한다.
- 또는 방이 완전히 종료되거나 MatchingLobby로 돌아갈 때 전체 clear한다.

왜 이 방법을 선택해야 하는가:
- HostMigration과 reconnect는 네트워크 상황에 따라 순서가 흔들린다.
- 캐시를 너무 빨리 지우면 정상 재접속도 실패한다.
- 반대로 TTL을 두면 stale cache 위험은 있지만, token과 playerId를 함께 비교하므로 오인식 위험을 낮출 수 있다.

---

## 오류 8. 복구 실패 시 사용자 fallback이 약함

오류 위치:
- `Assets/Scripts/Network/HostMigrationHandler.cs:1377`
- `Assets/Scripts/Network/HostMigrationHandler.cs:1391`
- `Assets/Scripts/Network/HostMigrationHandler.cs:1401`

증상:
- `[MIGRATION COMPLETE] 마이그레이션은 끝났지만 게임 복원은 실패했습니다.`
- 이후 자동 로비 복귀, 재시도, 실패 UI가 명확하지 않음

원인:
- `_migrationRecoverySucceeded == false`이면 로그만 남기고 `GameEvents.TriggerHostMigrationCompleted(isNewHost)`를 발행한다.
- 복구 실패 후 어떤 UX로 이동할지 정책이 아직 약하다.

해결 방법:
1. 복구 실패 시 정책을 명확히 정한다.
2. 추천 정책:
   - 새 Runner 시작 실패: 즉시 MatchingLobby로 이동
   - 새 Runner는 시작됐지만 `GameManagers` 복원 실패: 실패 UI 표시 후 MatchingLobby 이동
   - smoke check 실패: 게임은 유지하되 문제 유형을 UI 또는 debug overlay로 노출
3. 자동 재시도는 한 번만 허용한다. 무한 재시도는 Runner/Session 상태를 더 꼬이게 만들 수 있다.

왜 이 방법을 선택해야 하는가:
- HostMigration 복구 실패는 부분적으로 이어진 상태가 가장 위험하다.
- 플레이어 입장에서는 멈춘 게임보다 명확한 실패 처리와 로비 복귀가 낫다.
- 재시도는 성공 가능성이 있지만, 네트워크 세션과 scene object 상태가 이미 바뀐 뒤라 여러 번 반복하면 중복 object와 callback 문제가 커질 수 있다.

---

## 4. 현재 코드에서 잘 되어 있는 부분

현재 HostMigration 구현에서 긍정적인 부분은 아래다.

1. `NetworkProjectConfig.fusion`에서 HostMigration auto update가 켜져 있다.
2. `NetworkManager.OnHostMigration(...)` 진입점이 있고 `HostMigrationHandler`로 책임이 분리되어 있다.
3. HostMigration 중 `OnShutdown`, `OnDisconnectedFromServer`, `CloudConnectionLost` fallback이 복원 흐름을 방해하지 않도록 막고 있다.
4. 새 Runner를 만든 뒤 `NetworkManager._runner`를 새 Runner로 교체한다.
5. `GameManagers.Instance`를 새 Runner 소속 object로 재바인딩한다.
6. `GameManagers` 복원 전에 Runner 일치, StateAuthority, Spawned, PlayerManager runtime readiness를 확인한다.
7. wall map / unit map 재구성 루틴이 있다.
8. Battle 재부트스트랩과 AI takeover 루틴이 있다.
9. HostMigration 후 smoke check가 있다.
10. 과거 더미 object 문제의 핵심 원인이었던 stale runtime object cleanup이 현재 코드에 들어가 있다.

---

## 5. 실제 테스트 체크리스트

실제 Play Mode 또는 빌드 테스트에서는 아래 순서로 확인하면 된다.

1. Host 1개, Client 1개 이상으로 세션 시작
2. Prepare 상태에서 Host 종료
3. 새 Host가 이어받는지 확인
4. `GameManagers.Instance.Runner == NetworkManager._runner`인지 확인
5. 새 Host에서 `GameManagers.Object.HasStateAuthority == true`인지 확인
6. 상점 UI, 증강 UI, 구매/배치 입력이 정상인지 확인
7. Battle1 시작 직후 Host 종료
8. 전투 UI, 카메라, 몬스터 spawn, 공격 진행이 정상인지 확인
9. Battle 중반/후반 Host 종료
10. 중복 몬스터나 멈춘 더미 캐릭터가 남는지 확인
11. HostMigration 직후 기존 플레이어가 재접속했을 때 같은 `PlayerManager`에 input authority가 다시 붙는지 확인
12. 로그에서 `[HM-SMOKE] PASS`가 찍히는지 확인

중점 로그:
- `[NetworkManager] OnHostMigration 호출됨!`
- `[HostMigrationHandler] Host Migration 시작`
- `[HostMigrationHandler] StartGame 성공!`
- `[HostMigrationHandler] GameManagers 복원됨`
- `[STEP 5.3] GameManagers 준비 완료!`
- `[STEP 6] 재개 조건 충족`
- `[MIGRATION COMPLETE] Host Migration 성공!`
- `[HM-SMOKE] PASS`

실패 로그:
- `Resume Spawn 실패`
- `Spawn 결과 null`
- `GameMode 불일치`
- `GameManagers 대기 시간 초과`
- `RestoreAfterHostMigration:ABORT_*`
- `timeout fallback 차단`
- `[HM-SMOKE] FAIL`

---

## 6. 검증 결과

### C# 빌드

명령:

```powershell
dotnet build .\Assembly-CSharp.csproj --no-restore
```

결과:
- 오류: 0개
- 경고: 19개

HostMigration 관련 컴파일 실패는 없었다.

### Unity 배치모드

시도한 명령:

```powershell
C:\Program Files\Unity\Hub\Editor\2021.3.45f1\Editor\Unity.exe -batchmode -projectPath D:\Unity\Mdf\Mdf\Mdfproject -quit -logFile D:\Unity\Mdf\Mdf\Mdfproject\Logs\hostmigration-compile-check.log
```

결과:
- 같은 프로젝트를 이미 열고 있는 Unity 인스턴스가 있어서 중단됨
- 로그: `It looks like another Unity instance is running with this project open.`
- 따라서 Unity Editor 기준 스크립트 리로드 검증은 완료하지 못함

---

## 7. 최종 판단

HostMigration은 현재 코드 구조상 **동작 가능하도록 상당히 많이 보강되어 있다.** 특히 새 Runner 재시작, snapshot 복원, `GameManagers` 재바인딩, stale runtime object cleanup, 복원 게이트, AI takeover, smoke check가 모두 들어가 있다.

하지만 아직 완전하다고 단정하면 안 된다. 가장 큰 남은 위험은 실제 런타임에서 다음 네 가지다.

1. `HostMigrationResume(...)`에서 일부 object가 spawn/copy 실패하는 경우
2. `GameManagers` 또는 `PlayerManager` readiness gate가 timeout 나는 경우
3. Battle 후반 HostMigration에서 human attacker UI/입력 경로가 완전히 복원되지 않는 경우
4. migration 직후 재접속 플레이어 캐시가 너무 빨리 지워지는 경우

---

## 8. 간략한 설명

현재 HostMigration 코드는 컴파일 오류는 없고, Fusion 설정도 켜져 있어서 기본 구조는 맞다. 실패가 난다면 대부분 새 Runner 복원 이후 `GameManagers`, `PlayerManager`, battle/UI/wall map 준비 조건 중 하나가 맞지 않아서 발생할 가능성이 높다. 해결 방향은 기존 object를 억지로 재사용하는 것이 아니라, 현재 코드 흐름에 맞게 **새 Runner + snapshot 복원 전략을 유지하고 old runtime object 정리와 복원 게이트를 강화하는 것**이다.
