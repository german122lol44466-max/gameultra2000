using System.Collections.Generic;
using UnityEditor;

namespace SW.EditorTools
{
    /// <summary>
    /// Настройки импорта FBX персонажей: Generic-риг, клипы с короткими именами (Idle, Walk, ...),
    /// зацикливание для Idle/Walk/Run/Aim, без сжатия ключей (анимации уже запечены по кадрам).
    /// </summary>
    public class CharacterImportSettings : AssetPostprocessor
    {
        public const string CharactersDir = "Assets/_Project/Art/Characters/";
        public const string WeaponsDir = "Assets/_Project/Art/Weapons/";
        static readonly HashSet<string> Looping = new HashSet<string> { "Idle", "Walk", "Run", "Aim" };

        void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            if (assetPath.StartsWith(CharactersDir))
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                mi.importBlendShapes = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            }
            else if (assetPath.StartsWith(WeaponsDir))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(CharactersDir)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                c.name = n;
                c.loopTime = Looping.Contains(n);
                c.loopPose = false;
            }
            mi.clipAnimations = clips;
        }
    }
}
