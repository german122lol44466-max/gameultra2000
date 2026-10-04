using UnityEngine;

namespace SW
{
    /// <summary>Летящий болт бластера.</summary>
    public class Bolt : MonoBehaviour
    {
        public Vector3 Velocity;
        float life = 1.5f;

        void Update()
        {
            transform.position += Velocity * Time.deltaTime;
            life -= Time.deltaTime;
            if (life <= 0) Destroy(gameObject);
        }
    }
}
