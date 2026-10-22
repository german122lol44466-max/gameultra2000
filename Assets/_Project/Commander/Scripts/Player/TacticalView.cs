using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Основная камера игры — RTS: WASD или мышь у края экрана — сдвиг, колесо — приближение (от 12 до 320 м,
    /// вблизи камера опускается к горизонту), Q/E или средняя кнопка — поворот, Пробел — к выбранным отрядам,
    /// Home — к командиру. ЛКМ — выбрать отряд (рамкой — несколько, Shift — добавить), ПКМ — выдвинуться
    /// (протянуть — направление строя), ПКМ по врагу — атака, по командному пункту — захват, Ctrl+ПКМ — оборона.
    /// Tab — вселиться в командира (вид от третьего лица) и обратно.
    /// </summary>
    public class TacticalView : MonoBehaviour
    {
        public static TacticalView I { get; private set; }
        public bool Active { get; private set; }
        public bool Dragging => dragging;
        public Rect DragRect { get; private set; }
        public Vector3? OrderStart => rmbDown ? orderStart : (Vector3?)null;
        public Vector3 OrderEnd { get; private set; }

        Vector2 dragFrom;
        bool dragging, rmbDown;
        Vector3 orderStart;
        LineRenderer arrow;
        CommanderCamera.Mode prevMode;

        bool autoEntered;
        float wantHeight = 70f;
        /// <summary>Точка земли под курсором (для приказов с клавиатуры).</summary>
        public Vector3 CursorPoint { get; private set; }
        public bool CursorValid { get; private set; }

        void Awake() => I = this;

        public void FocusOn(Vector3 p)
        {
            var cam = CommanderCamera.I;
            if (cam) cam.TacticalFocus = new Vector3(p.x, cam.TacticalFocus.y, p.z);
        }

        public void Toggle()
        {
            if (Active) Exit(); else Enter();
        }

        public void Enter()
        {
            var cam = CommanderCamera.Ensure();
            Active = true;
            prevMode = cam.Current == CommanderCamera.Mode.Tactical ? CommanderCamera.Mode.Follow : cam.Current;
            var b = BattleNet.I;
            Vector3 focus = b && b.LocalCommander ? b.LocalCommander.transform.position : Vector3.zero;
            // на старте — над своей армией
            var mine = b ? b.MySquads.Where(s => s.Members.Count > 0).ToList() : new List<Squad>();
            if (!autoEntered && mine.Count > 0) focus = mine.Aggregate(Vector3.zero, (a, s) => a + s.Center) / mine.Count;
            cam.TacticalFocus = focus;
            cam.TacticalYaw = b && b.LocalTeam == Team.Empire ? 180f : 0f;
            if (autoEntered) cam.TacticalYaw = cam.Yaw;
            else if (b) b.ShowNotice("ЛКМ — выбрать отряд · ПКМ — отдать приказ (протяните — задать направление строя)", new Color(0.85f, 0.9f, 1f));
            autoEntered = true;
            cam.Current = CommanderCamera.Mode.Tactical;
            CommanderController.Blocked = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Exit()
        {
            Active = false;
            var cam = CommanderCamera.Ensure();
            var b = BattleNet.I;
            cam.Current = prevMode;
            if (b && b.LocalCommander && !b.LocalCommander.Dead) cam.FollowNow();
            CommanderController.Blocked = false;
            dragging = rmbDown = false;
            if (arrow) arrow.enabled = false;
        }

        void Update()
        {
            // RTS — основной режим: включаем, как только появился свой командир
            var bn = BattleNet.I;
            if (!autoEntered && !Active && bn && bn.LocalCommander && Net.NetSession.Instance && Net.NetSession.Instance.Phase == Net.SessionPhase.InGame)
            {
                Enter();
                wantHeight = 70f;
            }
            if (!Active) return;
            var cam = CommanderCamera.I;
            if (!cam) return;
            float dt = Time.unscaledDeltaTime;
            // камера: клавиши и край экрана
            var mv = new Vector2((Keys.Raw(KeyCode.D) || Keys.Raw(KeyCode.RightArrow) ? 1 : 0) - (Keys.Raw(KeyCode.A) || Keys.Raw(KeyCode.LeftArrow) ? 1 : 0),
                                 (Keys.Raw(KeyCode.W) || Keys.Raw(KeyCode.UpArrow) ? 1 : 0) - (Keys.Raw(KeyCode.S) || Keys.Raw(KeyCode.DownArrow) ? 1 : 0));
            if (Keys.Typing) mv = Vector2.zero;
            Vector2 mpos = Keys.MousePosition;
            const float edge = 10f;
            bool inside = mpos.x >= 0 && mpos.y >= 0 && mpos.x <= Screen.width && mpos.y <= Screen.height && Application.isFocused;
            if (inside && !dragging && !rmbDown && !UI.BattleHud.PointerOverUi)
            {
                if (mpos.x < edge) mv.x -= 1; else if (mpos.x > Screen.width - edge) mv.x += 1;
                if (mpos.y < edge) mv.y -= 1; else if (mpos.y > Screen.height - edge) mv.y += 1;
            }
            mv = Vector2.ClampMagnitude(mv, 1f);
            var rot = Quaternion.Euler(0, cam.TacticalYaw, 0);
            float speed = Mathf.Lerp(14f, 260f, Mathf.InverseLerp(12f, 300f, cam.TacticalHeight)) * (Keys.Shift ? 2.2f : 1f);
            cam.TacticalFocus += rot * new Vector3(mv.x, 0, mv.y) * speed * dt;
            if (Keys.Down(KeyCode.Space) && !Keys.Typing)
            {
                var sel = CommandInput.I ? CommandInput.I.SelectedSquads.Where(s => s.Members.Count > 0).ToList() : new List<Squad>();
                if (sel.Count > 0) FocusOn(sel.Aggregate(Vector3.zero, (a, s) => a + s.Center) / sel.Count);
            }
            if (Keys.Down(KeyCode.Home) && bn && bn.LocalCommander) FocusOn(bn.LocalCommander.transform.position);
            var map = MapInfo.I;
            if (map)
            {
                var r = map.Bounds;
                cam.TacticalFocus.x = Mathf.Clamp(cam.TacticalFocus.x, r.xMin, r.xMax);
                cam.TacticalFocus.z = Mathf.Clamp(cam.TacticalFocus.z, r.yMin, r.yMax);
            }
            if (!UI.BattleHud.PointerOverUi) wantHeight = Mathf.Clamp(wantHeight * (1f - Keys.Scroll * 0.15f), 12f, 320f);
            cam.TacticalHeight = Mathf.Lerp(cam.TacticalHeight, wantHeight, 1f - Mathf.Exp(-dt * 10f));
            if (!Keys.Typing)
            {
                if (Keys.Raw(KeyCode.Q)) cam.TacticalYaw -= 70f * dt;
                if (Keys.Raw(KeyCode.E)) cam.TacticalYaw += 70f * dt;
            }
            if (Keys.Mouse(2)) cam.TacticalYaw += Keys.Look.x * 1.5f;

            Vector2 mp = Keys.MousePosition;
            CursorValid = Ground(mp, out var cpnt);
            if (CursorValid) CursorPoint = cpnt;
            if (UI.BattleHud.PointerOverUi) return;
            // выбор
            if (Keys.MouseDown(0)) { dragFrom = mp; dragging = true; }
            if (dragging)
            {
                DragRect = Rect.MinMaxRect(Mathf.Min(dragFrom.x, mp.x), Mathf.Min(dragFrom.y, mp.y), Mathf.Max(dragFrom.x, mp.x), Mathf.Max(dragFrom.y, mp.y));
                if (Keys.MouseUp(0))
                {
                    dragging = false;
                    if (DragRect.width < 6 && DragRect.height < 6) ClickSelect(mp);
                    else BoxSelect(DragRect);
                }
            }
            // приказы
            if (Keys.MouseDown(1) && Ground(mp, out var p0)) { rmbDown = true; orderStart = p0; OrderEnd = p0; }
            if (rmbDown)
            {
                if (Ground(mp, out var p1)) OrderEnd = p1;
                DrawArrow();
                if (Keys.MouseUp(1))
                {
                    rmbDown = false;
                    if (arrow) arrow.enabled = false;
                    IssueOrder(mp);
                }
            }
        }

        bool Ground(Vector2 screen, out Vector3 p)
        {
            var ray = CommanderCamera.I.Cam.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var h, 3000f, ~0, QueryTriggerInteraction.Ignore)) { p = h.point; return true; }
            p = default;
            return false;
        }

        void ClickSelect(Vector2 mp)
        {
            var ci = CommandInput.I;
            var b = BattleNet.I;
            if (!ci || !b) return;
            var cam = CommanderCamera.I.Cam;
            Squad best = null;
            float bd = 28f;
            foreach (var sq in b.MySquads)
            {
                foreach (var m in sq.Members)
                {
                    if (!m || m.Dead) continue;
                    var sp = cam.WorldToScreenPoint(m.transform.position + Vector3.up);
                    if (sp.z < 0) continue;
                    float d = Vector2.Distance(mp, sp);
                    if (d < bd) { bd = d; best = sq; }
                }
            }
            if (best != null)
            {
                if (Keys.Shift) ci.Toggle(best.Id);
                else ci.Select(new[] { best.Id });
                Audio.Ui("impact", 0.2f);
            }
            else if (!Keys.Shift) ci.Select(new ushort[0]);
        }

        void BoxSelect(Rect r)
        {
            var ci = CommandInput.I;
            var b = BattleNet.I;
            if (!ci || !b) return;
            var cam = CommanderCamera.I.Cam;
            var ids = new List<ushort>();
            foreach (var sq in b.MySquads)
                foreach (var m in sq.Members)
                {
                    if (!m || m.Dead) continue;
                    var sp = cam.WorldToScreenPoint(m.transform.position);
                    if (sp.z > 0 && r.Contains(sp)) { ids.Add(sq.Id); break; }
                }
            ci.Select(ids, Keys.Shift);
        }

        void IssueOrder(Vector2 mp)
        {
            var ci = CommandInput.I;
            if (!ci || ci.Selected.Count == 0) return;
            Vector3 end = OrderEnd;
            Vector3 d = end - orderStart;
            d.y = 0;
            float yaw = d.magnitude > 3f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : float.NaN;
            // враг под курсором — атака
            var cam = CommanderCamera.I.Cam;
            var ray = cam.ScreenPointToRay(mp);
            if (Physics.Raycast(ray, out var h, 3000f, ~0, QueryTriggerInteraction.Ignore))
            {
                var hb = h.collider.GetComponent<Hitbox>();
                Unit u = hb ? hb.Owner : h.collider.GetComponentInParent<Unit>();
                if (u && !u.Dead && u.Team != BattleNet.I.LocalTeam)
                {
                    ci.Order(OrderType.Attack, u.transform.position);
                    return;
                }
                var des = h.collider.GetComponentInParent<Destructible>();
                if (des && des.Team != BattleNet.I.LocalTeam)
                {
                    ci.Order(OrderType.Attack, h.point);
                    return;
                }
            }
            ci.Order(Keys.Ctrl ? OrderType.Defend : Keys.Raw(KeyCode.LeftAlt) ? OrderType.Attack : OrderType.Move, orderStart, yaw);
        }

        void DrawArrow()
        {
            if (!arrow)
            {
                arrow = new GameObject("OrderArrow").AddComponent<LineRenderer>();
                arrow.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
                arrow.widthMultiplier = 0.6f;
                arrow.positionCount = 5;
                arrow.startColor = arrow.endColor = new Color(0.4f, 0.8f, 1f, 0.9f);
            }
            Vector3 a = orderStart + Vector3.up * 0.4f, b = OrderEnd + Vector3.up * 0.4f;
            Vector3 dir = b - a;
            dir.y = 0;
            if (dir.magnitude < 3f) { arrow.enabled = false; return; }
            arrow.enabled = true;
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            arrow.SetPosition(0, a);
            arrow.SetPosition(1, b);
            arrow.SetPosition(2, b - dir * 3f + side * 2f);
            arrow.SetPosition(3, b);
            arrow.SetPosition(4, b - dir * 3f - side * 2f);
        }
    }
}
