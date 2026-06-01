using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System.Collections.Generic;

public static class MdfInput
{
    private static readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>(16);

    public static Vector2 PointerPosition
    {
        get
        {
            var touch = Touchscreen.current;
            if (touch != null && IsTouchActiveThisFrame(touch.primaryTouch))
            {
                return touch.primaryTouch.position.ReadValue();
            }

            if (Pointer.current != null)
            {
                return Pointer.current.position.ReadValue();
            }

            var mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }
    }

    public static bool PrimaryPointerWasPressedThisFrame()
    {
        return WasPressedThisFrame(Mouse.current?.leftButton)
               || WasPressedThisFrame(Touchscreen.current?.primaryTouch.press);
    }

    public static bool PrimaryPointerIsPressed()
    {
        return IsPressed(Mouse.current?.leftButton)
               || IsPressed(Touchscreen.current?.primaryTouch.press);
    }

    public static bool PrimaryPointerWasReleasedThisFrame()
    {
        return WasReleasedThisFrame(Mouse.current?.leftButton)
               || WasReleasedThisFrame(Touchscreen.current?.primaryTouch.press);
    }

    public static bool SecondaryPointerWasPressedThisFrame()
    {
        return WasPressedThisFrame(Mouse.current?.rightButton);
    }

    public static bool IsPointerOverUI()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        Vector2 pointerPosition = PointerPosition;
        bool gamePrepareToolkitBlocks = GamePrepareUIToolkitController.IsPointerOverBlockingElement(pointerPosition);
        bool hasNonToolkitUiHit = HasNonGamePrepareToolkitUiHit(eventSystem, pointerPosition);
        if (hasNonToolkitUiHit || gamePrepareToolkitBlocks)
        {
            return true;
        }

        var mouse = Mouse.current;
        if (mouse != null
            && (eventSystem.IsPointerOverGameObject()
                || eventSystem.IsPointerOverGameObject(mouse.deviceId)))
        {
            return !GamePrepareUIToolkitController.IsToolkitActive;
        }

        var touch = Touchscreen.current;
        if (touch != null)
        {
            int touchId = touch.primaryTouch.touchId.ReadValue();
            bool touchOverUi = (touchId != 0 && eventSystem.IsPointerOverGameObject(touchId))
                               || eventSystem.IsPointerOverGameObject(touch.deviceId);
            return touchOverUi && !GamePrepareUIToolkitController.IsToolkitActive;
        }

        return eventSystem.IsPointerOverGameObject() && !GamePrepareUIToolkitController.IsToolkitActive;
    }

    public static bool IsPointerOverFieldBlockingUI()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        Vector2 pointerPosition = PointerPosition;
        if (GamePrepareUIToolkitController.IsPointerOverBlockingElement(pointerPosition))
        {
            return true;
        }

        return HasFieldBlockingUiHit(eventSystem, pointerPosition);
    }

    public static string DescribeFieldBlockingUiHits()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return "eventSystem=null";
        }

        Vector2 pointerPosition = PointerPosition;
        var pointerData = new PointerEventData(eventSystem)
        {
            position = pointerPosition
        };

        uiRaycastResults.Clear();
        eventSystem.RaycastAll(pointerData, uiRaycastResults);

        var parts = new List<string>(uiRaycastResults.Count + 1)
        {
            $"pointer={pointerPosition} prepareToolkitBlocks={GamePrepareUIToolkitController.IsPointerOverBlockingElement(pointerPosition)} rankingBlocks={RankingUIController.IsPointerOverBlockingElement(pointerPosition)} hits={uiRaycastResults.Count}"
        };

        for (int i = 0; i < uiRaycastResults.Count; i++)
        {
            var result = uiRaycastResults[i];
            var target = result.gameObject;
            if (target == null)
            {
                parts.Add($"{i}:null");
                continue;
            }

            bool prepareToolkit = GamePrepareUIToolkitController.IsToolkitRaycastObject(target);
            bool passthrough = IsFieldPassthroughUi(target, pointerPosition);
            bool blocks = !prepareToolkit && !passthrough;
            string module = result.module != null ? result.module.GetType().Name : "none";
            parts.Add($"{i}:{target.name}:blocks={blocks}:prepareToolkit={prepareToolkit}:passthrough={passthrough}:module={module}");
        }

        return string.Join(" | ", parts);
#else
        return string.Empty;
#endif
    }

    private static bool HasNonGamePrepareToolkitUiHit(EventSystem eventSystem, Vector2 pointerPosition)
    {
        var pointerData = new PointerEventData(eventSystem)
        {
            position = pointerPosition
        };

        uiRaycastResults.Clear();
        eventSystem.RaycastAll(pointerData, uiRaycastResults);

        for (int i = 0; i < uiRaycastResults.Count; i++)
        {
            var result = uiRaycastResults[i];
            if (result.gameObject == null)
            {
                continue;
            }

            if (!GamePrepareUIToolkitController.IsToolkitRaycastObject(result.gameObject))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFieldBlockingUiHit(EventSystem eventSystem, Vector2 pointerPosition)
    {
        var pointerData = new PointerEventData(eventSystem)
        {
            position = pointerPosition
        };

        uiRaycastResults.Clear();
        eventSystem.RaycastAll(pointerData, uiRaycastResults);

        for (int i = 0; i < uiRaycastResults.Count; i++)
        {
            var result = uiRaycastResults[i];
            if (result.gameObject == null)
            {
                continue;
            }

            if (GamePrepareUIToolkitController.IsToolkitRaycastObject(result.gameObject) ||
                IsFieldPassthroughUi(result.gameObject, pointerPosition))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsFieldPassthroughUi(GameObject target, Vector2 pointerPosition)
    {
        if (target == null)
        {
            return false;
        }

        if (target.GetComponentInParent<StatusBarUI>() != null)
        {
            return true;
        }

        if (target.GetComponentInParent<RankingUIController>() != null)
        {
            return !RankingUIController.IsPointerOverBlockingElement(pointerPosition);
        }

        return false;
    }

    public static bool GetKeyDown(KeyCode keyCode)
    {
        return TryGetKeyControl(keyCode, out KeyControl key) && key.wasPressedThisFrame;
    }

    public static bool GetKey(KeyCode keyCode)
    {
        return TryGetKeyControl(keyCode, out KeyControl key) && key.isPressed;
    }

    private static bool TryGetKeyControl(KeyCode keyCode, out KeyControl keyControl)
    {
        keyControl = null;
        var keyboard = Keyboard.current;
        if (keyboard == null || !TryMapKeyCode(keyCode, out Key key))
        {
            return false;
        }

        keyControl = keyboard[key];
        return keyControl != null;
    }

    private static bool TryMapKeyCode(KeyCode keyCode, out Key key)
    {
        switch (keyCode)
        {
            case KeyCode.Space:
                key = Key.Space;
                return true;
            case KeyCode.C:
                key = Key.C;
                return true;
            case KeyCode.K:
                key = Key.K;
                return true;
            case KeyCode.F8:
                key = Key.F8;
                return true;
            case KeyCode.LeftControl:
                key = Key.LeftCtrl;
                return true;
            case KeyCode.RightControl:
                key = Key.RightCtrl;
                return true;
            case KeyCode.LeftCommand:
                key = Key.LeftMeta;
                return true;
            case KeyCode.RightCommand:
                key = Key.RightMeta;
                return true;
            case KeyCode.Alpha0:
                key = Key.Digit0;
                return true;
            case KeyCode.Alpha1:
                key = Key.Digit1;
                return true;
            case KeyCode.Alpha2:
                key = Key.Digit2;
                return true;
            case KeyCode.Alpha3:
                key = Key.Digit3;
                return true;
            case KeyCode.Alpha4:
                key = Key.Digit4;
                return true;
            case KeyCode.Alpha5:
                key = Key.Digit5;
                return true;
            case KeyCode.Alpha6:
                key = Key.Digit6;
                return true;
            case KeyCode.Alpha7:
                key = Key.Digit7;
                return true;
            case KeyCode.Alpha8:
                key = Key.Digit8;
                return true;
            case KeyCode.Alpha9:
                key = Key.Digit9;
                return true;
            default:
                key = Key.None;
                return false;
        }
    }

    private static bool IsTouchActiveThisFrame(TouchControl touch)
    {
        return touch != null
               && (touch.press.isPressed
                   || touch.press.wasPressedThisFrame
                   || touch.press.wasReleasedThisFrame);
    }

    private static bool WasPressedThisFrame(ButtonControl button)
    {
        return button != null && button.wasPressedThisFrame;
    }

    private static bool IsPressed(ButtonControl button)
    {
        return button != null && button.isPressed;
    }

    private static bool WasReleasedThisFrame(ButtonControl button)
    {
        return button != null && button.wasReleasedThisFrame;
    }
}
