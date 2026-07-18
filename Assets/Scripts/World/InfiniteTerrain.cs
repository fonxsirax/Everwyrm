using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mundo infinito por streaming de tiles de Terrain (GDD: mapa praticamente infinito).
///
///  - Tiles de Terrain gerados ao redor do jogador com orçamento por frame;
///    tiles distantes são destruídos. Determinístico pela seed.
///  - BIOMAS por mapas contínuos de temperatura/umidade/montanha:
///      Campos (base) · Floresta Antiga (úmido) · Montanhas Rochosas · Tundra (frio)
///    Altura, textura (grama/floresta/rocha/neve) e vegetação derivam deles,
///    então as bordas entre tiles são sempre contínuas.
///  - Árvores (prefabs ALP) espalhadas na Floresta.
///  - Fase 2 (futuro): blocos de bioma construídos à mão substituindo o noise
///    região por região, como manda o GDD.
///
/// O FoodSpawner e o pouso do dragão funcionam por raycast — nada muda para eles.
/// </summary>
public class InfiniteTerrain : MonoBehaviour
{
    [Header("Geral")]
    public Transform player;                    // auto: tag Player
    public int seed = 20260718;
    [SerializeField] float tileSize = 250f;
    [SerializeField] int heightmapRes = 129;    // potência de 2 + 1
    [SerializeField] int alphamapRes = 64;
    [SerializeField] float maxHeight = 130f;
    [SerializeField] int loadRadius = 2;        // 2 => 5x5 tiles (~625 m de vista)
    [SerializeField] int tilesPerFrame = 1;

    [Header("Biomas habilitados (para testar cada um)")]
    public bool enablePlains = true;      // Campos
    public bool enableForest = true;      // Floresta Antiga
    public bool enableMountains = true;   // Montanhas Rochosas
    public bool enableTundra = true;      // Tundra

    [Header("Tamanho das manchas de bioma (metros aprox.)")]
    [SerializeField] float biomePatchSize = 700f;   // menor = biomas mais próximos uns dos outros

    [Header("Vegetação")]
    public GameObject[] treePrefabs;            // Floresta Antiga (+ raras nos Campos)
    public GameObject[] bushPrefabs;            // arbustos: floresta densa + campos esparsos
    [SerializeField] int forestVegetationPerTile = 600;   // floresta BEM densa
    [SerializeField, Range(0f, 1f)] float bushShare = 0.25f;

    [Header("Texturas do chão (auto-preenchidas no editor)")]
    public Texture2D grassDiffuse; public Texture2D grassNormal; public Texture2D grassMask;
    public Texture2D forestDiffuse; public Texture2D forestNormal; public Texture2D forestMask;
    public Texture2D rockDiffuse; public Texture2D rockNormal; public Texture2D rockMask;
    public Texture2D snowDiffuse; public Texture2D snowNormal; public Texture2D snowMask;
    [SerializeField] float groundTextureTile = 12f;

    [Header("Graminhas (espalhadas como tree instances)")]
    public GameObject[] grassPrefabs;
    [SerializeField] int grassPerTile = 500;

    public static InfiniteTerrain Instance { get; private set; }

    [Header("Cores dos biomas (fallback)")]
    [SerializeField] Color grassColor = new(0.42f, 0.55f, 0.25f);
    [SerializeField] Color forestColor = new(0.22f, 0.38f, 0.16f);
    [SerializeField] Color rockColor = new(0.45f, 0.42f, 0.4f);
    [SerializeField] Color snowColor = new(0.92f, 0.94f, 0.97f);

    readonly Dictionary<Vector2Int, Terrain> tiles = new();
    readonly Dictionary<Vector2Int, List<Vector2>> tileTrees = new(); // XZ das árvores (minimapa)
    readonly Queue<Vector2Int> buildQueue = new();
    readonly HashSet<Vector2Int> pending = new();

    TerrainLayer[] layers;
    Material terrainMat;
    float oxT, ozT, oxM, ozM, oxH, ozH, oxD, ozD; // offsets de noise (seed)

    // ------------------------------------------------------------ LIFECYCLE
    void Awake()
    {
        Instance = this;
        var rng = new System.Random(seed);
        float Off() => (float)(rng.NextDouble() * 10000.0 - 5000.0);
        oxT = Off(); ozT = Off(); oxM = Off(); ozM = Off();
        oxH = Off(); ozH = Off(); oxD = Off(); ozD = Off();

        var shader = Shader.Find("HDRP/TerrainLit");
        terrainMat = shader != null ? new Material(shader) : null;

        layers = new[]
        {
            MakeLayer(grassDiffuse, grassNormal, grassMask, grassColor, groundTextureTile),
            MakeLayer(forestDiffuse, forestNormal, forestMask, forestColor, groundTextureTile),
            MakeLayer(rockDiffuse, rockNormal, rockMask, rockColor, groundTextureTile * 1.6f),
            MakeLayer(snowDiffuse, snowNormal, snowMask, snowColor, groundTextureTile)
        };
    }

    void Start()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player == null) { enabled = false; return; }

        BuildTile(TileOf(player.position));     // tile inicial síncrono
        SnapPlayerToGround();
    }

    void Update()
    {
        if (player == null) return;
        Vector2Int center = TileOf(player.position);

        // enfileira tiles do raio (mais próximos primeiro)
        for (int r = 0; r <= loadRadius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                    var c = new Vector2Int(center.x + dx, center.y + dz);
                    if (!tiles.ContainsKey(c) && pending.Add(c)) buildQueue.Enqueue(c);
                }

        // constrói com orçamento
        for (int i = 0; i < tilesPerFrame && buildQueue.Count > 0; i++)
        {
            var c = buildQueue.Dequeue();
            pending.Remove(c);
            if (!tiles.ContainsKey(c) &&
                Mathf.Max(Mathf.Abs(c.x - center.x), Mathf.Abs(c.y - center.y)) <= loadRadius)
                BuildTile(c);
        }

        // descarta os distantes
        List<Vector2Int> remove = null;
        foreach (var kv in tiles)
            if (Mathf.Max(Mathf.Abs(kv.Key.x - center.x), Mathf.Abs(kv.Key.y - center.y)) > loadRadius + 1)
                (remove ??= new List<Vector2Int>()).Add(kv.Key);
        if (remove != null)
            foreach (var c in remove)
            {
                var t = tiles[c];
                tiles.Remove(c);
                tileTrees.Remove(c);
                if (t != null)
                {
                    var data = t.terrainData;
                    Destroy(t.gameObject);
                    Destroy(data);
                }
            }
    }

    // ---------------------------------------------------------------- TILES
    Vector2Int TileOf(Vector3 pos) =>
        new(Mathf.FloorToInt(pos.x / tileSize), Mathf.FloorToInt(pos.z / tileSize));

    void BuildTile(Vector2Int coord)
    {
        float ox = coord.x * tileSize, oz = coord.y * tileSize;

        var td = new TerrainData
        {
            heightmapResolution = heightmapRes,
            alphamapResolution = alphamapRes,
            size = new Vector3(tileSize, maxHeight, tileSize)
        };
        // (size precisa ser re-aplicado após heightmapResolution)
        td.size = new Vector3(tileSize, maxHeight, tileSize);
        td.terrainLayers = layers;

        // ---- alturas (bordas contínuas: noise em coordenadas de mundo)
        float step = tileSize / (heightmapRes - 1);
        var heights = new float[heightmapRes, heightmapRes];
        for (int z = 0; z < heightmapRes; z++)
            for (int x = 0; x < heightmapRes; x++)
                heights[z, x] = HeightAt(ox + x * step, oz + z * step) / maxHeight;
        td.SetHeights(0, 0, heights);

        // ---- texturas por bioma + inclinação
        var alphas = new float[alphamapRes, alphamapRes, 4];
        float aStep = tileSize / (alphamapRes - 1);
        for (int z = 0; z < alphamapRes; z++)
            for (int x = 0; x < alphamapRes; x++)
            {
                float wx = ox + x * aStep, wz = oz + z * aStep;
                BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out float cold);

                float h01 = HeightAt(wx, wz) / maxHeight;
                float slope = SlopeAt(wx, wz);
                float rock = mount + Mathf.Clamp01(slope * 2.2f - 0.35f);
                float snow = cold + Mathf.Clamp01((h01 - 0.75f) * 4f); // neve só em picos altos/tundra

                float g = plains, f = forest, r = rock, s = snow;
                float sum = g + f + r + s + 0.0001f;
                alphas[z, x, 0] = g / sum;
                alphas[z, x, 1] = f / sum;
                alphas[z, x, 2] = r / sum;
                alphas[z, x, 3] = s / sum;
            }
        td.SetAlphamaps(0, 0, alphas);

        // ---- vegetação (determinística por tile): floresta densa, campos esparsos
        //      Graminhas entram como tree instances: o detail system do Unity não
        //      aceita prefabs com LODGroup (caso dos ALP) e falhava em silêncio.
        int treeCount = treePrefabs?.Length ?? 0;
        int bushCount = bushPrefabs?.Length ?? 0;
        int grassCount = grassPrefabs?.Length ?? 0;
        if (treeCount + bushCount + grassCount > 0)
        {
            var protos = new TreePrototype[treeCount + bushCount + grassCount];
            for (int i = 0; i < treeCount; i++)
                protos[i] = new TreePrototype { prefab = treePrefabs[i] };
            for (int i = 0; i < bushCount; i++)
                protos[treeCount + i] = new TreePrototype { prefab = bushPrefabs[i] };
            for (int i = 0; i < grassCount; i++)
                protos[treeCount + bushCount + i] = new TreePrototype { prefab = grassPrefabs[i] };
            td.treePrototypes = protos;
            td.RefreshPrototypes();

            var rng = new System.Random(seed ^ (coord.x * 73856093) ^ (coord.y * 19349663));
            var instances = new List<TreeInstance>();
            var treeXZ = new List<Vector2>();
            for (int i = 0; i < forestVegetationPerTile; i++)
            {
                float nx = (float)rng.NextDouble(), nz = (float)rng.NextDouble();
                float wx = ox + nx * tileSize, wz = oz + nz * tileSize;
                BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out _);
                if (mount > 0.35f || SlopeAt(wx, wz) > 0.5f) continue;

                bool isBush;
                if (forest > 0.4f)                        // Floresta Antiga: BEM densa
                    isBush = bushCount > 0 && rng.NextDouble() < bushShare;
                else if (plains > 0.5f)                   // Campos: raros e esparsos
                {
                    if (rng.NextDouble() > 0.05) continue;
                    isBush = bushCount > 0 && rng.NextDouble() < 0.7;
                }
                else continue;                            // tundra/transições: sem vegetação
                if (!isBush && treeCount == 0) continue;
                if (!isBush) treeXZ.Add(new Vector2(wx, wz)); // só ÁRVORES no minimapa

                float scale = (isBush ? 0.8f : 0.85f) + (float)rng.NextDouble() * (isBush ? 0.4f : 0.55f);
                instances.Add(new TreeInstance
                {
                    // altura interpolada do PRÓPRIO heightmap: planta cravada no chão
                    position = new Vector3(nx, td.GetInterpolatedHeight(nx, nz) / maxHeight, nz),
                    prototypeIndex = isBush ? treeCount + rng.Next(bushCount) : rng.Next(treeCount),
                    heightScale = scale,
                    widthScale = scale,
                    color = Color.white,
                    lightmapColor = Color.white
                });
            }
            // ---- graminhas: densas nos Campos e na Floresta
            for (int i = 0; i < (grassCount > 0 ? grassPerTile : 0); i++)
            {
                float nx = (float)rng.NextDouble(), nz = (float)rng.NextDouble();
                float wx = ox + nx * tileSize, wz = oz + nz * tileSize;
                BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out float cold);
                if (mount > 0.3f || cold > 0.4f) continue;
                if (rng.NextDouble() > plains + forest * 0.6f) continue;
                if (SlopeAt(wx, wz) > 0.5f) continue;

                float scale = 0.9f + (float)rng.NextDouble() * 0.7f;
                instances.Add(new TreeInstance
                {
                    position = new Vector3(nx, td.GetInterpolatedHeight(nx, nz) / maxHeight, nz),
                    prototypeIndex = treeCount + bushCount + rng.Next(grassCount),
                    heightScale = scale,
                    widthScale = scale,
                    color = Color.white,
                    lightmapColor = Color.white
                });
            }

            td.SetTreeInstances(instances.ToArray(), true);
            tileTrees[coord] = treeXZ;
        }

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = $"Tile {coord.x},{coord.y}";
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(ox, 0f, oz);

        var terrain = go.GetComponent<Terrain>();
        if (terrainMat != null) terrain.materialTemplate = terrainMat;
        terrain.allowAutoConnect = true;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 8f;
        terrain.treeBillboardDistance = 180f;
        terrain.treeDistance = 700f;

        tiles[coord] = terrain;
    }

    void SnapPlayerToGround()
    {
        float h = HeightAt(player.position.x, player.position.z);
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.position = new Vector3(player.position.x, h + 1.5f, player.position.z);
        if (cc != null) cc.enabled = true;
    }

    /// <summary>Árvores (XZ do mundo) num raio — usado pelo minimapa.</summary>
    public void GetTreesNear(Vector3 pos, float range, List<Vector2> results, int max)
    {
        float r2 = range * range;
        int minX = Mathf.FloorToInt((pos.x - range) / tileSize);
        int maxX = Mathf.FloorToInt((pos.x + range) / tileSize);
        int minZ = Mathf.FloorToInt((pos.z - range) / tileSize);
        int maxZ = Mathf.FloorToInt((pos.z + range) / tileSize);

        for (int tx = minX; tx <= maxX; tx++)
            for (int tz = minZ; tz <= maxZ; tz++)
            {
                if (!tileTrees.TryGetValue(new Vector2Int(tx, tz), out var list)) continue;
                foreach (var p in list)
                {
                    float dx = p.x - pos.x, dz = p.y - pos.z;
                    if (dx * dx + dz * dz > r2) continue;
                    results.Add(p);
                    if (results.Count >= max) return;
                }
            }
    }

    // ---------------------------------------------------------------- NOISE
    /// <summary>Altura do mundo em metros — contínua, determinística, sem costuras.</summary>
    public float HeightAt(float wx, float wz)
    {
        BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out float cold);

        float hPlains = 6f + FBM(wx, wz, 0.008f, 2) * 8f;
        float hForest = 8f + FBM(wx, wz, 0.011f, 3) * 14f;
        float hTundra = 5f + FBM(wx, wz, 0.007f, 2) * 6f;

        float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx * 0.0035f + oxH, wz * 0.0035f + ozH) - 1f);
        float hMount = 14f + ridge * ridge * 100f + FBM(wx, wz, 0.02f, 2) * 10f;

        return plains * hPlains + forest * hForest + mount * hMount + cold * hTundra;
    }

    /// <summary>Pesos normalizados dos 4 biomas em um ponto do mundo (respeita os toggles).</summary>
    public void BiomeWeights(float wx, float wz,
        out float plains, out float forest, out float mount, out float cold)
    {
        float f = 1f / Mathf.Max(100f, biomePatchSize);
        float temp = Mathf.PerlinNoise(wx * f + oxT, wz * f + ozT);
        float moist = Mathf.PerlinNoise(wx * f * 1.15f + oxM, wz * f * 1.15f + ozM);
        float mountains = Mathf.PerlinNoise(wx * f * 1.4f + oxD, wz * f * 1.4f + ozD);

        mount = enableMountains
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.72f, mountains)) : 0f;
        cold = enableTundra
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.78f, 1f - temp)) * (1f - mount) : 0f;
        forest = enableForest
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.68f, moist)) * (1f - mount) * (1f - cold) : 0f;
        plains = enablePlains ? Mathf.Max(0f, 1f - mount - cold - forest) : 0f;

        // normaliza (se desligar tudo, vira Campos)
        float sum = plains + forest + mount + cold;
        if (sum < 0.001f) { plains = 1f; forest = mount = cold = 0f; return; }
        plains /= sum; forest /= sum; mount /= sum; cold /= sum;
    }

    float SlopeAt(float wx, float wz)
    {
        const float e = 3f;
        float h = HeightAt(wx, wz);
        float dx = HeightAt(wx + e, wz) - h;
        float dz = HeightAt(wx, wz + e) - h;
        return Mathf.Sqrt(dx * dx + dz * dz) / e;
    }

    float FBM(float wx, float wz, float freq, int octaves)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(wx * freq + oxH + i * 37.7f, wz * freq + ozH + i * 51.3f);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }

    // -------------------------------------------------------------- HELPERS
    static TerrainLayer MakeLayer(Texture2D diffuse, Texture2D normal, Texture2D mask,
                                  Color fallback, float tile)
    {
        if (diffuse == null)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = fallback;
            tex.SetPixels(px);
            tex.Apply();
            return new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(64f, 64f),
                smoothness = 0.05f, metallic = 0f
            };
        }
        return new TerrainLayer
        {
            diffuseTexture = diffuse,
            normalMapTexture = normal,
            maskMapTexture = mask,       // HDRP: controla brilho/AO — sem isso o chão fica lustroso
            normalScale = 1f,
            smoothness = 0.08f, metallic = 0f,
            tileSize = new Vector2(tile, tile)
        };
    }

#if UNITY_EDITOR
    // Auto-preenche os assets do pacote ALP — funciona mesmo sem rodar o menu de setup.
    void OnValidate()
    {
        if (Application.isPlaying || grassDiffuse != null) return;
        const string G = "Assets/ALP_Assets/Nature Package - Forest Environment_/GroundTextures/";
        const string V = "Assets/ALP_Assets/Nature Package - Forest Environment_/_Vegetation/HDRP/HDRP_Prefabs/";
        Texture2D T(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        GameObject P(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);

        grassDiffuse = T(G + "Grass_001.tif"); grassNormal = T(G + "Grass_001_N.png"); grassMask = T(G + "Grass_001_mask.png");
        forestDiffuse = T(G + "Ground01.tif"); forestNormal = T(G + "Ground01_N.png"); forestMask = T(G + "Ground01_mask.png");
        rockDiffuse = T(G + "Ground02.tif"); rockNormal = T(G + "Ground02_N.png"); rockMask = T(G + "Ground02_mask.png");

        if (treePrefabs == null || treePrefabs.Length == 0)
            treePrefabs = new[] { P(V + "ForestTree01_HDRP.prefab"), P(V + "ForestTree02_HDRP.prefab"),
                                  P(V + "ForestTree03_HDRP.prefab"), P(V + "ForestTree04_HDRP.prefab") };
        if (bushPrefabs == null || bushPrefabs.Length == 0)
            bushPrefabs = new[] { P(V + "ForestBush01_HDRP.prefab"), P(V + "ForestBush02_HDRP.prefab"),
                                  P(V + "ForestBush03_HDRP.prefab"), P(V + "ForestBush04_HDRP.prefab") };
        if (grassPrefabs == null || grassPrefabs.Length == 0)
            grassPrefabs = new[] { P(V + "FlowerGrass01_HDRP.prefab"), P(V + "FlowerGrass02_HDRP.prefab"),
                                   P(V + "GrassPlant02_HDRP.prefab"), P(V + "GrassPlant03_HDRP.prefab") };
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
