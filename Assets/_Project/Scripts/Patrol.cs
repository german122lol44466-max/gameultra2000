using UnityEngine;

namespace SW
{
    /// <summary>Патруль по точкам: персонаж идёт (клип Walk) или бежит (Run) по замкнутому маршруту.</summary>
    public class Patrol : MonoBehaviour
    {
        public Vector3[] Points = new Vector3[0];
        public float Speed = 1.6f;
        public string Clip = "Walk";
        public float TurnSpeed = 240f;

        int target;
        Animator anim;

        void Start()
        {
            anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;
                anim.Play(Clip, 0, Random.value);
            }
        }

        void Update()
        {
            if (Points.Length < 2) return;
            Vector3 to = Points[target] - transform.position;
            to.y = 0;
            if (to.magnitude < 0.25f)
            {
                target = (target + 1) % Points.Length;
                return;
            }
            Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * Time.deltaTime);
            transform.position += transform.forward * Speed * Time.deltaTime;
        }
    }
}
