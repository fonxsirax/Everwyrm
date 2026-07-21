using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Converte o pacote RockyDesert (Built-in/Amplify → magenta em HDRP) para HDRP.
/// Chamado automaticamente pelo EverwyrmAutoSetup quando os prefabs convertidos
/// não existem — sem menu.
///
///   1. Gera materiais HDRP/Lit em Assets/Everwyrm/Desert/Materials usando as
///      texturas originais do pacote (albedo do penhasco, normais, casca, grama seca
///      com alpha-clip + double-sided).
///   2. Reconstrói os 14 prefabs LIMPOS em Assets/Everwyrm/Desert (LODGroups
///      saneados, sem colliders); o pacote original fica intacto.
///   3. Reaponta os arrays do InfiniteTerrain da cena aberta.
/// </summary>
public static class DesertSetup
{
    const string Pkg = "Assets/RockyDesert/";
    const string Tex = Pkg + "Textures/";
    const string OutRoot = "Assets/Everwyrm";
    const string OutDir = OutRoot + "/Desert";
    const string MatDir = OutDir + "/Materials";

    static readonly string[] PrefabNames =
    {
        "SM_RockSide_A1", "SM_Rock_Side-A2",
        "SM_Small_Rock_1", "SM_Small_Rock_2", "SM_Small_Rock_3", "SM_Small_Rock_4",
        "SM_Small_Rock_B1", "SM_Small_Rock_B2",
        "SM_Tree_1", "SM_Tree_A", "SM_Tree_B",
        "SM_DeadGrass_A", "SM_DeadGrass_A1", "SM_DeadGrass_A2",
    };

    public static void Convert()
    {
        if (!AssetDatabase.IsValidFolder(Pkg.TrimEnd('/')))
        {
            Debug.LogError("Pacote RockyDesert não encontrado em Assets/RockyDesert.");
            return;
        }
        EnsureFolder(OutRoot);
        EnsureFolder(OutDir);
        EnsureFolder(MatDir);

        // ---- 1. materiais HDRP (mapeados pelo NOME do material original)
        var map = new Dictionary<string, Material>
        {
            ["MI_Side_Rock_A1"] = RockMat("M_Desert_RockSide_A1", "T_RockSide_A1_n.tga"),
            ["MI_Side_Rock_A2"] = RockMat("M_Desert_RockSide_A2", "T_Rock_Side-A2_nm.png"),
            ["MI_Small_Rock"]   = RockMat("M_Desert_SmallRock",   "T_Small_Rock_1_nm.tga"),
            ["MI_Small_Rock_A"] = RockMat("M_Desert_SmallRock",   "T_Small_Rock_1_nm.tga"),
            ["MI_DeadGrass_A"]  = GrassMat(),
            ["MI_DeadGrass_A1"] = GrassMat(),
            ["MI_DeadTree_A"]   = TreeMat(),
        };

        // ---- 2. prefabs LIMPOS, reconstruídos do zero (não variantes!).
        //      Os originais violam as regras de tree instance do terrain:
        //       - entradas de renderer NULAS no LODGroup ("renderer of type other than
        //         MeshRenderer") em 7 prefabs;
        //       - SM_Tree_*/B1 sem MeshRenderer/LODGroup na raiz;
        //       - MeshColliders (não suportados em trees — spam de warning por instância).
        //      Reconstruir dá controle total: raiz + LODGroup + só MeshRenderers válidos.
        var converted = new Dictionary<string, GameObject>();
        foreach (var name in PrefabNames)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Pkg + "Prefabs/" + name + ".prefab");
            if (src == null) { Debug.LogWarning($"Prefab não encontrado: {name}"); continue; }
            var clean = RebuildClean(src, name, map);
            if (clean != null) converted[name] = clean;
        }
        AssetDatabase.SaveAssets();

        // ---- 3. reaponta o InfiniteTerrain da cena aberta
        var world = UnityEngine.Object.FindFirstObjectByType<InfiniteTerrain>();
        if (world != null)
        {
            InfiniteTerrain.PaintTree[] Get(params string[] names)
            {
                var list = new List<GameObject>();
                foreach (var n in names)
                    if (converted.TryGetValue(n, out var go)) list.Add(go);
                return InfiniteTerrain.PaintTree.From(list);
            }
            world.desertFormationPrefabs = Get("SM_RockSide_A1", "SM_Rock_Side-A2");
            world.desertRockPrefabs = Get("SM_Small_Rock_2", "SM_Small_Rock_3", "SM_Small_Rock_4");
            world.desertPebblePrefabs = Get("SM_Small_Rock_1", "SM_Small_Rock_B1", "SM_Small_Rock_B2");
            world.desertTreePrefabs = Get("SM_Tree_1", "SM_Tree_A", "SM_Tree_B");
            world.desertGrassPrefabs = Get("SM_DeadGrass_A", "SM_DeadGrass_A1", "SM_DeadGrass_A2");
            if (world.sandLayer == null)
                world.sandLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Pkg + "Terrain/Terrain_Sand.terrainlayer");
            if (world.desertRockLayer == null)
                world.desertRockLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Pkg + "Terrain/Terrain_Rock.terrainlayer");
            EditorUtility.SetDirty(world);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        }

        Debug.Log($"<b>Deserto pronto!</b> {converted.Count} prefabs convertidos para HDRP em {OutDir} " +
                  (world != null ? "e ligados ao InfiniteTerrain da cena." :
                   "— abra a cena Main e os campos serão auto-preenchidos (OnValidate)."));
    }

    // ------------------------------------------------------- RECONSTRUÇÃO
    // Alturas de tela por LOD (descendo); o último nível vira o limiar de cull.
    static readonly float[] LodHeights = { 0.18f, 0.09f, 0.045f, 0.02f };

    /// <summary>
    /// Recria o prefab como raiz + LODGroup + filhos MeshFilter/MeshRenderer,
    /// descartando renderers nulos/inválidos e colliders. Materiais trocados
    /// pelos HDRP via nome do material original.
    /// </summary>
    static GameObject RebuildClean(GameObject src, string name, Dictionary<string, Material> matMap)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        // grupos de renderers por LOD (ou todos juntos se não houver LODGroup)
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
            Debug.LogWarning($"{name}: nenhum MeshRenderer válido — ignorado.");
            UnityEngine.Object.DestroyImmediate(inst);
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
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != null && matMap.TryGetValue(mats[m].name, out var hd))
                        mats[m] = hd;
                mr.sharedMaterials = mats;
                built.Add(mr);
            }
            float h = i == lodRenderers.Count - 1 ? 0.006f
                    : LodHeights[Mathf.Min(i, LodHeights.Length - 1)];
            lods.Add(new LOD(h, built.ToArray()));
        }

        var lodGroup = root.AddComponent<LODGroup>();
        lodGroup.SetLODs(lods.ToArray());
        lodGroup.fadeMode = LODFadeMode.CrossFade;      // troca de LOD em dither, sem pop
        lodGroup.animateCrossFading = true;
        lodGroup.RecalculateBounds();

        var saved = PrefabUtility.SaveAsPrefabAsset(root, OutDir + "/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(inst);
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    static bool IsValidMesh(Renderer r, out MeshRenderer mr)
    {
        mr = r as MeshRenderer;
        return mr != null && mr.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null;
    }

    // ------------------------------------------------------------ MATERIAIS
    static Material RockMat(string name, string normalTex)
    {
        // Rochas: albedo do penhasco + normal específica de cada mesh, bem fosco.
        // (O shader Amplify original misturava areia por cima — aproximação com Lit.)
        return SaveMat(name, m =>
        {
            m.SetTexture("_BaseColorMap", LoadTex("T_Cliff_Surface_alb.tga"));
            m.SetTexture("_NormalMap", LoadTex(normalTex));
            m.SetFloat("_Smoothness", 0.08f);
        });
    }

    static Material GrassMat()
    {
        // Grama seca: cartões com alpha — precisa de clip + double-sided.
        return SaveMat("M_Desert_DeadGrass", m =>
        {
            m.SetTexture("_BaseColorMap", LoadTex("T_DeadGrass_A1.TGA"));
            m.SetTexture("_NormalMap", LoadTex("T_DeadGrass_A_Normal.png"));
            m.SetFloat("_Smoothness", 0.1f);

            m.SetFloat("_AlphaCutoffEnable", 1f);
            m.SetFloat("_AlphaCutoff", 0.35f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;

            m.SetFloat("_DoubleSidedEnable", 1f);
            m.EnableKeyword("_DOUBLESIDED_ON");
            m.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
            m.SetFloat("_CullModeForward", (float)UnityEngine.Rendering.CullMode.Off);
            m.doubleSidedGI = true;
        });
    }

    static Material TreeMat()
    {
        return SaveMat("M_Desert_DeadTree", m =>
        {
            m.SetTexture("_BaseColorMap", LoadTex("T_TreeBark_Albedo.tga"));
            m.SetTexture("_NormalMap", LoadTex("T_TreeBark_Normal.png"));
            m.SetFloat("_Smoothness", 0.1f);
        });
    }

    static Material SaveMat(string name, Action<Material> configure)
    {
        string path = MatDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;   // idempotente: reexecutar não duplica

        var shader = Shader.Find("HDRP/Lit");
        if (shader == null)
        {
            Debug.LogError("Shader HDRP/Lit não encontrado — o projeto está em HDRP?");
            shader = Shader.Find("Standard");
        }
        var m = new Material(shader);
        configure(m);
        ValidateHdrp(m);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>HDMaterial.ValidateMaterial via reflexão (recalcula keywords/passes do HDRP).</summary>
    static void ValidateHdrp(Material m)
    {
        var t = Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime")
             ?? Type.GetType("UnityEditor.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Editor");
        t?.GetMethod("ValidateMaterial", new[] { typeof(Material) })?.Invoke(null, new object[] { m });
    }

    static Texture2D LoadTex(string file)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + file);
        if (tex == null) Debug.LogWarning($"Textura não encontrada: {Tex + file}");
        return tex;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path[..i], path[(i + 1)..]);
    }
}
