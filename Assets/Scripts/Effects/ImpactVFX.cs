using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VFX PROCEDURAL de impacto — partículas montadas por código, no mesmo espírito
/// do CombatVFX. São o padrão do ImpactEffects quando um tipo de impacto não tem
/// prefab: poeira de pouso, folhas e galhos ao bater numa árvore, lascas ao
/// raspar rocha, spray e splash na água.
///
/// TEXTURAS: os materiais vêm de Resources/ImpactVFX (gerados por
/// Tools > Everwyrm > VFX de Impacto — poeira do fog Inguz, gotas/splash/cacos
/// e folhas sombreadas gerados com ruído; visual realista p/ HDRP). Sem eles,
/// cai no quadradinho de cor sólida — funciona, só fica mais cru. Prefab na
/// tabela do ImpactEffects continua mandando.
/// </summary>
public static class ImpactVFX
{
    // ---- nomes dos materiais em Resources/ImpactVFX (ImpactVFXSetup gera)
    const string MatPuffSoft = "PuffSoft";    // nuvem macia (poeira, pó)
    const string MatPuffDense = "PuffDense";  // nuvem densa (pó de pedra)
    const string MatDroplet = "Droplet";      // gotícula (spray, splash)
    const string MatLeaf = "Leaf";            // atlas 2×2 procedural (fallback)
    const string MatLeafA = "LeafA";          // folha carvalho sombreada (grayscale)
    const string MatLeafB = "LeafB";          // folha lisa sombreada (grayscale)
    const string MatChip = "Chip";            // lasca/torrão irregular
    const string MatRipple = "Ripple";        // anel de ondulação
    const string MatSplash = "SplashStreak";  // jatos de splash 2×2 (CFXR)

    // ---- paleta
    static readonly Color SprayColor = new(0.82f, 0.92f, 1f);
    static readonly Color TwigColor = new(0.32f, 0.24f, 0.14f);
    static readonly Color ChipColor = new(0.78f, 0.76f, 0.73f);
    static readonly Color StoneDust = new(0.55f, 0.53f, 0.50f);

    // ---- tons de poeira por bioma (o chão que o dragão levanta)
    static readonly Color DustEarth = new(0.38f, 0.30f, 0.22f);   // floresta: terra escura
    static readonly Color DustDry = new(0.48f, 0.42f, 0.29f);     // campos: terra seca
    static readonly Color DustSand = new(0.80f, 0.69f, 0.46f);    // deserto: areia
    static readonly Color DustSnow = new(0.90f, 0.93f, 0.97f);    // tundra/montanha: neve

    static readonly Dictionary<string, Material> mats = new();

    /// <summary>
    /// Material tingido para as partículas. Tenta o material texturizado de
    /// Resources/ImpactVFX (transparente, receita do Winter pack); sem ele,
    /// monta um HDRP/Unlit de cor sólida — o fallback honesto de sempre.
    /// </summary>
    static Material Get(string name, Color tint, float emissive = 0f)
    {
        string key = $"{name}#{(Color32)tint}{emissive}";
        if (mats.TryGetValue(key, out var m) && m != null) return m;

        var baseMat = Resources.Load<Material>("ImpactVFX/" + name);
        if (baseMat != null)
        {
            m = new Material(baseMat);
            if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", tint);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.color = tint;
            if (emissive > 0f && m.HasProperty("_EmissiveColor"))
                m.SetColor("_EmissiveColor", tint * emissive);
        }
        else
        {
            var shader = Shader.Find("HDRP/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            m = new Material(shader);
            if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", tint);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.color = tint;
            if (emissive > 0f && m.HasProperty("_EmissiveColor"))
                m.SetColor("_EmissiveColor", tint * emissive);
        }
        mats[key] = m;
        return m;
    }

    static ParticleSystem NewSystem(Transform parent, string matName, Color tint,
                                    float emissive = 0f,
                                    ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
    {
        var go = new GameObject("PFX");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = Get(matName, tint, emissive);
        rend.renderMode = mode;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return ps;
    }

    /// <summary>Encolher até sumir — HDRP/Unlit ignora alfa por vertex color,
    /// então o fade é por tamanho; com textura macia o efeito lê como dissipar.</summary>
    static void Shrink(ParticleSystem ps, float holdUntil = 0.65f)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(holdUntil, 0.75f), new Keyframe(1f, 0f)));
    }

    /// <summary>Cresce e some — para anéis de ondulação e nuvens que abrem.</summary>
    static void GrowFade(ParticleSystem ps, float from = 0.25f, float peak = 0.55f)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, from), new Keyframe(peak, 1f), new Keyframe(1f, 0f)));
    }

    /// <summary>Freia a partícula ao longo da vida (poeira assenta, folha planeia).</summary>
    static void Dampen(ParticleSystem ps, float dampen, float limit)
    {
        var lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.limit = new ParticleSystem.MinMaxCurve(limit);
        lim.dampen = dampen;
    }

    static void Burst(ParticleSystem ps, float count)
    {
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1f, count)) });
    }

    /// <summary>Flipbook: cada partícula sorteia UM quadro da grade (sem animar).
    /// É o que dá variedade real com as texturas do CartoonFX (2×2, 3×3, 1×3).</summary>
    static void Sheet(ParticleSystem ps, int tilesX, int tilesY)
    {
        var tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.numTilesX = tilesX;
        tsa.numTilesY = tilesY;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f);
    }

    /// <summary>Cada folha sorteia um dos 4 quadros do atlas 2×2 (sem animar).</summary>
    static void LeafSheet(ParticleSystem ps) => Sheet(ps, 2, 2);

    // ------------------------------------------------------------- ROTEADOR
    /// <summary>Impacto pontual sem prefab: escolhe o placeholder do tipo.</summary>
    public static void Spawn(in ImpactEvent e, float strength01)
    {
        switch (e.kind)
        {
            case ImpactKind.Landing:
                Dust(e.position, strength01 * 0.7f, e.scale, GroundTintAt(e.position));
                break;

            case ImpactKind.HardLanding:
                Dust(e.position, Mathf.Max(0.55f, strength01), e.scale, GroundTintAt(e.position));
                Debris(e.position, strength01, e.scale, GroundTintAt(e.position));
                break;

            // árvore ou pedra? o mundo sabe onde estão as árvores (GetTreesNear)
            case ImpactKind.ObstacleStrike:
                if (IsTreeAt(e.position)) Foliage(e.position, e.normal, e.velocity, strength01, e.scale);
                else RockChips(e.position, e.normal, strength01, e.scale);
                break;

            case ImpactKind.WaterEntry:
                Splash(e.position, strength01, e.scale);
                break;
        }
    }

    /// <summary>Efeito que vive enquanto a condição durar. Null = tipo sem visual.</summary>
    public static GameObject CreateContinuous(ImpactKind kind, Transform parent, float scale)
    {
        return kind == ImpactKind.WaterSkim ? WaterSkim(parent, scale) : null;
    }

    // ------------------------------------------------------- POUSO (POEIRA)
    /// <summary>Poeira do pouso: anel baixo que abre para os lados e assenta.</summary>
    public static void Dust(Vector3 position, float strength01, float scale, Color tint)
    {
        var root = new GameObject("Poeira");
        root.transform.position = position;

        var ps = NewSystem(root.transform, MatPuffSoft, tint);
        Sheet(ps, 2, 2);                          // poeira 2×2: 4 variações do fog
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale,
                                                         3.5f * scale * Mathf.Lerp(0.6f, 1.5f, strength01));
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.2f * scale);
        main.gravityModifier = -0.04f;            // a nuvem sobe de leve enquanto abre
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 78f;                        // quase rasteiro: espalha, não sobe
        shape.radius = 0.5f * scale;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);   // nuvem gira lenta

        Burst(ps, Mathf.Lerp(8f, 30f, strength01));
        Dampen(ps, 0.55f, 1.2f);                  // assenta em vez de disparar
        GrowFade(ps, 0.45f, 0.4f);                // abre e se dissipa
        ps.Play();

        Object.Destroy(root, 3f);
    }

    /// <summary>Torrões e cascalho de uma queda feia — acompanham a poeira.</summary>
    static void Debris(Vector3 position, float strength01, float scale, Color tint)
    {
        var root = new GameObject("Detritos");
        root.transform.position = position + Vector3.up * 0.15f * scale;

        var ps = NewSystem(root.transform, MatChip, tint * 0.8f);
        Sheet(ps, 3, 3);                          // cacos 3×3: 9 formas
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * scale, 6f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f * scale, 0.22f * scale);
        main.gravityModifier = 1.6f;              // pedaços pesados: sobem e caem
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.35f * scale;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        Burst(ps, Mathf.Lerp(6f, 22f, strength01));
        Shrink(ps, 0.85f);
        ps.Play();

        Object.Destroy(root, 3f);
    }

    // --------------------------------------------------- ÁRVORE (FOLHAGEM)
    /// <summary>
    /// Bateu numa árvore: folhas arrancadas rodopiando + galhos secos. As folhas
    /// saem na direção do baque, freiam no ar e descem planando; os galhos caem
    /// direto. É o efeito que dá peso à copa que o dragão atravessou.
    /// </summary>
    public static void Foliage(Vector3 position, Vector3 normal, Vector3 velocity,
                               float strength01, float scale)
    {
        var root = new GameObject("Folhas");
        // direção do estrago: para onde o dragão empurrou, afastando da árvore
        Vector3 dir = velocity.sqrMagnitude > 0.01f
            ? Vector3.Slerp(velocity.normalized, normal, 0.45f)
            : normal;
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir, Vector3.up));

        // ---- folhas: as duas silhuetas do CartoonFX (grayscale tingido de verde),
        //      em tons variados. Sem os materiais LeafA/B, cai no atlas antigo.
        float total = Mathf.Lerp(14f, 55f, strength01);
        if (Resources.Load<Material>("ImpactVFX/" + MatLeafA) != null)
        {
            LeafBurst(root.transform, MatLeafA, new Color(0.30f, 0.46f, 0.15f),
                      total * 0.35f, strength01, scale);
            LeafBurst(root.transform, MatLeafA, new Color(0.45f, 0.42f, 0.16f),   // seca
                      total * 0.15f, strength01, scale);
            LeafBurst(root.transform, MatLeafB, new Color(0.36f, 0.54f, 0.20f),
                      total * 0.35f, strength01, scale);
            LeafBurst(root.transform, MatLeafB, new Color(0.24f, 0.40f, 0.13f),   // sombra
                      total * 0.15f, strength01, scale);
        }
        else
        {
            var atlas = LeafBurst(root.transform, MatLeaf, Color.white, total, strength01, scale);
            LeafSheet(atlas);
        }

        // ---- galhos: poucos, escuros, caem direto
        var twigs = NewSystem(root.transform, MatChip, TwigColor);
        Sheet(twigs, 3, 3);
        var main = twigs.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 6f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f * scale, 0.16f * scale);
        main.gravityModifier = 1.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var shape = twigs.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.4f * scale;

        var rot = twigs.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

        Burst(twigs, Mathf.Lerp(3f, 12f, strength01));
        Shrink(twigs, 0.85f);
        twigs.Play();

        Object.Destroy(root, 4f);
    }

    /// <summary>Um sistema de folhas: rodopia, freia no ar e desce planando.
    /// startSize3D respeita a proporção 1:2 das folhas do CartoonFX.</summary>
    static ParticleSystem LeafBurst(Transform parent, string matName, Color tint,
                                    float count, float strength01, float scale)
    {
        var ps = NewSystem(parent, matName, tint);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * scale,
                                                         5f * scale * Mathf.Lerp(0.5f, 1.4f, strength01));
        main.startSize3D = true;                  // folha alta: X fino, Y = 2×X
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.09f * scale, 0.2f * scale);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.18f * scale, 0.4f * scale);
        main.startSizeZ = new ParticleSystem.MinMaxCurve(1f);
        main.gravityModifier = 0.22f;             // desce devagar: folha plana
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 42f;
        shape.radius = 0.5f * scale;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);   // rodopio no ar

        Burst(ps, count);
        Dampen(ps, 0.75f, 0.9f);                  // freia rápido e vira queda lenta
        Shrink(ps, 0.8f);
        ps.Play();
        return ps;
    }

    // --------------------------------------------------------- ROCHA (LASCAS)
    /// <summary>Raspou pedra/penhasco: lascas ricocheteando + nuvenzinha de pó.</summary>
    public static void RockChips(Vector3 position, Vector3 normal, float strength01, float scale)
    {
        var root = new GameObject("Lascas");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(
            normal.sqrMagnitude > 0.001f ? normal : Vector3.up, Vector3.up));

        var chips = NewSystem(root.transform, MatChip, ChipColor);
        Sheet(chips, 3, 3);
        var main = chips.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f * scale,
                                                         9f * scale * Mathf.Lerp(0.6f, 1.4f, strength01));
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f * scale, 0.15f * scale);
        main.gravityModifier = 1.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var shape = chips.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 38f;                        // saem "para fora" da parede
        shape.radius = 0.25f * scale;

        var rot = chips.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-9f, 9f);

        Burst(chips, Mathf.Lerp(8f, 30f, strength01));
        Shrink(chips, 0.85f);
        chips.Play();

        // pó de pedra: fica pairando um instante no ponto do risco
        var dust = NewSystem(root.transform, MatPuffDense, StoneDust);
        Sheet(dust, 2, 2);
        main = dust.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * scale, 2f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.7f * scale);
        main.gravityModifier = -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f * scale;

        Burst(dust, Mathf.Lerp(4f, 14f, strength01));
        Dampen(dust, 0.6f, 0.8f);
        GrowFade(dust, 0.5f, 0.4f);
        dust.Play();

        Object.Destroy(root, 3f);
    }

    // ------------------------------------------------------------------ ÁGUA
    /// <summary>Splash: coroa de gotas + anéis de ondulação abrindo na lâmina.</summary>
    public static void Splash(Vector3 position, float strength01, float scale)
    {
        var root = new GameObject("Splash");
        root.transform.position = position;

        // coroa: leque de filetes d'água (2×2); sem o material, gotas simples
        bool hasStreak = Resources.Load<Material>("ImpactVFX/" + MatSplash) != null;
        var ps = NewSystem(root.transform, hasStreak ? MatSplash : MatDroplet,
                           SprayColor, 1.6f);
        if (hasStreak) Sheet(ps, 2, 2);
        else Sheet(ps, 1, 3);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale,
                                                         6f * scale * Mathf.Lerp(0.5f, 1.4f, strength01));
        if (hasStreak)
        {
            main.startSize3D = true;              // jato alto: X fino, Y = 2×X
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.15f * scale, 0.3f * scale);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.6f * scale);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(1f);
        }
        else main.startSize = new ParticleSystem.MinMaxCurve(0.1f * scale, 0.3f * scale);
        main.gravityModifier = 1.4f;              // gotas caem de volta no lago
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 32f;
        shape.radius = 0.7f * scale;
        shape.rotation = new Vector3(-90f, 0f, 0f);   // coroa apontando p/ cima

        Burst(ps, Mathf.Lerp(12f, 60f, strength01));
        Shrink(ps);
        ps.Play();

        // anéis de ondulação: nascem pequenos e abrem na superfície
        var rings = NewSystem(root.transform, MatRipple, SprayColor, 1.2f,
                              ParticleSystemRenderMode.HorizontalBillboard);
        main = rings.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.6f * scale, 2.4f * scale);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        shape = rings.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.15f * scale;

        Burst(rings, Mathf.Lerp(1f, 3f, strength01));
        GrowFade(rings, 0.15f, 0.7f);
        rings.Play();

        Object.Destroy(root, 2.5f);
    }

    /// <summary>
    /// Barriga raspando o lago: leque de gotículas para trás + anéis rasos de
    /// ondulação. Sutil de propósito — é tempero visual, não evento de jogo.
    /// </summary>
    static GameObject WaterSkim(Transform parent, float scale)
    {
        var root = new GameObject("Water Skim FX");
        root.transform.SetParent(parent, false);

        // ---- spray: gotas arrancadas da superfície, jogadas para trás e para cima
        var spray = NewSystem(root.transform, MatDroplet, SprayColor, 1.6f);
        Sheet(spray, 1, 3);                       // gota 1×3: 3 fases de borrão
        var main = spray.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale, 3.2f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f * scale, 0.16f * scale);
        main.gravityModifier = 1.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 260;

        var shape = spray.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 42f;
        shape.radius = 0.5f * scale;
        shape.rotation = new Vector3(-70f, 0f, 0f);   // p/ cima e ligeiramente atrás

        var em = spray.emission;
        em.rateOverTime = 55f;
        Shrink(spray);

        // ---- ondulações: anéis deitados na água, abrindo e sumindo
        var ripple = NewSystem(root.transform, MatRipple, SprayColor, 1.2f,
                               ParticleSystemRenderMode.HorizontalBillboard);
        main = ripple.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = 0.15f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 1f * scale);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 90;

        shape = ripple.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.8f * scale;
        shape.rotation = new Vector3(90f, 0f, 0f);    // deitado na lâmina

        em = ripple.emission;
        em.rateOverTime = 14f;
        GrowFade(ripple, 0.3f, 0.5f);

        return root;
    }

    // --------------------------------------------------------- CONSULTAS AO MUNDO
    static readonly List<Vector2> treeBuf = new();

    /// <summary>Tem árvore no ponto do impacto? (o mundo registra os marcos de
    /// vegetação; formações de rocha não entram nessa lista)</summary>
    static bool IsTreeAt(Vector3 position)
    {
        var world = InfiniteTerrain.Instance;
        if (world == null) return false;
        treeBuf.Clear();
        world.GetTreesNear(position, 4f, treeBuf, 1);
        return treeBuf.Count > 0;
    }

    /// <summary>Cor da poeira levantada — o chão do bioma manda (areia no
    /// deserto, neve na tundra, terra escura na mata).</summary>
    public static Color GroundTintAt(Vector3 position)
    {
        var world = InfiniteTerrain.Instance;
        if (world == null) return DustDry;

        world.BiomeWeights(position.x, position.z, out float plains, out float forest,
                           out float mount, out float cold, out float desert);
        if (desert > 0.5f) return DustSand;
        if (cold > 0.45f || mount > 0.6f) return DustSnow;
        if (forest > plains) return DustEarth;
        return DustDry;
    }
}
