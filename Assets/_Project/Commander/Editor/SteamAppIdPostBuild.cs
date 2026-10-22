using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace SW.Commander.EditorTools
{
    /// <summary>
    /// Кладёт steam_appid.txt рядом с собранным .exe — без него Steam API не инициализируется,
    /// если игра запущена не из библиотеки Steam как «своя» игра с App ID.
    /// </summary>
    public class SteamAppIdPostBuild : IPostprocessBuildWithReport
    {
        const string HowToPlay = "docs/HOW_TO_PLAY.txt";

        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows &&
                target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneOSX)
                return;

            string dir = Path.GetDirectoryName(report.summary.outputPath);
            string source = "steam_appid.txt";
            if (File.Exists(source)) File.Copy(source, Path.Combine(dir, "steam_appid.txt"), true);
            if (File.Exists(HowToPlay)) File.Copy(HowToPlay, Path.Combine(dir, "КАК ИГРАТЬ.txt"), true);
        }
    }
}
