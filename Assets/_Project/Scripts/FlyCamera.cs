using UnityEngine;

namespace SW
{
    /// <summary>Свободная камера: ПКМ — обзор, WASD — движение, Q/E — вниз/вверх, Shift — быстрее, колесо — скорость.</summary>
    public class FlyCamera : MonoBehaviour
    {
        public float Speed = 6f;
        public float LookSpeed = 2.5f;

        float yaw, pitch;
        Vector3? focusPos;
        Quaternion focusRot;

        void Start()
        {
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180 ? e.x - 360 : e.x;
        }

        /// <summary>Плавно подлететь и посмотреть на точку.</summary>
        public void Focus(Vector3 target, float distance = 4.5f, float height = 1.4f)
        {
            // персонажи смотрят в +Z — камера встаёт спереди и чуть сбоку
            Vector3 pos = target + new Vector3(-1.6f, height, distance);
            focusPos = pos;
            focusRot = Quaternion.LookRotation(target + Vector3.up * 1.0f - pos);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (InputCompat.RightMouse)
            {
                Vector2 d = InputCompat.MouseDelta * LookSpeed;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                focusPos = null;
            }
            Speed = Mathf.Clamp(Speed * (1f + InputCompat.Scroll * 0.15f), 1f, 60f);
            Vector3 move = Vector3.zero;
            if (InputCompat.Held(KeyCode.W)) move += Vector3.forward;
            if (InputCompat.Held(KeyCode.S)) move += Vector3.back;
            if (InputCompat.Held(KeyCode.A)) move += Vector3.left;
            if (InputCompat.Held(KeyCode.D)) move += Vector3.right;
            if (InputCompat.Held(KeyCode.E)) move += Vector3.up;
            if (InputCompat.Held(KeyCode.Q)) move += Vector3.down;
            if (move != Vector3.zero)
            {
                focusPos = null;
                float sp = Speed * (InputCompat.Held(KeyCode.LeftShift) ? 3f : 1f);
                transform.position += transform.TransformDirection(move.normalized) * sp * dt;
            }
            if (focusPos.HasValue)
            {
                float k = 1f - Mathf.Exp(-6f * dt);
                transform.position = Vector3.Lerp(transform.position, focusPos.Value, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, focusRot, k);
                var e = transform.eulerAngles;
                yaw = e.y;
                pitch = e.x > 180 ? e.x - 360 : e.x;
                if ((transform.position - focusPos.Value).sqrMagnitude < 0.0004f) focusPos = null;
            }
        }
    }
}
