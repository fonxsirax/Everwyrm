using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Setup automático do projeto — roda após cada recompilação, sem menus.
/// As checagens são baratas; o trabalho pesado só executa quando algo está
/// faltando/desatualizado:
///  1. HDRP Water habilitado (lagos do InfiniteTerrain);
///  2. Prefabs do RockyDesert e do Winter Environment convertidos, materiais
///     de FX do inverno em HDRP e "AG Global Settings" na cena;
///  3. Animator do dragão regenerado quando o setup ganha estados novos;
///  4. Ciclo dia/noite (DayNightCycle) presente na cena;
///  5. Fauna (controllers + espécies) configurada.
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
            //     sem vento/neve/tint (globais não vinculados).
            WinterSetup.EnsureGlobalSettings();
        }

        // limpeza one-shot: a galeria de diagnóstico (ferramenta já removida)
        // ficou salva na cena Main — remover se ainda existir.
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name.StartsWith("Prefab Gallery"))
            {
                Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Debug.Log("[EverwyrmAutoSetup] Galeria de diagnóstico removida da cena — salve a cena.");
                break;
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

        // 2e) billboards das árvores da floresta (LOD distante) vinham com
        //     HDRP/Unlit — dia assado na textura, imunes a QUALQUER luz: de
        //     noite brilhavam como se fosse dia. O ALP traz o shader lit
        //     próprio p/ eles; converte uma vez.
        FixForestBillboards();

        // 2f) varredura geral dos materiais de vegetação dos 3 biomas: qualquer
        //     um preso na fila TRANSPARENT (não recebe sombra no HDRP → claro
        //     à noite, ex.: arbustos DryLeaves01) volta p/ opaco alpha-test.
        FixWorldVegetationMaterials();

        // 4) Ciclo dia/noite + céu noturno na cena? (componentes auto-constroem
        //    sol/lua/volume em runtime; os objetos ficam na cena p/ os
        //    designers ajustarem curvas)
        var cycle = Object.FindFirstObjectByType<DayNightCycle>();
        if (cycle == null)
        {
            cycle = new GameObject("Day-Night Cycle").AddComponent<DayNightCycle>();
            cycle.tuningVersion = DayNightCycle.CurrentTuningVersion;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[EverwyrmAutoSetup] Day-Night Cycle criado na cena — salve a cena.");
        }
        if (cycle.GetComponent<NightSky>() == null)
        {
            cycle.gameObject.AddComponent<NightSky>();
            EditorUtility.SetDirty(cycle.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[EverwyrmAutoSetup] NightSky criado (estrelas seeded + lua + estrelas cadentes) — salve a cena.");
        }
        MigrateCycleTuning(cycle, cycle.GetComponent<NightSky>());
        EnsureMoonTexture(cycle.GetComponent<NightSky>());

        // 5) Fauna configurada? (controllers + espécies do Forest Animals 2.0)
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

    // --------------------------------------------- MIGRAÇÕES DE CALIBRAÇÃO
    /// <summary>Migrações de calibração do ciclo dia/noite, consolidadas e
    /// carimbadas por versão (roda 1x por cena, não a cada recompilação).
    /// Cada passo só sobrescreve valores que ainda são o DEFAULT antigo —
    /// calibração manual do designer nunca é tocada.</summary>
    static void MigrateCycleTuning(DayNightCycle cycle, NightSky nightSky)
    {
        if (cycle.tuningVersion >= DayNightCycle.CurrentTuningVersion) return;
        int applied = 0;

        // v1→v3: noite breu (lua fraca/exposição baixa) e névoa noturna rala
        if (cycle.moonMaxLux < 100f || cycle.fogDistanceByHour.Evaluate(0f) > 500f)
        {
            cycle.moonMaxLux = 100f;
            cycle.exposureByHour = DayNightCycle.DefaultExposureByHour();
            cycle.fogDistanceByHour = DayNightCycle.DefaultFogDistanceByHour();
            applied++;
        }
        // v2→v4: indireta noturna 0.02 deixava sombras pretas (contraste)
        if (cycle.indirectByHour.Evaluate(0f) < 0.05f)
        {
            cycle.indirectByHour = DayNightCycle.DefaultIndirectByHour();
            applied++;
        }
        // dia "temperado": meio-dia 6500K neutro → 5900K dourado
        if (Mathf.Abs(cycle.sunTemperature.Evaluate(0.5f) - 6500f) < 1f)
        {
            cycle.sunTemperature = DayNightCycle.DefaultSunTemperature();
            applied++;
        }

        // v4→v5: calibração fotométrica final — noite azul-profunda cinematográfica.
        // Céu ao luar 250 lux clareava como dia nublado (céu ~11 cd/m² > estrelas)
        // e o disco da lua (36 cd/m²) perdia p/ o próprio halo (195) = "eclipse".
        // Alvos: céu ~4.6 · nuvens ~22 · estrelas ~9 · halo ~104 · disco ~250.
        if (Mathf.Abs(cycle.moonMaxLux - 250f) < 0.5f)
        {
            cycle.moonMaxLux = 100f;
            applied++;
        }
        if (Mathf.Abs(cycle.exposureByHour.Evaluate(0f) - 7.5f) < 0.1f)
        {
            cycle.exposureByHour = DayNightCycle.DefaultExposureByHour();
            applied++;
        }
        if (Mathf.Abs(cycle.cloudOpacity - 0.6f) < 0.01f)
        {
            cycle.cloudOpacity = 0.5f;
            applied++;
        }
        if (nightSky != null)
        {
            if (Mathf.Abs(nightSky.moonDiscBrightness - 0.003f) < 0.0001f)
            {
                nightSky.moonDiscBrightness = 0.012f;
                applied++;
            }
            if (Mathf.Abs(nightSky.moonFlareIntensity - 0.0015f) < 0.0001f)
            {
                nightSky.moonFlareIntensity = 0.0008f;
                applied++;
            }
            if (Mathf.Abs(nightSky.spaceMultiplierByHour.Evaluate(0f) - 1f) < 0.05f)
            {
                nightSky.spaceMultiplierByHour = NightSky.DefaultSpaceMultiplier();
                applied++;
            }
            EditorUtility.SetDirty(nightSky);
        }

        cycle.tuningVersion = DayNightCycle.CurrentTuningVersion;
        EditorUtility.SetDirty(cycle);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cycle.gameObject.scene);
        if (applied > 0)
            Debug.Log($"[EverwyrmAutoSetup] Calibração do ciclo migrada p/ v{DayNightCycle.CurrentTuningVersion} ({applied} ajuste(s)) — salve a cena.");
    }

    // ------------------------------------------------------ TEXTURA DA LUA
    const string MoonTif = "Assets/Everwyrm/Textures/lroc_color_poles_2k.tif";
    const string MoonDisc = "Assets/Everwyrm/Textures/MoonDisc.png";

    /// <summary>O mapa da NASA (CGI Moon Kit) é EQUIRETANGULAR, mas o
    /// surfaceTexture do corpo celeste do HDRP é amostrado como projeção de
    /// DISCO ("foto" da face). Assa uma vez a projeção ortográfica do lado
    /// visível (mares + crateras reais) e liga no slot do NightSky.</summary>
    static void EnsureMoonTexture(NightSky nightSky)
    {
        if (nightSky == null) return;

        var disc = AssetDatabase.LoadAssetAtPath<Texture2D>(MoonDisc);
        if (disc == null)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(MoonTif) == null)
                return;                                   // NASA ainda não baixada
            var imp = (TextureImporter)AssetImporter.GetAtPath(MoonTif);
            if (imp != null && !imp.isReadable)
            {
                imp.isReadable = true;                    // fonte só de editor
                imp.SaveAndReimport();
            }
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(MoonTif);
            if (src == null) return;

            var baked = BakeMoonDisc(src, 1024);
            System.IO.File.WriteAllBytes(MoonDisc, baked.EncodeToPNG());
            Object.DestroyImmediate(baked);
            AssetDatabase.ImportAsset(MoonDisc);
            disc = AssetDatabase.LoadAssetAtPath<Texture2D>(MoonDisc);
            Debug.Log("[EverwyrmAutoSetup] Disco lunar assado da textura NASA (lado visível, ortográfico) → MoonDisc.png.");
        }

        if (disc != null && nightSky.moonSurfaceTexture == null)
        {
            nightSky.moonSurfaceTexture = disc;
            EditorUtility.SetDirty(nightSky);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(nightSky.gameObject.scene);
            Debug.Log("[EverwyrmAutoSetup] Textura NASA ligada na lua (NightSky.moonSurfaceTexture) — salve a cena.");
        }
    }

    /// <summary>Projeção ortográfica do lado visível: p/ cada pixel do disco,
    /// ponto na esfera unitária → lat/long → sample bilinear no equiretangular.
    /// NORMALIZA o brilho: o albedo real da Lua é rocha escura (~12%) — sem o
    /// ganho, o disco fica mais escuro que o céu ao redor e vira "eclipse".</summary>
    static Texture2D BakeMoonDisc(Texture2D equirect, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
        var px = new Color[size * size];
        double sum = 0; int count = 0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r2 = dx * dx + dy * dy;
                if (r2 > 1f) { px[y * size + x] = Color.black; continue; }
                float z = Mathf.Sqrt(1f - r2);
                float lat = Mathf.Asin(dy);
                float lon = Mathf.Atan2(dx, z);
                float u = 0.5f + lon / (2f * Mathf.PI);
                float v = 0.5f + lat / Mathf.PI;
                var c = equirect.GetPixelBilinear(u, v);
                px[y * size + x] = c;
                sum += c.grayscale; count++;
            }
        // ganho p/ média do disco ≈ 0.55 (contraste dos mares preservado)
        float gain = Mathf.Clamp(0.55f / Mathf.Max(0.01f, (float)(sum / count)), 1f, 8f);
        if (gain > 1.01f)
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(Mathf.Clamp01(px[i].r * gain),
                                  Mathf.Clamp01(px[i].g * gain),
                                  Mathf.Clamp01(px[i].b * gain));
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    // ------------------------------------------------- BILLBOARDS DA FLORESTA
    const string BillboardMat =
        "Assets/ALP_Assets/Nature Package - Forest Environment_/_Vegetation/HDRP/HDRP_Prefabs/Materials/billboards_.mat";
    const string BillboardShader =
        "Assets/ALP_Assets/Core/Shaders/Shaders HDRP/ALP Billboard Cross Wind.shader";

    /// <summary>O LOD distante das árvores ALP usa billboards_.mat com
    /// HDRP/Unlit — a textura tem o DIA assado e ignora toda a iluminação, o
    /// que quebrava o ciclo dia/noite (árvores "de dia" à meia-noite). Troca
    /// uma vez para o shader lit de billboard que o próprio ALP fornece.</summary>
    static void FixForestBillboards()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BillboardMat);
        if (mat == null || mat.shader == null) return;

        if (mat.shader.name.Contains("Unlit"))
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(BillboardShader);
            if (shader == null)
            {
                Debug.LogWarning("[EverwyrmAutoSetup] Shader 'ALP Billboard Cross Wind' não encontrado — billboards seguem unlit.");
                return;
            }

            // pega a textura ANTES da troca (o lit lê _MainTex, não _UnlitColorMap)
            Texture tex = mat.HasProperty("_UnlitColorMap") ? mat.GetTexture("_UnlitColorMap") : null;

            mat.shader = shader;
            if (tex != null) mat.SetTexture("_MainTex", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Brightness", 1f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[EverwyrmAutoSetup] billboards_ das árvores convertido de Unlit para 'ALP/Billboard Cross Wind' — LOD distante agora reage ao ciclo dia/noite.");
        }

        // calibração pós-conversão: billboard não tem auto-sombreamento de
        // dossel (recebe a lua "limpa") e a textura assada já é clara — escurece
        // o conjunto e zera o specular. Idempotente: só mexe se _Brightness
        // ainda for o 1.0 da conversão (ajuste manual do designer é respeitado).
        if (mat.HasProperty("_Brightness") && Mathf.Abs(mat.GetFloat("_Brightness") - 1f) < 0.001f)
        {
            mat.SetFloat("_Brightness", 0.65f);
            if (mat.HasProperty("_SmoothnessStrength")) mat.SetFloat("_SmoothnessStrength", 0f);
            if (mat.HasProperty("_MetallicStrength")) mat.SetFloat("_MetallicStrength", 0f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[EverwyrmAutoSetup] billboards_ calibrado (_Brightness 0.65, sem specular) — LOD distante mais próximo do tom das árvores 3D.");
        }

        // a fila TRANSPARENT (3000) forçada pelo material Unlit antigo sobrevive
        // à troca de shader — e transparente no HDRP NÃO recebe sombra, então o
        // billboard seguia claro à noite. Restaura a fila do shader (alpha-test),
        // remove keywords órfãos e recalibra o brilho (a textura assada tem a
        // luz do dia embutida ≈ albedo x sol; 0.35 recupera o "albedo" real).
        if (mat.renderQueue == 3000)
        {
            mat.renderQueue = -1;                       // usa a fila do shader
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
            mat.DisableKeyword("_ADD_PRECOMPUTED_VELOCITY");
            if (mat.HasProperty("_Brightness") &&
                Mathf.Abs(mat.GetFloat("_Brightness") - 0.65f) < 0.001f)
                mat.SetFloat("_Brightness", 0.35f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[EverwyrmAutoSetup] billboards_ tirado da fila Transparent (não recebia sombra!) — agora leva sombra e escurece à noite como as árvores 3D.");
        }
    }

    // --------------------------------- VEGETAÇÃO DO MUNDO (todos os biomas)
    static readonly string[] VegetationMaterialDirs =
    {
        "Assets/ALP_Assets/Nature Package - Forest Environment_/_Vegetation/HDRP/HDRP_Prefabs/Materials",
        "Assets/Everwyrm/Desert/Materials",
        "Assets/ANGRY MESH/Nature Pack - Winter Environment/Sources/Materials",
    };

    /// <summary>Vegetação presa na fila TRANSPARENT não recebe sombra no HDRP
    /// e fica clara à noite (mesmo bug do billboards_ — ex.: DryLeaves01 dos
    /// arbustos LeafDry01). Converte para opaco alpha-test. Pula FX_* (neve/
    /// nuvem são partículas, transparentes de propósito) e shaders Unlit
    /// (tratados à parte). Idempotente: depois do fix nada mais casa a regra.</summary>
    static void FixWorldVegetationMaterials()
    {
        var dirs = new List<string>();
        foreach (var d in VegetationMaterialDirs)
            if (AssetDatabase.IsValidFolder(d)) dirs.Add(d);
        if (dirs.Count == 0) return;

        int fixedCount = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", dirs.ToArray()))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (mat.name.StartsWith("FX_")) continue;
            if (mat.shader.name.Contains("Unlit")) continue;

            bool hdrpLitTransparent = mat.shader.name.StartsWith("HDRP/") &&
                mat.HasProperty("_SurfaceType") && mat.GetFloat("_SurfaceType") > 0.5f;
            bool transparentQueue = mat.renderQueue >= 2981;
            if (!hdrpLitTransparent && !transparentQueue) continue;

            if (hdrpLitTransparent)
            {
                mat.SetFloat("_SurfaceType", 0f);
                mat.SetFloat("_AlphaCutoffEnable", 1f);
                if (mat.HasProperty("_AlphaCutoff") && mat.GetFloat("_AlphaCutoff") <= 0.01f)
                    mat.SetFloat("_AlphaCutoff", 0.4f);
                mat.EnableKeyword("_ALPHATEST_ON");
            }
            mat.renderQueue = -1;                       // fila do shader
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
            mat.DisableKeyword("_BLENDMODE_ALPHA");
            if (mat.shader.name.StartsWith("HDRP/")) ValidateHdrp(mat);
            EditorUtility.SetDirty(mat);
            fixedCount++;
            Debug.Log($"[EverwyrmAutoSetup] '{mat.name}' tirado da fila Transparent (não recebia sombra) — agora escurece à noite.");
        }
        if (fixedCount > 0) AssetDatabase.SaveAssets();
    }

    /// <summary>HDMaterial.ValidateMaterial via reflexão (recalcula keywords e
    /// passes do HDRP após mudar surface type) — mesmo helper do DesertSetup.</summary>
    static void ValidateHdrp(Material m)
    {
        var t = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime")
             ?? System.Type.GetType("UnityEditor.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Editor");
        t?.GetMethod("ValidateMaterial", new[] { typeof(Material) })?.Invoke(null, new object[] { m });
    }
}
