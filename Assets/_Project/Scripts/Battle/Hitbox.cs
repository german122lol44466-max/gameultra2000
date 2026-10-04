using UnityEngine;

namespace SW.Battle
{
    /// <summary>Коллайдер части тела: передаёт урон юниту с множителем (голова — x2.5).</summary>
    public class Hitbox : MonoBehaviour
    {
        public Unit Owner;
        public float Multiplier = 1f;
        public bool IsHead;
    }
}
