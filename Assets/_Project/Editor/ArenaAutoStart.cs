using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace SW.EditorTools
{
    /// <summary>
    /// «Сразу в бой»: Play всегда запускает полигон BattleArena (какая бы сцена ни была открыта),
    /// а если полигон ещё не собран — он собирается автоматически при открытии проекта.
    /// Отключить: меню Star Wars → Play всегда с арены.
    /// </summary>
    [InitializeOnLoad]
    public static class ArenaAutoStart
    {
        const string Scene = "Assets/_Project/Scenes/BattleArena.unity";
        const string Pref = "SW_PlayFromArena";
        const string MenuPath = "Star Wars/Play всегда с арены";

        static ArenaAutoStart()
        {
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool on = EditorPrefs.GetBool(Pref, true);
            Menu_Checked(on);
            if (!File.Exists(Scene))
            {
                if (!SessionState.GetBool("SW_AutoBuilt", false) &&
                    File.Exists("Assets/_Project/Art/CharactersV2/manifest_v2.json"))
                {
                    SessionState.SetBool("SW_AutoBuilt", true);
                    BattleArenaBuilder.BuildAll();
                }
            }
            else if (EditorSceneManager.GetActiveScene().path != Scene && string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path))
            {
                EditorSceneManager.OpenScene(Scene);          // пустой проект — сразу показываем полигон
            }
            EditorSceneManager.playModeStartScene = on ? AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene) : null;
        }

        static void Menu_Checked(bool on) => UnityEditor.Menu.SetChecked(MenuPath, on);

        [MenuItem(MenuPath, priority = 30)]
        static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(Pref, true);
            EditorPrefs.SetBool(Pref, on);
            Apply();
        }
    }
}
