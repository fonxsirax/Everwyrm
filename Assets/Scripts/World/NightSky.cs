using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Céu noturno do Everwyrm — estudo em Docs/estudo-ceu-noturno.md, revisão em
/// Docs/revisao-ceu-noturno.md. Usa o pipeline NATIVO do HDRP no máximo:
///  - Estrelas/Via Láctea: cubemap procedural (seed do mundo) gerado UMA vez
///    no load (shader Everwyrm/NightSkyStarGen) e aplicado no
///    spaceEmissionTexture do Physically Based Sky → o amanhecer as apaga
///    pela FÍSICA da atmosfera e a água futura as reflete de graça.
///  - Firmamento ESTÁTICO por decisão de design (só as nuvens se movem);
///    spaceRotation é setado uma vez no load (poleTilt = orientação fixa).
///  - Lua: TODO o visual dela vive aqui (disco, fase, textura, halo); o
///    DayNightCycle cuida só da LUZ (lux/cor/sombras) e da órbita. Fase
///    automática por ciclo lunar de 29.5 dias in-game (determinístico).
///  - Estrelas cadentes: único sistema próprio (HDRP não tem; VFX Graph não
///    está no projeto) — eventos raros por Poisson, risco emissivo HDR +
///    detritos Shuriken.
/// Vive no mesmo GameObject do DayNightCycle, que é a única fonte de tempo.
/// </summary>
[RequireComponent(typeof(DayNightCycle))]
public class NightSky : MonoBehaviour
{
    [Header("Estrelas (procedural, determinístico)")]
    [Tooltip("0 = usa a seed do mundo (InfiniteTerrain) — cada mundo tem seu próprio céu.")]
    public int seedOverride = 0;
    [Tooltip("Resolução por face do cubemap. 1024 ≈ 25 MB VRAM; 2048 ≈ 100 MB (mais nítido).")]
    public int resolution = 1024;
    [Tooltip("HDRI externo (Via Láctea fotográfica, cubemap). Se setado, SUBSTITUI o procedural — o resto do sistema não muda.")]
    public Cubemap hdriOverride;
    [Tooltip("Parâmetros de GERAÇÃO (usar 'Regerar estrelas' no menu ⋮ para aplicar em Play).")]
    [Range(0.2f, 3f)] public float starDensity = 1f;
    [Range(0.1f, 20f)] public float starIntensity = 3f;
    [Range(0f, 3f)] public float milkyWayIntensity = 1f;
    [Tooltip("Meia-largura da faixa da Via Láctea (0-1).")]
    [Range(0.05f, 0.6f)] public float milkyWayWidth = 0.22f;
    [Tooltip("Multiplicador do espaço por HORA solar. ZERO durante o dia e o crepúsculo (constante 1.8 fazia estrelas furarem o céu laranja das 17:55/06:10 — a exposição baixa do fim de tarde vence a física); entra depois do pôr, some antes do nascer. 1.8 = estrelas médias vencem o brilho do céu ao luar.")]
    public AnimationCurve spaceMultiplierByHour = DefaultSpaceMultiplier();

    public static AnimationCurve DefaultSpaceMultiplier() => new(
        new Keyframe(0f, 1.8f), new Keyframe(4.75f, 1.8f), new Keyframe(6.25f, 0f),
        new Keyframe(17.75f, 0f), new Keyframe(19.5f, 1.8f), new Keyframe(24f, 1.8f));
    [Tooltip("Cintilação GLOBAL sutil (amplitude da variação de brilho). 0 desliga.")]
    [Range(0f, 0.2f)] public float shimmer = 0.04f;
    [Tooltip("Orientação FIXA do firmamento (graus de inclinação). As estrelas não giram — decisão de design: só as nuvens se movem no céu.")]
    [Range(0f, 90f)] public float poleTilt = 35f;

    [Header("Lua (visual — a luz fica no DayNightCycle)")]
    [Tooltip("Diâmetro do disco no céu (graus). A lua real tem ~0.5; 7 = fantasia marcante; 16 = lua ÉPICA dominando o céu (estilo Majora's Mask). A sombra usa no máx. 4° p/ não derreter.")]
    [Range(0.5f, 25f)] public float moonAngularDiameter = 16f;
    [Tooltip("Fase automática: ciclo lunar de 29.5 dias IN-GAME (determinístico, começa gibosa crescente). Desligado usa a fase manual.")]
    public bool autoLunarCycle = true;
    [Tooltip("Fase manual (0 = nova, 0.5 = cheia) quando o ciclo automático está desligado.")]
    [Range(0f, 1f)] public float moonPhaseManual = 0.5f;
    [Tooltip("Textura de superfície (crateras). Recomendado: NASA CGI Moon Kit (gratuito). Vazio = crateras procedurais.")]
    public Texture2D moonSurfaceTexture;
    [Tooltip("Luz da terra no lado escuro da lua — detalhe que vende a fase.")]
    [Range(0f, 1f)] public float earthshine = 1f;
    [Tooltip("Brilho da SUPERFÍCIE do disco. O HDRP ilumina o disco com um 'sol virtual' de 130k lux — cru, estoura branco na exposição noturna. 0.012 = disco protagonista (~50× o céu ao luar), detalhes legíveis.")]
    [Range(0.0005f, 0.05f)] public float moonDiscBrightness = 0.012f;
    [Tooltip("Tamanho do halo/glow em volta da lua (graus além do disco).")]
    [Range(0f, 12f)] public float moonFlareSize = 5f;
    [Range(1f, 10f)] public float moonFlareFalloff = 4f;
    [Tooltip("Intensidade do halo — usa o mesmo sol virtual de 130k lux, precisa ser minúscula. Deve ficar ABAIXO do brilho do disco (glow, não anel).")]
    [Range(0f, 0.02f)] public float moonFlareIntensity = 0.0008f;
    public Color moonFlareTint = new(0.75f, 0.82f, 1f);

    [Header("Estrelas cadentes (eventos raros)")]
    public bool shootingStars = true;
    [Tooltip("Intervalo MÉDIO entre eventos (s). Agendado por Poisson — nunca vira cadência fixa.")]
    [Min(10f)] public float meteorMeanInterval = 90f;
    [Tooltip("Intensidade HDR do risco (o bloom faz o glow).")]
    public float meteorIntensity = 80f;
    [Tooltip("Vida mínima/máxima de um meteoro (s).")]
    public Vector2 meteorLifetime = new(0.45f, 1.2f);
    [Tooltip("Partículas de detritos se desprendendo no percurso.")]
    public bool meteorDebris = true;

    // ------------------------------------------------------------------ estado
    DayNightCycle cycle;
    RenderTexture starCube;
    Texture2D generatedMoonTex;
    float nextMeteorAt = -1f;
    System.Random rng;                    // eventos (meteoros) — única fonte

    // Meteoros a ~1.2 km: acima da camada de névoa (fogMaxHeight ~120 m), a
    // atenuação atravessa só a camada baixa — dimerização leve, aceitável.
    const float MeteorDistance = 1200f;

    class Meteor
    {
        public LineRenderer line;
        public Material mat;
        public ParticleSystem debris;
        public Material debrisMat;
        public Vector3 pos, vel;
        public float life, t;
        public Color color;
        public bool active;
    }
    readonly List<Meteor> meteors = new();

    // bases das 6 faces do cubemap (+X -X +Y -Y +Z -Z, convenção DX)
    static readonly Vector3[] FaceX = { new(0,0,-1), new(0,0, 1), new(1,0,0), new(1,0, 0), new(1,0,0), new(-1,0,0) };
    static readonly Vector3[] FaceY = { new(0,-1,0), new(0,-1,0), new(0,0,1), new(0,0,-1), new(0,-1,0), new(0,-1,0) };
    static readonly Vector3[] FaceZ = { new(1,0, 0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1) };

    void Start()
    {
        cycle = GetComponent<DayNightCycle>();   // Start dele já rodou (ordem -100)
        if (cycle.Sky == null)
        {
            Debug.LogWarning("[NightSky] DayNightCycle sem volume PBS — céu noturno desativado.");
            enabled = false;
            return;
        }

        int seed = WorldSeed();
        rng = new System.Random(seed ^ 0x5EED);

        if (moonSurfaceTexture == null)
            generatedMoonTex = GenerateMoonTexture(seed);

        ApplySpaceTexture(seed);
        ApplyMoonVisuals();
        // firmamento fixo: orientação setada UMA vez (estrelas não se movem)
        cycle.Sky.spaceRotation.value = new Vector3(poleTilt, 0f, 0f);
        ScheduleNextMeteor();
    }

    void OnDestroy()
    {
        if (starCube != null) starCube.Release();
        if (generatedMoonTex != null) Destroy(generatedMoonTex);
        foreach (var m in meteors)
        {
            if (m.line != null) Destroy(m.line.gameObject);
            if (m.mat != null) Destroy(m.mat);
            if (m.debrisMat != null) Destroy(m.debrisMat);
        }
    }

    void Update()
    {
        var sky = cycle.Sky;
        if (sky == null) return;
        float hour = cycle.TimeOfDay;

        // ---- fase → intensidade da luz noturna (o DayNightCycle escala a lua)
        cycle.moonIllumination = 0.5f * (1f - Mathf.Cos(MoonPhase * 2f * Mathf.PI));

        // ---- brilho: curva artística (hora solar) + cintilação global sutil
        float k = Mathf.Max(0f, spaceMultiplierByHour.Evaluate(cycle.SolarCurveHour(hour)));
        if (shimmer > 0f)
            k *= 1f + (Mathf.PerlinNoise(Time.time * 1.9f, 0.37f) - 0.5f) * 2f * shimmer;
        sky.spaceEmissionMultiplier.value = k;

        // ---- visual da lua (campos simples — barato; sliders funcionam em Play)
        ApplyMoonVisuals();

        UpdateMeteors();
    }

    int WorldSeed()
    {
        if (seedOverride != 0) return seedOverride;
        var world = FindFirstObjectByType<InfiniteTerrain>();
        return world != null ? world.seed : 20260718;
    }

    // ------------------------------------------------------------- CUBEMAP
    void ApplySpaceTexture(int seed)
    {
        var sky = cycle.Sky;
        sky.spaceEmissionTexture.overrideState = true;
        sky.spaceEmissionMultiplier.overrideState = true;
        sky.spaceRotation.overrideState = true;

        if (hdriOverride != null)                 // upgrade drop-in: mesmo slot
        {
            sky.spaceEmissionTexture.value = hdriOverride;
            return;
        }

        var shader = Shader.Find("Everwyrm/NightSkyStarGen");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("[NightSky] Shader 'Everwyrm/NightSkyStarGen' ausente ou com erro de compilação — sem estrelas.");
            return;
        }

        var gen = new System.Random(seed);        // determinístico p/ o cubemap
        var mat = new Material(shader);
        // offset pequeno de propósito: somado às células (~±300), precisa caber
        // com folga na precisão do float dentro do hash
        mat.SetVector("_SeedOffset", new Vector4(
            (float)gen.NextDouble() * 96f, (float)gen.NextDouble() * 96f,
            (float)gen.NextDouble() * 96f, 0f));
        mat.SetVector("_Params", new Vector4(
            starDensity, starIntensity, milkyWayIntensity, milkyWayWidth));
        // plano galáctico aleatório do mundo (normal unitária)
        var n = new Vector3((float)gen.NextDouble() * 2f - 1f,
                            (float)gen.NextDouble() * 2f - 1f,
                            (float)gen.NextDouble() * 2f - 1f);
        if (n.sqrMagnitude < 0.01f) n = Vector3.up;
        mat.SetVector("_GalaxyNormal", n.normalized);

        int size = Mathf.Clamp(Mathf.NextPowerOfTwo(resolution), 256, 4096);
        starCube = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf)
        {
            dimension = TextureDimension.Cube,
            useMipMap = false,
            name = "NightSky Stars"
        };
        starCube.Create();

        var tmp = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBHalf);
        for (int f = 0; f < 6; f++)
        {
            mat.SetVector("_FaceX", FaceX[f]);
            mat.SetVector("_FaceY", FaceY[f]);
            mat.SetVector("_FaceZ", FaceZ[f]);
            Graphics.Blit(Texture2D.blackTexture, tmp, mat);
            Graphics.CopyTexture(tmp, 0, 0, starCube, f, 0);
        }
        RenderTexture.ReleaseTemporary(tmp);
        Destroy(mat);

        sky.spaceEmissionTexture.value = starCube;
        Debug.Log($"[NightSky] Céu estelar gerado (seed {seed}, {size}px/face).");
    }

    /// <summary>Regenera o cubemap com os valores atuais do Inspector —
    /// densidade/intensidade/Via Láctea são parâmetros de GERAÇÃO.</summary>
    [ContextMenu("Regerar estrelas (aplica os parâmetros de geração)")]
    public void RegenerateStars()
    {
        if (cycle == null || cycle.Sky == null) return;
        if (starCube != null) { starCube.Release(); starCube = null; }
        ApplySpaceTexture(WorldSeed());
    }

    // ---------------------------------------------------------------- LUA
    /// <summary>Fase atual (0 = nova, 0.5 = cheia) — automática pelo ciclo
    /// lunar de 29.5 dias in-game, ou manual. Pública p/ gameplay futuro
    /// (lobisomens? marés? eventos de lua cheia...).</summary>
    public float MoonPhase => autoLunarCycle
        ? Mathf.Repeat(0.4f + (cycle.Day - 1) / 29.5f, 1f)
        : moonPhaseManual;

    void ApplyMoonVisuals()
    {
        var md = cycle.MoonData;
        if (md == null) return;
        md.surfaceTexture = moonSurfaceTexture != null ? moonSurfaceTexture : generatedMoonTex;
        md.earthshine = earthshine;
        // o tint escala o "sol virtual" interno de 130k lux p/ a exposição noturna
        md.surfaceTint = Color.white * moonDiscBrightness;
        // DESACOPLADO de propósito: o DISCO lê diameterOverride (modo default),
        // e a SOMBRA/specular leem angularDiameter — com lua gigante, o visual
        // cresce à vontade mas a penumbra fica capada em 4° (sombras não viram
        // mancha) — verificado no fonte do HDRP (HDGpuLightsBuilder).
        md.diameterOverride = moonAngularDiameter;
        md.angularDiameter = Mathf.Min(moonAngularDiameter, 4f);
        md.moonPhase = MoonPhase;
        md.flareSize = moonFlareSize;
        md.flareFalloff = moonFlareFalloff;
        md.flareTint = moonFlareTint;
        md.flareMultiplier = moonFlareIntensity;
    }

    /// <summary>Fallback procedural de crateras (fBm + círculos com borda) —
    /// funcional até a textura da NASA entrar no slot.</summary>
    static Texture2D GenerateMoonTexture(int seed)
    {
        const int s = 512;
        var tex = new Texture2D(s, s, TextureFormat.RGB24, true) { name = "NightSky Moon (proc)" };
        var rnd = new System.Random(seed ^ 0x0770);
        // crateras: centro, raio, profundidade
        int n = 90;
        var cx = new float[n]; var cy = new float[n]; var cr = new float[n]; var cd = new float[n];
        for (int i = 0; i < n; i++)
        {
            cx[i] = (float)rnd.NextDouble() * s;
            cy[i] = (float)rnd.NextDouble() * s;
            cr[i] = Mathf.Pow((float)rnd.NextDouble(), 2.2f) * s * 0.09f + 2.5f;
            cd[i] = 0.25f + (float)rnd.NextDouble() * 0.5f;
        }
        float ox = (float)rnd.NextDouble() * 64f, oy = (float)rnd.NextDouble() * 64f;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // base: "mares" escuros em noise largo + granulado fino
                float baseV = 0.78f
                    + (Mathf.PerlinNoise(ox + x * 0.008f, oy + y * 0.008f) - 0.5f) * 0.38f
                    + (Mathf.PerlinNoise(ox + x * 0.05f, oy + y * 0.05f) - 0.5f) * 0.10f;
                // crateras: sombra no miolo, borda clara
                for (int i = 0; i < n; i++)
                {
                    float dx = x - cx[i], dy = y - cy[i];
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / cr[i];
                    if (d > 1.25f) continue;
                    if (d < 1f) baseV -= cd[i] * (1f - d * d) * 0.5f;
                    else baseV += cd[i] * (1.25f - d) * 0.55f;   // rim iluminado
                }
                baseV = Mathf.Clamp01(baseV);
                px[y * s + x] = new Color(baseV, baseV * 0.985f, baseV * 0.95f);
            }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    // ---------------------------------------------------- ESTRELAS CADENTES
    void ScheduleNextMeteor()
    {
        // Poisson: -ln(U) * média — raro e irregular, nunca cadência
        nextMeteorAt = Time.time +
            (float)(-System.Math.Log(1.0 - rng.NextDouble()) * meteorMeanInterval);
    }

    void UpdateMeteors()
    {
        if (shootingStars && cycle.IsNight && Time.time >= nextMeteorAt)
        {
            SpawnMeteor();
            ScheduleNextMeteor();
        }

        for (int i = 0; i < meteors.Count; i++)
        {
            var m = meteors[i];
            if (!m.active) continue;
            m.t += Time.deltaTime;
            if (m.t >= m.life)
            {
                m.active = false;
                m.line.enabled = false;
                if (m.debris != null) m.debris.Stop();
                continue;
            }
            m.pos += m.vel * Time.deltaTime;
            // envelope suave de brilho (entra e sai sem corte)
            float env = Mathf.Sin(Mathf.Clamp01(m.t / m.life) * Mathf.PI);
            float trail = m.vel.magnitude * 0.22f;
            m.line.SetPosition(0, m.pos);
            m.line.SetPosition(1, m.pos - m.vel.normalized * trail);
            Color c = m.color * (meteorIntensity * env);
            if (m.mat.HasProperty("_UnlitColor")) m.mat.SetColor("_UnlitColor", c);
            if (m.mat.HasProperty("_EmissiveColor")) m.mat.SetColor("_EmissiveColor", c);
            if (m.debris != null) m.debris.transform.position = m.pos;
        }
    }

    void SpawnMeteor()
    {
        var cam = Camera.main;
        if (cam == null) return;

        var m = GetPooledMeteor();

        // ponto alto do domo em azimute aleatório, longe da câmera
        float az = (float)rng.NextDouble() * 360f * Mathf.Deg2Rad;
        float elev = Mathf.Lerp(35f, 75f, (float)rng.NextDouble()) * Mathf.Deg2Rad;
        var dirUp = new Vector3(Mathf.Cos(elev) * Mathf.Sin(az), Mathf.Sin(elev),
                                Mathf.Cos(elev) * Mathf.Cos(az));
        m.pos = cam.transform.position + dirUp * MeteorDistance;

        // trajetória: tangente ao domo com viés pra baixo, velocidade variada
        var rndDir = new Vector3((float)rng.NextDouble() * 2f - 1f,
                                 (float)rng.NextDouble() * 2f - 1f,
                                 (float)rng.NextDouble() * 2f - 1f);
        var tangent = Vector3.Cross(dirUp, rndDir.normalized).normalized;
        tangent = (tangent + Vector3.down * 0.7f).normalized;
        float angSpeed = Mathf.Lerp(9f, 26f, (float)rng.NextDouble());   // graus/s
        m.vel = tangent * (angSpeed * Mathf.Deg2Rad * MeteorDistance);
        m.life = Mathf.Lerp(meteorLifetime.x, meteorLifetime.y, (float)rng.NextDouble());
        m.t = 0f;
        // cor levemente variada: branco-azulado a branco-quente
        m.color = Color.Lerp(new Color(0.75f, 0.82f, 1f), new Color(1f, 0.92f, 0.8f),
                             (float)rng.NextDouble());
        m.active = true;
        m.line.enabled = true;
        m.line.SetPosition(0, m.pos);
        m.line.SetPosition(1, m.pos);

        if (meteorDebris && m.debris != null)
        {
            m.debris.transform.position = m.pos;
            m.debris.Play();
        }
    }

    Meteor GetPooledMeteor()
    {
        foreach (var m in meteors)
            if (!m.active) return m;

        var meteor = new Meteor();
        var go = new GameObject("Meteoro");
        go.transform.SetParent(transform, false);

        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        meteor.mat = new Material(shader);

        meteor.line = go.AddComponent<LineRenderer>();
        meteor.line.positionCount = 2;
        meteor.line.startWidth = 5f;              // metros a ~1.2 km ≈ 0.24°
        meteor.line.endWidth = 0.4f;
        meteor.line.numCapVertices = 3;
        meteor.line.material = meteor.mat;
        meteor.line.shadowCastingMode = ShadowCastingMode.Off;
        meteor.line.receiveShadows = false;
        meteor.line.enabled = false;

        if (meteorDebris)
        {
            var dgo = new GameObject("Detritos");
            dgo.transform.SetParent(go.transform, false);
            var ps = dgo.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.maxParticles = 64;
            var em = ps.emission;
            em.rateOverTime = 26f;
            var sol = ps.sizeOverLifetime;        // encolher substitui fade alfa
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var rend = dgo.GetComponent<ParticleSystemRenderer>();
            meteor.debrisMat = new Material(shader);
            Color dc = new Color(1f, 0.85f, 0.65f) * (meteorIntensity * 0.25f);
            if (meteor.debrisMat.HasProperty("_UnlitColor")) meteor.debrisMat.SetColor("_UnlitColor", dc);
            if (meteor.debrisMat.HasProperty("_EmissiveColor")) meteor.debrisMat.SetColor("_EmissiveColor", dc);
            rend.sharedMaterial = meteor.debrisMat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            meteor.debris = ps;
        }

        meteors.Add(meteor);
        return meteor;
    }
}
