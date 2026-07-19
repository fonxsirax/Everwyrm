using UnityEditor;
using UnityEngine;

/// <summary>
/// Setup automático do projeto — roda após cada recompilação, sem menus.
/// As checagens são baratas; o trabalho pesado só executa quando algo está
/// faltando/desatualizado:
///  1. HDRP Water habilitado (lagos do InfiniteTerrain);
///  2. Prefabs do RockyDesert convertidos para HDRP;
///  3. Animator do dragão regenerado quando o setup ganha estados novos.
/// </summary>
[InitializeOnLoad]
static class EverwyrmAutoSetup
{
    // Parâmetro-sentinela do Animator: se faltar, o controller está desatualizado.
    // Mude para o nome do parâmetro mais novo sempre que o DragonSetup evoluir.
    const string AnimatorMarker = "Swimming";
    const string ControllerPath = "Assets/Dragao/Dragon Player.controller";

    static EverwyrmAutoSetup()
    {
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        // 1) HDRP Water (idempotente, silencioso quando já ativo)
        WaterSetup.Enable(quiet: true);

        // 2) RockyDesert convertido?
        if (AssetDatabase.IsValidFolder("Assets/RockyDesert") &&
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Everwyrm/Desert/SM_RockSide_A1.prefab") == null)
            DesertSetup.Convert();

        // 2b) Winter Environment (Tundra/Montanha) convertido?
        if (WinterSetup.IsInstalled && !WinterSetup.IsConverted)
            WinterSetup.Convert();

        // 2c) materiais de FX do Winter pack (neve/nuvem) ainda em shader builtin (rosa)?
        if (WinterSetup.IsInstalled)
        {
            WinterSetup.EnsureFxMaterials();
            // 2d) "AG Global Settings" na cena — sem ele os shaders do pack ficam
            //     com manchas magenta (globais de vento/neve/tint não vinculados).
            WinterSetup.EnsureGlobalSettings();
        }

        // 3) Animator do dragão em dia?
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
        bool outdated = controller == null;
        if (!outdated)
        {
            outdated = true;
            foreach (var p in controller.parameters)
                if (p.name == AnimatorMarker) { outdated = false; break; }
        }
        if (outdated) DragonSetup.RegenerateAnimatorAndPrefab();

        // 4) Fauna configurada? (controllers + espécies do Forest Animals 2.0)
        if (AssetDatabase.IsValidFolder("Assets/Red_Deer/Wild_Animals"))
        {
            if (AssetDatabase.LoadAssetAtPath<AnimalDefinition>(WildlifeSetup.MarkerAsset) == null)
                WildlifeSetup.SetupAll(quiet: false);
            else
            {
                // materiais do pacote ainda em Built-in (magenta)? converte.
                var probe = AssetDatabase.LoadAssetAtPath<Material>(WildlifeSetup.ProbeMaterial);
                if (probe != null && probe.shader != null && !probe.shader.name.StartsWith("HDRP/"))
                    WildlifeSetup.ConvertMaterials();
            }
        }
    }
}
