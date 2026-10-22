using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace SW.Commander
{
    /// <summary>Клавиатура и мышь: работает и со старым Input Manager, и с новым Input System.</summary>
    public static class Keys
    {
        public static float Sensitivity = 1f;
        public static bool InvertY;
        /// <summary>Ввод занят интерфейсом (чат, поле имени) — игровые клавиши не реагируют.</summary>
        public static bool Typing;

#if ENABLE_INPUT_SYSTEM
        static Key Map(KeyCode k)
        {
            if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return Key.Digit1 + (k - KeyCode.Alpha1);
            if (k >= KeyCode.F1 && k <= KeyCode.F12) return Key.F1 + (k - KeyCode.F1);
            switch (k)
            {
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
            }
            return Key.None;
        }

        static ButtonControl Mb(int b)
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return null;
            return b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton;
        }
#endif

        public static bool Held(KeyCode k)
        {
            if (Typing) return false;
            return Raw(k);
        }

        public static bool Raw(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var key = Map(k);
            return kb != null && key != Key.None && kb[key].isPressed;
#else
            return Input.GetKey(k);
#endif
        }

        public static bool Down(KeyCode k)
        {
            if (Typing && k != KeyCode.Escape && k != KeyCode.Return && k != KeyCode.KeypadEnter) return false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var key = Map(k);
            return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(k);
#endif
        }

        public static bool Up(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var key = Map(k);
            return kb != null && key != Key.None && kb[key].wasReleasedThisFrame;
#else
            return Input.GetKeyUp(k);
#endif
        }

        public static bool Mouse(int b)
        {
#if ENABLE_INPUT_SYSTEM
            var c = Mb(b);
            return c != null && c.isPressed;
#else
            return Input.GetMouseButton(b);
#endif
        }

        public static bool MouseDown(int b)
        {
#if ENABLE_INPUT_SYSTEM
            var c = Mb(b);
            return c != null && c.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(b);
#endif
        }

        public static bool MouseUp(int b)
        {
#if ENABLE_INPUT_SYSTEM
            var c = Mb(b);
            return c != null && c.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(b);
#endif
        }

        /// <summary>Смещение мыши за кадр (градусы обзора) с учётом чувствительности.</summary>
        public static Vector2 Look
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var v = UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.delta.ReadValue() * 0.08f : Vector2.zero;
#else
                var v = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 2f;
#endif
                v *= Sensitivity;
                if (InvertY) v.y = -v.y;
                return v;
            }
        }

        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() : Vector2.zero;
#else
                return Input.mousePosition;
#endif
            }
        }

        public static float Scroll
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }

        public static Vector2 Move
        {
            get
            {
                float x = (Held(KeyCode.D) ? 1 : 0) - (Held(KeyCode.A) ? 1 : 0);
                float y = (Held(KeyCode.W) ? 1 : 0) - (Held(KeyCode.S) ? 1 : 0);
                var v = new Vector2(x, y);
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        public static bool Shift => Raw(KeyCode.LeftShift) || Raw(KeyCode.RightShift);
        public static bool Ctrl => Raw(KeyCode.LeftControl) || Raw(KeyCode.RightControl);

        /// <summary>Нажатая цифра 1..9 в этом кадре (0 — нет).</summary>
        public static int DigitDown()
        {
            for (int i = 1; i <= 9; i++) if (Down(KeyCode.Alpha0 + i)) return i;
            return 0;
        }
    }
}
