# MDF 로드 시간 개선 계획

작성일: 2026-06-16  
대상: `Mdfproject` Unity 2021.3.45f1 / Photon Fusion 2.0.9  
관련 문서:

- `docs/ui-optimization-plan.md`
- `docs/ui-optimization-baseline.md`
- `docs/program-stability-plan.md`
- `docs/ai-harness/mp-test-protocol.md`
- `docs/ai-harness/automation-server-contract.md`
- `docs/ai-harness/verification-profile-selector.md`

## 1. 목적

이 문서는 MDF의 로드 시간을 줄이기 위한 별도 최적화 계획이다.

기존 `docs/ui-optimization-plan.md`의 주목표는 플레이 중 UI refresh, GC, 중복 sprite load, raycast 비용을 줄이는 것이었다. 그 작업은 장시간 플레이 중 프레임 안정성에는 도움이 되지만, `Title -> MatchingLobby -> Game -> 첫 Prepare`까지의 전체 로드 시간을 직접 줄인다고 보장할 수 없다.

따라서 로드 시간 개선은 별도 목표로 분리한다.

목표:

- Player 실행 후 자동화 서버 응답까지의 시간을 줄인다.
- 로비 Host/Client join 대기 시간을 줄인다.
- Host가 Game 씬을 요청한 뒤 첫 Prepare snapshot까지의 시간을 줄인다.
- Addressables, 데이터베이스, 프리팹, UI 리소스 로딩을 필요한 시점과 우선순위로 나눈다.
- 초기 로딩 중 main thread spike와 불필요한 전체 preload를 줄인다.
- 실제 개선 여부를 smoke timestamp가 아니라 세분화된 marker로 증명한다.

## 2. 현재 관찰

`docs/ui-optimization-baseline.md`의 최종 재측정 기준:

| 이벤트 | 최적화 전 | UI 최적화 후 | 차이 |
| --- | ---: | ---: | ---: |
| Host 자동화 ping 성공 | 11.52초 | 11.09초 | -0.43초 |
| Client 자동화 ping 성공 | 15.43초 | 14.93초 | -0.50초 |
| Host runner 시작 확인 | 19.80초 | 19.36초 | -0.44초 |
| Client join 확인 | 23.90초 | 26.50초 | +2.60초 |
| Host Game 씬 load 요청 성공 | 24.95초 | 27.56초 | +2.61초 |
| Host 첫 Prepare snapshot | 29.07초 | 31.67초 | +2.60초 |
| Host clean-prepare screenshot | 29.62초 | 32.29초 | +2.67초 |
| `result.json` 작성 | 41.24초 | 45.07초 | +3.83초 |

해석:

- 초기 Player ping은 소폭 개선됐다.
- 로비 join 이후 Game 진입 구간은 느려졌다.
- smoke timestamp만으로는 UI 최적화 효과와 로드 병목을 분리할 수 없다.
- 로드 시간 개선을 위해서는 `프로세스 시작`, `Addressables 초기화`, `로비 네트워크 join`, `Game 씬 로드`, `데이터 준비`, `첫 UI 표시`, `첫 snapshot 안정화`를 별도 marker로 분리해야 한다.

## 3. 로드 시간 분해 기준

앞으로 로드 시간은 하나의 총합이 아니라 아래 구간으로 나눠 기록한다.

| ID | 구간 | 시작 | 종료 | 주요 의심 병목 |
| --- | --- | --- | --- | --- |
| LOAD-001 | Player boot | 프로세스 실행 | `/ping` 성공 | IL2CPP/Development boot, Addressables 초기화, 초기 singleton 생성 |
| LOAD-002 | Lobby start | `/startHost` 또는 `/join` 요청 | runner running, lobby scene ready | Fusion runner start, Photon lobby/session join |
| LOAD-003 | Room ready | host/client runner 시작 | 모든 peer active player count 충족 | Photon join propagation, AI fill, player object spawn |
| LOAD-004 | Game scene load | Host `load_game Game` 요청 | `03_Game` scene active | Fusion scene load, scene object activation |
| LOAD-005 | Core data ready | Game scene active | 핵심 managers/data ready | AddressablesManager, Unit/Monster/Wave/Augment/Shop data |
| LOAD-006 | First UI paint | Game scene active | Prepare HUD와 최소 버튼 표시 | UI Toolkit/UXML/USS/Resources load, legacy bridge |
| LOAD-007 | First playable Prepare | Game scene active | first stable Prepare snapshot | GameManagers state, PlayerManager, FieldManager, shop/augment sync |
| LOAD-008 | Visual capture overhead | Prepare ready | screenshot artifact 작성 | 하네스 스크린샷, hide transient UI |

성능 판단은 우선 `LOAD-004`부터 `LOAD-007`까지를 핵심으로 본다. 이 구간이 사용자가 "게임 들어갈 때 느리다"고 느끼는 부분에 가장 가깝다.

## 4. 기록해야 할 지표

### 4.1 필수 marker

Development build와 MP test mode에서만 아래 marker를 남긴다.

| Marker | 기록 위치 후보 | 의미 |
| --- | --- | --- |
| `boot_begin` | `MPTestBootstrap` 또는 automation bootstrap | Player 프로세스 시작 이후 test bootstrap 진입 |
| `automation_ready` | automation server start | `/ping` 응답 가능 |
| `addressables_init_begin/end` | `AddressablesManager.PreloadAllAsync` 또는 초기화 wrapper | Addressables 초기화 비용 |
| `addressables_preload_all_begin/end` | `AddressablesManager.PreloadAllAsync` | 전체 preload 비용과 대상 개수 |
| `game_prefabs_load_begin/end` | `AddressablesManager.LoadGamePrefabsAsync` | PlayerManager/Grid/Monster/WaveDatabase 로드 비용 |
| `lobby_runner_start_begin/end` | `NetworkManager` 또는 MP bootstrap | Host/Client runner start 비용 |
| `room_all_players_ready` | lobby wait helper 또는 runtime marker | 모든 peer가 방에 들어온 시점 |
| `game_scene_load_requested` | load game command path | Host가 Game 씬 로드를 요청한 시점 |
| `game_scene_active` | scene loaded callback | `03_Game` active 시점 |
| `game_managers_ready` | `GameManagers` ready event | 핵심 GameManagers 준비 |
| `field_ready` | `FieldManager` path/grid ready 시점 | 필드 grid/path 준비 |
| `shop_data_ready` | `ShopManager` 또는 sync command | 첫 상점 데이터 준비 |
| `prepare_ui_first_paint` | `GamePrepareUIToolkitController` | 최소 HUD가 화면에 처음 바인딩된 시점 |
| `first_prepare_snapshot_ready` | automation snapshot | 하네스가 안정 snapshot을 읽은 시점 |

### 4.2 산출물

하네스 artifact에는 다음 파일을 추가하는 것이 좋다.

| 파일 | 내용 |
| --- | --- |
| `load-markers.json` | marker별 UTC, 이전 marker 대비 delta, boot 기준 delta |
| `load-summary.md` | 주요 구간별 전후 비교 표 |
| `addressables-load-summary.json` | key/label, count, duration, 실패 목록 |
| `scene-load-summary.json` | scene request, active, ready까지의 duration |
| `first-ui-paint.json` | Prepare HUD 최초 표시 시점과 필수 element ready 여부 |

## 5. 병목 후보

### 5.1 전체 Addressables preload

`AddressablesManager`에는 `autoPreloadAllOnStart=true`와 `PreloadAllAsync()`가 있다. 이 경로는 `CollectAllObjectLocations()`로 모은 모든 `Object` location을 한 번에 `Addressables.LoadAssetsAsync<Object>()`로 로드한다.

위험:

- Player boot 초기에 필요 없는 asset까지 로드할 수 있다.
- 사용자 입장에서는 Title 또는 Lobby에서 이미 지연이 발생할 수 있다.
- 메모리 사용량도 불필요하게 커질 수 있다.

개선 방향:

- `PreloadAll`을 기본 로드 경로에서 제거하거나 Development debug 옵션으로 내린다.
- label 기반 단계별 preload로 나눈다.
  - `boot_critical`
  - `lobby_critical`
  - `game_core`
  - `prepare_ui`
  - `round1_combat`
  - `late_combat`
- Game 진입 전에 꼭 필요한 asset만 먼저 로드한다.
- 전투 VFX, 후반 몬스터, 희귀 증강/스크롤 이미지는 첫 Prepare 이후 background warmup으로 미룬다.

### 5.2 Game prefab 로딩 시점

`AddressablesManager.LoadGamePrefabsAsync()`는 PlayerManager, Grid, 기본 Monster prefab, WaveDatabase를 병렬로 로드한다. 병렬 로드 자체는 맞지만, 이 호출이 Game scene 진입 후에 시작되면 첫 Prepare까지의 시간이 늘어난다.

개선 방향:

- Lobby에서 모든 player가 join되는 동안 `LoadGamePrefabsAsync()`를 미리 시작한다.
- Host가 Game 씬 load를 요청하기 전에 `GamePrefabsLoaded`를 확인하거나, 최소한 pending task를 공유한다.
- 이미 로딩 중이면 중복 task를 만들지 않고 기존 task를 await한다.

### 5.3 UI 리소스 로딩 시점

Game prepare UI는 `Resources/UI/GamePrepare`의 UXML/USS와 runtime `UIDocument` 생성을 사용한다. UI 최적화 후에도 첫 표시 시점에는 UXML clone, style 적용, element query, 카드 bind가 필요하다.

개선 방향:

- Lobby 후반 또는 Game scene load 직후에 UXML/USS를 미리 로드한다.
- 첫 화면에는 최소 HUD와 필수 버튼만 표시하고, shop card image는 placeholder 후 비동기 채움으로 전환한다.
- 상점/증강/몬스터 카드 이미지는 `UISpriteCache`의 pending load 공유를 사용하되, 첫 Prepare 전에는 round 1에서 필요한 key만 prefetch한다.

### 5.4 첫 Prepare에 너무 많은 작업 집중

현재 첫 Prepare 전후에는 다음 작업이 몰릴 수 있다.

- PlayerManager spawn과 durable playerId 확정.
- FieldManager grid/path 준비.
- Shop item sync와 augment sync.
- UI Toolkit root 생성과 card bind.
- Monster/wave data 접근.
- snapshot 안정화 대기.

개선 방향:

- Game scene active 직후와 first Prepare 진입 직후의 작업을 나눈다.
- 시각적으로 먼저 필요한 작업과 gameplay state에 필요한 작업을 분리한다.
- 플레이어가 즉시 조작해야 하는 최소 UI만 먼저 표시한다.
- 비필수 카드 이미지, 후반 wave data, VFX prewarm은 여러 frame으로 분산한다.

### 5.5 Network join 변동

기준선에서 느려진 구간은 `Client join -> Game load -> first Prepare` 쪽이다. 이 구간은 UI보다 Fusion runner, Photon session, player spawn, scene transition 변동이 더 크게 반영될 수 있다.

개선 방향:

- 같은 build, 같은 target, 같은 seed, 같은 headless 여부로 최소 5회 반복 측정한다.
- 평균뿐 아니라 p50, p90, max를 기록한다.
- 네트워크 join 구간과 Game scene 내부 로드 구간을 분리한다.
- 로드 시간 문서에는 단일 smoke 1회의 결과만으로 결론을 내리지 않는다.

## 6. 개선 Phase

### Phase 0. 계측 먼저 추가

목표:

로드 시간이 어디서 쓰이는지 분해한다.

작업:

- MP test mode에서만 동작하는 `LoadTimeMarker` helper를 추가한다.
- marker는 `[MPTEST] phase=load_marker code=<name> result=pass elapsedMs=<n>` 형태로 남긴다.
- automation artifact에 `load-markers.json`을 추가한다.
- 기존 `ui-optimization-baseline`의 timestamp와 새 marker 결과를 연결한다.

후보 파일:

- `Mdfproject/Assets/Scripts/Testing/MP/MPTestBootstrap.cs`
- `Mdfproject/Assets/Scripts/Testing/MP/MPTestLogger.cs`
- `Mdfproject/Assets/Scripts/Managers/AddressablesManager.cs`
- `Mdfproject/Assets/Scripts/Managers/GameManagers.cs`
- `Mdfproject/Assets/Scripts/Managers/FieldManager.cs`
- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`
- `tools/harness/mp/run_two_humanbot_two_ai_smoke.py`

완료 기준:

- 같은 smoke 실행에서 `load-markers.json`이 생성된다.
- `boot -> automation_ready -> lobby_ready -> game_scene_active -> first_prepare_snapshot` delta가 자동 계산된다.
- production build에는 marker가 포함되지 않는다.

### Phase 1. 전체 preload 제거 또는 단계화

목표:

초기 boot에서 필요 없는 Addressables 전체 로드를 제거한다.

작업:

- `AddressablesManager.autoPreloadAllOnStart` 기본값을 재검토한다.
- `PreloadAllAsync()`는 debug/manual warmup 용도로만 남긴다.
- label 기반 `PreloadByLabelAsync(label)`를 추가한다.
- `boot_critical`, `lobby_critical`, `game_core`, `prepare_ui`, `round1_combat` label 정책을 문서화한다.
- 현재 Addressables entry에 label이 없다면 먼저 audit 문서를 만든다.

주의:

- 모든 asset을 늦게 로드하면 첫 클릭 또는 첫 전투에서 hitch가 생길 수 있다.
- 로드 시간을 줄이되, 첫 전투 spike를 새로 만들면 안 된다.

완료 기준:

- `LOAD-001` 또는 `LOAD-004~007` 중 하나가 유의미하게 감소한다.
- 첫 전투 시작 시 VFX/monster prefab load failure가 없다.
- 메모리 사용량이 증가하지 않는다.

### Phase 2. Lobby 동안 Game core 선로딩

목표:

사용자가 로비에 있는 시간을 Game scene 준비 시간으로 활용한다.

작업:

- 모든 peer가 lobby에 들어오기 전후로 `LoadGamePrefabsAsync()`를 시작한다.
- Host와 Client 모두 같은 pending task를 공유한다.
- Host는 Game scene load 요청 전에 `GamePrefabsLoaded` 또는 `game_core_preload_in_progress` 상태를 marker로 남긴다.
- Game scene에서 같은 로드가 다시 호출되어도 즉시 cache hit 또는 pending task await로 끝나야 한다.

완료 기준:

- `game_prefabs_load_begin/end`가 Game scene active 이전으로 이동한다.
- `Host Game 씬 load 요청 성공 -> first Prepare snapshot` 시간이 줄어든다.
- lobby 대기 시간이 길어진 경우에도 전체 `boot -> first Prepare`는 줄어야 한다.

### Phase 3. First meaningful Prepare 화면 분리

목표:

첫 Prepare에 들어왔을 때 "전체 UI 완성"을 기다리지 않고, 사용자가 볼 수 있는 최소 화면을 먼저 표시한다.

작업:

- Prepare HUD 최소 구성:
  - player HP/gold/wall count
  - round/timer
  - option button
  - shop open/reroll button
- 비필수 구성:
  - shop card image
  - augment icon/image
  - monster/scroll card image
  - decorative style 또는 후반 battle card
- 최소 구성은 동기적으로 빠르게 bind한다.
- 비필수 이미지는 placeholder 후 `UISpriteCache` 비동기 결과로 채운다.
- `prepare_ui_first_paint`와 `prepare_ui_full_paint` marker를 분리한다.

완료 기준:

- `prepare_ui_first_paint`가 빨라진다.
- `prepare_ui_full_paint`가 늦어져도 사용자가 조작 가능한 버튼은 먼저 표시된다.
- screenshot에서 placeholder가 너무 오래 남지 않는다.

### Phase 4. Prewarm을 한 프레임에 몰지 않기

목표:

첫 Prepare 또는 첫 전투 직전에 instantiate/prewarm이 한 번에 몰리는 것을 줄인다.

작업:

- MonsterSpawner prewarm, VfxPoolManager prewarm, UI card image prefetch를 분리한다.
- round 1에 필요한 prefab만 우선 prewarm한다.
- 나머지는 `UniTask.Yield()` 또는 frame budget 기반으로 여러 frame에 나눠 실행한다.
- prewarm 완료 여부를 gameplay hard gate로 삼지 말고, 필요한 순간에 fallback load가 가능하게 둔다.

완료 기준:

- first Prepare와 first battle 전환에서 frame spike가 줄어든다.
- first battle에서 prefab missing 또는 VFX missing이 없다.
- prewarm이 끝나지 않았다는 이유로 state transition이 timeout되지 않는다.

### Phase 5. Scene load와 manager ready path 정리

목표:

`03_Game` scene active 이후 ready까지의 순서를 짧고 예측 가능하게 만든다.

작업:

- `GameManagers`, `PlayerManager`, `FieldManager`, `ShopManager`, `AugmentManager` ready 조건을 명시한다.
- ready 조건을 polling보다 event/task 기반으로 바꾼다.
- 같은 reference lookup을 여러 manager가 반복하지 않도록 scene-level registry를 검토한다.
- `FindObjectOfType` fallback은 migration/reconnect 또는 legacy scene 호환 경로로 제한한다.

완료 기준:

- `game_scene_active -> game_managers_ready` 시간이 줄어든다.
- `game_managers_ready -> first_prepare_snapshot_ready` 시간이 줄어든다.
- Host/Client snapshot comparison은 유지된다.

### Phase 6. 반복 실행 통계화

목표:

네트워크와 로컬 PC 변동 때문에 단일 실행 결과로 결론 내리지 않는다.

작업:

- 같은 build와 seed set으로 5회 이상 반복 실행한다.
- 결과는 p50, p90, max를 기록한다.
- headless load-only run과 non-headless visual run을 분리한다.
- screenshot 캡처가 필요한 검증과 순수 로드 시간 측정을 분리한다.

권장 매트릭스:

| 프로필 | 목적 | 실행 방식 |
| --- | --- | --- |
| load-headless-smoke | 순수 로드 시간 | `--headless-player`, screenshot 판단 제외 |
| load-visible-smoke | 실제 화면 로드 | 비헤드리스, screenshot 포함 |
| load-repeat-5 | 변동성 확인 | 같은 build에서 seed 5회 |
| load-cold-start | 첫 실행 비용 | Player/Addressables 캐시 영향 기록 |
| load-warm-start | 반복 실행 비용 | OS/Addressables cache가 있는 상태 기록 |

완료 기준:

- 개선 전후 p50과 p90이 함께 줄어든다.
- max만 좋아지고 p50이 나빠지는 결과는 성공으로 보지 않는다.
- cleanup은 모든 run에서 `PASS`, `orphanedPids=[]`이어야 한다.

## 7. 목표 수치

초기 목표는 보수적으로 잡는다.

| 지표 | 현재 참고값 | 1차 목표 | 비고 |
| --- | ---: | ---: | --- |
| Host ping | 11.09초 | 10.0초 이하 | Player boot/automation ready |
| Client ping | 14.93초 | 13.0초 이하 | 두 번째 프로세스 시작 비용 포함 |
| Game load 요청 -> first Prepare snapshot | 약 4.11초 | 3.0초 이하 | `27.56 -> 31.67` 기준 |
| boot -> first Prepare snapshot | 31.67초 | 27.0초 이하 | 네트워크 변동 포함 |
| boot -> result.json | 45.07초 | 40.0초 이하 | screenshot/cleanup 포함이라 보조 지표 |
| final log exception | 0건 | 0건 유지 | 속도보다 우선 |
| cleanup | PASS | PASS 유지 | E2E 반복 가능성 |

최종 목표는 5회 반복 측정 후 다시 조정한다.

## 8. 구현 우선순위

권장 순서:

1. `LoadTimeMarker`와 artifact 기록 추가.
2. 최적화 전후 5회 반복 기준선 생성.
3. `PreloadAllAsync()` 비용 측정.
4. `autoPreloadAllOnStart` 단계화 또는 비활성화 실험.
5. Lobby 중 `LoadGamePrefabsAsync()` 선로딩.
6. Prepare UI first paint와 full paint 분리.
7. prewarm frame budget 적용.
8. scene manager ready path 정리.
9. 5회 반복 재측정.
10. 결과를 `docs/ui-optimization-baseline.md` 또는 별도 `load-time-baseline.md`에 추가.

## 9. 코드 변경 후보

| 영역 | 후보 파일 | 작업 |
| --- | --- | --- |
| Addressables | `Mdfproject/Assets/Scripts/Managers/AddressablesManager.cs` | 전체 preload 단계화, marker 추가, label preload |
| Asset cache | `Mdfproject/Assets/Scripts/Managers/AssetLoader.cs` | pending task 공유 범위 확대, load count marker |
| Lobby/Game 전환 | `Mdfproject/Assets/Scripts/Network/NetworkManager.cs` | Game core preload 시작 시점 검토 |
| Game ready | `Mdfproject/Assets/Scripts/Managers/GameManagers.cs` | ready marker, event/task 기반 준비 |
| Field ready | `Mdfproject/Assets/Scripts/Managers/FieldManager.cs` | grid/path ready marker |
| Prepare UI | `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs` | first paint marker, full paint marker, 비필수 bind 지연 |
| Monster/VFX | `Mdfproject/Assets/Scripts/Game/Monsters/MonsterSpawner.cs`, `Mdfproject/Assets/Scripts/VFX/VfxPoolManager.cs` | prewarm 단계화와 frame budget |
| Harness | `tools/harness/mp/run_two_humanbot_two_ai_smoke.py` | load marker 수집, load summary 작성 |

## 10. 주의할 점

- timeout 값을 늘리는 것은 로드 시간 개선이 아니다.
- 화면을 늦게 그리면서 snapshot만 빨라지는 것도 사용자 체감 개선이 아니다.
- 모든 asset을 미리 로드하면 첫 클릭 hitch는 줄 수 있지만 boot 시간이 늘 수 있다.
- 모든 asset을 늦게 로드하면 boot는 빨라질 수 있지만 첫 전투나 첫 상점 열기에서 hitch가 생길 수 있다.
- Addressables handle release 정책 없이 cache만 늘리면 메모리 사용량이 커진다.
- Host와 Client의 로드 순서가 달라져 snapshot drift가 생기면 속도 개선보다 위험하다.
- Photon/Fusion state authority 규칙은 로드 최적화 때문에 바꾸면 안 된다.

## 11. 완료 판정

로드 시간 개선 작업은 아래 조건을 모두 만족해야 완료로 본다.

- Unity compile 통과.
- Unity console user error `[]`.
- 관련 EditMode 통과.
- smoke 또는 load 전용 E2E artifact 존재.
- `cleanupStatus=PASS`, `orphanedPids=[]`.
- `[MPTEST] phase=error` 없음.
- Host/Client snapshot comparison success.
- 최적화 전후 5회 반복 결과에서 p50과 p90이 개선됨.
- 시각 검증 run에서 첫 Prepare UI가 누락 없이 표시됨.
- 개선 결과가 문서에 수치와 artifact path로 기록됨.

## 12. 다음 작업 제안

가장 먼저 해야 할 작업은 실제 최적화가 아니라 계측이다.

1. `LoadTimeMarker` helper를 추가한다.
2. `AddressablesManager`, `GameManagers`, `FieldManager`, `GamePrepareUIToolkitController`에 marker를 심는다.
3. 하네스가 marker를 모아 `load-markers.json`과 `load-summary.md`를 쓰게 한다.
4. 같은 build로 5회 반복 기준선을 만든다.
5. 그 다음 `PreloadAllAsync()` 단계화와 Lobby 중 `LoadGamePrefabsAsync()` 선로딩을 실험한다.

이 순서로 진행해야 "로드 시간이 왜 줄었는지" 또는 "왜 줄지 않았는지"를 설명할 수 있다.

## 13. 현재 로드 시간 10회 측정 결과

측정일: 2026-06-16  
측정 목적: 현재 코드 기준 headless load smoke 10회 반복 평균 산출  
Unity 상태: `unity-cli --project Mdfproject status` ready, Unity `2021.3.45f1`, connector `0.3.18`  
컴파일: `unity-cli --project Mdfproject editor refresh --compile` 완료  
Console error: `unity-cli --project Mdfproject console --type error --stacktrace user` 결과 `[]`

### 13.1 측정 조건

| 항목 | 값 |
| --- | --- |
| Player build | `artifacts/builds/load-measure-20260616-120618-win64/MDF-MPTest.exe` |
| Build target | `StandaloneWindows64` |
| Build type | Development Build, Allow Debugging |
| Build timestamp | `2026-06-16T03:07:10.6622005Z` |
| Build total size | `440,912,755` bytes |
| Addressables build | `success=true`, `durationSeconds=9.39` |
| Artifact root | `artifacts/load-time-measurements/20260616-120618-headless-10` |
| Summary artifact | `artifacts/load-time-measurements/20260616-120618-headless-10/load-time-summary.json` |
| Loop artifact | `artifacts/load-time-measurements/20260616-120618-headless-10/loop-results.json` |
| 하네스 | `tools/harness/mp/run_two_humanbot_two_ai_smoke.py` |
| 실행 모드 | `--headless-player` |
| Seed | `26061101` |
| 반복 횟수 | 10회 |
| Cleanup 조건 | `--strict-cleanup --orphan-threshold 0` |

실행 명령:

```powershell
& "C:\Users\djthe\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" `
  tools\harness\mp\run_two_humanbot_two_ai_smoke.py `
  --player-path artifacts\builds\load-measure-20260616-120618-win64\MDF-MPTest.exe `
  --artifact-root artifacts\load-time-measurements\20260616-120618-headless-10 `
  --seed 26061101 `
  --bot-duration-seconds 30 `
  --bot-max-commands 1 `
  --bot-stop-at-round 1 `
  --headless-player `
  --strict-cleanup `
  --orphan-threshold 0
```

### 13.2 계산 기준

이번 측정은 아직 `LoadTimeMarker`가 구현되기 전이므로 기존 하네스 artifact timestamp를 사용했다.

| 계산값 | 기준 |
| --- | --- |
| Host ping | artifact directory 생성 시각 -> `host-ping.json.timestampUtc` |
| Client ping | artifact directory 생성 시각 -> `client-ping.json.timestampUtc` |
| Host runner 시작 | artifact directory 생성 시각 -> `host-start-latest.json.timestampUtc` |
| Client join | artifact directory 생성 시각 -> `client-start-latest.json.timestampUtc` |
| Game load 요청 성공 | artifact directory 생성 시각 -> `host-load-game.json.timestampUtc` |
| First Prepare snapshot | artifact directory 생성 시각 -> `snapshots/host-before-bot-latest.json.timestampUtc` |
| Game load -> First Prepare | `host-load-game.json.timestampUtc` -> `snapshots/host-before-bot-latest.json.timestampUtc` |
| Result 작성 | artifact directory 생성 시각 -> `result.json` last write time |

주의:

- 이 값은 Unity Profiler marker가 아니라 하네스 timestamp 기반이다.
- 스크린샷은 headless 조건이라 제외했다.
- 모든 run은 기존 smoke 시나리오 한계인 `move_unit.no_successful_command`로 `success=false`를 기록했다.
- 그러나 모든 run에서 `cleanupStatus=PASS`, `orphanedPids=[]`, `comparisonSuccess=true`였으므로 로드 시간 계산에는 포함했다.

### 13.3 10회 전체 평균

| 지표 | 평균 | 중앙값 | P90 | Min | Max |
| --- | ---: | ---: | ---: | ---: | ---: |
| Host ping | 2.13초 | 1.41초 | 1.50초 | 1.37초 | 8.43초 |
| Client ping | 2.89초 | 2.20초 | 2.25초 | 2.11초 | 9.18초 |
| Host runner 시작 | 7.09초 | 6.40초 | 6.46초 | 6.32초 | 13.39초 |
| Client join | 11.25초 | 10.56초 | 10.61초 | 10.48초 | 17.57초 |
| Game load 요청 성공 | 12.36초 | 11.66초 | 11.74초 | 11.58초 | 18.70초 |
| First Prepare snapshot | 16.50초 | 15.79초 | 15.91초 | 15.70초 | 22.83초 |
| Game load -> First Prepare | 4.14초 | 4.12초 | 4.18초 | 4.09초 | 4.18초 |
| After move snapshot | 23.22초 | 22.35초 | 24.22초 | 22.19초 | 29.27초 |
| Result 작성 | 27.10초 | 26.27초 | 28.07초 | 26.00초 | 32.93초 |

핵심 값:

- 현재 headless 기준 `boot -> First Prepare snapshot` 평균은 `16.50초`다.
- 현재 headless 기준 `Game load 요청 -> First Prepare snapshot` 평균은 `4.14초`다.
- 첫 번째 run이 cold-start 성격으로 가장 느렸고, 이후 2~10회는 거의 같은 값으로 안정화됐다.

### 13.4 Warm run 평균

첫 번째 run은 OS/Addressables/프로세스 cold-start 영향이 커서 별도로 본다. 2~10회 warm run 평균은 다음과 같다.

| 지표 | 평균 | 중앙값 | P90 | Min | Max |
| --- | ---: | ---: | ---: | ---: | ---: |
| Host ping | 1.42초 | 1.41초 | 1.50초 | 1.37초 | 1.50초 |
| Client ping | 2.19초 | 2.20초 | 2.25초 | 2.11초 | 2.25초 |
| Host runner 시작 | 6.40초 | 6.40초 | 6.46초 | 6.32초 | 6.46초 |
| Client join | 10.55초 | 10.56초 | 10.61초 | 10.48초 | 10.61초 |
| Game load 요청 성공 | 11.66초 | 11.66초 | 11.74초 | 11.58초 | 11.74초 |
| First Prepare snapshot | 15.79초 | 15.79초 | 15.91초 | 15.70초 | 15.91초 |
| Game load -> First Prepare | 4.13초 | 4.12초 | 4.18초 | 4.09초 | 4.18초 |
| After move snapshot | 22.55초 | 22.35초 | 24.22초 | 22.19초 | 24.22초 |
| Result 작성 | 26.45초 | 26.27초 | 28.07초 | 26.00초 | 28.07초 |

해석:

- 반복 실행이 안정화된 뒤에는 `boot -> First Prepare snapshot`이 약 `15.8초`로 수렴한다.
- `Game load -> First Prepare snapshot`은 cold/warm 차이가 거의 없고 약 `4.1초`로 고정된다.
- 따라서 현재 가장 큰 변동은 Game 내부 로드보다 Player boot와 초기 Host/Client 기동 쪽에 있다.

### 13.5 Run별 결과

| Run | Host ping | Client ping | Host start | Client join | Game load | First Prepare | Load -> Prepare | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 8.43초 | 9.18초 | 13.39초 | 17.57초 | 18.70초 | 22.83초 | 4.14초 | 32.93초 |
| 2 | 1.40초 | 2.11초 | 6.32초 | 10.48초 | 11.58초 | 15.70초 | 4.12초 | 26.07초 |
| 3 | 1.47초 | 2.23초 | 6.43초 | 10.57초 | 11.63초 | 15.77초 | 4.14초 | 26.00초 |
| 4 | 1.39초 | 2.20초 | 6.41초 | 10.57초 | 11.70초 | 15.88초 | 4.18초 | 26.43초 |
| 5 | 1.44초 | 2.20초 | 6.44초 | 10.59초 | 11.71초 | 15.80초 | 4.09초 | 26.22초 |
| 6 | 1.41초 | 2.17초 | 6.36초 | 10.49초 | 11.61초 | 15.72초 | 4.11초 | 28.07초 |
| 7 | 1.43초 | 2.22초 | 6.40초 | 10.56초 | 11.66초 | 15.78초 | 4.11초 | 26.25초 |
| 8 | 1.41초 | 2.17초 | 6.38초 | 10.54초 | 11.69초 | 15.80초 | 4.11초 | 26.27초 |
| 9 | 1.37초 | 2.14초 | 6.36초 | 10.50초 | 11.61초 | 15.79초 | 4.18초 | 26.43초 |
| 10 | 1.50초 | 2.25초 | 6.46초 | 10.61초 | 11.74초 | 15.91초 | 4.17초 | 26.33초 |

### 13.6 결론

현재 headless load smoke 기준 로드 시간은 다음 값으로 잡는다.

| 기준 | 값 |
| --- | ---: |
| 10회 전체 평균 `boot -> First Prepare snapshot` | 16.50초 |
| warm 9회 평균 `boot -> First Prepare snapshot` | 15.79초 |
| 10회 전체 평균 `Game load -> First Prepare snapshot` | 4.14초 |
| warm 9회 평균 `Game load -> First Prepare snapshot` | 4.13초 |
| 10회 전체 평균 `result.json` 작성까지 | 27.10초 |
| warm 9회 평균 `result.json` 작성까지 | 26.45초 |

우선 최적화 타깃은 두 갈래로 본다.

1. Boot/Host/Client 기동 구간:
   - cold run에서 크게 흔들린다.
   - Player boot, Addressables 초기화, automation server ready, 두 번째 client process 기동 비용을 분리 계측해야 한다.

2. Game load -> First Prepare 구간:
   - 약 4.1초로 안정적이다.
   - 개선하려면 Game scene active, GameManagers ready, Field ready, Prepare UI first paint marker가 필요하다.

다음 구현 단계에서는 `LoadTimeMarker`를 추가해 위 4.1초 내부를 더 잘게 나누는 것이 우선이다.

## 14. 최적화 코드 적용 후 10회 측정 결과

측정일: 2026-06-16  
측정 목적: `MDF UI 최적화 계획` 이후 로드 시간 개선 작업을 실제 Development player로 다시 10회 반복 측정  
최종 코드 기준: speculative lobby preload는 중간 실험에서 제거하고, 부팅 전체 preload 비활성화와 game core 로드 marker만 기본 경로에 남겼다.

### 14.1 적용한 코드 변경

| 영역 | 파일 | 변경 |
| --- | --- | --- |
| Addressables 전체 preload | `Mdfproject/Assets/Scripts/Managers/AddressablesManager.cs` | `autoPreloadAllOnStart` 기본값을 `false`로 변경 |
| Bootstrap | `Mdfproject/Assets/Scripts/Bootstrap/AppBootstrapper.cs` | `AutoPreloadAllOnStart`가 켜진 경우에만 `PreloadAllAsync()`를 await |
| Scene serialized 값 | `Assets/Scenes/00_Title.unity`, `Assets/Scenes/03_Game.unity` | `autoPreloadAllOnStart: 0`으로 변경 |
| Game core marker | `AddressablesManager.LoadGamePrefabsAsync()` | `game_prefabs_load_begin/end` MPTEST marker 추가 |
| Opt-in preload API | `AddressablesManager.BeginGamePrefabsPreload(reason)` | 추후 명시적 실험용 API 추가. 최종 기본 경로에는 연결하지 않음 |
| 회귀 방지 | `MPTestHarnessEditModeTests.LoadTimeOptimizationPreloadsGameCoreBeforeGameScene` | 부팅 전체 preload 비활성화와 marker 존재를 검증 |

중간 실험에서 `NetworkManager`의 lobby 진입 시점에 game core preload를 걸어 봤지만 전체 시간이 줄지 않았다. 특히 `MatchingLobby` 로드 완료 후 preload를 걸면 host/client 모두 `load_game` 이전에 game core가 준비되었지만, end-to-end 평균은 더 나빠졌다. 따라서 최종 코드에서는 해당 speculative preload 연결을 제거했다.

### 14.2 최종 측정 조건

| 항목 | 값 |
| --- | --- |
| Player build | `artifacts/builds/load-optimized-20260616-125141-win64/MDF-MPTest.exe` |
| Build metadata | `artifacts/builds/load-optimized-20260616-125141-win64/build-metadata.json` |
| Build target | `StandaloneWindows64` |
| Build type | Development Build, Allow Debugging |
| Build timestamp | `2026-06-16T03:52:07.6962668Z` |
| Build total size | `440,914,067` bytes |
| Addressables build | `success=true`, `durationSeconds=6.13` |
| Artifact root | `artifacts/load-time-measurements/20260616-125232-post-headless-10-final` |
| Summary artifact | `artifacts/load-time-measurements/20260616-125232-post-headless-10-final/load-time-summary.json` |
| Loop artifact | `artifacts/load-time-measurements/20260616-125232-post-headless-10-final/loop-results.json` |
| Harness | `tools/harness/mp/run_two_humanbot_two_ai_smoke.py` |
| 실행 모드 | `--headless-player` |
| Seed | `26061101` |
| 반복 횟수 | 10회 |
| Cleanup 조건 | `--strict-cleanup --orphan-threshold 0` |

검증:

- `unity-cli --project Mdfproject editor refresh --compile`: PASS
- `unity-cli --project Mdfproject console --type error --stacktrace user`: `[]`
- `unity-cli --project Mdfproject test --mode EditMode --filter MPTestHarnessEditModeTests.LoadTimeOptimizationPreloadsGameCoreBeforeGameScene`: 1/1 PASS
- `python tools/harness/precommit.py --all`: 0 errors, 기존 경고 5개
- 10회 모두 `cleanupStatus=PASS`
- 10회 모두 `orphanedPids=[]`
- 10회 모두 `comparisonSuccess=true`
- smoke 기능 결과는 기존과 동일하게 `move_unit.no_successful_command`로 `success=false`

### 14.3 최적화 후 10회 전체 평균

| 지표 | 평균 | 중앙값 | P90 | Min | Max |
| --- | ---: | ---: | ---: | ---: | ---: |
| Host ping | 3.16초 | 2.60초 | 2.88초 | 2.04초 | 9.19초 |
| Client ping | 3.93초 | 3.31초 | 3.65초 | 2.81초 | 10.00초 |
| Host runner 시작 | 8.27초 | 7.62초 | 8.00초 | 7.08초 | 14.49초 |
| Client join | 12.40초 | 11.75초 | 12.13초 | 11.22초 | 18.62초 |
| Game load 요청 성공 | 13.50초 | 12.85초 | 13.23초 | 12.31초 | 19.74초 |
| First Prepare snapshot | 17.62초 | 16.97초 | 17.33초 | 16.45초 | 23.90초 |
| Game load -> First Prepare | 4.12초 | 4.12초 | 4.14초 | 4.11초 | 4.16초 |
| After move snapshot | 24.34초 | 23.47초 | 25.69초 | 22.95초 | 30.47초 |
| Result 작성 | 29.84초 | 29.05초 | 31.33초 | 28.62초 | 35.60초 |
| Host game core load marker | 21.20ms | 21.00ms | 23.00ms | 18.00ms | 26.00ms |
| Client game core load marker | 38.50ms | 30.50ms | 75.00ms | 17.00ms | 77.00ms |

### 14.4 Warm run 평균

첫 번째 run은 cold-start 성격이 강하므로 2~10회 warm run 평균도 별도로 본다.

| 지표 | 최적화 전 warm 평균 | 최적화 후 warm 평균 | 차이 |
| --- | ---: | ---: | ---: |
| Host ping | 1.42초 | 2.49초 | +1.07초 |
| Client ping | 2.19초 | 3.25초 | +1.06초 |
| Host runner 시작 | 6.40초 | 7.57초 | +1.17초 |
| Client join | 10.55초 | 11.71초 | +1.16초 |
| Game load 요청 성공 | 11.66초 | 12.81초 | +1.15초 |
| First Prepare snapshot | 15.79초 | 16.93초 | +1.14초 |
| Game load -> First Prepare | 4.13초 | 4.12초 | -0.01초 |
| Result 작성 | 26.45초 | 29.21초 | +2.76초 |

해석:

- 최종 코드 기준 `Game load -> First Prepare`는 4.13초에서 4.12초로 사실상 동일하다.
- `game_prefabs_load_end` marker 기준 game core prefab 로드는 host 평균 21.2ms, client 평균 38.5ms 수준이다.
- 따라서 현재 4.1초 구간의 주 병목은 `LoadGamePrefabsAsync()`가 아니다.
- 전체 `boot -> First Prepare`는 이번 10회 측정에서 오히려 약 1.1초 느려졌다.
- 이 결과만 보면 이번 코드 변경을 “로드 시간 개선 성공”으로 판단하면 안 된다.

### 14.5 Run별 결과

| Run | Host ping | Client ping | Host start | Client join | Game load | First Prepare | Load -> Prepare | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 9.19초 | 10.00초 | 14.49초 | 18.62초 | 19.74초 | 23.90초 | 4.16초 | 35.60초 |
| 2 | 2.64초 | 3.36초 | 7.66초 | 11.78초 | 12.87초 | 16.98초 | 4.11초 | 29.19초 |
| 3 | 2.46초 | 3.11초 | 7.43초 | 11.55초 | 12.63초 | 16.74초 | 4.11초 | 29.28초 |
| 4 | 2.72초 | 3.40초 | 7.73초 | 11.88초 | 13.01초 | 17.12초 | 4.11초 | 28.77초 |
| 5 | 2.55초 | 3.29초 | 7.59초 | 11.72초 | 12.82초 | 16.94초 | 4.12초 | 31.33초 |
| 6 | 2.88초 | 3.65초 | 8.00초 | 12.13초 | 13.23초 | 17.33초 | 4.11초 | 29.73초 |
| 7 | 2.60초 | 3.31초 | 7.62초 | 11.75초 | 12.86초 | 16.97초 | 4.12초 | 28.68초 |
| 8 | 2.04초 | 2.81초 | 7.08초 | 11.22초 | 12.31초 | 16.45초 | 4.14초 | 28.62초 |
| 9 | 2.31초 | 3.05초 | 7.34초 | 11.51초 | 12.60초 | 16.71초 | 4.11초 | 29.02초 |
| 10 | 2.22초 | 2.90초 | 7.14초 | 11.39초 | 12.52초 | 16.85초 | 4.13초 | 30.24초 |

### 14.6 중간 실험 기록

코드 작업 중 lobby 선로딩도 실험했다.

| 실험 | Artifact root | 결과 |
| --- | --- | --- |
| Photon lobby ready 직후 game core preload | `artifacts/load-time-measurements/20260616-122725-post-headless-10` | game core는 약 650ms에 준비됐지만 Host/Client 시작 구간이 같이 느려짐 |
| MatchingLobby/JoinLobby scene loaded 이후 game core preload | `artifacts/load-time-measurements/20260616-124352-post-headless-10-final` | 10/10회 `load_game` 전에 준비됐지만 warm `First Prepare`가 16.83초로 기준선보다 느림 |

결론:

- game core prefab 선로딩은 현재 smoke 경로에서 실효 병목이 아니었다.
- 선로딩을 더 앞당기면 비용을 숨기지 못하고 네트워크 시작/로비 구간에 전가되는 경향이 있었다.
- 따라서 최종 코드에서는 lobby 선로딩 연결을 제거했다.

### 14.7 다음 최적화 방향

이번 결과 기준 다음 타깃은 `LoadGamePrefabsAsync()`가 아니라 `Game load -> First Prepare` 내부의 다른 준비 작업이다.

우선순위:

1. `game_scene_active`, `game_managers_ready`, `field_ready`, `prepare_ui_first_paint`, `first_prepare_snapshot_ready` marker를 추가한다.
2. `SetupPlayersAndGrids()`, `SetupGameUI()`, `StartNextRound()` 각각의 duration을 marker로 분리한다.
3. Prepare UI first paint와 full paint를 나누어, 사용자에게 먼저 보여야 하는 최소 HUD를 따로 측정한다.
4. marker 추가 후 다시 10회 측정하고 p50/p90 기준으로 병목을 확정한다.

현재 결론은 보수적으로 잡는다.

| 항목 | 판정 |
| --- | --- |
| 부팅 전체 preload 비활성화 | 구조적으로는 맞지만 이번 smoke에서는 속도 개선 증거 없음 |
| game core prefab marker | 병목이 아님을 확인하는 데 유효 |
| lobby game core preload | 측정상 비효율이라 최종 코드에서 제거 |
| 이번 최적화 성공 여부 | 로드 시간 개선 성공으로 보지 않음 |
