using System.Collections.Generic;
using System.Linq;
using SW.Battle;
using SW.Commander.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace SW.Commander.UI
{
    /// <summary>Экраны боя: казарма (B), высадка после гибели, пауза (Esc), итоги, ожидание игроков, справка (F1).</summary>
    public partial class BattleHud
    {
        VisualElement army, deploy, pause, results, waiting, help;
        enum Open { None, Army, Pause, Help }
        Open open;
        bool deployShown, resultsShown;
        int deployPost = -1;
        Label deployTimer, waitText;
        Button deployBtn;
        // казарма
        ushort armySquad;
        readonly HashSet<ushort> armySelected = new HashSet<ushort>();
        int recruitType = -1, recruitCount = 4, recruitPost = -1;
        float armyRefreshAt;

        public bool AnyScreenOpen => open != Open.None || deployShown || resultsShown || (waiting != null && !waiting.ClassListContains("hidden"));

        void BuildScreens()
        {
            army = Ui.Box(root, "screen", "hidden");
            deploy = Ui.Box(root, "screen", "hidden");
            pause = Ui.Box(root, "screen", "hidden");
            results = Ui.Box(root, "screen", "hidden");
            help = Ui.Box(root, "screen", "hidden");
            waiting = Ui.Box(root, "screen");
            Ui.Box(waiting, "shade");
            var wc = Ui.Col(waiting, "screen", "center");
            Ui.Text(wc, "STAR WARS · КОМАНДИР КЛОНОВ", "brand-small");
            Ui.Text(wc, "ВЫСАДКА НА ФЕЛЛУСИЮ", "h1");
            Ui.Box(wc, "loading-bar-bg");
            waitText = Ui.Text(wc, "Ожидание остальных игроков...", "small", "muted");
            waitText.style.marginTop = 14;
            var tip = Ui.Text(wc, Tips[Random.Range(0, Tips.Length)], "tip");
        }

        static readonly string[] Tips =
        {
            "Строевым шагом отряд идёт в ногу — красиво, но в бою бойцы сами перейдут на тактический шаг.",
            "Отряд в стойке «Лёжа» стреляет точнее и реже попадает под огонь, но медленно реагирует на манёвры.",
            "Захваченные командные пункты дают снабжение и становятся точками высадки подкреплений.",
            "Генераторы щита почти не берут бластеры — используйте ракетчиков и термальные детонаторы.",
            "Дважды нажмите F, чтобы выбранные отряды следовали за вами.",
            "На тактической карте (Tab) протяните правой кнопкой — отряд встанет строем в нужном направлении.",
            "«Только ответный огонь» поможет пройти мимо патруля, не выдав себя.",
            "Круговая оборона и каре — лучшие строи, чтобы удержать захваченный пункт.",
        };

        void Toggle(Open o)
        {
            open = open == o ? Open.None : o;
            Ui.Show(army, open == Open.Army);
            Ui.Show(pause, open == Open.Pause);
            Ui.Show(help, open == Open.Help);
            if (open == Open.Army) BuildArmy();
            if (open == Open.Pause) BuildPause();
            if (open == Open.Help) BuildHelp();
            if (open != Open.None && TacticalView.I && TacticalView.I.Active) TacticalView.I.Exit();
            CommanderController.Blocked = open != Open.None || deployShown || resultsShown;
        }

        void UpdateScreens()
        {
            var s = NetSession.Instance;
            bool began = s && s.Phase == SessionPhase.InGame;
            Ui.Show(waiting, !began);
            if (!began)
            {
                if (waitText != null) waitText.text = string.IsNullOrEmpty(BattleNet.NavStatus) ? "Ожидание остальных игроков..." : BattleNet.NavStatus;
                return;
            }
            if (!Keys.Typing)
            {
                if (Keys.Down(KeyCode.Escape))
                {
                    if (TacticalView.I && TacticalView.I.Active) TacticalView.I.Exit();
                    else if (open != Open.None) Toggle(open);
                    else Toggle(Open.Pause);
                }
                if (Keys.Down(KeyCode.B) && !resultsShown) Toggle(Open.Army);
                if (Keys.Down(KeyCode.F1)) Toggle(Open.Help);
            }
            if (open == Open.Army && Time.unscaledTime > armyRefreshAt) { armyRefreshAt = Time.unscaledTime + 1f; RefreshArmyLive(); }
            if (deployShown) UpdateDeploy();
        }

        // ================================================================== казарма
        VisualElement armySquads, armySoldiers, armyRecruit, armyConfig;
        Label armyReq;

        void BuildArmy()
        {
            army.Clear();
            Ui.Box(army, "shade");
            var c = Ui.Box(army, "content");
            c.style.top = new Length(6, LengthUnit.Percent);
            c.style.bottom = new Length(6, LengthUnit.Percent);
            var head = Ui.Row(c);
            var ht = Ui.Col(head);
            Ui.Text(ht, "КАЗАРМА", "brand-small");
            Ui.Text(ht, "ВАША АРМИЯ", "h1");
            Ui.Box(head, "spacer");
            armyReq = Ui.Text(head, "", "h2", "accent");
            Ui.Button(head, "ЗАКРЫТЬ (B)", () => Toggle(Open.Army));
            Ui.Box(c, "line");
            var cols = Ui.Row(c, "grow");
            cols.style.alignItems = Align.Stretch;
            // 1: отряды
            var col1 = Ui.Col(cols, "panel");
            col1.style.width = new Length(22, LengthUnit.Percent);
            Ui.Text(col1, "ОТРЯДЫ", "h3");
            armySquads = Ui.Add(col1, new ScrollView(), "grow");
            Ui.Button(col1, "+ НОВЫЙ ОТРЯД ИЗ ВЫБРАННЫХ", () =>
            {
                if (armySelected.Count == 0) { Notice("Выберите бойцов в списке", new Color(1f, 0.7f, 0.3f)); return; }
                B.SendSquadEdit(BattleNet.EditOp.CreateSquad, 0, armySelected.ToList());
                armySelected.Clear();
            }, "btn", "btn-small");
            // 2: бойцы и настройки отряда
            var col2 = Ui.Col(cols, "panel");
            col2.style.width = new Length(36, LengthUnit.Percent);
            col2.style.marginLeft = 12;
            armyConfig = Ui.Col(col2);
            Ui.Text(col2, "БОЙЦЫ ОТРЯДА", "h3");
            armySoldiers = Ui.Add(col2, new ScrollView(), "grow");
            var actions = Ui.Row(col2);
            actions.style.flexWrap = Wrap.Wrap;
            Ui.Button(actions, "ВЫБРАТЬ ВСЕХ", () =>
            {
                if (B.Squads.TryGetValue(armySquad, out var sq)) foreach (var m in sq.Members) if (m && !m.Dead) armySelected.Add(m.NetId);
                FillSoldiers();
            }, "btn", "btn-small");
            Ui.Button(actions, "ПЕРЕВЕСТИ В...", () => TransferMenu(actions), "btn", "btn-small");
            Ui.Button(actions, "УВОЛИТЬ (+50%)", () =>
            {
                if (armySelected.Count == 0) return;
                B.SendSquadEdit(BattleNet.EditOp.Dismiss, armySquad, armySelected.ToList());
                armySelected.Clear();
            }, "btn", "btn-small", "btn-danger");
            // 3: набор
            var col3 = Ui.Col(cols, "panel");
            col3.style.flexGrow = 1;
            col3.style.marginLeft = 12;
            Ui.Text(col3, "НАБОР ПОДКРЕПЛЕНИЙ", "h3");
            armyRecruit = Ui.Col(col3, "grow");
            if (armySquad == 0 || !B.Squads.ContainsKey(armySquad)) armySquad = B.MySquads.FirstOrDefault()?.Id ?? 0;
            FillSquads();
            FillConfig();
            FillSoldiers();
            FillRecruit();
        }

        void RefreshArmyLive()
        {
            if (armyReq != null) armyReq.text = $"СНАБЖЕНИЕ {B.Requisition}   ·   БОЙЦОВ {B.UnitCount}/{B.UnitCap}";
            FillSquads();
            FillSoldiers();
        }

        void FillSquads()
        {
            if (armySquads == null) return;
            armySquads.Clear();
            int i = 1;
            foreach (var sq in B.MySquads)
            {
                var row = Ui.Row(armySquads, "list-item");
                row.style.borderLeftColor = TeamColors.Squad[sq.Color % TeamColors.Squad.Length];
                if (sq.Id == armySquad) row.AddToClassList("list-item-selected");
                var info = Ui.Col(row, "grow");
                Ui.Text(info, $"{i}. {sq.Name}", "squad-title");
                Ui.Text(info, $"{sq.Members.Count(m => m && !m.Dead)} бойцов · {Names.Of(sq.Order).ToLower()}", "squad-info");
                ushort id = sq.Id;
                row.RegisterCallback<ClickEvent>(_ => { armySquad = id; armySelected.Clear(); FillSquads(); FillConfig(); FillSoldiers(); });
                if (sq.Members.Count == 0)
                    Ui.Button(row, "✕", () => B.SendSquadEdit(BattleNet.EditOp.DeleteSquad, id), "btn", "btn-small");
                i++;
            }
        }

        void FillConfig()
        {
            if (armyConfig == null) return;
            armyConfig.Clear();
            if (!B.Squads.TryGetValue(armySquad, out var sq)) { Ui.Text(armyConfig, "Отряд не выбран", "muted"); return; }
            var name = Ui.Field(armyConfig, "НАЗВАНИЕ", sq.Name, 24);
            name.RegisterCallback<FocusOutEvent>(_ => { if (name.value != sq.Name) { sq.Name = name.value; B.SendSquadConfig(sq); } });
            var colors = Ui.Row(armyConfig);
            colors.style.marginTop = 6;
            for (int i = 0; i < TeamColors.Squad.Length; i++)
            {
                byte ci = (byte)i;
                var sw = Ui.Button(colors, "", () => { sq.Color = ci; B.SendSquadConfig(sq); FillSquads(); }, "btn", "btn-small");
                sw.style.width = 22; sw.style.height = 18;
                sw.style.backgroundColor = TeamColors.Squad[i];
                if (sq.Color == i) { sw.style.borderTopColor = sw.style.borderBottomColor = sw.style.borderLeftColor = sw.style.borderRightColor = Color.white; }
            }
            Ui.Choice(armyConfig, "СТРОЙ", Names.Formation, (int)sq.Formation, v => { sq.SetFormation((FormationType)v); B.SendSquadConfig(sq); });
            Ui.SliderField(armyConfig, "ИНТЕРВАЛ", 1f, 6f, sq.Spacing, v => { sq.Spacing = v; B.SendSquadConfig(sq); }, v => v.ToString("0.0") + " м");
            Ui.Choice(armyConfig, "СТОЙКА", Names.Stance, (int)sq.Stance, v => { sq.Stance = (Stance)v; B.SendSquadConfig(sq); });
            Ui.Choice(armyConfig, "ПОХОДКА", Names.Gait, (int)sq.Gait, v => { sq.Gait = (Gait)v; B.SendSquadConfig(sq); });
            Ui.Choice(armyConfig, "ПРАВИЛА ОГНЯ", Names.Roe, (int)sq.Roe, v => { sq.Roe = (Roe)v; B.SendSquadConfig(sq); });
            Ui.Box(armyConfig, "line");
        }

        void FillSoldiers()
        {
            if (armySoldiers == null) return;
            armySoldiers.Clear();
            if (!B.Squads.TryGetValue(armySquad, out var sq)) return;
            foreach (var m in sq.Members)
            {
                if (!m || m.Dead) continue;
                var row = Ui.Row(armySoldiers, "soldier-row");
                if (armySelected.Contains(m.NetId)) row.AddToClassList("soldier-row-selected");
                var pic = Ui.Box(row);
                pic.style.width = 26; pic.style.height = 32;
                pic.style.marginRight = 8;
                if (m.Type?.Portrait) pic.style.backgroundImage = new StyleBackground(m.Type.Portrait);
                var info = Ui.Col(row, "grow");
                Ui.Text(info, m.Type?.Title ?? m.name, "small");
                Ui.Text(info, $"{Names.Role[(int)(m.Type?.Role ?? Role.Rifleman)]} · {m.Type?.Weapon.Title}", "tiny", "muted");
                var hb = Ui.Box(row, "health-mini");
                var hf = Ui.Box(hb, "health-mini-fill");
                hf.style.width = new Length(Mathf.Clamp01(m.Health / m.MaxHealth) * 100f, LengthUnit.Percent);
                ushort id = m.NetId;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (!armySelected.Remove(id)) armySelected.Add(id);
                    FillSoldiers();
                });
            }
        }

        void TransferMenu(VisualElement near)
        {
            if (armySelected.Count == 0) { Notice("Выберите бойцов в списке", new Color(1f, 0.7f, 0.3f)); return; }
            var menu = Ui.Col(army, "panel");
            menu.style.position = Position.Absolute;
            menu.style.left = new Length(30, LengthUnit.Percent);
            menu.style.top = new Length(40, LengthUnit.Percent);
            menu.style.width = 360;
            Ui.Text(menu, "ПЕРЕВЕСТИ В ОТРЯД", "h3");
            foreach (var sq in B.MySquads)
            {
                if (sq.Id == armySquad) continue;
                ushort id = sq.Id;
                Ui.Button(menu, sq.Name, () =>
                {
                    B.SendSquadEdit(BattleNet.EditOp.MoveSoldiers, id, armySelected.ToList());
                    armySelected.Clear();
                    menu.RemoveFromHierarchy();
                });
            }
            Ui.Button(menu, "ОТМЕНА", () => menu.RemoveFromHierarchy(), "btn", "btn-small");
        }

        void FillRecruit()
        {
            armyRecruit.Clear();
            var grid = Ui.Add(armyRecruit, new ScrollView(), "grow");
            var wrap = Ui.Row(grid);
            wrap.style.flexWrap = Wrap.Wrap;
            var cat = Catalog.I;
            for (int i = 0; i < cat.Units.Count; i++)
            {
                var u = cat.Units[i];
                if (u.Team != B.LocalTeam || !u.Recruitable || !u.Prefab) continue;
                int ti = i;
                var card = Ui.Col(wrap, "unit-card");
                if (recruitType == ti) card.AddToClassList("unit-card-selected");
                var img = Ui.Box(card, "portrait");
                if (u.Portrait) img.style.backgroundImage = new StyleBackground(u.Portrait);
                Ui.Text(card, u.Title, "small");
                Ui.Text(card, $"{u.Weapon.Title}", "tiny", "muted");
                Ui.Text(card, $"{u.Cost} снаб.", "cost");
                card.RegisterCallback<ClickEvent>(_ => { recruitType = ti; FillRecruit(); });
            }
            var bar = Ui.Col(armyRecruit);
            Ui.Choice(bar, "КОЛИЧЕСТВО", new[] { "1", "2", "4", "6", "8", "12" }, new[] { 1, 2, 4, 6, 8, 12 }.ToList().IndexOf(recruitCount) is int ci && ci >= 0 ? ci : 2,
                      v => recruitCount = new[] { 1, 2, 4, 6, 8, 12 }[v]);
            var posts = CommandPost.All.Where(c => c.OwnedBy(B.LocalTeam)).OrderBy(c => c.Id).ToList();
            var names = posts.Select(p => $"{p.Letter} — {p.Title}").ToArray();
            if (names.Length > 0)
            {
                int pi = Mathf.Max(0, posts.FindIndex(p => p.Id == recruitPost));
                recruitPost = posts[pi].Id;
                Ui.Choice(bar, "ТОЧКА ВЫСАДКИ", names, pi, v => recruitPost = posts[v].Id);
            }
            var buttons = Ui.Row(bar);
            Ui.Button(buttons, "В ВЫБРАННЫЙ ОТРЯД", () => Recruit(armySquad), "btn");
            Ui.Button(buttons, "НОВЫМ ОТРЯДОМ", () => Recruit(0), "btn", "btn-primary");
            if (recruitType >= 0)
            {
                var u = cat.Get(recruitType);
                Ui.Text(bar, $"Итого: {u.Cost * recruitCount} снабжения", "small", "accent");
            }
        }

        void Recruit(ushort squad)
        {
            if (recruitType < 0) { Notice("Выберите тип бойца", new Color(1f, 0.7f, 0.3f)); return; }
            B.SendRecruit(recruitType, recruitCount, squad, recruitPost);
            Audio.Ui("reload", 0.4f);
        }

        // ================================================================== высадка
        void OnDied()
        {
            // в RTS-режиме командование продолжается; высадка — по Tab или кнопке
            if (TacticalView.I && TacticalView.I.Active)
            {
                Notice("Командир погиб. Армия ждёт приказов. Tab — высадить нового командира", new Color(1f, 0.55f, 0.35f));
                return;
            }
            ShowDeploy();
        }

        void ShowDeploy()
        {
            deployShown = true;
            if (TacticalView.I && TacticalView.I.Active) TacticalView.I.Exit();
            if (open != Open.None) Toggle(open);
            BuildDeploy();
            Ui.Show(deploy, true);
            CommanderController.Blocked = true;
        }

        void OnSpawned(Soldier s)
        {
            bool was = deployShown;
            deployShown = false;
            Ui.Show(deploy, false);
            CommanderController.Blocked = open != Open.None || resultsShown || (TacticalView.I && TacticalView.I.Active);
            if (was && TacticalView.I && !TacticalView.I.Active) TacticalView.I.Enter();   // после высадки — снова над полем боя
        }

        void BuildDeploy()
        {
            deploy.Clear();
            var left = Ui.Box(deploy, "vignette-left");
            left.style.width = new Length(34, LengthUnit.Percent);
            var c = Ui.Col(deploy, "menu-column");
            c.style.width = new Length(28, LengthUnit.Percent);
            Ui.Text(c, "КОМАНДИР ПОГИБ", "brand-small");
            Ui.Text(c, "ВЫСАДКА", "h1");
            Ui.Box(c, "line-accent");
            Ui.Text(c, "Выберите точку высадки:", "small", "muted");
            var posts = CommandPost.All.Where(p => p.OwnedBy(B.LocalTeam)).OrderBy(p => p.Id).ToList();
            if (deployPost < 0 || !posts.Any(p => p.Id == deployPost)) deployPost = posts.FirstOrDefault()?.Id ?? -1;
            foreach (var p in posts)
            {
                int id = p.Id;
                var b = Ui.MenuItem(c, $"{p.Letter} · {p.Title.ToUpper()}", () => { deployPost = id; BuildDeploy(); });
                if (id == deployPost) b.AddToClassList("selected");
                if (p.Contested) b.text += "  (ИДЁТ БОЙ)";
            }
            deployTimer = Ui.Text(c, "", "h2");
            deployTimer.style.marginTop = 20;
            deployBtn = Ui.Button(c, "ВЫСАДИТЬСЯ", () => B.SendRespawn(deployPost), "btn", "btn-primary");
        }

        void UpdateDeploy()
        {
            float left = B.RespawnLeft;
            if (deployTimer != null) deployTimer.text = left > 0.05f ? $"ПОДКРЕПЛЕНИЕ ЧЕРЕЗ {Mathf.CeilToInt(left)} С" : "ГОТОВ К ВЫСАДКЕ";
            deployBtn?.SetEnabled(left <= 0.05f);
            // командир снова жив — экран закрываем
            if (B.LocalCommander && !B.LocalCommander.Dead && B.LocalCommander.GetComponent<CommanderController>()) OnSpawned(B.LocalCommander);
        }

        // ================================================================== пауза
        void BuildPause()
        {
            pause.Clear();
            Ui.Box(pause, "shade");
            var c = Ui.Col(pause, "menu-column");
            Ui.Text(c, "ПАУЗА", "brand-small");
            Ui.Text(c, "КОМАНДИР КЛОНОВ", "h1");
            Ui.Box(c, "line-accent");
            Ui.MenuItem(c, "ПРОДОЛЖИТЬ", () => Toggle(Open.Pause));
            Ui.MenuItem(c, "СПРАВКА ПО УПРАВЛЕНИЮ", () => { Toggle(Open.Pause); Toggle(Open.Help); });
            var set = Ui.Col(c, "panel");
            set.style.marginTop = 16;
            set.style.width = 420;
            Ui.SliderField(set, "ЧУВСТВИТЕЛЬНОСТЬ МЫШИ", 0.2f, 3f, Prefs.Sensitivity, v => { Prefs.Sensitivity = v; Prefs.Apply(false); });
            Ui.SliderField(set, "ГРОМКОСТЬ", 0f, 1f, Prefs.Master, v => { Prefs.Master = v; Prefs.Apply(false); }, v => (v * 100).ToString("0") + "%");
            Ui.ToggleField(set, "Подсказки клавиш", Prefs.Hints, v => Prefs.Hints = v);
            var s = NetSession.Instance;
            if (s && s.IsServer && s.Online) Ui.MenuItem(c, "ЗАВЕРШИТЬ БОЙ (В ЛОББИ)", () => s.ReturnToLobby());
            if (s && s.Mode == SessionMode.Steam) Ui.MenuItem(c, "ПРИГЛАСИТЬ ДРУЗЕЙ", () => s.OpenInvites());
            Ui.MenuItem(c, "ВЫЙТИ В МЕНЮ", () => s?.Leave());
        }

        void BuildHelp()
        {
            help.Clear();
            Ui.Box(help, "shade");
            var c = Ui.Box(help, "content");
            Ui.Text(c, "СПРАВКА", "brand-small");
            Ui.Text(c, "УПРАВЛЕНИЕ КОМАНДИРОМ И АРМИЕЙ", "h1");
            Ui.Box(c, "line-accent");
            var cols = Ui.Row(c);
            cols.style.alignItems = Align.FlexStart;
            void Block(string title, string[] lines)
            {
                var p = Ui.Col(cols, "panel");
                p.style.marginRight = 14;
                p.style.width = new Length(31, LengthUnit.Percent);
                Ui.Text(p, title, "h3");
                foreach (var l in lines) Ui.Text(p, l, "small").style.whiteSpace = WhiteSpace.Normal;
            }
            Block("КОМАНДИР", new[]
            {
                "WASD — движение, Shift — бег", "Мышь — обзор, ПКМ — прицел", "ЛКМ — огонь, R — перезарядка", "G — термальный детонатор",
                "C / Ctrl — присесть, Z — лечь", "Ctrl+V — сменить плечо камеры", "Enter — чат (в мультиплеере)",
            });
            Block("ОТРЯДЫ", new[]
            {
                "1–9 — выбрать отряд, Shift+цифра — добавить", "` или 0 — все отряды", "F — выдвинуться к точке прицела", "F дважды — следовать за мной",
                "T — атаковать район, Y — оборонять", "H — стоять на месте", "Приказ у командного пункта — захват",
            });
            Block("СТРОЙ И ТАКТИКА", new[]
            {
                "Q (держать) — строй, мышью выбрать", "E (держать) — стойка: боевая, вольно, смирно, лёжа…", "X (держать) — походка: шагом, строевым, бегом…",
                "V — правила огня", "Tab — тактическая карта (как в RTS)", "B — казарма: набор, отряды, перевод бойцов", "Esc — пауза",
            });
            Ui.Button(c, "ЗАКРЫТЬ", () => Toggle(Open.Help));
        }

        // ================================================================== итоги
        void OnMatchEnded(Team winner)
        {
            resultsShown = true;
            if (open != Open.None) Toggle(open);
            if (TacticalView.I && TacticalView.I.Active) TacticalView.I.Exit();
            results.Clear();
            Ui.Box(results, "shade");
            var c = Ui.Col(results, "screen", "center");
            bool won = winner == B.LocalTeam;
            Ui.Text(c, winner == Team.Republic ? "РЕСПУБЛИКА ПОБЕЖДАЕТ" : "ИМПЕРИЯ ПОБЕЖДАЕТ", "brand-small");
            Ui.Text(c, won ? "ПОБЕДА" : "ПОРАЖЕНИЕ", "title");
            Ui.Text(c, winner == Team.Republic ? "Командный центр Империи уничтожен. Феллусия свободна." : "Республиканская база потеряна.", "small", "muted");
            var table = Ui.Col(c, "panel");
            table.style.marginTop = 24;
            table.style.width = 640;
            var hdr = Ui.Row(table);
            foreach (var h in new[] { "ИГРОК", "УБИТО", "ПОТЕРИ", "ПУНКТЫ" }) { var l = Ui.Text(hdr, h, "field-label"); l.style.width = new Length(25, LengthUnit.Percent); }
            var s = NetSession.Instance;
            foreach (var p in s ? s.Players : new List<PlayerInfo>())
            {
                var row = Ui.Row(table, "list-item");
                var st = B.StatsOf(p.Id);
                foreach (var v in new[] { p.Name, st.kills.ToString(), st.losses.ToString(), st.captures.ToString() })
                {
                    var l = Ui.Text(row, v, "small");
                    l.style.width = new Length(25, LengthUnit.Percent);
                }
            }
            var bar = Ui.Row(c);
            bar.style.marginTop = 20;
            if (s && s.IsServer && s.Online) Ui.Button(bar, "В ЛОББИ", () => s.ReturnToLobby(), "btn", "btn-primary");
            Ui.Button(bar, "В ГЛАВНОЕ МЕНЮ", () => s?.Leave());
            Ui.Show(results, true);
            CommanderController.Blocked = true;
        }
    }
}
