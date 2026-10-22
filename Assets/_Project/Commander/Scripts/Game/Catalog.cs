using System;
using System.Collections.Generic;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    public enum BoltKind : byte { Blue, Red, BlueHeavy, RedHeavy, Rocket, TurretBlue, TurretRed, WalkerRed, WalkerBlue }

    [Serializable]
    public class WeaponDef
    {
        public string Id = "E11";
        public string Title = "E-11";
        public float Damage = 20f;
        public float Rpm = 400f;
        public int Magazine = 40;
        public float Range = 70f;
        public float Spread = 1.2f;            // градусы (у ИИ умножается на разброс от дистанции/движения)
        public BoltKind Bolt = BoltKind.Red;
        public string Sound = "e11";
        public float ReloadTime = 2.0f;
        public float Splash;                   // > 0 — взрыв
        public float BoltSpeed = 90f;

        public float Interval => 60f / Mathf.Max(1f, Rpm);
    }

    [Serializable]
    public class UnitType
    {
        public string Id;
        public string Title;
        public Team Team;
        public Role Role;
        public GameObject Prefab;
        public Texture2D Portrait;
        public int Cost = 60;
        public float Health = 100f;
        public float SpeedMul = 1f;
        public float Accuracy = 1f;
        public WeaponDef Weapon = new WeaponDef();
        public Vector3 Muzzle = new Vector3(0, 0.7f, 0.065f);   // в пространстве кости Weapon.R
        public bool Recruitable = true;
        public string[] ClipNames = new string[0];
        public float[] ClipLengths = new float[0];
        public string Description = "";
    }

    /// <summary>
    /// Каталог юнитов и оружия (ScriptableObject в Resources/Commander/Catalog, собирается редактором).
    /// Порядок типов одинаков у всех (индекс передаётся по сети).
    /// </summary>
    public class Catalog : ScriptableObject
    {
        public List<UnitType> Units = new List<UnitType>();
        public GameObject CommanderRepublic, CommanderEmpire;
        public GameObject SentryRepublic, SentryEmpire;
        public GameObject ThermalDetonator;
        public List<AudioClip> Sounds = new List<AudioClip>();

        static Catalog inst;

        public static Catalog I
        {
            get
            {
                if (!inst) inst = Resources.Load<Catalog>("Commander/Catalog");
                if (!inst)
                {
                    Debug.LogError("Нет Resources/Commander/Catalog — выполните меню Star Wars → Командир клонов → Собрать игру");
                    inst = CreateInstance<Catalog>();
                }
                return inst;
            }
        }

        public UnitType Get(int index) => index >= 0 && index < Units.Count ? Units[index] : null;
        public int IndexOf(string id) => Units.FindIndex(u => u.Id == id);
        public UnitType Find(string id) => Units.Find(u => u.Id == id);

        public IEnumerable<UnitType> Recruitable(Team team)
        {
            foreach (var u in Units) if (u.Team == team && u.Recruitable && u.Prefab) yield return u;
        }

        /// <summary>Тип командира стороны (для аватара игрока).</summary>
        public int CommanderIndex(Team team)
        {
            for (int i = 0; i < Units.Count; i++) if (Units[i].Team == team && Units[i].Role == Role.Commander) return i;
            for (int i = 0; i < Units.Count; i++) if (Units[i].Team == team) return i;
            return 0;
        }

        /// <summary>Базовый стрелок стороны.</summary>
        public int RiflemanIndex(Team team)
        {
            for (int i = 0; i < Units.Count; i++) if (Units[i].Team == team && Units[i].Role == Role.Rifleman) return i;
            return CommanderIndex(team);
        }

        public AudioClip Sound(string name)
        {
            foreach (var s in Sounds) if (s && s.name == name) return s;
            return null;
        }
    }
}
