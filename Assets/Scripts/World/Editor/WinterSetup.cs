using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Integra o pacote Winter Environment - Nature Pack (ANGRY MESH) aos biomas
/// Tundra e Montanha. Chamado automaticamente pelo EverwyrmAutoSetup quando o
/// pack está presente e ainda não foi convertido — sem depender de menu.
///
///   1. Descobre os prefabs do pack por PALAVRA-CHAVE no nome (não hard-codeamos
///      nomes porque o pack ainda não foi importado — ajustar as listas Keywords
///      abaixo se a classificação errar após o import).
///   2. Reconstrói prefabs LIMPOS em Assets/Everwyrm/Winter (raiz + LODGroup +
///      só MeshRenderers válidos, sem colliders — regras de tree instance do
///      terrain; o pack original fica intacto). DIFERENTE do DesertSetup, os
///      materiais originais são MANTIDOS: o template HDRP do pack já traz
///      shaders próprios com vento GPU e neve (importar o .unitypackage HDRP
///      que vem dentro do pack ANTES de rodar isto).
///   3. Reaponta os arrays winter* do InfiniteTerrain da cena aberta.
///   4. Garante o prefab "AG Global Settings" na cena (o pack exige um por
///      cena para vento/neve/tint funcionarem).
///
/// Menu manual para iterar (reclassificar após ajustar keywords):
///   Tools > Everwyrm > Winter — (Re)converter pack
/// </summary>
public static class WinterSetup
{
    const string OutRoot = "Assets/Everwyrm";
    const string OutDir = OutRoot + "/Winter";

    // Raízes prováveis do pack (probe barato; fallback varre Assets/ um nível).
    static readonly string[] PackRootCandidates =
    {
        "Assets/ANGRY MESH/Winter Environment",
        "Assets/AngryMesh/Winter Environment",
        "Assets/Winter Environment",
    };

    // ------------------------------------------------ CLASSIFICAÇÃO (ajustável)
    // Nome do prefab (lowercase) precisa conter UMA keyword da categoria e
    // NENHUMA da lista de exclusão. TODO após o import: conferir no console o
    // resumo por categoria e afinar estas listas.
    static readonly (string field, string[] keywords)[] Categories =
    {
        ("fir",   new[] { "fir", "spruce", "pine" }),
        ("birch", new[] { "birch" }),
        ("bush",  new[] { "bush", "shrub" }),
        ("rock",  new[] { "rock", "stone", "cliff", "boulder" }),
    };

    // Peças que NÃO são vegetação/pedra de scatter (galhos, troncos, decor de cena…).
    static readonly string[] Excluded =
    {
        "root", "trunk", "log", "stump", "branch", "billboard",
        "particle", "fx", "vfx", "settings", "camera", "demo",
    };

    // ------------------------------------------------------------- DETECÇÃO
    /// <summary>Pack importado no projeto?</summary>
    public static bool IsInstalled => FindPackRoot() != null;

    /// <summary>Conversão já rodou? (marker = pasta de saída existente)</summary>
    public static bool IsConverted => AssetDatabase.IsValidFolder(OutDir);

    static string FindPackRoot()
    {
        foreach (var p in PackRootCandidates)
            if (AssetDatabase.IsValidFolder(p)) return p;

        // fallback: qualquer pasta "*Winter*" um ou dois níveis abaixo de Assets/
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
                           "(esperado algo como 'Assets/ANGRY MESH/Winter Environment').");
            return;
        }

        // template HDRP importado? (sem ele os materiais ficam magenta — mesmo
        // sintoma do RockyDesert). Heurística barata: existe algum shader do pack?
        WarnIfNoHdrpTemplate(pkg);

        EnsureFolder(OutRoot);
        EnsureFolder(OutDir);   // marker do IsConverted — criado mesmo se algo falhar abaixo

        // ---- 1. classifica os prefabs do pack por keyword
        var byCategory = new Dictionary<string, List<GameObject>>();
        foreach (var (field, _) in Categories) byCategory[field] = new List<GameObject>();

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { pkg }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string lower = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (MatchesAny(lower, Excluded)) continue;

            foreach (var (field, keywords) in Categories)
                if (MatchesAny(lower, keywords))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) byCategory[field].Add(go);
                    break;   // primeira categoria vence (fir antes de rock etc.)
                }
        }

        // ---- 2. reconstrói prefabs limpos (materiais do pack MANTIDOS)
        var converted = new Dictionary<string, List<GameObject>>();
        int total = 0;
        foreach (var (field, list) in EnumeratePairs(byCategory))
        {
            converted[field] = new List<GameObject>();
            foreach (var src in list)
            {
                var clean = RebuildClean(src, src.name);
                if (clean != null) { converted[field].Add(clean); total++; }
            }
        }
        AssetDatabase.SaveAssets();

        if (total == 0)
        {
            Debug.LogWarning("[WinterSetup] Nenhum prefab classificado — os nomes do pack " +
                             "não bateram com as keywords. Ajuste as listas 'Categories'/'Excluded' " +
                             "no WinterSetup.cs e rode Tools > Everwyrm > Winter — (Re)converter pack.");
            return;
        }

        // ---- 3. reaponta o InfiniteTerrain da cena aberta
        var world = Object.FindFirstObjectByType<InfiniteTerrain>();
        if (world != null)
        {
            world.winterFirPrefabs = converted["fir"].ToArray();
            world.winterBirchPrefabs = converted["birch"].ToArray();
            world.winterBushPrefabs = converted["bush"].ToArray();
            world.winterRockPrefabs = converted["rock"].ToArray();
            EditorUtility.SetDirty(world);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        }

        // ---- 4. AG Global Settings na cena (vento/neve globais do pack)
        EnsureGlobalSettings(pkg);

        Debug.Log($"<b>Inverno pronto!</b> {total} prefabs limpos em {OutDir} — " +
                  $"abetos {converted["fir"].Count} · bétulas {converted["birch"].Count} · " +
                  $"arbustos {converted["bush"].Count} · pedras {converted["rock"].Count}" +
                  (world != null ? ", ligados ao InfiniteTerrain da cena."
                                 : " — abra a cena Main e rode o menu de novo para ligar.") +
                  " Calibrar 'winterDensity' e as camadas Tundra/Montanha com a Demo do pack.");
    }

    // ------------------------------------------------------- RECONSTRUÇÃO
    // Mesmo pipeline do DesertSetup.RebuildClean, sem troca de materiais:
    // raiz + LODGroup + só MeshRenderers válidos (regras de tree instance),
    // colliders descartados (não suportados em trees — spam de warning).
    static readonly float[] LodHeights = { 0.18f, 0.09f, 0.045f, 0.02f };

    static GameObject RebuildClean(GameObject src, string name)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var lodRenderers = new List<MeshRenderer[]>();
        if (inst.TryGetComponent<LODGroup>(out var srcLod))
        {
            foreach (var lod in srcLod.GetLODs())
            {
                var valid = new List<MeshRenderer>();
                foreach (var r in lod.renderers)
                    if (IsValidMesh(r, out var mr)) valid.Add(mr);
                if (valid.Count > 0) lodRenderers.Add(valid.ToArray());
            }
        }
        else
        {
            var all = new List<MeshRenderer>();
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                if (IsValidMesh(r, out var mr)) all.Add(mr);
            if (all.Count > 0) lodRenderers.Add(all.ToArray());
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
            float h = i == lodRenderers.Count - 1 ? 0.006f
                    : LodHeights[Mathf.Min(i, LodHeights.Length - 1)];
            lods.Add(new LOD(h, built.ToArray()));
        }

        var lodGroup = root.AddComponent<LODGroup>();
        lodGroup.SetLODs(lods.ToArray());
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

    // ------------------------------------------------------------ CENA/AVISOS
    /// <summary>O pack exige um "AG Global Settings" por cena (vento/neve globais).</summary>
    static void EnsureGlobalSettings(string pkg)
    {
        if (Object.FindFirstObjectByType<Transform>() == null) return; // sem cena aberta

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.ToLowerInvariant().Contains("global settings")) return; // já tem

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab Global Settings", new[] { pkg }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Contains("global settings"))
                continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(inst.scene);
            Debug.Log("[WinterSetup] 'AG Global Settings' adicionado à cena (exigência do pack).");
            return;
        }
        Debug.LogWarning("[WinterSetup] Prefab 'AG Global Settings' não encontrado no pack — " +
                         "adicione manualmente à cena (obrigatório p/ vento/neve dos shaders).");
    }

    static void WarnIfNoHdrpTemplate(string pkg)
    {
        // O template HDRP vem como .unitypackage DENTRO do pack e precisa ser
        // importado manualmente. Se nenhum shader do pack existe, provavelmente
        // só o base foi importado → materiais magenta.
        if (AssetDatabase.FindAssets("t:Shader", new[] { pkg }).Length == 0)
            Debug.LogWarning("[WinterSetup] Nenhum shader encontrado no pack — importe o " +
                             ".unitypackage do template HDRP incluído no Winter Environment " +
                             "(senão árvores/arbustos ficam magenta).");
    }

    // ------------------------------------------------------------ UTILIDADES
    static bool MatchesAny(string lower, string[] keywords)
    {
        foreach (var k in keywords)
            if (lower.Contains(k)) return true;
        return false;
    }

    static IEnumerable<(string, List<GameObject>)> EnumeratePairs(Dictionary<string, List<GameObject>> d)
    {
        foreach (var kv in d) yield return (kv.Key, kv.Value);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path[..i], path[(i + 1)..]);
    }
}
