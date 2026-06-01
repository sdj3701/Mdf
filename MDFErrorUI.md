# MDF APK UI 오류 수정 가이드

## 증상

APK 빌드에서 `03_Game` 준비 단계 UI가 에디터/Game View와 다르게 보인다.

- 증강 선택 카드에서 이름, 설명, 선택 버튼 텍스트가 보이지 않는다.
- 상점/캐릭터 선택 카드가 상단 HUD와 겹친다.
- 캐릭터 초상화가 카드 프레임의 의도된 위치와 크기에 맞지 않는다.
- 카드 배치가 에디터에서 맞춘 USS 값과 APK 런타임 값이 다르게 적용된다.

## 관련 파일

- `Mdfproject/Assets/Scripts/UI/Game/GamePrepareUIToolkitController.cs`
- `Mdfproject/Assets/Resources/UI/GamePrepare/GamePreparePanels.uxml`
- `Mdfproject/Assets/Resources/UI/GamePrepare/GamePreparePanelsStyles.uss`
- `Mdfproject/Assets/Resources/UI/GamePrepare/GamePrepareRuntimeTheme.tss`
- `Mdfproject/Assets/Resources/Fonts & Materials/NotoSansKR-VariableFont_wght_UITK.asset`
- `Mdfproject/Assets/Resource/Image/UI/SlotUI/Spr_Slot*.png`
- 참고 비교 파일:
  - `Mdfproject/Assets/Scripts/UI/ReLogin/ReLoginUIToolkitController.cs`
  - `Mdfproject/Assets/Scripts/UI/TestMatching/TestMatchingUIToolkitController.cs`
  - `Mdfproject/Assets/Scripts/Network/JoinLobbyUI.cs`

## 왜 Game 씬만 에디터와 빌드 배치가 달라지는가

다른 UI 씬과 `03_Game` 씬은 UI 배치 방식이 다르다.

### 다른 씬의 방식

`ReLogin`, `TestMatching`, `JoinLobby` UI는 고정 크기의 디자인 공간을 하나 만들고, 화면 크기가 바뀌면 그 디자인 공간 전체만 같은 비율로 스케일한다.

대표 코드 패턴:

```csharp
private const float DesignWidth = 1672f;
private const float DesignHeight = 941f;

float scale = Mathf.Min(rootWidth / DesignWidth, rootHeight / DesignHeight);
float left = (rootWidth - DesignWidth * scale) * 0.5f;
float top = (rootHeight - DesignHeight * scale) * 0.5f;

designSpace.style.left = left;
designSpace.style.top = top;
designSpace.transform.scale = new Vector3(scale, scale, 1f);
```

USS에도 `*-design-space`가 있다.

```css
.tm-design-space,
.jl-design-space,
.relogin-design-space {
    position: absolute;
    left: 0;
    top: 0;
    width: 1672px;
    height: 941px;
    transform-origin: left top;
}
```

이 방식은 에디터와 빌드의 실제 해상도가 달라도 내부 좌표계는 항상 `1672x941`이다. 그래서 버튼과 패널의 상대 위치가 달라지지 않고, 전체 UI만 균일하게 커지거나 작아진다.

### Game 씬의 방식

`03_Game` 준비 UI는 `GamePrepareUIToolkitController`가 런타임에 `UIDocument`와 `PanelSettings`를 만들 수 있고, 패널 설정도 코드에서 만든다.

```csharp
settings.scaleMode = PanelScaleMode.ConstantPixelSize;
settings.referenceResolution = new Vector2Int((int)ReferenceWidth, (int)ReferenceHeight);
```

또 `ApplyResponsiveSize()`가 화면 픽셀 크기와 safe area를 읽어서 카드, HUD, 라운드 타이머, 새로고침 버튼의 위치와 크기를 직접 다시 넣는다.

```csharp
var uiWidth = root != null && root.resolvedStyle.width > 1f ? root.resolvedStyle.width : Screen.width;
var uiHeight = root != null && root.resolvedStyle.height > 1f ? root.resolvedStyle.height : Screen.height;
var uiSize = new Vector2(uiWidth, uiHeight);

shopPanel.style.paddingTop = CalculateShopTopPadding(uiSize);
roundTimerLabel.style.left = Mathf.Max(0f, (uiWidth - RoundTimerWidth) * 0.5f);
```

즉 Game 씬은 고정 디자인 공간을 통째로 스케일하지 않고, APK의 실제 `Screen.width`, `Screen.height`, `Screen.safeArea`, `root.resolvedStyle` 값에 따라 각 요소를 따로 계산한다. 에디터 Game View와 Android 빌드의 해상도, safe area, 패널 초기화 타이밍이 다르면 배치도 달라진다.

### 결론

다른 씬은 "고정 좌표계 + 전체 스케일"이라 안정적이고, Game 씬은 "실제 화면 픽셀 + 개별 재배치"라 APK에서 차이가 난다. Game 씬도 다른 씬처럼 고정 디자인 공간 패턴으로 맞추는 것이 가장 안정적인 수정 방향이다.

## 원인

### 1. USS 배치값을 C# 런타임 코드가 덮어쓴다

`GamePreparePanelsStyles.uss`에는 `.shop-panel`의 `padding-top`이 크게 잡혀 있어도, 실제 게임에서는 `GamePrepareUIToolkitController.ApplyResponsiveSize()`가 다시 값을 넣는다.

현재 코드에서 상점 카드 위치는 다음 값으로 결정된다.

```csharp
private const float ShopRowTopRatio = 0.02f;
private const float ShopPanelTopMin = 10f;
private const float ShopPanelTopMax = 24f;
```

이 값은 APK 해상도에서 상점 카드 줄을 거의 화면 최상단으로 붙인다. 그래서 라운드 타이머, 옵션 버튼, 플레이어 HUD와 카드가 겹친다.

### 2. 상점 카드 크기가 슬롯 이미지 비율과 다르게 계산된다

슬롯 배경 이미지는 `200x250` 비율이다. 현재 상점 카드 계산은 `188x246` 기준에 `ShopUiScaleBoost = 3f`를 적용한 뒤 화면 폭에 맞춰 다시 줄인다.

이 방식은 5장을 한 줄에 맞추긴 하지만, 카드 높이와 배경 프레임 비율이 어긋나기 쉽다. 캐릭터 초상화도 고정 `166px`이라 카드가 커질수록 프레임 안에서 작거나 어긋나 보인다.

### 3. GamePrepare UI만 한글 UITK 폰트를 직접 지정하지 않는다

다른 UI Toolkit 화면은 USS에서 다음 폰트를 지정한다.

```css
-unity-font-definition: url("/Assets/Resources/Fonts & Materials/NotoSansKR-VariableFont_wght_UITK.asset");
```

하지만 `GamePreparePanelsStyles.uss`에는 이 지정이 없다. APK에서는 기본 폰트/폴백이 에디터와 다를 수 있어서 증강 이름, 설명, `선택`, `상점`, `벽` 같은 한글 텍스트가 안 보일 수 있다. 상점 영문 이름과 숫자만 보이는 증상은 이 가능성이 높다.

### 4. Safe Area와 실제 패널 크기 갱신 타이밍이 민감하다

Android에서는 첫 프레임 이후 실제 패널 크기, 노치 safe area, 해상도 값이 바뀌는 경우가 있다. Game 씬은 이 값들을 직접 배치 계산에 사용하므로, 에디터와 APK의 차이가 UI 위치 차이로 바로 드러난다.

## 수정 방법

### 0. Game 씬도 design-space 패턴으로 통일한다

가장 권장하는 수정은 GamePrepare UI도 다른 씬처럼 `game-prepare-design-space`를 두고, 그 안의 좌표는 고정한 뒤 전체만 스케일하는 방식으로 바꾸는 것이다.

#### UXML 구조

`GamePreparePanels.uxml`에서 `game-prepare-safe-root` 아래에 디자인 공간을 추가하고, 기존 HUD/상점/증강 요소를 그 안으로 옮긴다.

```xml
<ui:VisualElement name="game-prepare-safe-root" class="game-prepare-safe-root">
    <ui:VisualElement name="game-prepare-design-space" class="game-prepare-design-space">
        <!-- augment-panel, shop-panel, resource-root, timer, HUD buttons -->
    </ui:VisualElement>
</ui:VisualElement>
```

#### USS

`GamePreparePanelsStyles.uss`에 고정 디자인 공간을 추가한다. Game 씬은 현재 코드 기준 `1600x900`을 쓰는 것이 자연스럽다. 기존 메뉴 UI와 완전히 같은 기준으로 맞추고 싶으면 `1672x941`로 통일해도 된다. 중요한 것은 UXML, USS, C#이 같은 기준을 쓰는 것이다.

```css
.game-prepare-design-space {
    position: absolute;
    left: 0;
    top: 0;
    width: 1600px;
    height: 900px;
    transform-origin: left top;
    overflow: hidden;
    -unity-font-definition: url("/Assets/Resources/Fonts & Materials/NotoSansKR-VariableFont_wght_UITK.asset");
}
```

#### C#

`GamePrepareUIToolkitController`에 `designSpace` 필드를 추가하고 바인딩한다.

```csharp
private VisualElement designSpace;

private void BindElements()
{
    safeRoot = root?.Q<VisualElement>("game-prepare-safe-root");
    designSpace = root?.Q<VisualElement>("game-prepare-design-space");
    // existing binds...
}
```

다른 씬과 같은 스케일 함수를 추가한다.

```csharp
private void UpdateDesignScale()
{
    if (root == null || designSpace == null)
    {
        return;
    }

    var rootWidth = root.resolvedStyle.width > 0f ? root.resolvedStyle.width : Screen.width;
    var rootHeight = root.resolvedStyle.height > 0f ? root.resolvedStyle.height : Screen.height;
    var scale = Mathf.Min(rootWidth / ReferenceWidth, rootHeight / ReferenceHeight);
    var left = (rootWidth - ReferenceWidth * scale) * 0.5f;
    var top = (rootHeight - ReferenceHeight * scale) * 0.5f;

    designSpace.style.left = left;
    designSpace.style.top = top;
    designSpace.transform.scale = new Vector3(scale, scale, 1f);
}
```

`GeometryChangedEvent`에서는 개별 카드 위치를 다시 계산하기보다 전체 디자인 공간 스케일을 갱신한다.

```csharp
private void OnRootGeometryChanged(GeometryChangedEvent evt)
{
    ApplySafeArea();
    UpdateDesignScale();
}
```

이 방식으로 바꾸면 `.shop-panel`, `.round-timer-label`, `.game-resource-root`, `.hud-button`의 위치는 USS의 고정 좌표로 맞추고, APK에서는 전체 UI가 같은 비율로만 스케일된다.

#### 같이 정리할 코드

design-space 방식으로 통일하면 `ApplyResponsiveSize()`에서 다음 항목을 직접 덮어쓰는 코드는 제거하거나 최소화한다.

- `shopPanel.style.paddingLeft/right/top/bottom`
- `roundTimerLabel.style.left/top/width/height`
- `hudRoot.style.left/top/width/height`
- `shopControlRow.style.top/width/height`
- 카드 전체 크기를 화면 폭으로 매번 재계산하는 로직

상점 카드 크기, 카드 간격, 타이머 위치, 버튼 위치는 우선 USS에서 고정하고, 정말 화면 크기에 따라 달라져야 하는 값만 C#에서 조정한다.

### 0-1. design-space로 바로 바꾸기 어렵다면 최소 수정한다

당장 구조 변경이 부담되면 다음만 먼저 고친다.

1. `ShopRowTopRatio = 0.02f`, `ShopPanelTopMin = 10f`, `ShopPanelTopMax = 24f`를 쓰지 않는다.
2. 상단 HUD 높이보다 큰 최소 여백을 계산한다.
3. `GamePreparePanelsStyles.uss`에 NotoSansKR UITK 폰트를 지정한다.
4. `ShopUiScaleBoost = 3f`를 제거하거나 낮춘다.
5. 슬롯 PNG 비율 `200:250`을 유지해서 카드 크기를 계산한다.

이 최소 수정은 APK 겹침을 빨리 줄일 수 있지만, 장기적으로는 다른 씬과 같은 design-space 패턴이 더 안정적이다.

### 1. GamePrepare USS에 한글 폰트를 지정한다

`GamePreparePanelsStyles.uss` 상단의 루트 스타일에 폰트를 추가한다.

```css
.game-prepare-root {
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    bottom: 0;
    flex-grow: 1;
    -unity-font-definition: url("/Assets/Resources/Fonts & Materials/NotoSansKR-VariableFont_wght_UITK.asset");
}
```

필요하면 `.game-prepare-safe-root`나 새로 추가한 `.game-prepare-design-space`에도 같은 값을 넣는다. UXML의 한글 텍스트는 UTF-8로 유지해야 한다.

### 2. 상점 카드가 HUD 아래에서 시작하도록 C# 값을 복구하거나 보정한다

가장 빠른 수정은 기존에 안정적이던 범위로 되돌리는 것이다.

```csharp
private const float ShopRowTopRatio = 0.16f;
private const float ShopPanelTopMin = 118f;
private const float ShopPanelTopMax = 230f;
```

더 안전한 방식은 HUD 높이를 기준으로 최소 여백을 계산하는 것이다.

```csharp
public static float CalculateShopTopPadding(Vector2 screenSize)
{
    var scale = CalculateResponsiveScale(screenSize);
    var hudClearance = TopHudY + HudButtonSize + HudActionGroupCardGap * scale;
    var desiredTop = screenSize.y * 0.16f;
    var maxTop = Mathf.Max(hudClearance, screenSize.y * 0.28f);
    return Mathf.Clamp(Mathf.Max(desiredTop, hudClearance), hudClearance, maxTop);
}
```

핵심은 `10~24px` 같은 고정 상단 여백을 쓰지 않는 것이다.

### 3. 상점 카드 비율을 슬롯 이미지 비율에 맞춘다

슬롯 배경은 `200:250`이므로 카드 계산도 이 비율을 유지한다.

```csharp
private const float ShopSlotAspect = 200f / 250f;

public static Vector2 CalculateCardSize(bool isShop, Vector2 screenSize)
{
    var scale = CalculateResponsiveScale(screenSize);
    if (!isShop)
    {
        return new Vector2(332f * scale * AugmentCardScaleBoost, 480f * scale * AugmentCardScaleBoost);
    }

    var targetWidth = 280f * scale;
    var rowMargins = ShopCardHorizontalMargin * 2f * scale * ShopCardCount;
    var availableWidth = Mathf.Max(
        MinTouchSize * ShopCardCount,
        screenSize.x - ShopPanelLeftPadding - ShopPanelRightPadding - rowMargins);

    var width = Mathf.Min(targetWidth, availableWidth / ShopCardCount);
    var height = width / ShopSlotAspect;
    return new Vector2(width, height);
}
```

`ShopUiScaleBoost = 3f`는 카드가 과하게 커지는 원인이 되므로 제거하거나 `1.1~1.4` 범위로 낮춘다.

### 4. 캐릭터 초상화 크기를 카드 크기에 비례시킨다

`ShopCardView.ApplySize()`에서 카드 크기만 바꾸지 말고 아이콘도 같이 조절한다.

```csharp
public void ApplySize(float width, float height, float scale)
{
    if (Root == null)
    {
        return;
    }

    Root.style.width = width;
    Root.style.height = height;
    Root.style.marginLeft = ShopCardHorizontalMargin * scale;
    Root.style.marginRight = ShopCardHorizontalMargin * scale;

    if (icon != null)
    {
        var iconSize = Mathf.Clamp(width * 0.48f, 120f * scale, 190f * scale);
        icon.style.width = iconSize;
        icon.style.height = iconSize;
    }

    if (body != null)
    {
        body.style.justifyContent = Justify.FlexStart;
        body.style.paddingTop = height * 0.12f;
    }
}
```

슬롯 PNG의 초상화 창 위치가 다르면 `paddingTop`과 `iconSize` 비율을 APK 스크린샷 기준으로 조정한다.

### 5. 증강 카드 텍스트가 아이콘에 밀리지 않게 공간을 고정한다

폰트 지정 후에도 증강 설명이 잘리면 `.augment-icon`, `.card-name`, `.card-description`을 증강 카드 전용으로 분리한다.

```css
.augment-card .card-body {
    justify-content: flex-start;
    padding-top: 34px;
}

.augment-card .augment-icon {
    width: 150px;
    height: 150px;
    margin-bottom: 18px;
}

.augment-card .card-name {
    font-size: 24px;
    margin-bottom: 10px;
}

.augment-card .card-description {
    font-size: 17px;
    white-space: normal;
}
```

### 6. Android 해상도 변경에 맞춰 재계산한다

design-space 방식으로 전환하지 않는다면, root 또는 safeRoot 크기가 바뀔 때 safe area와 responsive size를 다시 계산한다. 현재 코드에는 `safeRoot`의 `GeometryChangedEvent` 콜백이 있으므로, 이 콜백이 실제 Android 빌드에서도 호출되는지 로그나 스크린샷으로 확인한다.

필요하면 다른 씬처럼 root 기준 콜백으로 명확히 분리한다.

```csharp
private void OnEnable()
{
    EnsureVisualTree();
    BindElements();
    ConfigurePickingModes();
    ApplySafeArea();
    ApplyResponsiveSize();
    root?.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
    RegisterCallbacks();
    SubscribeEvents();
    RefreshRuntimeReferences();
    HideLegacyContent();
    SetShopVisible(false);
    SetAugmentVisible(false);
    SetAttackSequenceVisible(false);
    UpdateHudState(true);
}

private void OnDisable()
{
    root?.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
    UnsubscribeEvents();
    ReleaseIconHandles();
}

private void OnRootGeometryChanged(GeometryChangedEvent evt)
{
    ApplySafeArea();
    ApplyResponsiveSize();
}
```

## 권장 수정 순서

1. Game 씬 UI를 다른 씬과 같은 `game-prepare-design-space` 패턴으로 통일할지 결정한다.
2. 바로 통일한다면 UXML에 design-space를 추가하고, 위치/크기 대부분을 USS 고정 좌표로 옮긴다.
3. 당장 최소 수정만 한다면 `ShopRowTopRatio`, `ShopPanelTopMin`, `ShopPanelTopMax`부터 복구한다.
4. `GamePreparePanelsStyles.uss`에 NotoSansKR UITK 폰트 지정을 추가한다.
5. `CalculateCardSize(true, ...)`를 슬롯 PNG 비율 `200:250` 기준으로 변경한다.
6. `ShopCardView.ApplySize()`에서 초상화 크기와 `card-body` 상단 여백을 카드 크기 기준으로 조정한다.
7. APK에서 증강/상점/공격 선택 UI를 각각 스크린샷으로 확인한다.

## 검증 명령

C# 또는 USS/UXML 변경 후:

```powershell
unity-cli --project Mdfproject status
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

씬 YAML이나 프리팹을 직접 수정했다면 먼저 리시리얼라이즈한다.

```powershell
unity-cli --project Mdfproject reserialize Mdfproject/Assets/Scenes/03_Game.unity
unity-cli --project Mdfproject editor refresh --compile
unity-cli --project Mdfproject console --type error --stacktrace user
```

APK 확인 기준:

- 증강 카드 3장 모두 이름, 등급, 설명, `선택` 텍스트가 보인다.
- 상점 카드 5장이 라운드 타이머와 좌상단 버튼을 침범하지 않는다.
- 캐릭터 초상화가 슬롯 프레임 안에 들어간다.
- Gold/Stone HUD가 safe area 밖으로 밀리지 않는다.
- 에디터 Game View와 APK 스크린샷의 카드 위치가 같은 규칙으로 보인다.

