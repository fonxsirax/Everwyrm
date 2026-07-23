using System;
using System.Collections;
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
    [SerializeField] int alphamapRes = 128;     // 128 => ~2 m/pixel de blend no tile de 250 m
    [SerializeField] float maxHeight = 130f;
    [SerializeField] int loadRadius = 2;        // 2 => 5x5 tiles (~625 m de vista)
    [Tooltip("Orçamento de CPU por frame (ms) para construir tiles. A construção é " +
             "FATIADA: um tile nasce ao longo de vários frames sem estourar o frame " +
             "(um tile inteiro num frame só custava ~114 ms = stutter visível).")]
    [SerializeField] float buildBudgetMs = 4f;

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
    public float waterLevel = 5f;
    [SerializeField] float lakeDepth = 7f;          // profundidade máxima da escavação
    [SerializeField] float lakePatchSize = 550f;    // tamanho das manchas de bacia (m)
    [Tooltip("Oásis no deserto: manchas ENORMES e raríssimas. Maior = oásis maiores e mais raros.")]
    [SerializeField] float oasisPatchSize = 1500f;
    [Tooltip("Lado do quad de água que segue o jogador (deve cobrir o raio de tiles).")]
    [SerializeField] float waterQuadSize = 1800f;
    [Tooltip("Escava um lago garantido a ~70 m do spawn do jogador (bom p/ testar).")]
    [SerializeField] bool guaranteedStartLake = true;
    [SerializeField] float startLakeRadius = 50f;
    [Tooltip("Limpidez da água: distância de absorção da luz (m). Maior = mais límpida, fundo de areia visível (oásis); default HDRP era 5 (poço escuro).")]
    [SerializeField] float waterClarity = 12f;
    [Tooltip("Cor do corpo d'água nas partes fundas (espalhamento).")]
    [SerializeField] Color waterScatteringColor = new(0.04f, 0.24f, 0.28f);
    [Tooltip("Distorção máxima da refração (m) — o 'tremido' do fundo sob as ondulações.")]
    [SerializeField] float waterRefractionDistance = 2.5f;

    Vector2 startLakeCenter;
    bool startLakeSet;

    [Header("Riachos (canal escavado + mesma lâmina d'água dos lagos)")]
    [Tooltip("Riachos serpenteando por bioma (padrão: só Floresta Antiga). O leito é escavado " +
             "abaixo do waterLevel, então a MESMA WaterSurface dos lagos preenche a água.")]
    public bool enableStreams = true;
    [Tooltip("Ripples com direção fixa na água — sensação de correnteza nos riachos " +
             "(deriva sutil também nos lagos).")]
    [SerializeField] bool streamFlowRipples = true;
    [SerializeField] float streamRippleSpeed = 3.5f;

    [Header("Gelo (Tundra — água congelada, caminhável)")]
    [Tooltip("Na Tundra a água congela: uma placa de gelo sólida (com collider) cobre lagos e " +
             "riachos no waterLevel — impossível nadar; o dragão pousa e anda por cima.")]
    public bool freezeTundraWater = true;
    [Tooltip("Peso mínimo do bioma frio p/ congelar (corte seco no gameplay: nadar/pousar).")]
    [SerializeField, Range(0f, 1f)] float frozenColdThreshold = 0.6f;
    [Tooltip("Material da placa (Tools > Everwyrm > Gelo gera o M_Ice_Lake: HDRP/Lit " +
             "transparente + refração, com a água do lago aparecendo por baixo). " +
             "Vazio = HDRP/Lit chapado gerado em runtime.")]
    public Material iceMaterial;
    [Tooltip("Altura da placa acima da lâmina d'água (m). Com material transparente é " +
             "a espessura aparente do gelo; a água continua sendo renderizada abaixo.")]
    [SerializeField] float iceSurfaceOffset = 0.08f;
    [Tooltip("Metros de mundo por repetição da textura de gelo (bater com o IceTextureGenerator.WorldTile).")]
    [SerializeField] float iceTextureTile = 22f;
    [Tooltip("Distorção do domínio da UV em metros — o antídoto para a repetição: " +
             "o padrão nunca se repete idêntico porque a própria UV serpenteia. 0 desliga.")]
    [SerializeField] float iceUvWarp = 2.4f;
    [Tooltip("Amplitude do relevo da placa em metros (encurvamento + cristas de pressão). " +
             "É ruído de MUNDO: nunca se repete e é o que impede o gelo de parecer um plano.")]
    [SerializeField] float iceRelief = 0.07f;
    [Tooltip("Faixa de 'frio' na qual a borda da placa MERGULHA sob a lâmina d'água — " +
             "sem isso o corte seco do bioma vira um degrau reto no meio do lago.")]
    [SerializeField, Range(0f, 0.3f)] float iceEdgeFade = 0.10f;
    [Tooltip("Amortece as ondulações da WaterSurface sob o gelo (lago congelado é espelho). " +
             "Gera uma máscara de água na CPU com a MESMA regra do mesh.")]
    [SerializeField] bool iceCalmsWaterUnder = true;

    [Header("Vegetação da Floresta/Campos (ALP)")]
    [Tooltip("Cada entrada = prefab + peso relativo (o 'pincel' serializável). Peso 0 desliga sem remover.")]
    public PaintTree[] treePrefabs;             // Floresta Antiga (+ raras nos Campos)
    public PaintTree[] bushPrefabs;             // arbustos: floresta densa + campos esparsos
    public PaintTree[] grassPrefabs;            // graminhas verdes
    [Tooltip("Moitas grandes (Thicket) — massas densas de vegetação, mais raras que arbustos.")]
    public PaintTree[] forestThicketPrefabs;
    [Tooltip("Serrapilheira (gravetos ForestStick* + folhas LeafDry*) — detalhe fino do chão da floresta.")]
    public PaintTree[] forestLitterPrefabs;
    [SerializeField] int forestVegetationPerTile = 600;   // floresta BEM densa
    [Tooltip("Fração DESCONTADA da cota acima antes de sortear as árvores " +
             "(arbustos têm contagem própria em forestBushPerTile).")]
    [SerializeField, Range(0f, 1f)] float bushShare = 0.25f;
    [SerializeField] int grassPerTile = 500;
    [Tooltip("Tapete de sub-bosque da floresta (GrassPlant*) — a camada MAIS numerosa: " +
             "manchas densas de grama alta com clareiras abertas entre elas.")]
    public PaintTree[] forestGroundcoverPrefabs;
    [Tooltip("Tentativas por tile do tapete de GrassPlant (o grosso da vegetação rasteira).")]
    [SerializeField] int forestGroundcoverPerTile = 2600;
    [Tooltip("Tentativas por tile dos arbustos da floresta (ForestBush*).")]
    [SerializeField] int forestBushPerTile = 950;
    [SerializeField] int forestThicketsPerTile = 40;
    [SerializeField] int forestLitterPerTile = 450;

    [Header("Deserto Rochoso (RockyDesert)")]
    [Tooltip("Formações grandes de penhasco (SM_RockSide_*) — agrupadas em afloramentos, com collider.")]
    public PaintTree[] desertFormationPrefabs;
    [Tooltip("Pedras médias (SM_Small_Rock_2/3/4) — ao redor dos afloramentos.")]
    public PaintTree[] desertRockPrefabs;
    [Tooltip("Seixos/pedrinhas (SM_Small_Rock_1/B1/B2) — preenchimento fino do chão.")]
    public PaintTree[] desertPebblePrefabs;
    [Tooltip("Árvores mortas (SM_Tree_*) — marcos raríssimos, aparecem no minimapa.")]
    public PaintTree[] desertTreePrefabs;
    [Tooltip("Tufos de grama seca (SM_DeadGrass_*) — manchas de vegetação nas áreas abertas.")]
    public PaintTree[] desertGrassPrefabs;
    [Tooltip("Multiplicador geral de densidade do deserto (1 = calibrado pela Demo Scene do pacote).")]
    [SerializeField, Range(0.1f, 2f)] float desertDensity = 1f;

    [Header("Tundra/Montanha (Winter Environment — preenchido pelo WinterSetup)")]
    [Tooltip("Abetos nevados (Tree_A) — a mata fechada da Tundra e a treeline da Montanha.")]
    public PaintTree[] winterFirPrefabs;
    [Tooltip("Pinheiros altos e ralos com raízes expostas (Tree_B) — quebram a silhueta dos bosques.")]
    public PaintTree[] winterPinePrefabs;
    [Tooltip("Decíduas nuas com neve (Tree_C, tipo bétula) — capões próprios nas áreas abertas.")]
    public PaintTree[] winterBirchPrefabs;
    [Tooltip("Arbustos nevados (Bush_A) — preenchimento baixo da Tundra.")]
    public PaintTree[] winterBushPrefabs;
    [Tooltip("Tufos de grama seca (Grass_A/B/C) — furam a neve nas áreas abertas.")]
    public PaintTree[] winterGrassPrefabs;
    [Tooltip("Pedras avulsas nevadas (Rock_*) — detalhe da Tundra e das encostas da Montanha.")]
    public PaintTree[] winterRockPrefabs;
    [Tooltip("Afloramentos grandes (Rock_Group) — marcos rochosos que reservam espaço.")]
    public PaintTree[] winterOutcropPrefabs;
    [Tooltip("Madeira caída (Log/Stump/Root) — troncos, tocos e raízes viradas junto da mata.")]
    public PaintTree[] winterDeadwoodPrefabs;
    [Tooltip("Galhada fina (Debris) — serrapilheira de inverno sob os bosques.")]
    public PaintTree[] winterDebrisPrefabs;
    [Tooltip("Multiplicador geral de densidade da Tundra/Montanha (1 = calibrado pela Demo do pack).")]
    [SerializeField, Range(0.1f, 2f)] float winterDensity = 1f;
    [Tooltip("Neve funda do pack (Snow_01_Layer) — substitui a cor sólida no canal 3.")]
    public TerrainLayer snowLayer;
    [Tooltip("Folhas congeladas do pack (Leaves_01_Layer) — chão sob a mata da Tundra (canal 7; " +
             "sem MicroSplat reconvertido, cai no canal de terra da floresta).")]
    public TerrainLayer winterGroundLayer;
    [Tooltip("Partícula de neve do pack (Snow_01) — segue o jogador dentro da Tundra/Montanha.")]
    public GameObject winterSnowfallPrefab;

    [Header("Montanha (Mountain Environment — preenchido pelo MountainSetup)")]
    [Tooltip("Pinheiros adultos (Forest_pine) — a mata dos vales e encostas baixas. " +
             "Regra do autor: pintados a ~55% da escala (scaleRange já calibrado).")]
    public PaintTree[] mountainPinePrefabs;
    [Tooltip("Mudas e árvores jovens (pine_plant/tree_00) — sub-bosque e borda da mata.")]
    public PaintTree[] mountainSaplingPrefabs;
    [Tooltip("Krummholz (dwarf pine + hazel) — vegetação anã da treeline.")]
    public PaintTree[] mountainBushPrefabs;
    [Tooltip("Grama, urze, mirtilo, rododendro — cobertura dos vales.")]
    public PaintTree[] mountainGrassPrefabs;
    [Tooltip("Pedras avulsas (4 famílias: nua/musgo/agulhas/solo).")]
    public PaintTree[] mountainRockPrefabs;
    [Tooltip("Muralhas e formações grandes (big_rock/rock_wall/mountain_rock_big) — " +
             "vestem penhascos em escala 2.5–7 como na Demo (lá chegam a 49×).")]
    public PaintTree[] mountainBoulderPrefabs;
    [Tooltip("Seixos de rio (river_stone 1–12) — SÓ no leito dos riachos (inStreamBed).")]
    public PaintTree[] mountainRiverStonePrefabs;
    [Tooltip("Raízes de barranco, tocos e troncos — vestem as quebras de encosta.")]
    public PaintTree[] mountainDeadwoodPrefabs;
    [Tooltip("Cogumelos (32 espécies) — chão da mata de pinheiros.")]
    public PaintTree[] mountainMushroomPrefabs;
    [Tooltip("Galhos, pinhas e formigueiros — serrapilheira da montanha.")]
    public PaintTree[] mountainDetailPrefabs;
    [Tooltip("Decals de chão — reforço de textura só em patamares planos.")]
    public PaintTree[] mountainDecalPrefabs;
    [Tooltip("Estátua Sviatovid — landmark RARÍSSIMO em platôs (decisão de lore: mantida).")]
    public PaintTree[] mountainStatuePrefabs;
    [Tooltip("Multiplicador geral de densidade da Montanha (1 = calibrado pela Demo NM).")]
    [SerializeField, Range(0.1f, 2f)] float mountainDensity = 1f;
    [Tooltip("Rocha de penhasco do pack — RESERVADA p/ um canal futuro só da montanha " +
             "(no canal 2 global ela escurecia as bacias de lago; revertido).")]
    public TerrainLayer mountainRockTerrainLayer;
    [Tooltip("Musgo (vales úmidos) — canal extra do alphamap.")]
    public TerrainLayer mountainMossLayer;
    [Tooltip("Agulhas de pinheiro (sob a mata, via groundPaint) — canal extra.")]
    public TerrainLayer mountainNeedleLayer;
    [Tooltip("Solo de altitude (meia encosta) — canal extra.")]
    public TerrainLayer mountainSoilLayer;

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
    [Tooltip("Fração do chão da floresta pintada com grama fora das manchas de terra " +
             "(0 = chão todo de terra como antes, 1 = grama pura entre as manchas).")]
    [Range(0f, 1f)] [SerializeField] float forestGrassShare = 0.85f;
    [Tooltip("Limiar do noise das manchas de terra da floresta — maior = manchas mais raras/menores.")]
    [Range(0f, 1f)] [SerializeField] float forestDirtThreshold = 0.68f;

    [Header("MicroSplat (gerar via Tools > Everwyrm > MicroSplat — Converter Terreno)")]
    [Tooltip("Material template do MicroSplat: cada tile ganha um MicroSplatTerrain sincronizado " +
             "com ele no spawn. Vazio = HDRP/TerrainLit padrão (sem anti-tiling/height blend).")]
    public Material microSplatMaterial;
    public JBooth.MicroSplat.MicroSplatPropData microSplatPropData;
    public JBooth.MicroSplat.MicroSplatKeywords microSplatKeywords;
    [Tooltip("Shader *_Base do MicroSplat — o Sync só o resolve sozinho no editor; em build precisa da referência.")]
    public Shader microSplatBaseShader;

    public static InfiniteTerrain Instance { get; private set; }

    /// <summary>Mundo tem lagos? (DragonController/FoodSpawner consultam.)</summary>
    public bool HasLakes => enableLakes;
    /// <summary>Altura da lâmina d'água (m de mundo).</summary>
    public float WaterLevel => waterLevel;

    /// <summary>A água (lago/riacho) neste ponto do mundo está CONGELADA?
    /// Corte seco por peso do bioma frio — a MESMA regra do mesh de gelo dos
    /// tiles, então gameplay (nado/pouso) e visual nunca discordam.</summary>
    public bool IsWaterFrozenAt(float wx, float wz)
    {
        if (!freezeTundraWater || (!enableLakes && !StreamsActive)) return false;
        BiomeWeights(wx, wz, out _, out _, out _, out float cold, out _);
        return cold >= frozenColdThreshold;
    }

    float maxTreeHeightCache = -1f;

    /// <summary>
    /// Altura (m) da MAIOR árvore que a floresta pode gerar — o prefab mais alto
    /// das camadas de árvore da Floresta na sua escala máxima. Referência viva
    /// para regras que dependem do porte da mata (ex.: altura segura de queda no
    /// DragonController): trocar a vegetação recalibra tudo sozinho.
    /// </summary>
    public float MaxForestTreeHeight
    {
        get
        {
            if (maxTreeHeightCache >= 0f) return maxTreeHeightCache;
            if (activeLayers == null) return 0f;      // Awake ainda não montou as camadas

            float max = 0f;
            foreach (var l in activeLayers)
            {
                // só ÁRVORES da floresta: `landmark` marca as camadas de porte
                // (arbustos, moitas e grama não entram)
                if (l == null || l.biome != Biome.Floresta || !l.landmark || l.prefabs == null)
                    continue;
                foreach (var p in l.prefabs)
                    if (p != null && p.prefab != null)
                        max = Mathf.Max(max, PrefabHeight(p.prefab) * Mathf.Max(0.01f, l.scaleRange.y));
            }
            return maxTreeHeightCache = max;
        }
    }

    /// <summary>Altura do prefab em unidades locais (topo das malhas sobre a raiz).</summary>
    static float PrefabHeight(GameObject prefab)
    {
        float top = 0f;
        var toRoot = prefab.transform.worldToLocalMatrix;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            var m = toRoot * mf.transform.localToWorldMatrix;
            Vector3 c = m.MultiplyPoint3x4(b.center);
            Vector3 e = m.MultiplyVector(b.extents);
            top = Mathf.Max(top, c.y + Mathf.Abs(e.y));
        }
        return top;
    }

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
    /// Um prefab "paint tree" com peso relativo dentro da camada — o análogo
    /// serializável do pincel de árvores do terreno. weight 2 = sorteado 2x mais
    /// que weight 1; 0 = desligado sem precisar tirar da lista.
    /// </summary>
    [Serializable]
    public class PaintTree
    {
        public GameObject prefab;
        [Tooltip("Proporção relativa dentro da camada (0 = desligado).")]
        [Min(0f)] public float weight = 1f;

        public PaintTree() { }
        public PaintTree(GameObject p, float w = 1f) { prefab = p; weight = w; }
        public static implicit operator PaintTree(GameObject p) => new(p);

        /// <summary>Converte listas de GameObject (setups de editor) com peso 1.</summary>
        public static PaintTree[] From(IEnumerable<GameObject> prefabs)
        {
            var list = new List<PaintTree>();
            if (prefabs != null)
                foreach (var p in prefabs)
                    if (p != null) list.Add(new PaintTree(p));
            return list.ToArray();
        }
    }

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
        public PaintTree[] prefabs;

        [Tooltip("Pontos sorteados por tile (antes dos filtros)")]
        [Min(0)] public int attemptsPerTile = 100;
        [Tooltip("Peso mínimo do bioma para aparecer (0.4 = só razoavelmente dentro dele)")]
        [Range(0f, 1f)] public float minBiomeWeight = 0.4f;
        [Tooltip("Bioma RIVAL que veta esta camada quando presente acima do limite " +
                 "(ex.: árvores verdes nunca sobre areia dominante). 1 = veto desligado.")]
        public Biome avoidBiome = Biome.Deserto;
        [Range(0f, 1f)] public float avoidBiomeMax = 1f;
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
        [Tooltip("Quanto da área fica dentro das manchas — 0.15 = ilhas raras, 0.5 = metade do chão.")]
        [Range(0f, 1f)] public float clusterCoverage = 0.5f;
        [Tooltip("Borda da mancha: 0 = recorte duro, 1 = a densidade cai bem devagar até sumir.")]
        [Range(0f, 1f)] public float clusterEdgeSoftness = 0.5f;
        [Tooltip("2ª octava de noise que quebra o contorno redondo da mancha (0 = desligada).")]
        [Range(0f, 1f)] public float clusterIrregularity = 0f;

        [Tooltip("0 = espécies misturadas ponto a ponto · 1 = cada região tem UMA espécie dominante " +
                 "(bosques/manchas puras). Respeita os pesos dos PaintTrees.")]
        [Range(0f, 1f)] public float speciesClumping = 0f;
        [Tooltip("Tamanho das regiões de espécie dominante (metros).")]
        public float speciesPatchSize = 60f;

        [Tooltip("Distância mínima entre instâncias DESTA camada (0 = livre)")]
        public float minSpacing = 0f;
        [Tooltip("> 0: reserva um raio que bloqueia camadas SEGUINTES (formações grandes)")]
        public float blockRadius = 0f;
        [Tooltip("> 0: mantém esta distância extra de todo espaço bloqueado")]
        public float avoidBlockers = 0f;

        [Tooltip("Instâncias entram no minimapa (árvores/marcos)")]
        public bool landmark = false;

        [Tooltip("≥ 0: a máscara de AGRUPAMENTO desta camada também pinta este canal do " +
                 "alphamap (6 folhas · 8 agulhas — ex.: chão sob os bosques). -1 = não pinta.")]
        public int groundPaint = -1;

        [Tooltip("Inverte a regra dos riachos: coloca SÓ no leito/margens (seixos de rio) " +
                 "e permite ficar abaixo da lâmina d'água.")]
        public bool inStreamBed = false;

        [Tooltip("Coloca SOBRE a placa de gelo do lago congelado: exige bacia (chão " +
                 "abaixo do waterLevel) + frio suficiente, e assenta a instância na " +
                 "SUPERFÍCIE do gelo em vez de no leito. Pedras encravadas na placa.")]
        public bool onFrozenIce = false;

        [NonSerialized] public int protoBase;   // offset no array de TreePrototypes
        [NonSerialized] public int protoCount;
        [NonSerialized] public float[] cumWeights;   // pesos acumulados p/ sorteio ponderado

        /// <summary>Índice do prefab sorteado (0..protoCount-1) a partir de um roll [0..1).</summary>
        public int PickPrototype(float roll01)
        {
            if (cumWeights == null || cumWeights.Length == 0) return 0;
            float target = roll01 * cumWeights[cumWeights.Length - 1];
            for (int i = 0; i < cumWeights.Length - 1; i++)
                if (target <= cumWeights[i]) return i;
            return cumWeights.Length - 1;
        }
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
        [Tooltip("Meia-largura do canal em unidades de noise (~0.026 ≈ canal de 10–20 m)")]
        [Range(0.008f, 0.06f)] public float channelWidth = 0.026f;
        [Tooltip("Profundidade do leito abaixo do waterLevel (m)")]
        public float depth = 1.8f;
        [Tooltip("Altura do fundo do vale acima do waterLevel (m) — margem seca estreita")]
        public float bankHeight = 0.6f;
        [Tooltip("Largura do vale relativa ao canal (encostas suaves até o leito)")]
        public float valleyWidthMul = 2.6f;
        [Tooltip("Fração aproximada do bioma com riachos (1 = bioma inteiro). " +
                 "Baixo = achar um riacho é raro/especial.")]
        [Range(0f, 1f)] public float density = 0.25f;
        [Tooltip("Tamanho das regiões com/sem riachos (m) — grande = longos trechos " +
                 "secos entre 'bacias hidrográficas'")]
        public float regionSize = 2600f;
        [Tooltip("Peso do bioma a partir do qual o riacho aparece em força total")]
        [Range(0f, 1f)] public float fadeStart = 0.55f;
        [Tooltip("Peso do bioma abaixo do qual não existe riacho nenhum")]
        [Range(0f, 1f)] public float fadeEnd = 0.25f;
        [Tooltip("Confina o riacho aos VALES em U da montanha (lê o mesmo campo de " +
                 "cristas do relevo) — sem isso, o curso escavaria gargantas nos picos.")]
        public bool valleyOnly = false;

        [NonSerialized] public Vector2 offCourse, offWidth, offRegion; // da seed (BuildStreamSetup)
    }

    readonly Dictionary<Vector2Int, Terrain> tiles = new();
    readonly Dictionary<Vector2Int, List<Vector2>> tileTrees = new(); // XZ das árvores (minimapa)
    readonly Queue<Vector2Int> buildQueue = new();
    readonly HashSet<Vector2Int> pending = new();
    IEnumerator activeBuild;                    // tile em construção fatiada
    Vector2Int activeCoord;
    readonly System.Diagnostics.Stopwatch buildSw = new();
    bool initialized;                           // Awake já derivou offsets/camadas?

    TerrainLayer[] layers;
    Material terrainMat;
    TreePrototype[] prototypes;                 // união dos prefabs de todas as camadas
    List<ScatterLayer> activeLayers;            // scatterLayers ou o padrão
    List<ScatterLayer> groundPaintLayers;       // camadas que também pintam o chão
    List<StreamSettings> activeStreams;         // riachos ativos (config em DefaultStreams)
    readonly Dictionary<int, Vector2> clusterOffsets = new();
    float oxT, ozT, oxM, ozM, oxH, ozH, oxD, ozD; // offsets de noise (seed)

    // ------------------------------------------------------------ LIFECYCLE
    void Awake()
    {
        Instance = this;
        InitNoiseOffsets();

        var shader = Shader.Find("HDRP/TerrainLit");
        terrainMat = shader != null ? new Material(shader) : null;

        // Ordem dos canais do alphamap:
        // 0 grama · 1 floresta · 2 rocha de montanha · 3 neve · 4 areia
        // · 5 rocha do deserto — e, com os packs de bioma, o bloco ESTENDIDO em
        // posições FIXAS: 6 folhas congeladas (Winter) · 7 musgo · 8 agulhas ·
        // 9 solo de altitude (Mountain). O bloco entra por inteiro se QUALQUER
        // layer dele existir (slots vazios ganham fallback) — os índices dos
        // canais nunca mudam, então groundPaint e MicroSplat ficam estáveis.
        var layerList = new List<TerrainLayer>
        {
            MakeLayer(grassDiffuse, grassNormal, grassMask, grassColor, groundTextureTile),
            MakeLayer(forestDiffuse, forestNormal, forestMask, forestColor, groundTextureTile),
            // canal 2 pinta encostas do MUNDO INTEIRO (bacias de lago incluídas)
            // — fica com a rocha clara de sempre; a NM (escura, de penhasco) é
            // agressiva demais fora da montanha e escureceu os lagos.
            MakeLayer(rockDiffuse, rockNormal, rockMask, rockColor, groundTextureTile * 1.6f),
            snowLayer != null ? snowLayer
                              : MakeLayer(snowDiffuse, snowNormal, snowMask, snowColor, groundTextureTile),
            sandLayer != null ? sandLayer
                              : MakeLayer(null, null, null, sandColor, groundTextureTile),
            desertRockLayer != null ? desertRockLayer
                              : MakeLayer(null, null, null, desertRockColor, groundTextureTile * 1.6f)
        };
        if (winterGroundLayer != null || mountainMossLayer != null ||
            mountainNeedleLayer != null || mountainSoilLayer != null)
        {
            layerList.Add(winterGroundLayer != null ? winterGroundLayer
                : MakeLayer(null, null, null, new Color(0.55f, 0.48f, 0.35f), groundTextureTile));
            layerList.Add(mountainMossLayer != null ? mountainMossLayer
                : MakeLayer(null, null, null, new Color(0.25f, 0.34f, 0.18f), groundTextureTile));
            layerList.Add(mountainNeedleLayer != null ? mountainNeedleLayer
                : MakeLayer(null, null, null, new Color(0.42f, 0.33f, 0.22f), groundTextureTile));
            layerList.Add(mountainSoilLayer != null ? mountainSoilLayer
                : MakeLayer(null, null, null, new Color(0.45f, 0.38f, 0.30f), groundTextureTile));
        }
        layers = layerList.ToArray();

        BuildScatterSetup();
        BuildStreamSetup();
        initialized = true;
    }

    /// <summary>Deriva TODOS os offsets de noise da seed — o único "estado interno"
    /// da geração. Re-executável (ApplyState troca a seed em runtime).</summary>
    void InitNoiseOffsets()
    {
        var rng = new System.Random(seed);
        float Off() => (float)(rng.NextDouble() * 10000.0 - 5000.0);
        oxT = Off(); ozT = Off(); oxM = Off(); ozM = Off();
        oxH = Off(); ozH = Off(); oxD = Off(); ozD = Off();
    }

    void Start()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player == null) { enabled = false; return; }

        // lago garantido perto do spawn — definir ANTES do primeiro tile.
        // Só na PRIMEIRA criação do mundo: um estado carregado (ApplyState) já
        // traz o centro salvo — recalcular aqui moveria o lago (e o terreno)
        // se o jogador carregasse o save longe do spawn original.
        if (enableLakes && guaranteedStartLake && !startLakeSet)
        {
            startLakeCenter = new Vector2(player.position.x + 70f, player.position.z);
            startLakeSet = true;
        }

        EnsureWaterSurface();
        EnsureSnowfall();
        BuildTile(TileOf(player.position));     // tile inicial síncrono
        SnapPlayerToGround();
    }

    // ------------------------------------- ESTADO DO MUNDO (persistência/rede)
    /// <summary>
    /// Fotografa a identidade do mundo — o que um save grava e um servidor
    /// autoritativo enviaria aos clientes. Tiles NÃO entram: são projeções
    /// determinísticas deste estado + da configuração que viaja com a build.
    /// </summary>
    public WorldGenState CaptureState() => new()
    {
        seed = seed,
        startLakeSet = startLakeSet,
        startLakeCenter = startLakeCenter,
    };

    /// <summary>
    /// Reconstrói o mundo a partir de uma identidade salva/recebida. Pode ser
    /// chamado a qualquer momento: tiles existentes são descartados e renascem
    /// do estado novo (mesma seed + mesmo lago inicial = mesmo mundo, bit a bit).
    /// </summary>
    public void ApplyState(WorldGenState state)
    {
        if (state == null) return;
        seed = state.seed;
        startLakeSet = state.startLakeSet;
        startLakeCenter = state.startLakeCenter;

        if (!initialized) return;   // Awake ainda vai rodar e ler os campos acima

        InitNoiseOffsets();
        BuildScatterSetup();        // re-executável: clusterOffsets dependem da seed
        BuildStreamSetup();
        ClearTiles();
        if (player != null) BuildTile(TileOf(player.position));   // chão sob os pés
    }

    /// <summary>Descarta todos os tiles e a fila de construção — o streaming
    /// normal do Update os reconstrói do estado atual.</summary>
    void ClearTiles()
    {
        activeBuild = null;
        buildQueue.Clear();
        pending.Clear();
        foreach (var kv in tiles)
        {
            var t = kv.Value;
            if (t == null) continue;
            var data = t.terrainData;
            var iceMesh = IceMeshOf(t);
            Destroy(t.gameObject);
            Destroy(data);
            if (iceMesh != null) Destroy(iceMesh);   // mesh de runtime: sem isso, vaza
        }
        tiles.Clear();
        tileTrees.Clear();

        // o mundo mudou de seed: a máscara de água congelada não vale mais
        frozenMaskCenter = new Vector2(float.NaN, float.NaN);
    }

    /// <summary>Mesh da placa de gelo pendurada no tile (null quando o tile não congelou).</summary>
    static Mesh IceMeshOf(Terrain t)
    {
        var f = t != null ? t.GetComponentInChildren<MeshFilter>() : null;
        return f != null ? f.sharedMesh : null;
    }

    void OnDestroy()
    {
        if (frozenMask != null) Destroy(frozenMask);
    }

    void Update()
    {
        if (player == null) return;
        UpdateWaterFollow();
        UpdateSnowfall();
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

        // rede de segurança: o tile SOB o jogador nunca pode faltar (voo muito
        // rápido pode atropelar a geração fatiada) — nesse caso raro, termina
        // síncrono e paga um frame cheio em vez de deixar o dragão cair no vazio.
        if (!tiles.ContainsKey(center))
        {
            if (activeBuild != null && activeCoord == center)
            {
                while (activeBuild.MoveNext()) { }
                activeBuild = null;
            }
            else BuildTile(center);
        }

        // constrói em FATIAS com orçamento de ms — avança o tile ativo (e puxa o
        // próximo da fila) até estourar o budget; cada yield é um ponto de corte.
        buildSw.Restart();
        while (buildSw.Elapsed.TotalMilliseconds < buildBudgetMs)
        {
            if (activeBuild == null)
            {
                bool found = false;
                while (buildQueue.Count > 0)
                {
                    var c = buildQueue.Dequeue();
                    pending.Remove(c);
                    if (!tiles.ContainsKey(c) &&
                        Mathf.Max(Mathf.Abs(c.x - center.x), Mathf.Abs(c.y - center.y)) <= loadRadius)
                    {
                        activeBuild = BuildTileSteps(c);
                        activeCoord = c;
                        found = true;
                        break;
                    }
                }
                if (!found) break;
            }
            if (!activeBuild.MoveNext()) activeBuild = null;
        }

        // descarta UM tile distante por frame (Destroy de Terrain+TerrainData em
        // lote concentrava o custo de GC num frame só)
        foreach (var kv in tiles)
            if (Mathf.Max(Mathf.Abs(kv.Key.x - center.x), Mathf.Abs(kv.Key.y - center.y)) > loadRadius + 1)
            {
                var t = kv.Value;
                tiles.Remove(kv.Key);
                tileTrees.Remove(kv.Key);
                if (t != null)
                {
                    var data = t.terrainData;
                    // mesh do gelo é asset de runtime como o TerrainData: sem o
                    // Destroy explícito ele vaza a cada tile descartado
                    var iceMesh = IceMeshOf(t);
                    Destroy(t.gameObject);
                    Destroy(data);
                    if (iceMesh != null) Destroy(iceMesh);
                }
                break;
            }
    }

    // ------------------------------------------------- SETUP DO ESPALHAMENTO
    void BuildScatterSetup()
    {
        activeLayers = scatterLayers != null && scatterLayers.Count > 0
            ? scatterLayers : DefaultLayers();
        maxTreeHeightCache = -1f;   // vegetação mudou: recalcular a altura da mata

        // remove prefabs incompatíveis/vazios/peso 0 e monta o array único de prototypes.
        // UM prototype inválido quebra a renderização de TODOS os trees do tile,
        // então o filtro aqui é obrigatório.
        foreach (var l in activeLayers)
            if (l.prefabs != null)
                l.prefabs = Array.FindAll(l.prefabs,
                    e => e != null && e.weight > 0f && IsTreeCompatible(e.prefab));
        activeLayers.RemoveAll(l => l.prefabs == null || l.prefabs.Length == 0);

        var protos = new List<TreePrototype>();
        foreach (var l in activeLayers)
        {
            l.protoBase = protos.Count;
            l.protoCount = l.prefabs.Length;
            l.cumWeights = new float[l.prefabs.Length];
            float acc = 0f;
            for (int i = 0; i < l.prefabs.Length; i++)
            {
                acc += l.prefabs[i].weight;
                l.cumWeights[i] = acc;
                protos.Add(new TreePrototype { prefab = l.prefabs[i].prefab });
            }
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

        // camadas cuja mancha de agrupamento também pinta o alphamap
        groundPaintLayers = activeLayers.FindAll(l => l.groundPaint >= 0 && l.clusterStrength > 0f);
    }

    /// <summary>
    /// Máscara PURA [0..1] das manchas de agrupamento de uma camada num ponto do
    /// mundo (1 = miolo da mancha, 0 = fora). Compartilhada pelo scatter (que a
    /// dilui com clusterStrength) e pelo splatmap (groundPaint, que a usa crua) —
    /// os objetos e o chão sob eles enxergam exatamente as mesmas manchas.
    /// </summary>
    float ClusterMask(ScatterLayer layer, Vector2 cOff, float wx, float wz)
    {
        float n = Mathf.PerlinNoise(wx / layer.clusterSize + cOff.x,
                                    wz / layer.clusterSize + cOff.y);
        if (layer.clusterIrregularity > 0f)
            n += (Mathf.PerlinNoise(wx * 3.1f / layer.clusterSize + cOff.x + 57.3f,
                                    wz * 3.1f / layer.clusterSize + cOff.y + 21.7f)
                  - 0.5f) * 0.5f * layer.clusterIrregularity;
        float lo = Mathf.Lerp(0.85f, 0.05f, layer.clusterCoverage);
        float wdt = Mathf.Lerp(0.02f, 0.52f, layer.clusterEdgeSoftness);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, lo + wdt, n));
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

        var list = DefaultStreams();
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

    /// <summary>
    /// Config dos riachos. Floresta: canal de 10–20 m escavado 1.8 m abaixo do
    /// waterLevel, meandros de ~430 m, ~1/4 do bioma. Montanha: torrentes
    /// CONFINADAS aos vales em U (valleyOnly) — o fundo do vale (~14–25 m)
    /// escavado até a lâmina vira uma garganta de rio com paredes pintadas de
    /// rocha pela inclinação, forrada de river stones (camada inStreamBed).
    /// </summary>
    static List<StreamSettings> DefaultStreams() => new()
    {
        new StreamSettings
        {
            name = "Riachos da Floresta",
            biome = Biome.Floresta,
            courseSize = 430f,
            channelWidth = 0.026f,
            depth = 1.8f,
            bankHeight = 0.6f,
            valleyWidthMul = 2.6f,
            density = 0.25f,
            regionSize = 2600f,
            fadeStart = 0.55f,
            fadeEnd = 0.25f,
        },
        new StreamSettings
        {
            name = "Torrentes da Montanha",
            biome = Biome.Montanha,
            courseSize = 500f,
            channelWidth = 0.02f,
            depth = 1.4f,
            bankHeight = 0.6f,
            valleyWidthMul = 2.2f,
            density = 0.3f,
            regionSize = 2200f,
            fadeStart = 0.5f,
            fadeEnd = 0.22f,
            valleyOnly = true,
        }
    };

    /// <summary>
    /// Conjunto padrão de camadas — Floresta/Campos como sempre foram, e o Deserto
    /// calibrado pela Demo Scene do RockyDesert (~12.5k seixos, ~5.4k pedras,
    /// ~1.9k gramas secas, ~780 formações e só 8 árvores mortas por km²,
    /// escalas 0.2–1.2 — aqui em manchas: afloramentos rochosos + areia aberta).
    /// </summary>
    List<ScatterLayer> DefaultLayers()
    {
        float d = desertDensity;
        float w = winterDensity;
        float n = mountainDensity;
        int treeAttempts = Mathf.RoundToInt(forestVegetationPerTile * (1f - bushShare));

        return new List<ScatterLayer>
        {
            // ---------------- FLORESTA ANTIGA (densa) ----------------
            // Árvores: cobertura quase contínua com ONDAS suaves de densidade (mata
            // fechada ↔ trechos ralos) e bosques de espécie dominante (stands de maple
            // no meio dos ForestTree, como mata real — não um mosaico ponto a ponto).
            new()
            {
                name = "Floresta — árvores", biome = Biome.Floresta, prefabs = treePrefabs,
                attemptsPerTile = treeAttempts, minBiomeWeight = 0.4f, density = 1f,
                avoidBiomeMax = 0.25f,   // nunca sobre areia dominante
                scaleRange = new Vector2(0.85f, 1.4f), aspectJitter = 0.1f,
                maxSlope = 0.5f, landmark = true,
                clusterStrength = 0.25f, clusterSize = 220f, clusterCoverage = 0.55f,
                clusterEdgeSoftness = 0.8f, clusterIrregularity = 0.4f,
                // 0.4/110 (era 0.55/150): stands de UMA espécie ficavam grandes
                // demais e a mata lia como monocultura vista do ar
                speciesClumping = 0.4f, speciesPatchSize = 110f
            },
            // Arbustos (ForestBush01–11): manchas GRANDES e irregulares com borda
            // suave, mancha tendendo a UMA espécie. Divide o noise (clusterGroup 2)
            // com as moitas e o sub-bosque — do miolo p/ fora: moita → arbusto →
            // grama alta → clareira, um gradiente contínuo denso→aberto.
            // minSpacing curto: volume cheio sem arbusto nascendo DENTRO de arbusto.
            new()
            {
                name = "Floresta — arbustos", biome = Biome.Floresta, prefabs = bushPrefabs,
                avoidBiomeMax = 0.25f,
                attemptsPerTile = forestBushPerTile, minBiomeWeight = 0.4f,
                density = 1f, scaleRange = new Vector2(0.7f, 1.45f), aspectJitter = 0.12f,
                maxSlope = 0.5f, minSpacing = 1.3f,
                clusterStrength = 0.85f, clusterSize = 42f, clusterGroup = 2,
                clusterCoverage = 0.38f, clusterEdgeSoftness = 0.75f, clusterIrregularity = 0.8f,
                speciesClumping = 0.65f, speciesPatchSize = 40f
            },
            new()
            {
                name = "Floresta — moitas", biome = Biome.Floresta, prefabs = forestThicketPrefabs,
                attemptsPerTile = Mathf.RoundToInt(forestThicketsPerTile * 1.7f),
                minBiomeWeight = 0.45f, density = 0.9f,
                scaleRange = new Vector2(0.9f, 1.4f), aspectJitter = 0.15f, maxSlope = 0.45f,
                minSpacing = 10f,
                clusterStrength = 0.9f, clusterSize = 42f, clusterGroup = 2,   // miolo das manchas
                clusterCoverage = 0.14f, clusterEdgeSoftness = 0.5f, clusterIrregularity = 0.6f
            },
            // O TAPETE (GrassPlant02–05): a camada mais numerosa da floresta — manchas
            // densas de grama alta com clareiras de chão limpo entre elas, espécie
            // dominante por região p/ formar "cantos" coesos em vez de confete.
            // Manchas PRÓPRIAS (fora do noise dos arbustos): o chão aberto entre os
            // bosques também ganha vida, não só a periferia das moitas.
            new()
            {
                name = "Floresta — tapete de grama", biome = Biome.Floresta,
                prefabs = forestGroundcoverPrefabs,
                attemptsPerTile = forestGroundcoverPerTile, minBiomeWeight = 0.35f,
                density = 1f, scaleRange = new Vector2(0.65f, 1.9f), aspectJitter = 0.15f,
                maxSlope = 0.55f,
                clusterStrength = 0.7f, clusterSize = 30f, clusterCoverage = 0.5f,
                clusterEdgeSoftness = 0.9f, clusterIrregularity = 0.75f,
                speciesClumping = 0.7f, speciesPatchSize = 20f
            },
            // Sub-bosque: MAIS GrassPlant concentrado nas manchas dos arbustos (grupo 2,
            // coverage maior = halo que transborda a mancha) — grama alta engolindo a
            // base dos arbustos e desmanchando a borda deles no chão.
            new()
            {
                name = "Floresta — sub-bosque", biome = Biome.Floresta,
                prefabs = forestGroundcoverPrefabs,
                attemptsPerTile = Mathf.RoundToInt(forestGroundcoverPerTile * 0.45f),
                minBiomeWeight = 0.4f, density = 1f,
                scaleRange = new Vector2(0.85f, 2.1f), aspectJitter = 0.15f, maxSlope = 0.5f,
                clusterStrength = 0.9f, clusterSize = 42f, clusterGroup = 2,
                clusterCoverage = 0.52f, clusterEdgeSoftness = 0.9f, clusterIrregularity = 0.7f,
                speciesClumping = 0.5f, speciesPatchSize = 26f
            },
            // Flores/graminhas variadas: acentos de cor espalhados por cima do tapete;
            // espécie por região = "cantos de flores" em vez de confete uniforme.
            new()
            {
                name = "Floresta — graminhas", biome = Biome.Floresta, prefabs = grassPrefabs,
                attemptsPerTile = Mathf.RoundToInt(grassPerTile * 1.3f), minBiomeWeight = 0.4f,
                density = 1f, scaleRange = new Vector2(0.9f, 1.6f), maxSlope = 0.5f,
                clusterStrength = 0.35f, clusterSize = 24f, clusterCoverage = 0.45f,
                clusterEdgeSoftness = 0.85f, clusterIrregularity = 0.5f,
                speciesClumping = 0.6f, speciesPatchSize = 28f
            },
            // Serrapilheira: quase uniforme (gravetos caem em todo lugar), só uma
            // ondulação leve p/ juntar folhas em "bolsões".
            new()
            {
                name = "Floresta — serrapilheira", biome = Biome.Floresta, prefabs = forestLitterPrefabs,
                attemptsPerTile = forestLitterPerTile, minBiomeWeight = 0.35f, density = 1f,
                scaleRange = new Vector2(0.8f, 1.3f), aspectJitter = 0.2f, maxSlope = 0.55f,
                clusterStrength = 0.3f, clusterSize = 60f, clusterCoverage = 0.5f,
                clusterEdgeSoftness = 0.9f, clusterIrregularity = 0.4f
            },

            // ---------------- CAMPOS (esparsos) ----------------
            // Árvores: em vez do polvilhado regular, capões raros (grupos de 2–5) com
            // muitos tiles quase vazios entre eles — silhueta clássica de savana/campo.
            new()
            {
                name = "Campos — árvores isoladas", biome = Biome.Campos, prefabs = treePrefabs,
                avoidBiomeMax = 0.25f,   // nunca sobre areia dominante
                speciesClumping = 0.35f, speciesPatchSize = 120f,   // capões com identidade
                attemptsPerTile = 14, minBiomeWeight = 0.5f, density = 0.9f,
                scaleRange = new Vector2(0.85f, 1.4f), maxSlope = 0.5f,
                minSpacing = 18f, landmark = true,
                clusterStrength = 0.55f, clusterSize = 90f, clusterCoverage = 0.18f,
                clusterEdgeSoftness = 0.5f, clusterIrregularity = 0.5f
            },
            // Arbustos: ilhas de vegetação no capim aberto — poucas, grandes, de uma
            // espécie só; o resto do campo fica limpo de verdade.
            new()
            {
                name = "Campos — arbustos", biome = Biome.Campos, prefabs = bushPrefabs,
                avoidBiomeMax = 0.25f,
                attemptsPerTile = 46, minBiomeWeight = 0.5f, density = 0.9f,
                scaleRange = new Vector2(0.8f, 1.2f), maxSlope = 0.5f, minSpacing = 8f,
                clusterStrength = 0.85f, clusterSize = 50f, clusterCoverage = 0.18f,
                clusterEdgeSoftness = 0.6f, clusterIrregularity = 0.7f,
                speciesClumping = 0.6f, speciesPatchSize = 60f
            },
            // Graminhas: ondas LARGAS de pradaria (denso ↔ ralo bem gradual) e campos
            // de flores por região de espécie.
            new()
            {
                name = "Campos — graminhas", biome = Biome.Campos, prefabs = grassPrefabs,
                attemptsPerTile = grassPerTile, minBiomeWeight = 0.35f, density = 1f,
                scaleRange = new Vector2(0.9f, 1.6f), maxSlope = 0.5f,
                clusterStrength = 0.4f, clusterSize = 90f, clusterCoverage = 0.5f,
                clusterEdgeSoftness = 0.9f, clusterIrregularity = 0.4f,
                speciesClumping = 0.55f, speciesPatchSize = 70f
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
                scaleRange = new Vector2(1.8f, 4.5f), aspectJitter = 0.2f,
                // minSlope 0.5 (era 0.35): a borda convexa do TOPO das mesetas
                // qualificava e as formações coroavam os cumes como cogumelos
                // em balanço — 0.5 só existe nas paredes de verdade.
                minSlope = 0.5f, maxSlope = 99f,
                clusterStrength = 0.35f, clusterSize = 130f, clusterGroup = 0,
                clusterIrregularity = 0.5f,
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
                clusterIrregularity = 0.6f,
                minSpacing = 6f, blockRadius = 6f
            },
            new()
            {
                name = "Deserto — pedras médias", biome = Biome.Deserto,
                prefabs = desertRockPrefabs,
                attemptsPerTile = Mathf.RoundToInt(60 * d), minBiomeWeight = 0.4f, density = 0.6f,
                scaleRange = new Vector2(0.7f, 1.3f), aspectJitter = 0.2f, maxSlope = 0.6f,
                clusterStrength = 0.5f, clusterSize = 130f, clusterGroup = 0,   // junto das formações
                clusterIrregularity = 0.5f,
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
                clusterEdgeSoftness = 0.75f, clusterIrregularity = 0.6f,
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

            // ---------------- TUNDRA (Winter Environment) ----------------
            // Calibrada com os DADOS REAIS das Demos do pack (por tile de 250 m,
            // média bruta ↔ efetiva dentro dos corredores vestidos): ~232↔6.695
            // grupos de grama, ~165↔2.576 arbustos, ~68↔631 árvores, ~32 pedras,
            // ~10 afloramentos, ~19 madeiras caídas, ~18 debris. Escalas medianas
            // ~1.0 (árvores), 0.9 (arbustos), 0.73 (grama), SEM aspect jitter
            // (100% das árvores da demo têm width == height).
            // A mata principal da demo é Tree_B (pinheiros) com TAPETE DE FOLHAS
            // no chão — o groundPaint 6 reproduz isso sob os bosques daqui.
            // Ordem importa: afloramentos primeiro (reservam espaço).
            new()
            {
                name = "Tundra — afloramentos", biome = Biome.Tundra,
                prefabs = winterOutcropPrefabs,
                attemptsPerTile = Mathf.RoundToInt(30 * w), minBiomeWeight = 0.4f, density = 0.5f,
                scaleRange = new Vector2(0.8f, 1.9f), maxSlope = 0.4f,
                clusterStrength = 0.6f, clusterSize = 140f, clusterGroup = 3,   // morros rochosos
                clusterCoverage = 0.25f, clusterEdgeSoftness = 0.5f, clusterIrregularity = 0.5f,
                minSpacing = 12f, blockRadius = 6f
            },
            new()
            {
                name = "Tundra — pedras", biome = Biome.Tundra, prefabs = winterRockPrefabs,
                attemptsPerTile = Mathf.RoundToInt(120 * w), minBiomeWeight = 0.35f, density = 0.7f,
                scaleRange = new Vector2(0.35f, 1.9f), aspectJitter = 0.2f, maxSlope = 0.7f,
                clusterStrength = 0.3f, clusterSize = 140f, clusterGroup = 3,   // espalha pelo aberto
                clusterIrregularity = 0.5f
            },
            // Margem do lago congelado: a faixa de 2 m acima da lâmina. Sem ela a
            // placa encosta na grama sem transição — e é ali que a pedra fica
            // naturalmente (o gelo empurra o material solto para a beira todo
            // inverno). Densidade alta, escala pequena: é orla, não afloramento.
            new()
            {
                name = "Tundra — pedras da margem do gelo", biome = Biome.Tundra,
                prefabs = winterRockPrefabs,
                attemptsPerTile = Mathf.RoundToInt(150 * w), minBiomeWeight = 0.35f, density = 0.85f,
                scaleRange = new Vector2(0.3f, 1.2f), aspectJitter = 0.25f, maxSlope = 0.8f,
                heightRange = new Vector2(waterLevel + 0.12f, waterLevel + 2.2f),
                minSpacing = 1.6f
            },
            // Pedras ENCRAVADAS na placa: o marco visual que dá escala ao lago
            // liso e prova que ele é sólido. Raras de propósito — uma a cada
            // ~2 tentativas em 40, agrupadas, para virar "campo de pedras" em vez
            // de pontilhado uniforme. Escala pequena: matacão em cima do gelo
            // não convence, seixo/laje convence.
            new()
            {
                name = "Tundra — pedras no gelo", biome = Biome.Tundra,
                prefabs = winterRockPrefabs,
                attemptsPerTile = Mathf.RoundToInt(70 * w), minBiomeWeight = 0.5f, density = 0.35f,
                scaleRange = new Vector2(0.25f, 1.0f), aspectJitter = 0.3f,
                maxSlope = 1f,                                  // o leito inclina; o gelo é plano
                clusterStrength = 0.75f, clusterSize = 60f, clusterGroup = 4,
                clusterCoverage = 0.3f, clusterEdgeSoftness = 0.6f, clusterIrregularity = 0.6f,
                minSpacing = 2.5f,
                onFrozenIce = true
            },
            // Bosques: pinheiros altos (Tree_B) são a espinha dorsal — manchas
            // GRANDES com borda suave; o chão sob elas vira folhas congeladas
            // (groundPaint = canal 6, a MESMA máscara de noise).
            new()
            {
                name = "Tundra — bosques (pinheiros)", biome = Biome.Tundra,
                prefabs = winterPinePrefabs,
                attemptsPerTile = Mathf.RoundToInt(380 * w), minBiomeWeight = 0.4f, density = 0.95f,
                scaleRange = new Vector2(0.9f, 1.8f), maxSlope = 0.5f,   // +30%: escala p/ dragão
                clusterStrength = 0.8f, clusterSize = 150f, clusterGroup = 1,
                clusterCoverage = 0.42f, clusterEdgeSoftness = 0.65f, clusterIrregularity = 0.5f,
                speciesClumping = 0.35f, speciesPatchSize = 90f,
                minSpacing = 3.5f, landmark = true, groundPaint = 6
            },
            // Abetos densos (Tree_A): acento DENTRO dos mesmos bosques — trechos
            // de mata fechada azulada no meio do pinheiral ralo.
            new()
            {
                name = "Tundra — abetos", biome = Biome.Tundra, prefabs = winterFirPrefabs,
                attemptsPerTile = Mathf.RoundToInt(90 * w), minBiomeWeight = 0.4f, density = 0.7f,
                scaleRange = new Vector2(1f, 1.6f), maxSlope = 0.5f,
                clusterStrength = 0.7f, clusterSize = 150f, clusterGroup = 1,
                clusterCoverage = 0.42f, clusterEdgeSoftness = 0.65f, clusterIrregularity = 0.5f,
                speciesClumping = 0.5f, speciesPatchSize = 70f,
                minSpacing = 4f, landmark = true
            },
            // Bétulas (Tree_C): capões PRÓPRIOS nas áreas abertas — grupos raros e
            // fechados de decíduas nuas, longe do miolo dos pinheirais.
            new()
            {
                name = "Tundra — capões de bétulas", biome = Biome.Tundra,
                prefabs = winterBirchPrefabs,
                attemptsPerTile = Mathf.RoundToInt(120 * w), minBiomeWeight = 0.45f, density = 0.8f,
                scaleRange = new Vector2(0.95f, 1.8f), maxSlope = 0.5f,
                clusterStrength = 0.8f, clusterSize = 70f,
                clusterCoverage = 0.16f, clusterEdgeSoftness = 0.55f, clusterIrregularity = 0.6f,
                speciesClumping = 0.5f, speciesPatchSize = 50f,
                minSpacing = 4f, landmark = true
            },
            // Madeira caída: troncos/tocos/raízes viradas na borda e dentro da
            // mata (compartilham o noise dos bosques, mais fraco = vazam p/ fora).
            new()
            {
                name = "Tundra — madeira caída", biome = Biome.Tundra,
                prefabs = winterDeadwoodPrefabs,
                attemptsPerTile = Mathf.RoundToInt(90 * w), minBiomeWeight = 0.4f, density = 0.65f,
                scaleRange = new Vector2(0.5f, 1.2f), aspectJitter = 0.15f, maxSlope = 0.5f,
                clusterStrength = 0.35f, clusterSize = 150f, clusterGroup = 1,
                clusterCoverage = 0.42f, clusterEdgeSoftness = 0.75f, clusterIrregularity = 0.5f,
                minSpacing = 5f, avoidBlockers = 1f
            },
            // Arbustos: o preenchimento onipresente da demo (165/tile) — manchas
            // médias irregulares, uma espécie dominante por mancha.
            new()
            {
                name = "Tundra — arbustos", biome = Biome.Tundra, prefabs = winterBushPrefabs,
                attemptsPerTile = Mathf.RoundToInt(560 * w), minBiomeWeight = 0.4f, density = 0.9f,
                scaleRange = new Vector2(0.3f, 1.4f), maxSlope = 0.55f,
                clusterStrength = 0.45f, clusterSize = 60f,   // mais vazamento p/ o aberto
                clusterCoverage = 0.45f, clusterEdgeSoftness = 0.75f, clusterIrregularity = 0.6f,
                speciesClumping = 0.5f, speciesPatchSize = 40f
            },
            // Grama seca: a categoria mais numerosa da demo (232/tile, mediana
            // 0.73) — tufos furando a neve em ondas largas, com regiões de UMA
            // variedade (A/B/C) como campos naturais.
            new()
            {
                name = "Tundra — grama seca", biome = Biome.Tundra, prefabs = winterGrassPrefabs,
                attemptsPerTile = Mathf.RoundToInt(1000 * w), minBiomeWeight = 0.35f, density = 1f,
                scaleRange = new Vector2(0.45f, 1.25f), maxSlope = 0.55f,
                clusterStrength = 0.45f, clusterSize = 55f,
                clusterCoverage = 0.6f, clusterEdgeSoftness = 0.85f, clusterIrregularity = 0.5f,
                speciesClumping = 0.6f, speciesPatchSize = 45f
            },
            // Debris: galhada fina sob os bosques — a serrapilheira do inverno.
            new()
            {
                name = "Tundra — debris", biome = Biome.Tundra, prefabs = winterDebrisPrefabs,
                attemptsPerTile = Mathf.RoundToInt(170 * w), minBiomeWeight = 0.35f, density = 0.7f,
                scaleRange = new Vector2(0.4f, 1.3f), aspectJitter = 0.25f, maxSlope = 0.6f,
                clusterStrength = 0.3f, clusterSize = 150f, clusterGroup = 1,   // galhada também no aberto
                clusterCoverage = 0.42f, clusterEdgeSoftness = 0.8f, clusterIrregularity = 0.5f
            },

            // ---------------- MONTANHA (Mountain Environment - NM) ----------------
            // Zonação alpina calibrada pela Demo NM (256 m ≈ 1 tile: 4.2k pines
            // pintadas a wScale 0.5–0.66, 1.6k cogumelos, 2.4k galhos, 700 raízes
            // em slope p90 37°, muralhas escala 5–49 nas paredes). Bandas de
            // altura: vale/mata < 52 m · krummholz 45–75 · abetos nevados do
            // Winter 52–80 · acima só rocha, pedra e neve (h01 > 0.75 pinta neve).
            // Ordem: muralhas primeiro (reservam espaço).
            new()
            {
                name = "Montanha — muralhas", biome = Biome.Montanha,
                prefabs = mountainBoulderPrefabs,
                attemptsPerTile = Mathf.RoundToInt(220 * n), minBiomeWeight = 0.4f, density = 0.7f,
                scaleRange = new Vector2(2.5f, 7f), aspectJitter = 0.2f,
                minSlope = 0.45f, maxSlope = 99f,      // revestem penhascos e quebras
                clusterStrength = 0.35f, clusterSize = 150f, clusterGroup = 5,
                clusterIrregularity = 0.5f,
                minSpacing = 6f, blockRadius = 6f
            },
            new()
            {
                name = "Montanha — afloramentos", biome = Biome.Montanha,
                prefabs = mountainBoulderPrefabs,
                attemptsPerTile = Mathf.RoundToInt(40 * n), minBiomeWeight = 0.45f, density = 0.5f,
                scaleRange = new Vector2(1f, 2.5f), aspectJitter = 0.15f, maxSlope = 0.45f,
                clusterStrength = 0.8f, clusterSize = 150f, clusterGroup = 5,
                clusterIrregularity = 0.6f,
                minSpacing = 8f, blockRadius = 5f
            },
            // Mata de pinheiros dos vales: a regra de ouro do autor — árvores a
            // ~55% da escala do prefab (a Demo NUNCA as usa em 1.0).
            new()
            {
                name = "Montanha — mata de pinheiros", biome = Biome.Montanha,
                prefabs = mountainPinePrefabs,
                attemptsPerTile = Mathf.RoundToInt(300 * n), minBiomeWeight = 0.4f, density = 0.95f,
                scaleRange = new Vector2(0.4f, 0.9f), maxSlope = 0.55f,
                heightRange = new Vector2(-1000f, 52f),
                clusterStrength = 0.75f, clusterSize = 160f, clusterGroup = 4,
                clusterCoverage = 0.5f, clusterEdgeSoftness = 0.7f, clusterIrregularity = 0.5f,
                speciesClumping = 0.35f, speciesPatchSize = 90f,
                minSpacing = 3f, landmark = true, groundPaint = 8   // agulhas sob a mata
            },
            new()
            {
                name = "Montanha — mudas e jovens", biome = Biome.Montanha,
                prefabs = mountainSaplingPrefabs,
                attemptsPerTile = Mathf.RoundToInt(160 * n), minBiomeWeight = 0.4f, density = 0.8f,
                scaleRange = new Vector2(0.4f, 1f), maxSlope = 0.55f,
                heightRange = new Vector2(-1000f, 58f),
                clusterStrength = 0.5f, clusterSize = 160f, clusterGroup = 4,
                clusterCoverage = 0.5f, clusterEdgeSoftness = 0.8f, clusterIrregularity = 0.5f,
                minSpacing = 2f
            },
            // Hero pines: pinheiros agarrados nas paredes de pedra (a Demo os
            // coloca à mão com 34–40% em slope > 45° — aqui o filtro faz isso).
            new()
            {
                name = "Montanha — pinheiros das encostas", biome = Biome.Montanha,
                prefabs = mountainPinePrefabs,
                attemptsPerTile = Mathf.RoundToInt(40 * n), minBiomeWeight = 0.45f, density = 0.5f,
                scaleRange = new Vector2(0.35f, 0.7f),
                minSlope = 0.5f, maxSlope = 99f, heightRange = new Vector2(-1000f, 78f),
                minSpacing = 8f, avoidBlockers = 1f
            },
            // Krummholz: pinheiros anões e aveleiras na faixa da treeline.
            new()
            {
                name = "Montanha — krummholz", biome = Biome.Montanha,
                prefabs = mountainBushPrefabs,
                attemptsPerTile = Mathf.RoundToInt(120 * n), minBiomeWeight = 0.4f, density = 0.8f,
                scaleRange = new Vector2(0.7f, 1.3f), maxSlope = 0.6f,
                heightRange = new Vector2(45f, 75f),
                clusterStrength = 0.6f, clusterSize = 60f,
                clusterCoverage = 0.35f, clusterEdgeSoftness = 0.7f, clusterIrregularity = 0.6f
            },
            new()
            {
                name = "Montanha — grama e urze", biome = Biome.Montanha,
                prefabs = mountainGrassPrefabs,
                attemptsPerTile = Mathf.RoundToInt(500 * n), minBiomeWeight = 0.35f, density = 1f,
                scaleRange = new Vector2(0.6f, 1.5f), maxSlope = 0.5f,
                heightRange = new Vector2(-1000f, 60f),
                clusterStrength = 0.45f, clusterSize = 55f,
                clusterCoverage = 0.5f, clusterEdgeSoftness = 0.85f, clusterIrregularity = 0.5f,
                speciesClumping = 0.6f, speciesPatchSize = 40f
            },
            new()
            {
                name = "Montanha — cogumelos", biome = Biome.Montanha,
                prefabs = mountainMushroomPrefabs,
                attemptsPerTile = Mathf.RoundToInt(120 * n), minBiomeWeight = 0.4f, density = 0.8f,
                scaleRange = new Vector2(0.5f, 1.5f), maxSlope = 0.5f,
                heightRange = new Vector2(-1000f, 55f),
                clusterStrength = 0.6f, clusterSize = 160f, clusterGroup = 4,   // sob a mata
                clusterCoverage = 0.5f, clusterEdgeSoftness = 0.8f, clusterIrregularity = 0.5f,
                speciesClumping = 0.6f, speciesPatchSize = 30f
            },
            new()
            {
                name = "Montanha — serrapilheira", biome = Biome.Montanha,
                prefabs = mountainDetailPrefabs,
                attemptsPerTile = Mathf.RoundToInt(180 * n), minBiomeWeight = 0.35f, density = 0.9f,
                scaleRange = new Vector2(0.5f, 1.3f), aspectJitter = 0.2f, maxSlope = 0.55f,
                heightRange = new Vector2(-1000f, 58f),
                clusterStrength = 0.45f, clusterSize = 160f, clusterGroup = 4,
                clusterCoverage = 0.5f, clusterEdgeSoftness = 0.85f, clusterIrregularity = 0.4f
            },
            // Raízes/tocos nos barrancos — a Demo os usa a slope p90 = 37°.
            new()
            {
                name = "Montanha — raízes de barranco", biome = Biome.Montanha,
                prefabs = mountainDeadwoodPrefabs,
                attemptsPerTile = Mathf.RoundToInt(70 * n), minBiomeWeight = 0.4f, density = 0.7f,
                scaleRange = new Vector2(0.5f, 1.1f), aspectJitter = 0.15f,
                minSlope = 0.2f, maxSlope = 0.9f, heightRange = new Vector2(-1000f, 70f),
                minSpacing = 5f, avoidBlockers = 1f
            },
            new()
            {
                name = "Montanha — pedras", biome = Biome.Montanha,
                prefabs = mountainRockPrefabs,
                attemptsPerTile = Mathf.RoundToInt(140 * n), minBiomeWeight = 0.35f, density = 0.7f,
                scaleRange = new Vector2(0.4f, 1.8f), aspectJitter = 0.2f, maxSlope = 0.75f,
                clusterStrength = 0.4f, clusterSize = 150f, clusterGroup = 5,   // junto do rochedo
                clusterIrregularity = 0.5f
            },
            // Seixos de rio: SÓ no leito/margens das torrentes (inStreamBed) —
            // podem ficar submersos, como os 6.4k river stones da Demo.
            new()
            {
                name = "Montanha — seixos do rio", biome = Biome.Montanha,
                prefabs = mountainRiverStonePrefabs,
                attemptsPerTile = Mathf.RoundToInt(400 * n), minBiomeWeight = 0.3f, density = 0.9f,
                scaleRange = new Vector2(0.5f, 1.6f), aspectJitter = 0.25f, maxSlope = 0.8f,
                inStreamBed = true
            },
            // Decals de chão: reforço de textura APENAS em patamares planos,
            // em pequenos grupos (a Demo nunca os espalha uniformemente).
            new()
            {
                name = "Montanha — decals", biome = Biome.Montanha,
                prefabs = mountainDecalPrefabs,
                attemptsPerTile = Mathf.RoundToInt(20 * n), minBiomeWeight = 0.5f, density = 0.4f,
                scaleRange = new Vector2(0.8f, 1.3f), maxSlope = 0.08f,
                clusterStrength = 0.8f, clusterSize = 40f, clusterCoverage = 0.15f,
                minSpacing = 4f
            },
            // Estátua Sviatovid: landmark RARÍSSIMO — só platôs planos a meia
            // altura; achar uma é evento (aparece no minimapa como marco).
            new()
            {
                name = "Montanha — estátua (landmark)", biome = Biome.Montanha,
                prefabs = mountainStatuePrefabs,
                attemptsPerTile = 2, minBiomeWeight = 0.7f, density = 0.06f,
                scaleRange = new Vector2(1.3f, 1.6f), maxSlope = 0.08f,
                heightRange = new Vector2(40f, 70f),
                minSpacing = 200f, avoidBlockers = 3f, landmark = true
            },

            // Treeline nevada (Winter pack): abetos acima da mata de pinheiros —
            // a transição para o topo branco; pedras nevadas perto dos picos.
            new()
            {
                name = "Montanha — treeline (abetos nevados)", biome = Biome.Montanha,
                prefabs = winterFirPrefabs,
                attemptsPerTile = Mathf.RoundToInt(60 * w), minBiomeWeight = 0.45f, density = 0.7f,
                scaleRange = new Vector2(1f, 1.5f), maxSlope = 0.55f,
                heightRange = new Vector2(52f, 80f),
                clusterStrength = 0.4f, clusterSize = 120f,
                clusterEdgeSoftness = 0.7f, clusterIrregularity = 0.5f,
                minSpacing = 6f, landmark = true
            },
            new()
            {
                name = "Montanha — pedras nevadas", biome = Biome.Montanha,
                prefabs = winterRockPrefabs,
                attemptsPerTile = Mathf.RoundToInt(50 * w), minBiomeWeight = 0.4f, density = 0.7f,
                scaleRange = new Vector2(0.7f, 1.8f), aspectJitter = 0.25f,
                maxSlope = 0.8f, heightRange = new Vector2(75f, 1000f)
            },
        };
    }

    // ---------------------------------------------------------------- TILES
    Vector2Int TileOf(Vector3 pos) =>
        new(Mathf.FloorToInt(pos.x / tileSize), Mathf.FloorToInt(pos.z / tileSize));

    /// <summary>Construção SÍNCRONA (tile inicial do spawn) — drena os passos num frame.</summary>
    void BuildTile(Vector2Int coord)
    {
        var steps = BuildTileSteps(coord);
        while (steps.MoveNext()) { }
    }

    /// <summary>
    /// Representação em memória de UM tile gerado — só dados (TerrainData é um
    /// asset de dados, válido até em servidor headless), nada de objetos de cena.
    /// É a fronteira entre GERAR o mundo e MOSTRAR o mundo: um servidor
    /// autoritativo pararia aqui; o cliente segue para InstantiateTile.
    /// </summary>
    class TileData
    {
        public Vector2Int coord;
        public TerrainData terrain;                     // heightmap + splatmap + trees
        public Mesh ice;                                // placa de gelo da Tundra (null = tile sem gelo)
        public readonly List<Vector2> landmarks = new(); // XZ dos marcos (minimapa)
    }

    /// <summary>
    /// Construção FATIADA de um tile: gera os DADOS (GenerateTileSteps) e só
    /// então instancia o visual (InstantiateTile). Cada yield é um ponto de
    /// corte onde o pump do Update pode parar ao estourar o orçamento de ms.
    /// </summary>
    IEnumerator BuildTileSteps(Vector2Int coord)
    {
        var data = new TileData { coord = coord };
        var gen = GenerateTileSteps(data);
        while (gen.MoveNext()) yield return null;
        InstantiateTile(data);
    }

    /// <summary>
    /// GERAÇÃO DE DADOS do tile — determinística por (estado do mundo, coord),
    /// sem tocar na cena. O splatmap e o scatter leem o HEIGHTMAP JÁ CALCULADO
    /// (SampleHeight01/SlopeFromGrid) em vez de rechamar HeightAt/SlopeAt — era
    /// o custo dominante do tile (SlopeAt = 3×HeightAt ≈ 45 Perlin POR PIXEL).
    /// </summary>
    IEnumerator GenerateTileSteps(TileData data)
    {
        Vector2Int coord = data.coord;
        float ox = coord.x * tileSize, oz = coord.y * tileSize;

        // ---- alturas (bordas contínuas: noise em coordenadas de mundo)
        float step = tileSize / (heightmapRes - 1);
        var heights = new float[heightmapRes, heightmapRes];
        for (int z = 0; z < heightmapRes; z++)
        {
            for (int x = 0; x < heightmapRes; x++)
                heights[z, x] = HeightAt(ox + x * step, oz + z * step) / maxHeight;
            if ((z & 3) == 3) yield return null;    // fatia: 4 linhas de noise
        }

        var td = new TerrainData
        {
            heightmapResolution = heightmapRes,
            alphamapResolution = alphamapRes,
            size = new Vector3(tileSize, maxHeight, tileSize)
        };
        // (size precisa ser re-aplicado após heightmapResolution)
        td.size = new Vector3(tileSize, maxHeight, tileSize);
        td.terrainLayers = layers;
        td.SetHeights(0, 0, heights);
        yield return null;

        // ---- gelo da Tundra: placa sólida sobre a água congelada (só dados;
        //      renderer + collider nascem no InstantiateTile)
        if (freezeTundraWater && (enableLakes || StreamsActive))
        {
            var iceGen = BuildIceMeshSteps(data, heights, ox, oz);
            while (iceGen.MoveNext()) yield return null;
        }

        // ---- texturas por bioma + inclinação
        //      canais: 0 grama · 1 floresta · 2 rocha de montanha · 3 neve · 4 areia
        //      · 5 rocha do deserto · 6 folhas congeladas (só com o Winter pack)
        int nCh = layers.Length;
        var alphas = new float[alphamapRes, alphamapRes, nCh];
        var chAcc = new float[nCh];
        float aStep = tileSize / (alphamapRes - 1);
        float aNorm = 1f / (alphamapRes - 1);
        for (int z = 0; z < alphamapRes; z++)
        {
            float nz = z * aNorm;
            for (int x = 0; x < alphamapRes; x++)
            {
                float wx = ox + x * aStep, wz = oz + z * aStep;
                BiomeWeights(wx, wz, out float plains, out float forest, out float mount, out float cold, out float desert);

                float h01 = SampleHeight01(heights, heightmapRes, x * aNorm, nz);
                float slope = SlopeFromGrid(heights, heightmapRes, x * aNorm, nz);
                float slopeRock = Mathf.Clamp01(slope * 2.2f - 0.35f);
                float snow = cold + Mathf.Clamp01((h01 - 0.75f) * 4f);    // neve só em picos altos/tundra
                float dRock = desert * Mathf.Clamp01(slope * 2.6f - 0.3f); // penhascos de arenito
                float sand = Mathf.Max(0f, desert - dRock);

                // Montanha: com as layers do Mountain pack (canais 7–9), o chão
                // vira zonação alpina real — musgo nos vales, solo a meia altura,
                // rocha nua no alto/íngreme (Demo NM: Moss 32% + agulhas 30% +
                // solo 24%; as agulhas entram sob a mata via groundPaint).
                // Sem as layers, comportamento antigo: montanha 100% rocha.
                float rock = slopeRock * (1f - desert);   // encostas fora do deserto
                float mMoss = 0f, mSoil = 0f;
                if (nCh >= 10 && mount > 0.001f)
                {
                    float hM = h01 * maxHeight;
                    float soft = mount * (1f - slopeRock);
                    float higher = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(38f, 52f, hM));
                    float bare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(72f, 92f, hM));
                    mMoss = soft * (1f - higher);
                    mSoil = soft * higher * (1f - bare);
                    rock += mount * slopeRock + soft * higher * bare;
                }
                else rock += mount;

                // riachos: leito pinta rocha molhada, margens do vale pintam terra
                float sBed = 0f, sBank = 0f;
                StreamPaintMasks(wx, wz, plains, forest, mount, cold, desert, ref sBed, ref sBank);

                // chão da floresta: grama como base, terra só em manchas de noise
                // (antes era 100% terra e o sub-bosque ficava marrom demais).
                // 2ª octava quebra o contorno redondo do Perlin; a 3ª (fina, ~3 m)
                // esfarela a borda — precisa do alphamapRes 128 p/ ser resolvida.
                float dn = Mathf.PerlinNoise(wx * 0.025f + oxM + 91.3f, wz * 0.025f + ozM + 47.9f);
                dn += (Mathf.PerlinNoise(wx * 0.08f + oxM + 217.4f, wz * 0.08f + ozM + 133.8f) - 0.5f) * 0.35f;
                dn += (Mathf.PerlinNoise(wx * 0.35f + oxM + 411.7f, wz * 0.35f + ozM + 305.2f) - 0.5f) * 0.15f;
                float dirtPatch = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(forestDirtThreshold, forestDirtThreshold + 0.12f, dn));
                float dirtShare = Mathf.Lerp(1f - forestGrassShare, 1f, dirtPatch);

                Array.Clear(chAcc, 0, nCh);
                // Campos: manchas raras de terra batida (55% de blend — desgaste,
                // não careca). Único bioma de canal ÚNICO em campo aberto — por
                // isso só aqui o tiling da grama aparecia. Reusa o noise 'dn'.
                float wear = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.78f, 0.90f, dn)) * 0.55f;
                chAcc[0] = plains * (1f - wear) + forest * (1f - dirtShare);
                chAcc[1] = plains * wear + forest * dirtShare + sBank * 1.6f;
                chAcc[2] = rock + sBed * 1.4f;
                chAcc[3] = snow;
                chAcc[4] = sand;
                chAcc[5] = dRock;
                if (nCh >= 10) { chAcc[7] = mMoss; chAcc[9] = mSoil; }

                // camadas com groundPaint: a MESMA mancha de agrupamento que junta
                // os objetos pinta o chão sob eles (folhas congeladas sob os bosques
                // da Tundra). Sem o 7º canal (MicroSplat antigo), cai na terra (1).
                if (groundPaintLayers != null)
                    for (int i = 0; i < groundPaintLayers.Count; i++)
                    {
                        var pl = groundPaintLayers[i];
                        float bw = PickWeight(pl.biome, plains, forest, mount, cold, desert);
                        if (bw < 0.05f) continue;
                        float mask = ClusterMask(pl, clusterOffsets[pl.clusterGroup], wx, wz);
                        int ch = pl.groundPaint < nCh ? pl.groundPaint : 1;
                        // ^1.5 concentra no miolo da mancha; 2.2 deixa as folhas
                        // dominarem a neve lá dentro (borda continua nevada)
                        chAcc[ch] += Mathf.Pow(mask, 1.5f) * bw * 2.2f;
                    }

                float sum = 0.0001f;
                for (int c = 0; c < nCh; c++) sum += chAcc[c];
                for (int c = 0; c < nCh; c++) alphas[z, x, c] = chAcc[c] / sum;
            }
            if ((z & 7) == 7) yield return null;    // fatia: 8 linhas (loop barato agora)
        }
        td.SetAlphamaps(0, 0, alphas);
        yield return null;

        // ---- vegetação/props: camadas de espalhamento (tree instances: o detail
        //      system do Unity não aceita prefabs com LODGroup e falhava em silêncio)
        if (prototypes != null && prototypes.Length > 0)
        {
            td.treePrototypes = prototypes;
            td.RefreshPrototypes();
            yield return null;
            var scatter = ScatterTileSteps(coord, td, ox, oz, heights, data.landmarks);
            while (scatter.MoveNext()) yield return null;
        }
        data.terrain = td;
    }

    // -------------------------------------------------------- GELO DA TUNDRA
    const float IceShoreMargin = 0.30f;     // avança na margem: borda do gelo fica enterrada no barranco
    const float IceShoreBury = 0.22f;       // quanto a borda afunda DENTRO do barranco (m)

    /// <summary>
    /// PLACA DE GELO do tile — a "pista" da Tundra.
    ///
    /// Cobre só as células com terreno submerso onde o peso do bioma frio passa
    /// do limiar. A máscara usa o MESMO Mathf.PerlinNoise da geração
    /// (BiomeWeights), na CPU — nunca replicar em shader: o Perlin da GPU não
    /// bate com o da Unity, e aí gameplay (nadar/pousar) e visual discordariam.
    ///
    /// A placa NÃO é mais um plano. Três coisas a tiram do "decalque chapado":
    ///
    ///  1. RELEVO DE MUNDO — encurvamento largo + cristas de pressão (ruído
    ///     ridged) + rugosidade fina. Como é ruído de mundo, NUNCA se repete:
    ///     é a única variação que nenhuma textura consegue dar.
    ///  2. UV COM WARP — o domínio da textura serpenteia (ruído de baixa
    ///     frequência em coordenadas de mundo). O tile de 12 m continua lá, mas
    ///     deformado de forma diferente em cada lugar: a repetição some.
    ///  3. BORDAS QUE SOMEM — na margem a placa afunda DENTRO do barranco; no
    ///     limite do bioma frio ela MERGULHA sob a lâmina d'água. Nos dois casos
    ///     a aresta serrilhada de 2 m fica escondida em vez de flutuar.
    ///
    /// Normal e tangente saem analiticamente do próprio relevo (RecalculateNormals
    /// erraria nas bordas do tile, onde faltam vizinhos) — sem tangente o normal
    /// map do gelo simplesmente não funcionaria.
    /// </summary>
    IEnumerator BuildIceMeshSteps(TileData data, float[,] heights, float ox, float oz)
    {
        int res = heightmapRes;
        float step = tileSize / (res - 1);
        float wet01 = (waterLevel + IceShoreMargin) / maxHeight;
        float tile = Mathf.Max(0.5f, iceTextureTile);

        var vertIdx = new int[res * res];           // cantos compartilhados entre células
        for (int i = 0; i < vertIdx.Length; i++) vertIdx[i] = -1;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var tris = new List<int>();

        int VertAt(int x, int z)
        {
            int k = z * res + x;
            if (vertIdx[k] >= 0) return vertIdx[k];
            vertIdx[k] = verts.Count;

            float wx = ox + x * step, wz = oz + z * step;
            float y = waterLevel + iceSurfaceOffset + IceRelief(wx, wz);

            // borda do BIOMA: a placa mergulha sob a lâmina antes de acabar
            BiomeWeights(wx, wz, out _, out _, out _, out float cold, out _);
            float solid = iceEdgeFade > 0.001f
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(frozenColdThreshold, frozenColdThreshold + iceEdgeFade, cold))
                : 1f;
            y -= (1f - solid) * (iceSurfaceOffset + 0.30f);

            // borda da MARGEM: onde o terreno já saiu da água, enterra no barranco
            float gh = heights[z, x] * maxHeight;
            if (gh > waterLevel - 0.05f) y = Mathf.Min(y, gh - IceShoreBury);

            verts.Add(new Vector3(x * step, y, z * step));   // tile em Y=0: local == mundo

            // UV de MUNDO com warp — contínua entre tiles vizinhos e sem repetir
            float wu = 0f, wv = 0f;
            if (iceUvWarp > 0.001f)
            {
                wu = (Mathf.PerlinNoise(wx * 0.011f + 13.7f, wz * 0.011f + 91.2f) - 0.5f) * 2f * iceUvWarp;
                wv = (Mathf.PerlinNoise(wx * 0.011f + 57.4f, wz * 0.011f + 22.9f) - 0.5f) * 2f * iceUvWarp;
            }
            uvs.Add(new Vector2((wx + wu) / tile, (wz + wv) / tile));

            var n = IceNormal(wx, wz);
            normals.Add(n);
            // UV.u cresce em +X e UV.v em +Z; com w=-1 a bitangente da Unity
            // (cross(n,t)*w) aponta para +Z, que é o que o normal map espera
            var t = (Vector3.right - n * n.x).normalized;
            tangents.Add(new Vector4(t.x, t.y, t.z, -1f));
            return vertIdx[k];
        }

        for (int z = 0; z < res - 1; z++)
        {
            for (int x = 0; x < res - 1; x++)
            {
                // célula entra se ALGUM canto está submerso (margem inclusa)...
                if (heights[z, x] >= wet01 && heights[z, x + 1] >= wet01 &&
                    heights[z + 1, x] >= wet01 && heights[z + 1, x + 1] >= wet01) continue;
                // ...e o centro dela é frio o bastante (MESMO corte do IsWaterFrozenAt)
                BiomeWeights(ox + (x + 0.5f) * step, oz + (z + 0.5f) * step,
                             out _, out _, out _, out float cold, out _);
                if (cold < frozenColdThreshold) continue;

                int a = VertAt(x, z), b = VertAt(x + 1, z);
                int c = VertAt(x, z + 1), d = VertAt(x + 1, z + 1);
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
            if ((z & 3) == 3) yield return null;    // fatia: 4 linhas (o vértice ficou mais caro)
        }
        if (tris.Count == 0) yield break;           // tile sem água congelada

        var mesh = new Mesh { name = $"Ice {data.coord.x},{data.coord.y}" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.SetTriangles(tris, 0);
        data.ice = mesh;
    }

    /// <summary>
    /// Relevo da placa (m). Gelo de lago nunca é um plano: a lâmina encurva com o
    /// nível da água, as placas se comprimem e formam CRISTAS DE PRESSÃO (o ruído
    /// ridged), e a superfície ainda tem uma ondulação fina de congelamento.
    /// Tudo em coordenadas de mundo — contínuo entre tiles e sem repetição.
    /// </summary>
    float IceRelief(float wx, float wz)
    {
        if (iceRelief <= 0.0001f) return 0f;
        float swell = (Mathf.PerlinNoise(wx * 0.035f + 311.2f, wz * 0.035f + 17.8f) - 0.5f) * 2f;
        float r = Mathf.PerlinNoise(wx * 0.09f + 77.1f, wz * 0.09f + 143.6f);
        float ridge = 1f - Mathf.Abs(r * 2f - 1f);
        ridge = ridge * ridge * ridge;                      // afina as cristas
        float fine = (Mathf.PerlinNoise(wx * 0.42f + 5.3f, wz * 0.42f + 88.4f) - 0.5f) * 2f;
        return (swell * 0.55f + ridge * 0.75f + fine * 0.12f) * iceRelief;
    }

    /// <summary>Normal analítica do relevo (diferenças centrais a 0,5 m). As bordas
    /// de margem/bioma são ignoradas de propósito: aquela geometria fica enterrada.</summary>
    Vector3 IceNormal(float wx, float wz)
    {
        if (iceRelief <= 0.0001f) return Vector3.up;
        const float d = 0.5f;
        float hl = IceRelief(wx - d, wz), hr = IceRelief(wx + d, wz);
        float hd = IceRelief(wx, wz - d), hu = IceRelief(wx, wz + d);
        return new Vector3(-(hr - hl) / (2f * d), 1f, -(hu - hd) / (2f * d)).normalized;
    }

    Material iceRuntimeMat;

    /// <summary>Material do gelo: o serializado (Tools &gt; Everwyrm &gt; Gelo gera o
    /// M_Ice_Lake com texturas + refração), ou um HDRP/Lit mínimo criado em runtime
    /// como rede de segurança — sem ele um projeto sem os assets renderizaria rosa.</summary>
    Material IceMaterial => iceMaterial != null ? iceMaterial
        : iceRuntimeMat != null ? iceRuntimeMat : (iceRuntimeMat = CreateIceMaterial());

    static Material CreateIceMaterial()
    {
        var m = new Material(Shader.Find("HDRP/Lit")) { name = "M_Ice (runtime)" };
        m.SetColor("_BaseColor", new Color(0.62f, 0.74f, 0.82f));   // azulado frio
        m.SetFloat("_Smoothness", 0.92f);
        m.SetFloat("_Metallic", 0f);
        return m;
    }

    /// <summary>INSTANCIAÇÃO VISUAL do tile — a única parte que toca a cena.</summary>
    void InstantiateTile(TileData data)
    {
        Vector2Int coord = data.coord;
        tileTrees[coord] = data.landmarks;

        var go = Terrain.CreateTerrainGameObject(data.terrain);
        go.name = $"Tile {coord.x},{coord.y}";
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(coord.x * tileSize, 0f, coord.y * tileSize);

        var terrain = go.GetComponent<Terrain>();
        if (terrainMat != null) terrain.materialTemplate = terrainMat;
        terrain.allowAutoConnect = true;
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 8f;
        terrain.treeBillboardDistance = 180f;
        terrain.treeDistance = 700f;
        // packs autoram LOD0 valendo só à queima-roupa e a troca ficava na cara
        // do jogador — empurra TODAS as transições de LOD da vegetação 50% p/
        // longe (uniforme, sem reautorar os LODGroups dos packs)
        terrain.treeLODBiasMultiplier = 1.5f;

        if (microSplatMaterial != null)
        {
            // tiles nascem em runtime, então a conversão de editor não os alcança:
            // cada um recebe o componente apontando pro template compartilhado.
            // O Sync instancia o material e liga os alphamaps como control textures
            // (depois do drawInstanced acima — ele decide o per-pixel normal).
            var mst = go.AddComponent<JBooth.MicroSplat.MicroSplatTerrain>();
            mst.terrain = terrain;
            mst.templateMaterial = microSplatMaterial;
            mst.propData = microSplatPropData;
            mst.keywordSO = microSplatKeywords;
            mst.baseMapShader = microSplatBaseShader;
            mst.Sync();
        }

        // gelo da Tundra: placa visual + MeshCollider — o CharacterController do
        // dragão (e qualquer física) pousa/anda nela como em chão comum.
        // Sombra desligada: a placa é transparente e a lâmina d'água por baixo é
        // justamente o que se quer enxergar — uma sombra projetada a apagaria.
        if (data.ice != null)
        {
            var ice = new GameObject("Ice");
            ice.transform.SetParent(go.transform, false);
            ice.AddComponent<MeshFilter>().sharedMesh = data.ice;
            var mr = ice.AddComponent<MeshRenderer>();
            mr.sharedMaterial = IceMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            ice.AddComponent<MeshCollider>().sharedMesh = data.ice;
        }

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

        // água LÍMPIDA de oásis/lago raso: olhando de cima o Fresnel manda ver
        // ATRAVÉS — com o default (absorção 5 m) o fundo vira poço preto e mata
        // a leitura; límpida, mostra o leito de areia e o céu espelha no rasante
        lakeSurface.absorptionDistance = waterClarity;
        lakeSurface.scatteringColor = waterScatteringColor;
        lakeSurface.maxRefractionDistance = waterRefractionDistance;

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
        UpdateFrozenWaterMask(sx, sz);
    }

    // ------------------------------------------- ÁGUA PARADA SOB O GELO
    // A WaterSurface é UMA só para o mundo inteiro e não sabe onde congelou.
    // Enquanto a placa era opaca isso não importava; com a placa TRANSPARENTE,
    // ver marolas mexendo sob o gelo sólido estragaria a ilusão na hora.
    // O HDRP tem a Water Mask exatamente para isso: uma textura que atenua as
    // bandas de simulação por região. Preenchemos com a MESMA regra de bioma da
    // CPU — sob o gelo a lâmina fica de espelho, ao lado continua ondulando.
    const int FrozenMaskRes = 128;          // ~14 m/texel: sobra para amortecer onda
    Texture2D frozenMask;
    Color32[] frozenMaskPixels;
    Vector2 frozenMaskCenter = new(float.NaN, float.NaN);
    Coroutine frozenMaskJob;

    void UpdateFrozenWaterMask(float cx, float cz)
    {
        if (!iceCalmsWaterUnder || !freezeTundraWater) return;
        if (frozenMaskCenter.x == cx && frozenMaskCenter.y == cz) return;
        frozenMaskCenter = new Vector2(cx, cz);
        if (frozenMaskJob != null) StopCoroutine(frozenMaskJob);
        frozenMaskJob = StartCoroutine(BuildFrozenMaskSteps(cx, cz));
    }

    IEnumerator BuildFrozenMaskSteps(float cx, float cz)
    {
        if (frozenMask == null)
        {
            frozenMask = new Texture2D(FrozenMaskRes, FrozenMaskRes, TextureFormat.RGBA32, false, true)
            {
                name = "Frozen Water Mask",
                wrapMode = TextureWrapMode.Clamp,   // fora do quad: amostra a borda (branca = água normal)
                filterMode = FilterMode.Bilinear
            };
            frozenMaskPixels = new Color32[FrozenMaskRes * FrozenMaskRes];
        }

        float stepM = waterQuadSize / FrozenMaskRes;
        float x0 = cx - waterQuadSize * 0.5f + stepM * 0.5f;
        float z0 = cz - waterQuadSize * 0.5f + stepM * 0.5f;

        for (int j = 0; j < FrozenMaskRes; j++)
        {
            bool border = j == 0 || j == FrozenMaskRes - 1;
            for (int i = 0; i < FrozenMaskRes; i++)
            {
                byte v = 255;
                if (!border && i > 0 && i < FrozenMaskRes - 1)
                {
                    BiomeWeights(x0 + i * stepM, z0 + j * stepM,
                                 out _, out _, out _, out float cold, out _);
                    // 1 = água livre · 0 = congelada. A queda começa um pouco ANTES
                    // do limiar: a onda morre chegando na placa, não bate nela.
                    float open = Mathf.InverseLerp(frozenColdThreshold - 0.12f, frozenColdThreshold, cold);
                    v = (byte)(Mathf.Clamp01(1f - open) * 255f);
                }
                frozenMaskPixels[j * FrozenMaskRes + i] = new Color32(v, v, v, 255);
            }
            if ((j & 7) == 7) yield return null;    // fatia: 8 linhas por frame
        }

        frozenMask.SetPixels32(frozenMaskPixels);
        frozenMask.Apply(false);
        if (lakeSurface != null)
        {
            lakeSurface.waterMask = frozenMask;
            lakeSurface.waterMaskExtent = new Vector2(waterQuadSize, waterQuadSize);
            lakeSurface.waterMaskOffset = new Vector2(cx, cz);
        }
        frozenMaskJob = null;
    }

    // ------------------------------------------------------ NEVE AMBIENTE
    Transform snowfall;
    ParticleSystem[] snowfallPs;
    float snowfallNext;

    /// <summary>
    /// UMA partícula de neve do Winter pack seguindo o jogador — emite só na
    /// Tundra e na Montanha (o mesmo padrão da WaterSurface única dos lagos).
    /// </summary>
    void EnsureSnowfall()
    {
        if (winterSnowfallPrefab == null || snowfall != null) return;
        var go = Instantiate(winterSnowfallPrefab, transform);
        go.name = "Snowfall (Winter pack)";
        snowfall = go.transform;
        snowfallPs = go.GetComponentsInChildren<ParticleSystem>(true);

        // o prefab do pack cai uniforme demais (mesmo tamanho/velocidade, sem
        // vento) — vira "Particle System padrão". Variação: flocos de 0.5–1.8x,
        // velocidade 0.6–1.5x e turbulência de noise p/ deriva lateral.
        for (int i = 0; i < snowfallPs.Length; i++)
        {
            var main = snowfallPs[i].main;
            float size = main.startSize.mode == ParticleSystemCurveMode.TwoConstants
                ? main.startSize.constantMax : main.startSize.constant;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.8f);
            float speed = main.startSpeed.mode == ParticleSystemCurveMode.TwoConstants
                ? main.startSpeed.constantMax : main.startSpeed.constant;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed * 1.5f);

            var noise = snowfallPs[i].noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.3f;
            noise.damping = true;
        }
    }

    void UpdateSnowfall()
    {
        if (snowfall == null || Time.time < snowfallNext) return;
        snowfallNext = Time.time + 0.5f;    // amostrar bioma 2x/s basta

        BiomeWeights(player.position.x, player.position.z,
                     out _, out _, out float mount, out float cold, out _);
        bool snowing = cold > 0.45f || mount > 0.6f;
        snowfall.position = player.position;
        for (int i = 0; i < snowfallPs.Length; i++)
        {
            var em = snowfallPs[i].emission;
            if (em.enabled != snowing) em.enabled = snowing;
        }
    }

    // ------------------------------------------------ MOTOR DE ESPALHAMENTO
    /// <summary>
    /// Processa todas as ScatterLayers de um tile, em FATIAS (yield a cada bloco
    /// de tentativas). Determinístico por (seed, tile) — o fatiamento não muda a
    /// ordem de consumo do rng. Filtros do mais barato ao mais caro: bioma →
    /// agrupamento/densidade → relevo (do heightmap pronto) → espaçamento/bloqueio.
    /// </summary>
    IEnumerator ScatterTileSteps(Vector2Int coord, TerrainData td, float ox, float oz,
                                 float[,] heights, List<Vector2> treeXZ)
    {
        var rng = new System.Random(seed ^ (coord.x * 73856093) ^ (coord.y * 19349663));
        var instances = new List<TreeInstance>();
        var blockers = new List<Vector3>();          // x,z = posição · y = raio
        var spacingHash = new Dictionary<long, List<Vector2>>();

        int work = 0;
        foreach (var layer in activeLayers)
        {
            if (layer.protoCount == 0) continue;
            spacingHash.Clear();
            float cell = Mathf.Max(layer.minSpacing, 0.001f);
            Vector2 cOff = clusterOffsets[layer.clusterGroup];

            for (int i = 0; i < layer.attemptsPerTile; i++)
            {
                if (++work >= 500) { work = 0; yield return null; }   // fatia

                float nx = (float)rng.NextDouble(), nz = (float)rng.NextDouble();
                double roll = rng.NextDouble();      // consumir SEMPRE mantém o determinismo
                float wx = ox + nx * tileSize, wz = oz + nz * tileSize;

                // 1) peso do bioma — densidade cai suavemente rumo à borda.
                //    O limiar ganha um JITTER espacial (~±12%): sem ele, a borda
                //    do bioma virava uma "linha de plantio" de árvores perfeitas.
                BiomeWeights(wx, wz, out float bPl, out float bFo, out float bMo, out float bCo, out float bDe);
                float w = PickWeight(layer.biome, bPl, bFo, bMo, bCo, bDe);
                float th = layer.minBiomeWeight *
                           (0.88f + 0.24f * Mathf.PerlinNoise(wx * 0.021f + oxD + 71.7f,
                                                              wz * 0.021f + ozD + 33.1f));
                if (w < th) continue;
                // veto do bioma rival (árvore verde × areia etc.)
                if (layer.avoidBiomeMax < 1f &&
                    PickWeight(layer.avoidBiome, bPl, bFo, bMo, bCo, bDe) > layer.avoidBiomeMax)
                    continue;
                float p = layer.density *
                          Mathf.Sqrt(Mathf.InverseLerp(layer.minBiomeWeight, 1f, w));

                // 2) agrupamento: manchas de noise → aglomerados naturais + áreas abertas.
                //    coverage move o limiar (fração da área nas manchas), edgeSoftness
                //    abre a largura da transição e irregularity soma uma 2ª octava que
                //    quebra o contorno redondo do Perlin puro.
                //    Defaults (0.5/0.5/0) reproduzem o limiar antigo InverseLerp(0.45, 0.72).
                if (layer.clusterStrength > 0f)
                    p *= Mathf.Lerp(1f, ClusterMask(layer, cOff, wx, wz), layer.clusterStrength);
                if (roll > p) continue;

                // 2.5) riachos: fora do vale para as camadas normais; DENTRO
                //      dele para as inStreamBed (seixos forrando o leito)
                bool inValley = StreamExcluded(wx, wz, bPl, bFo, bMo, bCo, bDe);
                if (layer.inStreamBed ? !inValley : inValley) continue;

                // 3) relevo — do heightmap JÁ CALCULADO (rechamar HeightAt/SlopeAt
                //    aqui era o custo dominante do scatter: ~60 Perlin por tentativa)
                float slope = SlopeFromGrid(heights, heightmapRes, nx, nz);
                if (slope < layer.minSlope || slope > layer.maxSlope) continue;
                float h = SampleHeight01(heights, heightmapRes, nx, nz) * maxHeight;
                if (h < layer.heightRange.x || h > layer.heightRange.y) continue;

                // 3.5) placa de gelo: a camada onFrozenIce faz o CONTRÁRIO das
                //      outras — ela EXIGE bacia de lago e frio, e depois é
                //      reassentada na superfície da placa (passo 5).
                float iceY = 0f;
                if (layer.onFrozenIce)
                {
                    if (!enableLakes || !freezeTundraWater) continue;
                    if (h > waterLevel - 0.25f) continue;        // fora da bacia
                    // frio COM folga além do fade: na borda da placa o mesh
                    // mergulha sob a lâmina, e a pedra ficaria boiando no ar
                    if (bCo < frozenColdThreshold + iceEdgeFade) continue;
                    iceY = waterLevel + iceSurfaceOffset + IceRelief(wx, wz);
                }
                else if ((enableLakes || StreamsActive) && !layer.inStreamBed &&
                         h < waterLevel + 0.12f)                 // seixos PODEM ficar submersos
                    continue;   // nada dentro d'água (0.12: a grama chega até a
                                // beira — 0.35 abria um anel de terra nua nos lagos)

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

                // espécie: roll aleatório puxado p/ um noise regional — regiões com UMA
                // dominante (bosques de maple, mancha de um só arbusto), sem draw extra do rng
                float pickRoll = (float)rng.NextDouble();
                if (layer.speciesClumping > 0f && layer.protoCount > 1)
                {
                    float sn = Mathf.PerlinNoise(wx / layer.speciesPatchSize + cOff.x + 137.9f,
                                                 wz / layer.speciesPatchSize + cOff.y + 71.3f);
                    pickRoll = Mathf.Lerp(pickRoll, sn, layer.speciesClumping);
                }
                // a instância de árvore não é grudada no heightmap pela Unity — o
                // Y sai daqui. Normalmente é o chão; na placa é a cota do gelo,
                // e a pedra ainda AFUNDA um pouco (raio × escala) para não ficar
                // apoiada numa lâmina como um adesivo.
                float y = layer.onFrozenIce
                    ? iceY - Mathf.Lerp(0.15f, 0.55f, Mathf.InverseLerp(0.3f, 1.6f, scale))
                    : td.GetInterpolatedHeight(nx, nz);

                instances.Add(new TreeInstance
                {
                    position = new Vector3(nx, y / maxHeight, nz),
                    prototypeIndex = layer.protoBase + layer.PickPrototype(pickRoll),
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

        // riachos de montanha: confinados aos VALES em U — o mesmo campo de
        // cristas do hMount decide (t = ridge² baixo = fundo de vale). Sem isto,
        // a curva de nível atravessaria picos escavando gargantas de 100 m.
        if (s.valleyOnly)
        {
            float r = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx * 0.0035f + oxH,
                                                            wz * 0.0035f + ozH) - 1f);
            fade *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.28f, r * r));
            if (fade <= 0.002f) return 0f;
        }

        // largura respira ao longo do curso e AFINA junto com o fade do bioma —
        // na borda da floresta o riacho estreita antes de secar (nunca corte seco)
        float wn = Mathf.PerlinNoise(wx / (s.courseSize * 0.31f) + s.offWidth.x,
                                     wz / (s.courseSize * 0.31f) + s.offWidth.y);
        float halfW = s.channelWidth * Mathf.Lerp(0.55f, 1.45f, wn) * Mathf.Lerp(0.3f, 1f, fade);

        // (Mathf.SmoothStep é interpolador (from,to,t) — o limiar estilo shader
        //  é sempre SmoothStep(0,1, InverseLerp(a,b,x)), como no resto do arquivo.)
        valley = (1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0.25f, 1f, d / (halfW * s.valleyWidthMul)))) * fade;
        return d < halfW
            ? (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, d / halfW))) * fade
            : 0f;
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

        // Tundra: planície ondulada + DRIFTS de neve — cristas ridged suaves e
        // ALONGADAS (frequência anisotrópica = neve soprada numa direção), sutis
        // (≤ ~2.5 m) para dar vida ao manto branco sem virar campo de dunas.
        float drift = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx * 0.013f + oxT + 57.1f,
                                                            wz * 0.009f + ozT + 88.7f) - 1f);
        // montículos finos (~18 m): microrrelevo que o manto liso não tinha —
        // "a montanha parece um cone suavizado" era a crítica
        float micro = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx * 0.055f + oxT + 13.9f,
                                                            wz * 0.055f + ozT + 41.2f) - 1f);
        float hTundra = 5f + FBM(wx, wz, 0.007f, 2) * 6f + drift * drift * 2.5f
                      + micro * micro * 1.1f;

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

        // Montanha: cristas ridged com VALES EM U (fundo ~14–25 m, onde mata,
        // torrentes e musgo vivem), PLATÔS a meia altura (~52 m — patamares de
        // pouso/ninho; o t entre 0.33 e 0.45 não sobe) e picos piramidais.
        float ridge = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(wx * 0.0035f + oxH, wz * 0.0035f + ozH) - 1f);
        float tR = ridge * ridge;
        float tLow = Mathf.Clamp01(tR / 0.33f);
        float tHigh = Mathf.Clamp01((tR - 0.45f) / 0.55f);
        float hMount = 14f + tLow * 38f + tHigh * tHigh * 62f + FBM(wx, wz, 0.02f, 2) * 10f;

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

                // Leito ABSOLUTO: no miolo do canal o alvo é exatamente
                // waterLevel−depth (patamar plano, água garantida), subindo até o
                // fundo do vale nas beiradas. O vale adota o alvo por inteiro já a
                // meia encosta (blend satura) — um Lerp parcial a partir do terreno
                // alto da floresta deixava o canal acima da lâmina = riacho seco.
                float bedT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.75f, channel));
                float target = Mathf.Lerp(waterLevel + s.bankHeight,
                                          waterLevel - s.depth, bedT);
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.55f, valley));
                h = Mathf.Min(h, Mathf.Lerp(h, target, blend));
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

        // MODO TUNDRA-FUNDO: só Tundra (e opcionalmente Montanha) marcada — o frio
        // preenche tudo que não é montanha, como nos modos -fundo da floresta/deserto.
        // Bom p/ testar o bioma de inverno sem procurar as manchas frias.
        if (enableTundra && !enablePlains && !enableForest && !enableDesert)
            cold = 1f - mount;

        float free = (1f - mount) * (1f - cold);   // terreno plano restante (nem montanha nem tundra)

        // Deserto = quente + seco. Fica fora de montanha/tundra por causa do 'free'.
        // Limiar de temperatura em 0.58 (era 0.52): garante ≥0.2 de faixa TEMPERADA
        // entre o deserto e o frio (que começa em temp < 0.38) — sem isso, areia
        // encostava na neve em ~100 m e o mundo lia como colagem de biomas.
        float desertNiche = enableDesert
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.74f, temp)) *
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

    /// <summary>Amostra bilinear do heightmap normalizado do tile (nx/nz em [0..1]).</summary>
    static float SampleHeight01(float[,] h, int res, float nx, float nz)
    {
        float gx = Mathf.Clamp01(nx) * (res - 1), gz = Mathf.Clamp01(nz) * (res - 1);
        int x0 = Mathf.Min((int)gx, res - 2), z0 = Mathf.Min((int)gz, res - 2);
        float tx = gx - x0, tz = gz - z0;
        float a = Mathf.Lerp(h[z0, x0], h[z0, x0 + 1], tx);
        float b = Mathf.Lerp(h[z0 + 1, x0], h[z0 + 1, x0 + 1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    /// <summary>
    /// Inclinação por diferenças centrais INTERPOLADAS no heightmap já calculado
    /// — mesma razão m/m do antigo SlopeAt, zero chamadas de noise. As amostras
    /// são bilineares (não o ponto de grade mais próximo): a versão "snapped"
    /// serrilhava a pintura de rocha na resolução da grade — regressão visível
    /// nas bordas das bacias de lago.
    /// </summary>
    float SlopeFromGrid(float[,] h, int res, float nx, float nz)
    {
        float e = 1f / (res - 1);                 // 1 célula (~1.95 m)
        float span = 2f * tileSize * e;
        float dx = (SampleHeight01(h, res, nx + e, nz) - SampleHeight01(h, res, nx - e, nz)) * maxHeight / span;
        float dz = (SampleHeight01(h, res, nx, nz + e) - SampleHeight01(h, res, nx, nz - e)) * maxHeight / span;
        return Mathf.Sqrt(dx * dx + dz * dz);
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
    static bool msLayerWarned;   // aviso de MicroSplat desatualizado só 1x por sessão

    // Auto-preenche assets dos pacotes (ALP + RockyDesert + Winter) — sem rodar menus.
    void OnValidate()
    {
        if (Application.isPlaying) return;

        // ---- ALP (floresta/campos)
        const string G = "Assets/ALP_Assets/Nature Package - Forest Environment_/GroundTextures/";
        const string V = "Assets/ALP_Assets/Nature Package - Forest Environment_/_Vegetation/HDRP/HDRP_Prefabs/";
        // paint tree do pack ALP com peso (os *Group de árvore ficam de fora:
        // um cluster inteiro numa instância flutua nas encostas)
        PaintTree N(string name, float w = 1f) => new(P(V + name + "_HDRP.prefab"), w);

        bool dirty = false;

        // ---- migração one-shot: valores ANTIGOS default → novos defaults.
        //      (se você já ajustou p/ outro valor no Inspector, nada muda aqui)
        if (alphamapRes == 64) { alphamapRes = 128; dirty = true; }
        if (Mathf.Approximately(forestGrassShare, 0.8f)) { forestGrassShare = 0.85f; dirty = true; }
        if (Mathf.Approximately(forestDirtThreshold, 0.62f)) { forestDirtThreshold = 0.68f; dirty = true; }

        if (grassDiffuse == null)
        {
            grassDiffuse = T(G + "Grass_001.tif"); grassNormal = T(G + "Grass_001_N.png"); grassMask = T(G + "Grass_001_mask.png");
            forestDiffuse = T(G + "Ground01.tif"); forestNormal = T(G + "Ground01_N.png"); forestMask = T(G + "Ground01_mask.png");
            rockDiffuse = T(G + "Ground02.tif"); rockNormal = T(G + "Ground02_N.png"); rockMask = T(G + "Ground02_mask.png");
            dirty = true;
        }
        if (Empty(treePrefabs))
        {
            // ForestTree = espinha dorsal; MapleSpring entra como variação (peso menor
            // p/ as 9 variantes não dominarem a mata)
            treePrefabs = new[]
            {
                N("ForestTree01"), N("ForestTree02"), N("ForestTree03"),
                N("ForestTree04"), N("ForestTree05"),
                N("MapleSpringTree01", 0.25f), N("MapleSpringTree02", 0.25f),
                N("MapleSpringTree03", 0.25f), N("MapleSpringTree03_1", 0.25f),
                N("MapleSpringTree04", 0.25f), N("MapleSpringTree05", 0.25f),
                N("MapleSpringTree06", 0.25f), N("MapleSpringTree06_1", 0.25f),
                N("MapleSpringTree06_2", 0.25f),
            };
            dirty = true;
        }
        if (Empty(bushPrefabs))
        {
            bushPrefabs = new[]
            {
                N("ForestBush01"), N("ForestBush02"), N("ForestBush03"), N("ForestBush04"),
                N("ForestBush05"), N("ForestBush06"), N("ForestBush07"), N("ForestBush08"),
                N("ForestBush09"), N("ForestBush10"), N("ForestBush11"),
                N("MapleSpringBushes01", 0.5f), N("MapleSpringBushes02", 0.5f),
                N("MapleSpringBushes03", 0.5f),
            };
            dirty = true;
        }
        if (Empty(grassPrefabs))
        {
            grassPrefabs = new[]
            {
                N("FlowerGrass01"), N("FlowerGrass02"), N("FlowerGrass03"), N("FlowerGrass04"),
                N("GrassPlant02"), N("GrassPlant03"), N("GrassPlant04"), N("GrassPlant05"),
            };
            dirty = true;
        }
        if (Empty(forestGroundcoverPrefabs))
        {
            // só os GrassPlant (sem flores): o tapete é volume verde; as flores
            // continuam como acentos na camada de graminhas.
            forestGroundcoverPrefabs = new[]
            {
                N("GrassPlant02"), N("GrassPlant03"), N("GrassPlant04"), N("GrassPlant05"),
            };
            dirty = true;
        }
        if (Empty(forestThicketPrefabs))
        {
            forestThicketPrefabs = new[] { N("Thicket01"), N("Thicket02") };
            dirty = true;
        }
        if (Empty(forestLitterPrefabs))
        {
            forestLitterPrefabs = new[]
            {
                N("ForestStick01"), N("ForestStick02"), N("ForestStick03"),
                N("ForestStickSmall01"), N("ForestStickSmall02"), N("ForestStickSmall03"),
                N("ForestStick01Group", 0.5f), N("ForestStickGroup01", 0.5f),
                N("LeafDry01"), N("LeafDry01Group", 0.5f),
            };
            dirty = true;
        }

        // ---- RockyDesert (deserto). Prefere os prefabs CONVERTIDOS para HDRP
        //      (Tools > Everwyrm > Deserto — os originais são Built-in e ficam magenta).
        const string Conv = "Assets/Everwyrm/Desert/";
        const string Orig = "Assets/RockyDesert/Prefabs/";
        string D(string name) => System.IO.File.Exists(Conv + name + ".prefab")
            ? Conv + name + ".prefab" : Orig + name + ".prefab";
        PaintTree DP(string name, float w = 1f) => new(P(D(name)), w);

        if (Empty(desertFormationPrefabs))
        {
            desertFormationPrefabs = new[] { DP("SM_RockSide_A1"), DP("SM_Rock_Side-A2") };
            dirty = true;
        }
        if (Empty(desertRockPrefabs))
        {
            desertRockPrefabs = new[] { DP("SM_Small_Rock_2"), DP("SM_Small_Rock_3"),
                                        DP("SM_Small_Rock_4") };
            dirty = true;
        }
        if (Empty(desertPebblePrefabs))
        {
            desertPebblePrefabs = new[] { DP("SM_Small_Rock_1"), DP("SM_Small_Rock_B1"),
                                          DP("SM_Small_Rock_B2") };
            dirty = true;
        }
        if (Empty(desertTreePrefabs))
        {
            desertTreePrefabs = new[] { DP("SM_Tree_1"), DP("SM_Tree_A"), DP("SM_Tree_B") };
            dirty = true;
        }
        if (Empty(desertGrassPrefabs))
        {
            desertGrassPrefabs = new[] { DP("SM_DeadGrass_A"), DP("SM_DeadGrass_A1"),
                                         DP("SM_DeadGrass_A2") };
            dirty = true;
        }

        // ---- Winter (tundra/montanha): reencontra os prefabs limpos da conversão
        //      (Tools > Everwyrm > Winter) se os arrays estiverem vazios.
        if ((Empty(winterFirPrefabs) || Empty(winterPinePrefabs) || Empty(winterBirchPrefabs) ||
             Empty(winterBushPrefabs) || Empty(winterGrassPrefabs) || Empty(winterRockPrefabs) ||
             Empty(winterOutcropPrefabs) || Empty(winterDeadwoodPrefabs) || Empty(winterDebrisPrefabs)) &&
            UnityEditor.AssetDatabase.IsValidFolder("Assets/Everwyrm/Winter"))
        {
            // mesmas categorias do WinterSetup.Categories (primeira que bater vence;
            // rock_group ANTES de rock_)
            var cats = new (List<GameObject> list, string[] keys)[]
            {
                (new List<GameObject>(), new[] { "tree_a" }),                   // abetos
                (new List<GameObject>(), new[] { "tree_b" }),                   // pinheiros
                (new List<GameObject>(), new[] { "tree_c" }),                   // bétulas
                (new List<GameObject>(), new[] { "bush" }),
                (new List<GameObject>(), new[] { "grass_" }),
                (new List<GameObject>(), new[] { "rock_group" }),               // afloramentos
                (new List<GameObject>(), new[] { "rock_" }),
                (new List<GameObject>(), new[] { "log_", "stump_", "root_" }),  // madeira caída
                (new List<GameObject>(), new[] { "debris_" }),
            };
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets(
                         "t:Prefab", new[] { "Assets/Everwyrm/Winter" }))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (var (list, keys) in cats)
                {
                    bool hit = false;
                    foreach (var k in keys) if (n.Contains(k)) { hit = true; break; }
                    if (hit) { list.Add(go); break; }
                }
            }
            void Fill(ref PaintTree[] arr, List<GameObject> list)
            {
                if (Empty(arr) && list.Count > 0) { arr = PaintTree.From(list); dirty = true; }
            }
            Fill(ref winterFirPrefabs, cats[0].list);
            Fill(ref winterPinePrefabs, cats[1].list);
            Fill(ref winterBirchPrefabs, cats[2].list);
            Fill(ref winterBushPrefabs, cats[3].list);
            Fill(ref winterGrassPrefabs, cats[4].list);
            Fill(ref winterOutcropPrefabs, cats[5].list);
            Fill(ref winterRockPrefabs, cats[6].list);
            Fill(ref winterDeadwoodPrefabs, cats[7].list);
            Fill(ref winterDebrisPrefabs, cats[8].list);
        }

        // ---- chão/neve ambiente do Winter pack
        const string WPack = "Assets/ANGRY MESH/Nature Pack - Winter Environment";
        if (snowLayer == null)
        {
            snowLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                WPack + "/Sources/Terrain Layers/Snow_01_Layer.terrainlayer");
            if (snowLayer != null) dirty = true;
        }
        if (winterGroundLayer == null)
        {
            winterGroundLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                WPack + "/Sources/Terrain Layers/Leaves_01_Layer.terrainlayer");
            if (winterGroundLayer != null) dirty = true;
        }
        if (winterSnowfallPrefab == null)
        {
            winterSnowfallPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                WPack + "/Prefabs/Particles/Snow_01.prefab");
            if (winterSnowfallPrefab != null) dirty = true;
        }

        // ---- Mountain Environment (NM): reencontra os prefabs limpos da conversão
        //      (Tools > Everwyrm > Mountain) — mesmas categorias do MountainSetup.
        if ((Empty(mountainPinePrefabs) || Empty(mountainSaplingPrefabs) ||
             Empty(mountainBushPrefabs) || Empty(mountainGrassPrefabs) ||
             Empty(mountainRockPrefabs) || Empty(mountainBoulderPrefabs) ||
             Empty(mountainRiverStonePrefabs) || Empty(mountainDeadwoodPrefabs) ||
             Empty(mountainMushroomPrefabs) || Empty(mountainDetailPrefabs) ||
             Empty(mountainDecalPrefabs) || Empty(mountainStatuePrefabs)) &&
            UnityEditor.AssetDatabase.IsValidFolder("Assets/Everwyrm/Mountain"))
        {
            var mcats = new (List<GameObject> list, string[] keys)[]
            {
                (new List<GameObject>(), new[] { "sviatovid" }),
                (new List<GameObject>(), new[] { "river_stone" }),
                (new List<GameObject>(), new[] { "dwarf_pine", "hazel" }),
                (new List<GameObject>(), new[] { "pine_plant", "pine_tree_00" }),
                (new List<GameObject>(), new[] { "forest_pine" }),
                (new List<GameObject>(), new[] { "_log", "log_pile", "stump", "roots", "root_" }),
                (new List<GameObject>(), new[] { "big_rock", "rock_wall", "mountain_rock_big" }),
                (new List<GameObject>(), new[] { "rock", "stone" }),
                (new List<GameObject>(), new[] { "mushroom" }),
                (new List<GameObject>(), new[] { "grass_", "heath", "billberry", "lingonberry", "rhododendron" }),
                (new List<GameObject>(), new[] { "branch", "cone", "anthill" }),
                (new List<GameObject>(), new[] { "decal" }),
            };
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets(
                         "t:Prefab", new[] { "Assets/Everwyrm/Mountain" }))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                string mn = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (var (list, keys) in mcats)
                {
                    bool hit = false;
                    foreach (var k in keys) if (mn.Contains(k)) { hit = true; break; }
                    if (hit) { list.Add(go); break; }
                }
            }
            void MFill(ref PaintTree[] arr, List<GameObject> list)
            {
                if (Empty(arr) && list.Count > 0) { arr = PaintTree.From(list); dirty = true; }
            }
            MFill(ref mountainStatuePrefabs, mcats[0].list);
            MFill(ref mountainRiverStonePrefabs, mcats[1].list);
            MFill(ref mountainBushPrefabs, mcats[2].list);
            MFill(ref mountainSaplingPrefabs, mcats[3].list);
            MFill(ref mountainPinePrefabs, mcats[4].list);
            MFill(ref mountainDeadwoodPrefabs, mcats[5].list);
            MFill(ref mountainBoulderPrefabs, mcats[6].list);
            MFill(ref mountainRockPrefabs, mcats[7].list);
            MFill(ref mountainMushroomPrefabs, mcats[8].list);
            MFill(ref mountainGrassPrefabs, mcats[9].list);
            MFill(ref mountainDetailPrefabs, mcats[10].list);
            MFill(ref mountainDecalPrefabs, mcats[11].list);
        }
        const string MPack = "Assets/NatureManufacture Assets/Mountain Environment";
        if (mountainMossLayer == null)
        {
            mountainMossLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                MPack + "/Terrain/Terrain Layer_Moss.terrainlayer");
            if (mountainMossLayer != null) dirty = true;
        }
        if (mountainNeedleLayer == null)
        {
            mountainNeedleLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                MPack + "/Terrain/Terrain Layer_needless_01.terrainlayer");
            if (mountainNeedleLayer != null) dirty = true;
        }
        if (mountainSoilLayer == null)
        {
            mountainSoilLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                MPack + "/Terrain/Terrain Layer_soil_01.terrainlayer");
            if (mountainSoilLayer != null) dirty = true;
        }
        if (mountainRockTerrainLayer == null)
        {
            mountainRockTerrainLayer = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                MPack + "/Terrain/Terrain Layer_rocks.terrainlayer");
            if (mountainRockTerrainLayer != null) dirty = true;
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
        // ---- MicroSplat: reencontra os assets da conversão (Tools > Everwyrm > MicroSplat)
        if (microSplatMaterial == null &&
            UnityEditor.AssetDatabase.IsValidFolder("Assets/Everwyrm/MicroSplat"))
        {
            microSplatMaterial = MS<Material>("t:Material");
            if (microSplatMaterial != null)
            {
                microSplatPropData = MS<JBooth.MicroSplat.MicroSplatPropData>("t:MicroSplatPropData");
                microSplatKeywords = MS<JBooth.MicroSplat.MicroSplatKeywords>("t:MicroSplatKeywords");
                microSplatBaseShader = MS<Shader>("t:Shader _Base");
                dirty = true;
            }
        }

        // MicroSplat gerado com menos camadas do que o chão usa agora? Avisar 1x —
        // sem reconversão, a Tundra fica sem a neve/folhas novas no shader.
        if (!msLayerWarned && microSplatMaterial != null)
        {
            int want = (winterGroundLayer != null || mountainMossLayer != null ||
                        mountainNeedleLayer != null || mountainSoilLayer != null) ? 10 : 6;
            if (microSplatMaterial.GetTexture("_Diffuse") is Texture2DArray arr && arr.depth < want)
            {
                msLayerWarned = true;
                Debug.LogWarning($"[InfiniteTerrain] MicroSplat foi gerado com {arr.depth} camadas, " +
                    $"mas o chão agora tem {want} (neve funda + folhas congeladas do Winter pack). " +
                    "Reconverta: apague Assets/Everwyrm/MicroSplat, limpe o campo Micro Splat Material " +
                    "e rode Tools > Everwyrm > MicroSplat — Converter Terreno.");
            }
        }
        if (dirty) UnityEditor.EditorUtility.SetDirty(this);

        static Texture2D T(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        static GameObject P(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
        static bool Empty(PaintTree[] a) =>
            a == null || a.Length == 0 || a[0] == null || a[0].prefab == null;
        static U MS<U>(string filter) where U : UnityEngine.Object
        {
            var g = UnityEditor.AssetDatabase.FindAssets(filter, new[] { "Assets/Everwyrm/MicroSplat" });
            return g.Length > 0 ? UnityEditor.AssetDatabase.LoadAssetAtPath<U>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(g[0])) : null;
        }
    }
#endif
}
