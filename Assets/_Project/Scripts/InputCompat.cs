using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SW
{
    /// <summary>Клавиатура и мышь — работает и со старым Input Manager, и с новым Input System.</summary>
    public static class InputCompat
    {
#if ENABLE_INPUT_SYSTEM
        static Key Map(KeyCode k)
        {
            if (k >= KeyCode.A && k <= KeyCode.Z) return Key.A + (k - KeyCode.A);
            if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) return Key.Digit1 + (k - KeyCode.Alpha1);
            switch (k)
            {
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.F1: return Key.F1;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
            }
            return Key.None;
        }
#endif

        public static bool Held(KeyCode k)
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
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var key = Map(k);
            return kb != null && key != Key.None && kb[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(k);
#endif
        }

        public static bool RightMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
                return Input.GetMouseButton(1);
#endif
            }
        }

        /// <summary>Смещение мыши за кадр (в условных «градусах»).</summary>
        public static Vector2 MouseDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
#else
                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
#endif
            }
        }

        public static float Scroll
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }
    }
}
