using UnityEngine;

namespace SW.Commander
{
    /// <summary>
    /// Камера: от третьего лица из-за плеча (как в Battlefront), при прицеливании — ближе и уже FOV;
    /// после смерти — облёт тела; тактический режим — вид сверху (управляет TacticalView).
    /// </summary>
    public class CommanderCamera : MonoBehaviour
    {
        public enum Mode { Follow, Dead, Tactical, Overview }

        public static CommanderCamera I { get; private set; }
        public Camera Cam { get; private set; }
        public Mode Current = Mode.Overview;
        public float Yaw, Pitch = 8f;
        public float BaseFov = 62f;
        public static bool ShoulderLeft;

        CommanderController target;
        float fov, kick, shake;
        Vector3 smoothPivot;
        // тактический вид
        public Vector3 TacticalFocus;
        public float TacticalHeight = 90f, TacticalYaw;

        public static CommanderCamera Ensure()
        {
            if (I) return I;
            var main = Camera.main;
            GameObject go;
            if (main) go = main.gameObject;
            else
            {
                go = new GameObject("Main Camera") { tag = "MainCamera" };
                go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            I = go.GetComponent<CommanderCamera>();
            if (!I) I = go.AddComponent<CommanderCamera>();
            return I;
        }

        void Awake()
        {
            I = this;
            Cam = GetComponent<Camera>();
            Cam.nearClipPlane = 0.08f;
            Cam.farClipPlane = 2500f;
            fov = BaseFov;
        }

        public void Follow(CommanderController c)
        {
            target = c;
            Current = Mode.Follow;
            Yaw = c.transform.eulerAngles.y;
            Pitch = 8f;
            smoothPivot = c.transform.position + Vector3.up * 1.6f;
        }

        public void Kick(float amount)
        {
            kick += amount;
            shake = Mathf.Max(shake, amount * 0.05f);
        }

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            kick = Mathf.MoveTowards(kick, 0f, dt * 12f);
            shake = Mathf.MoveTowards(shake, 0f, dt * 0.8f);
            if (target && target.Self && target.Self.Dead && Current == Mode.Follow) Current = Mode.Dead;
            switch (Current)
            {
                case Mode.Follow: FollowCam(dt); break;
                case Mode.Dead: DeadCam(dt); break;
                case Mode.Tactical: TacticalCam(dt); break;
                case Mode.Overview: OverviewCam(dt); break;
            }
            if (shake > 0.001f) transform.position += Random.insideUnitSphere * shake * 0.3f;
        }

        void FollowCam(float dt)
        {
            if (!target) return;
            if (!CommanderController.Blocked)
            {
                var look = Keys.Look;
                float sens = target.Aiming ? 0.55f : 1f;
                Yaw += look.x * sens;
                Pitch = Mathf.Clamp(Pitch - look.y * sens, -70f, 75f);
            }
            if (Keys.Down(KeyCode.V) && Keys.Ctrl) ShoulderLeft = !ShoulderLeft;
            float height = target.Prone ? 0.55f : target.Crouched ? 1.15f : 1.6f;
            Vector3 pivotWant = target.transform.position + Vector3.up * height;
            smoothPivot = Vector3.Lerp(smoothPivot, pivotWant, 1f - Mathf.Exp(-dt * 18f));
            var rot = Quaternion.Euler(Pitch - kick, Yaw, 0);
            float side = ShoulderLeft ? -0.55f : 0.55f;
            float dist = target.Aiming ? 1.5f : target.Sprinting ? 3.4f : 2.9f;
            Vector3 offset = rot * new Vector3(side * (target.Aiming ? 0.8f : 1f), 0.15f, -dist);
            Vector3 wantPos = smoothPivot + offset;
            // столкновение со стенами
            Vector3 from = smoothPivot + rot * new Vector3(side * 0.4f, 0, 0);
            Vector3 d = wantPos - from;
            if (Physics.SphereCast(from, 0.2f, d.normalized, out var h, d.magnitude, ~0, QueryTriggerInteraction.Ignore) && !h.collider.transform.IsChildOf(target.transform))
                wantPos = from + d.normalized * Mathf.Max(0.3f, h.distance);
            transform.position = wantPos;
            transform.rotation = rot;
            float wantFov = target.Aiming ? BaseFov * 0.72f : target.Sprinting ? BaseFov * 1.08f : BaseFov;
            fov = Mathf.Lerp(fov, wantFov, 1f - Mathf.Exp(-dt * 10f));
            Cam.fieldOfView = fov;
        }

        void DeadCam(float dt)
        {
            if (!target) { Current = Mode.Overview; return; }
            Yaw += dt * 12f;
            var body = target.Self && target.Self.Body && target.Self.Body.Pelvis ? target.Self.Body.Pelvis.position : target.transform.position;
            var rot = Quaternion.Euler(30f, Yaw, 0);
            Vector3 want = body + rot * new Vector3(0, 0, -6f);
            transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-dt * 3f));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(body - transform.position), 1f - Mathf.Exp(-dt * 4f));
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, BaseFov, dt * 3f);
        }

        void TacticalCam(float dt)
        {
            float ground = MapInfo.GroundHeight(TacticalFocus);
            TacticalFocus.y = ground;
            var rot = Quaternion.Euler(58f, TacticalYaw, 0);
            Vector3 want = TacticalFocus - rot * Vector3.forward * TacticalHeight;
            transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-dt * 8f));
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-dt * 8f));
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, 50f, dt * 6f);
        }

        void OverviewCam(float dt)
        {
            // до появления командира: медленный облёт поля боя
            Yaw += dt * 3f;
            var map = MapInfo.I;
            Vector3 c = map && map.RepublicBase ? map.RepublicBase.transform.position : Vector3.zero;
            var rot = Quaternion.Euler(25f, Yaw, 0);
            transform.position = c + Vector3.up * MapInfo.GroundHeight(c) * 0 + rot * new Vector3(0, 0, -60f) + Vector3.up * 30f;
            transform.rotation = Quaternion.LookRotation(c - transform.position);
        }
    }
}
