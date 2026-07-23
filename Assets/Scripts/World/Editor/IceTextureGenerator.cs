using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// SÍNTESE PROCEDURAL DAS TEXTURAS DE GELO (editor-only, roda uma vez e vira PNG).
///
/// O gelo do projeto não tinha textura NENHUMA — era um HDRP/Lit de cor sólida.
/// Aqui nasce o conjunto PBR completo, desenhado para gelo de lago congelado real:
///
///   BaseColor(RGB) + OPACIDADE(A) · Normal · MaskMap(AO/detail/smoothness)
///   · Height (para POM) · Thickness (absorção da refração) · DetailMap (micro)
///
/// CAMADAS FÍSICAS empilhadas (a ordem importa — cada uma escreve nos mapas que
/// fazem sentido para ela):
///
///  1. REDE DE FRATURAS em DUAS PROFUNDIDADES. A arte vem do pack In-ice
///     (subsurface_cracked_ice_D) quando ele existe: a mesma imagem entra uma
///     vez como veia de SUPERFÍCIE (curva apertada, branca, opaca) e uma vez
///     girada 90° como fratura FUNDA (curva larga, azulada, translúcida). As
///     duas ficam em alturas diferentes no height map, e é o POM do HDRP que
///     as separa em tempo real. Sem o pack, cai numa rede Voronoi procedural
///     em 3 escalas — que funciona, mas lê como craquelê de cerâmica.
///  2. ESTRELAS DE IMPACTO e FISSURAS TÉRMICAS longas, rasterizadas como
///     polilinhas — SÓ no caminho procedural (a arte do pack já tem o desenho
///     radial que o Voronoi sozinho não produz).
///  3. BOLHAS DE AR em nuvens: esféricas e COLUNARES (as que se formam quando o
///     congelamento é rápido), densidade modulada por manchas.
///  4. GEADA/NEVE SOPRADA: manchas opacas e ásperas — é o que dá "variação de
///     opacidade" e "níveis de polimento diferentes" pedidos.
///  5. ESTRIAS DE VENTO: riscos direcionais finos, quase só na smoothness.
///
/// TILEABILIDADE: todo ruído é PERIÓDICO (Perlin/Worley com wrap inteiro) e as
/// polilinhas são rasterizadas com wrap — a textura fecha nas 4 bordas. A
/// repetição visível é morta no MESH (o InfiniteTerrain distorce o domínio da UV
/// com um warp de baixa frequência em coordenadas de mundo).
///
/// Custo: ~4,2 M pixels × 6 mapas. Roda em Parallel.For (só matemática, nada de
/// API da Unity dentro do laço) — poucos segundos no editor.
/// </summary>
public static class IceTextureGenerator
{
    // ------------------------------------------------------------ PARÂMETROS
    /// <summary>Metros de mundo cobertos por UMA repetição da textura.
    /// Precisa bater com o `iceTextureTile` do InfiniteTerrain.
    /// 22 m: as células da rede de fratura do pack (~10 por textura) caem em
    /// ~2 m cada, que é a escala das placas na foto de referência. Em 12 m a
    /// mesma arte virava um craquelê miúdo de 1 m — lê como cerâmica.</summary>
    public const float WorldTile = 22f;

    /// <summary>Resultado de uma geração — arrays prontos para virar PNG.</summary>
    public class IceMaps
    {
        public int res;
        public Color32[] baseColor;   // RGB tinta do gelo · A OPACIDADE (1-A = refração)
        public Color32[] normal;      // normal tangent-space (RGB)
        public Color32[] mask;        // R metallic · G AO · B detail mask · A smoothness
        public Color32[] height;      // altura p/ POM (R)
        public Color32[] thickness;   // espessura p/ absorção da refração (R)
        public int detailRes;
        public Color32[] detail;      // HDRP detail: R albedo · G normalY · B smooth · A normalX
    }

    /// <summary>
    /// Arte de fraturas vinda de fora (o pack In-ice) substituindo a rede
    /// procedural. `veins` é a LUMINÂNCIA de subsurface_cracked_ice_D: escuro =
    /// gelo limpo, claro = plano de fratura que espalha luz.
    /// </summary>
    public class PackArt
    {
        public int res;
        public float[] veins;
    }

    public static IceMaps Generate(int res = 2048, int detailRes = 1024, int seed = 71723,
                                   PackArt pack = null)
    {
        var m = new IceMaps { res = res, detailRes = detailRes };
        float[] veins = (pack != null && pack.res == res) ? pack.veins : null;

        // ---- 1. features desenhadas (estrelas + fissuras) e bolhas, rasterizadas.
        //      Com a arte do pack a rede desenhada não entra: ela existia para
        //      dar à Voronoi o desenho radial que o Voronoi sozinho não produz —
        //      a arte já tem isso, e somar as duas vira sujeira.
        float[] drawnCracks = null;
        if (veins == null)
        {
            drawnCracks = new float[res * res];
            RasterizeImpactStars(drawnCracks, res, seed);
            RasterizeThermalFissures(drawnCracks, res, seed + 991);
        }

        var bubbles = new float[res * res];
        RasterizeBubbles(bubbles, res, seed + 4409);

        // ---- 2. campos por pixel + mapas de cor/máscara/altura
        var height = new float[res * res];
        m.baseColor = new Color32[res * res];
        m.mask = new Color32[res * res];
        m.thickness = new Color32[res * res];

        Parallel.For(0, res, y =>
        {
            float v = (y + 0.5f) / res;
            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res;
                int i = y * res + x;

                // --- DUAS CAMADAS DE FRATURA em profundidades diferentes.
                //     É a lógica central do shader do pack (_IceDepth +
                //     _BackGroundIceBlend: ele amostra a mesma arte duas vezes,
                //     uma deslocada por paralaxe, e mistura) — só que resolvida
                //     no bake e devolvida ao POM do HDRP pelo height map, que
                //     desloca as duas por quantidades diferentes em tempo real.
                float surf, deep;
                if (veins != null)
                {
                    // superfície: a veia branca nítida, curva apertada (a arte
                    // tem mediana 0,12 e só ~1% acima de 0,49 — é quase toda
                    // gelo limpo, e é isso que queremos)
                    surf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.135f, 0.50f, veins[i]));

                    // profunda: a MESMA arte girada 90° e deslocada. Giro de 90°
                    // + translação inteira num quadrado é uma bijeção do toro:
                    // a textura continua fechando nas 4 bordas. A curva mais
                    // larga e o ganho menor fazem a camada ler como borrada,
                    // que é o que a distância dentro do gelo faz.
                    int sx = (y + res / 3) % res;
                    int sy = ((res - 1 - x) + res / 7) % res;
                    deep = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(0.125f, 0.62f, veins[sy * res + sx])) * 0.75f;
                }
                else
                {
                    // fallback procedural (sem o pack): rede Voronoi em 3 escalas
                    float crack = 0f;
                    crack = Mathf.Max(crack, CrackLayer(u, v, res, 4, 1.6f, seed + 11, 0.45f, 0.30f));
                    crack = Mathf.Max(crack, CrackLayer(u, v, res, 11, 1.1f, seed + 23, 0.55f, 0.62f) * 0.75f);
                    crack = Mathf.Max(crack, CrackLayer(u, v, res, 29, 0.9f, seed + 37, 0.74f, 0.40f) * 0.32f);
                    crack = Mathf.Max(crack, drawnCracks[i]);
                    surf = Mathf.Clamp01((crack - 0.22f) * 2.1f) *
                           (0.60f + 0.40f * Mathf.Abs(Fbm(u, v, 36, 3, seed + 51)));
                    deep = crack * 0.45f;
                }

                float bub = Mathf.Clamp01(bubbles[i]);

                // --- geada/neve soprada: manchas RARAS. Gelo de lago limpo é
                //     escuro e translúcido; a geada é a exceção (bordas de placa,
                //     sombra de vento), não o fundo. Threshold alto = ~12% de área.
                float frostN = Fbm(u, v, 3, 5, seed + 61) * 0.70f + Fbm(u, v, 8, 4, seed + 67) * 0.30f;
                Worley(u, v, 5, seed + 71, out float wf1, out _);
                float frost = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.44f, 0.72f, frostN * 0.85f + (1f - wf1) * 0.15f));

                // --- estrias de vento — quase só polimento
                float scour = Streaks(u, v, seed + 83);

                // --- ondulação macro (o gelo nunca é plano nem uniforme).
                //     Período 3 = manchas de ~4 m: grande o bastante para ler como
                //     variação natural, pequeno o bastante para não denunciar o tile.
                float macro = Fbm(u, v, 3, 4, seed + 97);
                float milky = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.55f, macro));

                // ================================================== HEIGHT (POM)
                // A separação de altura entre as duas camadas de fratura é o que
                // dá o paralaxe: o POM desloca a veia profunda MUITO mais que a
                // da superfície, então virar a câmera faz uma escorregar sobre a
                // outra — a leitura de "isto tem espessura" que gelo pintado
                // num plano nunca tem.
                float h = 0.58f
                        + macro * 0.10f
                        + surf * 0.16f           // costura de gelo recongelado: SOBE
                        - deep * 0.34f           // a fratura interna afunda (POM entra nela)
                        - bub * 0.06f            // bolha fica logo abaixo da superfície
                        + frost * 0.20f          // geada se acumula POR CIMA
                        + scour * 0.02f;
                height[i] = Mathf.Clamp01(h);

                // ================================================== BASE COLOR
                // gelo limpo: ardósia ESCURA com viés ciano. Albedo claro aqui é o
                // que dava o véu leitoso — no gelo real a superfície limpa quase
                // não devolve difusa, o que se vê é a água por baixo (refração).
                // Valores em sRGB (o que se escolheria no color picker) — o PNG é
                // importado como sRGB, então vão direto para o byte.
                Color c = new(0.30f, 0.40f, 0.46f);
                c = Color.Lerp(c, new Color(0.44f, 0.53f, 0.58f), milky * 0.55f);
                // profunda ANTES da superfície: a de cima cobre a de baixo, e é
                // mais fria/apagada porque a espessura de gelo entre ela e o olho
                // já comeu parte da luz (o _IceColorBackground deles)
                c = Color.Lerp(c, new Color(0.55f, 0.66f, 0.73f), deep * 0.85f);
                c = Color.Lerp(c, new Color(0.90f, 0.95f, 0.98f), surf * 0.95f);
                c = Color.Lerp(c, new Color(0.93f, 0.96f, 1.00f), bub);
                c = Color.Lerp(c, new Color(0.95f, 0.97f, 0.99f), frost);

                // ================================================== OPACIDADE
                // 1-alpha vira o transmittanceMask da refração no HDRP: baixo =
                // enxerga a água por baixo · alto = gelo leitoso/opaco.
                // Piso em 0,02: a placa limpa é praticamente uma janela — só a
                // fratura, a bolha e a geada fecham o gelo.
                float a = 0.025f
                        + surf * 0.62f
                        + deep * 0.30f           // a fratura funda fecha MENOS: dá pra ver água através
                        + bub * 0.30f
                        + frost * 0.92f
                        + milky * 0.07f;
                a = Mathf.Clamp(a, 0.02f, 1f);

                m.baseColor[i] = new Color32(B(c.r), B(c.g), B(c.b), B(a));

                // ================================================== MASK MAP
                float ao = 1f - deep * 0.30f - frost * 0.12f - bub * 0.08f;
                float detailMask = 1f - frost * 0.65f;          // micro só no gelo nu
                float smooth = 0.95f
                             - frost * 0.72f                     // geada é fosca
                             - scour * 0.22f                     // riscado do vento
                             - surf * 0.30f                      // a costura na superfície é áspera
                             - deep * 0.08f                      // a funda quase não muda o brilho
                             + macro * 0.05f;                    // polimento irregular
                m.mask[i] = new Color32(
                    0,                                           // metallic
                    (byte)(Mathf.Clamp01(ao) * 255f),
                    (byte)(Mathf.Clamp01(detailMask) * 255f),
                    (byte)(Mathf.Clamp01(smooth) * 255f));

                // ================================================== THICKNESS
                // mais grosso onde é leitoso/geado; mais fino nas janelas limpas.
                // Alimenta a absorção Box do HDRP -> azul profundo nas partes
                // grossas. Baixo no geral: absorção alta come a água refratada,
                // que é justamente o que se quer ver.
                float th = Mathf.Clamp01(0.08f + milky * 0.30f + frost * 0.40f + deep * 0.10f);
                byte tb = (byte)(th * 255f);
                m.thickness[i] = new Color32(tb, tb, tb, 255);
            }
        });

        // ---- 3. normal a partir da altura (diferenças centrais com wrap)
        m.height = new Color32[res * res];
        m.normal = new Color32[res * res];
        // 1 texel = WorldTile/res metros; a amplitude de relevo do mapa é ~4 cm
        float texelM = WorldTile / res;
        float reliefM = 0.045f;
        Parallel.For(0, res, y =>
        {
            int ym = ((y - 1) + res) % res, yp = (y + 1) % res;
            for (int x = 0; x < res; x++)
            {
                int xm = ((x - 1) + res) % res, xp = (x + 1) % res;
                float hl = height[y * res + xm], hr = height[y * res + xp];
                float hd = height[ym * res + x], hu = height[yp * res + x];

                var n = new Vector3(
                    -(hr - hl) * reliefM / (2f * texelM),
                    -(hu - hd) * reliefM / (2f * texelM),
                    1f).normalized;

                int i = y * res + x;
                m.normal[i] = new Color32(
                    (byte)((n.x * 0.5f + 0.5f) * 255f),
                    (byte)((n.y * 0.5f + 0.5f) * 255f),
                    (byte)((n.z * 0.5f + 0.5f) * 255f), 255);

                byte hb = (byte)(height[i] * 255f);
                m.height[i] = new Color32(hb, hb, hb, 255);
            }
        });

        // ---- 4. detail map (micro-craquelê + grão de geada, escala ~1,7 m)
        m.detail = GenerateDetail(detailRes, seed + 6151);
        return m;
    }

    // ================================================================ DETALHE
    /// <summary>
    /// DetailMap no layout do HDRP: R = albedo dessaturado (0,5 neutro) ·
    /// G = normal Y · B = smoothness (0,5 neutro) · A = normal X.
    /// Conteúdo: craquelê MUITO fino + grão de geada — a camada que sobrevive
    /// quando a câmera encosta no chão e a base já perdeu resolução.
    /// </summary>
    static Color32[] GenerateDetail(int res, int seed)
    {
        var px = new Color32[res * res];
        var h = new float[res * res];

        Parallel.For(0, res, y =>
        {
            float v = (y + 0.5f) / res;
            for (int x = 0; x < res; x++)
            {
                float u = (x + 0.5f) / res;
                float craze = CrackLayer(u, v, res, 22, 1.4f, seed + 3, 0.50f, 0.45f);
                craze = Mathf.Max(craze, CrackLayer(u, v, res, 53, 1.0f, seed + 9, 0.58f, 0.40f) * 0.6f);
                float grain = Fbm(u, v, 64, 3, seed + 17);
                h[y * res + x] = Mathf.Clamp01(0.5f - craze * 0.45f + grain * 0.10f);
            }
        });

        Parallel.For(0, res, y =>
        {
            int ym = ((y - 1) + res) % res, yp = (y + 1) % res;
            for (int x = 0; x < res; x++)
            {
                int xm = ((x - 1) + res) % res, xp = (x + 1) % res;
                float dx = h[y * res + xp] - h[y * res + xm];
                float dy = h[yp * res + x] - h[ym * res + x];

                var n = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                int i = y * res + x;
                float hv = h[i];
                px[i] = new Color32(
                    (byte)(Mathf.Clamp01(0.5f + (hv - 0.5f) * 0.6f) * 255f),   // albedo
                    (byte)((n.y * 0.5f + 0.5f) * 255f),                        // normal Y
                    (byte)(Mathf.Clamp01(0.5f + (hv - 0.5f) * 0.9f) * 255f),   // smoothness
                    (byte)((n.x * 0.5f + 0.5f) * 255f));                       // normal X
            }
        });
        return px;
    }

    // =========================================================== CAMADA DE TRINCA
    /// <summary>
    /// Uma escala da rede de fraturas: bordas de células Voronoi (F2-F1),
    /// MASCARADAS por ruído — trincas cobrindo tudo viram padrão de tecido.
    /// `widthPx` é a espessura em PIXELS DESTA textura (por isso `res` entra:
    /// no detail map, de resolução menor, a mesma largura em células sumiria).
    /// `maskThreshold` controla quanto da rede sobrevive.
    /// </summary>
    static float CrackLayer(float u, float v, int res, int period, float widthPx, int seed,
                            float maskThreshold, float maskSoftness)
    {
        Worley(u, v, period, seed, out float f1, out float f2);
        // f2-f1 ~ 2× a distância até a aresta, em unidades de célula
        float widthCells = widthPx * period / res;
        float line = 1f - Mathf.SmoothStep(0f, widthCells * 2f, f2 - f1);
        if (line <= 0.001f) return 0f;

        // máscara: qual parte da rede realmente rompeu
        float mask = Fbm(u, v, Mathf.Max(3, period / 2), 4, seed + 313) * 0.5f + 0.5f;
        float keep = Mathf.SmoothStep(maskThreshold, maskThreshold + maskSoftness, mask);
        return line * keep;
    }

    // ============================================================ RASTERIZAÇÃO
    /// <summary>
    /// Estrelas de impacto: um ponto de ruptura com 5–9 raios que afinam para
    /// fora, alguns com bifurcação. É o desenho que Voronoi nunca produz e o que
    /// faz o olho ler "gelo" em vez de "mosaico".
    /// </summary>
    static void RasterizeImpactStars(float[] buf, int res, int seed)
    {
        var rng = new System.Random(seed);
        int stars = 16;
        for (int s = 0; s < stars; s++)
        {
            float cx = (float)rng.NextDouble() * res;
            float cy = (float)rng.NextDouble() * res;
            int rays = 5 + rng.Next(5);
            float a0 = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int r = 0; r < rays; r++)
            {
                float a = a0 + r * (Mathf.PI * 2f / rays)
                        + ((float)rng.NextDouble() - 0.5f) * 0.55f;
                float len = res * (0.02f + (float)rng.NextDouble() * 0.09f);
                Polyline(buf, res, cx, cy, a, len, 1.7f, 0.95f, rng, 2);
            }
        }
    }

    /// <summary>
    /// Fissuras térmicas: linhas LONGAS e pouco curvas atravessando a placa
    /// inteira (o gelo racha de ponta a ponta quando a temperatura vira).
    /// </summary>
    static void RasterizeThermalFissures(float[] buf, int res, int seed)
    {
        var rng = new System.Random(seed);
        for (int f = 0; f < 6; f++)
        {
            float cx = (float)rng.NextDouble() * res;
            float cy = (float)rng.NextDouble() * res;
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            Polyline(buf, res, cx, cy, a, res * 1.2f, 2.3f, 1f, rng, 1);
            Polyline(buf, res, cx, cy, a + Mathf.PI, res * 0.9f, 2.1f, 1f, rng, 1);
        }
    }

    /// <summary>
    /// Traça uma trinca: caminha na direção `angle` com deriva aleatória,
    /// afinando e enfraquecendo até o fim; `branches` sorteia bifurcações.
    /// Tudo com wrap — a trinca que sai por uma borda entra pela oposta.
    /// </summary>
    static void Polyline(float[] buf, int res, float x, float y, float angle,
                         float length, float width, float strength,
                         System.Random rng, int branches)
    {
        float step = 2f;
        int steps = Mathf.Max(1, Mathf.RoundToInt(length / step));
        float bx = -1f, by = -1f, bAngle = 0f, bAt = branches > 0 ? steps * (0.15f + (float)rng.NextDouble() * 0.5f) : -1f;

        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            float fade = 1f - t * t;                       // a ponta da trinca some
            Stamp(buf, res, x, y, width * fade + 0.6f, strength * fade);

            angle += ((float)rng.NextDouble() - 0.5f) * 0.14f;
            x += Mathf.Cos(angle) * step;
            y += Mathf.Sin(angle) * step;

            if (bAt > 0 && i > bAt && bx < 0f)
            {
                bx = x; by = y;
                bAngle = angle + (rng.Next(2) == 0 ? 0.6f : -0.6f);
            }
        }
        if (bx >= 0f && branches > 0)
            Polyline(buf, res, bx, by, bAngle, length * 0.45f, width * 0.65f,
                     strength * 0.8f, rng, branches - 1);
    }

    /// <summary>
    /// Nuvens de bolhas de ar: densidade por manchas, tamanhos variados e ~14%
    /// COLUNARES (esticadas) — a assinatura do congelamento rápido.
    /// ESPARSAS de propósito: na referência de lago congelado a bolha é um ponto
    /// branco isolado dentro do gelo escuro. Densa, vira espuma e apaga a água.
    /// </summary>
    static void RasterizeBubbles(float[] buf, int res, int seed)
    {
        var rng = new System.Random(seed);
        int cells = 150;                    // grade jitterada = distribuição sem grumos
        float cell = res / (float)cells;
        for (int cy = 0; cy < cells; cy++)
            for (int cx = 0; cx < cells; cx++)
            {
                float u = (cx + 0.5f) / cells, v = (cy + 0.5f) / cells;
                float density = Fbm(u, v, 4, 4, seed + 5) * 0.5f + 0.5f;
                density = Mathf.SmoothStep(0.58f, 0.90f, density);
                if (rng.NextDouble() > density * 0.45f) continue;

                float x = (cx + (float)rng.NextDouble()) * cell;
                float y = (cy + (float)rng.NextDouble()) * cell;
                float r = 0.9f + (float)rng.NextDouble() * (float)rng.NextDouble() * 4.5f;
                float amp = 0.30f + (float)rng.NextDouble() * 0.45f;

                if (rng.NextDouble() < 0.14f)
                {
                    // colunar: uma coluninha de ar presa no congelamento
                    float len = r * (2f + (float)rng.NextDouble() * 3.5f);
                    float a = (float)rng.NextDouble() * Mathf.PI;
                    int n = Mathf.Max(2, Mathf.RoundToInt(len));
                    for (int k = 0; k < n; k++)
                    {
                        float t = k / (float)(n - 1) - 0.5f;
                        Stamp(buf, res, x + Mathf.Cos(a) * t * len,
                                        y + Mathf.Sin(a) * t * len,
                                        r * 0.75f, amp);
                    }
                }
                else Stamp(buf, res, x, y, r, amp);
            }
    }

    /// <summary>Carimba um disco com queda suave, acumulando por MAX e com wrap.</summary>
    static void Stamp(float[] buf, int res, float cx, float cy, float radius, float strength)
    {
        if (strength <= 0.001f || radius <= 0.01f) return;
        int r = Mathf.CeilToInt(radius);
        int ix = Mathf.FloorToInt(cx), iy = Mathf.FloorToInt(cy);
        for (int dy = -r; dy <= r; dy++)
        {
            int y = ((iy + dy) % res + res) % res;
            for (int dx = -r; dx <= r; dx++)
            {
                float ddx = ix + dx + 0.5f - cx, ddy = iy + dy + 0.5f - cy;
                float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                if (d > radius) continue;
                int x = ((ix + dx) % res + res) % res;
                float f = 1f - Mathf.SmoothStep(radius * 0.35f, radius, d);
                int i = y * res + x;
                float val = f * strength;
                if (val > buf[i]) buf[i] = val;
            }
        }
    }

    // ==================================================================== RUÍDO
    // Perlin e Worley PERIÓDICOS: sem wrap inteiro a textura não fecha e a
    // emenda aparece como uma linha reta no lago inteiro.

    static readonly float[] GradX = new float[16];
    static readonly float[] GradY = new float[16];

    static IceTextureGenerator()
    {
        for (int i = 0; i < 16; i++)
        {
            float a = i * (Mathf.PI * 2f / 16f);
            GradX[i] = Mathf.Cos(a);
            GradY[i] = Mathf.Sin(a);
        }
    }

    static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

    static int Wrap(int v, int p) { v %= p; return v < 0 ? v + p : v; }

    /// <summary>Perlin periódico em coordenadas de célula (u*period). Saída ~[-1,1].</summary>
    static float Perlin(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        int wx0 = Wrap(x0, period), wy0 = Wrap(y0, period);
        int wx1 = Wrap(x0 + 1, period), wy1 = Wrap(y0 + 1, period);

        float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        float v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

        float n00 = Dot(wx0, wy0, seed, fx, fy);
        float n10 = Dot(wx1, wy0, seed, fx - 1f, fy);
        float n01 = Dot(wx0, wy1, seed, fx, fy - 1f);
        float n11 = Dot(wx1, wy1, seed, fx - 1f, fy - 1f);
        return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v) * 1.4f;
    }

    /// <summary>Perlin com período INDEPENDENTE por eixo (para ruído esticado).</summary>
    static float Perlin2(float x, float y, int px, int py, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        int wx0 = Wrap(x0, px), wy0 = Wrap(y0, py);
        int wx1 = Wrap(x0 + 1, px), wy1 = Wrap(y0 + 1, py);

        float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        float v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);

        float n00 = Dot(wx0, wy0, seed, fx, fy);
        float n10 = Dot(wx1, wy0, seed, fx - 1f, fy);
        float n01 = Dot(wx0, wy1, seed, fx, fy - 1f);
        float n11 = Dot(wx1, wy1, seed, fx - 1f, fy - 1f);
        return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v) * 1.4f;
    }

    /// <summary>
    /// Estrias direcionais TILEÁVEIS (neve varrida pelo vento / marcas de patinação).
    /// Truque: a direção é um vetor INTEIRO do toro — along = 2u+v e across = u-2v.
    /// Somar 1 a u ou a v desloca ambos por um número inteiro de períodos, então a
    /// costura fecha mesmo com o ruído rotacionado (rotação livre sempre emenda).
    /// </summary>
    static float Streaks(float u, float v, int seed)
    {
        float along = 2f * u + 1f * v;
        float across = 1f * u - 2f * v;

        float n = 0f, amp = 1f, norm = 0f;
        int px = 3, py = 30;                     // longo no sentido do vento, fino no perpendicular
        for (int i = 0; i < 3; i++)
        {
            n += Perlin2(along * px, across * py, px, py, seed + i * 71) * amp;
            norm += amp; amp *= 0.5f; px *= 2; py *= 2;
        }
        // |ruído| com corte alto = riscos finos e esparsos, não um degradê
        return Mathf.SmoothStep(0.42f, 0.88f, Mathf.Abs(n / norm));
    }

    static float Dot(int gx, int gy, int seed, float dx, float dy)
    {
        uint h = Hash(gx, gy, seed) & 15u;
        return GradX[h] * dx + GradY[h] * dy;
    }

    /// <summary>fbm periódico sobre UV normalizada [0,1). Saída ~[-1,1].</summary>
    static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        int p = Mathf.Max(1, basePeriod);
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin(u * p, v * p, p, seed + i * 131) * amp;
            norm += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return sum / norm;
    }

    /// <summary>Worley periódico: f1/f2 em unidades de célula (para F2-F1).</summary>
    static void Worley(float u, float v, int period, int seed, out float f1, out float f2)
    {
        float x = u * period, y = v * period;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        f1 = 99f; f2 = 99f;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = xi + dx, cy = yi + dy;
                int wx = Wrap(cx, period), wy = Wrap(cy, period);
                float px = cx + Hash01(wx, wy, seed);
                float py = cy + Hash01(wx, wy, seed + 7717);
                float ddx = px - x, ddy = py - y;
                float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) f2 = d;
            }
    }

    static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * 255f), 0, 255);
}
