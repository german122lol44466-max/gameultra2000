using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace SW.Commander.UI
{
    /// <summary>
    /// Интерфейс боя (UI Toolkit): прицел, оружие, здоровье, снабжение, командные пункты, карточки отрядов,
    /// метки в мире, миникарта, уведомления, чат, радиальное меню, панель тактической карты и экраны
    /// (казарма, высадка, пауза, итоги) — последние в BattleScreens.cs.
    /// </summary>
    public partial class BattleHud : MonoBehaviour
    {
        public static bool PointerOverUi { get; private set; }
        public static BattleHud I { get; private set; }

        VisualElement root, hud, crosshair, hitmarker, squadsPanel, postsBar, noticeBox, chatBox, markers, minimap, radial, tacticalBar, selectBox, vignette, hints;
        Label ammo, ammoMax, weaponName, grenades, healthText, reqText, unitsText, ticketsText, objectiveText;
        VisualElement healthFill;
        readonly VisualElement[] cross = new VisualElement[4];
        readonly List<(Label l, float t)> notices = new List<(Label, float)>();
        readonly List<(Label l, float t)> chatLines = new List<(Label, float)>();
        readonly Dictionary<object, VisualElement> markerMap = new Dictionary<object, VisualElement>();
        readonly Dictionary<Soldier, VisualElement> mapDots = new Dictionary<Soldier, VisualElement>();
        TextField chatInput;
        float squadsAt, hitAt;
        BattleNet B => BattleNet.I;
        CommanderController CC => B && B.LocalCommander ? B.LocalCommander.GetComponent<CommanderController>() : null;

        void Awake() => I = this;

        void Start()
        {
            root = Ui.Document(gameObject, 10);
            BuildHud();
            BuildScreens();
            if (B)
            {
                B.NoticeShown += Notice;
                B.MatchEnded += OnMatchEnded;
                B.SquadsChanged += () => squadsAt = 0f;
                B.LocalCommanderDied += OnDied;
                B.LocalCommanderSpawned += OnSpawned;
            }
            var s = NetSession.Instance;
            if (s) s.ChatReceived += OnChat;
            if (CommandInput.I) CommandInput.I.SelectionChanged += () => squadsAt = 0f;
        }

        void OnDestroy()
        {
            var s = NetSession.Instance;
            if (s) s.ChatReceived -= OnChat;
            Keys.Typing = false;
            CommanderController.Blocked = false;
        }

        // ================================================================== построение
        void BuildHud()
        {
            hud = Ui.Box(root, "hud");
            hud.pickingMode = PickingMode.Ignore;
            vignette = Ui.Box(hud, "damage-vignette");
            vignette.pickingMode = PickingMode.Ignore;
            markers = Ui.Box(hud, "hud");
            markers.pickingMode = PickingMode.Ignore;

            crosshair = Ui.Box(hud, "crosshair");
            for (int i = 0; i < 4; i++)
            {
                cross[i] = Ui.Box(crosshair, "cross-line");
                cross[i].style.width = i < 2 ? 10 : 2;
                cross[i].style.height = i < 2 ? 2 : 10;
            }
            var dot = Ui.Box(crosshair, "cross-line");
            dot.style.width = 2; dot.style.height = 2; dot.style.left = -1; dot.style.top = -1;
            hitmarker = Ui.Box(crosshair, "hitmarker");

            // низ слева: здоровье, снабжение
            var bl = Ui.Box(hud, "hud-bottom-left");
            healthText = Ui.Text(bl, "ЗДОРОВЬЕ", "weapon-name");
            var hb = Ui.Box(bl, "bar-bg");
            healthFill = Ui.Box(hb, "bar-fill", "bar-health");
            healthFill.style.width = new Length(100, LengthUnit.Percent);
            var rr = Ui.Row(bl);
            rr.style.marginTop = 10;
            reqText = Ui.Text(rr, "", "h2");
            unitsText = Ui.Text(rr, "", "small", "muted");
            unitsText.style.marginLeft = 14;

            // низ справа: оружие
            var br = Ui.Box(hud, "hud-bottom-right");
            weaponName = Ui.Text(br, "", "weapon-name");
            var ar = Ui.Row(br);
            ammo = Ui.Text(ar, "", "ammo");
            ammoMax = Ui.Text(ar, "", "ammo-small");
            grenades = Ui.Text(br, "", "small", "muted");

            // верх: командные пункты
            var top = Ui.Box(hud, "hud-top");
            top.style.flexDirection = FlexDirection.Column;
            top.style.alignItems = Align.Center;
            postsBar = Ui.Row(top);
            var obj = Ui.Row(top);
            obj.style.marginTop = 10;
            objectiveText = Ui.Text(obj, "", "small");
            ticketsText = Ui.Text(obj, "", "small", "emp");
            ticketsText.style.marginLeft = 16;

            squadsPanel = Ui.Box(hud, "hud-left");
            var tr = Ui.Box(hud, "hud-right");
            minimap = Ui.Box(tr, "minimap");
            noticeBox = Ui.Box(hud, "notice-box");
            chatBox = Ui.Box(hud, "chat");
            chatInput = Ui.Field(chatBox, null, "", 200);
            Ui.Show(chatInput, false);
            chatInput.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    NetSession.Instance?.SendChat(chatInput.value);
                    CloseChat();
                }
                else if (e.keyCode == KeyCode.Escape) CloseChat();
            });
            radial = Ui.Box(hud, "radial");
            radial.pickingMode = PickingMode.Ignore;
            selectBox = Ui.Box(hud, "select-box");
            Ui.Show(selectBox, false);
            tacticalBar = Ui.Box(hud, "tactical-bar");
            Ui.Show(tacticalBar, false);
            hints = Ui.Box(hud, "letterbox-bottom");
            hints.style.backgroundColor = new Color(0, 0, 0, 0);
            hints.style.justifyContent = Justify.Center;
            hints.pickingMode = PickingMode.Ignore;
            Ui.Hint(hints, "1–9", "ОТРЯД");
            Ui.Hint(hints, "F", "ВЫДВИНУТЬСЯ");
            Ui.Hint(hints, "2×F", "ЗА МНОЙ");
            Ui.Hint(hints, "T", "АТАКА");
            Ui.Hint(hints, "Q/E/X", "СТРОЙ/СТОЙКА/ПОХОДКА");
            Ui.Hint(hints, "TAB", "ТАКТ. КАРТА");
            Ui.Hint(hints, "B", "КАЗАРМА");
            Ui.Hint(hints, "F1", "СПРАВКА");
            foreach (var e in new[] { hud, crosshair, postsBar, top, obj, noticeBox, bl, br, minimap, tr, markers })
                e.pickingMode = PickingMode.Ignore;
        }

        // ================================================================== кадр
        void Update()
        {
            if (!B) return;
            UpdatePointer();
            UpdateInputModes();
            UpdateCombat();
            UpdatePosts();
            if (Time.unscaledTime > squadsAt) { squadsAt = Time.unscaledTime + 0.3f; UpdateSquads(); }
            UpdateMarkers();
            UpdateMinimap();
            UpdateRadial();
            UpdateTactical();
            UpdateNotices();
            UpdateScreens();
            UpdateCursor();
        }

        void UpdatePointer()
        {
            PointerOverUi = false;
            var panel = root?.panel;
            if (panel == null) return;
            var mp = Keys.MousePosition;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(mp.x, Screen.height - mp.y));
            var picked = panel.Pick(p);
            PointerOverUi = picked != null && picked != root && picked != root.parent;
        }

        void UpdateInputModes()
        {
            bool screenOpen = AnyScreenOpen;
            if (!screenOpen && !Keys.Typing)
            {
                if (Keys.Down(KeyCode.Tab) && TacticalView.I && B.LocalCommander && !B.LocalCommander.Dead) TacticalView.I.Toggle();
                if (Keys.Down(KeyCode.Return) && NetSession.Instance && NetSession.Instance.Online) OpenChat();
            }
        }

        void UpdateCombat()
        {
            var cc = CC;
            bool alive = cc && !cc.Self.Dead;
            bool tactical = TacticalView.I && TacticalView.I.Active;
            Ui.Show(crosshair, alive && !tactical && !AnyScreenOpen);
            if (alive)
            {
                var s = cc.Self;
                float spread = 6f + cc.Spread * 9f;
                cross[0].style.left = -spread - 10; cross[0].style.top = -1;
                cross[1].style.left = spread; cross[1].style.top = -1;
                cross[2].style.top = -spread - 10; cross[2].style.left = -1;
                cross[3].style.top = spread; cross[3].style.left = -1;
                float hp = Mathf.Clamp01(s.Health / Mathf.Max(1f, s.MaxHealth));
                healthFill.style.width = new Length(hp * 100f, LengthUnit.Percent);
                healthFill.style.backgroundColor = hp < 0.3f ? new Color(1f, 0.35f, 0.25f) : new Color(0.92f, 0.94f, 0.96f);
                healthText.text = $"{s.Title.ToUpper()} · {Mathf.CeilToInt(s.Health)}";
                var w = s.Type?.Weapon;
                weaponName.text = w != null ? w.Title.ToUpper() : "";
                ammo.text = s.Reloading ? "··" : s.Ammo.ToString();
                ammoMax.text = w != null ? $"/ {w.Magazine}" : "";
                grenades.text = $"ДЕТОНАТОРЫ: {cc.Grenades}   " + (s.Reloading ? "ПЕРЕЗАРЯДКА..." : cc.Prone ? "ЛЁЖА" : cc.Crouched ? "ПРИСЕД" : "");
                float a = Mathf.Max(cc.HurtFlash, hp < 0.3f ? 0.35f + Mathf.Sin(Time.time * 5f) * 0.1f : 0f);
                vignette.style.borderTopColor = vignette.style.borderBottomColor = vignette.style.borderLeftColor = vignette.style.borderRightColor = new Color(0.9f, 0.05f, 0.02f, a * 0.45f);
            }
            float hm = Mathf.Clamp01(1f - (Time.time - hitAt) / 0.25f);
            hitmarker.style.opacity = hm;
            reqText.text = $"СНАБЖЕНИЕ {B.Requisition}";
            unitsText.text = $"БОЙЦОВ {B.UnitCount}/{B.UnitCap}";
            Ui.Show(hints, Prefs.Hints && alive && !tactical && !AnyScreenOpen);
        }

        public void HitConfirmed() => hitAt = Time.time;

        // ================================================================== пункты
        readonly Dictionary<CommandPost, (VisualElement chip, VisualElement fill, Label letter)> postChips = new Dictionary<CommandPost, (VisualElement, VisualElement, Label)>();

        void UpdatePosts()
        {
            if (postChips.Count != CommandPost.All.Count)
            {
                postsBar.Clear();
                postChips.Clear();
                foreach (var cp in CommandPost.All.OrderBy(c => c.Id))
                {
                    var chip = Ui.Box(postsBar, "post-chip");
                    var fill = Ui.Box(chip, "post-fill");
                    var l = Ui.Text(chip, cp.Letter, "post-letter");
                    postChips[cp] = (chip, fill, l);
                }
            }
            foreach (var kv in postChips)
            {
                var cp = kv.Key;
                var (chip, fill, _) = kv.Value;
                var c = CommandPost.ColorOf(cp.Owner);
                chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = cp.Contested ? new Color(1f, 0.85f, 0.3f) : c;
                float p = Mathf.Abs(cp.Progress);
                var pc = cp.Progress >= 0 ? TeamColors.Republic : TeamColors.Empire;
                pc.a = 0.6f;
                fill.style.backgroundColor = pc;
                fill.style.height = new Length(p * 100f, LengthUnit.Percent);
            }
            var map = MapInfo.I;
            if (map && map.CommandCenter)
            {
                bool shield = map.ShieldGenerators.Any(g => g && !g.Dead);
                int gens = map.ShieldGenerators.Count(g => g && !g.Dead);
                objectiveText.text = shield ? $"ЦЕЛЬ: УНИЧТОЖИТЬ ГЕНЕРАТОРЫ ЩИТА ({gens})" : $"ЦЕЛЬ: УНИЧТОЖИТЬ КОМАНДНЫЙ ЦЕНТР ({Mathf.CeilToInt(map.CommandCenter.Health / map.CommandCenter.MaxHealth * 100)}%)";
            }
            ticketsText.text = $"ПОДКРЕПЛЕНИЯ ИМПЕРИИ: {B.EmpireTickets}";
        }

        // ================================================================== отряды
        void UpdateSquads()
        {
            squadsPanel.Clear();
            var list = B.MySquads.ToList();
            var sel = CommandInput.I ? CommandInput.I.Selected : new List<ushort>();
            int i = 1;
            foreach (var sq in list)
            {
                var card = Ui.Col(squadsPanel, "squad-card");
                card.style.borderLeftColor = TeamColors.Squad[sq.Color % TeamColors.Squad.Length];
                if (sel.Contains(sq.Id)) card.AddToClassList("squad-card-selected");
                var head = Ui.Row(card);
                Ui.Text(head, $"{i}", "hint-key").style.marginLeft = 0;
                Ui.Text(head, sq.Name.ToUpper(), "squad-title");
                Ui.Box(head, "spacer");
                if (sq.ClientInCombat || (B.IsServer && sq.InCombat)) Ui.Text(head, "БОЙ", "tiny", "bad");
                Ui.Text(card, $"{Names.Of(sq.Order)} · {Names.Of(sq.Formation)}", "squad-info");
                Ui.Text(card, $"{Names.Of(sq.Stance)} · {Names.Of(sq.Gait)} · {Names.Of(sq.Roe)}", "squad-info");
                var pips = Ui.Row(card, "pips");
                foreach (var m in sq.Members)
                {
                    var pip = Ui.Box(pips, "pip");
                    if (!m || m.Dead) pip.AddToClassList("pip-dead");
                    else if (m.Health < m.MaxHealth * 0.5f) pip.AddToClassList("pip-hurt");
                }
                ushort id = sq.Id;
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    if (Keys.Shift) CommandInput.I?.Toggle(id);
                    else CommandInput.I?.Select(new[] { id });
                });
                i++;
            }
            if (list.Count == 0) Ui.Text(squadsPanel, "Нет отрядов — откройте казарму (B)", "small", "muted");
        }

        // ================================================================== метки
        VisualElement Marker(object key, string text, Color c)
        {
            if (!markerMap.TryGetValue(key, out var m))
            {
                m = Ui.Box(markers, "marker");
                m.pickingMode = PickingMode.Ignore;
                Ui.Box(m, "marker-icon");
                Ui.Text(m, text, "marker-text");
                markerMap[key] = m;
            }
            var icon = m[0];
            icon.style.borderTopColor = icon.style.borderBottomColor = icon.style.borderLeftColor = icon.style.borderRightColor = c;
            ((Label)m[1]).text = text;
            ((Label)m[1]).style.color = c;
            return m;
        }

        readonly HashSet<object> seen = new HashSet<object>();

        void UpdateMarkers()
        {
            var cam = CommanderCamera.I ? CommanderCamera.I.Cam : Camera.main;
            if (!cam || root.panel == null) return;
            seen.Clear();
            bool tactical = TacticalView.I && TacticalView.I.Active;
            var sel = CommandInput.I ? CommandInput.I.Selected : new List<ushort>();
            Vector3 me = B.LocalCommander ? B.LocalCommander.transform.position : cam.transform.position;
            // командные пункты
            foreach (var cp in CommandPost.All)
                Place(cp, cp.transform.position + Vector3.up * 6f, $"{cp.Letter}  {Mathf.RoundToInt(Vector3.Distance(me, cp.transform.position))} м", CommandPost.ColorOf(cp.Owner), cam);
            // цели
            var map = MapInfo.I;
            if (map)
            {
                foreach (var g in map.ShieldGenerators)
                    if (g && !g.Dead) Place(g, g.Center + Vector3.up * 8f, $"ГЕНЕРАТОР {Mathf.CeilToInt(g.Health / g.MaxHealth * 100)}%", new Color(1f, 0.8f, 0.3f), cam);
                if (map.CommandCenter && !map.CommandCenter.Dead)
                    Place(map.CommandCenter, map.CommandCenter.Center + Vector3.up * 14f, map.CommandCenter.Invulnerable ? "КОМАНДНЫЙ ЦЕНТР (ЩИТ)" : "КОМАНДНЫЙ ЦЕНТР", new Color(1f, 0.5f, 0.3f), cam);
            }
            // свои отряды
            int i = 1;
            foreach (var sq in B.MySquads)
            {
                var c = TeamColors.Squad[sq.Color % TeamColors.Squad.Length];
                if (sel.Contains(sq.Id) || tactical)
                {
                    var center = sq.Members.Count > 0 ? sq.Center : sq.Anchor;
                    Place(sq, center + Vector3.up * 3f, $"{i} {sq.Name}", c, cam);
                    if (sq.Order != OrderType.Hold && sq.Order != OrderType.Follow)
                        Place((sq, "dst"), sq.Destination + Vector3.up * 1f, "▼", c, cam);
                }
                i++;
            }
            // другие игроки
            foreach (var s in Soldier.Living)
            {
                if (!s || !s.IsCommander || s == B.LocalCommander || s.Dead) continue;
                if (s.Team != B.LocalTeam) continue;
                var p = NetSession.Instance ? NetSession.Instance.Get(s.Owner) : null;
                Place(s, s.transform.position + Vector3.up * 2.3f, p?.Name ?? "Командир", new Color(0.6f, 0.85f, 1f), cam);
            }
            foreach (var key in markerMap.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                markerMap[key].RemoveFromHierarchy();
                markerMap.Remove(key);
            }
        }

        void Place(object key, Vector3 world, string text, Color c, Camera cam)
        {
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0.5f) return;
            seen.Add(key);
            var m = Marker(key, text, c);
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sp.x, Screen.height - sp.y));
            m.style.left = p.x - 60;
            m.style.top = p.y - 10;
            m.style.width = 120;
            float dist = sp.z;
            m.style.opacity = Mathf.Clamp(1.4f - dist / 600f, 0.45f, 1f);
        }

        // ================================================================== миникарта
        void UpdateMinimap()
        {
            var map = MapInfo.I;
            if (!map) return;
            float size = 220f;
            Vector3 me = B.LocalCommander ? B.LocalCommander.transform.position : Vector3.zero;
            // масштаб: 320 м на ширину, центр — игрок
            float span = 320f;
            if (mapImg == null)
            {
                mapImg = Ui.Box(minimap);
                mapImg.style.position = Position.Absolute;
                mapImg.pickingMode = PickingMode.Ignore;
                if (map.MapImage) mapImg.style.backgroundImage = new StyleBackground(map.MapImage);
                mapImg.SendToBack();
            }
            var bg = map.ToMap(me);
            float scale = map.Size.x / span;
            mapImg.style.width = size * scale;
            mapImg.style.height = size * scale;
            mapImg.style.left = -(bg.x * size * scale - size / 2f);
            mapImg.style.top = -((1f - bg.y) * size * scale - size / 2f);
            var alive = new HashSet<Soldier>();
            foreach (var s in Soldier.Living)
            {
                if (!s || s.Dead) continue;
                Vector3 d = s.transform.position - me;
                if (Mathf.Abs(d.x) > span / 2f || Mathf.Abs(d.z) > span / 2f) continue;
                bool friend = s.Team == B.LocalTeam;
                if (!friend && !SpottedByFriends(s)) continue;
                alive.Add(s);
                if (!mapDots.TryGetValue(s, out var dot))
                {
                    dot = Ui.Box(minimap, "map-dot");
                    dot.pickingMode = PickingMode.Ignore;
                    mapDots[s] = dot;
                }
                dot.style.left = size / 2f + d.x / span * size - 2;
                dot.style.top = size / 2f - d.z / span * size - 2;
                dot.style.backgroundColor = s == B.LocalCommander ? Color.white : s.Owner == B.LocalId ? TeamColors.Squad[(s.Squad?.Color ?? 0) % TeamColors.Squad.Length]
                                          : friend ? new Color(0.4f, 0.7f, 1f, 0.8f) : TeamColors.Empire;
            }
            foreach (var k in mapDots.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                mapDots[k].RemoveFromHierarchy();
                mapDots.Remove(k);
            }
        }

        VisualElement mapImg;
        float spotAt;
        readonly HashSet<Soldier> spotted = new HashSet<Soldier>();

        bool SpottedByFriends(Soldier enemy)
        {
            if (Time.time > spotAt)
            {
                spotAt = Time.time + 0.5f;
                spotted.Clear();
                var friends = Soldier.Living.Where(s => s && !s.Dead && s.Team == B.LocalTeam).ToList();
                foreach (var e in Soldier.Living)
                {
                    if (!e || e.Dead || e.Team == B.LocalTeam) continue;
                    foreach (var f in friends)
                        if ((f.transform.position - e.transform.position).sqrMagnitude < 70f * 70f) { spotted.Add(e); break; }
                }
            }
            return spotted.Contains(enemy);
        }

        // ================================================================== радиальное меню
        int radialShownKind = -1;

        void UpdateRadial()
        {
            var ci = CommandInput.I;
            if (!ci || ci.Radial == CommandInput.RadialKind.None)
            {
                if (radialShownKind != -1) { radial.Clear(); radialShownKind = -1; }
                return;
            }
            int n = ci.RadialItems.Length;
            if (radialShownKind != (int)ci.Radial)
            {
                radial.Clear();
                radialShownKind = (int)ci.Radial;
                var t = Ui.Text(radial, ci.Radial == CommandInput.RadialKind.Formation ? "СТРОЙ" : ci.Radial == CommandInput.RadialKind.Stance ? "СТОЙКА" : "ПОХОДКА", "radial-title");
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    var item = Ui.Text(radial, ci.RadialItems[i], "radial-item");
                    item.style.left = Mathf.Sin(a) * 210f;
                    item.style.top = -Mathf.Cos(a) * 170f;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var item = radial[i + 1];
                item.EnableInClassList("radial-item-hot", i == ci.RadialHot);
                item.EnableInClassList("radial-item-current", i == ci.RadialCurrent);
            }
        }

        // ================================================================== тактическая карта
        bool tacticalBuilt;

        void UpdateTactical()
        {
            var tv = TacticalView.I;
            bool on = tv && tv.Active;
            Ui.Show(tacticalBar, on);
            Ui.Show(squadsPanel, true);
            if (!on) { tacticalBuilt = false; Ui.Show(selectBox, false); return; }
            if (!tacticalBuilt) BuildTacticalBar();
            if (tv.Dragging && tv.DragRect.width > 6)
            {
                Ui.Show(selectBox, true);
                var r = tv.DragRect;
                var p0 = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(r.xMin, Screen.height - r.yMax));
                var p1 = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(r.xMax, Screen.height - r.yMin));
                selectBox.style.left = p0.x; selectBox.style.top = p0.y;
                selectBox.style.width = p1.x - p0.x; selectBox.style.height = p1.y - p0.y;
            }
            else Ui.Show(selectBox, false);
        }

        void BuildTacticalBar()
        {
            tacticalBuilt = true;
            tacticalBar.Clear();
            var ci = CommandInput.I;
            VisualElement Group(string title)
            {
                var g = Ui.Col(tacticalBar, "tactical-group");
                Ui.Text(g, title, "h3");
                return Ui.Box(g, "tactical-buttons");
            }
            var orders = Group("ПРИКАЗЫ");
            Ui.Button(orders, "СТОЯТЬ", () => ci.Order(OrderType.Hold, CenterOfSelection()), "btn", "btn-small");
            Ui.Button(orders, "ЗА МНОЙ", () => ci.Order(OrderType.Follow, B.LocalCommander ? B.LocalCommander.transform.position : Vector3.zero), "btn", "btn-small");
            Ui.Button(orders, "ОБОРОНА", () => ci.Order(OrderType.Defend, CenterOfSelection()), "btn", "btn-small");
            var f = Group("СТРОЙ");
            for (int i = 0; i < Names.Formation.Length; i++)
            {
                int v = i;
                Ui.Button(f, Names.Formation[i], () => ci.Apply(CommandInput.RadialKind.Formation, v), "btn", "btn-small");
            }
            var s = Group("СТОЙКА");
            for (int i = 0; i < Names.Stance.Length; i++)
            {
                int v = i;
                Ui.Button(s, Names.Stance[i], () => ci.Apply(CommandInput.RadialKind.Stance, v), "btn", "btn-small");
            }
            var g2 = Group("ПОХОДКА / ОГОНЬ");
            for (int i = 0; i < Names.Gait.Length; i++)
            {
                int v = i;
                Ui.Button(g2, Names.Gait[i], () => ci.Apply(CommandInput.RadialKind.Gait, v), "btn", "btn-small");
            }
            for (int i = 0; i < Names.Roe.Length; i++)
            {
                var r = (Roe)i;
                Ui.Button(g2, Names.Roe[i], () =>
                {
                    foreach (var sq in ci.SelectedSquads.ToList()) { sq.Roe = r; B.SendSquadConfig(sq); }
                }, "btn", "btn-small");
            }
        }

        Vector3 CenterOfSelection()
        {
            var list = CommandInput.I.SelectedSquads.ToList();
            if (list.Count == 0) return Vector3.zero;
            Vector3 c = Vector3.zero;
            foreach (var s in list) c += s.Members.Count > 0 ? s.Center : s.Anchor;
            return c / list.Count;
        }

        // ================================================================== уведомления и чат
        void Notice(string text, Color c)
        {
            var l = Ui.Text(noticeBox, text.ToUpper(), "notice");
            l.style.color = c;
            notices.Add((l, Time.unscaledTime));
            if (notices.Count > 4)
            {
                notices[0].l.RemoveFromHierarchy();
                notices.RemoveAt(0);
            }
        }

        void OnChat(string from, string text)
        {
            var l = Ui.Text(chatBox, string.IsNullOrEmpty(from) ? text : $"{from}: {text}", "chat-line");
            l.PlaceBehind(chatInput);
            chatLines.Add((l, Time.unscaledTime));
            if (chatLines.Count > 7) { chatLines[0].l.RemoveFromHierarchy(); chatLines.RemoveAt(0); }
        }

        void OpenChat()
        {
            Ui.Show(chatInput, true);
            chatInput.value = "";
            chatInput.Focus();
            Keys.Typing = true;
            CommanderController.Blocked = true;
        }

        void CloseChat()
        {
            chatInput.value = "";
            chatInput.Blur();
            Ui.Show(chatInput, false);
            Keys.Typing = false;
            CommanderController.Blocked = AnyScreenOpen || (TacticalView.I && TacticalView.I.Active);
        }

        void UpdateNotices()
        {
            float now = Time.unscaledTime;
            for (int i = notices.Count - 1; i >= 0; i--)
            {
                float age = now - notices[i].t;
                notices[i].l.style.opacity = Mathf.Clamp01(4.5f - age);
                if (age > 4.5f) { notices[i].l.RemoveFromHierarchy(); notices.RemoveAt(i); }
            }
            bool typing = chatInput.focusController?.focusedElement == chatInput;
            for (int i = chatLines.Count - 1; i >= 0; i--)
            {
                float age = now - chatLines[i].t;
                chatLines[i].l.style.opacity = typing ? 1f : Mathf.Clamp01(12f - age);
            }
        }

        void UpdateCursor()
        {
            bool free = AnyScreenOpen || (TacticalView.I && TacticalView.I.Active) || Keys.Typing || !B.LocalCommander || B.LocalCommander.Dead;
            UnityEngine.Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            UnityEngine.Cursor.visible = free;
        }
    }
}
