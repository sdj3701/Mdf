# MDF 게임 프로그램 안정성 강화 계획

작성일: 2026-06-12  
대상: `Mdfproject` Unity 2021.3.45f1 / Photon Fusion 2.0.9 프로젝트  
관련 문서:

- `docs/product-backlog.md`
- `docs/ui-optimization-plan.md`
- `docs/ui-optimization-baseline.md`
- `docs/ai-harness/feature-implementation-loop.md`
- `docs/ai-harness/fusion-sync-rules.md`
- `docs/ai-harness/mp-test-protocol.md`
- `docs/ai-harness/verification-profile-selector.md`
- `docs/ai-harness/long-progression-test-plan.md`

## 1. 목적

이 문서는 MDF의 "게임 프로그램 안정성"을 올리기 위한 기준, 우선순위, 검증 방법을 정리한다.

여기서 안정성은 단순히 Unity compile이 통과하는 상태가 아니다. 실제 멀티플레이어 실행에서 방 생성, 참가, 게임 씬 진입, 준비, 전투, 라운드 반복, reconnect, Host Migration, GameOver 또는 장기 진행 중단까지 관측했을 때 프로그램이 멈추거나, 동기화가 깨지거나, 복구 불가능한 상태로 빠지지 않는 것을 의미한다.

## 2. 안정성 목표

안정성 개선의 목표는 다음 다섯 가지다.

1. 실행 실패 감소: Title, MatchingLobby, JoinLobby, Game 씬 진입 중 NullReference, timeout, 중복 runner, 데이터 미로드 상태를 줄인다.
2. 멀티플레이 동기화 보존: State Authority가 소유해야 하는 상태는 `[Networked]`, deterministic rebuild, snapshot 비교 중 하나로 검증 가능해야 한다.
3. 장기 진행 보장: 최소 3라운드 진행과 GameOver/endurance 경로에서 stall, timeout, drift를 분류하고 줄인다.
4. 복구 흐름 강화: reconnect, disconnect/AI takeover, Host Migration 이후 durable `playerId`, field, shop, battle, UI reference가 복구되어야 한다.
5. 반복 검증 가능성 확보: E2E 종료 후 `cleanupStatus=PASS`, `orphanedPids=[]`를 유지해 테스트 환경 오염을 줄인다.

## 3. 범위

주요 범위:

- Fusion runner, lobby, scene load, player join/left, reconnect, Host Migration.
- `GameManagers`, `PlayerManager`, `FieldManager`, `ShopManager`, `MonsterSpawner`, `AttackSequenceManager`, `CommandProcessor`.
- 준비 단계와 전투 단계의 UI/AI/HumanBot command request 경로.
- Addressables, UnitData, MonsterData, Scroll/Augment data loading.
- MP harness, snapshot, cleanup, long progression, lifecycle profile.

제외 범위:

- 전면적인 UI 아트 리디자인.
- Photon Fusion, Firebase, TMP, Toon Shader, 샘플 또는 generated vendor 파일 수정.
- 제품 밸런스 조정만을 목적으로 하는 수치 변경.
- 증거 없이 timeout 값을 크게 늘리는 방식의 안정화.

## 4. 현재 기준점

현재 프로젝트에는 안정성 개선을 진행할 수 있는 기반이 이미 있다.

- 기본 씬 흐름은 `00_Title -> 01_MatchingLobby -> 02_JoinLobby -> 03_Game`로 정리되어 있다.
- 주요 gameplay action은 `CommandProcessor`와 State Authority 검증 경로를 사용한다.
- Host/client/build/editor snapshot 비교와 `[MPTEST]` timeline이 존재한다.
- `smoke`, `battle`, `lifecycle`, `long`, `endurance` 검증 profile이 정의되어 있다.
- UI 최적화 기준선 문서에서 startup, scene load, screenshot, snapshot, cleanup 관측 항목이 이미 정리되어 있다.

남은 핵심 리스크:

- `GameManagers`, `PlayerManager`, `FieldManager`, `NetworkManager`, `HostMigrationHandler`가 크고 책임이 넓어 작은 변경도 side effect가 생기기 쉽다.
- reconnect/Host Migration 이후 runtime reference rebind와 UI 갱신이 stale해질 수 있다.
- 장기 진행은 timeout, stall, tuning, sync drift를 구분해서 추적해야 한다.
- Addressables/data loading 실패가 사용자 화면과 harness artifact에 충분히 드러나야 한다.
- E2E 환경에 orphan process가 남으면 gameplay failure처럼 보이는 false failure가 생길 수 있다.

## 5. 안정성 원칙

### 5.1 Authority 우선

Persistent gameplay state는 State Authority가 결정해야 한다. Client는 요청만 보낼 수 있고, 요청 자체를 성공으로 간주하면 안 된다.

유지할 규칙:

- `PlayerRef`는 연결 identity다. Durable gameplay identity는 `playerId`와 connection token/cache로 판단한다.
- 새 gameplay action은 command class, `CommandType`, serialization, deserialization, UI/AI caller, snapshot/assertion을 함께 갱신한다.
- RPC는 event/request다. 영속 상태는 `[Networked]`, deterministic rebuild, snapshot으로 남긴다.
- UI는 local click 결과가 아니라 authority result event 또는 replicated state를 기준으로 표시한다.

### 5.2 Snapshot으로 증명

안정성 PASS는 "에러가 안 보였다"가 아니라 "같은 player의 durable state가 peer 간 일치한다"로 판단한다.

필수 비교 대상:

- scene, runner, session, tick, role.
- `GameManagers.currentState`, `currentRound`, `phaseTimer`, battle pairing.
- player별 HP, gold, wall count, shop, augment, connection, AI takeover.
- field별 placed unit hash, wall hash, path readiness.
- monster/alive count, attack pool, scroll/effect summary.
- Host Migration/reconnect status.
- command sequence와 마지막 accepted command.

### 5.3 장기 진행은 별도 gate

짧은 smoke가 통과해도 장기 진행이 안정적이라는 뜻은 아니다.

운영 기준:

- 일반 변경: `smoke`를 최소 gate로 사용한다.
- persistent state 또는 lifecycle 변경: `lifecycle`을 사용한다.
- 라운드 진행, battle flow, timer, AI/HumanBot 변경: `long`을 사용한다.
- GameOver 또는 endurance 요청: `endurance`를 명시적으로 사용한다.
- merge 전 큰 변경: `full-regression`을 사용한다.

### 5.4 Cleanup은 안정성의 일부

테스트 종료 후 process가 남는 것은 환경 문제가 아니라 제품 안정성 검증을 방해하는 직접 리스크다.

E2E PASS 최소 조건:

- `cleanupStatus=PASS`
- `orphanedPids=[]`
- `[MPTEST] phase=error` 없음
- snapshot comparison success
- command output과 artifact path 존재

## 6. 우선순위 로드맵

### P0. 안정성 기준선 고정

목표:

기능을 더 추가하기 전에 "현재 어디까지 안정적인가"를 반복 가능한 artifact로 남긴다.

작업:

- 최신 Development player 경로, branch, commit, Unity version을 기록한다.
- `smoke` profile을 기준선으로 실행하고 case별 startup, scene load, first snapshot, cleanup 결과를 기록한다.
- Unity console user error가 0인지 확인한다.
- 현재 알려진 실패가 있으면 gameplay failure, environment failure, harness failure로 분류한다.
- `docs/ui-optimization-baseline.md`처럼 artifact path와 결과 요약을 남긴다.

완료 기준:

- `unity-cli --project Mdfproject editor refresh --compile` PASS.
- `unity-cli --project Mdfproject console --type error --stacktrace user` 결과가 빈 목록.
- `run_matrix.py --profile smoke`에서 모든 case가 artifact와 cleanup report를 남긴다.
- 실패가 있다면 exact reason과 재현 artifact가 기록되어 있다.

### P0. Runtime exception 제거

목표:

사용자 플레이 또는 MP harness 중 발생하는 NullReference, InvalidOperation, data wait timeout, duplicate runner 문제를 먼저 줄인다.

작업:

- 최근 artifacts의 Host/Client `Player.log`, `host-logs-recent.json`, `client-logs-recent.json`에서 exception pattern을 수집한다.
- Null guard를 추가하기 전에 "왜 reference가 비었는지"를 scene lifecycle, migration/rebind, data loading 순서로 분류한다.
- 임시 fallback search는 migration/reconnect 또는 legacy scene 호환에만 제한한다.
- Addressables/data loading timeout은 화면 표시, `[MPTEST]` log, failure artifact에 남긴다.

완료 기준:

- 같은 seed와 같은 player build에서 동일 exception이 재현되지 않는다.
- console user error가 0이다.
- 관련 E2E artifact에서 `[MPTEST] phase=error`가 없다.

### P0. Command/state drift 차단

목표:

요청은 성공했지만 durable state가 다르게 남는 문제를 줄인다.

작업:

- `CommandProcessor` serialization/deserialization과 command execution 결과를 audit한다.
- `RpcSources.All` 경로는 `RpcInfo.Source`, owner, `playerId`, phase, cost, cooldown, command sequence를 검증한다.
- 새 command 또는 기존 command 수정 시 snapshot field를 먼저 정의한다.
- UI/HumanBot/AI가 presentation helper나 low-level mechanism을 직접 호출하지 않는지 precommit 경고를 확인한다.

완료 기준:

- `python tools/harness/precommit.py --all` PASS.
- battle command 관련 BLOCK 0건.
- relevant profile snapshot comparison success.

### P1. 3라운드 장기 진행 gate 정착

목표:

짧은 smoke 너머에서 준비/전투 반복이 안정적으로 유지되는지 확인한다.

작업:

- `human-bot-3round-progression`을 정기 안정성 gate로 사용한다.
- round/state checkpoint별 snapshot을 남긴다.
- failure를 timeout, stall, tuning, sync drift, environment cleanup 중 하나로 분류한다.
- random outcome은 고정값이 아니라 same-player hash와 invariant로 비교한다.

완료 기준:

- `maxRoundReached >= 4` 또는 target round complete.
- checkpoint assertion PASS.
- `cleanupStatus=PASS`, `orphanedPids=[]`.
- failure가 발생하면 category와 다음 조사 파일이 명확하다.

### P1. Lifecycle 복구 강화

목표:

전투 이후 reconnect, disconnect/AI takeover, Host Migration에서도 durable state가 유지되도록 한다.

작업:

- same-token reconnect는 같은 `playerId`와 token hash reclaim을 증명한다.
- disconnect는 대상 player가 `isConnected=false`, `isAI=true`, AI controller registered 상태로 바뀌는지 확인한다.
- Host Migration은 실제 callback/token/resume/recovery evidence를 요구한다.
- Migration 후 `GameManagers.Instance`, local player, UI controllers, field/shop/attack sequence reference가 rebind되는지 확인한다.

완료 기준:

- `run_matrix.py --profile lifecycle` PASS.
- Host Migration artifact에 `OnHostMigration`, token, `HostMigrationResume`, recovery, post-migration snapshot이 있다.
- normal `/quit`을 Host Migration proof로 사용하지 않는다.

### P1. UI/input 안정화

목표:

UI가 gameplay state를 잘못 바꾸거나, field input을 막거나, migration 후 stale state를 표시하는 문제를 줄인다.

작업:

- UI Toolkit panel raycaster가 빈 공간 field click을 막지 않는지 유지한다.
- 상점, 증강, 벽, 유닛, 공격 시퀀스 UI는 authority success event 또는 replicated state만 표시한다.
- migration/reconnect 후 UI reference refresh를 명시 이벤트 기반으로 정리한다.
- mobile/touch 환경에서 tap, drag, scroll 충돌을 별도 검증한다.

완료 기준:

- UI screenshot과 snapshot이 모두 성공한다.
- 수동/자동 wall placement는 screenshot만이 아니라 `wallCount`, `destructibleWallCount`, `wallHash` 변화로 증명한다.
- stale ranking/shop/augment 표시가 재현되지 않는다.

### P2. Endurance와 제품 UX 복구

목표:

GameOver 또는 장시간 진행 중 사용자에게 납득 가능한 상태 표시와 복구 흐름을 제공한다.

작업:

- `human-bot-game-to-end`를 명시 opt-in gate로 유지한다.
- timeout이면 진행도, 마지막 정상 checkpoint, 다음 tuning 후보를 기록한다.
- reconnect 중 표시, AI takeover 안내, Host Migration 대기/복귀 화면을 제품 UX로 정리한다.
- GameOver 결과, ranking, 재시작/로비 복귀 흐름을 안정화한다.

완료 기준:

- actual GameOver 또는 bounded timeout/stall classification.
- final snapshots agree.
- duplicate `playerId` 없음.
- cleanup PASS.

## 7. 안정성 작업 목록

| ID | 우선순위 | 영역 | 작업 | 검증 |
| --- | --- | --- | --- | --- |
| STAB-001 | P0 | 기준선 | 최신 player build와 smoke matrix 기준선 문서화 | `smoke`, console error 0, cleanup PASS |
| STAB-002 | P0 | 로그 | 최근 E2E exception/error pattern 수집 및 분류 | Host/Client logs, `[MPTEST]` timeline |
| STAB-003 | P0 | Runner | duplicate `StartGame`, duplicate runner, stale session state 재점검 | lobby/game smoke 양방향 |
| STAB-004 | P0 | Data | UnitData/Addressables/AugmentData load timeout 처리와 artifact 기록 강화 | data load failure injection 또는 targeted smoke |
| STAB-005 | P0 | Command | command validation audit: phase, cost, owner, playerId, cooldown, sequence | precommit, command-specific E2E |
| STAB-006 | P0 | Snapshot | persistent state가 RPC-only로 남은 영역 확인 | snapshot schema diff, lifecycle |
| STAB-007 | P1 | Long | 3라운드 진행 gate 정기화 | `long` profile |
| STAB-008 | P1 | Lifecycle | battle 이후 reconnect/disconnect/Host Migration 회귀 검증 | `lifecycle` profile |
| STAB-009 | P1 | UI | migration/reconnect 후 UI rebind와 stale display 방지 | visual screenshot + snapshot |
| STAB-010 | P1 | Cleanup | orphan pressure gate와 cleanup report 누락 case 점검 | cleanup report, live process count |
| STAB-011 | P2 | Endurance | GameOver/endurance 결과 분류 체계 유지 | `endurance` profile |
| STAB-012 | P2 | UX | 네트워크 실패, reconnect, migration 대기/복귀 사용자 표시 정리 | manual UX pass + targeted E2E |

## 8. 검증 명령 세트

기본 정적/Unity 검증:

```powershell
python tools/harness/precommit.py --all
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

기본 MP smoke:

```powershell
python tools/harness/mp/run_matrix.py --profile smoke --headless-player
```

Persistent state, reconnect, disconnect, Host Migration 관련:

```powershell
python tools/harness/mp/run_matrix.py --profile lifecycle --headless-player
```

라운드 반복 안정성:

```powershell
python tools/harness/mp/run_matrix.py --profile long --headless-player
```

큰 변경 또는 merge 전:

```powershell
python tools/harness/mp/run_matrix.py --profile full-regression --headless-player
```

GameOver/endurance 명시 요청:

```powershell
python tools/harness/mp/run_matrix.py --profile endurance --headless-player
```

시각 검증이 필요한 UI 변경은 `--headless-player`를 빼고 screenshot artifact를 확인한다.

## 9. 실패 분류 기준

안정성 실패는 다음 중 하나로 분류한다.

| 분류 | 의미 | 첫 확인 파일 |
| --- | --- | --- |
| `COMPILE_ERROR` | C# compile 실패 | Unity compile output |
| `CONSOLE_ERROR` | Unity user stacktrace error 존재 | `unity-cli console`, Player logs |
| `STARTUP_TIMEOUT` | player ping, runner start, lobby join timeout | `result.json`, automation response |
| `SCENE_LOAD_FAILURE` | Game scene load 또는 first snapshot 실패 | load game artifact, snapshot wait |
| `COMMAND_REJECTED` | command가 authority validation에서 거절됨 | command logs, assertions |
| `STATE_DRIFT` | peer snapshot comparison 실패 | `comparison*.json` |
| `LIFECYCLE_RECOVERY_FAIL` | reconnect/disconnect/migration 복구 실패 | lifecycle assertions |
| `STALL` | 진행이 살아 있으나 target state에 도달하지 않음 | timeline, phase/round checkpoints |
| `TIMEOUT` | 전체 timeout 초과, 원인 추가 분류 필요 | harness stdout/stderr |
| `CLEANUP_FAIL` | process 정리 실패 또는 orphan 발생 | `cleanup-report.json` |
| `NEEDS_ENVIRONMENT` | orphan pressure, GPU/D3D pressure 등 환경 차단 | cleanup report, process count |

## 10. 완료 판정

안정성 작업은 다음 조건을 만족해야 완료로 본다.

- 변경 파일과 위험 영역이 명확하다.
- compile과 console 검증 결과가 기록되어 있다.
- 관련 profile 또는 targeted case의 artifact path가 있다.
- E2E를 실행했다면 `cleanupStatus=PASS`, `orphanedPids=[]`가 있다.
- snapshot comparison이 성공했거나, 실패라면 exact diff와 다음 작업이 기록되어 있다.
- Host Migration 관련 작업은 실제 Fusion callback/token/resume evidence가 있다.
- 새로 발견한 반복 가능한 방법이나 함정은 learned recipe로 남긴다.

## 11. 첫 안정화 스프린트 제안

첫 스프린트는 코드 대수술보다 관측과 P0 리스크 제거에 집중한다.

1. `STAB-001`: 최신 기준선 smoke matrix를 다시 남긴다.
2. `STAB-002`: 최근 artifacts에서 exception/error pattern을 표로 정리한다.
3. `STAB-005`: command validation/precommit 경고를 먼저 정리한다.
4. `STAB-007`: `human-bot-3round-progression`을 한 번 실행해 장기 진행 현재 상태를 고정한다.
5. `STAB-010`: cleanup report 누락과 orphan pressure gate가 모든 주요 script에 적용되는지 확인한다.

이 순서가 끝나면 이후 작업은 "느낌상 안정화"가 아니라 실패 분류와 artifact를 기준으로 하나씩 줄일 수 있다.
