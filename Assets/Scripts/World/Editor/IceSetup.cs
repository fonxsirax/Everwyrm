using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SETUP DO GELO — Tools > Everwyrm > Gelo.
///
/// Gera o conjunto PBR do gelo (IceTextureGenerator) e monta os materiais HDRP
/// que o InfiniteTerrain usa nas placas de água congelada da Tundra.
///
/// POR QUE NÃO USAMOS O SHADER DO PACK In-ice
/// Os 8 "Ice Shader ... Lake" dele são Built-in RP: `#pragma surface surf
/// StandardCustom`, GrabPass, UnityPBSLighting.cginc. Nada disso existe no HDRP —
/// não compilam, e todo .mat do pack fica magenta. A lógica deles, porém, é boa
/// e foi REIMPLEMENTADA aqui com as peças nativas do HDRP, que fazem o mesmo
/// melhor e mais barato:
///
///   pack (Built-in)                     | aqui (HDRP)
///   ------------------------------------|--------------------------------------
///   GrabPass + _RefractionPower         | Refraction Box + IOR real (color
///                                       | pyramid, sem pass extra)
///   _WaterDepth/_ShalowColor/_Falloff   | Transmittance + ATDistance + Thickness
///   (profundidade FALSA por screen depth)| (absorção física sobre a água REAL)
///   _IceDepth + _BackGroundIceBlend     | duas camadas de fratura em alturas
///   (2ª amostra deslocada por paralaxe) | diferentes no height map -> o POM as
///                                       | desloca de verdade, por pixel
///   _IceNoiseTiling/_NoisePower         | DetailMap em escala não harmônica
///
/// As TEXTURAS do pack, essas sim, são usadas: `subsurface_cracked_ice` é a
/// única do conjunto com a polaridade de lago congelado de verdade (campo
/// escuro, veia branca fina) e alimenta o bake.
///
/// ARQUITETURA VISUAL (por que os materiais são assim):
///
///   ┌─ M_Ice_Lake ──────────────── HDRP/Lit TRANSPARENTE + REFRAÇÃO (Box) ─┐
///   │  A refração do HDRP lê a COLOR PYRAMID, que é gerada DEPOIS do       │
///   │  compositing da WaterSurface e ANTES dos transparentes refrativos    │
///   │  (HDRenderPipeline.RenderGraph: "after water lighting ... before     │
///   │  render refractive transparents"). Ou seja: a MESMA água dos lagos   │
///   │  continua sendo renderizada e aparece POR BAIXO do gelo, distorcida  │
///   │  e tingida pela absorção — a profundidade é de verdade, não pintada. │
///   │  O canal ALFA do BaseColor vira o transmittanceMask: janelas limpas  │
///   │  (alfa ~0,1) mostram a água; geada (alfa ~1) fica opaca.             │
///   └──────────────────────────────────────────────────────────────────────┘
///
///   M_Ice_Ground — a MESMA pele em versão OPACA, para gelo sobre terreno
///   (sem água por baixo para refratar, então refração só custaria).
///
/// POM (pixel displacement) dá a profundidade das trincas: elas foram gravadas
/// AFUNDANDO no height map, então o paralaxe as empurra para DENTRO do gelo.
/// Precisa de UV0 (o HDRP não aceita POM com mapeamento planar/triplanar) — por
/// isso o InfiniteTerrain assa a UV de mundo direto nos vértices da placa.
/// </summary>
public static class IceSetup
{
    const string Menu = "Tools/Everwyrm/Gelo/";
    const string Dir = "Assets/Everwyrm/Ice";
    const string TexDir = Dir + "/Textures";
    const string LakeMat = Dir + "/M_Ice_Lake.mat";
    const string GroundMat = Dir + "/M_Ice_Ground.mat";

    // ---- arte do pack In-ice (NatureManufacture Ice Lake).
    // Os SHADERS do pack são Built-in RP (surface shader + GrabPass) e não
    // renderizam no HDRP — todo .mat dele fica magenta e foi descartado. As
    // TEXTURAS são o que presta, e `subsurface_cracked_ice` é a única do
    // conjunto com a polaridade certa: campo escuro, veia branca fina.
    const string PackDir = "Assets/In-ice/Textures";
    const string PackVeins = PackDir + "/subsurface_cracked_ice_D_gray.tga";
    const string PackNormal = PackDir + "/subsurface_cracked_ice_N.tga";

    // ================================================================ MENUS
    [MenuItem(Menu + "1 - Gerar Texturas e Materiais")]
    public static void Generate() => Build(force: true);

    [MenuItem(Menu + "2 - Aplicar no Mundo da Cena")]
    public static void Apply()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(LakeMat);
        if (mat == null) { Build(force: false); mat = AssetDatabase.LoadAssetAtPath<Material>(LakeMat); }

        var it = Object.FindFirstObjectByType<InfiniteTerrain>(FindObjectsInactive.Include);
        if (it == null)
        {
            EditorUtility.DisplayDialog("Gelo", "Nenhum InfiniteTerrain na cena aberta.", "OK");
            return;
        }
        it.iceMaterial = mat;

        // o tile do mundo tem que bater com o que a textura foi assada para cobrir
        var so = new SerializedObject(it);
        var tileProp = so.FindProperty("iceTextureTile");
        if (tileProp != null && !Mathf.Approximately(tileProp.floatValue, IceTextureGenerator.WorldTile))
        {
            tileProp.floatValue = IceTextureGenerator.WorldTile;
            so.ApplyModifiedProperties();
            Debug.Log($"[IceSetup] iceTextureTile ajustado para {IceTextureGenerator.WorldTile} m.");
        }

        EditorUtility.SetDirty(it);
        Debug.Log($"<b>Gelo:</b> {mat.name} aplicado no InfiniteTerrain da cena.");
    }

    /// <summary>True quando os assets já existem (o auto-setup consulta).</summary>
    public static bool IsGenerated => AssetDatabase.LoadAssetAtPath<Material>(LakeMat) != null;

    /// <summary>Gera só o que falta — chamado pelo EverwyrmAutoSetup.</summary>
    public static void EnsureAssets()
    {
        if (!IsGenerated) Build(force: false);
    }

    // ================================================================ BUILD
    static void Build(bool force)
    {
        EnsureFolder(TexDir);

        bool haveTextures = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_Ice_BaseColor.png") != null;
        if (force || !haveTextures)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EditorUtility.DisplayProgressBar("Gelo", "Sintetizando texturas de gelo…", 0.1f);
            IceTextureGenerator.IceMaps maps;
            var pack = ReadPackVeins(2048);
            try { maps = IceTextureGenerator.Generate(2048, 1024, 71723, pack); }
            finally { EditorUtility.ClearProgressBar(); }

            int r = maps.res;
            SaveTexture("T_Ice_BaseColor", maps.baseColor, r, Kind.Color);
            SaveTexture("T_Ice_Normal", maps.normal, r, Kind.Normal);
            SaveTexture("T_Ice_Mask", maps.mask, r, Kind.Linear);
            SaveTexture("T_Ice_Height", maps.height, r, Kind.SingleChannel);
            SaveTexture("T_Ice_Thickness", maps.thickness, r, Kind.SingleChannel);
            SaveTexture("T_Ice_Detail", maps.detail, maps.detailRes, Kind.Linear);
            Debug.Log($"[IceSetup] Texturas de gelo geradas em {sw.ElapsedMilliseconds} ms ({r}²).");
        }

        var baseColor = Load("T_Ice_BaseColor");
        // normal AUTORAL do pack quando existe: ela foi desenhada para esta mesma
        // rede de fraturas, então casa pixel a pixel com o BaseColor/Height que
        // acabamos de assar. A derivada do height (fallback) é sempre mais mole.
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(PackNormal) ?? Load("T_Ice_Normal");
        var mask = Load("T_Ice_Mask");
        var height = Load("T_Ice_Height");
        var thickness = Load("T_Ice_Thickness");
        var detail = Load("T_Ice_Detail");

        MakeIceMaterial(LakeMat, transparent: true, baseColor, normal, mask, height, thickness, detail);
        MakeIceMaterial(GroundMat, transparent: false, baseColor, normal, mask, height, thickness, detail);

        AssetDatabase.SaveAssets();
        Debug.Log("<b>Gelo:</b> M_Ice_Lake (transparente + refração) e M_Ice_Ground (opaco) prontos em " + Dir);
    }

    static Texture2D Load(string name) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

    /// <summary>
    /// Lê a luminância da arte de fraturas do pack em `res`².
    ///
    /// As texturas do pack NÃO são Read/Write (e ligar isso mexeria no .meta de
    /// um asset vendorizado, além de dobrar a memória delas em runtime). O
    /// caminho é a GPU: Blit para uma RenderTexture sRGB — que também faz o
    /// downsample de 2048² com filtro — e ReadPixels de volta. RT sRGB porque a
    /// origem é sRGB: numa RT linear o Blit converteria e os bytes voltariam
    /// clareados, deslocando toda a curva de veias calibrada no gerador.
    /// </summary>
    static IceTextureGenerator.PackArt ReadPackVeins(int res)
    {
        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(PackVeins);
        if (src == null)
        {
            Debug.LogWarning($"[IceSetup] {PackVeins} não encontrado — caindo no " +
                             "craquelê procedural (resultado bem inferior).");
            return null;
        }

        var rt = RenderTexture.GetTemporary(res, res, 0, RenderTextureFormat.ARGB32,
                                            RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        var tmp = new Texture2D(res, res, TextureFormat.RGBA32, false, false);
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            tmp.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            tmp.Apply(false);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }

        var px = tmp.GetPixels32();
        Object.DestroyImmediate(tmp);

        var veins = new float[res * res];
        for (int i = 0; i < veins.Length; i++)
            veins[i] = (px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f) / 255f;

        Debug.Log($"[IceSetup] Arte de fraturas lida de {System.IO.Path.GetFileName(PackVeins)} ({res}²).");
        return new IceTextureGenerator.PackArt { res = res, veins = veins };
    }

    // ============================================================= MATERIAL
    static void MakeIceMaterial(string path, bool transparent, Texture2D baseColor,
                                Texture2D normal, Texture2D mask, Texture2D height,
                                Texture2D thickness, Texture2D detail)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("HDRP/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find("HDRP/Lit");

        // ---- mapas (UV0: a placa já traz a UV de mundo assada nos vértices,
        //      então tiling do material fica 1:1 e o POM continua válido)
        m.SetTexture("_BaseColorMap", baseColor);
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_NormalMap", normal);
        m.SetFloat("_NormalScale", 1f);            // a normal do pack já é discreta
        m.SetTexture("_MaskMap", mask);
        m.SetFloat("_SmoothnessRemapMin", 0f);
        m.SetFloat("_SmoothnessRemapMax", 1f);
        m.SetFloat("_AORemapMin", 0.60f);          // AO do gelo é sutil (nada de sujeira nos vincos)
        m.SetFloat("_AORemapMax", 1f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_UVBase", 0f);
        m.SetTextureScale("_BaseColorMap", Vector2.one);
        m.SetTextureOffset("_BaseColorMap", Vector2.zero);

        // ---- POM: é ele que faz o trabalho do _IceDepth do shader do pack. As
        //      duas camadas de fratura estão em alturas diferentes no height map,
        //      então o paralaxe desloca a funda ~3× mais que a da superfície e
        //      elas escorregam uma sobre a outra conforme a câmera vira.
        //      3 cm: alto o bastante para o efeito existir, baixo o bastante para
        //      não desmontar a silhueta nas bordas da placa.
        m.SetTexture("_HeightMap", height);
        m.SetFloat("_DisplacementMode", 2f);        // 2 = Pixel displacement (POM)
        m.SetFloat("_HeightPoMAmplitude", 3f);
        m.SetFloat("_PPDMinSamples", 6f);
        m.SetFloat("_PPDMaxSamples", 24f);          // camada funda pede mais passos
        m.SetFloat("_PPDLodThreshold", 4f);         // some com a distância (custo ~0 de longe)
        m.SetFloat("_PPDPrimitiveLength", 1f);
        m.SetFloat("_PPDPrimitiveWidth", 1f);
        m.SetFloat("_DisplacementLockObjectScale", 1f);
        m.SetFloat("_DisplacementLockTilingScale", 1f);

        // ---- detail: micro-craquelê numa escala NÃO harmônica com a base
        //      (×12.3 sobre um tile de 22 m ≈ 1,8 m) — a repetição da base fica
        //      muito mais difícil de ler
        m.SetTexture("_DetailMap", detail);
        m.SetFloat("_LinkDetailsWithBase", 0f);
        m.SetTextureScale("_DetailMap", new Vector2(12.3f, 12.3f));
        m.SetFloat("_DetailAlbedoScale", 0.20f);   // albedo de detalhe = grão claro em cima do gelo
        m.SetFloat("_DetailNormalScale", 0.7f);
        m.SetFloat("_DetailSmoothnessScale", 0.6f);
        m.SetFloat("_UVDetail", 0f);

        // ---- verniz: a lâmina de gelo tem uma camada lisa por cima do miolo
        //      fosco — é o clear coat que dá o brilho especular "molhado".
        //      Doses altas viram um véu de céu refletido por cima de TUDO, que
        //      lava a placa de branco e some com a água por baixo.
        m.SetFloat("_CoatMask", transparent ? 0.22f : 0.30f);
        m.SetFloat("_SpecularOcclusionMode", 1f);
        m.SetFloat("_EnableGeometricSpecularAA", 1f);   // placa enorme + normal fina = cintilação
        m.SetFloat("_SpecularAAScreenSpaceVariance", 0.12f);
        m.SetFloat("_SpecularAAThreshold", 0.22f);

        if (transparent)
        {
            m.SetFloat("_SurfaceType", 1f);             // Transparent
            m.SetFloat("_BlendMode", 0f);               // Alpha
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_TransparentZWrite", 0f);
            m.SetFloat("_EnableBlendModePreserveSpecularLighting", 1f);
            m.SetFloat("_EnableFogOnTransparent", 1f);
            m.SetFloat("_ReceivesSSRTransparent", 1f);  // reflexo de tela na pista de gelo
            m.SetFloat("_TransparentSortPriority", 0f);

            // REFRAÇÃO: é o que faz a água aparecer por baixo. Box (plane) usa a
            // espessura para calcular a absorção — daí o azul ficar mais fundo
            // nas partes grossas/leitosas e a janela limpa mostrar o leito.
            m.SetFloat("_RefractionModel", 1f);         // 1 = Planar/Box
            m.SetFloat("_Ior", 1.31f);                  // índice de refração REAL do gelo
            m.SetColor("_TransmittanceColor", new Color(0.66f, 0.84f, 0.90f));
            m.SetFloat("_ATDistance", 3.6f);            // absorção FRACA: a água tem que atravessar
            m.SetTexture("_ThicknessMap", thickness);
            m.SetFloat("_Thickness", 1f);
            m.SetVector("_ThicknessRemap", new Vector4(0.01f, 0.32f, 0f, 0f));
            m.renderQueue = -1;                         // fila default de transparentes
        }
        else
        {
            m.SetFloat("_SurfaceType", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_RefractionModel", 0f);
            m.SetFloat("_ReceivesSSR", 1f);
            m.renderQueue = -1;
        }

        m.SetFloat("_DoubleSidedEnable", 0f);
        ValidateHdrp(m);
        EditorUtility.SetDirty(m);
    }

    /// <summary>HDMaterial.ValidateMaterial via reflexão (recalcula keywords e
    /// passes do HDRP) — mesmo helper do DesertSetup/EverwyrmAutoSetup. Sem ele
    /// o material fica sem _REFRACTION_PLANE/_MASKMAP/_PIXEL_DISPLACEMENT e
    /// renderiza como um Lit chapado.</summary>
    static void ValidateHdrp(Material m)
    {
        var t = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime")
             ?? System.Type.GetType("UnityEditor.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Editor");
        var method = t?.GetMethod("ValidateMaterial", new[] { typeof(Material) });
        if (method == null)
        {
            Debug.LogWarning("[IceSetup] HDMaterial.ValidateMaterial não encontrado — abra o material " +
                             "no Inspector uma vez para o HDRP recalcular as keywords.");
            return;
        }
        method.Invoke(null, new object[] { m });
    }

    // ============================================================== TEXTURAS
    enum Kind { Color, Linear, Normal, SingleChannel }

    static void SaveTexture(string name, Color32[] pixels, int res, Kind kind)
    {
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply(false);
        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) return;

        imp.wrapMode = TextureWrapMode.Repeat;      // a placa cobre o lago inteiro
        imp.filterMode = FilterMode.Trilinear;
        imp.mipmapEnabled = true;
        imp.anisoLevel = 8;                         // rasante extremo (dragão no chão)
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.maxTextureSize = 2048;

        switch (kind)
        {
            case Kind.Color:
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = false;    // é OPACIDADE (dado), não recorte
                break;
            case Kind.Linear:
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                break;
            case Kind.Normal:
                imp.textureType = TextureImporterType.NormalMap;
                imp.sRGBTexture = false;
                break;
            case Kind.SingleChannel:
                // height/thickness: 1 canal (BC4) em vez de RGBA — o HDRP lê .r,
                // e o default do importer é ALPHA (viria tudo 1 e o POM sumiria)
                imp.textureType = TextureImporterType.SingleChannel;
                imp.sRGBTexture = false;
                // singleChannelComponent só é exposto via TextureImporterSettings
                var st = new TextureImporterSettings();
                imp.ReadTextureSettings(st);
                st.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                imp.SetTextureSettings(st);
                break;
        }
        imp.SaveAndReimport();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
