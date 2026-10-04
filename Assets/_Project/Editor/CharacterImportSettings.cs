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
        public const string CharactersV2Dir = "Assets/_Project/Art/CharactersV2/";
        public const string VehiclesV2Dir = "Assets/_Project/Art/VehiclesV2/";
        static readonly HashSet<string> Looping = new HashSet<string>
        {
            "Idle", "Walk", "Run", "Aim", "WalkAim", "WalkBack", "StrafeL", "StrafeR", "Crouch", "CrouchAim", "FireAuto",
            "Ride", "Drive", "Move"
        };

        static bool IsAnimated(string p) => p.StartsWith(CharactersDir) || p.StartsWith(CharactersV2Dir) || p.StartsWith(VehiclesV2Dir);

        void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            if (IsAnimated(assetPath))
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
            if (!IsAnimated(assetPath)) return;
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
