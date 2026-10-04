using System;
using System.Collections.Generic;
using UnityEngine;

namespace SW.Battle
{
    public enum Team { Empire = 0, Republic = 1 }

    public enum DamageKind { Blaster, Saber, Explosion, Force, Fall }

    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;      // направление полёта урона (от атакующего к цели)
        public DamageKind Kind;
        public Unit Attacker;
        public bool Headshot;
        public float Impulse;          // толчок для рэгдолла
    }

    /// <summary>Всё, что воюет: персонажи и техника. Здоровье, команда, реестр живых юнитов.</summary>
    public class Unit : MonoBehaviour
    {
        public static readonly List<Unit> All = new List<Unit>();
        public static event Action<Unit, DamageInfo> OnDied;

        public Team Team;
        public string Title;
        public float MaxHealth = 100f;
        public float Health = 100f;
        public bool IsVehicle;
        public bool IsDuelist;          // джедай/ситх
        public float Threat = 1f;       // приоритет как цели
        public bool Dead { get; private set; }
        public float LastHitTime { get; private set; } = -99f;
        public Unit LastAttacker { get; private set; }
        public float SuppressedUntil;   // под огнём: болты пролетают рядом

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;

        /// <summary>Точка прицеливания (грудь).</summary>
        public Transform AimPoint;
        public Transform HeadPoint;

        protected virtual void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        protected virtual void OnDisable() { All.Remove(this); }

        public Vector3 Center => AimPoint ? AimPoint.position : transform.position + Vector3.up * 1.2f;
        public Vector3 Eye => HeadPoint ? HeadPoint.position : transform.position + Vector3.up * 1.65f;

        public bool IsEnemy(Unit other) => other != null && other.Team != Team && !other.Dead;

        public virtual void Damage(DamageInfo d)
        {
            if (Dead) return;
            Health -= d.Amount;
            LastHitTime = Time.time;
            if (d.Attacker) LastAttacker = d.Attacker;
            Damaged?.Invoke(d);
            if (Health <= 0f)
            {
                Health = 0f;
                Dead = true;
                All.Remove(this);
                Died?.Invoke(d);
                OnDied?.Invoke(this, d);
            }
        }

        public static IEnumerable<Unit> Enemies(Team of)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var u = All[i];
                if (u && !u.Dead && u.Team != of) yield return u;
            }
        }

        public static int CountAlive(Team t)
        {
            int n = 0;
            foreach (var u in All) if (u && !u.Dead && u.Team == t) n++;
            return n;
        }
    }
}
