using SW.Battle;
using UnityEngine;

namespace SW.Commander.UI
{
    /// <summary>Настройки игрока (PlayerPrefs): графика, управление, звук.</summary>
    public static class Prefs
    {
        public static int Quality { get => PlayerPrefs.GetInt("q", Mathf.Max(0, QualitySettings.names.Length - 2)); set => PlayerPrefs.SetInt("q", value); }
        public static bool Fullscreen { get => PlayerPrefs.GetInt("fs", 1) == 1; set => PlayerPrefs.SetInt("fs", value ? 1 : 0); }
        public static bool VSync { get => PlayerPrefs.GetInt("vs", 1) == 1; set => PlayerPrefs.SetInt("vs", value ? 1 : 0); }
        public static int FpsLimit { get => PlayerPrefs.GetInt("fps", 0); set => PlayerPrefs.SetInt("fps", value); }
        public static int ResolutionIndex { get => PlayerPrefs.GetInt("res", -1); set => PlayerPrefs.SetInt("res", value); }
        public static float Sensitivity { get => PlayerPrefs.GetFloat("sens", 1f); set => PlayerPrefs.SetFloat("sens", value); }
        public static bool InvertY { get => PlayerPrefs.GetInt("inv", 0) == 1; set => PlayerPrefs.SetInt("inv", value ? 1 : 0); }
        public static float Fov { get => PlayerPrefs.GetFloat("fov", 62f); set => PlayerPrefs.SetFloat("fov", value); }
        public static float Master { get => PlayerPrefs.GetFloat("vol", 0.9f); set => PlayerPrefs.SetFloat("vol", value); }
        public static float Effects { get => PlayerPrefs.GetFloat("sfx", 1f); set => PlayerPrefs.SetFloat("sfx", value); }
        public static float Voices { get => PlayerPrefs.GetFloat("voice", 1f); set => PlayerPrefs.SetFloat("voice", value); }
        public static bool Bloom { get => PlayerPrefs.GetInt("bloom", 1) == 1; set => PlayerPrefs.SetInt("bloom", value ? 1 : 0); }
        public static bool Hints { get => PlayerPrefs.GetInt("hints", 1) == 1; set => PlayerPrefs.SetInt("hints", value ? 1 : 0); }
        public static string PlayerName { get => PlayerPrefs.GetString("name", ""); set => PlayerPrefs.SetString("name", value); }
        public static string LastIp { get => PlayerPrefs.GetString("ip", "127.0.0.1"); set => PlayerPrefs.SetString("ip", value); }

        static bool applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (applied) return;
            applied = true;
            Apply(true);
        }

        public static void Apply(bool display)
        {
            QualitySettings.SetQualityLevel(Mathf.Clamp(Quality, 0, QualitySettings.names.Length - 1), true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FpsLimit > 0 ? FpsLimit : -1;
            QualitySettings.shadowDistance = 160f;
            QualitySettings.lodBias = 1.5f;
            if (display)
            {
                var list = Screen.resolutions;
                int ri = ResolutionIndex;
                if (ri >= 0 && ri < list.Length)
                    Screen.SetResolution(list[ri].width, list[ri].height, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
                else Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            }
            Keys.Sensitivity = Sensitivity;
            Keys.InvertY = InvertY;
            AudioListener.volume = Master;
            Sfx.Volume = Effects;
            Audio.VoiceVolume = Voices;
            if (CommanderCamera.I) CommanderCamera.I.BaseFov = Fov;
            PlayerPrefs.Save();
        }
    }
}
