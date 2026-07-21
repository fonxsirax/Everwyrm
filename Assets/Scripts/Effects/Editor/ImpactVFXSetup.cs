using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Setup dos VFX de impacto — Tools > Everwyrm > VFX de Impacto.
///
///  1. Gerar Texturas e Materiais: escreve as texturas procedurais (folha,
///     lasca, anel de ondulação) em Assets/Everwyrm/VFX/Textures e monta os
///     materiais transparentes em Resources/ImpactVFX clonando a receita do
///     FX_Snow do Winter pack (HDRP/Unlit transparente comprovado no projeto).
///     Puffs de nuvem e gotícula reusam as texturas do próprio pack.
///  2. Criar Cena de Debug: Assets/Scenes/VFXDebug.unity com mundo procedural,
///     dragão, câmera, sol + céu PBS e o VFX Tester.
///  3. Testar ...: em Play, dispara cada efeito repetidamente num palco à
///     frente do dragão (ImpactVFXTester).
/// </summary>
public static class ImpactVFXSetup
{
    const string Menu = "Tools/Everwyrm/VFX de Impacto/";
    const string TexDir = "Assets/Everwyrm/VFX/Textures";
    const string MatDir = "Assets/Everwyrm/VFX/Resources/ImpactVFX";
    const string ScenePath = "Assets/Scenes/VFXDebug.unity";
    const string SkyProfilePath = "Assets/Scenes/VFXDebug_Sky.asset";
    const string DragonPrefabPath = "Assets/Prefabs/Unka Realistic.prefab";

    const string PackTexDir =
        "Assets/ANGRY MESH/Nature Pack - Winter Environment/Sources/Textures/";
    const string PackSnowMat =
        "Assets/ANGRY MESH/Nature Pack - Winter Environment/Sources/Materials/FX_Snow_A_01.mat";

    // ================================================= 1. TEXTURAS E MATERIAIS
    [MenuItem(Menu + "1 - Gerar Texturas e Materiais")]
    public static void GenerateAssets()
    {
        EnsureFolder(TexDir);
        EnsureFolder(MatDir);

        // ---- texturas procedurais (folha, lasca, anel — o pack não as tem)
        var leafTex = SaveTexture("ImpactLeafAtlas", GenLeafAtlas(256));
        var chipTex = SaveTexture("ImpactChip", GenChip(128));
        var ringTex = SaveTexture("ImpactRipple", GenRing(256));

        // ---- texturas do Winter pack (arte de artista, já no projeto)
        var cloudA = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTexDir + "FX_Cloud_A_01.tif");
        var cloudB = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTexDir + "FX_Cloud_B_01.tif");
        var snow = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTexDir + "FX_Snow_A_01.tif");
        if (cloudA == null || snow == null)
            Debug.LogWarning("Texturas FX do Winter pack não encontradas — os materiais " +
                             "de nuvem/gotícula ficarão sem textura (quadrado macio).");

        // ---- folhas do CartoonFX (Sprites/Textures) — grayscale p/ tingir de verde
        var leafA = LoadTex("Assets/Sprites/Textures/cfxr leave a.png");
        var leafB = LoadTex("Assets/Sprites/Textures/cfxr leave b.png");

        // ---- biblioteca CartoonFX (auditoria dos pacotes): flipbooks tingíveis.
        //      Faltando (biblioteca não importada), cai nas texturas do Winter pack.
        const string Lib = "Assets/Everwyrm/VFX/Library/CartoonFX/";
        var smoke = LoadTex(Lib + "cfxr smoke cloud x4 white.png");      // 2×2 nuvens
        var drop = LoadTex(Lib + "cfxr water drop blur anim.png");       // 1×3 gotas
        var splash = LoadTex(Lib + "cfxr water small splash stretch.png"); // 2×2 jatos
        var debris = LoadTex(Lib + "cfxr debris flat unlit 3x3.png");    // 3×3 cacos

        // ---- materiais transparentes (um por papel)
        MakeMaterial("PuffSoft", smoke != null ? smoke : cloudA);
        MakeMaterial("PuffDense", smoke != null ? smoke
                                : cloudB != null ? cloudB : cloudA);
        MakeMaterial("Droplet", drop != null ? drop : snow);
        if (splash != null) MakeMaterial("SplashStreak", splash);
        MakeMaterial("Leaf", leafTex);       // atlas procedural: fallback
        if (leafA != null) MakeMaterial("LeafA", leafA);
        if (leafB != null) MakeMaterial("LeafB", leafB);
        MakeMaterial("Chip", debris != null ? debris : chipTex);
        MakeMaterial("Ripple", ringTex);     // o "ring ripple" do CFXR é rampa de shader — o nosso anel é melhor aqui

        AssetDatabase.SaveAssets();
        Debug.Log("<b>VFX de Impacto:</b> texturas e materiais gerados em " +
                  $"{TexDir} e {MatDir}. Os efeitos usam tudo automaticamente.");
    }

    /// <summary>Material transparente para partícula: clona a receita comprovada
    /// do FX_Snow do pack; sem ela, monta o HDRP/Unlit transparente na mão.</summary>
    static void MakeMaterial(string name, Texture2D tex)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (m == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(PackSnowMat);
            m = template != null ? new Material(template) : ManualTransparent();
            AssetDatabase.CreateAsset(m, path);
        }

        if (m.HasProperty("_UnlitColorMap")) m.SetTexture("_UnlitColorMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", Color.white);
        EditorUtility.SetDirty(m);
    }

    /// <summary>Carrega uma textura garantindo importação com alfa correto.</summary>
    static Texture2D LoadTex(string path)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null)
        {
            Debug.LogWarning($"Textura não encontrada: {path} — usando fallback.");
            return null;
        }
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp != null && !imp.alphaIsTransparency)
        {
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        return tex;
    }

    static Material ManualTransparent()
    {
        var m = new Material(Shader.Find("HDRP/Unlit"));
        m.SetFloat("_SurfaceType", 1f);
        m.SetFloat("_BlendMode", 0f);
        m.SetFloat("_SrcBlend", 1f);
        m.SetFloat("_DstBlend", 10f);
        m.SetFloat("_AlphaSrcBlend", 1f);
        m.SetFloat("_AlphaDstBlend", 10f);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = 3000;
        return m;
    }

    // ------------------------------------------------------------ GERADORES
    /// <summary>Atlas 2×2 de folhas: elipse com ponta, nervura central e leve
    /// gradiente — 4 variações de forma/tom para o rodopio não parecer clonado.</summary>
    static Color[] GenLeafAtlas(int size)
    {
        var px = new Color[size * size];
        int half = size / 2;
        // (curvatura, meia-largura, tom)
        (float bend, float width, Color tint)[] variants =
        {
            (-0.22f, 0.42f, new Color(0.30f, 0.47f, 0.16f)),
            ( 0.15f, 0.50f, new Color(0.36f, 0.52f, 0.20f)),
            (-0.08f, 0.35f, new Color(0.25f, 0.40f, 0.14f)),
            ( 0.28f, 0.46f, new Color(0.42f, 0.50f, 0.18f)),
        };

        for (int t = 0; t < 4; t++)
        {
            var (bend, width, tint) = variants[t];
            int ox = (t % 2) * half, oy = (t / 2) * half;

            for (int y = 0; y < half; y++)
                for (int x = 0; x < half; x++)
                {
                    float u = (x + 0.5f) / half * 2f - 1f;
                    float v = (y + 0.5f) / half * 2f - 1f;

                    float t01 = Mathf.InverseLerp(-0.85f, 0.85f, v);
                    float axis = Mathf.Clamp01(t01);
                    // perfil: base estreita → bojo → ponta fina
                    float halfW = width * Mathf.Sin(Mathf.PI * Mathf.Pow(1f - axis, 0.8f));
                    float uc = u - bend * (axis * axis - 0.3f);   // curvatura da lâmina
                    float d = Mathf.Abs(uc) - halfW;
                    float alpha = (v < -0.85f || v > 0.85f) ? 0f
                                : Mathf.Clamp01(-d / 0.06f);
                    if (alpha <= 0f) { px[(oy + y) * size + ox + x] = Color.clear; continue; }

                    Color c = tint * Mathf.Lerp(0.75f, 1.1f, axis);       // base escura
                    if (Mathf.Abs(uc) < 0.035f) c *= 0.8f;                // nervura
                    c.a = alpha;
                    px[(oy + y) * size + ox + x] = c;
                }
        }
        return px;
    }

    /// <summary>Lasca: polígono irregular (raio modulado por senos) com
    /// sombreamento lateral — serve de pedra, torrão e galho.</summary>
    static Color[] GenChip(int size)
    {
        var px = new Color[size * size];
        var grey = new Color(0.62f, 0.60f, 0.58f);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);

                float edge = 0.55f + 0.22f * Mathf.Sin(3f * ang + 1.7f)
                                   + 0.12f * Mathf.Sin(7f * ang + 0.4f);
                float alpha = Mathf.Clamp01((edge - r) / 0.05f);
                if (alpha <= 0f) { px[y * size + x] = Color.clear; continue; }

                float shade = 0.75f + 0.25f * Mathf.Cos(ang - 0.8f);   // luz do alto-esquerdo
                Color c = grey * shade;
                c.a = alpha;
                px[y * size + x] = c;
            }
        return px;
    }

    /// <summary>Anel de ondulação: faixa gaussiana com borda macia.</summary>
    static Color[] GenRing(int size)
    {
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);

                float g = (r - 0.62f) / 0.09f;
                float alpha = Mathf.Exp(-g * g) * 0.85f;
                px[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        return px;
    }

    static Texture2D SaveTexture(string name, Color[] pixels)
    {
        int size = Mathf.RoundToInt(Mathf.Sqrt(pixels.Length));
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();

        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ======================================================= 2. CENA DE DEBUG
    [MenuItem(Menu + "2 - Criar Cena de Debug")]
    public static void CreateDebugScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- dragão (prefab real do jogo)
        var dragonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DragonPrefabPath);
        if (dragonPrefab == null)
        {
            Debug.LogError($"Prefab do dragão não encontrado em {DragonPrefabPath} — " +
                           "rode Tools > Dragão > Setup Completo primeiro.");
            return;
        }
        var dragon = (GameObject)PrefabUtility.InstantiatePrefab(dragonPrefab);
        dragon.transform.position = new Vector3(0f, 14f, 0f);   // o mundo o crava no chão

        // ---- mundo procedural (mesmo componente da Main — gera a floresta em Play)
        var world = new GameObject("World (Infinite Terrain)").AddComponent<InfiniteTerrain>();
        world.player = dragon.transform;
        // auto-preenche os assets ALP: chamada direta (SendMessage em modo de
        // edição dispara o assert 'ShouldRunBehaviour' num objeto recém-criado)
        typeof(InfiniteTerrain)
            .GetMethod("OnValidate", System.Reflection.BindingFlags.Instance |
                                     System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(world, null);

        // ---- câmera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.farClipPlane = 4000f;
        camGo.AddComponent<HDAdditionalCameraData>();
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = dragon.transform.position + new Vector3(0f, 6f, -12f);
        var dc = camGo.AddComponent<DragonCamera>();
        dc.target = dragon.transform;

        // ---- sol + céu físico (sem o DayNightCycle: aqui a luz é fixa e clara)
        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sunGo.AddComponent<HDAdditionalLightData>();
        sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, SkyProfilePath);
        var env = profile.Add<VisualEnvironment>(true);
        env.skyType.Override((int)SkyType.PhysicallyBased);
        profile.Add<PhysicallyBasedSky>(true);
        var exposure = profile.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Automatic);

        var volumeGo = new GameObject("Sky Volume");
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;

        // ---- bancada de VFX (os menus "Testar" falam com ela em Play)
        new GameObject("VFX Tester").AddComponent<ImpactVFXTester>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"<b>Cena de debug criada:</b> {ScenePath}. Dê Play e use " +
                  "Tools > Everwyrm > VFX de Impacto > Testar para disparar os efeitos.");
    }

    // ============================================================ 3. TESTES
    [MenuItem(Menu + "Testar - Poeira (pouso)")]
    static void TestDust() => Fire(ImpactVFXTester.TestMode.Poeira);

    [MenuItem(Menu + "Testar - Queda forte (poeira + torrões)")]
    static void TestHard() => Fire(ImpactVFXTester.TestMode.QuedaForte);

    [MenuItem(Menu + "Testar - Folhas (árvore)")]
    static void TestLeaves() => Fire(ImpactVFXTester.TestMode.Folhas);

    [MenuItem(Menu + "Testar - Lascas (rocha)")]
    static void TestChips() => Fire(ImpactVFXTester.TestMode.Lascas);

    [MenuItem(Menu + "Testar - Splash (entrar na água)")]
    static void TestSplash() => Fire(ImpactVFXTester.TestMode.Splash);

    [MenuItem(Menu + "Testar - Spray (raspar a água)")]
    static void TestSpray() => Fire(ImpactVFXTester.TestMode.Spray);

    [MenuItem(Menu + "Testar - TUDO (ciclar)")]
    static void TestAll() => Fire(ImpactVFXTester.TestMode.Tudo);

    [MenuItem(Menu + "Testar - Parar")]
    static void TestStop() => Fire(ImpactVFXTester.TestMode.Nada);

    static void Fire(ImpactVFXTester.TestMode mode)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Entre em Play (de preferência na cena VFXDebug) e chame o teste de novo.");
            return;
        }
        ImpactVFXTester.Set(mode);
    }
}
