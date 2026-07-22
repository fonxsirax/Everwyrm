using UnityEngine;

/// <summary>
/// Vista de horizonte ("far terrain") — anel de terreno low-poly de ~1 a 9 km
/// em volta do jogador, estilo Zelda BotW, MAS honesto: as alturas e cores vêm
/// das MESMAS funções puras da seed (InfiniteTerrain.HeightAt/BiomeWeightOf),
/// então a montanha que você vê no horizonte é um lugar REAL — voe até lá e os
/// tiles de verdade nascem com aquele formato. A perspectiva aérea (haze) vem
/// de graça da nossa névoa + PBS, que tingem as silhuetas por distância.
///
/// Custo: um mesh de ~5k vértices + uma textura polar de cores; rebuild só
/// quando o jogador anda `rebuildStep` metros (~3 ms de CPU, esporádico).
/// Material HDRP/Lit (NUNCA unlit — aprendemos essa lição): a vista escurece à
/// noite, doura no pôr do sol e recebe a névoa como todo o resto do mundo.
/// </summary>
public class HorizonVista : MonoBehaviour
{
    [Header("Anel (m)")]
    [Tooltip("Raio interno — logo além dos tiles reais (~900 m). No raio interno a escala é 1:1 com o terreno real, escondendo a emenda.")]
    public float innerRadius = 900f;
    [Tooltip("Raio externo — até onde o horizonte alcança. A névoa dissolve o fim.")]
    public float outerRadius = 9000f;
    [Range(64, 360)] public int angularSegments = 200;
    [Range(8, 48)] public int radialRings = 22;
    [Tooltip("Exagero de altura no anel DISTANTE (1 = fiel). Cresce gradualmente do interno (sempre 1:1) ao externo — silhuetas dramáticas sem quebrar a emenda.")]
    [Range(1f, 5f)] public float farHeightScale = 2.5f;
    [Tooltip("Rebuild do anel quando o jogador anda isto (m).")]
    public float rebuildStep = 300f;

    [Header("Cores por bioma (tingidas pela luz/névoa reais)")]
    public Color colorCampos = new(0.45f, 0.52f, 0.30f);
    public Color colorFloresta = new(0.22f, 0.35f, 0.20f);
    public Color colorMontanha = new(0.45f, 0.44f, 0.42f);
    public Color colorTundra = new(0.85f, 0.87f, 0.90f);
    public Color colorDeserto = new(0.76f, 0.66f, 0.47f);
    public Color colorAgua = new(0.15f, 0.30f, 0.38f);
    [Tooltip("Altura (m, real, sem exagero) onde começa a neve nos picos da vista; funde até +30 m acima.")]
    public float snowLine = 60f;

    // ------------------------------------------------------------------ estado
    InfiniteTerrain world;
    Mesh mesh;
    Texture2D colorTex;
    Material mat;
    Transform vistaGo;
    Vector3 center = new(float.MinValue, 0f, float.MinValue);
    Vector3[] verts;
    Vector2[] uvs;
    Color[] pixels;

    void Start()
    {
        world = FindFirstObjectByType<InfiniteTerrain>();
        if (world == null)
        {
            Debug.LogWarning("[HorizonVista] Sem InfiniteTerrain na cena — vista desativada.");
            enabled = false;
            return;
        }

        BuildObjects();

        // o horizonte precisa caber no far plane da câmera
        var cam = Camera.main;
        if (cam != null)
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, outerRadius * 1.15f);
    }

    void LateUpdate()
    {
        if (world == null || world.player == null) return;
        Vector3 p = world.player.position;
        float dx = p.x - center.x, dz = p.z - center.z;
        if (dx * dx + dz * dz > rebuildStep * rebuildStep)
            Rebuild(p);
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (colorTex != null) Destroy(colorTex);
        if (mat != null) Destroy(mat);
    }

    // ---------------------------------------------------------------- BUILD
    void BuildObjects()
    {
        var go = new GameObject("Vista Mesh");
        go.transform.SetParent(transform, false);
        vistaGo = go.transform;

        int cols = angularSegments + 1;              // coluna extra p/ fechar o UV
        int rows = radialRings + 1;
        verts = new Vector3[cols * rows];
        uvs = new Vector2[cols * rows];
        pixels = new Color[cols * rows];

        // triângulos fixos (grade anelar)
        var tris = new int[angularSegments * radialRings * 6];
        int t = 0;
        for (int r = 0; r < radialRings; r++)
            for (int a = 0; a < angularSegments; a++)
            {
                int i0 = r * cols + a;
                int i1 = i0 + 1;
                int i2 = i0 + cols;
                int i3 = i2 + 1;
                tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
            }

        for (int r = 0; r < rows; r++)
            for (int a = 0; a < cols; a++)
                uvs[r * cols + a] = new Vector2(a / (float)angularSegments,
                                                r / (float)radialRings);

        mesh = new Mesh { name = "Horizon Vista", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;                        // preenchido no Rebuild
        mesh.uv = uvs;
        mesh.triangles = tris;

        colorTex = new Texture2D(cols, rows, TextureFormat.RGB24, false)
        { name = "Vista Colors", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        var shader = Shader.Find("HDRP/Lit");
        mat = new Material(shader);
        mat.SetTexture("_BaseColorMap", colorTex);
        mat.SetFloat("_Smoothness", 0f);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Rebuild(world.player != null ? world.player.position : Vector3.zero);
    }

    /// <summary>Recentra o anel no jogador e recalcula alturas/cores a partir
    /// das funções puras da seed — o horizonte é uma PRÉVIA do mundo real.</summary>
    void Rebuild(Vector3 playerPos)
    {
        center = playerPos;
        float waterLevel = world.WaterLevel;
        int cols = angularSegments + 1;
        int rows = radialRings + 1;

        for (int r = 0; r < rows; r++)
        {
            // espaçamento exponencial: mais detalhe perto, alcance longe
            float rt = r / (float)radialRings;
            float radius = innerRadius * Mathf.Pow(outerRadius / innerRadius, rt);
            float scale = Mathf.Lerp(1f, farHeightScale, rt * rt);

            for (int a = 0; a < cols; a++)
            {
                float ang = a / (float)angularSegments * Mathf.PI * 2f;
                float wx = center.x + Mathf.Cos(ang) * radius;
                float wz = center.z + Mathf.Sin(ang) * radius;

                float rawH = world.HeightAt(wx, wz);
                bool isWater = rawH < waterLevel;
                // exagero só ACIMA da lâmina d'água (lagos continuam planos)
                float h = isWater ? waterLevel
                                  : waterLevel + (rawH - waterLevel) * scale;

                verts[r * cols + a] = new Vector3(wx - center.x, h - 1.5f, wz - center.z);

                if (isWater)
                    pixels[r * cols + a] = colorAgua;
                else
                {
                    world.BiomeWeights(wx, wz, out float pl, out float fo,
                                       out float mo, out float co, out float de);
                    float sum = Mathf.Max(0.001f, pl + fo + mo + co + de);
                    Color col =
                        (colorCampos * pl + colorFloresta * fo + colorMontanha * mo +
                         colorTundra * co + colorDeserto * de) / sum;
                    // neve por altitude REAL (antes do exagero): picos nevados
                    // como no mundo de verdade (módulo Snow do MicroSplat)
                    float snow = Mathf.InverseLerp(snowLine, snowLine + 30f, rawH);
                    if (snow > 0f) col = Color.Lerp(col, colorTundra, snow * 0.9f);
                    pixels[r * cols + a] = col;
                }
            }
        }

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        colorTex.SetPixels(pixels);
        colorTex.Apply(false);
        vistaGo.position = new Vector3(center.x, 0f, center.z);
    }
}
