using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Тактическая карта (Tab): вид сверху как в RTS. ЛКМ — выбрать отряд (рамкой — несколько, Shift — добавить),
    /// ПКМ — выдвинуться (протянуть — задать направление строя), ПКМ по врагу — атака, по командному пункту — захват,
    /// Ctrl+ПКМ — оборонять. WASD — сдвиг, колесо — высота, Q/E или средняя кнопка — поворот.
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

        void Awake() => I = this;

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
            cam.TacticalFocus = focus;
            cam.TacticalYaw = cam.Yaw;
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
            cam.Current = b && b.LocalCommander && !b.LocalCommander.Dead ? CommanderCamera.Mode.Follow : prevMode;
            CommanderController.Blocked = false;
            dragging = rmbDown = false;
            if (arrow) arrow.enabled = false;
        }

        void Update()
        {
            if (!Active) return;
            var cam = CommanderCamera.I;
            if (!cam) return;
            float dt = Time.unscaledDeltaTime;
            // камера
            var mv = new Vector2((Keys.Raw(KeyCode.D) ? 1 : 0) - (Keys.Raw(KeyCode.A) ? 1 : 0), (Keys.Raw(KeyCode.W) ? 1 : 0) - (Keys.Raw(KeyCode.S) ? 1 : 0));
            if (Keys.Typing) mv = Vector2.zero;
            var rot = Quaternion.Euler(0, cam.TacticalYaw, 0);
            float speed = Mathf.Lerp(30f, 260f, Mathf.InverseLerp(25f, 300f, cam.TacticalHeight)) * (Keys.Shift ? 2.2f : 1f);
            cam.TacticalFocus += rot * new Vector3(mv.x, 0, mv.y) * speed * dt;
            var map = MapInfo.I;
            if (map)
            {
                var r = map.Bounds;
                cam.TacticalFocus.x = Mathf.Clamp(cam.TacticalFocus.x, r.xMin, r.xMax);
                cam.TacticalFocus.z = Mathf.Clamp(cam.TacticalFocus.z, r.yMin, r.yMax);
            }
            cam.TacticalHeight = Mathf.Clamp(cam.TacticalHeight * (1f - Keys.Scroll * 0.12f), 25f, 320f);
            if (!Keys.Typing)
            {
                if (Keys.Raw(KeyCode.Q)) cam.TacticalYaw -= 70f * dt;
                if (Keys.Raw(KeyCode.E)) cam.TacticalYaw += 70f * dt;
            }
            if (Keys.Mouse(2)) cam.TacticalYaw += Keys.Look.x * 1.5f;

            if (UI.BattleHud.PointerOverUi) return;
            Vector2 mp = Keys.MousePosition;
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
