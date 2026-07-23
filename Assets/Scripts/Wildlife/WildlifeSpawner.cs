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
    [SerializeField] float spawnRadiusMin = 70f;
    [SerializeField] float spawnRadiusMax = 320f;
    [SerializeField] float despawnRadius = 400f;
    [SerializeField] float minPlayerDistance = 50f;    // nada de pop na cara do jogador
    [SerializeField] float noPopInFrontDistance = 140f; // dentro do cone da câmera, só além disto

    [Header("Orçamento")]
    [SerializeField] int maxAnimals = 240;
    [SerializeField] float populateInterval = 1.5f;
    [SerializeField] int attemptsPerCycle = 10;

    [Header("Voo & Encontros")]
    [SerializeField] float flightBiasSpeed = 8f;        // acima disto (m/s), spawna à FRENTE
    [SerializeField, Range(0f, 1f)] float flightBiasFraction = 0.65f;
    [SerializeField] float flightConeHalfAngle = 55f;   // meio-ângulo do cone frontal
    [SerializeField] Vector2 flightSpawnRange = new(160f, 380f);
    [SerializeField] float encounterRadius = 120f;      // grupo a menos disto = "encontro"
    [SerializeField] float encounterTimeout = 45f;      // seca de encontros → força anel próximo
    [SerializeField] Vector2 pressureSpawnRange = new(70f, 140f);

    [Header("Ciclo Dia/Noite")]
    [SerializeField] float shiftChangeDuration = 120f;  // janela da "troca de turno"

    AnimalDefinition[] defs;
    readonly List<AnimalGroup> groups = new();

    Transform player;
    DragonController dragonCtrl;
    DragonGrowth dragonGrowth;
    DragonVitals dragonVitals;
    bool dragonWasFlying;
    float nextPopulate;

    // ---- encontros & direção de deslocamento
    Vector3 lastPlayerPos, playerVel;
    float lastEncounterAt;

    // ---- troca de turno (dia/noite)
    AnimalDefinition.ActivityPeriod lastPeriod;
    float shiftUntil;

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

    /// <summary>Passo de caçada (Shift): a fauna quase não percebe o dragão.</summary>
    public static bool DragonStealth =>
        Instance != null && Instance.dragonCtrl != null && Instance.dragonCtrl.IsStealth;

    /// <summary>Contra-ataque da fauna (alce/urso/javali defendendo-se).
    /// `source` = posição do atacante — alimenta o feedback de dano.</summary>
    public static void DamageDragon(float damage, Vector3? source = null)
    {
        if (Instance == null || Instance.dragonVitals == null) return;
        // Damage já dispara DragonController.OnDamaged (reação) e o evento
        // OnDamaged dos assinantes de feedback — nada mais a chamar aqui
        Instance.dragonVitals.Damage(damage, source);
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
        WildlifePool.Ensure(gameObject);

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
        // Start (não Awake): o DayNightCycle já plugou o Provider do relógio
        lastPeriod = WildlifeClock.Current;
        lastEncounterAt = Time.time;
        if (player != null) lastPlayerPos = player.position;

        // leva inicial: o mundo já nasce habitado (anel mais próximo)
        for (int i = 0; i < 40; i++)
            TrySpawnGroup(spawnRadiusMin, spawnRadiusMax);
    }

    void Update()
    {
        if (player == null || defs == null || defs.Length == 0) return;
        float dt = Time.deltaTime;
        float now = Time.time;

        // ---- direção/velocidade de deslocamento (alimenta o spawn à frente no voo)
        Vector3 pv = (player.position - lastPlayerPos) / Mathf.Max(dt, 1e-4f);
        lastPlayerPos = player.position;
        if (pv.sqrMagnitude < 60f * 60f)   // teleporte/carregamento não conta
            playerVel = Vector3.Lerp(playerVel, pv, 1f - Mathf.Exp(-3f * dt));

        // ---- grupos pensam (migração, caça, uivos, alarme)
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var g = groups[i];
            g.Tick(dt);
            if (g.IsEmpty) { groups.RemoveAt(i); continue; }

            Vector3 c = g.Centroid();
            float dc = Horizontal(c, player.position);
            if (dc < encounterRadius) lastEncounterAt = now;   // há fauna por perto

            // grupo ficou para trás → recicla
            if (dc > despawnRadius) DespawnGroup(i);
        }

        // ---- barulho de pouso: o dragão aterrissar perto acorda a vizinhança
        bool flying = dragonCtrl != null && dragonCtrl.IsFlying;
        if (dragonWasFlying && !flying)
            foreach (var a in AnimalAgent.All)
                if (Horizontal(a.transform.position, player.position) < 45f)
                    a.Startle(player.position);
        dragonWasFlying = flying;

        // ---- troca de turno: o período virou (amanheceu/anoiteceu)?
        var period = WildlifeClock.Current;
        if (period != lastPeriod)
        {
            lastPeriod = period;
            shiftUntil = now + shiftChangeDuration;
            // anoiteceu: as alcateias anunciam o turno — coro de uivos
            if (period == AnimalDefinition.ActivityPeriod.Noite)
                foreach (var g in groups) g.HowlSoon();
        }

        // ---- reposição contínua
        if (now >= nextPopulate)
        {
            nextPopulate = now + populateInterval;

            // durante a troca de turno, recicla (gradualmente, longe e fora da
            // câmera) quem não pertence ao novo período — cervos saem, lobos entram
            if (now < shiftUntil) RecycleOneOffPeriodGroup(period);

            int alive = AnimalAgent.All.Count;
            if (alive < maxAnimals)
            {
                // seca de encontros? força o anel próximo até a fauna reaparecer
                bool pressure = now - lastEncounterAt > encounterTimeout;
                for (int i = 0; i < attemptsPerCycle; i++)
                {
                    if (pressure)
                        TrySpawnGroup(pressureSpawnRange.x, pressureSpawnRange.y,
                                      allowFlightBias: false);
                    else
                        TrySpawnGroup(spawnRadiusMin, spawnRadiusMax);
                }
            }
        }
    }

    /// <summary>Devolve o grupo `index` inteiro ao pool e o esquece em todo lugar.</summary>
    void DespawnGroup(int index)
    {
        var g = groups[index];
        foreach (var other in groups)
            if (other != g && other.HuntTarget != null && other.HuntTarget.Group == g)
                other.ForgetTarget(other.HuntTarget);
        for (int m = g.Members.Count - 1; m >= 0; m--)
            g.Members[m].Despawn();
        groups.RemoveAt(index);
    }

    /// <summary>
    /// Troca de turno: remove UM grupo fora do período por ciclo — só longe
    /// (>250 m) e fora do cone da câmera, para ninguém ver o mundo "piscar".
    /// </summary>
    void RecycleOneOffPeriodGroup(AnimalDefinition.ActivityPeriod period)
    {
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var g = groups[i];
            if ((g.Def.activity & period) != 0) continue;   // ainda em turno
            Vector3 c = g.Centroid();
            if (Horizontal(c, player.position) < 250f || InCameraView(c)) continue;
            DespawnGroup(i);
            return;
        }
    }

    /// <summary>Ponto dentro do cone de visão da câmera principal (aproximação barata).</summary>
    static bool InCameraView(Vector3 worldPos)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 to = worldPos - cam.transform.position;
        to.y *= 0.5f;   // tolerância vertical (relevo não conta tanto)
        // ~meia FOV horizontal + margem: 60° verticais ≈ 45° de meio-cone em 16:9
        return Vector3.Angle(cam.transform.forward, to) < cam.fieldOfView * 0.75f;
    }

    static float Horizontal(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ================================================================ SPAWN
    void TrySpawnGroup(float radiusMin, float radiusMax, bool allowFlightBias = true)
    {
        if (player == null) return;

        // ponto candidato: em deslocamento rápido (voo!), a maioria das
        // tentativas nasce num cone à FRENTE — o jogador sobrevoa fauna,
        // não o vazio que ela deixou para trás
        Vector2 dir;
        float dist;
        Vector3 vel = playerVel; vel.y = 0f;
        if (allowFlightBias && vel.magnitude > flightBiasSpeed &&
            Random.value < flightBiasFraction)
        {
            Vector3 d3 = Quaternion.Euler(0f, Random.Range(-flightConeHalfAngle,
                                                            flightConeHalfAngle), 0f) *
                         vel.normalized;
            dir = new Vector2(d3.x, d3.z);
            dist = Random.Range(flightSpawnRange.x, flightSpawnRange.y);
        }
        else
        {
            dir = Random.insideUnitCircle.normalized;
            dist = Random.Range(Mathf.Max(radiusMin, minPlayerDistance), radiusMax);
        }
        Vector3 origin = player.position + new Vector3(dir.x, 0f, dir.y) * dist;

        // pop-in visível? (perto E dentro do cone da câmera) → tenta o lado oposto
        if (dist < noPopInFrontDistance && InCameraView(origin))
        {
            origin = player.position - new Vector3(dir.x, 0f, dir.y) * dist;
            if (InCameraView(origin)) return;   // câmera cobrindo os dois lados
        }

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
            // horário importa DE VERDADE: quem está no turno domina o elenco,
            // quem está fora vira exceção (raro, não impossível)
            w *= (d.activity & period) != 0 ? 1.5f : 0.15f;
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

        // variação visual: prefab de cor sorteado (c1..cN) + escala própria.
        // O pool recicla o corpo — o Init() abaixo dá a ele uma vida nova.
        var prefab = role.prefabs[Random.Range(0, role.prefabs.Length)];
        var go = WildlifePool.Get(prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        go.name = $"{def.speciesName} ({role.name})";
        go.transform.SetParent(transform, true);

        var agent = go.GetComponent<AnimalAgent>();
        if (agent == null) agent = go.AddComponent<AnimalAgent>();
        agent.Init(def, role, group, slot, Random.Range(role.scaleRange.x, role.scaleRange.y));
        return agent;
    }
}
