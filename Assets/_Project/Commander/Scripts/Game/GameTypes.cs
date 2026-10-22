using System.Collections.Generic;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>Строй отряда.</summary>
    public enum FormationType : byte
    {
        Line, DoubleLine, Column, DoubleColumn, Wedge, Vee, Diamond, Square, Circle, EchelonLeft, EchelonRight, Skirmish
    }

    /// <summary>Стойка: как бойцы стоят на месте.</summary>
    public enum Stance : byte
    {
        Combat,     // оружие у плеча, готовы стрелять
        Ready,      // оружие опущено (на изготовку)
        AtEase,     // «вольно»
        Attention,  // «смирно»
        Crouch,     // на колене
        Prone,      // лёжа
    }

    /// <summary>Походка: как бойцы перемещаются.</summary>
    public enum Gait : byte
    {
        Walk,       // шагом, оружие опущено
        Tactical,   // шагом с оружием у плеча (стреляют на ходу)
        Patrol,     // патрульный шаг
        March,      // строевой шаг, в ногу
        Run,        // бегом
        CrouchWalk, // пригнувшись
    }

    /// <summary>Правила открытия огня.</summary>
    public enum Roe : byte { FireAtWill, ReturnFire, HoldFire }

    public enum OrderType : byte
    {
        Hold,       // стоять (держать позицию)
        Move,       // выдвинуться в точку
        Follow,     // следовать за командиром
        Attack,     // атаковать район
        Capture,    // захватить командный пункт
        Defend,     // оборонять район (занять укрытия)
    }

    public static class Names
    {
        public static readonly string[] Formation =
            { "Шеренга", "Две шеренги", "Колонна", "Колонна по двое", "Клин", "Обратный клин", "Ромб", "Каре", "Круговая оборона", "Уступ влево", "Уступ вправо", "Рассыпной" };
        public static readonly string[] Stance = { "Боевая", "На изготовку", "Вольно", "Смирно", "На колене", "Лёжа" };
        public static readonly string[] Gait = { "Шагом", "Тактический шаг", "Патрульный", "Строевой", "Бегом", "Пригнувшись" };
        public static readonly string[] Roe = { "Огонь по готовности", "Только ответный", "Не стрелять" };
        public static readonly string[] Order = { "Держать позицию", "Выдвинуться", "За мной", "Атаковать", "Захватить пункт", "Оборонять" };
        public static readonly string[] Role =
            { "Стрелок", "Пулемётчик", "Снайпер", "Ракетчик", "Инженер", "Офицер", "Элита", "Командир" };

        public static string Of(FormationType f) => Formation[(int)f];
        public static string Of(Stance s) => Stance[(int)s];
        public static string Of(Gait g) => Gait[(int)g];
        public static string Of(Roe r) => Roe[(int)r];
        public static string Of(OrderType o) => Order[(int)o];
    }

    public enum Role : byte { Rifleman, Heavy, Sniper, Rocket, Engineer, Officer, Elite, Commander }

    /// <summary>
    /// Клипы анимаций: индекс передаётся по сети байтом. Имена совпадают с клипами FBX (anims2 + anims3).
    /// </summary>
    public static class Clips
    {
        public static readonly string[] Names =
        {
            "Idle", "Walk", "WalkAim", "WalkBack", "StrafeL", "StrafeR", "Run", "Aim", "AimIn", "Crouch", "CrouchAim",
            "Fire", "FireAuto", "Reload", "Hit", "HitBack", "Ride", "Drive",
            "Attention", "AtEase", "PortArms", "March", "Patrol", "CrouchWalk", "CrouchIdle", "Prone", "ProneAim", "ProneFire",
            "GoProne", "StandUp", "Point", "Halt", "Salute",
            "Death_Back", "Death_Forward", "Death_Knees", "Death_Spin", "Death_Headshot", "Death_Blast",
        };

        static Dictionary<string, byte> index;

        public static byte Id(string name)
        {
            if (index == null)
            {
                index = new Dictionary<string, byte>();
                for (int i = 0; i < Names.Length; i++) index[Names[i]] = (byte)i;
            }
            return index.TryGetValue(name, out var b) ? b : (byte)0;
        }

        public static string Name(byte id) => id < Names.Length ? Names[id] : "Idle";

        /// <summary>Чем заменить клип, если у модели его нет (модели v2 без строевых клипов).</summary>
        public static readonly Dictionary<string, string> Fallback = new Dictionary<string, string>
        {
            { "Attention", "Idle" }, { "AtEase", "Idle" }, { "PortArms", "Idle" }, { "March", "Walk" }, { "Patrol", "Walk" },
            { "CrouchWalk", "WalkAim" }, { "CrouchIdle", "Crouch" }, { "Prone", "Crouch" }, { "ProneAim", "CrouchAim" },
            { "ProneFire", "Fire" }, { "GoProne", "Crouch" }, { "StandUp", "Idle" }, { "Point", "Idle" }, { "Halt", "Idle" },
            { "Salute", "Idle" }, { "WalkAim", "Walk" }, { "WalkBack", "Walk" }, { "StrafeL", "Walk" }, { "StrafeR", "Walk" },
            { "CrouchAim", "Crouch" }, { "Crouch", "Idle" }, { "Aim", "Idle" }, { "FireAuto", "Fire" },
        };
    }

    /// <summary>Скорости походок (м/с) и клипы.</summary>
    public static class Gaits
    {
        public static float Speed(Gait g) => g switch
        {
            Gait.Walk => 1.5f,
            Gait.Tactical => 1.1f,
            Gait.Patrol => 1.15f,
            Gait.March => 1.35f,
            Gait.Run => 4.3f,
            Gait.CrouchWalk => 0.95f,
            _ => 1.5f,
        };

        public static string Clip(Gait g) => g switch
        {
            Gait.Walk => "Walk",
            Gait.Tactical => "WalkAim",
            Gait.Patrol => "Patrol",
            Gait.March => "March",
            Gait.Run => "Run",
            Gait.CrouchWalk => "CrouchWalk",
            _ => "Walk",
        };

        /// <summary>Скорость, под которую записан клип (для подгонки темпа анимации).</summary>
        public static float ClipSpeed(string clip) => clip switch
        {
            "Run" => 4.4f,
            "Walk" => 1.45f,
            "Patrol" => 1.1f,
            "March" => 1.35f,
            "CrouchWalk" => 0.9f,
            "WalkAim" => 1.0f,
            "WalkBack" => 0.85f,
            "StrafeL" => 0.75f,
            "StrafeR" => 0.75f,
            _ => 1.4f,
        };

        public static string StanceClip(Stance s, bool combat) => s switch
        {
            Stance.Combat => "Aim",
            Stance.Ready => "Idle",
            Stance.AtEase => "AtEase",
            Stance.Attention => "Attention",
            Stance.Crouch => combat ? "CrouchAim" : "CrouchIdle",
            Stance.Prone => combat ? "ProneAim" : "Prone",
            _ => "Idle",
        };
    }

    public static class TeamColors
    {
        public static readonly Color Republic = new Color(0.25f, 0.55f, 1f);
        public static readonly Color Empire = new Color(1f, 0.22f, 0.15f);
        public static readonly Color BoltBlue = new Color(0.2f, 0.45f, 1f);
        public static readonly Color BoltRed = new Color(1f, 0.12f, 0.08f);

        /// <summary>Цвета отрядов/игроков (метки на карте).</summary>
        public static readonly Color[] Squad =
        {
            new Color(0.3f, 0.65f, 1f), new Color(1f, 0.62f, 0.15f), new Color(0.35f, 0.9f, 0.45f), new Color(0.95f, 0.85f, 0.25f),
            new Color(0.75f, 0.45f, 1f), new Color(0.2f, 0.9f, 0.9f), new Color(1f, 0.45f, 0.65f), new Color(0.85f, 0.85f, 0.85f),
        };
    }
}
