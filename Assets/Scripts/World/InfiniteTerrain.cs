using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mundo infinito por streaming de tiles de Terrain (GDD: mapa praticamente infinito).
///
///  - Tiles de Terrain gerados ao redor do jogador com orçamento por frame;
///    tiles distantes são destruídos. Determinístico pela seed.
///  - BIOMAS por mapas contínuos de temperatura/umidade/montanha:
///      Campos (base) · Floresta Antiga (úmido) · Montanhas Rochosas · Tundra (frio)
///      · Deserto Rochoso (quente + seco, assets do pacote RockyDesert)
///    Altura, textura e vegetação derivam deles — bordas entre tiles sempre contínuas.
///
///  - VEGETAÇÃO/PROPS por CAMADAS DE ESPALHAMENTO (ScatterLayer): cada camada define
///    bioma, densidade, limites de altura/inclinação, AGRUPAMENTO por noise (manchas
///    naturais + áreas abertas), espaçamento mínimo, bloqueio de espaço (formações
///    grandes afastam o resto), escala/rotação aleatórias. Novos biomas = novas
///    camadas, sem tocar no algoritmo.
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
    public bool enableDesert = true;      // Deserto Rochoso (quente + seco)

    [Header("Tamanho das manchas de bioma (metros aprox.)")]
    [SerializeField] float biomePatchSize = 700f;   // menor = biomas mais próximos uns dos outros

    [Header("Cobertura da Floresta")]
    [Tooltip("Com vários biomas: 0 = floresta rara (só manchas úmidas) · 1 = floresta dominante. " +
             "Com SÓ a floresta marcada (Campos desligado): a floresta preenche tudo e o slider " +
             "controla as clareiras — 1 = quase nenhuma, valores menores = mais/maiores clareiras.")]
    [SerializeField, Range(0f, 1f)] float forestCoverage = 0.6f;

    [Header("Lagos (HDRP Water System)")]
    public bool enableLakes = true;
    [Tooltip("Altura da lâmina d'água no mundo (m). Bacias escavadas abaixo disso viram lago.")]
    public float waterLevel = 3.2f;
    [SerializeField] float lakeDepth = 7f;          // profundidade máxima da escavação
    [SerializeField] float lakePatchSize = 550f;    // tamanho das manchas de bacia (m)
    [Tooltip("Oásis no deserto: manchas ENORMES e raríssimas. Maior = oásis maiores e mais raros.")]
    [SerializeField] float oasisPatchSize = 1500f;
    [Tooltip("Lado do quad de água que segue o jogador (deve cobrir o raio de tiles).")]
    [SerializeField] float waterQuadSize = 1800f;
    [Tooltip("Escava um lago garantido a ~70 m do spawn do jogador (bom p/ testar).")]
    [SerializeField] bool guaranteedStartLake = true;
    [SerializeField] float startLakeRadius = 50f;

    Vector2 startLakeCenter;
    bool startLakeSet;

    [Header("Riachos (canal escavado + mesma lâmina d'água dos lagos)")]
    [Tooltip("Riachos serpenteando por bioma (padrão: só Floresta Antiga). O leito é escavado " +
             "abaixo do waterLevel, então a MESMA WaterSurface dos lagos preenche a água.")]
    public bool enableStreams = true;
    [Tooltip("Config por bioma. Vazio = padrão (só Floresta Antiga). Para expandir a outros " +
             "biomas no futuro, basta adicionar entradas aqui.")]
    [SerializeField] List<StreamSettings> streamSettings = new();
    [Tooltip("Ripples com direção fixa na água — sensação de correnteza nos riachos " +
             "(deriva sutil também nos lagos).")]
    [SerializeField] bool streamFlowRipples = true;
    [SerializeField] float streamRippleSpeed = 3.5f;

    [Header("Vegetação da Floresta/Campos (ALP)")]
    public GameObject[] treePrefabs;            // Floresta Antiga (+ raras nos Campos)
    public GameObject[] bushPrefabs;            // arbustos: floresta densa + campos esparsos
    public GameObject[] grassPrefabs;           // graminhas verdes
    [SerializeField] int forestVegetationPerTile = 600;   // floresta BEM densa
    [SerializeField, Range(0f, 1f)] float bushShare = 0.25f;
    [SerializeField] int grassPerTile = 500;

    [Header("Deserto Rochoso (RockyDesert)")]
    [Tooltip("Formações grandes de penhasco (SM_RockSide_*) — agrupadas em afloramentos, com collider.")]
    public GameObject[] desertFormationPrefabs;
    [Tooltip("Pedras médias (SM_Small_Rock_2/3/4) — ao redor dos afloramentos.")]
    public GameObject[] desertRockPrefabs;
    [Tooltip("Seixos/pedrinhas (SM_Small_Rock_1/B1/B2) — preenchimento fino do chão.")]
    public GameObject[] desertPebblePrefabs;
    [Tooltip("Árvores mortas (SM_Tree_*) — marcos raríssimos, aparecem no minimapa.")]
    public GameObject[] desertTreePrefabs;
    [Tooltip("Tufos de grama seca (SM_DeadGrass_*) — manchas de vegetação nas áreas abertas.")]
    public GameObject[] desertGrassPrefabs;
    [Tooltip("Multiplicador geral de densidade do deserto (1 = calibrado pela Demo Scene do pacote).")]
    [SerializeField, Range(0.1f, 2f)] float desertDensity = 1f;

    [Header("Camadas de espalhamento (vazio = padrão gerado dos arrays acima)")]
    [Tooltip("Controle fino da distribuição. Deixe vazio para usar o conjunto padrão " +
             "(floresta + campos + deserto). Cada camada = um 'tipo' de objeto com suas regras.")]
    [SerializeField] List<ScatterLayer> scatterLayers = new();

    [Header("Texturas do chão (auto-preenchidas no editor)")]
    public Texture2D grassDiffuse; public Texture2D grassNormal; public Texture2D grassMask;
    public Texture2D forestDiffuse; public Texture2D forestNormal; public Texture2D forestMask;
    public Texture2D rockDiffuse; public Texture2D rockNormal; public Texture2D rockMask;
    public Texture2D snowDiffuse; public Texture2D snowNormal; public Texture2D snowMask;
    public TerrainLayer sandLayer;        // Deserto: areia (RockyDesert/Terrain_Sand)
    public TerrainLayer desertRockLayer;  // Deserto: rocha de encosta (RockyDesert/Terrain_Rock)
    [SerializeField] float groundTextureTile = 12f;

    public static InfiniteTerrain Instance { get; private set; }

    /// <summary>Mundo tem lagos? (DragonController/FoodSpawner consultam.)</summary>
    public bool HasLakes => enableLakes;
    /// <summary>Altura da lâmina d'água (m de mundo).</summary>
    public float WaterLevel => waterLevel;

    [Header("Cores dos biomas (fallback)")]
    [SerializeField] Color grassColor = new(0.42f, 0.55f, 0.25f);
    [SerializeField] Color forestColor = new(0.22f, 0.38f, 0.16f);
    [SerializeField] Color rockColor = new(0.45f, 0.42f, 0.4f);
    [SerializeField] Color snowColor = new(0.92f, 0.94f, 0.97f);
    [SerializeField] Color sandColor = new(0.80f, 0.69f, 0.46f);
    [SerializeField] Color desertRockColor = new(0.62f, 0.50f, 0.38f);

    // ================================================== CAMADAS DE ESPALHAMENTO
    public enum Biome { Campos, Floresta, Montanha, Tundra, Deserto }

    /// <summary>
    /// Uma "espécie" de objeto espalhado no mundo e todas as suas regras de
    /// distribuição. O gerador processa as camadas NA ORDEM: coloque primeiro as
    /// grandes (blockRadius) para as pequenas respeitarem o espaço delas.
    /// </summary>
    [Serializable]
    public class ScatterLayer
    {
        public string name = "Camada";
        public Biome biome = Biome.Campos;
        public GameObject[] prefabs;

        [Tooltip("Pontos sorteados por tile (antes dos filtros)")]
        [Min(0)] public int attemptsPerTile = 100;
        [Tooltip("Peso mínimo do bioma para aparecer (0.4 = só razoavelmente dentro dele)")]
        [Range(0f, 1f)] public float minBiomeWeight = 0.4f;
        [Tooltip("Chance base por ponto — a densidade final ainda cai perto da borda do bioma")]
        [Range(0f, 1f)] public float density = 0.5f;

        [Tooltip("Escala mín/máx (sorteio com viés para as menores)")]
        public Vector2 scaleRange = new(0.8f, 1.2f);
        [Tooltip("Variação largura≠altura para quebrar silhuetas repetidas")]
        [Range(0f, 0.4f)] public float aspectJitter = 0.08f;

        [Tooltip("Inclinação mín/máx do chão (0 = plano). Ex.: formações aceitam encosta")]
        public float minSlope = 0f;
        public float maxSlope = 0.5f;
        [Tooltip("Faixa de ALTURA do mundo em metros (x = mín, y = máx)")]
        public Vector2 heightRange = new(-1000f, 1000f);

        [Tooltip("0 = espalhado uniforme · 1 = só dentro das manchas do noise de agrupamento")]
        [Range(0f, 1f)] public float clusterStrength = 0f;
        [Tooltip("Tamanho aproximado das manchas de agrupamento (metros)")]
        public float clusterSize = 90f;
        [Tooltip("Camadas com o MESMO grupo compartilham as manchas (pedras junto de formações). -1 = grupo próprio")]
        public int clusterGroup = -1;

        [Tooltip("Distância mínima entre instâncias DESTA camada (0 = livre)")]
        public float minSpacing = 0f;
        [Tooltip("> 0: reserva um raio que bloqueia camadas SEGUINTES (formações grandes)")]
        public float blockRadius = 0f;
        [Tooltip("> 0: mantém esta distância extra de todo espaço bloqueado")]
        public float avoidBlockers = 0f;

        [Tooltip("Instâncias entram no minimapa (árvores/marcos)")]
        public bool landmark = false;

        [NonSerialized] public int protoBase;   // offset no array de TreePrototypes
        [NonSerialized] public int protoCount;
    }

    /// <summary>
    /// Regras de riacho de UM bioma. O traçado é a curva de nível 0.5 de um Perlin
    /// em coordenadas de mundo (linhas sinuosas, contínuas, sem costura entre tiles),
    /// com largura modulada por um 2º noise e presença regional por um 3º.
    /// Tudo multiplicado pelo peso do bioma: na borda o riacho afina, seca e some.
    /// </summary>
    [Serializable]
    public class StreamSettings
    {
        public string name = "Riachos da Floresta";
        public Biome biome = Biome.Floresta;
        public bool enabled = true;

        [Tooltip("Escala do meandro (m) — maior = curvas mais largas e riachos mais afastados")]
        public float courseSize = 430f;
        [Tooltip("Meia-largura do canal em unidades de noise (~0.022 ≈ canal de 8–18 m)")]
        [Range(0.008f, 0.06f)] public float channelWidth = 0.022f;
        [Tooltip("Profundidade do leito abaixo do waterLevel (m)")]
        public float depth = 1.6f;
        [Tooltip("Altura do fundo do vale acima do waterLevel (m)")]
        public float bankHeight = 1.2f;
        [Tooltip("Largura do vale relativa ao canal (encostas suaves até o leito)")]
        public float valleyWidthMul = 3.2f;
        [Tooltip("Fração aproximada do bioma com riachos (1 = bioma inteiro)")]
        [Range(0f, 1f)] public float density = 0.6f;
        [Tooltip("Tamanho das regiões com/sem riachos (m)")]
        public float regionSize = 1400f;
        [Tooltip("Peso do bioma a partir do qual o riacho aparece em força total")]
        [Range(0f, 1f)] public float fadeStart = 0.55f;
        [Tooltip("Peso do bioma abaixo do qual não existe riacho nenhum")]
        [Range(0f, 1f)] public float fadeEnd = 0.25f;

        [NonSerialized] public Vector2 offCourse, offWidth, offRegion; // da seed (BuildStreamSetup)
    }

    readonly Dictionary<Vector2Int, Terrain> tiles = new();
    readonly Dictionary<Vector2Int, List<Vector2>> tileTrees = new(); // XZ das árvores (minimapa)
    readonly Queue<Vector2Int> buildQueue = new();
    readonly HashSet<Vector2Int> pending = new();

    TerrainLayer[] layers;
    Material terrainMat;
    TreePrototype[] prototypes;                 // união dos prefabs de todas as camadas
    List<ScatterLayer> activeLayers;            // scatterLayers ou o padrão
    List<StreamSettings> activeStreams;         // streamSettings habilitados ou o padrão
    readonly Dictionary<int, Vector2> clusterOffsets = new();
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

        // Ordem dos canais do alphamap:
        // 0 grama · 1 floresta · 2 rocha de montanha · 3 neve · 4 areia · 5 rocha do deserto
        layers = new[]
        {
            MakeLayer(grassDiffuse, grassNormal, grassMask, grassColor, groundTextureTile),
            MakeLayer(forestDiffuse, forestNormal, forestMask, forestColor, groundTextureTile),
            MakeLayer(rockDiffuse, rockNormal, rockMask, rockColor, groundTextureTile * 1.6f),
            MakeLayer(snowDiffuse, snowNormal, snowMask, snowColor, groundTextureTile),
            sandLayer != null ? sandLayer
                              : MakeLayer(null, null, null, sandColor, groundTextureTile),
            desertRockLayer != null ? desertRockLayer
                              : MakeLayer(null, null, null, desertRockColor, groundTextureTile * 1.6f)
        };

        BuildScatterSetup();
        BuildStreamSetup();
    }

    void Start()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player == null) { enabled = false; return; }

        // lago garantido perto do spawn — definir ANTES do primeiro tile
        if (enableLakes && guaranteedStartLake)
        {
            startLakeCenter = new Vector2(player.position.x + 70f, player.position.z);
            startLakeSet = true;
        }

        EnsureWaterSurface();
        BuildTile(TileOf(player.position));     // tile inicial síncrono
        SnapPlayerToGround();
    }

    void Update()
    {
        if (player == null) return;
        UpdateWaterFollow();
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

    // ------------------------------------------------- SETUP DO ESPALHAMENTO
    void BuildScatterSetup()
    {
        activeLayers = scatterLayers != null && scatterLayers.Count > 0
            ? scatterLayers : DefaultLayers();

        // remove prefabs incompatíveis/vazios e monta o array único de prototypes.
        // UM prototype inválido quebra a renderização de TODOS os trees do tile,
        // então o filtro aqui é obrigatório.
        foreach (var l in activeLayers)
            if (l.prefabs != null)
                l.prefabs = Array.FindAll(l.prefabs, IsTreeCompatible);
        activeLayers.RemoveAll(l => l.prefabs == null || l.prefabs.Length == 0);

        var protos = new List<TreePrototype>();
        foreach (var l in activeLayers)
        {
            l.protoBase = protos.Count;
            l.protoCount = l.prefabs.Length;
            foreach (var p in l.prefabs)
                protos.Add(new TreePrototype { prefab = p });
        }
        prototypes = protos.ToArray();

        // offsets determinísticos por grupo de agrupamento (mesmo grupo = mesmas manchas)
        clusterOffsets.Clear();
        for (int i = 0; i < activeLayers.Count; i++)
        {
            var l = activeLayers[i];
            int g = l.clusterGroup >= 0 ? l.clusterGroup : 1000 + i;  // -1 = grupo próprio
            l.clusterGroup = g;
            if (!clusterOffsets.ContainsKey(g))
            {
                var r = new System.Random(seed * 31 + g * 977 + 13);
                clusterOffsets[g] = new Vector2(
                    (float)(r.NextDouble() * 8000.0 - 4000.0),
                    (float)(r.NextDouble() * 8000.0 - 4000.0));
            }
        }
    }

    /// <summary>
    /// Resolve as configs de riacho ativas e sorteia offsets de noise próprios por
    /// entrada (System.Random independente — não consome o rng dos offsets do
    /// terreno, então ligar/desligar riachos NÃO muda o resto do mundo).
    /// </summary>
    void BuildStreamSetup()
    {
        activeStreams = new List<StreamSettings>();
        if (!enableStreams) return;

        var list = streamSettings != null && streamSettings.Count > 0
            ? streamSettings : DefaultStreams();
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s == null || !s.enabled) continue;
            var r = new System.Random(seed * 131 + i * 613 + 7);
            float Off() => (float)(r.NextDouble() * 8000.0 - 4000.0);
            s.offCourse = new Vector2(Off(), Off());
            s.offWidth = new Vector2(Off(), Off());
            s.offRegion = new Vector2(Off(), Off());
            activeStreams.Add(s);
        }
    }

    /// <summary>Padrão de riachos: decisão de design — SÓ na Floresta Antiga.</summary>
    static List<StreamSettings> DefaultStreams() => new() { new StreamSettings() };

    /// <summary>
    /// Conjunto padrão de camadas — Floresta/Campos como sempre foram, e o Deserto
    /// calibrado pela Demo Scene do RockyDesert (~12.5k seixos, ~5.4k pedras,
    /// ~1.9k gramas secas, ~780 formações e só 8 árvores mortas por km²,
    /// escalas 0.2–1.2 — aqui em manchas: afloramentos rochosos + areia aberta).
    /// </summary>
    List<ScatterLayer> DefaultLayers()
    {
        float d = desertDensity;
        int treeAttempts = Mathf.RoundToInt(forestVegetationPerTile * (1f - bushShare));
        int bushAttempts = forestVegetationPerTile - treeAttempts;

        return new List<ScatterLayer>
        {
            // ---------------- FLORESTA ANTIGA (densa) ----------------
            new()
            {
                name = "Floresta — árvores", biome = Biome.Floresta, prefabs = treePrefabs,
                attemptsPerTile = treeAttempts, minBiomeWeight = 0.4f, density = 1f,
                scaleRange = new Vector2(0.85f, 1.4f), aspectJitter = 0.1f,
                maxSlope = 0.5f, landmark = true
            },
            new()
            {
                name = "Floresta — arbustos", biome = Biome.Floresta, prefabs = bushPrefabs,
                attemptsPerTile = bushAttempts, minBiomeWeight = 0.4f, density = 1f,
                scaleRange = new Vector2(0.8f, 1.2f), maxSlope = 0.5f
            },
            new()
            {
                name = "Floresta — graminhas", biome = Biome.Floresta, prefabs = grassPrefabs,
                attemptsPerTile = Mathf.RoundToInt(grassPerTile * 0.6f), minBiomeWeight = 0.4f,
                density = 1f, scaleRange = new Vector2(0.9f, 1.6f), maxSlope = 0.5f
            },

            // ---------------- CAMPOS (esparsos) ----------------
            new()
            {
                name = "Campos — árvores isoladas", biome = Biome.Campos, prefabs = treePrefabs,
                attemptsPerTile = 10, minBiomeWeight = 0.5f, density = 0.9f,
                scaleRange = new Vector2(0.85f, 1.4f), maxSlope = 0.5f,
                minSpacing = 18f, landmark = true
            },
            new()
            {
                name = "Campos — arbustos", biome = Biome.Campos, prefabs = bushPrefabs,
                attemptsPerTile = 20, minBiomeWeight = 0.5f, density = 0.9f,
                scaleRange = new Vector2(0.8f, 1.2f), maxSlope = 0.5f, minSpacing = 8f
            },
            new()
            {
                name = "Campos — graminhas", biome = Biome.Campos, prefabs = grassPrefabs,
                attemptsPerTile = grassPerTile, minBiomeWeight = 0.35f, density = 1f,
                scaleRange = new Vector2(0.9f, 1.6f), maxSlope = 0.5f
            },

            // ---------------- DESERTO ROCHOSO ----------------
            // Calibrado com os DADOS REAIS da Demo (por tile de 250 m): ~63 formações
            // em escala 2–7 encravadas nos paredões, ~22 pedras médias, ~1450 seixos,
            // ~150 tufos GRANDES de grama seca e árvore morta raríssima.
            // Ordem importa: formações primeiro (reservam espaço), detalhe fino por último.
            new()
            {
                name = "Deserto — paredões", biome = Biome.Deserto,
                prefabs = desertFormationPrefabs,
                attemptsPerTile = Mathf.RoundToInt(260 * d), minBiomeWeight = 0.4f, density = 0.7f,
                scaleRange = new Vector2(1.8f, 5.5f), aspectJitter = 0.2f,
                minSlope = 0.35f, maxSlope = 99f,       // SÓ nas encostas: revestem os cânions
                clusterStrength = 0.35f, clusterSize = 130f, clusterGroup = 0,
                minSpacing = 5f, blockRadius = 5f
            },
            new()
            {
                name = "Deserto — afloramentos", biome = Biome.Deserto,
                prefabs = desertFormationPrefabs,
                attemptsPerTile = Mathf.RoundToInt(90 * d), minBiomeWeight = 0.45f, density = 0.5f,
                scaleRange = new Vector2(1f, 3f), aspectJitter = 0.15f,
                maxSlope = 0.35f,                       // grupos de rocha no piso aberto
                clusterStrength = 0.85f, clusterSize = 140f, clusterGroup = 0,
                minSpacing = 6f, blockRadius = 6f
            },
            new()
            {
                name = "Deserto — pedras médias", biome = Biome.Deserto,
                prefabs = desertRockPrefabs,
                attemptsPerTile = Mathf.RoundToInt(60 * d), minBiomeWeight = 0.4f, density = 0.6f,
                scaleRange = new Vector2(0.7f, 1.3f), aspectJitter = 0.2f, maxSlope = 0.6f,
                clusterStrength = 0.5f, clusterSize = 130f, clusterGroup = 0,   // junto das formações
                minSpacing = 2.5f
            },
            new()
            {
                name = "Deserto — seixos", biome = Biome.Deserto,
                prefabs = desertPebblePrefabs,
                attemptsPerTile = Mathf.RoundToInt(2300 * d), minBiomeWeight = 0.3f, density = 0.8f,
                scaleRange = new Vector2(0.25f, 1.4f), aspectJitter = 0.25f, maxSlope = 0.7f,
                clusterStrength = 0.3f, clusterSize = 130f, clusterGroup = 0    // mais densos na zona rochosa
            },
            new()
            {
                name = "Deserto — grama seca", biome = Biome.Deserto,
                prefabs = desertGrassPrefabs,
                attemptsPerTile = Mathf.RoundToInt(260 * d), minBiomeWeight = 0.4f, density = 0.7f,
                scaleRange = new Vector2(1.4f, 3f), maxSlope = 0.5f,   // tufos GRANDES (demo: mediana 2.3)
                clusterStrength = 0.55f, clusterSize = 80f,            // manchas nas áreas abertas
                avoidBlockers = 1f
            },
            new()
            {
                name = "Deserto — árvores mortas", biome = Biome.Deserto,
                prefabs = desertTreePrefabs,
                attemptsPerTile = 4, minBiomeWeight = 0.55f, density = 0.5f,
                scaleRange = new Vector2(1.2f, 2f), maxSlope = 0.35f,
                minSpacing = 60f, avoidBlockers = 3f, landmark = true   // marcos raros (8/km² na demo)
            },
        };
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
        //      6 canais: grama/floresta/rocha de montanha/neve/areia/rocha do deserto
        var alphas = new float[alphamapRes, alphamapRes, 6];
        float aStep = tileSize / (alphamapRes - 1);
        for (int z = 0; z < alphamapRes; z++)
            for (int x = 0; x < alphamapRes; x++)
            {
                float wx = ox + x * aStep, wz = oz + z * aStep;
                BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out float cold, out float desert);

                float h01 = HeightAt(wx, wz) / maxHeight;
                float slope = SlopeAt(wx, wz);
                float slopeRock = Mathf.Clamp01(slope * 2.2f - 0.35f);
                float rock = mount + slopeRock * (1f - desert);           // encostas fora do deserto
                float snow = cold + Mathf.Clamp01((h01 - 0.75f) * 4f);    // neve só em picos altos/tundra
                float dRock = desert * Mathf.Clamp01(slope * 2.6f - 0.3f); // penhascos de arenito
                float sand = Mathf.Max(0f, desert - dRock);

                // riachos: leito pinta rocha molhada, margens do vale pintam terra
                float sBed = 0f, sBank = 0f;
                StreamPaintMasks(wx, wz, plains, forest, mount, cold, desert, ref sBed, ref sBank);

                float g = plains, f = forest + sBank * 1.6f, r = rock + sBed * 1.4f, s = snow;
                float sum = g + f + r + s + sand + dRock + 0.0001f;
                alphas[z, x, 0] = g / sum;
                alphas[z, x, 1] = f / sum;
                alphas[z, x, 2] = r / sum;
                alphas[z, x, 3] = s / sum;
                alphas[z, x, 4] = sand / sum;
                alphas[z, x, 5] = dRock / sum;
            }
        td.SetAlphamaps(0, 0, alphas);

        // ---- vegetação/props: camadas de espalhamento (tree instances: o detail
        //      system do Unity não aceita prefabs com LODGroup e falhava em silêncio)
        var treeXZ = new List<Vector2>();
        if (prototypes != null && prototypes.Length > 0)
        {
            td.treePrototypes = prototypes;
            td.RefreshPrototypes();
            ScatterTile(coord, td, ox, oz, treeXZ);
        }
        tileTrees[coord] = treeXZ;

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

    // ------------------------------------------------------ ÁGUA (HDRP)
    UnityEngine.Rendering.HighDefinition.WaterSurface lakeSurface;

    /// <summary>
    /// UMA WaterSurface (tipo Pool, quad) para o mundo todo, seguindo o jogador.
    /// Só as bacias escavadas ficam abaixo do waterLevel, então a lâmina só
    /// aparece nos lagos — o resto fica oculto pelo terreno.
    /// Requer Water habilitado no HDRP Asset (Tools > Everwyrm > Água).
    /// </summary>
    void EnsureWaterSurface()
    {
        if ((!enableLakes && !StreamsActive) || lakeSurface != null) return;

        var go = new GameObject("Lake Water (HDRP)");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0f, waterLevel, 0f);
        go.transform.localScale = new Vector3(waterQuadSize, 1f, waterQuadSize);

        lakeSurface = go.AddComponent<UnityEngine.Rendering.HighDefinition.WaterSurface>();
        lakeSurface.surfaceType = UnityEngine.Rendering.HighDefinition.WaterSurfaceType.Pool;
        lakeSurface.geometryType = UnityEngine.Rendering.HighDefinition.WaterGeometryType.Quad;

        // volume subaquático: nevoeiro/efeito quando a câmera mergulha.
        // Escala do transform: x/z = 1800 (quad), y = 1 — então a caixa precisa
        // dos metros de profundidade no próprio size.y.
        var volume = go.AddComponent<BoxCollider>();
        volume.isTrigger = true;
        volume.center = new Vector3(0f, -5.5f, 0f);
        volume.size = new Vector3(1f, 12f, 1f);   // x/z cobrem o quad via escala
        lakeSurface.underWater = true;
        lakeSurface.volumeBounds = volume;

        // correnteza visual dos riachos: ripples com direção própria em vez de
        // herdada (deriva sutil também nos lagos — aceitável). Upgrade futuro:
        // ripplesCurrentMap regional apontando ao longo de cada curso.
        if (streamFlowRipples && StreamsActive)
        {
            lakeSurface.ripplesMotionMode =
                UnityEngine.Rendering.HighDefinition.WaterPropertyOverrideMode.Custom;
            lakeSurface.ripplesOrientationValue = 25f;
            lakeSurface.ripplesWindSpeed = streamRippleSpeed;
        }
    }

    void UpdateWaterFollow()
    {
        if (lakeSurface == null || player == null) return;
        // recentraliza por passo de tile (evita jitter de precisão longe da origem)
        float sx = Mathf.Round(player.position.x / tileSize) * tileSize;
        float sz = Mathf.Round(player.position.z / tileSize) * tileSize;
        lakeSurface.transform.position = new Vector3(sx, waterLevel, sz);
    }

    // ------------------------------------------------ MOTOR DE ESPALHAMENTO
    /// <summary>
    /// Processa todas as ScatterLayers de um tile. Determinístico por (seed, tile).
    /// Filtros na ordem do mais barato ao mais caro: bioma → agrupamento/densidade
    /// → inclinação/altura → espaçamento/bloqueio.
    /// </summary>
    void ScatterTile(Vector2Int coord, TerrainData td, float ox, float oz, List<Vector2> treeXZ)
    {
        var rng = new System.Random(seed ^ (coord.x * 73856093) ^ (coord.y * 19349663));
        var instances = new List<TreeInstance>();
        var blockers = new List<Vector3>();          // x,z = posição · y = raio
        var spacingHash = new Dictionary<long, List<Vector2>>();

        foreach (var layer in activeLayers)
        {
            if (layer.protoCount == 0) continue;
            spacingHash.Clear();
            float cell = Mathf.Max(layer.minSpacing, 0.001f);
            Vector2 cOff = clusterOffsets[layer.clusterGroup];

            for (int i = 0; i < layer.attemptsPerTile; i++)
            {
                float nx = (float)rng.NextDouble(), nz = (float)rng.NextDouble();
                double roll = rng.NextDouble();      // consumir SEMPRE mantém o determinismo
                float wx = ox + nx * tileSize, wz = oz + nz * tileSize;

                // 1) peso do bioma — densidade cai suavemente rumo à borda
                BiomeWeights(wx, wz, out float bPl, out float bFo, out float bMo, out float bCo, out float bDe);
                float w = PickWeight(layer.biome, bPl, bFo, bMo, bCo, bDe);
                if (w < layer.minBiomeWeight) continue;
                float p = layer.density *
                          Mathf.Sqrt(Mathf.InverseLerp(layer.minBiomeWeight, 1f, w));

                // 2) agrupamento: manchas de noise → aglomerados naturais + áreas abertas
                if (layer.clusterStrength > 0f)
                {
                    float n = Mathf.PerlinNoise(wx / layer.clusterSize + cOff.x,
                                                wz / layer.clusterSize + cOff.y);
                    float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.72f, n));
                    p *= Mathf.Lerp(1f, mask, layer.clusterStrength);
                }
                if (roll > p) continue;

                // 2.5) riachos: nada dentro do canal nem nas margens do vale
                //      (barato — vem antes de SlopeAt, que custa 3x HeightAt)
                if (StreamExcluded(wx, wz, bPl, bFo, bMo, bCo, bDe)) continue;

                // 3) relevo
                float slope = SlopeAt(wx, wz);
                if (slope < layer.minSlope || slope > layer.maxSlope) continue;
                float h = HeightAt(wx, wz);
                if (h < layer.heightRange.x || h > layer.heightRange.y) continue;
                if ((enableLakes || StreamsActive) && h < waterLevel + 0.35f)
                    continue;   // nada dentro/na beira d'água

                // 4) distância de outros objetos
                var pos2 = new Vector2(wx, wz);
                if (layer.avoidBlockers > 0f && NearBlocker(blockers, pos2, layer.avoidBlockers))
                    continue;
                if (layer.minSpacing > 0f && !SpacingOk(spacingHash, pos2, cell, layer.minSpacing))
                    continue;

                // 5) coloca — escala com viés p/ menores, rotação livre, aspecto variado
                float t = Mathf.Pow((float)rng.NextDouble(), 1.6f);
                float scale = Mathf.Lerp(layer.scaleRange.x, layer.scaleRange.y, t);
                float aspect = 1f + ((float)rng.NextDouble() * 2f - 1f) * layer.aspectJitter;
                instances.Add(new TreeInstance
                {
                    // altura interpolada do PRÓPRIO heightmap: cravado no chão
                    position = new Vector3(nx, td.GetInterpolatedHeight(nx, nz) / maxHeight, nz),
                    prototypeIndex = layer.protoBase + rng.Next(layer.protoCount),
                    heightScale = scale,
                    widthScale = scale * aspect,
                    rotation = (float)(rng.NextDouble() * Math.PI * 2.0),
                    color = Color.white,
                    lightmapColor = Color.white
                });

                if (layer.blockRadius > 0f)
                    blockers.Add(new Vector3(wx, layer.blockRadius * scale, wz));
                if (layer.minSpacing > 0f)
                {
                    long key = SpacingKey(pos2, cell);
                    if (!spacingHash.TryGetValue(key, out var list))
                        spacingHash[key] = list = new List<Vector2>();
                    list.Add(pos2);
                }
                if (layer.landmark) treeXZ.Add(pos2);
            }
        }

        td.SetTreeInstances(instances.ToArray(), true);
    }

    static bool NearBlocker(List<Vector3> blockers, Vector2 pos, float extra)
    {
        foreach (var b in blockers)
        {
            float r = b.y + extra;
            float dx = b.x - pos.x, dz = b.z - pos.y;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return false;
    }

    static long SpacingKey(Vector2 pos, float cell) =>
        ((long)Mathf.FloorToInt(pos.x / cell) << 32) ^ (uint)Mathf.FloorToInt(pos.y / cell);

    static bool SpacingOk(Dictionary<long, List<Vector2>> hash, Vector2 pos, float cell, float spacing)
    {
        float s2 = spacing * spacing;
        int cx = Mathf.FloorToInt(pos.x / cell), cz = Mathf.FloorToInt(pos.y / cell);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                if (!hash.TryGetValue(key, out var list)) continue;
                foreach (var o in list)
                    if ((o - pos).sqrMagnitude < s2) return false;
            }
        return true;
    }

    /// <summary>
    /// Tree instances exigem MeshRenderer ou LODGroup na RAIZ do prefab
    /// (o EverwyrmAutoSetup reconstrói os prefabs do RockyDesert já corretos).
    /// </summary>
    static bool IsTreeCompatible(GameObject p)
    {
        if (p == null) return false;
        if (p.TryGetComponent<LODGroup>(out _) || p.TryGetComponent<MeshRenderer>(out _)) return true;
        Debug.LogWarning($"[InfiniteTerrain] '{p.name}' ignorado: prefab sem MeshRenderer/LODGroup " +
                         "na raiz não funciona como tree instance.");
        return false;
    }

    /// <summary>Peso [0..1] de um bioma específico num ponto do mundo.</summary>
    public float BiomeWeightOf(Biome biome, float wx, float wz)
    {
        BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out float cold, out float desert);
        return PickWeight(biome, plains, forest, mount, cold, desert);
    }

    static float PickWeight(Biome biome, float plains, float forest, float mount, float cold, float desert)
        => biome switch
        {
            Biome.Campos => plains,
            Biome.Floresta => forest,
            Biome.Montanha => mount,
            Biome.Tundra => cold,
            _ => desert,
        };

    // ------------------------------------------------------------- RIACHOS
    bool StreamsActive => enableStreams && activeStreams != null && activeStreams.Count > 0;

    /// <summary>Mundo tem riachos? (fauna/gameplay consultam.)</summary>
    public bool HasStreams => StreamsActive;

    /// <summary>
    /// Máscaras de UM riacho num ponto: retorna o canal [0..1] (0 = fora) e o vale
    /// (mais largo, encostas incluídas) em out. Função pura de (worldXZ, seed).
    /// Custo: 1–3 Perlin, com early-out fora do bioma e longe da curva de nível.
    /// </summary>
    float StreamMasks(float wx, float wz, float biomeW, StreamSettings s, out float valley)
    {
        valley = 0f;

        // fade pelo peso do bioma: força total dentro, nada fora (requisito de design)
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.fadeEnd, s.fadeStart, biomeW));
        if (fade <= 0.002f) return 0f;

        // curva de nível 0.5 do noise de curso = linha sinuosa contínua no mundo
        float n = Mathf.PerlinNoise(wx / s.courseSize + s.offCourse.x,
                                    wz / s.courseSize + s.offCourse.y);
        float d = Mathf.Abs(n - 0.5f);
        if (d >= s.channelWidth * 1.45f * s.valleyWidthMul) return 0f;   // longe do curso

        // regiões com/sem riachos (density) — manchas grandes, transição suave
        if (s.density < 0.999f)
        {
            float t = Mathf.Lerp(0.72f, 0.15f, s.density);
            float rg = Mathf.PerlinNoise(wx / s.regionSize + s.offRegion.x,
                                         wz / s.regionSize + s.offRegion.y);
            fade *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(t, t + 0.12f, rg));
            if (fade <= 0.002f) return 0f;
        }

        // largura respira ao longo do curso e AFINA junto com o fade do bioma —
        // na borda da floresta o riacho estreita antes de secar (nunca corte seco)
        float wn = Mathf.PerlinNoise(wx / (s.courseSize * 0.31f) + s.offWidth.x,
                                     wz / (s.courseSize * 0.31f) + s.offWidth.y);
        float halfW = s.channelWidth * Mathf.Lerp(0.55f, 1.45f, wn) * Mathf.Lerp(0.3f, 1f, fade);

        valley = (1f - Mathf.SmoothStep(0.25f, 1f, d / (halfW * s.valleyWidthMul))) * fade;
        return d < halfW ? (1f - Mathf.SmoothStep(0.3f, 1f, d / halfW)) * fade : 0f;
    }

    /// <summary>Força [0..1] do canal de riacho num ponto do mundo (0 = fora).</summary>
    public float StreamStrength(float wx, float wz)
    {
        if (!StreamsActive) return 0f;
        BiomeWeights(wx, wz, out float pl, out float fo, out float mo, out float co, out float de);
        float best = 0f;
        for (int i = 0; i < activeStreams.Count; i++)
        {
            var s = activeStreams[i];
            float bw = PickWeight(s.biome, pl, fo, mo, co, de);
            if (bw < s.fadeEnd) continue;
            float c = StreamMasks(wx, wz, bw, s, out _);
            if (c > best) best = c;
        }
        return best;
    }

    /// <summary>Há água de riacho neste ponto? (Fauna usa como fonte de água.)</summary>
    public bool IsStream(Vector3 worldPos) =>
        StreamStrength(worldPos.x, worldPos.z) > 0.45f &&
        HeightAt(worldPos.x, worldPos.z) < waterLevel;

    /// <summary>Ponto cai no canal ou nas margens do vale? (Exclusão do scatter.)</summary>
    bool StreamExcluded(float wx, float wz, float pl, float fo, float mo, float co, float de)
    {
        if (!StreamsActive) return false;
        for (int i = 0; i < activeStreams.Count; i++)
        {
            var s = activeStreams[i];
            float bw = PickWeight(s.biome, pl, fo, mo, co, de);
            if (bw < s.fadeEnd) continue;
            StreamMasks(wx, wz, bw, s, out float valley);
            if (valley > 0.35f) return true;
        }
        return false;
    }

    /// <summary>Leito/margens para o splatmap (máximo entre todos os riachos).</summary>
    void StreamPaintMasks(float wx, float wz, float pl, float fo, float mo, float co, float de,
                          ref float bed, ref float bank)
    {
        if (!StreamsActive) return;
        for (int i = 0; i < activeStreams.Count; i++)
        {
            var s = activeStreams[i];
            float bw = PickWeight(s.biome, pl, fo, mo, co, de);
            if (bw < s.fadeEnd) continue;
            float c = StreamMasks(wx, wz, bw, s, out float valley);
            bed = Mathf.Max(bed, c);
            bank = Mathf.Max(bank, Mathf.Clamp01(valley - c));
        }
    }

    void SnapPlayerToGround()
    {
        float h = HeightAt(player.position.x, player.position.z);
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.position = new Vector3(player.position.x, h + 1.5f, player.position.z);
        if (cc != null) cc.enabled = true;
    }

    /// <summary>Marcos (XZ do mundo) num raio — usado pelo minimapa.</summary>
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
        BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out float cold, out float desert);

        float hPlains = 6f + FBM(wx, wz, 0.008f, 2) * 8f;
        float hForest = 8f + FBM(wx, wz, 0.011f, 3) * 14f;
        float hTundra = 5f + FBM(wx, wz, 0.007f, 2) * 6f;

        // Deserto: perfil da Demo do RockyDesert — piso de cânion com dunas/ondulações
        // + MESETAS em DOIS níveis (~28 m e ~52 m) com paredões íngremes de borda
        // recortada (o jitter impede platôs redondos/repetitivos).
        float dFloor = 6f + FBM(wx, wz, 0.009f, 3) * 9f;
        float ripple = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx * 0.02f + oxT + 9.1f,
                                                             wz * 0.02f + ozT + 3.7f) - 1f);
        float mesaN = Mathf.PerlinNoise(wx * 0.0038f + oxM + 77.7f, wz * 0.0038f + ozM + 41.3f);
        float edgeJit = (FBM(wx, wz, 0.015f, 2) - 0.5f) * 0.10f;
        float tier1 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.54f, 0.62f, mesaN + edgeJit));
        float tier2 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.70f, 0.78f, mesaN + edgeJit * 0.6f));
        float hDesert = dFloor + ripple * 2.2f + tier1 * 28f + tier2 * 24f;

        float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx * 0.0035f + oxH, wz * 0.0035f + ozH) - 1f);
        float hMount = 14f + ridge * ridge * 100f + FBM(wx, wz, 0.02f, 2) * 10f;

        float h = plains * hPlains + forest * hForest + mount * hMount + cold * hTundra + desert * hDesert;

        // ---- LAGOS: bacias raras em terreno úmido (Campos/Floresta/Tundra).
        //      A bacia PUXA o chão para baixo da lâmina d'água (não só subtrai),
        //      então toda mancha vira lago de verdade, mesmo na Floresta alta.
        //      Margens suaves; deserto e montanha ficam secos.
        if (enableLakes)
        {
            float lakeBed = waterLevel - lakeDepth * 0.6f;
            float lk = Mathf.PerlinNoise(wx / lakePatchSize + oxT + 191.3f,
                                         wz / lakePatchSize + ozT + 67.9f);
            float basin = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.66f, 0.78f, lk));
            float wet = Mathf.Min(1f, plains + forest * 0.9f + cold * 0.7f);
            h = Mathf.Lerp(h, lakeBed, basin * wet);

            // OÁSIS: raríssimos no deserto (threshold alto num noise de manchas
            // enormes) — quando um existe, é um lago GRANDE no meio da areia.
            if (desert > 0.01f)
            {
                float oa = Mathf.PerlinNoise(wx / oasisPatchSize + oxM + 431.7f,
                                             wz / oasisPatchSize + ozM + 129.3f);
                // threshold altíssimo: achar um oásis é EVENTO — quilômetros de areia
                // entre um e outro. (Baixar o 0.86 torna mais comum.)
                float oasis = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 0.92f, oa));
                h = Mathf.Lerp(h, lakeBed, oasis * desert);
            }

            // lago garantido perto do spawn (independe de bioma — é p/ teste)
            if (startLakeSet)
            {
                float d = Vector2.Distance(new Vector2(wx, wz), startLakeCenter);
                float bowl = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(startLakeRadius * 0.55f, startLakeRadius, d));
                if (bowl > 0f) h = Mathf.Lerp(h, lakeBed, bowl);
            }
        }

        // ---- RIACHOS: canais por curva de nível de noise, por bioma (padrão: só
        //      Floresta Antiga). Vale raso até pertinho da água + canal abaixo do
        //      waterLevel — a MESMA lâmina dos lagos preenche o leito, sem nova
        //      superfície. Min() nunca LEVANTA terreno (desaguar em lago é seguro).
        //      Depois dos lagos de propósito: a bacia já escavada tem prioridade.
        if (enableStreams && activeStreams != null)
            for (int i = 0; i < activeStreams.Count; i++)
            {
                var s = activeStreams[i];
                float bw = PickWeight(s.biome, plains, forest, mount, cold, desert);
                if (bw < s.fadeEnd) continue;               // early-out: fora do bioma
                float channel = StreamMasks(wx, wz, bw, s, out float valley);
                if (valley <= 0f) continue;
                h = Mathf.Min(h, Mathf.Lerp(h, waterLevel + s.bankHeight, valley));
                if (channel > 0f)
                    h = Mathf.Min(h, Mathf.Lerp(h, waterLevel - s.depth, channel));
            }
        return h;
    }

    /// <summary>Pesos normalizados dos 5 biomas em um ponto do mundo (respeita os toggles).</summary>
    public void BiomeWeights(float wx, float wz,
        out float plains, out float forest, out float mount, out float cold, out float desert)
    {
        float f = 1f / Mathf.Max(100f, biomePatchSize);
        float temp = Mathf.PerlinNoise(wx * f + oxT, wz * f + ozT);
        float moist = Mathf.PerlinNoise(wx * f * 1.15f + oxM, wz * f * 1.15f + ozM);
        float mountains = Mathf.PerlinNoise(wx * f * 1.4f + oxD, wz * f * 1.4f + ozD);

        mount = enableMountains
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.72f, mountains)) : 0f;
        cold = enableTundra
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.78f, 1f - temp)) * (1f - mount) : 0f;
        float free = (1f - mount) * (1f - cold);   // terreno plano restante (nem montanha nem tundra)

        // Deserto = quente + seco. Fica fora de montanha/tundra por causa do 'free'.
        float desertNiche = enableDesert
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.70f, temp)) *
              Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.50f, 0.68f, 1f - moist)) : 0f;

        forest = 0f; plains = 0f; desert = 0f;

        if (enablePlains)
        {
            // MODO NORMAL: Campos = fundo; Floresta (úmido) e Deserto (quente/seco) em manchas.
            // forestCoverage desliza o limiar: mais cobertura => floresta com menos umidade.
            float fLo = Mathf.Lerp(0.60f, 0.28f, forestCoverage);
            float forestNiche = enableForest
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fLo, fLo + 0.14f, moist)) : 0f;
            desertNiche *= (1f - forestNiche);                 // floresta vence a sobreposição úmida
            forest = free * forestNiche;
            desert = free * desertNiche;
            plains = free * Mathf.Max(0f, 1f - forestNiche - desertNiche);
        }
        else if (enableForest)
        {
            // MODO FLORESTA-FUNDO: floresta preenche tudo e só sobram CLAREIRAS de grama pequenas
            // (mata os "campos imensos"). O Deserto, se ligado, recorta as zonas quentes/secas antes.
            // forestCoverage controla a raridade/tamanho das clareiras (1 = quase nenhuma).
            float clg = Mathf.PerlinNoise(wx * f * 3f + oxD + 313.7f, wz * f * 3f + ozD + 217.1f);
            float clgLo = Mathf.Lerp(0.55f, 0.82f, forestCoverage);
            float clearing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clgLo, clgLo + 0.10f, clg));
            float land = free * (1f - desertNiche);
            forest = land * (1f - clearing);   // floresta cobre o resto
            plains = land * clearing;          // clareira de grama (pequena)
            desert = free * desertNiche;
        }
        else if (enableDesert)
        {
            // MODO DESERTO-FUNDO: a areia preenche tudo; a rocha das encostas dá a variação.
            desert = free;
        }
        else
        {
            // só Montanha/Tundra (ou nada marcado): a sobra vira grama.
            plains = free;
        }

        // normaliza (se desligar tudo, vira Campos/grama)
        float sum = plains + forest + mount + cold + desert;
        if (sum < 0.001f) { plains = 1f; forest = mount = cold = desert = 0f; return; }
        plains /= sum; forest /= sum; mount /= sum; cold /= sum; desert /= sum;
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
    // Auto-preenche assets dos pacotes (ALP + RockyDesert) — funciona sem rodar menus.
    void OnValidate()
    {
        if (Application.isPlaying) return;

        // ---- ALP (floresta/campos)
        if (grassDiffuse == null)
        {
            const string G = "Assets/ALP_Assets/Nature Package - Forest Environment_/GroundTextures/";
            const string V = "Assets/ALP_Assets/Nature Package - Forest Environment_/_Vegetation/HDRP/HDRP_Prefabs/";

            grassDiffuse = T(G + "Grass_001.tif"); grassNormal = T(G + "Grass_001_N.png"); grassMask = T(G + "Grass_001_mask.png");
            forestDiffuse = T(G + "Ground01.tif"); forestNormal = T(G + "Ground01_N.png"); forestMask = T(G + "Ground01_mask.png");
            rockDiffuse = T(G + "Ground02.tif"); rockNormal = T(G + "Ground02_N.png"); rockMask = T(G + "Ground02_mask.png");

            if (Empty(treePrefabs))
                treePrefabs = new[] { P(V + "ForestTree01_HDRP.prefab"), P(V + "ForestTree02_HDRP.prefab"),
                                      P(V + "ForestTree03_HDRP.prefab"), P(V + "ForestTree04_HDRP.prefab") };
            if (Empty(bushPrefabs))
                bushPrefabs = new[] { P(V + "ForestBush01_HDRP.prefab"), P(V + "ForestBush02_HDRP.prefab"),
                                      P(V + "ForestBush03_HDRP.prefab"), P(V + "ForestBush04_HDRP.prefab") };
            if (Empty(grassPrefabs))
                grassPrefabs = new[] { P(V + "FlowerGrass01_HDRP.prefab"), P(V + "FlowerGrass02_HDRP.prefab"),
                                       P(V + "GrassPlant02_HDRP.prefab"), P(V + "GrassPlant03_HDRP.prefab") };
            UnityEditor.EditorUtility.SetDirty(this);
        }

        // ---- RockyDesert (deserto). Prefere os prefabs CONVERTIDOS para HDRP
        //      (Tools > Everwyrm > Deserto — os originais são Built-in e ficam magenta).
        const string Conv = "Assets/Everwyrm/Desert/";
        const string Orig = "Assets/RockyDesert/Prefabs/";
        string D(string name) => System.IO.File.Exists(Conv + name + ".prefab")
            ? Conv + name + ".prefab" : Orig + name + ".prefab";

        bool dirty = false;
        if (Empty(desertFormationPrefabs))
        {
            desertFormationPrefabs = new[] { P(D("SM_RockSide_A1")), P(D("SM_Rock_Side-A2")) };
            dirty = true;
        }
        if (Empty(desertRockPrefabs))
        {
            desertRockPrefabs = new[] { P(D("SM_Small_Rock_2")), P(D("SM_Small_Rock_3")),
                                        P(D("SM_Small_Rock_4")) };
            dirty = true;
        }
        if (Empty(desertPebblePrefabs))
        {
            desertPebblePrefabs = new[] { P(D("SM_Small_Rock_1")), P(D("SM_Small_Rock_B1")),
                                          P(D("SM_Small_Rock_B2")) };
            dirty = true;
        }
        if (Empty(desertTreePrefabs))
        {
            desertTreePrefabs = new[] { P(D("SM_Tree_1")), P(D("SM_Tree_A")), P(D("SM_Tree_B")) };
            dirty = true;
        }
        if (Empty(desertGrassPrefabs))
        {
            desertGrassPrefabs = new[] { P(D("SM_DeadGrass_A")), P(D("SM_DeadGrass_A1")),
                                         P(D("SM_DeadGrass_A2")) };
            dirty = true;
        }
        if (sandLayer == null)
        {
            sandLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                "Assets/RockyDesert/Terrain/Terrain_Sand.terrainlayer");
            dirty = true;
        }
        if (desertRockLayer == null)
        {
            desertRockLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                "Assets/RockyDesert/Terrain/Terrain_Rock.terrainlayer");
            dirty = true;
        }
        if (dirty) UnityEditor.EditorUtility.SetDirty(this);

        static Texture2D T(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        static GameObject P(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
        static bool Empty(GameObject[] a) => a == null || a.Length == 0 || a[0] == null;
    }
#endif
}
