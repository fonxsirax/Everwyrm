using System.IO;
using UnityEngine;
using UnityEditor;
using JBooth.MicroSplat;

/// <summary>
/// Converte o pipeline de terreno do Everwyrm para MicroSplat (HDRP).
///
/// O InfiniteTerrain gera os tiles em RUNTIME, então não há terreno na cena para
/// converter pelo botão padrão do MicroSplat. Este menu faz o caminho todo:
///  1. Salva as 6 camadas como TerrainLayer assets na MESMA ordem dos canais do
///     alphamap do InfiniteTerrain (grama/floresta/rocha/neve/areia/rocha deserto);
///  2. Cria um terreno template com as dimensões reais dos tiles (250 m × 130 m);
///  3. Roda a conversão oficial (MicroSplatTerrainEditor.ConvertTerrains) — gera
///     shader, material, texture arrays e propData em Assets/Everwyrm/MicroSplat;
///  4. Aponta o InfiniteTerrain para os assets gerados — os tiles de runtime
///     passam a receber MicroSplatTerrain + Sync() no spawn (ver BuildTile).
///
/// Height blending (_HYBRIDHEIGHTBLEND) já vem ligado pela conversão, usando o
/// canal B dos mask maps das camadas. Anti-Tiling/Triplanar/Tessellation são
/// módulos pagos: depois de importá-los, ligue as opções no material gerado
/// (elas aparecem no inspector do material — nada muda no código).
/// </summary>
public static class MicroSplatSetup
{
    const string Dir = "Assets/Everwyrm/MicroSplat";
    const string TemplateName = "MicroSplat Template Terrain";

    [MenuItem("Tools/Everwyrm/MicroSplat — Converter Terreno")]
    public static void Convert()
    {
        var it = Object.FindFirstObjectByType<InfiniteTerrain>(FindObjectsInactive.Include);
        if (it == null)
        {
            EditorUtility.DisplayDialog("MicroSplat",
                "Nenhum InfiniteTerrain na cena aberta.", "OK");
            return;
        }
        if (it.microSplatMaterial != null)
        {
            EditorUtility.DisplayDialog("MicroSplat",
                "O InfiniteTerrain já usa um material MicroSplat.\n\n" +
                "Para reconverter do zero, limpe o campo Micro Splat Material e " +
                "apague a pasta Assets/Everwyrm/MicroSplat.", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(Dir))
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();
        }

        // campos privados do InfiniteTerrain (tiling/dimensões/cor da neve)
        var so = new SerializedObject(it);
        float tile = so.FindProperty("groundTextureTile").floatValue;
        float tileSize = so.FindProperty("tileSize").floatValue;
        float maxHeight = so.FindProperty("maxHeight").floatValue;
        Color snowColor = so.FindProperty("snowColor").colorValue;

        // ordem OBRIGATÓRIA = canais do alphamap em InfiniteTerrain.BuildTile:
        // 0 grama · 1 floresta · 2 rocha de montanha · 3 neve · 4 areia · 5 rocha do deserto
        var layers = new[]
        {
            LayerAsset("MS_0_Grama", it.grassDiffuse, it.grassNormal, it.grassMask, tile),
            LayerAsset("MS_1_Floresta", it.forestDiffuse, it.forestNormal, it.forestMask, tile),
            LayerAsset("MS_2_RochaMontanha", it.rockDiffuse, it.rockNormal, it.rockMask, tile * 1.6f),
            LayerAsset("MS_3_Neve", SnowTexture(it, snowColor), it.snowNormal, it.snowMask, tile),
            it.sandLayer,
            it.desertRockLayer
        };
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] == null || layers[i].diffuseTexture == null)
            {
                EditorUtility.DisplayDialog("MicroSplat",
                    $"Camada {i} sem textura. Selecione o InfiniteTerrain na cena " +
                    "(o OnValidate auto-preenche os campos) e rode o menu de novo.", "OK");
                return;
            }
        AssetDatabase.SaveAssets();

        // terreno template com as dimensões reais dos tiles do jogo — o MicroSplat
        // deriva _UVScale/_TriplanarUVScale do size, então precisa bater com o runtime
        var old = GameObject.Find(TemplateName);
        if (old != null) Object.DestroyImmediate(old);

        var td = new TerrainData { heightmapResolution = 129, alphamapResolution = 64 };
        td.size = new Vector3(tileSize, maxHeight, tileSize);
        td.terrainLayers = layers;
        string tdPath = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/MicroSplatTemplate.asset");
        AssetDatabase.CreateAsset(td, tdPath);   // ConvertTerrains salva config/shader/material na pasta deste asset

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = TemplateName;
        var terrain = go.GetComponent<Terrain>();

        MicroSplatTerrainEditor.ConvertTerrains(new[] { terrain }, layers);

        var mst = terrain.GetComponent<MicroSplatTerrain>();
        if (mst == null || mst.templateMaterial == null)
        {
            EditorUtility.DisplayDialog("MicroSplat",
                "A conversão não gerou material — veja o Console.", "OK");
            return;
        }

        it.microSplatMaterial = mst.templateMaterial;
        it.microSplatPropData = mst.propData;
        it.microSplatKeywords = mst.keywordSO != null
            ? mst.keywordSO : MicroSplatUtilities.FindOrCreateKeywords(mst.templateMaterial);
        it.microSplatBaseShader = FindBaseShader(mst.templateMaterial);
        EditorUtility.SetDirty(it);

        // fica na cena desativado: ative para ajustar o material vendo um tile de verdade
        go.SetActive(false);
        AssetDatabase.SaveAssets();

        EditorGUIUtility.PingObject(mst.templateMaterial);
        Debug.Log($"[MicroSplatSetup] Convertido. Material: " +
                  $"{AssetDatabase.GetAssetPath(mst.templateMaterial)} — height blending ativo. " +
                  "Módulos pagos (Anti-Tiling/Triplanar/Tessellation) são ligados no inspector do material.");
    }

    /// <summary>Basemap p/ builds: o Sync só resolve o shader _Base no editor.</summary>
    static Shader FindBaseShader(Material mat)
    {
        var path = AssetDatabase.GetAssetPath(mat.shader);
        if (string.IsNullOrEmpty(path)) return null;
        int i = path.LastIndexOf(".shader");
        if (i < 0) return null;
        return AssetDatabase.LoadAssetAtPath<Shader>(path.Remove(i) + "_Base.shader");
    }

    static TerrainLayer LayerAsset(string name, Texture2D d, Texture2D n, Texture2D m, float tile)
    {
        if (d == null) return null;
        string path = $"{Dir}/{name}.terrainlayer";
        var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (l == null)
        {
            l = new TerrainLayer { name = name };
            AssetDatabase.CreateAsset(l, path);
        }
        l.diffuseTexture = d;
        l.normalMapTexture = n;
        l.maskMapTexture = m;                    // height blending lê o canal B daqui
        l.normalScale = 1f;
        l.smoothness = 0.08f; l.metallic = 0f;   // mesmos valores do MakeLayer do runtime
        l.tileSize = new Vector2(tile, tile);
        EditorUtility.SetDirty(l);
        return l;
    }

    /// <summary>
    /// A neve não tem textura no projeto (o runtime usa um fallback de cor sólida),
    /// mas o texture array do MicroSplat precisa de um asset — gera um PNG 64×64.
    /// </summary>
    static Texture2D SnowTexture(InfiniteTerrain it, Color snowColor)
    {
        if (it.snowDiffuse != null) return it.snowDiffuse;

        string path = $"{Dir}/MS_3_Neve_D.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var px = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = snowColor;
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
