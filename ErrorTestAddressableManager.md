# AddressablesManager 오류 점검 문서

작성일: 2026-05-08  
대상 프로젝트: `D:\Unity\Mdf\Mdf\Mdfproject`  
주요 코드:
- `Assets/Scripts/Managers/AddressablesManager.cs`
- `Assets/Scripts/Managers/GameManagers.cs`
- `Assets/Scripts/Bootstrap/AppBootstrapper.cs`
- `Assets/Scripts/Managers/PlayerManager.cs`

---

## 1. 발생한 오류

로그:

```text
Attempting to load AssetReference that has already been loaded.
Handle is exposed through getter OperationHandle
UnityEngine.AddressableAssets.AssetReference:LoadAssetAsync<UnityEngine.GameObject> ()
AddressablesManager/<LoadPrefabAsync>d__40:MoveNext () (at Assets/Scripts/Managers/AddressablesManager.cs:168)
AddressablesManager/<LoadGamePrefabsAsync>d__39:MoveNext () (at Assets/Scripts/Managers/AddressablesManager.cs:140)
```

비슷한 로그가 `Grid`, `Monster`, `WaveDatabase`에서도 반복된다.

마지막에는 아래 오류도 같이 발생했다.

```text
[AddressablesManager] 게임 프리팹 로딩 실패: Attempting to use an invalid operation handle
```

이 오류는 단순히 보기 싫은 warning 수준이 아니다. `LoadGamePrefabsAsync()`가 catch 블록으로 빠졌다는 뜻이고, 이후 `GameManagers.SetupPlayersAndGrids()`에서 필요한 `GridPrefab`, `PlayerManagerPrefab`, `DefaultMonsterPrefab`, `WaveDatabase`가 null 또는 불완전한 상태일 수 있다.

---

## 2. 문제가 나는 위치

### 2-1. `LoadGamePrefabsAsync()`

위치:
- `Assets/Scripts/Managers/AddressablesManager.cs:128`

현재 구조:

```csharp
public async UniTask LoadGamePrefabsAsync()
{
    if (GamePrefabsLoaded) return;

    var loadTasks = new List<UniTask>();
    loadTasks.Add(LoadPrefabAsync(playerManagerPrefabRef, ...));
    loadTasks.Add(LoadPrefabAsync(gridPrefabRef, ...));
    loadTasks.Add(LoadPrefabAsync(defaultMonsterPrefabRef, ...));
    loadTasks.Add(LoadWaveDatabaseAsync());

    await UniTask.WhenAll(loadTasks);
    GamePrefabsLoaded = true;
}
```

문제는 `GamePrefabsLoaded`가 모든 로드가 끝난 뒤에만 true가 된다는 점이다. 로딩 중인 상태를 나타내는 별도 guard가 없다.

즉 첫 번째 `LoadGamePrefabsAsync()`가 아직 끝나기 전에 두 번째 `LoadGamePrefabsAsync()`가 들어오면 둘 다 `GamePrefabsLoaded == false`로 판단하고 같은 `AssetReference`를 다시 로드한다.

### 2-2. `LoadPrefabAsync()`

위치:
- `Assets/Scripts/Managers/AddressablesManager.cs:166`
- `Assets/Scripts/Managers/AddressablesManager.cs:168`

현재 구조:

```csharp
private async UniTask LoadPrefabAsync(AssetReference assetRef, Action<GameObject> onLoaded, string prefabName)
{
    var handle = assetRef.LoadAssetAsync<GameObject>();
    await handle.Task;
    ...
}
```

`AssetReference`는 이미 로드된 상태에서 다시 `LoadAssetAsync<T>()`를 호출하면 안 된다. Unity Addressables는 이미 로드된 `AssetReference`의 handle을 `assetRef.OperationHandle`로 노출한다. 그래서 같은 `AssetReference`에 대해 중복 `LoadAssetAsync()`가 들어오면 아래 오류가 난다.

```text
Attempting to load AssetReference that has already been loaded.
Handle is exposed through getter OperationHandle
```

### 2-3. `LoadWaveDatabaseAsync()`

위치:
- `Assets/Scripts/Managers/AddressablesManager.cs:182`
- `Assets/Scripts/Managers/AddressablesManager.cs:184`

`WaveDatabase`도 같은 구조다.

```csharp
var handle = waveDatabaseRef.LoadAssetAsync<WaveDatabase>();
```

이미 로드된 `waveDatabaseRef`에 대해 다시 `LoadAssetAsync<WaveDatabase>()`를 호출하면 같은 문제가 발생한다.

---

## 3. 왜 HostMigration 중에 더 잘 발생하는가

호출 흐름:

```text
GameManagers.Spawned()
-> InitializeAndStartGame()
-> GameFlow()
-> AddressablesManager.LoadGamePrefabsAsync()
```

관련 위치:
- `Assets/Scripts/Managers/GameManagers.cs:191`
- `Assets/Scripts/Managers/GameManagers.cs:261`
- `Assets/Scripts/Managers/GameManagers.cs:599`
- `Assets/Scripts/Managers/GameManagers.cs:613`
- `Assets/Scripts/Managers/GameManagers.cs:901`
- `Assets/Scripts/Managers/GameManagers.cs:920`

HostMigration 중에는 old Runner와 new Runner의 `GameManagers`가 짧은 시간 동안 겹칠 수 있다. 또한 snapshot 복원으로 `GameManagers.Spawned()`가 다시 호출될 수 있다.

현재 `GameManagers.Spawned()`에는 HostMigration이면 `GameFlow`를 다시 시작하지 않는 guard가 있다.

위치:
- `Assets/Scripts/Managers/GameManagers.cs:247`

하지만 실제 로그에서는 `GameManagers.Spawned()` 이후 `InitializeAndStartGame()`과 `GameFlow()`가 실행되었다. 이는 다음 중 하나일 가능성이 높다.

1. HostMigration 플래그가 true가 되기 전 또는 false가 된 뒤 새 `GameManagers.Spawned()`가 실행되었다.
2. 일반 게임 시작 경로와 HostMigration 복원 경로가 겹쳤다.
3. `AddressablesManager.LoadGamePrefabsAsync()`가 이미 실행 중인데 다른 `GameManagers` 인스턴스가 다시 호출했다.
4. Start 시점의 `PreloadAllAsync()`와 게임 프리팹 로드가 시간상 겹쳤다.

핵심은 `AddressablesManager`가 "이미 로드 중" 또는 "이미 로드됨" 상태를 안전하게 처리하지 못한다는 점이다. HostMigration은 이 재진입 문제를 더 잘 드러나게 만든다.

---

## 4. 현재 코드의 구조적 문제

## 문제 1. `LoadGamePrefabsAsync()`가 재진입에 안전하지 않다

현재 guard:

```csharp
if (GamePrefabsLoaded) return;
```

이 조건은 "이미 완료됨"만 막는다. "현재 로딩 중"은 막지 못한다.

필요한 guard:

```csharp
if (_gamePrefabsLoading)
{
    await UniTask.WaitUntil(() => !_gamePrefabsLoading);
    return;
}
```

또는 `_gamePrefabsLoadTask`를 저장하고 같은 task를 await하는 방식이 더 낫다.

## 문제 2. `AssetReference.Asset` 또는 `OperationHandle`을 재사용하지 않는다

Addressables의 `AssetReference`는 이미 로드된 경우 `Asset`과 `OperationHandle`을 통해 기존 결과를 확인할 수 있다. 그런데 현재 코드는 매번 `LoadAssetAsync<T>()`만 호출한다.

결과:
- 이미 로드된 `AssetReference`에 다시 load 요청
- Addressables가 중복 로드 예외 발생
- catch에서 `LoadGamePrefabsAsync()` 실패 로그
- `GamePrefabsLoaded`는 true가 되지 않음
- 다음 호출에서 다시 같은 오류 반복

## 문제 3. 실패 후 상태 복구가 없다

`LoadGamePrefabsAsync()` catch 블록은 로그만 남긴다.

```csharp
catch (System.Exception ex)
{
    Debug.LogError($"[AddressablesManager] 게임 프리팹 로딩 실패: {ex.Message}");
}
```

이후 `GamePrefabsLoaded`는 false로 남는다. 그래서 다음 호출도 다시 로드를 시도하고, 같은 invalid handle 문제가 반복될 수 있다.

## 문제 4. `PreloadAllAsync()`와 게임용 prefab cache가 분리되어 있다

`PreloadAllAsync()`는 모든 Addressables Object를 미리 로드하지만, 그 결과를 `_playerManagerPrefab`, `_gridPrefab`, `_defaultMonsterPrefab`, `_waveDatabase`에 직접 연결하지 않는다.

즉 preload가 성공해도 `LoadGamePrefabsAsync()`는 다시 각 `AssetReference.LoadAssetAsync()`를 호출한다. preload와 game prefab cache가 서로 독립적으로 움직이기 때문에 중복 handle 문제가 더 쉽게 생긴다.

---

## 5. 이 오류를 무시하면 안 되는 이유

무시하면 안 된다.

이 오류가 발생하면 아래 문제가 이어질 수 있다.

1. `GamePrefabsLoaded`가 true가 되지 않는다.
2. `AddressablesManager.PlayerManagerPrefab` 또는 `GridPrefab`이 null일 수 있다.
3. `GameManagers.SetupPlayersAndGrids()`에서 플레이어/그리드 spawn을 건너뛸 수 있다.
4. HostMigration 복원 게이트에서 `playersReady=false`, `fieldManager=null`, `wallMapNotReady`, `unitMapNotReady`가 발생할 수 있다.
5. 결과적으로 `WaitAndRestoreGameManagers` 또는 `WaitForRestoreDependenciesAndResumeFlow` timeout으로 이어질 수 있다.

즉 이 문제는 `ErrorTestHostMigration.md`의 오류 1번 자체는 아니지만, 오류 3번과 오류 4번으로 이어질 수 있는 선행 문제다.

---

## 6. 해결 방법

## 해결 1. `LoadGamePrefabsAsync()`에 로딩 중 guard 추가

목표:
- 동시에 여러 번 호출되어도 실제 로드는 한 번만 실행
- 이미 로딩 중이면 기존 로드가 끝날 때까지 기다림
- 로드가 끝난 뒤 cache가 채워졌는지 확인

예시 방향:

```csharp
private bool _gamePrefabsLoading;

public async UniTask LoadGamePrefabsAsync()
{
    if (GamePrefabsLoaded)
    {
        return;
    }

    if (_gamePrefabsLoading)
    {
        await UniTask.WaitUntil(() => !_gamePrefabsLoading);
        return;
    }

    _gamePrefabsLoading = true;
    try
    {
        ...
        GamePrefabsLoaded = true;
    }
    finally
    {
        _gamePrefabsLoading = false;
    }
}
```

왜 이 방법을 선택하는가:
- HostMigration, scene reload, duplicate `GameManagers.Spawned()`가 발생해도 Addressables 로드는 한 번만 진행된다.
- 호출자는 기존처럼 `await LoadGamePrefabsAsync()`만 사용하면 된다.
- `GameManagers` 쪽에 임시 guard를 추가하는 것보다 원인 위치인 `AddressablesManager`에서 해결하는 것이 맞다.

## 해결 2. 이미 로드된 `AssetReference`는 기존 결과를 재사용

목표:
- `AssetReference.Asset`이 이미 있으면 `LoadAssetAsync()`를 다시 호출하지 않는다.
- `OperationHandle`이 valid하고 완료된 상태면 `Result`를 재사용한다.
- 아직 진행 중인 handle이면 해당 handle을 기다린다.

예시 방향:

```csharp
if (assetRef.Asset is GameObject cachedPrefab)
{
    onLoaded?.Invoke(cachedPrefab);
    return;
}

if (assetRef.OperationHandle.IsValid())
{
    await assetRef.OperationHandle.Task;
    onLoaded?.Invoke(assetRef.OperationHandle.Result as GameObject);
    return;
}

var handle = assetRef.LoadAssetAsync<GameObject>();
await handle.Task;
```

왜 이 방법을 선택하는가:
- Unity Addressables가 알려준 원인이 바로 "이미 로드된 handle은 OperationHandle getter로 접근하라"이기 때문이다.
- 같은 `AssetReference`를 반복 로드하지 않으면 현재 오류 메시지가 사라진다.
- prefab cache가 이미 채워진 상태에서도 안전하게 재호출할 수 있다.

## 해결 3. cache 상태 기준으로 개별 로드를 건너뛰기

현재는 `GamePrefabsLoaded` 하나만 본다. 더 안전하게 하려면 개별 cache도 확인해야 한다.

예시:

```csharp
if (_playerManagerPrefab == null)
{
    await LoadPrefabAsync(playerManagerPrefabRef, prefab => _playerManagerPrefab = prefab, "PlayerManager");
}

if (_gridPrefab == null)
{
    await LoadPrefabAsync(gridPrefabRef, prefab => _gridPrefab = prefab, "Grid");
}
```

왜 이 방법을 선택하는가:
- 일부 asset만 로드되고 일부가 실패한 경우에도 전체를 다시 중복 로드하지 않아도 된다.
- 실패한 asset만 다시 시도할 수 있다.
- `WaveDatabase`도 같은 방식으로 `_waveDatabase == null`일 때만 로드하면 된다.

## 해결 4. 성공 판정을 실제 cache 기준으로 한다

현재는 `UniTask.WhenAll(loadTasks)`가 끝나면 바로 `GamePrefabsLoaded = true`다. 하지만 task가 내부에서 실패 로그만 남기고 Result가 null일 수 있다.

추천 판정:

```csharp
GamePrefabsLoaded =
    _playerManagerPrefab != null &&
    _gridPrefab != null &&
    _defaultMonsterPrefab != null &&
    _waveDatabase != null;
```

왜 이 방법을 선택하는가:
- `GameManagers.SetupPlayersAndGrids()`가 실제로 필요한 것은 bool이 아니라 prefab 참조다.
- bool이 true인데 prefab이 null인 상태가 되면 더 찾기 어려운 오류가 된다.

---

## 7. 권장 수정 순서

1. `AddressablesManager`에 `_gamePrefabsLoading` 또는 `_gamePrefabsLoadTask` guard를 추가한다.
2. `LoadPrefabAsync()`에서 `assetRef.Asset`과 `assetRef.OperationHandle`을 먼저 재사용한다.
3. `LoadWaveDatabaseAsync()`도 같은 방식으로 수정한다.
4. `LoadGamePrefabsAsync()`에서 이미 cache가 있는 항목은 다시 로드하지 않는다.
5. `GamePrefabsLoaded`는 실제 cache가 모두 채워졌을 때만 true로 둔다.
6. `dotnet build .\Assembly-CSharp.csproj --no-restore`로 컴파일 확인한다.
7. Unity Editor 또는 빌드 2개로 HostMigration 재현 테스트를 한다.

---

## 8. 테스트 체크리스트

### 일반 시작 테스트

1. Title에서 게임 세션 시작
2. `LoadGamePrefabsAsync()`가 한 번만 실행되는지 확인
3. `PlayerManagerPrefab`, `GridPrefab`, `DefaultMonsterPrefab`, `WaveDatabase`가 null이 아닌지 확인
4. 플레이어/그리드 spawn이 정상인지 확인

### 중복 호출 테스트

1. `LoadGamePrefabsAsync()`를 짧은 시간 안에 두 번 호출
2. 두 번째 호출이 새 `LoadAssetAsync()`를 만들지 않고 기존 로드를 기다리는지 확인
3. `Attempting to load AssetReference that has already been loaded` 로그가 사라지는지 확인

### HostMigration 테스트

1. Host + Client 세션 시작
2. Prepare 상태에서 Host 종료
3. 새 Host 복원 후 Addressables 중복 로드 오류가 없는지 확인
4. Battle 상태에서 Host 종료
5. `GameManagers` 복원 게이트가 Addressables 문제로 timeout 나지 않는지 확인

성공 기준:
- `Attempting to load AssetReference that has already been loaded` 로그 없음
- `Attempting to use an invalid operation handle` 로그 없음
- `GamePrefabsLoaded == true`
- `GridPrefab != null`
- `PlayerManagerPrefab != null`
- `DefaultMonsterPrefab != null`
- `WaveDatabase != null`

---

## 9. 최종 판단

현재 오류는 무시하면 안 된다. HostMigration 자체의 snapshot spawn 오류는 아니지만, HostMigration 복원 중 `GameManagers`가 다시 초기화되거나 일반 시작 경로가 겹칠 때 Addressables 로드가 재진입되면서 발생한다.

가장 정확한 해결은 `GameManagers`에서 임시로 호출을 막는 것이 아니라 `AddressablesManager.LoadGamePrefabsAsync()`를 재진입 안전한 함수로 만드는 것이다. 그래야 일반 시작, scene reload, HostMigration, 재접속 복원 모두에서 같은 asset을 안전하게 재사용할 수 있다.

---

## 10. 간단한 설명

이 오류는 이미 로드된 Addressables `AssetReference`를 다시 `LoadAssetAsync()`로 로드하려 해서 발생한다. HostMigration 중에는 `GameManagers.Spawned()`와 `GameFlow()`가 다시 들어올 수 있어 이 문제가 더 쉽게 드러난다. 해결은 `AddressablesManager`가 이미 로드 중이면 기다리고, 이미 로드된 asset이면 기존 `Asset` 또는 `OperationHandle`을 재사용하도록 바꾸는 것이다.
