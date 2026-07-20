using System.IO;
using UnityEngine;
using UnityEditor;
using JBooth.MicroSplat;

/// <summary>
/// Converte o pipeline de terreno do Everwyrm para MicroSplat (HDRP).
///
/// O InfiniteTerrain gera os tiles em RUNTIME, então não há terreno na cena para
/// converter pelo botão padrão do MicroSplat. Este menu faz o caminho todo:
///  1. Salva as camadas (6, ou 7 com o Winter pack) como TerrainLayer assets na
///     MESMA ordem dos canais do alphamap do InfiniteTerrain (grama/floresta/
///     rocha/neve/areia/rocha deserto/folhas congeladas);
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

        // Auto-preenche os campos do InfiniteTerrain ANTES de montar as camadas —
        // o OnValidate só roda quando o objeto é tocado no Inspector, então uma
        // referência quebrada/limpa na cena aberta derrubava o menu com
        // "Camada X sem textura" até o usuário selecionar o objeto na mão.
        typeof(InfiniteTerrain).GetMethod("OnValidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(it, null);

        // campos privados do InfiniteTerrain (tiling/dimensões/cor da neve)
        var so = new SerializedObject(it);
        float tile = so.FindProperty("groundTextureTile").floatValue;
        float tileSize = so.FindProperty("tileSize").floatValue;
        float maxHeight = so.FindProperty("maxHeight").floatValue;
        Color snowColor = so.FindProperty("snowColor").colorValue;

        // ordem OBRIGATÓRIA = canais do alphamap em InfiniteTerrain.BuildTile:
        // 0 grama · 1 floresta · 2 rocha de montanha · 3 neve · 4 areia
        // · 5 rocha do deserto · 6 folhas congeladas (opcional, Winter pack)
        var layerList = new System.Collections.Generic.List<TerrainLayer>
        {
            LayerAsset("MS_0_Grama", it.grassDiffuse, it.grassNormal, it.grassMask, tile),
            LayerAsset("MS_1_Floresta", it.forestDiffuse, it.forestNormal, it.forestMask, tile),
            LayerAsset("MS_2_RochaMontanha", it.rockDiffuse, it.rockNormal, it.rockMask, tile * 1.6f),
            it.snowLayer != null ? it.snowLayer   // neve funda do Winter pack
                : LayerAsset("MS_3_Neve", SnowTexture(it, snowColor), it.snowNormal, it.snowMask, tile),
            it.sandLayer,
            it.desertRockLayer
        };
        if (it.winterGroundLayer != null) layerList.Add(it.winterGroundLayer);
        var layers = layerList.ToArray();
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

    // ================================================= MÓDULOS DA COLLECTION
    const string AntiTileTex = "Packages/com.jbooth.microsplat.anti-tile/Textures/";

    /// <summary>
    /// Fase 1 do plano de qualidade: liga os módulos da Terrain Collection que
    /// combatem a repetição, com os mesmos defaults que o inspector aplicaria.
    /// Idempotente — rodar de novo só recompila. Custo: STOCHASTIC + noises são
    /// baratos; TRIPLANAR global aumenta os samples (o _BRANCHSAMPLES corta os
    /// canais sem peso) — medir FPS depois de ligar.
    /// </summary>
    [MenuItem("Tools/Everwyrm/MicroSplat — Ativar Módulos (Fase 1)")]
    public static void EnableQualityModules()
    {
        if (!TryGetMaterial(out var mat, out var keywords)) return;

        // repetição: sampling estocástico (mata a "colmeia") + noises anti-tile
        // (variação de normal/albedo por região, perto e longe) + triplanar
        // (encostas íngremes das Montanhas/mesetas sem textura esticada)
        AddKeywords(keywords,
            "_STOCHASTIC",
            "_NORMALNOISE", "_DETAILNOISE", "_DISTANCENOISE",
            "_TRIPLANAR");
        Recompile(mat, keywords);

        // noises padrão do pacote (o keyword sozinho fica neutro: default = grey/bump)
        SetTex(mat, "_NormalNoise", AntiTileTex + "microsplat_def_detail_normal_01.png");
        SetTex(mat, "_DetailNoise", AntiTileTex + "microsplat_def_detail_noise.png");
        SetTex(mat, "_DistanceNoise", AntiTileTex + "microsplat_def_detail_noise.png");
        // (escala do noise, força, distâncias de fade) — calibrados p/ tiles de 12 m
        mat.SetVector("_NormalNoiseScaleStrength", new Vector4(8f, 0.5f, 0f, 0f));
        mat.SetVector("_DetailNoiseScaleStrengthFade", new Vector4(4f, 0.35f, 12f, 0f));
        mat.SetVector("_DistanceNoiseScaleStrengthFade", new Vector4(0.25f, 0.5f, 100f, 250f));

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        MicroSplatObject.SyncAll();
        Debug.Log("[MicroSplatSetup] Fase 1 ligada: Stochastic + Anti-Tile (normal/detail/distance) " +
                  "+ Triplanar. Teste o FPS; tessellation é um menu separado.");
    }

    /// <summary>
    /// Tessellation em menu próprio: é o módulo mais caro e o displacement é só
    /// visual — amplitude alta descola o chão dos raycasts de pouso/comida.
    /// Alterna ligado/desligado a cada execução.
    /// </summary>
    [MenuItem("Tools/Everwyrm/MicroSplat — Tessellation (alternar)")]
    public static void ToggleTessellation()
    {
        if (!TryGetMaterial(out var mat, out var keywords)) return;

        bool on = keywords.keywords.Contains("_TESSDISTANCE");
        if (on) keywords.keywords.Remove("_TESSDISTANCE");
        else AddKeywords(keywords, "_TESSDISTANCE");
        Recompile(mat, keywords);

        if (!on)
        {
            // x=fator de tess, y=DESLOCAMENTO EM METROS (manter baixo!), z=mip bias, w=edge length
            mat.SetVector("_TessData1", new Vector4(8f, 0.15f, 0f, 16f));
            // x=dist mín, y=dist máx do fade (m), z=shaping, w=up bias
            mat.SetVector("_TessData2", new Vector4(0f, 25f, 1f, 0f));
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        MicroSplatObject.SyncAll();
        Debug.Log($"[MicroSplatSetup] Tessellation {(!on ? "LIGADA (displacement 0.15 m)" : "desligada")}.");
    }

    // ============================================ PER-TEXTURE (7 GROUNDS)
    /// <summary>
    /// Identidade individual por ground via propData (textura 32×32 de propriedades,
    /// 1 fetch no shader — custo ~zero). Estratégia de custo: TRIPLANAR só nas 2
    /// rochas (as demais usam projeção de cima = mesmo visual em chão plano, ⅓ dos
    /// samples) e STOCHASTIC só nas camadas planas (o triplanar já quebra repetição
    /// na rocha). Semântica dos canais conferida no shader gerado:
    /// brightness é ADITIVO (0 = neutro); heightOffset é v-1 (1 = neutro);
    /// os demais são multiplicadores (1 = neutro). PropData nasce zerado, então
    /// TODO canal habilitado precisa ser escrito para TODOS os grounds.
    /// </summary>
    [MenuItem("Tools/Everwyrm/MicroSplat — Per-Texture 7 Grounds (Fase 1b)")]
    public static void ConfigurePerTexture()
    {
        if (!TryGetMaterial(out var mat, out var keywords)) return;
        var it = Object.FindFirstObjectByType<InfiniteTerrain>(FindObjectsInactive.Include);
        var propData = it.microSplatPropData;
        if (propData == null)
        {
            EditorUtility.DisplayDialog("MicroSplat", "InfiniteTerrain sem propData.", "OK");
            return;
        }

        AddKeywords(keywords,
            "_PERTEXBRIGHTNESS", "_PERTEXCONTRAST", "_PERTEXSATURATION",
            "_PERTEXNORMSTR", "_PERTEXSMOOTHSTR", "_PERTEXAOSTR",
            "_PERTEXHEIGHTCONTRAST", "_PERTEXHEIGHTOFFSET",
            "_PERTEXSTOCHASTIC", "_PERTEXTRIPLANAR",
            "_PERTEXDETAILNOISESTRENGTH", "_PERTEXDISTANCENOISESTRENGTH");

        // 1º: marca as linhas que vamos escrever como INICIALIZADAS (bit na linha 15).
        // Sem isso, o DrawPerTextureGUI do inspector considera o propData virgem e
        // SOBRESCREVE tudo com defaults na primeira vez que o material é aberto
        // (foi exatamente o que apagou a 1ª tentativa desta configuração).
        // 2º: preenche o resto do array (tex 7..31) com os mesmos defaults que o
        // inspector aplicaria, para os índices não usados ficarem sãos.
        StampRow(propData, 2, new Color(1f, 0f, 1f, 0f));   // norm ×, smooth +, ao pow, metal
        StampRow(propData, 3, new Color(0f, 1f, 0.4f, 1f)); // bright +, contraste ×, porosidade, foam
        StampRow(propData, 4, new Color(1f, 1f, 1f, 1f));   // detail/distance noise ×
        StampRow(propData, 9, new Color(1f, 1f, 1f, 1f));   // triMode, triContraste, stochastic, saturação
        StampRow(propData, 10, new Color(1f, 1f, 1f, 1f));  // clusterC, clusterB, hOffset, hContraste

        // SEMÂNTICA (conferida no shader): norm ×1 · smooth ADITIVO 0 · AO EXPOENTE 1
        //                   nome           stoch  tri    norm  smth+  aoPow bright contr porosid detN  distN  sat   hOff  hContr
        Ground(propData, 0, "grama",          true, false, 0.90f, 0.00f, 1.00f, 0.00f, 1.06f, 0.45f, 1.0f, 1.0f, 1.03f, 1.00f, 1.00f);
        Ground(propData, 1, "terra floresta", true, false, 1.15f, -0.05f, 1.20f, -0.02f, 1.05f, 0.55f, 1.0f, 1.1f, 1.05f, 1.00f, 0.80f);
        Ground(propData, 2, "rocha montanha", false, true, 1.25f, 0.00f, 1.30f, 0.00f, 1.08f, 0.15f, 0.7f, 0.8f, 0.95f, 1.00f, 1.60f);
        Ground(propData, 3, "neve",           true, false, 0.55f, 0.12f, 0.85f, 0.03f, 0.95f, 0.30f, 0.8f, 1.2f, 0.90f, 1.15f, 0.50f);
        Ground(propData, 4, "areia",          true, false, 0.85f, -0.05f, 0.95f, 0.00f, 1.05f, 0.70f, 1.2f, 1.3f, 1.05f, 0.90f, 0.70f);
        Ground(propData, 5, "rocha deserto",  false, true, 1.20f, 0.00f, 1.35f, 0.00f, 1.06f, 0.15f, 0.7f, 0.8f, 1.00f, 1.00f, 1.50f);
        Ground(propData, 6, "folhas congel.", true, false, 1.15f, 0.05f, 1.15f, 0.00f, 0.98f, 0.50f, 1.0f, 1.0f, 0.95f, 1.10f, 1.25f);

        EditorUtility.SetDirty(propData);
        Recompile(mat, keywords);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        MicroSplatObject.SyncAll();
        Debug.Log("[MicroSplatSetup] Per-Texture configurado p/ 7 grounds. Triplanar: só rochas. " +
                  "Stochastic: só camadas planas. Ajustes finos: inspector do material > Per Texture Properties.");
    }

    /// <summary>
    /// Marca a linha como inicializada (bit em [linha,15] — protege contra o
    /// InitPropData do inspector) e preenche TODOS os 32 índices com o default.
    /// Os 7 grounds reais são sobrescritos na sequência por Ground().
    /// </summary>
    static void StampRow(MicroSplatPropData p, int row, Color def)
    {
        for (int i = 0; i < p.maxTextures; i++) p.SetValue(i, row, def);
        p.SetValue(row, 15, Color.white);
    }

    /// <summary>Escreve um ground no propData (linhas inteiras). Racional no chamador.</summary>
    static void Ground(MicroSplatPropData p, int i, string name, bool stochastic, bool triplanar,
        float normStr, float smoothAdd, float aoPow, float bright, float contrast,
        float porosity, float detailNoise, float distNoise,
        float sat, float hOffset, float hContrast)
    {
        // linha 2: normal ×, smoothness ADITIVO, AO como EXPOENTE, metallic absoluto
        p.SetValue(i, 2, new Color(normStr, smoothAdd, aoPow, 0f));
        // linha 3: brightness ADITIVO, contraste ×, porosidade (wetness da Fase 2), foam
        p.SetValue(i, 3, new Color(bright, contrast, porosity, 1f));
        // linha 4: multiplicadores dos noises anti-tile (detail, distance)
        p.SetValue(i, 4, new Color(detailNoise, distNoise, 1f, 1f));
        // linha 9: TriplanarMode (0 = triplanar pleno; >0.66 = projeção top-down
        //          barata), contraste do triplanar, stochastic on/off, saturação
        p.SetValue(i, 9, new Color(triplanar ? 0f : 1f, 1f, stochastic ? 1f : 0f, sat));
        // linha 10: cluster contraste/boost (neutro), height offset (v-1), height contraste
        p.SetValue(i, 10, new Color(1f, 1f, hOffset, hContrast));
    }

    static bool TryGetMaterial(out Material mat, out MicroSplatKeywords keywords)
    {
        var it = Object.FindFirstObjectByType<InfiniteTerrain>(FindObjectsInactive.Include);
        mat = it != null ? it.microSplatMaterial : null;
        keywords = it != null ? it.microSplatKeywords : null;
        if (mat == null || keywords == null)
            EditorUtility.DisplayDialog("MicroSplat",
                "InfiniteTerrain sem material/keywords MicroSplat — rode antes o menu " +
                "'MicroSplat — Converter Terreno'.", "OK");
        return mat != null && keywords != null;
    }

    static void AddKeywords(MicroSplatKeywords k, params string[] words)
    {
        foreach (var w in words)
            if (!k.keywords.Contains(w)) k.keywords.Add(w);
        EditorUtility.SetDirty(k);
    }

    static void Recompile(Material mat, MicroSplatKeywords keywords)
    {
        var comp = new MicroSplatShaderGUI.MicroSplatCompiler();
        comp.Compile(mat);
    }

    static void SetTex(Material mat, string prop, string assetPath)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (t != null) mat.SetTexture(prop, t);
        else Debug.LogWarning($"[MicroSplatSetup] Textura não encontrada: {assetPath}");
    }
}
