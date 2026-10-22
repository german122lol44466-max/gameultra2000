using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SW.Commander.UI
{
    /// <summary>Сборка интерфейса кодом (UI Toolkit): документ с общим стилем и короткие конструкторы элементов.</summary>
    public static class Ui
    {
        static PanelSettings panel;
        static StyleSheet style;

        public static PanelSettings Panel
        {
            get
            {
                if (panel) return panel;
                panel = Resources.Load<PanelSettings>("Commander/UI/PanelSettings");
                if (!panel)
                {
                    panel = ScriptableObject.CreateInstance<PanelSettings>();
                    panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Commander/UI/Runtime");
                    panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    panel.referenceResolution = new Vector2Int(1920, 1080);
                    panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                    panel.match = 0.5f;
                }
                return panel;
            }
        }

        public static StyleSheet Style => style ? style : style = Resources.Load<StyleSheet>("Commander/UI/Commander");

        /// <summary>Новый документ UI (корень со стилем). sort — порядок слоёв (больше — выше).</summary>
        public static VisualElement Document(GameObject host, int sort = 0)
        {
            var doc = host.GetComponent<UIDocument>();
            if (!doc) doc = host.AddComponent<UIDocument>();
            doc.panelSettings = Panel;
            doc.sortingOrder = sort;
            var root = doc.rootVisualElement;
            root.Clear();
            if (Style) root.styleSheets.Add(Style);
            var r = new VisualElement();
            r.AddToClassList("root");
            r.pickingMode = PickingMode.Ignore;
            root.Add(r);
            return r;
        }

        public static T Add<T>(VisualElement parent, T e, params string[] classes) where T : VisualElement
        {
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        public static VisualElement Box(VisualElement parent, params string[] classes) => Add(parent, new VisualElement(), classes);

        public static VisualElement Row(VisualElement parent, params string[] classes)
        {
            var e = Box(parent, classes);
            e.AddToClassList("row");
            return e;
        }

        public static VisualElement Col(VisualElement parent, params string[] classes)
        {
            var e = Box(parent, classes);
            e.AddToClassList("col");
            return e;
        }

        public static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = Add(parent, new Label(text), classes);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static Button Button(VisualElement parent, string text, Action onClick, params string[] classes)
        {
            var b = new Button(() =>
            {
                Click();
                onClick?.Invoke();
            }) { text = text };
            if (classes.Length == 0) b.AddToClassList("btn");
            foreach (var c in classes) b.AddToClassList(c);
            b.RegisterCallback<MouseEnterEvent>(_ => Hover());
            parent?.Add(b);
            return b;
        }

        public static Button MenuItem(VisualElement parent, string text, Action onClick, string hint = null)
        {
            var b = Button(parent, text, onClick, "menu-item");
            if (!string.IsNullOrEmpty(hint)) Text(parent, hint, "menu-hint");
            return b;
        }

        public static TextField Field(VisualElement parent, string label, string value, int max = 64)
        {
            if (!string.IsNullOrEmpty(label)) Text(parent, label, "field-label");
            var f = new TextField { value = value ?? "", maxLength = max };
            f.RegisterCallback<FocusInEvent>(_ => Keys.Typing = true);
            f.RegisterCallback<FocusOutEvent>(_ => Keys.Typing = false);
            parent?.Add(f);
            return f;
        }

        /// <summary>Переключатель вариантов «‹ значение ›».</summary>
        public static VisualElement Choice(VisualElement parent, string label, string[] options, int value, Action<int> changed)
        {
            var wrap = Col(parent);
            Text(wrap, label, "field-label");
            var row = Row(wrap);
            int cur = Mathf.Clamp(value, 0, options.Length - 1);
            Label val = null;
            Button(row, "‹", () => { cur = (cur + options.Length - 1) % options.Length; val.text = options[cur]; changed?.Invoke(cur); }, "btn", "btn-small");
            val = Text(row, options[cur], "h2");
            val.style.minWidth = 230;
            val.style.unityTextAlign = TextAnchor.MiddleCenter;
            Button(row, "›", () => { cur = (cur + 1) % options.Length; val.text = options[cur]; changed?.Invoke(cur); }, "btn", "btn-small");
            return wrap;
        }

        public static Slider SliderField(VisualElement parent, string label, float min, float max, float value, Action<float> changed, Func<float, string> format = null)
        {
            var wrap = Col(parent);
            var head = Row(wrap);
            Text(head, label, "field-label");
            Box(head, "spacer");
            var v = Text(head, format != null ? format(value) : value.ToString("0.00"), "field-label");
            var s = new Slider(min, max) { value = value };
            s.RegisterValueChangedCallback(e =>
            {
                v.text = format != null ? format(e.newValue) : e.newValue.ToString("0.00");
                changed?.Invoke(e.newValue);
            });
            wrap.Add(s);
            return s;
        }

        public static Toggle ToggleField(VisualElement parent, string label, bool value, Action<bool> changed)
        {
            var t = new Toggle(label) { value = value };
            t.RegisterValueChangedCallback(e => changed?.Invoke(e.newValue));
            parent?.Add(t);
            return t;
        }

        public static void Show(VisualElement e, bool show)
        {
            if (e == null) return;
            if (show) e.RemoveFromClassList("hidden"); else e.AddToClassList("hidden");
        }

        public static void Hint(VisualElement bar, string key, string text)
        {
            Text(bar, key, "hint-key");
            Text(bar, text, "hint-text");
        }

        // звуки интерфейса (из процедурных SW_Audio)
        public static void Click() => Audio.Ui("impact", 0.25f);
        public static void Hover() { }
    }
}
