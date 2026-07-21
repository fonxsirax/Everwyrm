using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Integra o pacote Winter Environment - Nature Pack (ANGRY MESH) aos biomas
/// Tundra e Montanha. Chamado automaticamente pelo EverwyrmAutoSetup quando o
/// pack está presente e a conversão está desatualizada — sem depender de menu.
///
///   1. Classifica os prefabs do pack pelo NOME REAL (conferido no import):
///      Tree_A = abetos nevados densos · Tree_B = pinheiros altos/ralos com
///      raízes expostas · Tree_C = decíduas nuas com neve (bétulas) ·
///      Bush_A · Grass_A/B/C (tufos secos) · Rock_* · Rock_Group (afloramento) ·
///      Log/Stump/Root (madeira caída) · Debris (galhada fina).
///      Ficam FORA: cercas/planks (não existe civilização no mundo do GDD),
///      montanhas de fundo (cenário, não scatter), partículas e *_Group de
///      grama/plank (um cluster inteiro numa instância flutua no relevo).
///   2. Reconstrói prefabs LIMPOS em Assets/Everwyrm/Winter (raiz + LODGroup +
///      só MeshRenderers válidos, sem colliders — regras de tree instance do
///      terrain; o pack original fica intacto). DIFERENTE do DesertSetup, os
///      materiais originais são MANTIDOS: o template HDRP do pack (extraído de
///      SRP/HDRP Template*.unitypackage) traz shaders com vento GPU e neve.
///   3. Reaponta os arrays winter* do InfiniteTerrain da cena aberta e liga as
///      TerrainLayers de chão do pack (neve + folhas congeladas).
///   4. Garante o prefab "AG Global Settings" na cena (o pack exige um por
///      cena para vento/neve/tint funcionarem) e converte os materiais de FX
///      das partículas (shader builtin → HDRP/Unlit).
///
/// Menu manual para iterar: Tools > Everwyrm > Winter — (Re)converter pack
///
/// LIÇÃO (caça ao "rosa parcial"): folhagem do pack usa transmissão/SSS com
/// Diffusion Profiles (AG_Trees/AG_Grass, em Nature Pack - Common). Perfil NÃO
/// registrado = partes magenta SEM erro no Console. Estão registrados na
/// Diffusion Profile List do DefaultSettingsVolumeProfile do projeto — se o
/// volume default for recriado, re-registrar por lá.
/// </summary>
public static class WinterSetup
{
    const string OutRoot = "Assets/Everwyrm";
    const string OutDir = OutRoot + "/Winter";
    const string Pack = "Assets/ANGRY MESH/Nature Pack - Winter Environment";

    // Prefab-sentinela: se não existe, a conversão está desatualizada (o marker
    // antigo era só a pasta — que ficava criada mesmo com a classificação vazia).
    const string ConvertedProbe = OutDir + "/Tree_A_01.prefab";

    // ------------------------------------------------ CLASSIFICAÇÃO
    // Nome do prefab (lowercase) → categoria. A PRIMEIRA que bater vence, então
    // "tree_b_01_roots" cai em pine antes de deadwood ("root") e "rock_group"
    // vem antes de "rock". Nomes conferidos no pack importado.
    static readonly (string field, string[] keywords)[] Categories =
    {
        ("fir",      new[] { "tree_a" }),
        ("pine",     new[] { "tree_b" }),
        ("birch",    new[] { "tree_c" }),
        ("bush",     new[] { "bush" }),
        ("grass",    new[] { "grass_a", "grass_b", "grass_c" }),
        ("outcrop",  new[] { "rock_group" }),
        ("rock",     new[] { "rock_" }),
        ("deadwood", new[] { "log_", "stump_", "root_" }),
        ("debris",   new[] { "debris_" }),
    };

    // Peças que NÃO entram no scatter (decisão de design + regras técnicas).
    // Os Grass_*_Group_* FICAM: a própria Demo do pack os pinta como terrain
    // trees (1.339 instâncias na cena 1) — footprint pequeno, não flutuam.
    static readonly string[] Excluded =
    {
        "fence", "plank",                       // civilização — não existe no GDD
        "mountain",                             // cenário de fundo gigante
        "fog", "storm", "snow_0",               // partículas (a neve ambiente é ligada à parte)
        "settings", "camera", "demo", "billboard", "particle", "fx",
    };

    // ------------------------------------------------------------- DETECÇÃO
    /// <summary>Pack importado no projeto?</summary>
    public static bool IsInstalled => FindPackRoot() != null;

    /// <summary>Conversão em dia? (probe = prefab de árvore convertido)</summary>
    public static bool IsConverted =>
        AssetDatabase.LoadAssetAtPath<GameObject>(ConvertedProbe) != null;

    static string FindPackRoot()
    {
        if (AssetDatabase.IsValidFolder(Pack)) return Pack;

        // fallback: qualquer pasta "*winter*" um ou dois níveis abaixo de Assets/
        foreach (var top in AssetDatabase.GetSubFolders("Assets"))
        {
            if (System.IO.Path.GetFileName(top).ToLowerInvariant().Contains("winter")) return top;
            foreach (var sub in AssetDatabase.GetSubFolders(top))
                if (System.IO.Path.GetFileName(sub).ToLowerInvariant().Contains("winter")) return sub;
        }
        return null;
    }

    // ------------------------------------------------------------- CONVERSÃO
    [MenuItem("Tools/Everwyrm/Winter — (Re)converter pack")]
    public static void ConvertMenu() => Convert();

    public static void Convert()
    {
        string pkg = FindPackRoot();
        if (pkg == null)
        {
            Debug.LogError("[WinterSetup] Winter Environment não encontrado em Assets/ " +
                           $"(esperado '{Pack}').");
            return;
        }

        WarnIfNoHdrpTemplate();
        EnsureFolder(OutRoot);
        EnsureFolder(OutDir);

        // ---- 1. classifica os prefabs do pack por keyword
        var byCategory = new Dictionary<string, List<GameObject>>();
        foreach (var (field, _) in Categories) byCategory[field] = new List<GameObject>();

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { pkg }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string lower = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            string category = null;
            foreach (var (field, keywords) in Categories)
                if (MatchesAny(lower, keywords)) { category = field; break; }
            if (category == null || MatchesAny(lower, Excluded)) continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) byCategory[category].Add(go);
        }

        // ---- 2. reconstrói prefabs limpos (materiais do pack MANTIDOS)
        var converted = new Dictionary<string, List<GameObject>>();
        int total = 0;
        foreach (var kv in byCategory)
        {
            converted[kv.Key] = new List<GameObject>();
            foreach (var src in kv.Value)
            {
                var clean = RebuildClean(src, src.name);
                if (clean != null) { converted[kv.Key].Add(clean); total++; }
            }
        }
        AssetDatabase.SaveAssets();

        if (total == 0)
        {
            Debug.LogWarning("[WinterSetup] Nenhum prefab classificado — os nomes do pack " +
                             "não bateram com as keywords. Ajuste 'Categories'/'Excluded' " +
                             "no WinterSetup.cs e rode Tools > Everwyrm > Winter — (Re)converter pack.");
            return;
        }

        // ---- 3. reaponta o InfiniteTerrain da cena aberta
        var world = Object.FindFirstObjectByType<InfiniteTerrain>();
        if (world != null)
        {
            world.winterFirPrefabs = InfiniteTerrain.PaintTree.From(converted["fir"]);
            world.winterPinePrefabs = InfiniteTerrain.PaintTree.From(converted["pine"]);
            world.winterBirchPrefabs = InfiniteTerrain.PaintTree.From(converted["birch"]);
            world.winterBushPrefabs = InfiniteTerrain.PaintTree.From(converted["bush"]);
            world.winterGrassPrefabs = InfiniteTerrain.PaintTree.From(converted["grass"]);
            world.winterRockPrefabs = InfiniteTerrain.PaintTree.From(converted["rock"]);
            world.winterOutcropPrefabs = InfiniteTerrain.PaintTree.From(converted["outcrop"]);
            world.winterDeadwoodPrefabs = InfiniteTerrain.PaintTree.From(converted["deadwood"]);
            world.winterDebrisPrefabs = InfiniteTerrain.PaintTree.From(converted["debris"]);

            // chão de inverno: TerrainLayers autorais do pack (neve funda com
            // relevo no mask + tapete de folhas congeladas p/ debaixo da mata)
            if (world.snowLayer == null)
                world.snowLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                    Pack + "/Sources/Terrain Layers/Snow_01_Layer.terrainlayer");
            if (world.winterGroundLayer == null)
                world.winterGroundLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                    Pack + "/Sources/Terrain Layers/Leaves_01_Layer.terrainlayer");

            // neve ambiente: partícula do pack seguindo o jogador dentro da Tundra
            if (world.winterSnowfallPrefab == null)
                world.winterSnowfallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Pack + "/Prefabs/Particles/Snow_01.prefab");

            EditorUtility.SetDirty(world);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        }

        // ---- 4. AG Global Settings na cena (vento/neve globais do pack)
        EnsureGlobalSettings();
        EnsureFxMaterials();

        Debug.Log($"<b>Inverno pronto!</b> {total} prefabs limpos em {OutDir} — " +
                  $"abetos {converted["fir"].Count} · pinheiros {converted["pine"].Count} · " +
                  $"bétulas {converted["birch"].Count} · arbustos {converted["bush"].Count} · " +
                  $"gramas {converted["grass"].Count} · pedras {converted["rock"].Count} · " +
                  $"afloramentos {converted["outcrop"].Count} · madeira caída {converted["deadwood"].Count} · " +
                  $"debris {converted["debris"].Count}" +
                  (world != null ? ", ligados ao InfiniteTerrain da cena."
                                 : " — abra a cena Main e rode o menu de novo para ligar."));
    }

    // ------------------------------------------------------- RECONSTRUÇÃO
    // Mesmo pipeline do DesertSetup.RebuildClean, sem troca de materiais:
    // raiz + LODGroup + só MeshRenderers válidos (regras de tree instance),
    // colliders descartados (não suportados em trees — spam de warning).
    // IMPORTANTE: alturas são COPIADAS do LODGroup original, mas o fade é
    // SEMPRE CrossFade animado — troca de LOD diluída em dither em vez de pop
    // na frente do jogador (os shaders de billboard/Tree Cross do pack são
    // autorados p/ CrossFade; fade None deixava o LOD de cruz magenta).
    const float SingleLodHeight = 0.18f;   // prefabs sem LODGroup (LOD único)

    static GameObject RebuildClean(GameObject src, string name)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var lodRenderers = new List<MeshRenderer[]>();
        var lodParams = new List<(float height, float fadeWidth)>();

        if (inst.TryGetComponent<LODGroup>(out var srcLod))
        {
            foreach (var lod in srcLod.GetLODs())
            {
                var valid = new List<MeshRenderer>();
                foreach (var r in lod.renderers)
                    if (IsValidMesh(r, out var mr)) valid.Add(mr);
                if (valid.Count > 0)
                {
                    lodRenderers.Add(valid.ToArray());
                    lodParams.Add((lod.screenRelativeTransitionHeight, lod.fadeTransitionWidth));
                }
            }
        }
        else
        {
            var all = new List<MeshRenderer>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                if (IsValidMesh(r, out var mr)) all.Add(mr);
            if (all.Count > 0)
            {
                all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                lodRenderers.Add(all.ToArray());
                lodParams.Add((SingleLodHeight, 0f));
            }
        }

        if (lodRenderers.Count == 0)
        {
            Debug.LogWarning($"[WinterSetup] {name}: nenhum MeshRenderer válido — ignorado.");
            Object.DestroyImmediate(inst);
            return null;
        }

        var root = new GameObject(name);
        var lods = new List<LOD>();
        for (int i = 0; i < lodRenderers.Count; i++)
        {
            var built = new List<Renderer>();
            foreach (var r in lodRenderers[i])
            {
                var child = new GameObject(r.name);
                child.transform.SetParent(root.transform, false);
                // instância na origem: transform de mundo == relativo à raiz
                child.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                child.transform.localScale = r.transform.lossyScale;

                child.AddComponent<MeshFilter>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                var mr = child.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                built.Add(mr);
            }
            lods.Add(new LOD(lodParams[i].height, built.ToArray())
            {
                fadeTransitionWidth = lodParams[i].fadeWidth
            });
        }

        var lodGroup = root.AddComponent<LODGroup>();
        lodGroup.SetLODs(lods.ToArray());
        lodGroup.fadeMode = LODFadeMode.CrossFade;
        lodGroup.animateCrossFading = true;
        lodGroup.RecalculateBounds();

        var saved = PrefabUtility.SaveAsPrefabAsset(root, OutDir + "/" + name + ".prefab");
        Object.DestroyImmediate(inst);
        Object.DestroyImmediate(root);
        return saved;
    }

    static bool IsValidMesh(Renderer r, out MeshRenderer mr)
    {
        mr = r as MeshRenderer;
        return mr != null && mr.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null;
    }

    // ------------------------------------------------------------ MATERIAIS DE FX
    // As partículas do pack (neve/nuvem) usam o shader BUILTIN "Particles/Alpha
    // Blended" (fileID 211) — rosa em HDRP, e o template HDRP do pack não as
    // cobre. Converte in-place p/ HDRP/Unlit transparente. Limitação conhecida:
    // HDRP/Unlit ignora vertex color, então o fade por Color over Lifetime some
    // (flocos aparecem/desaparecem sem esmaecer) — melhor que quads rosa.
    static readonly string[] FxMaterials =
    {
        Pack + "/Sources/Materials/FX_Snow_A_01.mat",
        Pack + "/Sources/Materials/FX_Cloud_A_01.mat",
        Pack + "/Sources/Materials/FX_Cloud_B_01.mat",
    };

    /// <summary>Converte os materiais de FX se ainda estiverem em shader builtin (barato, idempotente).</summary>
    public static void EnsureFxMaterials()
    {
        var unlit = Shader.Find("HDRP/Unlit");
        if (unlit == null) return;

        bool changed = false;
        foreach (var path in FxMaterials)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == unlit) continue;

            // ler ANTES da troca de shader (as props antigas ainda existem)
            var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Color c = m.HasProperty("_TintColor") ? m.GetColor("_TintColor")
                    : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

            m.shader = unlit;
            m.SetTexture("_UnlitColorMap", tex);
            m.SetColor("_UnlitColor", c);
            m.SetFloat("_SurfaceType", 1f);      // transparente
            m.SetFloat("_BlendMode", 0f);        // alpha blend
            m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            ValidateHdrp(m);
            EditorUtility.SetDirty(m);
            changed = true;
        }
        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[WinterSetup] Materiais de FX (neve/nuvem) convertidos para HDRP/Unlit transparente.");
        }
    }

    /// <summary>HDMaterial.ValidateMaterial via reflexão (recalcula keywords/passes do HDRP).</summary>
    static void ValidateHdrp(Material m)
    {
        var t = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime")
             ?? System.Type.GetType("UnityEditor.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Editor");
        t?.GetMethod("ValidateMaterial", new[] { typeof(Material) })?.Invoke(null, new object[] { m });
    }

    // ------------------------------------------------------------ CENA/AVISOS
    /// <summary>
    /// O pack EXIGE um "AG Global Settings" por cena: o script (ExecuteInEditMode)
    /// alimenta globais de vento/neve/tint dos shaders — sem ele, a textura global
    /// de tint fica não vinculada e os materiais mostram MANCHAS MAGENTA (sem
    /// nenhum erro no Console). Chamado pelo AutoSetup a cada recompilação.
    /// </summary>
    public static void EnsureGlobalSettings()
    {
        if (Object.FindFirstObjectByType<Transform>() == null) return; // sem cena aberta

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.ToLowerInvariant().Contains("global settings")) return; // já tem

        // o prefab mora no "Nature Pack - Common" (compartilhado entre os packs)
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab Global Settings",
                     new[] { "Assets/ANGRY MESH" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (n != "ag global settings") continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(inst.scene);
            Debug.Log("[WinterSetup] 'AG Global Settings' adicionado à cena (exigência do pack).");
            return;
        }
        Debug.LogWarning("[WinterSetup] Prefab 'AG Global Settings' não encontrado — " +
                         "adicione manualmente à cena (obrigatório p/ vento/neve dos shaders).");
    }

    static void WarnIfNoHdrpTemplate()
    {
        // O template HDRP fica em "Nature Pack - Common/Shaders/Unity HDRP" depois
        // de importar o .unitypackage incluído em SRP/. Sem ele, tudo magenta.
        if (!AssetDatabase.IsValidFolder("Assets/ANGRY MESH/Nature Pack - Common/Shaders/Unity HDRP"))
            Debug.LogWarning("[WinterSetup] Shaders HDRP do pack ausentes — importe o " +
                             ".unitypackage 'HDRP Template' em Nature Pack - Winter Environment/SRP " +
                             "(senão árvores/arbustos ficam magenta).");
    }

    // ------------------------------------------------------------ UTILIDADES
    static bool MatchesAny(string lower, string[] keywords)
    {
        foreach (var k in keywords)
            if (lower.Contains(k)) return true;
        return false;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path[..i], path[(i + 1)..]);
    }
}
