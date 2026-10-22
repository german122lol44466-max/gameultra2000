using UnityEngine;

namespace SW.Commander
{
    /// <summary>Описание карты боя (заполняет сборщик сцены): базы, пункты, цели, границы, тактическая карта.</summary>
    public class MapInfo : MonoBehaviour
    {
        public static MapInfo I { get; private set; }

        public string MapName = "Феллусия";
        public Vector2 Size = new Vector2(1500, 1500);      // размер игровой области (центр — в начале координат)
        public CommandPost RepublicBase, EmpireBase;
        public CommandPost[] Posts = new CommandPost[0];   // все пункты, по порядку от Республики к Империи
        public Destructible CommandCenter;                  // главная цель — командный центр Империи
        public Destructible[] ShieldGenerators = new Destructible[0];
        public Destructible RepublicHQ;
        public Turret[] Turrets = new Turret[0];
        public Texture2D MapImage;                           // вид сверху для тактической карты
        public Transform MenuCameraPath;

        void Awake()
        {
            I = this;
            // протяжённость фронта: лишние аванпосты выключаются (одинаково у всех — настройки общие)
            int size = Net.NetSession.Instance ? Net.NetSession.Instance.Settings.MapSize : 2;
            foreach (var cp in Posts)
            {
                if (!cp || cp.IsBase || cp.MinSize <= size) continue;
                cp.enabled = false;
                foreach (var r in cp.Lights) if (r) r.gameObject.SetActive(false);
                foreach (var t in Turrets)
                    if (t && (t.transform.position - cp.transform.position).sqrMagnitude < 60f * 60f) t.gameObject.SetActive(false);
            }
            Turrets = System.Array.FindAll(Turrets, t => t && t.gameObject.activeSelf);
        }

        public Rect Bounds => new Rect(-Size.x * 0.5f, -Size.y * 0.5f, Size.x, Size.y);

        /// <summary>Мировая точка -> 0..1 на карте.</summary>
        public Vector2 ToMap(Vector3 p) => new Vector2((p.x + Size.x * 0.5f) / Size.x, (p.z + Size.y * 0.5f) / Size.y);
        public Vector3 FromMap(Vector2 uv) => new Vector3(uv.x * Size.x - Size.x * 0.5f, 0, uv.y * Size.y - Size.y * 0.5f);

        public static float GroundHeight(Vector3 p)
        {
            if (Physics.Raycast(new Vector3(p.x, 600f, p.z), Vector3.down, out var h, 1200f, ~0, QueryTriggerInteraction.Ignore)) return h.point.y;
            var t = Terrain.activeTerrain;
            return t ? t.SampleHeight(p) + t.transform.position.y : 0f;
        }
    }
}
