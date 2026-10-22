using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace SW.Commander.UI
{
    /// <summary>
    /// Главное меню в стиле Battlefront: слева заголовок и пункты, справа — живая сцена (клоны в строю, техника).
    /// Экраны: одиночная игра, мультиплеер (создать лобби Steam / найти / по коду / по IP), лобби, арсенал, настройки.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        VisualElement root, main, content, statusBar;
        Label status;
        NetSession session;
        MatchSettings draft = new MatchSettings();
        enum Screen { Main, Solo, Multi, Lobby, Arsenal, Settings }
        Screen screen;
        string multiTab = "create";
        // лобби
        VisualElement lobbyPlayers, lobbySettings, chatLog;
        TextField chatField;
        Button readyBtn, startBtn;
        readonly List<string> chat = new List<string>();
        List<LobbyListing> listings = new List<LobbyListing>();
        VisualElement listBox;
        float refreshAt;

        void Start()
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            Time.timeScale = 1f;
            session = NetSession.Ensure();
            session.StatusChanged += OnStatus;
            session.LobbyChanged += OnLobbyChanged;
            session.ChatReceived += OnChat;
            session.Lobby.ListReceived += OnList;
            draft.LobbyName = $"Отряд {SteamBootstrap.LocalName}";
            root = Ui.Document(gameObject, 0);
            Build();
            if (session.Phase == SessionPhase.Lobby || session.Phase == SessionPhase.Connecting) Show(Screen.Lobby);
            else Show(Screen.Main);
            if (!string.IsNullOrEmpty(session.LastStatus)) OnStatus(session.LastStatus);
        }

        void OnDestroy()
        {
            if (!session) return;
            session.StatusChanged -= OnStatus;
            session.LobbyChanged -= OnLobbyChanged;
            session.ChatReceived -= OnChat;
            if (session.Lobby) session.Lobby.ListReceived -= OnList;
            Keys.Typing = false;
        }

        // ================================================================== каркас
        void Build()
        {
            root.Clear();
            Ui.Box(root, "vignette-left");
            Ui.Box(root, "letterbox-top");
            var bottom = Ui.Box(root, "letterbox-bottom");
            status = Ui.Text(bottom, "", "status");
            Ui.Box(bottom, "spacer");
            Ui.Text(bottom, SteamBootstrap.Initialized ? $"STEAM: {SteamBootstrap.LocalName}" : $"STEAM НЕДОСТУПЕН — {SteamBootstrap.Error ?? "игра по IP и одиночная"}", "hint-text");
            Ui.Text(bottom, "   v" + Application.version, "hint-text");
            statusBar = bottom;
            main = Ui.Box(root, "menu-column");
            content = Ui.Box(root, "content");
        }

        void Show(Screen s)
        {
            screen = s;
            main.Clear();
            content.Clear();
            Ui.Show(main, s == Screen.Main);
            Ui.Show(content, s != Screen.Main);
            switch (s)
            {
                case Screen.Main: BuildMain(); break;
                case Screen.Solo: BuildSolo(); break;
                case Screen.Multi: BuildMulti(); break;
                case Screen.Lobby: BuildLobby(); break;
                case Screen.Arsenal: BuildArsenal(); break;
                case Screen.Settings: BuildSettings(); break;
            }
        }

        Label Header(VisualElement parent, string brand, string title)
        {
            Ui.Text(parent, brand, "brand-small");
            var t = Ui.Text(parent, title, "h1");
            Ui.Box(parent, "line-accent");
            return t;
        }

        // ================================================================== главное
        void BuildMain()
        {
            Ui.Text(main, "STAR WARS", "brand-small");
            Ui.Text(main, "КОМАНДИР", "title");
            Ui.Text(main, "КЛОНОВ", "title");
            Ui.Text(main, "ВОЙНА ЗА ФЕЛЛУСИЮ", "title-sub");
            var items = Ui.Box(main, "menu-items");
            Ui.MenuItem(items, "ОДИНОЧНАЯ ИГРА", () => Show(Screen.Solo), "Ведите армию клонов против гарнизонов Империи");
            Ui.MenuItem(items, "МУЛЬТИПЛЕЕР", () => Show(Screen.Multi), "Кооператив и сражения через Steam или по IP");
            Ui.MenuItem(items, "АРСЕНАЛ", () => Show(Screen.Arsenal), "Бойцы, оружие и строи");
            Ui.MenuItem(items, "НАСТРОЙКИ", () => Show(Screen.Settings));
            Ui.MenuItem(items, "ВЫХОД", Quit);
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void BackButton(VisualElement bar, Screen to = Screen.Main) => Ui.Button(bar, "‹ НАЗАД", () => Show(to));

        // ================================================================== одиночная
        void BuildSolo()
        {
            Header(content, "ОДИНОЧНАЯ ИГРА", "ПОДГОТОВКА К ВЫСАДКЕ");
            var cols = Ui.Row(content, "grow");
            cols.style.alignItems = Align.FlexStart;
            var left = Ui.Col(cols, "panel");
            left.style.width = new Length(48, LengthUnit.Percent);
            MatchControls(left, draft, true);
            var right = Ui.Col(cols, "panel-glass");
            right.style.marginLeft = 20;
            right.style.flexGrow = 1;
            Ui.Text(right, "БРИФИНГ", "h3");
            var t = Ui.Text(right, "Империя укрепилась на Феллусии: аванпосты с турелями, патрули и гарнизоны. Ваша задача — " +
                               "высадиться с армией клонов, захватить командные пункты, уничтожить генераторы щита и командный центр.\n\n" +
                               "Набирайте бойцов в казарме (B), собирайте отряды, задавайте им строй, стойку и походку. " +
                               "Захваченные пункты дают снабжение и становятся точками высадки.", "small");
            t.style.whiteSpace = WhiteSpace.Normal;
            Ui.Box(right, "spacer");
            Ui.Text(right, "УПРАВЛЕНИЕ", "h3");
            var keys = Ui.Text(right, "WASD — движение · Shift — бег · ПКМ — прицел · ЛКМ — огонь · R — перезарядка · G — граната\n" +
                                      "C — присесть · Z — лечь · 1–9 — выбрать отряд · ` — все отряды\n" +
                                      "F — выдвинуться к прицелу (2×F — за мной) · T — атаковать · H — стоять\n" +
                                      "Q — строй · E — стойка · X — походка · V — правила огня\n" +
                                      "Tab — тактическая карта · B — казарма · Esc — пауза", "small");
            keys.style.whiteSpace = WhiteSpace.Normal;
            var bar = Ui.Row(content);
            BackButton(bar);
            Ui.Box(bar, "spacer");
            Ui.Button(bar, "НАЧАТЬ БОЙ", () =>
            {
                session.HostOffline(draft);
                if (session.Phase == SessionPhase.Lobby) session.StartMatch();
            }, "btn", "btn-primary");
        }

        void MatchControls(VisualElement p, MatchSettings s, bool editable)
        {
            if (!editable)
            {
                Ui.Text(p, $"Сложность: {MatchSettings.DifficultyNames[s.Difficulty]}", "h2");
                Ui.Text(p, $"Фронт: {MatchSettings.MapSizeNames[s.MapSize]}", "small");
                Ui.Text(p, $"Силы Империи: {MatchSettings.StrengthNames[s.EmpireStrength]}", "small");
                Ui.Text(p, $"Снабжение на старте: {s.StartRequisition}", "small");
                Ui.Text(p, $"Бойцов на игрока: {s.UnitCap}", "small");
                Ui.Text(p, $"Время суток: {MatchSettings.TimeNames[s.TimeOfDay]}", "small");
                Ui.Text(p, s.AllowEmpirePlayers ? "Игроки могут играть за Империю" : "Только за Республику", "small");
                return;
            }
            void Changed()
            {
                if (screen == Screen.Lobby && session.IsServer) session.UpdateSettings(s);
            }
            Ui.Choice(p, "СЛОЖНОСТЬ", MatchSettings.DifficultyNames, s.Difficulty, v => { s.Difficulty = v; Changed(); });
            Ui.Choice(p, "ПРОТЯЖЁННОСТЬ ФРОНТА", MatchSettings.MapSizeNames, s.MapSize, v => { s.MapSize = v; Changed(); });
            Ui.Choice(p, "СИЛЫ ИМПЕРИИ", MatchSettings.StrengthNames, s.EmpireStrength, v => { s.EmpireStrength = v; Changed(); });
            var reqs = new[] { "400", "800", "1200", "2000" };
            Ui.Choice(p, "СНАБЖЕНИЕ НА СТАРТЕ", reqs, System.Array.IndexOf(reqs, s.StartRequisition.ToString()) < 0 ? 1 : System.Array.IndexOf(reqs, s.StartRequisition.ToString()),
                      v => { s.StartRequisition = int.Parse(reqs[v]); Changed(); });
            var caps = new[] { "30", "60", "90", "120" };
            Ui.Choice(p, "БОЙЦОВ НА ИГРОКА", caps, System.Array.IndexOf(caps, s.UnitCap.ToString()) < 0 ? 1 : System.Array.IndexOf(caps, s.UnitCap.ToString()),
                      v => { s.UnitCap = int.Parse(caps[v]); Changed(); });
            Ui.Choice(p, "ВРЕМЯ СУТОК", MatchSettings.TimeNames, s.TimeOfDay, v => { s.TimeOfDay = v; Changed(); });
        }

        // ================================================================== мультиплеер
        void BuildMulti()
        {
            Header(content, "МУЛЬТИПЛЕЕР", "ИГРА С ДРУЗЬЯМИ");
            var tabs = Ui.Row(content, "tabs");
            void Tab(string id, string title)
            {
                var b = Ui.Button(tabs, title, () => { multiTab = id; Show(Screen.Multi); }, "tab");
                if (multiTab == id) b.AddToClassList("tab-active");
            }
            Tab("create", "СОЗДАТЬ ЛОББИ");
            Tab("browse", "НАЙТИ ИГРУ");
            Tab("code", "ПО КОДУ");
            Tab("ip", "ПО IP");
            var body = Ui.Col(content, "grow");
            switch (multiTab)
            {
                case "create":
                {
                    var cols = Ui.Row(body, "grow");
                    cols.style.alignItems = Align.FlexStart;
                    var l = Ui.Col(cols, "panel");
                    l.style.width = new Length(44, LengthUnit.Percent);
                    var name = Ui.Field(l, "НАЗВАНИЕ ЛОББИ", draft.LobbyName, 40);
                    name.RegisterValueChangedCallback(e => draft.LobbyName = e.newValue);
                    Ui.Choice(l, "ДОСТУП", new[] { "Открытое (в списке игр)", "Только друзья", "По приглашению" }, (int)draft.Privacy, v => draft.Privacy = (LobbyPrivacy)v);
                    Ui.Choice(l, "ИГРОКОВ", new[] { "2", "3", "4", "6", "8" }, new[] { 2, 3, 4, 6, 8 }.ToList().IndexOf(draft.MaxPlayers) is int i && i >= 0 ? i : 2,
                              v => draft.MaxPlayers = new[] { 2, 3, 4, 6, 8 }[v]);
                    Ui.Choice(l, "РЕЖИМ", new[] { "Кооператив + за Империю можно", "Только кооператив" }, draft.AllowEmpirePlayers ? 0 : 1, v => draft.AllowEmpirePlayers = v == 0);
                    var r = Ui.Col(cols, "panel");
                    r.style.marginLeft = 20;
                    r.style.flexGrow = 1;
                    MatchControls(r, draft, true);
                    var bar = Ui.Row(content);
                    BackButton(bar);
                    Ui.Box(bar, "spacer");
                    Ui.Button(bar, "СОЗДАТЬ ПО IP (порт 7777)", () => { session.HostLan(draft); if (session.Phase == SessionPhase.Lobby) Show(Screen.Lobby); });
                    var steam = Ui.Button(bar, "СОЗДАТЬ ЛОББИ STEAM", () => { session.HostSteam(draft); Show(Screen.Lobby); }, "btn", "btn-primary");
                    steam.SetEnabled(SteamBootstrap.Initialized);
                    break;
                }
                case "browse":
                {
                    var head = Ui.Row(body);
                    Ui.Text(head, "ОТКРЫТЫЕ ЛОББИ", "h3");
                    Ui.Box(head, "spacer");
                    Ui.Button(head, "ОБНОВИТЬ", Refresh, "btn", "btn-small");
                    var hdr = Ui.Row(body, "list-item");
                    Col(hdr, "НАЗВАНИЕ", 34); Col(hdr, "ХОСТ", 18); Col(hdr, "ФРОНТ", 18); Col(hdr, "СЛОЖНОСТЬ", 14); Col(hdr, "ИГРОКИ", 10);
                    listBox = Ui.Add(body, new ScrollView(), "panel-glass", "grow");
                    FillList();
                    Refresh();
                    var bar = Ui.Row(content);
                    BackButton(bar);
                    break;
                }
                case "code":
                {
                    var p = Ui.Col(body, "panel");
                    p.style.width = 560;
                    Ui.Text(p, "Попросите у хоста код лобби (кнопка «Копировать код» в его лобби) и вставьте сюда.", "small");
                    var code = Ui.Field(p, "КОД ЛОББИ", "", 24);
                    var bar = Ui.Row(content);
                    BackButton(bar);
                    Ui.Box(bar, "spacer");
                    Ui.Button(bar, "ВОЙТИ", () => { session.JoinSteamLobby(code.value); Show(Screen.Lobby); }, "btn", "btn-primary").SetEnabled(SteamBootstrap.Initialized);
                    break;
                }
                case "ip":
                {
                    var p = Ui.Col(body, "panel");
                    p.style.width = 560;
                    Ui.Text(p, "Хост создаёт игру «По IP». Для игры через интернет нужен проброс UDP-порта 7777 у хоста или общая VPN (Radmin VPN, ZeroTier, Tailscale).", "small").style.whiteSpace = WhiteSpace.Normal;
                    var ip = Ui.Field(p, "АДРЕС ХОСТА", Prefs.LastIp, 64);
                    var port = Ui.Field(p, "ПОРТ", NetSession.DefaultPort.ToString(), 6);
                    var bar = Ui.Row(content);
                    BackButton(bar);
                    Ui.Box(bar, "spacer");
                    Ui.Button(bar, "ПОДКЛЮЧИТЬСЯ", () =>
                    {
                        Prefs.LastIp = ip.value;
                        int.TryParse(port.value, out int pt);
                        session.JoinLan(ip.value, pt > 0 ? pt : NetSession.DefaultPort);
                        Show(Screen.Lobby);
                    }, "btn", "btn-primary");
                    break;
                }
            }
        }

        static void Col(VisualElement row, string text, float percent)
        {
            var l = Ui.Text(row, text, "tiny", "muted");
            l.style.width = new Length(percent, LengthUnit.Percent);
        }

        void Refresh()
        {
            refreshAt = Time.time + 15f;
            session.Lobby.RequestList();
        }

        void OnList(List<LobbyListing> list)
        {
            listings = list;
            FillList();
        }

        void FillList()
        {
            if (listBox == null) return;
            listBox.Clear();
            if (!SteamBootstrap.Initialized) { Ui.Text(listBox, "Steam не запущен — список лобби недоступен.", "muted"); return; }
            if (listings.Count == 0) { Ui.Text(listBox, "Открытых лобби не найдено. Создайте своё или нажмите «Обновить».", "muted"); return; }
            foreach (var l in listings.OrderByDescending(x => x.Players))
            {
                var row = Ui.Row(listBox, "list-item");
                Col(row, string.IsNullOrEmpty(l.Name) ? "Лобби" : l.Name, 34);
                Col(row, l.Host, 18);
                Col(row, l.Map, 18);
                Col(row, MatchSettings.DifficultyNames[Mathf.Clamp(l.Difficulty, 0, 3)], 14);
                Col(row, $"{l.Players}/{l.MaxPlayers}", 10);
                ulong id = l.Id;
                row.RegisterCallback<ClickEvent>(_ => { session.JoinSteamLobby(id); Show(Screen.Lobby); });
                foreach (var c in row.Children()) ((Label)c).RemoveFromClassList("muted");
            }
        }

        // ================================================================== лобби
        void BuildLobby()
        {
            var title = Header(content, session.Mode == SessionMode.Lan ? "ЛОББИ · ПО IP" : "ЛОББИ · STEAM", session.Settings.LobbyName);
            var cols = Ui.Row(content, "grow");
            cols.style.alignItems = Align.Stretch;
            var left = Ui.Col(cols, "panel");
            left.style.width = new Length(52, LengthUnit.Percent);
            Ui.Text(left, "ИГРОКИ", "h3");
            lobbyPlayers = Ui.Col(left);
            Ui.Box(left, "line");
            Ui.Text(left, "ЧАТ", "h3");
            chatLog = Ui.Add(left, new ScrollView(), "grow");
            chatLog.style.minHeight = 120;
            chatField = Ui.Field(left, null, "", 200);
            chatField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                session.SendChat(chatField.value);
                chatField.value = "";
                chatField.schedule.Execute(() => chatField.Focus());
            });
            var right = Ui.Col(cols, "panel");
            right.style.marginLeft = 20;
            right.style.flexGrow = 1;
            Ui.Text(right, "НАСТРОЙКИ БОЯ", "h3");
            lobbySettings = Ui.Col(right);
            Ui.Box(right, "spacer");
            if (session.Mode == SessionMode.Steam)
            {
                var codeRow = Ui.Row(right);
                Ui.Text(codeRow, "КОД ЛОББИ: ", "field-label");
                var code = Ui.Text(codeRow, session.LobbyCode, "accent");
                Ui.Button(codeRow, "КОПИРОВАТЬ", () => GUIUtility.systemCopyBuffer = session.LobbyCode, "btn", "btn-small");
                Ui.Button(right, "ПРИГЛАСИТЬ ДРУЗЕЙ", () => session.OpenInvites()).SetEnabled(SteamLobby.OverlayEnabled);
            }
            var bar = Ui.Row(content);
            Ui.Button(bar, "‹ ПОКИНУТЬ", () => { session.Leave(false); Show(Screen.Multi); }, "btn", "btn-danger");
            Ui.Box(bar, "spacer");
            Ui.Button(bar, "СТОРОНА: РЕСПУБЛИКА / ИМПЕРИЯ", () =>
            {
                var me = session.Local;
                if (me != null) session.SetTeam(me.Team == Team.Republic ? Team.Empire : Team.Republic);
            });
            readyBtn = Ui.Button(bar, "ГОТОВ", () => { var me = session.Local; if (me != null) session.SetReady(!me.Ready); });
            startBtn = Ui.Button(bar, "НАЧАТЬ БОЙ", () => session.StartMatch(), "btn", "btn-primary");
            RefreshLobby();
        }

        void RefreshLobby()
        {
            if (screen != Screen.Lobby || lobbyPlayers == null) return;
            lobbyPlayers.Clear();
            if (session.Phase == SessionPhase.Connecting || session.Phase == SessionPhase.Idle)
            {
                Ui.Text(lobbyPlayers, session.Phase == SessionPhase.Idle ? "Нет подключения." : "Подключение...", "muted");
            }
            foreach (var p in session.Players.OrderBy(x => x.Id))
            {
                var row = Ui.Row(lobbyPlayers, "list-item");
                var av = Ui.Box(row, "avatar");
                var tex = SteamBootstrap.Avatar(p.SteamId);
                if (tex) av.style.backgroundImage = new StyleBackground(tex);
                var info = Ui.Col(row, "grow");
                Ui.Text(info, p.Name + (p.IsLocal ? "  (вы)" : ""), "h2");
                var chips = Ui.Row(info);
                Ui.Text(chips, p.Team == Team.Republic ? "РЕСПУБЛИКА" : "ИМПЕРИЯ", "chip", p.Team == Team.Republic ? "rep" : "emp");
                if (p.Host) Ui.Text(chips, "ХОСТ", "chip", "chip-host");
                else Ui.Text(chips, p.Ready ? "ГОТОВ" : "НЕ ГОТОВ", "chip", p.Ready ? "chip-ready" : "");
                if (!p.Host && p.Ping > 0) Ui.Text(chips, $"{p.Ping} мс", "tiny", "muted");
                if (session.IsServer && !p.Host)
                {
                    byte id = p.Id;
                    Ui.Button(row, "ИСКЛЮЧИТЬ", () => session.Kick(id), "btn", "btn-small", "btn-danger");
                }
            }
            lobbySettings.Clear();
            if (session.IsServer) MatchControls(lobbySettings, session.Settings.Clone(), true);
            else MatchControls(lobbySettings, session.Settings, false);
            var me = session.Local;
            if (readyBtn != null)
            {
                readyBtn.text = me != null && me.Ready ? "НЕ ГОТОВ" : "ГОТОВ";
                Ui.Show(readyBtn, !session.IsServer);
            }
            if (startBtn != null)
            {
                Ui.Show(startBtn, session.IsServer);
                startBtn.SetEnabled(session.IsServer && session.AllReady);
                startBtn.text = session.AllReady ? "НАЧАТЬ БОЙ" : "ЖДЁМ ГОТОВНОСТИ";
            }
        }

        void OnLobbyChanged()
        {
            if (screen == Screen.Lobby)
            {
                // настройки хоста не перестраиваем на каждое обновление (иначе сбивается ввод)
                if (session.IsServer && lobbySettings != null && lobbySettings.childCount > 0)
                {
                    lobbyPlayers.Clear();
                    var keep = lobbySettings;
                    lobbySettings = new VisualElement();
                    RefreshLobby();
                    lobbySettings = keep;
                }
                else RefreshLobby();
            }
            if (session.Phase == SessionPhase.Lobby && screen != Screen.Lobby) Show(Screen.Lobby);
            if (session.Phase == SessionPhase.Idle && screen == Screen.Lobby) Show(Screen.Multi);
        }

        void OnChat(string from, string text)
        {
            chat.Add(string.IsNullOrEmpty(from) ? $"<color=#ffcd50>{text}</color>" : $"<b>{from}:</b> {text}");
            if (chat.Count > 60) chat.RemoveAt(0);
            if (chatLog == null) return;
            var l = Ui.Text(chatLog, chat[chat.Count - 1], "small");
            l.enableRichText = true;
            l.style.whiteSpace = WhiteSpace.Normal;
            ((ScrollView)chatLog).schedule.Execute(() => ((ScrollView)chatLog).scrollOffset = new Vector2(0, 99999));
        }

        void OnStatus(string s)
        {
            if (status != null) status.text = s ?? "";
        }

        // ================================================================== арсенал
        void BuildArsenal()
        {
            Header(content, "АРСЕНАЛ", "БОЙЦЫ И ВООРУЖЕНИЕ");
            var tabs = Ui.Row(content, "tabs");
            var body = Ui.Add(content, new ScrollView(), "grow");
            void Fill(Team t)
            {
                body.Clear();
                var grid = Ui.Row(body);
                grid.style.flexWrap = Wrap.Wrap;
                foreach (var u in Catalog.I.Units.Where(x => x.Team == t))
                {
                    var card = Ui.Col(grid, "unit-card");
                    card.style.width = 210;
                    var img = Ui.Box(card, "portrait");
                    if (u.Portrait) img.style.backgroundImage = new StyleBackground(u.Portrait);
                    Ui.Text(card, u.Title.ToUpper(), "squad-title");
                    Ui.Text(card, Names.Role[(int)u.Role], "tiny", "muted");
                    Ui.Text(card, $"{u.Weapon.Title} · урон {u.Weapon.Damage:0} · {u.Weapon.Rpm:0} в/мин", "tiny");
                    Ui.Text(card, $"Здоровье {u.Health:0} · Цена {u.Cost}", "cost");
                }
            }
            Ui.Button(tabs, "РЕСПУБЛИКА", () => Fill(Team.Republic), "tab");
            Ui.Button(tabs, "ИМПЕРИЯ", () => Fill(Team.Empire), "tab");
            Ui.Button(tabs, "СТРОИ И СТОЙКИ", () =>
            {
                body.Clear();
                var p = Ui.Col(body, "panel-glass");
                Ui.Text(p, "СТРОИ", "h3");
                Ui.Text(p, string.Join(" · ", Names.Formation), "small").style.whiteSpace = WhiteSpace.Normal;
                Ui.Text(p, "СТОЙКИ", "h3");
                Ui.Text(p, string.Join(" · ", Names.Stance), "small").style.whiteSpace = WhiteSpace.Normal;
                Ui.Text(p, "ПОХОДКИ", "h3");
                Ui.Text(p, string.Join(" · ", Names.Gait) + "  (строевым шагом отряд идёт в ногу)", "small").style.whiteSpace = WhiteSpace.Normal;
                Ui.Text(p, "ПРАВИЛА ОГНЯ", "h3");
                Ui.Text(p, string.Join(" · ", Names.Roe), "small");
                Ui.Text(p, "ПРИКАЗЫ", "h3");
                Ui.Text(p, string.Join(" · ", Names.Order), "small").style.whiteSpace = WhiteSpace.Normal;
            }, "tab");
            Fill(Team.Republic);
            var bar = Ui.Row(content);
            BackButton(bar);
        }

        // ================================================================== настройки
        void BuildSettings()
        {
            Header(content, "НАСТРОЙКИ", "ПАРАМЕТРЫ ИГРЫ");
            var cols = Ui.Row(content, "grow");
            cols.style.alignItems = Align.FlexStart;
            var g = Ui.Col(cols, "panel");
            g.style.width = new Length(32, LengthUnit.Percent);
            Ui.Text(g, "ГРАФИКА", "h3");
            var q = QualitySettings.names;
            Ui.Choice(g, "КАЧЕСТВО", q, Prefs.Quality, v => { Prefs.Quality = v; Prefs.Apply(false); });
            var res = UnityEngine.Screen.resolutions.Select(r => $"{r.width}×{r.height}").Distinct().ToArray();
            if (res.Length > 0)
            {
                int cur = Prefs.ResolutionIndex >= 0 ? Prefs.ResolutionIndex : res.Length - 1;
                Ui.Choice(g, "РАЗРЕШЕНИЕ", res, Mathf.Clamp(cur, 0, res.Length - 1), v =>
                {
                    var target = res[v];
                    var all = UnityEngine.Screen.resolutions;
                    for (int i = all.Length - 1; i >= 0; i--) if ($"{all[i].width}×{all[i].height}" == target) { Prefs.ResolutionIndex = i; break; }
                    Prefs.Apply(true);
                });
            }
            Ui.ToggleField(g, "Полный экран", Prefs.Fullscreen, v => { Prefs.Fullscreen = v; Prefs.Apply(true); });
            Ui.ToggleField(g, "Вертикальная синхронизация", Prefs.VSync, v => { Prefs.VSync = v; Prefs.Apply(false); });
            Ui.ToggleField(g, "Свечение (bloom)", Prefs.Bloom, v => { Prefs.Bloom = v; Prefs.Apply(false); });
            Ui.Choice(g, "ЛИМИТ FPS", new[] { "Без лимита", "30", "60", "120", "144" }, new[] { 0, 30, 60, 120, 144 }.ToList().IndexOf(Prefs.FpsLimit) is int fi && fi >= 0 ? fi : 0,
                      v => { Prefs.FpsLimit = new[] { 0, 30, 60, 120, 144 }[v]; Prefs.Apply(false); });
            var c = Ui.Col(cols, "panel");
            c.style.width = new Length(32, LengthUnit.Percent);
            c.style.marginLeft = 16;
            Ui.Text(c, "УПРАВЛЕНИЕ", "h3");
            Ui.SliderField(c, "ЧУВСТВИТЕЛЬНОСТЬ МЫШИ", 0.2f, 3f, Prefs.Sensitivity, v => { Prefs.Sensitivity = v; Prefs.Apply(false); });
            Ui.SliderField(c, "ПОЛЕ ЗРЕНИЯ", 50f, 90f, Prefs.Fov, v => { Prefs.Fov = v; Prefs.Apply(false); }, v => v.ToString("0") + "°");
            Ui.ToggleField(c, "Инвертировать ось Y", Prefs.InvertY, v => { Prefs.InvertY = v; Prefs.Apply(false); });
            Ui.ToggleField(c, "Подсказки клавиш", Prefs.Hints, v => Prefs.Hints = v);
            var a = Ui.Col(cols, "panel");
            a.style.flexGrow = 1;
            a.style.marginLeft = 16;
            Ui.Text(a, "ЗВУК", "h3");
            Ui.SliderField(a, "ОБЩАЯ ГРОМКОСТЬ", 0f, 1f, Prefs.Master, v => { Prefs.Master = v; Prefs.Apply(false); }, v => (v * 100).ToString("0") + "%");
            Ui.SliderField(a, "ЭФФЕКТЫ", 0f, 1f, Prefs.Effects, v => { Prefs.Effects = v; Prefs.Apply(false); }, v => (v * 100).ToString("0") + "%");
            Ui.SliderField(a, "ГОЛОСА БОЙЦОВ", 0f, 1f, Prefs.Voices, v => { Prefs.Voices = v; Prefs.Apply(false); }, v => (v * 100).ToString("0") + "%");
            var bar = Ui.Row(content);
            BackButton(bar);
        }

        void Update()
        {
            if (screen == Screen.Multi && multiTab == "browse" && Time.time > refreshAt) Refresh();
            if (Keys.Down(KeyCode.Escape) && screen != Screen.Main && screen != Screen.Lobby) Show(Screen.Main);
        }
    }
}
