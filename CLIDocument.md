# Unity CLI Scene Transition Test

## Purpose

`unity-cli`를 사용해서 Unity Editor를 직접 제어하고, 시작 씬에서 `Game` 씬까지 실제 플레이 모드에서 전환되는지 확인했다.

요청에는 `Tile` 씬이라고 되어 있었지만 프로젝트의 `Assets/Scenes` 아래에는 `Tile.unity`가 없었다. 빌드 설정의 첫 enabled 씬이 `Assets/Scenes/00_Title.unity`였고, 코드에서도 `Title -> MatchingLobby -> JoinLobby -> Game` 흐름을 사용하고 있어 `00_Title`을 시작 씬으로 테스트했다.

## Environment

- Project: `D:\Unity\Mdf\Mdf\Mdfproject`
- Unity: `2021.3.45f1`
- unity-cli: `v0.3.15`
- Connector package: `com.youngwoocho02.unity-cli-connector`
- Connector state: `ready`, port `8090`

CLI 실행은 아래처럼 프로젝트 경로를 명시해서 다른 Unity 인스턴스와 섞이지 않게 했다.

```powershell
$UnityCli = "$env:LOCALAPPDATA\unity-cli\unity-cli.exe"
$Project = "D:\Unity\Mdf\Mdf\Mdfproject"

& $UnityCli --project $Project status
```

정상 연결 확인 결과:

```text
Unity (port 8090): ready
Project: D:/Unity/Mdf/Mdf/Mdfproject
Version: 2021.3.45f1
Connector: 0.3.15
```

## Scene Flow Checked

테스트한 전환 흐름은 다음과 같다.

```text
00_Title -> MatchingLobby -> JoinLobby -> Game
```

관련 코드 기준:

- `NextScenes.OnClick()`에서 로그인 후 `MatchingLobby`로 이동
- `NetworkManager.StartGame(GameMode.Host, roomName, "JoinLobby")`로 Host 방 생성 및 `JoinLobby` 로드
- `JoinLobbyUI`의 게임 시작 버튼 로직과 동일하게 `_runner.LoadScene(Game)` 호출

## Test Steps

### 1. Start From Title Scene

에디터가 플레이 모드가 아니면 `00_Title` 씬을 열었다.

```powershell
@'
if (UnityEditor.EditorApplication.isPlaying)
{
    UnityEditor.EditorApplication.isPlaying = false;
    return "STOPPING_PLAYMODE";
}

var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    "Assets/Scenes/00_Title.unity",
    UnityEditor.SceneManagement.OpenSceneMode.Single);

return "OPENED " + scene.path + " name=" + scene.name + " roots=" + scene.rootCount;
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
OPENED Assets/Scenes/00_Title.unity name=00_Title roots=9
```

### 2. Enter Play Mode

콘솔을 비운 뒤 플레이 모드에 진입했다.

```powershell
& $UnityCli --project $Project console --clear
& $UnityCli --project $Project editor play --wait
```

`editor play --wait` 호출은 전환 중 `connection closed before response` 메시지를 반환했지만, 이후 상태 조회로 실제 플레이 상태를 확인했다.

```powershell
@'
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "PLAY=" + UnityEditor.EditorApplication.isPlaying
    + " SCENE=" + scene.name
    + " PATH=" + scene.path
    + " ROOTS=" + scene.rootCount
    + " BOOT_READY=" + AppBootstrapper.IsBootReady
    + " BOOT_FAILED=" + AppBootstrapper.IsBootFailed
    + " NM=" + (NetworkManager.Instance != null)
    + " AM=" + (AddressablesManager.Instance != null);
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
PLAY=True SCENE=00_Title PATH=Assets/Scenes/00_Title.unity ROOTS=5 BOOT_READY=False BOOT_FAILED=False NM=True AM=True
```

### 3. Trigger Bootstrap Manually

`AppBootstrapper`는 코드상 씬 이름이 `SceneDefine.Title`일 때 자동 부팅하도록 되어 있다.

```csharp
if (scene.name != SceneDefine.Title)
{
    return;
}
```

그런데 실제 씬 이름은 `00_Title`이라 자동 훅이 걸리지 않았다. 그래서 CLI에서 직접 부트스트랩을 호출했다.

```powershell
@'
AppBootstrapper.EnsureExistsInScene();
return "BOOT_TRIGGERED ready=" + AppBootstrapper.IsBootReady
    + " failed=" + AppBootstrapper.IsBootFailed
    + " err=" + AppBootstrapper.LastBootError;
'@ | & $UnityCli --project $Project exec
```

잠시 대기 후 상태 확인:

```powershell
@'
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return "SCENE=" + scene.name
    + " BOOT_READY=" + AppBootstrapper.IsBootReady
    + " BOOT_FAILED=" + AppBootstrapper.IsBootFailed
    + " BOOT_ERR=" + AppBootstrapper.LastBootError
    + " NM_STATE=" + (NetworkManager.Instance == null ? "null" : NetworkManager.Instance.State.ToString())
    + " RUNNER=" + (NetworkManager.Instance != null && NetworkManager.Instance._runner != null);
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
SCENE=00_Title BOOT_READY=True BOOT_FAILED=False BOOT_ERR= NM_STATE=Disconnected RUNNER=False
```

### 4. Move From Title To MatchingLobby

Title 화면의 로그인 버튼이 실행하는 `NextScenes.OnClick()`을 CLI에서 직접 호출했다. 닉네임 입력 필드는 `CliRunner`로 세팅했다.

```powershell
@'
var inputs = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_InputField>(true);
foreach (var input in inputs)
{
    input.text = "CliRunner";
}

var next = UnityEngine.Object.FindObjectOfType<NextScenes>(true);
if (next == null)
{
    return "NEXT_SCENES_NOT_FOUND inputs=" + inputs.Length;
}

next.OnClick();
return "NEXT_CLICKED inputs=" + inputs.Length
    + " bootReady=" + AppBootstrapper.IsBootReady
    + " nmState=" + (NetworkManager.Instance == null ? "null" : NetworkManager.Instance.State.ToString());
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
NEXT_CLICKED inputs=2 bootReady=True nmState=Connecting
```

대기 후 현재 씬과 네트워크 상태를 확인했다.

```powershell
@'
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var nm = NetworkManager.Instance;
return "SCENE=" + scene.name
    + " PATH=" + scene.path
    + " NM_STATE=" + (nm == null ? "null" : nm.State.ToString())
    + " RUNNER=" + (nm != null && nm._runner != null)
    + " RUNNING=" + (nm != null && nm._runner != null && nm._runner.IsRunning)
    + " SESSIONS=" + (nm == null || nm._sessionList == null ? -1 : nm._sessionList.Count);
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
SCENE=MatchingLobby PATH=Assets/Scenes/MatchingLobby.unity NM_STATE=InLobby RUNNER=True RUNNING=False SESSIONS=0
```

### 5. Create Host Room And Move To JoinLobby

MatchingLobby에서 방 생성 버튼이 하는 것과 같은 호출을 직접 실행했다.

```powershell
@'
var nm = NetworkManager.Instance;
if (nm == null) return "NO_NETWORK_MANAGER";

nm.StartGame(Fusion.GameMode.Host, "CliRoom", SceneDefine.JoinLobby);
return "START_GAME_REQUESTED state=" + nm.State
    + " runner=" + (nm._runner != null)
    + " running=" + (nm._runner != null && nm._runner.IsRunning);
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
START_GAME_REQUESTED state=Connecting runner=True running=False
```

대기 후 상태 확인:

```powershell
@'
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var nm = NetworkManager.Instance;
return "SCENE=" + scene.name
    + " PATH=" + scene.path
    + " NM_STATE=" + (nm == null ? "null" : nm.State.ToString())
    + " RUNNER=" + (nm != null && nm._runner != null)
    + " RUNNING=" + (nm != null && nm._runner != null && nm._runner.IsRunning)
    + " IS_SERVER=" + (nm != null && nm._runner != null && nm._runner.IsServer)
    + " SESSION=" + (nm != null && nm._runner != null && nm._runner.SessionInfo != null ? nm._runner.SessionInfo.Name : "null")
    + " JUI=" + (UnityEngine.Object.FindObjectOfType<JoinLobbyUI>(true) != null);
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
SCENE=JoinLobby PATH=Assets/Scenes/JoinLobby.unity NM_STATE=InGame RUNNER=True RUNNING=True IS_SERVER=True SESSION=CliRoom JUI=True
```

### 6. Move From JoinLobby To Game

`JoinLobbyUI`의 게임 시작 버튼은 Host일 때 Fusion Runner로 `Assets/Scenes/Game.unity`를 로드한다. 같은 로직을 CLI에서 직접 실행했다.

```powershell
@'
var nm = NetworkManager.Instance;
if (nm == null || nm._runner == null) return "NO_ACTIVE_RUNNER";
if (!nm._runner.IsServer) return "NOT_SERVER";

int gameIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Game.unity");
if (gameIndex < 0) return "GAME_SCENE_NOT_IN_BUILD";

nm._runner.LoadScene(Fusion.SceneRef.FromIndex(gameIndex), UnityEngine.SceneManagement.LoadSceneMode.Single);
return "GAME_LOAD_REQUESTED index=" + gameIndex
    + " state=" + nm.State
    + " session=" + (nm._runner.SessionInfo != null ? nm._runner.SessionInfo.Name : "null");
'@ | & $UnityCli --project $Project exec
```

확인 결과:

```text
GAME_LOAD_REQUESTED index=4 state=InGame session=CliRoom
```

대기 후 최종 상태 확인:

```powershell
@'
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var nm = NetworkManager.Instance;
var gm = GameManagers.Instance;
return "FINAL scene=" + scene.name
    + " path=" + scene.path
    + " play=" + UnityEditor.EditorApplication.isPlaying
    + " nmState=" + (nm == null ? "null" : nm.State.ToString())
    + " runnerRunning=" + (nm != null && nm._runner != null && nm._runner.IsRunning)
    + " isServer=" + (nm != null && nm._runner != null && nm._runner.IsServer)
    + " gameManagers=" + (gm != null);
'@ | & $UnityCli --project $Project exec
```

최종 확인 결과:

```text
FINAL scene=Game path=Assets/Scenes/Game.unity play=True nmState=InGame runnerRunning=True isServer=True gameManagers=True
```

## Screenshot

Game 씬 진입 후 Game View 스크린샷을 요청했다.

```powershell
& $UnityCli --project $Project screenshot --view game --width 1280 --height 720 --output-path Screenshots/cli_title_to_game.png
```

실제 저장 결과는 커넥터 응답 기준 아래 파일이었다.

```text
D:\Unity\Mdf\Mdf\Mdfproject\Screenshots\screenshot.png
```

## Console Findings

씬 전환과 Game 씬 진입을 막는 에러는 확인되지 않았다.

반복적으로 남은 경고는 기존 Addressables 로딩 경로의 missing script 메시지다.

```text
The referenced script (Unknown) on this Behaviour is missing!
AddressablesManager:CollectAllObjectLocations () (at Assets/Scripts/Managers/AddressablesManager.cs:214)
AddressablesManager/<PreloadAllAsync>d__38:MoveNext () (at Assets/Scripts/Managers/AddressablesManager.cs:93)
```

Fusion 연결 로그도 warning 타입으로 출력되지만, 실제 상태는 `InGame`, `runnerRunning=True`, `isServer=True`로 정상 진행됐다.

## Result

CLI 테스트 결과 `00_Title`에서 시작해 `MatchingLobby`, `JoinLobby`를 거쳐 `Game` 씬까지 실제 플레이 모드에서 도달했다.

최종 판정 기준:

- Active Scene: `Assets/Scenes/Game.unity`
- Play Mode: `True`
- `NetworkManager.State`: `InGame`
- Fusion Runner: running
- Host 여부: `True`
- `GameManagers.Instance`: exists

