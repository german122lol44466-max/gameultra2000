using System;
using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Командование отрядами с поля боя: 1–9 — выбрать отряд (Shift — добавить), ` — все;
    /// F — выдвинуться к точке прицела (дважды — «за мной»), T — атаковать, H — стоять, Y — оборонять;
    /// Q/E/X (удерживать) — радиальные меню строя/стойки/походки, V — правила огня.
    /// </summary>
    public class CommandInput : MonoBehaviour
    {
        public static CommandInput I { get; private set; }
        public readonly List<ushort> Selected = new List<ushort>();
        public event Action SelectionChanged;

        // радиальное меню (рисует BattleHud)
        public enum RadialKind { None, Formation, Stance, Gait }
        public RadialKind Radial { get; private set; }
        public int RadialHot { get; private set; } = -1;
        public string[] RadialItems { get; private set; } = new string[0];
        public int RadialCurrent { get; private set; } = -1;
        Vector2 radialVec;
        float lastF = -9f;
        BattleNet B => BattleNet.I;

        void Awake() => I = this;

        public IEnumerable<Squad> SelectedSquads
        {
            get
            {
                if (!B) yield break;
                foreach (var id in Selected) if (B.Squads.TryGetValue(id, out var s)) yield return s;
            }
        }

        public void Select(IEnumerable<ushort> ids, bool add = false)
        {
            if (!add) Selected.Clear();
            foreach (var id in ids) if (!Selected.Contains(id)) Selected.Add(id);
            SelectionChanged?.Invoke();
        }

        public void Toggle(ushort id)
        {
            if (!Selected.Remove(id)) Selected.Add(id);
            SelectionChanged?.Invoke();
        }

        void Update()
        {
            if (!B) return;
            // пропавшие отряды
            if (Selected.RemoveAll(id => !B.Squads.ContainsKey(id)) > 0) SelectionChanged?.Invoke();
            bool tactical = TacticalView.I && TacticalView.I.Active;
            if (CommanderController.Blocked && Radial == RadialKind.None && !tactical) return;
            if (Keys.Typing) return;
            var mine = B.MySquads.ToList();
            int d = Keys.DigitDown();
            if (d > 0 && d <= mine.Count)
            {
                ushort id = mine[d - 1].Id;
                if (Keys.Shift) Toggle(id);
                else Select(new[] { id });
                Audio.Ui("impact", 0.2f);
            }
            if (Keys.Down(KeyCode.BackQuote) || Keys.Down(KeyCode.Alpha0)) Select(mine.Select(s => s.Id));
            if (Selected.Count == 0 && mine.Count > 0 && (Keys.Down(KeyCode.F) || Keys.Down(KeyCode.T) || Keys.Down(KeyCode.H)))
                Select(mine.Select(s => s.Id));                        // без выбора — приказ всем

            var cc = B.LocalCommander ? B.LocalCommander.GetComponent<CommanderController>() : null;
            bool alive = cc && !cc.Self.Dead;
            if (alive && Radial == RadialKind.None && !CommanderController.Blocked)
            {
                if (Keys.Down(KeyCode.F))
                {
                    if (Time.time - lastF < 0.35f) Order(OrderType.Follow, cc.transform.position);
                    else Order(OrderType.Move, cc.AimPoint, cc.transform.eulerAngles.y);
                    lastF = Time.time;
                    cc.Self.DoAction("Point", 0.1f);
                }
                if (Keys.Down(KeyCode.T)) { Order(OrderType.Attack, cc.AimPoint); cc.Self.DoAction("Point", 0.1f); }
                if (Keys.Down(KeyCode.H)) { Order(OrderType.Hold, cc.transform.position); cc.Self.DoAction("Halt", 0.1f); }
                if (Keys.Down(KeyCode.Y)) Order(OrderType.Defend, cc.AimPoint, cc.transform.eulerAngles.y);
                if (Keys.Down(KeyCode.V)) CycleRoe();
            }
            // в RTS-режиме приказы с клавиатуры — в точку под курсором
            if (tactical && Radial == RadialKind.None && TacticalView.I.CursorValid && !UI.BattleHud.PointerOverUi)
            {
                var pnt = TacticalView.I.CursorPoint;
                if (Keys.Down(KeyCode.F)) Order(OrderType.Move, pnt);
                if (Keys.Down(KeyCode.T)) Order(OrderType.Attack, pnt);
                if (Keys.Down(KeyCode.Y)) Order(OrderType.Defend, pnt);
                if (Keys.Down(KeyCode.H)) Order(OrderType.Hold, SelectedSquads.Any() ? SelectedSquads.First().Center : pnt);
                if (Keys.Down(KeyCode.G) && B.LocalCommander && !B.LocalCommander.Dead) Order(OrderType.Follow, B.LocalCommander.transform.position);
                if (Keys.Down(KeyCode.V)) CycleRoe();
            }
            UpdateRadial();
        }

        public void Order(OrderType o, Vector3 point, float yaw = float.NaN)
        {
            if (Selected.Count == 0) return;
            int post = -1;
            // приказ рядом с командным пунктом — захват
            if (o == OrderType.Move || o == OrderType.Attack)
            {
                var cp = CommandPost.All.OrderBy(c => (c.transform.position - point).sqrMagnitude).FirstOrDefault();
                if (cp && (cp.transform.position - point).sqrMagnitude < cp.Radius * cp.Radius && !cp.OwnedBy(B.LocalTeam))
                {
                    o = OrderType.Capture;
                    post = cp.Id;
                }
            }
            B.SendOrder(Selected, o, point, yaw, post);
            OrderMarker.Show(point, o);
            B.ShowNotice($"{SquadNames()}: {Names.Of(o).ToLower()}", new Color(0.85f, 0.9f, 1f));
        }

        string SquadNames()
        {
            var names = SelectedSquads.Select(s => s.Name).ToList();
            return names.Count > 2 ? $"{names.Count} отряда(ов)" : string.Join(", ", names);
        }

        void CycleRoe()
        {
            var list = SelectedSquads.ToList();
            if (list.Count == 0) return;
            var next = (Roe)(((int)list[0].Roe + 1) % 3);
            foreach (var s in list) { s.Roe = next; B.SendSquadConfig(s); }
            B.ShowNotice($"Правила огня: {Names.Of(next)}", new Color(1f, 0.8f, 0.3f));
        }

        // ------------------------------------------------------------------ радиальные меню
        void UpdateRadial()
        {
            if (Radial == RadialKind.None)
            {
                if (CommanderController.Blocked || SelectedSquads.Count() == 0 && B.MySquads.Count() == 0) return;
                RadialKind open = Keys.Down(KeyCode.Q) ? RadialKind.Formation : Keys.Down(KeyCode.E) ? RadialKind.Stance : Keys.Down(KeyCode.X) ? RadialKind.Gait : RadialKind.None;
                if (open == RadialKind.None) return;
                if (Selected.Count == 0) Select(B.MySquads.Select(s => s.Id));
                Radial = open;
                RadialItems = open == RadialKind.Formation ? Names.Formation : open == RadialKind.Stance ? Names.Stance : Names.Gait;
                var first = SelectedSquads.FirstOrDefault();
                RadialCurrent = first == null ? -1 : open == RadialKind.Formation ? (int)first.Formation : open == RadialKind.Stance ? (int)first.Stance : (int)first.Gait;
                radialVec = Vector2.zero;
                RadialHot = -1;
                CommanderController.Blocked = true;
                return;
            }
            radialVec += Keys.Look * 6f;
            if (radialVec.magnitude > 120f) radialVec = radialVec.normalized * 120f;
            int n = RadialItems.Length;
            if (radialVec.magnitude > 30f)
            {
                float a = Mathf.Atan2(radialVec.x, radialVec.y) * Mathf.Rad2Deg;
                if (a < 0) a += 360f;
                RadialHot = Mathf.RoundToInt(a / (360f / n)) % n;
            }
            KeyCode key = Radial == RadialKind.Formation ? KeyCode.Q : Radial == RadialKind.Stance ? KeyCode.E : KeyCode.X;
            if (Keys.Up(key) || Keys.MouseDown(0))
            {
                if (RadialHot >= 0) Apply(Radial, RadialHot);
                Radial = RadialKind.None;
                CommanderController.Blocked = false;
            }
            if (Keys.MouseDown(1) || Keys.Down(KeyCode.Escape))
            {
                Radial = RadialKind.None;
                CommanderController.Blocked = false;
            }
        }

        public void Apply(RadialKind kind, int value)
        {
            foreach (var s in SelectedSquads.ToList())
            {
                switch (kind)
                {
                    case RadialKind.Formation: s.SetFormation((FormationType)value); break;
                    case RadialKind.Stance: s.Stance = (Stance)value; break;
                    case RadialKind.Gait: s.Gait = (Gait)value; break;
                }
                B.SendSquadConfig(s);
            }
            string what = kind == RadialKind.Formation ? "Строй" : kind == RadialKind.Stance ? "Стойка" : "Походка";
            B.ShowNotice($"{what}: {RadialItems[value]}", new Color(0.85f, 0.9f, 1f));
        }
    }

    /// <summary>Метка приказа на земле (кольцо, гаснет).</summary>
    public class OrderMarker : MonoBehaviour
    {
        float born;
        LineRenderer lr;
        static Material mat;

        public static void Show(Vector3 p, OrderType o)
        {
            var go = new GameObject("OrderMarker");
            go.transform.position = new Vector3(p.x, MapInfo.GroundHeight(p) + 0.15f, p.z);
            var m = go.AddComponent<OrderMarker>();
            m.born = Time.time;
            m.lr = go.AddComponent<LineRenderer>();
            m.lr.useWorldSpace = false;
            m.lr.loop = true;
            m.lr.positionCount = 32;
            m.lr.widthMultiplier = 0.18f;
            mat ??= new Material(Shader.Find("Sprites/Default"));
            m.lr.sharedMaterial = mat;
            var c = o == OrderType.Attack ? new Color(1f, 0.35f, 0.25f) : o == OrderType.Capture ? new Color(1f, 0.85f, 0.3f) : new Color(0.4f, 0.75f, 1f);
            m.lr.startColor = m.lr.endColor = c;
            for (int i = 0; i < 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                m.lr.SetPosition(i, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.5f);
            }
            Destroy(go, 2.5f);
        }

        void Update()
        {
            float t = (Time.time - born) / 2.5f;
            transform.localScale = Vector3.one * (1f + t * 0.6f);
            var c = lr.startColor;
            c.a = 1f - t;
            lr.startColor = lr.endColor = c;
        }
    }
}
