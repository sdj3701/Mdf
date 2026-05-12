# ReLogin UI Toolkit 구현 계획

## 목표

`C:/Users/djthe/Downloads/ChatGPT Image 2026년 4월 30일 오후 10_08_20.png` 이미지를 기준으로 `Title` 또는 재로그인 화면을 Unity UI Toolkit으로 재현한다.

- 기준 해상도: `1536 x 1024`
- 기준 비율: `3:2`
- 목표 품질: 기준 해상도에서 이미지와 거의 같은 위치, 크기, 색감, 여백으로 보이게 만든다.
- 구현 방식: UI Toolkit `UXML + USS + C# Controller`
- 기존 프로젝트 연결: `Assets/Scripts/Auth`의 `LoginUseCase`, `AuthServiceFactory`, `SceneDefine`, `PlayerPrefsDefine`를 우선 재사용한다.

## 핵심 전략

이미지 그대로 옮기려면 UI를 전부 유동 레이아웃으로 만들기보다, `1536x1024` 디자인 캔버스를 하나 만들고 그 안에 절대 좌표로 배치하는 방식이 가장 정확하다.

1. `UIDocument`의 root 아래에 `relogin-design-space`를 만든다.
2. `relogin-design-space`의 크기를 `1536px x 1024px`로 고정한다.
3. 화면 크기가 바뀌면 C#에서 `scale = Min(screenWidth / 1536, screenHeight / 1024)`를 계산한다.
4. `relogin-design-space`에 `style.scale`을 적용하고 중앙 정렬한다.
5. 16:9 같은 넓은 화면에서는 좌우 여백이 생기거나 배경만 확장하고, 주요 UI 좌표는 깨지지 않게 유지한다.

이 방식이면 원본 이미지와 좌표 비교가 쉬워지고, UI Toolkit의 flex 레이아웃 때문에 버튼이나 입력창 위치가 미묘하게 밀리는 문제를 줄일 수 있다.

## 추천 파일 구조

```text
Mdfproject/
  Assets/
    UI/
      ReLogin/
        ReLogin.uxml
        ReLogin.uss
        ReLoginPanelSettings.asset
    Scripts/
      UI/
        ReLogin/
          ReLoginUIToolkitController.cs
    Resource/
      Image/
        UI/
          ReLogin/
            bg_relogin_full.png
            logo_nabia.png
            title_whisper_butterfly.png
            frame_corner_tl.png
            frame_corner_tr.png
            frame_corner_bl.png
            frame_corner_br.png
            input_frame.png
            login_button.png
            social_diamond.png
            icon_user.png
            icon_lock.png
            icon_eye.png
            icon_eye_off.png
            icon_gear.png
            icon_headset.png
            icon_globe.png
            icon_chevron_down.png
            icon_google.png
            icon_apple.png
            icon_gamepad.png
            icon_account_find.png
```

`Assets/Resource/Image/UI`가 이미 존재하므로, 로그인 전용 이미지는 `Assets/Resource/Image/UI/ReLogin` 아래에 모으는 것이 자연스럽다. UI Toolkit 문서 파일은 런타임 UI 성격이 강하므로 `Assets/UI/ReLogin`처럼 별도 루트로 분리한다.

## 에셋 준비 계획

### 1차 구현용 필수 에셋

| 에셋 | 용도 | 처리 방식 |
| --- | --- | --- |
| `bg_relogin_full.png` | 배경, 캐릭터, 숲, 보라색 나비 분위기 | UI 없는 원본 배경이 있으면 사용. 없으면 현재 이미지를 기준으로 UI 영역을 제거한 배경을 별도 제작 |
| `logo_nabia.png` | 좌상단 NABIA 로고 | 텍스트로 만들기보다 이미지화 권장 |
| `title_whisper_butterfly.png` | 중앙 우측 문장과 심볼 | 폰트와 글로우까지 맞추기 위해 이미지화 권장 |
| `input_frame.png` | 이메일/비밀번호 입력창 테두리 | 9-slice sprite |
| `login_button.png` | 보라색 로그인 버튼 | 9-slice sprite, normal/hover/pressed 상태 가능 |
| `social_diamond.png` | 구글/애플/게임패드 버튼 다이아 프레임 | 9-slice 또는 고정 이미지 |
| corner frame 4종 | 화면 외곽 장식 | 각 모서리 개별 이미지 |
| icon 12종 | 입력창, 상단 메뉴, 소셜 로그인 | PNG 또는 SVG. 프로젝트 일관성을 위해 PNG 권장 |

### 원본 PNG만 있을 때의 처리

현재 이미지는 UI와 배경이 합쳐진 완성 시안이다. 최종 구현에서 깨끗하게 재현하려면 아래 중 하나가 필요하다.

- 가장 좋은 방법: 배경, 캐릭터, 로고, 타이틀, 버튼 프레임이 분리된 PSD/PNG 레이어를 확보한다.
- 현실적인 방법: 원본 이미지를 기준으로 배경에서 우측 UI 영역을 제거한 `bg_relogin_full.png`를 새로 만든다.
- 임시 검증 방법: 원본 이미지를 `reference_overlay.png`로 넣고 opacity를 낮춰 실제 UI와 좌표를 비교한다. 이 이미지는 최종 빌드에서는 비활성화한다.

## 기준 좌표

아래 좌표는 `1536x1024` 디자인 캔버스 기준이다. 실제 구현에서는 `relogin-design-space` 내부 절대 좌표로 둔다.

| 영역 | x | y | w | h | 설명 |
| --- | ---: | ---: | ---: | ---: | --- |
| 외곽 프레임 | 16 | 14 | 1504 | 994 | 얇은 보라/회색 테두리와 네 모서리 장식 |
| 좌상단 로고 | 65 | 46 | 156 | 40 | `NABIA` |
| 로고 서브타이틀 | 66 | 78 | 145 | 12 | `THE PROMISED NIGHT` |
| 상단 메뉴 | 1140 | 48 | 330 | 34 | 설정, 고객센터, 언어 |
| 메인 타이틀 심볼 | 1030 | 126 | 145 | 116 | 보라색 문양 |
| 메인 타이틀 | 885 | 242 | 430 | 74 | `나비의 속삭임` |
| 영문 서브타이틀 | 943 | 342 | 260 | 20 | `THE WHISPER OF BUTTERFLY` |
| 로그인 폼 루트 | 840 | 402 | 520 | 470 | 우측 중앙 입력/버튼 묶음 |
| 이메일 입력창 | 842 | 405 | 516 | 68 | 아이콘 + placeholder |
| 비밀번호 입력창 | 842 | 486 | 516 | 68 | 아이콘 + placeholder + eye |
| 로그인 유지 체크 | 850 | 574 | 28 | 28 | 보라 체크박스 |
| 로그인 유지 라벨 | 888 | 578 | 160 | 30 | `로그인 상태 유지` |
| 비밀번호 찾기 | 1234 | 579 | 120 | 30 | 우측 링크 |
| 로그인 버튼 | 838 | 635 | 546 | 70 | 보라 그라데이션 버튼 |
| 구분선 | 895 | 744 | 370 | 18 | 가운데 `또는` |
| 구글 버튼 | 895 | 788 | 78 | 78 | 다이아몬드 버튼 |
| 애플 버튼 | 1000 | 788 | 78 | 78 | 다이아몬드 버튼 |
| 게스트 버튼 | 1107 | 788 | 210 | 78 | 게임패드 아이콘 + 텍스트 |
| 계정 찾기 | 66 | 928 | 130 | 42 | 좌하단 아이콘 + 텍스트 |
| 카피라이트 | 588 | 982 | 360 | 18 | 하단 중앙 |

좌표는 실제 구현 후 스크린샷을 찍어 조정한다. 특히 타이틀, 로그인 버튼, 소셜 로그인 영역은 2~6px 정도 보정이 필요할 수 있다.

## UXML 구조

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
  <ui:VisualElement name="relogin-root" class="relogin-root">
    <ui:VisualElement name="relogin-design-space" class="relogin-design-space">
      <ui:VisualElement name="background" class="background" />

      <ui:VisualElement name="outer-frame" class="outer-frame">
        <ui:VisualElement class="frame-corner frame-corner--tl" />
        <ui:VisualElement class="frame-corner frame-corner--tr" />
        <ui:VisualElement class="frame-corner frame-corner--bl" />
        <ui:VisualElement class="frame-corner frame-corner--br" />
      </ui:VisualElement>

      <ui:VisualElement name="top-left-logo" class="top-left-logo" />

      <ui:VisualElement name="top-actions" class="top-actions">
        <ui:Button name="settingsButton" class="top-icon-button" />
        <ui:VisualElement class="top-separator" />
        <ui:Button name="customerCenterButton" class="top-text-button" text="고객센터" />
        <ui:VisualElement class="top-separator" />
        <ui:Button name="languageButton" class="top-text-button" text="한국어" />
      </ui:VisualElement>

      <ui:VisualElement name="title-art" class="title-art" />

      <ui:VisualElement name="login-panel" class="login-panel">
        <ui:VisualElement name="emailInputWrap" class="input-wrap input-wrap--email">
          <ui:VisualElement class="input-icon input-icon--user" />
          <ui:TextField name="accountField" class="login-input" />
          <ui:Label name="accountPlaceholder" class="input-placeholder" text="이메일 또는 계정 입력" />
        </ui:VisualElement>

        <ui:VisualElement name="passwordInputWrap" class="input-wrap input-wrap--password">
          <ui:VisualElement class="input-icon input-icon--lock" />
          <ui:TextField name="passwordField" class="login-input" />
          <ui:Label name="passwordPlaceholder" class="input-placeholder" text="비밀번호 입력" />
          <ui:Button name="passwordVisibilityButton" class="eye-button" />
        </ui:VisualElement>

        <ui:VisualElement name="loginOptions" class="login-options">
          <ui:Toggle name="rememberToggle" class="remember-toggle" text="로그인 상태 유지" />
          <ui:Button name="forgotPasswordButton" class="link-button" text="비밀번호 찾기" />
        </ui:VisualElement>

        <ui:Button name="loginButton" class="login-button" text="로그인" />

        <ui:VisualElement name="divider" class="divider">
          <ui:VisualElement class="divider-line divider-line--left" />
          <ui:Label class="divider-text" text="또는" />
          <ui:VisualElement class="divider-line divider-line--right" />
        </ui:VisualElement>

        <ui:VisualElement name="socialLogin" class="social-login">
          <ui:Button name="googleLoginButton" class="social-button social-button--google" />
          <ui:Button name="appleLoginButton" class="social-button social-button--apple" />
          <ui:Button name="guestLoginButton" class="guest-button" text="게스트 로그인" />
        </ui:VisualElement>
      </ui:VisualElement>

      <ui:Button name="accountFindButton" class="account-find-button" text="계정 찾기" />
      <ui:Label name="copyrightLabel" class="copyright-label" text="© 2024 NABIA. ALL RIGHTS RESERVED." />

      <ui:VisualElement name="referenceOverlay" class="reference-overlay reference-overlay--hidden" />
    </ui:VisualElement>
  </ui:VisualElement>
</ui:UXML>
```

## USS 작성 방향

### 디자인 캔버스

```css
.relogin-root {
    width: 100%;
    height: 100%;
    background-color: #05040b;
    overflow: hidden;
}

.relogin-design-space {
    position: absolute;
    width: 1536px;
    height: 1024px;
    overflow: hidden;
}

.background {
    position: absolute;
    left: 0;
    top: 0;
    width: 1536px;
    height: 1024px;
    background-image: url("project://database/Assets/Resource/Image/UI/ReLogin/bg_relogin_full.png");
    -unity-background-scale-mode: stretch-to-fill;
}
```

### 색상 기준

| 목적 | 색상 |
| --- | --- |
| 배경 암부 | `#05040B` |
| 입력창 배경 | `rgba(8, 7, 15, 0.72)` |
| 입력창 테두리 | `rgba(164, 144, 180, 0.55)` |
| 일반 텍스트 | `#DDD6E6` |
| 비활성/placeholder | `#A8A0AA` |
| 보라 포인트 | `#B66CFF` |
| 버튼 광원 | `#8E35D8` |
| 버튼 암부 | `#321456` |
| 얇은 선 | `rgba(200, 181, 215, 0.35)` |

### 입력창

- `TextField` 기본 스타일을 제거하고, wrapper에 프레임 이미지를 입힌다.
- placeholder는 `Label`로 직접 올린다. 입력값이 생기면 C#에서 `display: none` 처리한다.
- 비밀번호 필드는 C#에서 `TextField.isPasswordField = true`로 설정한다.
- eye 버튼은 클릭 시 `isPasswordField`를 토글하고 아이콘을 `icon_eye.png` / `icon_eye_off.png`로 바꾼다.

### 버튼

- 로그인 버튼은 일반 CSS 사각형보다 `login_button.png`를 9-slice로 쓰는 편이 원본에 가깝다.
- hover 시 밝기 8~12% 증가, pressed 시 y축 1px 이동과 밝기 감소를 준다.
- UI Toolkit USS에서 이미지 버튼의 글자 중앙 정렬을 강제한다.

```css
.login-button {
    position: absolute;
    left: -2px;
    top: 233px;
    width: 546px;
    height: 70px;
    background-image: url("project://database/Assets/Resource/Image/UI/ReLogin/login_button.png");
    -unity-slice-left: 36;
    -unity-slice-right: 36;
    -unity-slice-top: 18;
    -unity-slice-bottom: 18;
    color: #ffffff;
    font-size: 30px;
    -unity-font-style: normal;
}
```

## C# Controller 계획

파일: `Assets/Scripts/UI/ReLogin/ReLoginUIToolkitController.cs`

책임:

- `UIDocument.rootVisualElement`에서 필요한 VisualElement를 Query한다.
- 디자인 캔버스 스케일을 계산한다.
- placeholder 표시/숨김을 처리한다.
- 비밀번호 보기 버튼을 처리한다.
- 로그인 버튼, 게스트 로그인, 소셜 버튼 이벤트를 연결한다.
- 기존 `LoginUseCase`와 `AuthServiceFactory`를 호출한다.
- 성공 시 `SceneDefine.MatchingLobby` 또는 현재 프로젝트 흐름에 맞는 다음 씬으로 이동한다.

예상 흐름:

```csharp
private void OnEnable()
{
    _root = document.rootVisualElement;
    _designSpace = _root.Q<VisualElement>("relogin-design-space");

    _accountField = _root.Q<TextField>("accountField");
    _passwordField = _root.Q<TextField>("passwordField");
    _passwordField.isPasswordField = true;

    _loginUseCase = new LoginUseCase(AuthServiceFactory.CreateFromDefine());

    RegisterCallbacks();
    UpdateDesignScale();
    _root.RegisterCallback<GeometryChangedEvent>(_ => UpdateDesignScale());
}
```

현재 인증 구조는 `IAuthService.SignInWithDisplayName(string)`만 제공한다. 따라서 1차 구현에서는 `accountField` 값을 displayName처럼 넘겨 로그인 흐름을 연결하고, `passwordField`는 UI만 존재하게 둔다. 실제 이메일/비밀번호 로그인을 붙일 때는 아래 확장이 필요하다.

```csharp
public interface IAuthService
{
    AuthResult SignInWithDisplayName(string displayNameInput);
    AuthResult SignInWithEmailPassword(string emailOrAccount, string password);
    AuthResult SignInAsGuest();
    AuthResult SignInWithProvider(AuthProviderMode provider);
}
```

소셜 로그인 버튼은 1차에서는 disabled 또는 안내 메시지로 처리하고, Firebase/플랫폼 SDK가 연결되는 시점에 실제 Provider 로그인으로 전환한다.

## 입력 필드 포커스/히트박스 문제 대응 계획

### 현재 증상

아이디/비밀번호 입력창에서 실제로 입력해야 하는 영역이 아닌 곳을 클릭한 뒤 키보드로 입력해도 `accountField`에 텍스트가 들어가는 문제가 있다. 목표 동작은 아래처럼 명확해야 한다.

- `accountInputHitbox`를 정확히 클릭했을 때만 아이디 입력 포커스가 생긴다.
- `passwordInputHitbox`를 정확히 클릭했을 때만 비밀번호 입력 포커스가 생긴다.
- 입력창 프레임, 아이콘, 입력창 주변 배경, 로그인 패널 빈 공간, 다른 버튼 영역을 클릭한 뒤 키보드를 누르면 어떤 TextField에도 값이 들어가지 않는다.
- 이미 아이디 입력창에 포커스가 있었더라도 입력 허용 영역 밖을 클릭하면 포커스가 반드시 해제된다.

### 현재 구현 기준으로 의심되는 원인

현재 구조는 `TextField` 자체를 직접 클릭하게 하지 않고, 별도의 투명 hitbox를 눌렀을 때 C#에서 강제로 포커스를 주는 방식이다.

- `Assets/UI/ReLogin/ReLogin.uxml`
  - `accountInputArea` 안에 `accountField`, `accountPlaceholder`, `accountInputHitbox`가 있다.
  - `passwordInputArea` 안에 `passwordField`, `passwordPlaceholder`, `passwordInputHitbox`, `passwordVisibilityButton`이 있다.
- `Assets/UI/ReLogin/ReLogin.uss`
  - `.input-area`는 `width: 560px; height: 68px; picking-mode: position;`이다.
  - `.relogin-text-field`는 `left: 76px; top: 8px; width: 430px; height: 52px;`이다.
  - `.text-input-hitbox`도 `left: 76px; top: 8px; height: 52px;`이고, account는 `width: 430px`, password는 `width: 385px`이다.
- `Assets/Scripts/UI/ReLogin/ReLoginUIToolkitController.cs`
  - `ConfigureTextFieldPicking(accountField, PickingMode.Ignore)`로 TextField 클릭을 막으려 한다.
  - `accountInputHitbox.PointerDown`에서만 `FocusTextField(accountField)`를 호출한다.
  - root의 `PointerDown`에서 `BlurTextField(accountField)`와 `BlurTextField(passwordField)`를 호출한다.

문제가 생길 수 있는 지점은 크게 세 가지다.

1. **포커스 해제가 완전히 되지 않을 가능성**
   - UI Toolkit의 `TextField`는 바깥 `TextField` 요소와 내부 입력 요소(`unity-text-field__input` 또는 `unity-base-text-field__input`)가 따로 있다.
   - 현재 `BlurTextField()`는 `field.Blur()`와 내부 입력 요소 `Blur()`를 호출하지만, 런타임에서 `FocusController`가 마지막 포커스 요소를 계속 들고 있으면 키 입력이 이전 TextField로 전달될 수 있다.
   - 특히 클릭한 대상이 focusable이 아닌 투명 `VisualElement`이면, 바깥을 클릭해도 새 포커스 대상이 생기지 않아 이전 입력 포커스가 남는 상황이 생길 수 있다.

2. **hitbox 좌표가 사용자가 생각하는 입력 가능 영역과 다를 가능성**
   - 화면에 보이는 입력창 프레임 전체와 실제 입력 허용 hitbox는 같은 영역이 아니다.
   - 현재 account 기준 실제 전역 hitbox는 `accountInputArea left 832 + hitbox left 76 = x 908`부터 `width 430`만큼이다.
   - 즉 account 입력 허용 영역은 대략 `x 908~1338`, `y 413~465`이다.
   - 원본 이미지의 입력창 프레임은 더 왼쪽 아이콘 영역과 오른쪽 여백까지 포함하므로, 사용자는 "입력창 밖"이라고 느끼는 지점이 실제 hitbox 안에 들어가거나 반대로 보이는 입력창인데도 hitbox 밖일 수 있다.
   - 따라서 문제 재현 위치가 좌/우 어느 쪽인지 먼저 debug 색상으로 확인하고, `.text-input-hitbox--account`와 `.text-input-hitbox--password`의 `left`, `top`, `width`, `height`를 실제로 입력을 허용할 사각형에 맞춰 줄여야 한다.

3. **TextField 내부 picking 상태가 다시 살아날 가능성**
   - `ConfigureTextFieldPicking()`은 현재 시점의 TextField와 자식에 `PickingMode.Ignore`를 적용하고, `ExecuteLater(0)`로 한 번 더 적용한다.
   - UI Toolkit은 attach, layout, password field 토글, 내부 구조 갱신 시 내부 입력 요소를 다시 만들거나 스타일 상태를 바꿀 수 있다.
   - 이때 내부 입력 요소가 다시 pointer target이 되면, hitbox가 아닌 TextField 영역 클릭으로도 포커스가 들어갈 수 있다.
   - password는 눈 버튼을 누를 때 `ConfigureTextFieldPicking(passwordField, PickingMode.Ignore)`를 다시 부르지만, account는 초기 이후 재적용 시점이 부족할 수 있다.

### 수정해야 할 위치

코드 수정 시에는 아래 파일만 우선 보면 된다. 이 문서에서는 코드 작업을 하지 않고, 수정 지점만 기록한다.

| 파일 | 수정 지점 | 수정 방향 |
| --- | --- | --- |
| `Assets/UI/ReLogin/ReLogin.uxml` | `accountInputHitbox`, `passwordInputHitbox` | 실제 입력 허용 영역을 명시하는 전용 요소로 유지한다. wrapper나 placeholder 클릭에서 포커스를 주는 콜백은 추가하지 않는다. |
| `Assets/UI/ReLogin/ReLogin.uss` | `.text-input-hitbox`, `.text-input-hitbox--account`, `.text-input-hitbox--password` | debug 색상으로 실제 클릭 가능 영역을 확인한 뒤, 정확히 입력을 허용할 영역으로 `left/top/width/height`를 조정한다. 입력창 프레임 전체가 아니라 텍스트가 들어갈 영역만 허용할지, 프레임 내부 전체를 허용할지 정책을 먼저 정한다. |
| `Assets/UI/ReLogin/ReLogin.uss` | `.relogin-text-field`, `.relogin-text-field .unity-text-field__input`, `.relogin-text-field .unity-base-text-field__input` | 가능하면 USS에서도 `picking-mode: ignore`를 명시해 C# 적용 전/후 타이밍 문제를 줄인다. C#의 recursive ignore만 믿지 않는다. |
| `Assets/UI/ReLogin/ReLogin.uss` | `.input-area` | wrapper 자체가 클릭 target이 되었을 때 포커스가 생기지 않게 유지한다. 자식 hitbox 클릭이 정상 동작하는지 확인하면서 `picking-mode: ignore` 또는 `position` 중 안정적인 쪽을 선택한다. wrapper에 focus 콜백은 두지 않는다. |
| `Assets/Scripts/UI/ReLogin/ReLoginUIToolkitController.cs` | `OnRootPointerDown()` | 단순히 `TextField.Blur()`만 호출하지 말고, 현재 클릭 위치가 입력 허용 hitbox 안인지 검사한 뒤 바깥 클릭이면 전체 포커스를 확실히 비우는 흐름으로 바꾼다. |
| `Assets/Scripts/UI/ReLogin/ReLoginUIToolkitController.cs` | `BlurTextField()` 또는 신규 `BlurAllTextFields()` | `field.Blur()`, 내부 input `Blur()`에 더해 `root.focusController`의 마지막 포커스 요소를 해제하거나, 별도 focus sink로 포커스를 이동시키는 방식을 검토한다. |
| `Assets/Scripts/UI/ReLogin/ReLoginUIToolkitController.cs` | `ConfigureTextFieldPicking()` | 초기 1회와 `ExecuteLater(0)`만이 아니라 `AttachToPanelEvent`, `GeometryChangedEvent`, password visibility toggle 후에도 account/password 둘 다 picking ignore가 유지되는지 확인한다. |
| `Assets/Scripts/UI/ReLogin/ReLoginUIToolkitController.cs` | `OnAccountInputHitboxPointerDown()`, `OnPasswordInputHitboxPointerDown()` | 포커스를 주기 전에 반대편 TextField를 blur하고, 이벤트는 현재처럼 `StopPropagation()`해서 root blur와 충돌하지 않게 한다. |

### 권장 수정 흐름

1. **재현 좌표 확인**
   - 현재 USS의 debug 색상이 켜져 있으므로, 문제가 나는 클릭 위치가 파란 account hitbox 안인지 밖인지 먼저 확인한다.
   - 파란 hitbox 안이라면 좌표/크기 정책 문제다. `.text-input-hitbox--account`의 영역을 줄이면 된다.
   - 파란 hitbox 밖인데도 입력된다면 포커스 해제 또는 TextField 내부 picking 문제다.

2. **입력 허용 영역 정책 결정**
   - 엄격한 정책: 텍스트가 실제로 표시되는 영역만 클릭 가능하게 한다. 아이콘 영역과 오른쪽 여백은 클릭해도 입력되지 않는다.
   - 일반 UX 정책: 입력창 프레임 내부 전체는 클릭 가능하게 한다. 아이콘 영역 포함 여부만 따로 결정한다.
   - 이번 요구는 "정확한 inputField를 누르고 키보드 입력해야 값이 들어가야 한다"이므로, 엄격한 정책을 기준으로 잡는 편이 맞다.

3. **TextField 직접 클릭 차단 강화**
   - `TextField`와 내부 input은 항상 `PickingMode.Ignore` 상태여야 한다.
   - 포커스는 오직 `accountInputHitbox`와 `passwordInputHitbox`의 `PointerDown`에서만 들어가야 한다.

4. **바깥 클릭 시 포커스 해제 강화**
   - root pointer down에서 클릭 위치가 account/password hitbox 내부가 아니면 전체 TextField 포커스를 비운다.
   - 단순 blur로 부족하면 `FocusController`의 현재 포커스를 비우거나, 화면에는 보이지 않는 focusable sink 요소로 포커스를 이동시키는 방식을 사용한다.
   - 이 처리가 없으면 이전에 account가 포커스를 가진 상태에서 비입력 영역을 클릭해도 키보드 입력이 account로 계속 들어갈 수 있다.

5. **검증**
   - account hitbox 안 클릭 후 입력: account에만 입력되어야 한다.
   - password hitbox 안 클릭 후 입력: password에만 입력되어야 한다.
   - account 입력 후 배경 클릭 후 입력: 아무 값도 들어가지 않아야 한다.
   - account 입력 후 입력창 프레임의 아이콘 영역 클릭 후 입력: 엄격 정책이면 아무 값도 들어가지 않아야 한다.
   - account 입력 후 로그인 버튼/소셜 버튼/상단 버튼 클릭 후 입력: 아무 값도 들어가지 않아야 한다.
   - password eye 클릭 후 입력: password가 이미 포커스 중일 때만 password에 입력되고, eye 클릭만으로 새 포커스가 생기면 안 된다.

### 완료 기준에 추가할 조건

- 입력값은 `accountInputHitbox` 또는 `passwordInputHitbox`를 직접 클릭한 이후에만 변경된다.
- 입력 허용 hitbox 밖을 클릭하면 기존 TextField 포커스가 반드시 해제된다.
- 보이지 않는 `TextField` 영역이나 wrapper 영역이 클릭 입력을 가로채지 않는다.
- debug hitbox 색상을 끈 상태에서도 동일하게 동작한다.

## 기존 코드와 연결할 부분

| 기존 파일 | 사용할 내용 | 계획 |
| --- | --- | --- |
| `Assets/Scripts/Auth/UseCases/LoginUseCase.cs` | 로그인 실행 | 1차 로그인 버튼에서 호출 |
| `Assets/Scripts/Auth/Services/AuthServiceFactory.cs` | 인증 서비스 생성 | `CreateFromDefine()` 사용 |
| `Assets/Scripts/Defines/AuthDefine.cs` | Firebase/Local 설정 | 현재는 LocalString 기본값 유지 |
| `Assets/Scripts/Defines/PlayerPrefsDefine.cs` | 로그인 사용자 저장 키 | 로그인 유지, 마지막 계정 저장 키 추가 검토 |
| `Assets/Scripts/Defines/SceneDefine.cs` | 씬 이름 | 로그인 성공 후 이동할 씬 상수 사용 |
| `Assets/Scripts/UI/LanguageSelector.cs` | 언어 변경 방식 참고 | UI Toolkit용 언어 버튼 컨트롤러로 별도 구현 |

## 씬 구성 계획

대상 씬: `Assets/Scenes/Title.unity`

1. 기존 Canvas 로그인 UI가 있다면 비활성화하거나 별도 GameObject로 분리한다.
2. `ReLoginUI` GameObject를 만든다.
3. `UIDocument` 컴포넌트를 추가한다.
4. `PanelSettings`를 `ReLoginPanelSettings.asset`으로 연결한다.
5. `Source Asset`을 `ReLogin.uxml`로 연결한다.
6. 같은 GameObject에 `ReLoginUIToolkitController`를 붙인다.
7. EventSystem이 필요하면 씬에 하나만 유지한다.

PanelSettings 권장값:

- Scale Mode: `Constant Pixel Size` 또는 `Scale With Screen Size`
- Target Texture: 없음
- Theme Style Sheet: 기본값 또는 ReLogin 전용 theme
- Sorting Order: 기존 타이틀 UI보다 위

정확한 픽셀 매칭을 위해서는 PanelSettings의 자동 스케일보다 Controller의 `design-space scale`을 우선한다.

## 반응형 처리

### 기준

- `1536x1024`: 원본 이미지와 1:1 매칭
- `1920x1080`: 디자인 캔버스를 `1080 / 1024 = 1.054`배로 키우면 너비는 약 `1620px`, 좌우 여백 약 `150px`
- `2560x1440`: 디자인 캔버스를 `1440 / 1024 = 1.406`배로 키우면 너비는 약 `2160px`, 좌우 여백 약 `200px`
- `1280x720`: 디자인 캔버스를 `720 / 1024 = 0.703`배로 줄이면 너비는 약 `1080px`, 좌우 여백 약 `100px`

PC 로그인 화면이면 주요 UI를 찌그러뜨리지 않는 letterbox 방식이 좋다. 모바일 대응이 필요하면 별도 모바일 UXML/USS를 만드는 편이 낫다.

### 넓은 화면 배경 처리

주요 UI는 design-space 안에 유지하고, root에 별도 `background-bleed`를 둬서 좌우 여백이 검은색으로 비지 않게 한다.

- root 배경: 원본 배경을 `scale-and-crop`으로 전체 화면에 깔기
- design-space 배경: 정확한 `1536x1024` 기준 배경
- 이렇게 하면 와이드 화면에서도 분위기는 유지되고, UI 좌표는 원본과 맞는다.

## 세부 UI 구현 체크리스트

### 배경/프레임

- [ ] `bg_relogin_full.png`를 full-screen 배경으로 배치
- [ ] 외곽 1px 테두리 추가
- [ ] 네 모서리 장식 이미지 배치
- [ ] 하단 보라색 빛 번짐이 배경에 포함되어 있는지 확인
- [ ] 좌하단/우하단 나비와 보라 파티클이 잘리지 않는지 확인

### 로고/상단 메뉴

- [ ] 좌상단 `NABIA` 로고 이미지 배치
- [ ] 우상단 설정 아이콘 버튼
- [ ] 고객센터 아이콘 + 텍스트 버튼
- [ ] 언어 아이콘 + `한국어` + 아래 화살표 버튼
- [ ] 상단 separator 선 높이와 색상 조정
- [ ] 모든 상단 버튼 hover 영역은 보이진 않지만 클릭 가능하게 `34px` 이상 확보

### 타이틀

- [ ] 보라색 문양 심볼을 별도 이미지로 배치
- [ ] `나비의 속삭임` 타이틀을 이미지로 배치
- [ ] 영문 서브타이틀을 이미지 또는 Label로 배치
- [ ] 타이틀 전체 중심이 로그인 폼보다 위쪽에 오도록 조정

### 입력폼

- [ ] 이메일/계정 입력창 프레임 9-slice 적용
- [ ] 유저 아이콘 위치 조정
- [ ] placeholder `이메일 또는 계정 입력`
- [ ] 비밀번호 입력창 프레임 9-slice 적용
- [ ] 자물쇠 아이콘 위치 조정
- [ ] placeholder `비밀번호 입력`
- [ ] eye 버튼 위치와 토글 상태 이미지 구현
- [ ] 포커스 상태에서 테두리 보라색 글로우 적용
- [ ] 오류 상태에서 얇은 붉은 테두리나 하단 메시지 표시. 원본에는 없으므로 기본은 숨김

### 로그인 옵션

- [ ] `로그인 상태 유지` 커스텀 체크박스 구현
- [ ] 체크 상태는 보라색 체크 아이콘으로 표현
- [ ] `비밀번호 찾기`는 오른쪽 정렬
- [ ] 링크 hover 시 밝기 증가

### 로그인 버튼

- [ ] 버튼 프레임과 보라 그라데이션 이미지 적용
- [ ] 중앙 `로그인` 텍스트 크기와 색상 조정
- [ ] hover/pressed/disabled 상태 작성
- [ ] 클릭 중 중복 요청 방지
- [ ] 로그인 실패 시 버튼 아래 메시지 표시 또는 toast 표시

### 소셜 로그인

- [ ] `또는` 구분선 배치
- [ ] 구글 다이아 버튼
- [ ] 애플 다이아 버튼
- [ ] 게임패드 아이콘 + `게스트 로그인`
- [ ] 미구현 provider는 disabled 스타일 또는 준비중 메시지

### 하단

- [ ] 좌하단 `계정 찾기` 버튼 배치
- [ ] 하단 중앙 copyright 배치
- [ ] 외곽 프레임과 텍스트가 겹치지 않게 조정

## 입력/상태 처리 계획

| 상태 | 처리 |
| --- | --- |
| 최초 진입 | account/password 비어 있음, remember는 PlayerPrefs 값 로드 |
| account 입력 | placeholder 숨김 |
| password 입력 | placeholder 숨김, 기본 마스킹 |
| eye 클릭 | password masking 토글 |
| 로그인 클릭 | 입력 검증 후 LoginUseCase 실행 |
| 로그인 성공 | PlayerPrefs 저장 후 다음 씬 이동 |
| 로그인 실패 | 사용자 메시지 표시, 버튼 재활성화 |
| remember 체크 | PlayerPrefs에 `RememberLogin` 같은 신규 키 저장 |
| 언어 클릭 | 드롭다운/팝업 열기 또는 현재는 `ko/en` 순환 |
| 게스트 로그인 | 기존 LocalStringAuthService를 활용해 임시 닉네임으로 로그인 |

## 신규 PlayerPrefs 키 제안

```csharp
public const string RememberLoginKey = "RememberLogin";
public const string LastAccountInputKey = "LastAccountInput";
public const string SelectedLanguageKey = "SelectedLanguage";
```

기존 `PlayerPrefsDefine`에 추가하거나, UI 전용 Define 파일을 따로 만든다. 이미 `LanguageSelector`가 내부 상수로 `SelectedLanguage`를 쓰고 있으므로 중복을 줄이려면 `PlayerPrefsDefine`으로 옮기는 것이 좋다.

## 폰트 계획

현재 프로젝트에는 `Assets/Resource/Fonts/NotoSansKR-VariableFont_wght.ttf`가 있다.

- 일반 UI 텍스트: `NotoSansKR`
- 버튼 텍스트: `NotoSansKR`, weight 500~700 느낌
- 타이틀: 원본과 같은 장식 serif 계열을 찾기 어렵기 때문에 이미지 에셋으로 처리 권장
- 영문 로고/서브타이틀: 이미지 에셋 권장

UI Toolkit은 TextMeshPro SDF asset이 아니라 TTF/OTF 폰트를 직접 쓰는 쪽이 단순하다.

## 검증 방법

### Reference overlay

개발 중에만 `referenceOverlay`를 켠다.

- `referenceOverlay`에 원본 이미지를 `1536x1024`로 배치
- opacity `0.35`
- `F9` 키로 표시/숨김 토글
- 실제 UI가 원본 이미지와 얼마나 어긋나는지 바로 확인

최종 빌드 전에는 `referenceOverlay`를 비활성화하거나 개발 전용 define으로 감싼다.

### 스크린샷 비교 기준

| 항목 | 허용 오차 |
| --- | --- |
| 로그인 폼 x/y | 4px 이하 |
| 입력창 w/h | 3px 이하 |
| 로그인 버튼 x/y/w/h | 4px 이하 |
| 상단 메뉴 위치 | 6px 이하 |
| 외곽 프레임 | 2px 이하 |
| 주요 텍스트 색상 | 육안상 같은 톤 |
| hover/pressed 상태 | 원본 분위기를 해치지 않음 |

### 테스트 해상도

- [ ] `1536x1024`: 원본 기준 1:1
- [ ] `1920x1080`: 좌우 여백과 배경 확장 확인
- [ ] `2560x1440`: UI가 과도하게 커지지 않는지 확인
- [ ] `1280x720`: 텍스트와 버튼이 겹치지 않는지 확인
- [ ] Unity Editor Game View Free Aspect

## 구현 순서

### 1단계: 에셋 정리

1. 원본 이미지를 reference로 보관한다.
2. `bg_relogin_full.png`를 준비한다.
3. 로고, 타이틀, 버튼, 입력창, 소셜 버튼, 아이콘을 분리한다.
4. UI용 이미지는 Sprite로 import하고, mipmap을 끈다.
5. 9-slice가 필요한 이미지는 Sprite Editor에서 border를 설정한다.

### 2단계: UI Toolkit 뼈대 생성

1. `Assets/UI/ReLogin/ReLogin.uxml` 생성
2. `Assets/UI/ReLogin/ReLogin.uss` 생성
3. `ReLoginPanelSettings.asset` 생성
4. `Title.unity`에 `UIDocument` 연결
5. 빈 배경과 프레임만 먼저 표시

### 3단계: 픽셀 좌표 배치

1. `relogin-design-space`를 `1536x1024`로 고정
2. 배경, 프레임, 로고, 타이틀 배치
3. 로그인 폼 root를 `(840, 402)` 근처에 배치
4. 입력창, 체크박스, 버튼, 소셜 버튼 배치
5. reference overlay를 켜고 좌표 보정

### 4단계: 인터랙션 연결

1. `ReLoginUIToolkitController` 작성
2. TextField placeholder 처리
3. 비밀번호 보기 토글
4. remember toggle PlayerPrefs 저장
5. 로그인 버튼에서 `LoginUseCase.Execute()` 호출
6. 게스트 로그인 연결
7. 소셜 로그인은 미구현 상태 처리

### 5단계: 상태/오류/사운드

1. loading 상태에서 로그인 버튼 disabled
2. 실패 메시지 표시
3. 입력창 focus/invalid 스타일
4. 버튼 hover/click 사운드가 프로젝트에 있으면 연결
5. 네트워크 지연 상황 중복 클릭 방지

### 6단계: 해상도 대응

1. root size 변경 시 scale 재계산
2. letterbox 영역 배경 처리
3. 16:9와 3:2에서 스크린샷 비교
4. 텍스트 잘림 여부 확인

### 7단계: 최종 QA

1. 원본 overlay와 1:1 비교
2. 로그인 성공/실패 테스트
3. PlayerPrefs 저장/복원 테스트
4. 씬 전환 테스트
5. 빌드에서 이미지 경로 누락 없는지 확인

## 완료 기준

- `Title` 또는 지정된 재로그인 씬에서 UI Toolkit 화면이 정상 표시된다.
- 기준 해상도 `1536x1024`에서 원본 이미지와 주요 UI 위치가 거의 일치한다.
- 이메일/계정 입력, 비밀번호 입력, 비밀번호 보기, 로그인 유지 체크가 동작한다.
- 로그인 버튼이 기존 Auth 흐름과 연결된다.
- 게스트 로그인은 현재 LocalString 인증 흐름으로 동작한다.
- 미구현 소셜 로그인은 깨진 버튼이 아니라 명확한 disabled/준비중 상태로 보인다.
- `1920x1080`, `2560x1440`, `1280x720`에서 레이아웃이 깨지지 않는다.

## 주의점

- 원본 PNG를 그대로 배경으로 깔고 그 위에 UI를 다시 얹으면 버튼/텍스트가 이중으로 보인다. 최종 구현에는 UI 없는 배경 에셋이 필요하다.
- UI Toolkit의 기본 `Button`, `TextField`, `Toggle` 스타일은 원본과 많이 다르므로 기본 스타일을 대부분 제거하고 wrapper에 커스텀 이미지를 입힌다.
- 타이틀 글꼴은 텍스트로 재현하면 원본과 차이가 크게 보일 가능성이 높다. 타이틀과 로고는 이미지 에셋으로 처리하는 것이 좋다.
- 현재 Auth 구조는 이메일/비밀번호 로그인이 아니라 displayName 기반 로컬 로그인에 가깝다. UI는 이메일/비밀번호 형태로 만들되, 실제 인증 확장은 별도 단계로 잡아야 한다.
- UI Toolkit USS 경로는 Unity 버전에 따라 `project://database/...` 또는 Inspector 연결 방식이 더 안정적일 수 있다. 문제가 생기면 USS에서 직접 참조하기보다 `VisualTreeAsset`, `StyleSheet`, Sprite를 Inspector serialize field로 연결한다.
