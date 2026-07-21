using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Setup dos VFX de impacto — Tools > Everwyrm > VFX de Impacto.
///
///  1. Gerar Texturas e Materiais: escreve as texturas em
///     Assets/Everwyrm/VFX/Textures e monta os materiais transparentes em
///     Resources/ImpactVFX clonando a receita do FX_Snow do Winter pack
///     (HDRP/Unlit transparente comprovado no projeto). Visual REALISTA:
///     poeira derivada do FogParticle01 (Inguz); gotas, splash, cacos e
///     sombreamento de folha gerados com ruído — sem silhueta cartoon do CFXR.
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

        // ---- texturas do Winter pack (fallback se o fog do Inguz sumir)
        var cloudA = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTexDir + "FX_Cloud_A_01.tif");
        var cloudB = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTexDir + "FX_Cloud_B_01.tif");
        var snow = AssetDatabase.LoadAssetAtPath<Texture2D>(PackTexDir + "FX_Snow_A_01.tif");

        // ---- REALISTAS p/ HDRP: as silhuetas do CFXR são cartoon (nuvem de
        //      borda dura, gota desenhada, caco chapado) e destoavam do mundo.
        //      A poeira nasce do FogParticle01 do Inguz (fumaça volumétrica de
        //      artista, alfa real); gotas/splash/cacos são gerados com ruído.
        //      Só o glow do CombatFX ainda vem de bake (radial, não-cartoon).
        var smokePx = GenSmokeAtlas("Assets/Everwyrm/VFX/Library/Inguz/FogParticle01.psd", 256);
        var smoke = smokePx != null ? SaveTexture("BakedSmoke", smokePx, 512, 512) : null;  // 2×2
        var drop = SaveTexture("BakedDrop", GenDropAtlas(), 512, 768);                      // 1×3
        var splash = SaveTexture("BakedSplash", GenSplashAtlas(), 512, 512);                // 2×2
        var debris = SaveTexture("BakedDebris", GenDebrisAtlas(340), 1020, 1020);           // 3×3
        var glow = BakeParticleTex(
            "Assets/Everwyrm/VFX/Library/CartoonFX Legacy/CFX2_T_GlowSoft.png", "BakedGlow");

        // ---- folhas: silhueta do CFXR (boa) + sombreamento/mosqueado bakeados
        //      e alfa apertado (sem o halo cartoon); grayscale p/ tingir de verde
        var leafA = ShadeLeafTex("Assets/Sprites/Textures/cfxr leave a.png", "BakedLeafA", 11);
        var leafB = ShadeLeafTex("Assets/Sprites/Textures/cfxr leave b.png", "BakedLeafB", 22);

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
        if (glow != null) MakeMaterial("GlowSoft", glow);   // brilho macio do CombatVFX (fogo, projéteis)

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

    // ------------------------------------------ GERADORES REALISTAS (HDRP)
    /// <summary>Value noise fractal 0..1 — o grão orgânico de tudo abaixo.</summary>
    static float[] FBm(int w, int h, int octaves, int baseRes, int seed)
    {
        var acc = new float[w * h];
        float amp = 1f, total = 0f;
        for (int o = 0; o < octaves; o++)
        {
            int g = baseRes << o;
            var rnd = new System.Random(seed * 31 + o);
            var grid = new float[(g + 1) * (g + 1)];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rnd.NextDouble();

            for (int y = 0; y < h; y++)
            {
                float gy = (float)y / (h - 1) * g;
                int y0 = Mathf.Min((int)gy, g - 1);
                float fy = gy - y0;
                for (int x = 0; x < w; x++)
                {
                    float gx = (float)x / (w - 1) * g;
                    int x0 = Mathf.Min((int)gx, g - 1);
                    float fx = gx - x0;
                    float top = Mathf.Lerp(grid[y0 * (g + 1) + x0], grid[y0 * (g + 1) + x0 + 1], fx);
                    float bot = Mathf.Lerp(grid[(y0 + 1) * (g + 1) + x0], grid[(y0 + 1) * (g + 1) + x0 + 1], fx);
                    acc[y * w + x] += amp * Mathf.Lerp(top, bot, fy);
                }
            }
            total += amp;
            amp *= 0.55f;
        }
        for (int i = 0; i < acc.Length; i++) acc[i] /= total;
        return acc;
    }

    /// <summary>Atlas 2×2 de poeira: 4 variações (rotação/espelho + mosqueado)
    /// do FogParticle01 do Inguz — fumaça volumétrica de artista, nada cartoon.</summary>
    static Color[] GenSmokeAtlas(string fogPath, int cell)
    {
        var imp = AssetImporter.GetAtPath(fogPath) as TextureImporter;
        if (imp == null)
        {
            Debug.LogWarning($"Textura não encontrada: {fogPath} — poeira cai no Winter pack.");
            return null;
        }
        bool wasReadable = imp.isReadable;
        if (!wasReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
        var fog = AssetDatabase.LoadAssetAtPath<Texture2D>(fogPath);

        int size = cell * 2;
        var px = new Color[size * size];
        for (int q = 0; q < 4; q++)
        {
            float ang = q * Mathf.PI / 2f;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            bool flip = (q % 2) == 1;
            var noise = FBm(cell, cell, 3, 6, 40 + q);
            int ox = (q % 2) * cell, oy = (q / 2) * cell;

            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = (x + 0.5f) / cell - 0.5f;
                    float v = (y + 0.5f) / cell - 0.5f;
                    if (flip) u = -u;
                    var c = fog.GetPixelBilinear(ca * u - sa * v + 0.5f, sa * u + ca * v + 0.5f);
                    c.a *= (0.82f + 0.18f * noise[y * cell + x]) * 0.55f;  // translúcida
                    px[(oy + y) * size + ox + x] = c;
                }
        }
        if (!wasReadable) { imp.isReadable = false; imp.SaveAndReimport(); }
        return px;
    }

    /// <summary>Gota 1×3: brilho macio esticado — cada quadro com mais motion
    /// blur (o flipbook sorteia; de perto lê como respingo real, não desenho).</summary>
    static Color[] GenDropAtlas()
    {
        const int w = 512, fh = 256;
        var px = new Color[w * fh * 3];
        for (int f = 0; f < 3; f++)
        {
            float stretch = 0.22f + 0.3f * f;
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f;
                    float v = (y + 0.5f) / fh * 2f - 1f;
                    float d2 = (u / 0.16f) * (u / 0.16f) + (v / stretch) * (v / stretch);
                    float a = Mathf.Clamp01(Mathf.Exp(-d2 * 1.6f) + Mathf.Exp(-d2 * 0.35f) * 0.35f)
                              * (0.95f - 0.15f * f);
                    float hi = Mathf.Exp(-Mathf.Pow((u + 0.05f) / 0.1f, 2f)
                                         - Mathf.Pow((v + 0.08f) / 0.2f, 2f));
                    float g = Mathf.Clamp01(0.85f + 0.15f * hi);
                    px[(f * fh + y) * w + x] = new Color(g, g, g, a);
                }
        }
        return px;
    }

    /// <summary>Splash 2×2: leque de filetes d'água nascendo da base, quebrados
    /// por ruído — substitui os jatos estilizados do CFXR.</summary>
    static Color[] GenSplashAtlas()
    {
        const int cell = 256, size = 512;
        var px = new Color[size * size];
        for (int q = 0; q < 4; q++)
        {
            var rnd = new System.Random(90 + q);
            var breakup = FBm(cell, cell, 4, 10, 200 + q);
            int n = 6 + rnd.Next(4);
            var jet = new (float ang, float len, float wid, float amp)[n];
            for (int k = 0; k < n; k++)
                jet[k] = (((float)k / (n - 1) - 0.5f) * 1.15f + ((float)rnd.NextDouble() - 0.5f) * 0.16f,
                          0.55f + (float)rnd.NextDouble() * 0.43f,
                          0.018f + (float)rnd.NextDouble() * 0.032f,
                          0.7f + (float)rnd.NextDouble() * 0.3f);

            int ox = (q % 2) * cell, oy = (q / 2) * cell;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = (x + 0.5f) / cell * 2f - 1f;
                    float v = (y + 0.5f) / cell * 2f - 1f;   // -1 = base do leque
                    float t = (v + 1f) / 2f;                 // 0 base → 1 topo
                    float a = 0f, core = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        float xu = u - Mathf.Max(v, 0f) * Mathf.Tan(jet[k].ang) * 0.9f;
                        float prof = Mathf.Exp(-(xu / jet[k].wid) * (xu / jet[k].wid));
                        float fade = Mathf.Pow(Mathf.Clamp01(1f - t / jet[k].len), 0.7f);
                        float s = v > -0.9f ? prof * fade : 0f;
                        a = Mathf.Max(a, s * jet[k].amp);
                        core = Mathf.Max(core, s);
                    }
                    a = Mathf.Clamp01(a * (0.55f + 0.45f * breakup[y * cell + x]) * 1.15f) * 0.9f;
                    float g = 0.75f + 0.25f * core;
                    px[(oy + y) * size + ox + x] = new Color(g, g, g, a);
                }
        }
        return px;
    }

    /// <summary>Detritos 3×3: cacos irregulares com luz direcional, mosqueado e
    /// leve tom terroso — silhueta dura (pedra é dura), interior com volume.</summary>
    static Color[] GenDebrisAtlas(int cell)
    {
        int size = cell * 3;
        var px = new Color[size * size];
        for (int q = 0; q < 9; q++)
        {
            var rnd = new System.Random(300 + q);
            var mott = FBm(cell, cell, 4, 12, 500 + q);
            var hAmp = new float[8];
            var hPh = new float[8];
            for (int k = 3; k < 8; k++)
            {
                hAmp[k] = 0.015f + (float)rnd.NextDouble() * 0.045f;
                hPh[k] = (float)rnd.NextDouble() * 6.28f;
            }
            float light = (float)rnd.NextDouble() * 6.28f;
            float tintG = 0.93f + (float)rnd.NextDouble() * 0.07f;
            float tintB = 0.85f + (float)rnd.NextDouble() * 0.12f;

            int ox = (q % 3) * cell, oy = (q / 3) * cell;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = (x + 0.5f) / cell * 2f - 1f;
                    float v = (y + 0.5f) / cell * 2f - 1f;
                    float rad = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);

                    float edge = 0.55f;
                    for (int k = 3; k < 8; k++) edge += hAmp[k] * Mathf.Sin(k * ang + hPh[k]);
                    edge = Mathf.Clamp(edge, 0.3f, 0.85f);

                    float a = Mathf.Clamp01((edge - rad) / 0.02f);
                    if (a <= 0f) { px[(oy + y) * size + ox + x] = Color.clear; continue; }

                    float shade = 0.78f + 0.2f * Mathf.Cos(ang - light);
                    float rim = Mathf.Clamp01((edge - rad) / (edge * 0.55f)) * 0.2f + 0.8f;
                    float g = Mathf.Clamp01(shade * mott[y * cell + x] * rim);
                    px[(oy + y) * size + ox + x] = new Color(g, g * tintG, g * tintB, a);
                }
        }
        return px;
    }

    /// <summary>Folha: mantém a silhueta/nervuras do CFXR mas baka gradiente +
    /// mosqueado no RGB e aperta o alfa (tira o halo cartoon da borda).</summary>
    static Texture2D ShadeLeafTex(string srcPath, string dstName, int seed)
    {
        var imp = AssetImporter.GetAtPath(srcPath) as TextureImporter;
        if (imp == null)
        {
            Debug.LogWarning($"Textura não encontrada: {srcPath} — usando fallback.");
            return null;
        }
        bool wasReadable = imp.isReadable;
        if (!wasReadable || imp.textureType != TextureImporterType.Default)
        {
            imp.isReadable = true;
            imp.textureType = TextureImporterType.Default;
            imp.SaveAndReimport();
        }
        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
        var px = src.GetPixels();
        int w = src.width, h = src.height;
        var mott = FBm(w, h, 4, 6, seed);

        for (int y = 0; y < h; y++)
        {
            float grad = 0.78f + 0.27f * ((float)y / (h - 1));   // ponta mais clara
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float m = grad * (0.66f + 0.34f * mott[i]);
                var c = px[i];
                px[i] = new Color(c.r * m, c.g * m, c.b * m,
                                  Mathf.Clamp01((c.a - 0.35f) / 0.3f));
            }
        }
        if (!wasReadable) { imp.isReadable = false; imp.SaveAndReimport(); }
        return SaveTexture(dstName, px, w, h);
    }

    /// <summary>
    /// Converte uma máscara do CFXR em PNG RGBA utilizável pelo HDRP/Unlit:
    /// se a textura não tem alfa de verdade, o alfa vira a luminância (a forma)
    /// e o RGB vira branco (a cor vem do tinte do material). Com alfa real,
    /// só copia. Sempre reimporta como Default (o CFXR usa tipos especiais).
    /// </summary>
    static Texture2D BakeParticleTex(string srcPath, string dstName)
    {
        var imp = AssetImporter.GetAtPath(srcPath) as TextureImporter;
        if (imp == null)
        {
            Debug.LogWarning($"Textura não encontrada: {srcPath} — usando fallback.");
            return null;
        }

        bool wasReadable = imp.isReadable;
        if (!wasReadable || imp.textureType != TextureImporterType.Default)
        {
            imp.isReadable = true;
            imp.textureType = TextureImporterType.Default;   // sai do modo máscara do CFXR
            imp.SaveAndReimport();
        }

        var src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
        var px = src.GetPixels();

        bool hasRealAlpha = false;
        foreach (var c in px)
            if (c.a < 0.95f) { hasRealAlpha = true; break; }

        if (!hasRealAlpha)
            for (int i = 0; i < px.Length; i++)
            {
                float a = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                px[i] = new Color(1f, 1f, 1f, a);
            }

        int w = src.width, h = src.height;
        if (!wasReadable) { imp.isReadable = false; imp.SaveAndReimport(); }

        return SaveTexture(dstName, px, w, h);
    }

    static Texture2D SaveTexture(string name, Color[] pixels, int width = 0, int height = 0)
    {
        int w = width > 0 ? width : Mathf.RoundToInt(Mathf.Sqrt(pixels.Length));
        int h = height > 0 ? height : w;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
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
