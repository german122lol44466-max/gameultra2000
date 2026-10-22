using System;
using System.Collections.Generic;
using SW.Battle;
using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Разрушаемая постройка: генератор щита, командный центр, турель, бункер. Здоровье — на сервере,
    /// клиентам уходит в StructureState. Бластеры почти не берут укрепления (5–15%), взрывы — полностью.
    /// </summary>
    public class Destructible : MonoBehaviour
    {
        public static readonly List<Destructible> All = new List<Destructible>();
        public static event Action<Destructible, Unit> Destroyed;

        public byte Id;
        public string Title = "Постройка";
        public Team Team = Team.Empire;
        public float MaxHealth = 1000f;
        public float Health = 1000f;
        public float BlasterFactor = 0.1f;
        public bool Objective;                 // цель миссии (показывается на HUD)
        public bool Invulnerable;              // командный центр под щитом
        public float ExplosionRadius = 8f;
        public GameObject IntactVisual, RuinVisual;
        public bool Dead { get; private set; }
        public float LastHit { get; private set; } = -99f;
        Bounds bounds;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            RecalcBounds();
        }

        void OnDisable() => All.Remove(this);

        public void RecalcBounds()
        {
            bounds = new Bounds(transform.position, Vector3.one);
            foreach (var r in GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        }

        public Vector3 Center => bounds.center;
        public float DistanceTo(Vector3 p) => Mathf.Sqrt(bounds.SqrDistance(p));

        public void Damage(float amount, DamageKind kind, Vector3 point, Unit by)
        {
            if (Dead || Invulnerable) return;
            if (kind == DamageKind.Blaster) amount *= BlasterFactor;
            Health -= amount;
            LastHit = Time.time;
            if (Health <= 0f)
            {
                Health = 0f;
                Kill(by);
            }
        }

        public void Kill(Unit by)
        {
            if (Dead) return;
            Dead = true;
            ShowDestroyed(true);
            Destroyed?.Invoke(this, by);
        }

        /// <summary>Визуал разрушения (у всех: сервер — при уничтожении, клиенты — по StructureState).</summary>
        public void ShowDestroyed(bool withFx)
        {
            Dead = true;
            Health = 0f;
            if (withFx)
            {
                Fx.Explosion(Center, ExplosionRadius);
                Fx.Explosion(Center + UnityEngine.Random.insideUnitSphere * 3f, ExplosionRadius * 0.6f);
            }
            if (IntactVisual) IntactVisual.SetActive(false);
            if (RuinVisual) RuinVisual.SetActive(true);
            Fx.Smoke(transform, Mathf.Clamp(ExplosionRadius / 4f, 1f, 4f));
            foreach (var t in GetComponentsInChildren<Turret>()) t.enabled = false;
        }

        public void SetHealth(float h)
        {
            Health = h;
            if (h <= 0f && !Dead) ShowDestroyed(true);
        }
    }
}
