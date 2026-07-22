using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Integra o pacote Mountain Environment - Dynamic Nature (NatureManufacture)
/// ao bioma Montanha. Chamado pelo EverwyrmAutoSetup quando o pack está
/// presente e a conversão está desatualizada — sem depender de menu.
///
///   1. Classifica os prefabs pelo NOME REAL (auditoria completa em jul/2026):
///      Forest_pine (mata de encosta) · pine_plant/tree_00 (mudas/jovens) ·
///      rochas em 4 famílias (nua/moss/needles/soil) + river stones + muralhas ·
///      roots/stumps (barrancos) · dwarf pine/hazel (krummholz) · grama/urze/
///      mirtilo · cogumelos · galhos/pinhas/troncos (serrapilheira) · decals ·
///      estátua Sviatovid (landmark raríssimo — decisão de lore: mantida).
///      Ficam FORA: partículas (cachoeiras/poeira — não são tree instances),
///      peixes, versões *_Unity_Terrain (p/ detail system futuro), VS_Prefab_*
///      (duplicatas p/ Vegetation Studio), backgrounds e R.A.M trial.
///   2. Reconstrói prefabs LIMPOS em Assets/Everwyrm/Mountain (raiz + LODGroup,
///      sem colliders). CROSSFADE É FORÇADO: rocks/roots/mushrooms/details vêm
///      com FadeMode=None e a regra do projeto exige CrossFade animado em
///      multi-LOD. Alturas de transição do autor são preservadas.
///      Materiais MANTIDOS — o HD Support Pack 17.3 os sobrescreveu com os
///      shaders NM_* de HDRP (extraído em 2026-07-22; 6 diffusion profiles
///      NM_SSSSettings_* registrados no volume default — sem isso, folhagem
///      com manchas magenta SEM erro no console, como no Winter pack).
///   3. Reaponta os arrays mountain* do InfiniteTerrain da cena aberta e liga
///      as TerrainLayers de chão (moss/needless/soil — canais 7/8/9).
///   4. Garante o "Prefab_Wind" na cena (NM_Wind alimenta os globais
///      WIND_SETTINGS_* dos shaders — o "AG Global Settings" deste pack).
///
/// Menu manual para iterar: Tools > Everwyrm > Mountain — (Re)converter pack
/// </summary>
public static class MountainSetup
{
    const string OutRoot = "Assets/Everwyrm";
    const string OutDir = OutRoot + "/Mountain";
    const string NM = "Assets/NatureManufacture Assets";
    const string Pack = NM + "/Mountain Environment";

    // Prefab-sentinela: se não existe, a conversão está desatualizada.
    const string ConvertedProbe = OutDir + "/Prefab_Forest_pine_01.prefab";

    // ------------------------------------------------ CLASSIFICAÇÃO
    // Nome (lowercase) → categoria; a PRIMEIRA que bater vence — a ordem importa
    // (river_stone antes de rock; roots/stump antes das famílias de rock com
    // "stones_roots"; dwarf/hazel antes de pine).
    static readonly (string field, string[] keywords)[] Categories =
    {
        ("statue",     new[] { "sviatovid" }),
        ("riverStone", new[] { "river_stone" }),
        ("bush",       new[] { "dwarf_pine", "hazel" }),
        ("sapling",    new[] { "pine_plant", "pine_tree_00" }),
        ("pine",       new[] { "forest_pine", "forest_pine_plant" }),
        ("deadwood",   new[] { "_log", "log_pile", "stump", "roots", "root_" }),
        ("boulder",    new[] { "big_rock", "rock_wall", "mountain_rock_big" }),
        ("rock",       new[] { "rock", "stone" }),
        ("mushroom",   new[] { "mushroom" }),
        ("grass",      new[] { "grass_", "heath", "billberry", "lingonberry",
                               "rhododendron" }),
        ("detail",     new[] { "branch", "cone", "anthill" }),
        ("decal",      new[] { "decal" }),
    };

    // Fora do scatter (partículas/peixes não são tree instances; flats e VS são
    // duplicatas; o resto é demo/trial).
    static readonly string[] Excluded =
    {
        "particle", "fish", "cloud", "waterfall", "dust", "wind",
        "_unity_terrain", "vs_prefab", "background", "ram_", "spline",
        "camera", "demo", "road",
    };

    // ------------------------------------------------------------- DETECÇÃO
    public static bool IsInstalled => AssetDatabase.IsValidFolder(Pack);
    public static bool IsConverted =>
        AssetDatabase.LoadAssetAtPath<GameObject>(ConvertedProbe) != null;

    /// <summary>Shaders HDRP do support pack presentes? (NM_Bark é o probe.)</summary>
    public static bool HdrpPackImported =>
        AssetDatabase.LoadAssetAtPath<Shader>(NM + "/Foliage Shaders/NM_Bark.shader") != null;

    // ------------------------------------------------------------- CONVERSÃO
    [MenuItem("Tools/Everwyrm/Mountain — (Re)converter pack")]
    public static void ConvertMenu() => Convert();

    public static void Convert()
    {
        if (!IsInstalled)
        {
            Debug.LogError($"[MountainSetup] Mountain Environment não encontrado em '{Pack}'.");
            return;
        }
        if (!HdrpPackImported)
            Debug.LogWarning("[MountainSetup] Shaders NM_* ausentes — importe o " +
                             "'HD RP 17.3 ... .unitypackage' em Mountain Environment/HD and " +
                             "URP Support Packs (senão TUDO fica rosa).");

        EnsureFolder(OutRoot);
        EnsureFolder(OutDir);

        // ---- 1. classifica
        var byCategory = new Dictionary<string, List<GameObject>>();
        foreach (var (field, _) in Categories) byCategory[field] = new List<GameObject>();

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Pack }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string lower = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            // pasta também conta (ex.: "Prefabs Moss" não muda o nome do asset)
            string folder = System.IO.Path.GetDirectoryName(path).ToLowerInvariant();

            if (MatchesAny(lower, Excluded) || folder.Contains("unity terrain") ||
                folder.Contains("vs prefabs")) continue;

            string category = null;
            foreach (var (field, keywords) in Categories)
                if (MatchesAny(lower, keywords)) { category = field; break; }
            if (category == null) continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) byCategory[category].Add(go);
        }

        // ---- 2. reconstrói prefabs limpos (CrossFade FORÇADO, materiais mantidos)
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
            Debug.LogWarning("[MountainSetup] Nenhum prefab classificado — ajuste " +
                             "Categories/Excluded e rode o menu de novo.");
            return;
        }

        // ---- 3. reaponta o InfiniteTerrain da cena aberta
        var world = Object.FindFirstObjectByType<InfiniteTerrain>();
        if (world != null)
        {
            world.mountainPinePrefabs = InfiniteTerrain.PaintTree.From(converted["pine"]);
            world.mountainSaplingPrefabs = InfiniteTerrain.PaintTree.From(converted["sapling"]);
            world.mountainBushPrefabs = InfiniteTerrain.PaintTree.From(converted["bush"]);
            world.mountainGrassPrefabs = InfiniteTerrain.PaintTree.From(converted["grass"]);
            world.mountainRockPrefabs = InfiniteTerrain.PaintTree.From(converted["rock"]);
            world.mountainBoulderPrefabs = InfiniteTerrain.PaintTree.From(converted["boulder"]);
            world.mountainRiverStonePrefabs = InfiniteTerrain.PaintTree.From(converted["riverStone"]);
            world.mountainDeadwoodPrefabs = InfiniteTerrain.PaintTree.From(converted["deadwood"]);
            world.mountainMushroomPrefabs = InfiniteTerrain.PaintTree.From(converted["mushroom"]);
            world.mountainDetailPrefabs = InfiniteTerrain.PaintTree.From(converted["detail"]);
            world.mountainDecalPrefabs = InfiniteTerrain.PaintTree.From(converted["decal"]);
            world.mountainStatuePrefabs = InfiniteTerrain.PaintTree.From(converted["statue"]);

            // chão de montanha: musgo/agulhas/solo (canais 7/8/9 do alphamap)
            TerrainLayer TL(string name) => AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                Pack + "/Terrain/" + name + ".terrainlayer");
            if (world.mountainMossLayer == null) world.mountainMossLayer = TL("Terrain Layer_Moss");
            if (world.mountainNeedleLayer == null) world.mountainNeedleLayer = TL("Terrain Layer_needless_01");
            if (world.mountainSoilLayer == null) world.mountainSoilLayer = TL("Terrain Layer_soil_01");
            // rocha de montanha ganha a textura autoral do pack (canal 2)
            if (world.mountainRockTerrainLayer == null)
                world.mountainRockTerrainLayer = TL("Terrain Layer_rocks");

            EditorUtility.SetDirty(world);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        }

        // ---- 4. vento global do pack na cena
        EnsureWind();

        Debug.Log($"<b>Montanha pronta!</b> {total} prefabs limpos em {OutDir} — " +
                  $"pinheiros {converted["pine"].Count} · mudas {converted["sapling"].Count} · " +
                  $"krummholz {converted["bush"].Count} · grama/urze {converted["grass"].Count} · " +
                  $"pedras {converted["rock"].Count} · muralhas {converted["boulder"].Count} · " +
                  $"seixos de rio {converted["riverStone"].Count} · madeira {converted["deadwood"].Count} · " +
                  $"cogumelos {converted["mushroom"].Count} · detalhes {converted["detail"].Count} · " +
                  $"decals {converted["decal"].Count} · estátua {converted["statue"].Count}" +
                  (world != null ? ", ligados ao InfiniteTerrain."
                                 : " — abra a cena Main e rode o menu de novo."));
    }

    // ------------------------------------------------------- RECONSTRUÇÃO
    // Igual ao WinterSetup.RebuildClean com UMA diferença: CrossFade é FORÇADO
    // (o pack traz FadeMode=None em rocks/roots/mushrooms — pop de LOD visível;
    // regra do projeto: CrossFade animado obrigatório em multi-LOD).
    const float SingleLodHeight = 0.18f;

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
            Debug.LogWarning($"[MountainSetup] {name}: nenhum MeshRenderer válido — ignorado.");
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
        lodGroup.fadeMode = LODFadeMode.CrossFade;      // FORÇADO (ver comentário acima)
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

    // ------------------------------------------------------------ CENA
    /// <summary>NM_Wind seta os globais WIND_SETTINGS_* todo frame — sem o
    /// prefab na cena a vegetação NM fica estática (mesmo papel do
    /// AG Global Settings do Winter pack). Chamado pelo AutoSetup.</summary>
    public static void EnsureWind()
    {
        if (Object.FindFirstObjectByType<Transform>() == null) return; // sem cena

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name == "Prefab_Wind" || t.name == "Prefab_Wind_Spherical") return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            NM + "/NatureManufacture Wind/Prefab_Wind.prefab");
        if (prefab == null) return;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(inst.scene);
        Debug.Log("[MountainSetup] 'Prefab_Wind' adicionado à cena (vento dos shaders NM).");
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
