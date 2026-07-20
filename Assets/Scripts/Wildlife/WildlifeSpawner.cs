using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Povoador do ecossistema — mantém a fauna viva num anel ao redor do jogador,
/// escolhendo espécies pelo PESO CONTÍNUO dos biomas do InfiniteTerrain
/// (mesma filosofia do ScatterLayer da vegetação, mas com GameObjects vivos).
///
///  - Carrega TODAS as AnimalDefinition de Resources/Wildlife (data-driven:
///    criar um asset novo = espécie nova no mundo, sem tocar em código).
///  - Spawna GRUPOS coerentes (líder/membros/filhotes com mãe), nunca
///    indivíduos soltos que "por acaso" nasceram perto.
///  - Despawna grupos que ficaram para trás; o mundo continua vivo à frente.
///  - Auto-bootstrap: cria a si próprio na cena ao dar Play (sem setup manual).
/// </summary>
[DefaultExecutionOrder(-10)]
public class WildlifeSpawner : MonoBehaviour
{
    public static WildlifeSpawner Instance { get; private set; }

    [Header("Anel de População")]
    [SerializeField] float spawnRadiusMin = 130f;
    [SerializeField] float spawnRadiusMax = 380f;
    [SerializeField] float despawnRadius = 470f;
    [SerializeField] float minPlayerDistance = 90f;   // nada de pop na cara do jogador

    [Header("Orçamento")]
    [SerializeField] int maxAnimals = 190;
    [SerializeField] float populateInterval = 2.5f;
    [SerializeField] int attemptsPerCycle = 7;

    AnimalDefinition[] defs;
    readonly List<AnimalGroup> groups = new();

    Transform player;
    DragonController dragonCtrl;
    DragonGrowth dragonGrowth;
    DragonVitals dragonVitals;
    bool dragonWasFlying;
    float nextPopulate;

    // ------------------------------------------------------- INFO DO DRAGÃO
    /// <summary>O dragão como "ameaça ambulante" para a fauna (null = sem jogador).</summary>
    public static Transform Dragon =>
        Instance != null ? Instance.player : null;

    /// <summary>Tamanho do dragão 0..1 — alces ignoram filhotes; tudo teme um Colossal.</summary>
    public static float DragonGrowth01 =>
        Instance != null && Instance.dragonGrowth != null
            ? Instance.dragonGrowth.Growth01 : 0.5f;

    public static bool DragonFlying =>
        Instance != null && Instance.dragonCtrl != null && Instance.dragonCtrl.IsFlying;

    /// <summary>Contra-ataque da fauna (alce/urso/javali defendendo-se).</summary>
    public static void DamageDragon(float damage)
    {
        if (Instance == null || Instance.dragonVitals == null) return;
        Instance.dragonVitals.Damage(damage);
        if (Instance.dragonCtrl != null) Instance.dragonCtrl.OnDamaged(damage);
    }

    // ------------------------------------------------------------ BOOTSTRAP
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<WildlifeSpawner>() != null) return;
        if (GameObject.FindGameObjectWithTag("Player") == null) return;
        if (Resources.LoadAll<AnimalDefinition>("Wildlife").Length == 0) return;
        new GameObject("Wildlife (Ecossistema)").AddComponent<WildlifeSpawner>();
    }

    void Awake()
    {
        Instance = this;
        defs = Resources.LoadAll<AnimalDefinition>("Wildlife");

        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            dragonCtrl = p.GetComponent<DragonController>();
            dragonGrowth = p.GetComponent<DragonGrowth>();
            dragonVitals = p.GetComponent<DragonVitals>();
        }
    }

    void Start()
    {
        // leva inicial: o mundo já nasce habitado (anel mais próximo)
        for (int i = 0; i < 30; i++)
            TrySpawnGroup(110f, spawnRadiusMax);
    }

    void Update()
    {
        if (player == null || defs == null || defs.Length == 0) return;
        float dt = Time.deltaTime;
        float now = Time.time;

        // ---- grupos pensam (migração, caça, uivos, alarme)
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var g = groups[i];
            g.Tick(dt);
            if (g.IsEmpty) { groups.RemoveAt(i); continue; }

            // grupo ficou para trás → recicla (cadáveres com carcaça cheia ficam)
            Vector3 c = g.Centroid();
            if (Horizontal(c, player.position) > despawnRadius)
            {
                for (int m = g.Members.Count - 1; m >= 0; m--)
                    g.Members[m].Despawn();
                groups.RemoveAt(i);
            }
        }

        // ---- barulho de pouso: o dragão aterrissar perto acorda a vizinhança
        bool flying = dragonCtrl != null && dragonCtrl.IsFlying;
        if (dragonWasFlying && !flying)
            foreach (var a in AnimalAgent.All)
                if (Horizontal(a.transform.position, player.position) < 45f)
                    a.Startle(player.position);
        dragonWasFlying = flying;

        // ---- reposição contínua
        if (now >= nextPopulate)
        {
            nextPopulate = now + populateInterval;
            int alive = AnimalAgent.All.Count;
            if (alive < maxAnimals)
                for (int i = 0; i < attemptsPerCycle; i++)
                    TrySpawnGroup(spawnRadiusMin, spawnRadiusMax);
        }
    }

    static float Horizontal(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ================================================================ SPAWN
    void TrySpawnGroup(float radiusMin, float radiusMax)
    {
        if (player == null) return;

        // ponto candidato no anel
        Vector2 dir = Random.insideUnitCircle.normalized;
        float dist = Random.Range(Mathf.Max(radiusMin, minPlayerDistance), radiusMax);
        Vector3 origin = player.position + new Vector3(dir.x, 0f, dir.y) * dist;

        var world = InfiniteTerrain.Instance;
        float waterFrac = 0f;
        if (world != null)
        {
            origin.y = world.HeightAt(origin.x, origin.z);
            // caiu na água? vira spawn NA MARGEM — rios/lagos largos "capturam"
            // tentativas proporcionalmente ao seu tamanho, povoando as beiras
            if (world.HasLakes && origin.y < world.WaterLevel + 0.6f &&
                !FindShore(world, ref origin))
                return;
            if (SlopeAt(world, origin) > 0.75f) return;                          // paredão
            waterFrac = WaterFractionNear(world, origin);
        }

        // ---- roleta de espécies ponderada por bioma + raridade + período + água
        AnimalDefinition chosen = null;
        float total = 0f;
        var period = WildlifeClock.Current;
        foreach (var d in defs)
        {
            if (d.roles == null || d.roles.Length == 0) continue;
            if (CountGroupsOf(d) >= d.maxActiveGroups) continue;

            float w = d.spawnWeight * d.BiomeAffinityAt(origin.x, origin.z);
            if ((d.activity & period) == 0) w *= 0.3f;   // fora do horário: raro, não impossível
            // beira de água grande: todo mundo aparece mais; quem AMA água
            // (alce bebendo no rio) aparece MUITO mais
            if (waterFrac > 0f)
                w *= 1f + waterFrac * (1.5f + d.waterAffinity * 4f);
            if (w <= 0f) continue;

            total += w;
            if (Random.value < w / total) chosen = d;    // reservoir sampling
        }
        if (chosen == null) return;

        SpawnGroup(chosen, origin);
    }

    /// <summary>Ponto caiu na água: marcha em direções opostas até achar terra firme na margem.</summary>
    bool FindShore(InfiniteTerrain world, ref Vector3 origin)
    {
        Vector2 dir2 = Random.insideUnitCircle.normalized;
        Vector3 dir = new(dir2.x, 0f, dir2.y);
        for (int side = 0; side < 2; side++, dir = -dir)
            for (float r = 10f; r <= 90f; r += 10f)
            {
                Vector3 p = origin + dir * r;
                float h = world.HeightAt(p.x, p.z);
                if (h <= world.WaterLevel + 0.8f) continue;   // ainda na água/beirada rasa
                p.y = h;
                if (SlopeAt(world, p) > 0.75f) continue;      // margem de penhasco
                origin = p;
                return true;
            }
        return false;
    }

    /// <summary>Fração de água num raio de ~55 m (0 = seco · alto = beira de rio/lago largo).</summary>
    float WaterFractionNear(InfiniteTerrain world, Vector3 p)
    {
        if (!world.HasLakes) return 0f;
        int wet = 0, totalSamples = 0;
        for (int a = 0; a < 8; a++)
        {
            Vector3 dir = Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward;
            for (int ring = 0; ring < 2; ring++)
            {
                float r = ring == 0 ? 25f : 55f;
                Vector3 s = p + dir * r;
                totalSamples++;
                if (world.HeightAt(s.x, s.z) < world.WaterLevel - 0.2f) wet++;
            }
        }
        return (float)wet / totalSamples;
    }

    float SlopeAt(InfiniteTerrain world, Vector3 p)
    {
        const float e = 2f;
        float hx = world.HeightAt(p.x + e, p.z) - world.HeightAt(p.x - e, p.z);
        float hz = world.HeightAt(p.x, p.z + e) - world.HeightAt(p.x, p.z - e);
        return Mathf.Sqrt(hx * hx + hz * hz) / (2f * e);
    }

    int CountGroupsOf(AnimalDefinition d)
    {
        int n = 0;
        foreach (var g in groups) if (g.Def == d) n++;
        return n;
    }

    /// <summary>
    /// Monta o grupo respeitando papéis: líder (chance), membros, filhotes —
    /// e amarra cada filhote a uma "mãe" adulta. Híbridos podem vir sozinhos.
    /// </summary>
    void SpawnGroup(AnimalDefinition def, Vector3 origin)
    {
        var group = new AnimalGroup(def, origin);
        bool solo = def.social == AnimalDefinition.SocialModel.Solitario ||
                    (def.social == AnimalDefinition.SocialModel.Hibrido &&
                     Random.value < def.soloChance);

        int budget = solo ? 1 : Random.Range(def.groupSize.x, def.groupSize.y + 1);
        int spawned = 0, slot = 0;
        var adults = new List<AnimalAgent>();
        var youngRoles = new List<AnimalDefinition.AnimalRole>();

        foreach (var role in def.roles)
        {
            if (role == null) continue;
            if (spawned >= budget && role.kind != AnimalDefinition.RoleKind.Lider) continue;
            if (Random.value > role.chance) continue;

            if (role.kind == AnimalDefinition.RoleKind.Filhote)
            {
                // filhotes só existem se houver adultos — guardamos para o fim
                youngRoles.Add(role);
                continue;
            }

            int count = solo ? (spawned == 0 ? 1 : 0)
                             : Random.Range(role.count.x, role.count.y + 1);
            for (int i = 0; i < count && (spawned < budget || role.kind == AnimalDefinition.RoleKind.Lider); i++)
            {
                var a = SpawnOne(def, role, group, origin, slot++);
                if (a != null) { adults.Add(a); spawned++; }
            }
        }

        // nenhum adulto conseguiu nascer? aborta
        if (adults.Count == 0) return;

        // ---- filhotes: sempre grudados numa mãe (nunca sozinhos no mundo)
        if (!solo)
            foreach (var role in youngRoles)
            {
                int count = Random.Range(role.count.x, role.count.y + 1);
                for (int i = 0; i < count; i++)
                {
                    var mother = adults[Random.Range(0, adults.Count)];
                    var baby = SpawnOne(def, role, group, mother.transform.position, slot++);
                    if (baby != null) baby.Mother = mother;
                }
            }

        groups.Add(group);
    }

    AnimalAgent SpawnOne(AnimalDefinition def, AnimalDefinition.AnimalRole role,
                         AnimalGroup group, Vector3 origin, int slot)
    {
        if (role.prefabs == null || role.prefabs.Length == 0 || role.controller == null)
            return null;

        // posição no "slot" orgânico do grupo + jitter
        Vector3 p = origin + group.SlotFor(slot);
        Vector2 j = Random.insideUnitCircle * def.memberSpacing * 0.4f;
        p.x += j.x; p.z += j.y;

        var world = InfiniteTerrain.Instance;
        if (world != null)
        {
            p.y = world.HeightAt(p.x, p.z);
            if (world.HasLakes && p.y < world.WaterLevel + 0.4f) return null;
        }

        // variação visual: prefab de cor sorteado (c1..cN) + escala própria
        var prefab = role.prefabs[Random.Range(0, role.prefabs.Length)];
        var go = Instantiate(prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        go.name = $"{def.speciesName} ({role.name})";
        go.transform.SetParent(transform, true);

        var agent = go.GetComponent<AnimalAgent>();
        if (agent == null) agent = go.AddComponent<AnimalAgent>();
        agent.Init(def, role, group, slot, Random.Range(role.scaleRange.x, role.scaleRange.y));
        return agent;
    }
}
