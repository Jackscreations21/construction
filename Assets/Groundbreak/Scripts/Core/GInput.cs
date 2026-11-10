using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Groundbreak
{
    /// <summary>Input wrapper that works with either the new Input System or the legacy Input Manager.
    /// ALL gameplay code must read input through this class.</summary>
    public static class GInput
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        public static bool GetKey(KeyCode k) => Input.GetKey(k);
        public static bool GetKeyDown(KeyCode k) => Input.GetKeyDown(k);
        public static bool Mouse(int b) => Input.GetMouseButton(b);
        public static bool MouseDown(int b) => Input.GetMouseButtonDown(b);
        public static bool MouseUp(int b) => Input.GetMouseButtonUp(b);
        public static Vector2 MouseDelta => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
        /// <summary>+1 / -1 per scroll notch, 0 otherwise.</summary>
        public static float Scroll { get { float s = Input.mouseScrollDelta.y; return s > 0 ? 1 : s < 0 ? -1 : 0; } }
#else
        public static bool GetKey(KeyCode k) { var c = Map(k); return c != null && c.isPressed; }
        public static bool GetKeyDown(KeyCode k) { var c = Map(k); return c != null && c.wasPressedThisFrame; }
        static UnityEngine.InputSystem.Controls.ButtonControl MouseBtn(int b)
        {
            var m = UnityEngine.InputSystem.Mouse.current; if (m == null) return null;
            return b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton;
        }
        public static bool Mouse(int b) { var c = MouseBtn(b); return c != null && c.isPressed; }
        public static bool MouseDown(int b) { var c = MouseBtn(b); return c != null && c.wasPressedThisFrame; }
        public static bool MouseUp(int b) { var c = MouseBtn(b); return c != null && c.wasReleasedThisFrame; }
        public static Vector2 MouseDelta => UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.delta.ReadValue() : Vector2.zero;
        public static float Scroll
        {
            get
            {
                var m = UnityEngine.InputSystem.Mouse.current; if (m == null) return 0;
                float s = m.scroll.ReadValue().y; return s > 0 ? 1 : s < 0 ? -1 : 0;
            }
        }

        static UnityEngine.InputSystem.Controls.KeyControl Map(KeyCode k)
        {
            var kb = Keyboard.current; if (kb == null) return null;
            if (k >= KeyCode.A && k <= KeyCode.Z) return kb[(Key)((int)Key.A + (k - KeyCode.A))];
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return kb[(Key)((int)Key.Digit1 + (k - KeyCode.Alpha1))];
            switch (k)
            {
                case KeyCode.Alpha0: return kb.digit0Key;
                case KeyCode.Space: return kb.spaceKey;
                case KeyCode.Escape: return kb.escapeKey;
                case KeyCode.Tab: return kb.tabKey;
                case KeyCode.Return: return kb.enterKey;
                case KeyCode.Backspace: return kb.backspaceKey;
                case KeyCode.LeftShift: return kb.leftShiftKey;
                case KeyCode.RightShift: return kb.rightShiftKey;
                case KeyCode.LeftControl: return kb.leftCtrlKey;
                case KeyCode.RightControl: return kb.rightCtrlKey;
                case KeyCode.LeftAlt: return kb.leftAltKey;
                case KeyCode.RightAlt: return kb.rightAltKey;
                case KeyCode.LeftBracket: return kb.leftBracketKey;
                case KeyCode.RightBracket: return kb.rightBracketKey;
                case KeyCode.Minus: return kb.minusKey;
                case KeyCode.Equals: return kb.equalsKey;
                case KeyCode.UpArrow: return kb.upArrowKey;
                case KeyCode.DownArrow: return kb.downArrowKey;
                case KeyCode.LeftArrow: return kb.leftArrowKey;
                case KeyCode.RightArrow: return kb.rightArrowKey;
                default: return null;
            }
        }
#endif
        public static bool Shift => GetKey(KeyCode.LeftShift) || GetKey(KeyCode.RightShift);
        public static bool Ctrl => GetKey(KeyCode.LeftControl) || GetKey(KeyCode.RightControl);
        public static bool Alt => GetKey(KeyCode.LeftAlt) || GetKey(KeyCode.RightAlt);
    }
}
