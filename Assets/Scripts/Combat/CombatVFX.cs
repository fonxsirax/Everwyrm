using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VFX PROCEDURAL de combate — fallback para quando um DragonAttackData não
/// tem prefab: partículas emissivas geradas por código (brilham com o bloom
/// do HDRP). São placeholders honestos; quando um asset de fogo entrar no
/// projeto, basta arrastar os prefabs dele nos campos do ScriptableObject —
/// nada aqui precisa mudar.
/// </summary>
public static class CombatVFX
{
    public static readonly Color FireColor = new(1f, 0.45f, 0.08f);
    static readonly Color EmberColor = new(1f, 0.8f, 0.25f);
    static readonly Color SmokeColor = new(0.16f, 0.14f, 0.12f);

    static readonly Dictionary<Color, Material> mats = new();

    static Material MatFor(Color c)
    {
        if (mats.TryGetValue(c, out var m) && m != null) return m;

        var shader = Shader.Find("HDRP/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        m = new Material(shader);
        if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.color = c;
        if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", c * 14f); // bloom
        mats[c] = m;
        return m;
    }

    static ParticleSystem NewSystem(Transform parent, Color color)
    {
        var go = new GameObject("PFX");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = MatFor(color);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return ps;
    }

    static void Shrink(ParticleSystem ps)
    {
        // encolher até sumir substitui o fade alfa (HDRP/Unlit ignora vertex color)
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
    }

    // ---------------------------------------------------------------- CHAMAS
    /// <summary>Chamas presas a um alvo em combustão (BurningStatus).</summary>
    public static GameObject Flames(Transform parent, float size)
    {
        var root = new GameObject("Chamas");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.up * (size * 0.5f);

        var fire = NewSystem(root.transform, FireColor);
        var main = fire.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f * size, 0.4f * size);
        main.gravityModifier = -0.15f;                    // fogo sobe
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = fire.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * 0.45f;
        var em = fire.emission;
        em.rateOverTime = 26f;
        Shrink(fire);
        fire.Play();

        var smoke = NewSystem(root.transform, SmokeColor);
        main = smoke.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f * size, 0.55f * size);
        main.gravityModifier = -0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * 0.35f;
        em = smoke.emission;
        em.rateOverTime = 8f;
        Shrink(smoke);
        smoke.Play();

        return root;
    }

    // ------------------------------------------------------- ÁREA NO CHÃO
    /// <summary>Tapete de fogo de uma FireArea (círculo de chamas subindo).</summary>
    public static GameObject GroundFire(Transform parent, float radius)
    {
        var fire = NewSystem(parent, FireColor);
        var main = fire.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.gravityModifier = -0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        var shape = fire.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        var em = fire.emission;
        em.rateOverTime = Mathf.Clamp(radius * radius * 3.2f, 30f, 220f);
        Shrink(fire);
        fire.Play();

        var embers = NewSystem(parent, EmberColor);
        main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.gravityModifier = -0.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        shape = embers.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        em = embers.emission;
        em.rateOverTime = Mathf.Clamp(radius * 5f, 12f, 60f);
        Shrink(embers);
        embers.Play();

        return fire.gameObject;
    }

    // ------------------------------------------------------------ PROJÉTIL
    /// <summary>Núcleo brilhante + rastro para projéteis sem prefab.</summary>
    public static GameObject ProjectileGlow(Transform parent, Color color, float size)
    {
        var core = NewSystem(parent, color);
        var main = core.main;
        main.startLifetime = 0.22f;
        main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.3f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;  // deixa rastro
        var shape = core.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * 0.25f;
        var em = core.emission;
        em.rateOverTime = 70f;
        Shrink(core);
        core.Play();
        return core.gameObject;
    }

    // ----------------------------------------------------------- EXPLOSÃO
    /// <summary>Explosão one-shot (impacto de projétil, Incinerate). Auto-destrói.</summary>
    public static void Burst(Vector3 position, float radius, Color color)
    {
        var root = new GameObject("Explosão");
        root.transform.position = position + Vector3.up * 0.4f;

        var ps = NewSystem(root.transform, color);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.6f, radius * 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.gravityModifier = 0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)Mathf.Clamp(radius * 14f, 30f, 130f))
        });
        Shrink(ps);
        ps.Play();

        Object.Destroy(root, 2f);
    }
}
