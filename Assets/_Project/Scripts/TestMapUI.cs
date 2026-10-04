using UnityEngine;

namespace SW
{
    /// <summary>
    /// Панель тестовой карты: список персонажей (клик — камера к персонажу), кнопки клипов,
    /// подписи над головами. Клавиши: Tab — следующий персонаж, ←/→ — клип, Пробел — автосмена клипов,
    /// 1..9 — клип по номеру, F1 — скрыть/показать панель.
    /// </summary>
    public class TestMapUI : MonoBehaviour
    {
        public FlyCamera Cam;
        ShowcaseCharacter[] chars = new ShowcaseCharacter[0];
        int selected = -1;
        bool show = true;
        bool autoAll = true;
        Vector2 scroll;
        GUIStyle label, title, small;

        void Start()
        {
            chars = FindObjectsByType<ShowcaseCharacter>(FindObjectsSortMode.None);
            System.Array.Sort(chars, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x) +
                                               2 * b.transform.position.z.CompareTo(a.transform.position.z));
            if (Cam == null) Cam = FindFirstObjectByType<FlyCamera>();
        }

        void Select(int i)
        {
            if (chars.Length == 0) return;
            selected = (i % chars.Length + chars.Length) % chars.Length;
            if (Cam != null) Cam.Focus(chars[selected].transform.position, 4.2f * chars[selected].transform.lossyScale.y);
        }

        void Update()
        {
            if (InputCompat.Down(KeyCode.F1)) show = !show;
            if (InputCompat.Down(KeyCode.Tab)) Select(selected + 1);
            if (InputCompat.Down(KeyCode.Space))
            {
                autoAll = !autoAll;
                foreach (var c in chars) c.AutoCycle = autoAll;
            }
            if (selected >= 0 && selected < chars.Length)
            {
                var c = chars[selected];
                if (InputCompat.Down(KeyCode.RightArrow)) { c.AutoCycle = false; c.Next(); }
                if (InputCompat.Down(KeyCode.LeftArrow)) { c.AutoCycle = false; c.Prev(); }
                for (int k = 0; k < 9; k++)
                    if (InputCompat.Down(KeyCode.Alpha1 + k) && k < c.Clips.Length) { c.AutoCycle = false; c.Play(k); }
            }
        }

        void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            label.normal.textColor = new Color(0.95f, 0.95f, 1f);
            small = new GUIStyle(label) { fontSize = 12, fontStyle = FontStyle.Normal };
            small.normal.textColor = new Color(1f, 0.8f, 0.35f);
            title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(1f, 0.85f, 0.2f);
        }

        void OnGUI()
        {
            Styles();
            var cam = Camera.main;
            // подписи над персонажами
            if (cam != null)
            {
                foreach (var c in chars)
                {
                    Vector3 head = c.transform.position + Vector3.up * (2.25f * c.transform.lossyScale.y);
                    Vector3 sp = cam.WorldToScreenPoint(head);
                    if (sp.z < 0.5f || sp.z > 40f) continue;
                    float y = Screen.height - sp.y;
                    GUI.Label(new Rect(sp.x - 150, y - 22, 300, 22), c.Title, label);
                    GUI.Label(new Rect(sp.x - 150, y - 4, 300, 20), c.CurrentClip, small);
                }
            }
            if (!show) return;
            GUILayout.BeginArea(new Rect(12, 12, 300, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("Тестовая карта", title);
            GUILayout.Label("ПКМ+мышь — обзор, WASD/QE — полёт, Shift — быстрее\nTab — следующий, ←/→ или 1-9 — клип\nПробел — автосмена клипов, F1 — панель");
            autoAll = GUILayout.Toggle(autoAll, " Автосмена клипов у всех");
            if (GUI.changed)
                foreach (var c in chars) c.AutoCycle = autoAll;
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (GUILayout.Button((i == selected ? "▶ " : "") + c.Title)) Select(i);
                if (i == selected)
                {
                    for (int k = 0; k < c.Clips.Length; k++)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(16);
                        string mark = k == c.CurrentIndex ? "● " : "";
                        if (GUILayout.Button($"{mark}{k + 1}. {c.Clips[k]}", GUILayout.Height(20))) { c.AutoCycle = false; c.Play(k); }
                        GUILayout.EndHorizontal();
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
