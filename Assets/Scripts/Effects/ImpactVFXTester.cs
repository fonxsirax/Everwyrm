using UnityEngine;

/// <summary>
/// Bancada de teste dos VFX de impacto — dispara cada efeito repetidamente num
/// palco à frente do dragão (ou na própria posição, sem dragão). Controlado
/// pelos menus Tools > Everwyrm > VFX de Impacto > Testar (em Play).
/// Só existe para a cena de debug; não entra no jogo.
/// </summary>
public class ImpactVFXTester : MonoBehaviour
{
    public enum TestMode
    {
        Nada,
        Poeira,        // pouso normal (tinta do bioma local)
        QuedaForte,    // poeira densa + torrões
        Folhas,        // colisão com árvore
        Lascas,        // colisão com rocha
        Splash,        // entrada na água
        Spray,         // contínuo: barriga raspando a lâmina
        Tudo,          // cicla todos os anteriores
    }

    [Tooltip("Efeito em teste (os menus do Tools trocam isto)")]
    public TestMode mode = TestMode.Nada;
    [Tooltip("Intervalo entre disparos dos efeitos pontuais (s)")]
    public float interval = 1.1f;
    [Tooltip("Distância do palco à frente do dragão (m)")]
    public float stageDistance = 9f;

    float nextShot;
    int cycle;
    DragonController dragon;

    /// <summary>Chamado pelos menus do editor: acha/cria o testador e arma o modo.</summary>
    public static void Set(TestMode mode)
    {
        var t = FindFirstObjectByType<ImpactVFXTester>();
        if (t == null) t = new GameObject("VFX Tester").AddComponent<ImpactVFXTester>();
        t.mode = mode;
        t.nextShot = 0f;     // dispara já
        Debug.Log($"VFX Tester: {mode}");
    }

    void Update()
    {
        if (mode == TestMode.Nada) return;
        if (dragon == null) dragon = FindFirstObjectByType<DragonController>();

        // contínuo: spray precisa de chamada TODO frame (apaga sozinho ao parar)
        var current = mode == TestMode.Tudo ? (TestMode)(1 + cycle % 6) : mode;
        if (current == TestMode.Spray) EmitSpray();

        if (Time.time < nextShot) return;
        nextShot = Time.time + interval;

        Vector3 p = StagePoint();
        float strength = Random.Range(0.35f, 1f);
        float scale = dragon != null ? Mathf.Max(0.2f, dragon.transform.lossyScale.y) : 1f;

        switch (current)
        {
            case TestMode.Poeira:
                ImpactVFX.Dust(p, strength, scale, ImpactVFX.GroundTintAt(p));
                break;

            case TestMode.QuedaForte:
                ImpactEffects.Emit(new ImpactEvent
                {
                    kind = ImpactKind.HardLanding,
                    position = p, normal = Vector3.up,
                    velocity = Vector3.down * 12f,
                    strength01 = strength, scale = scale,
                });
                break;

            case TestMode.Folhas:
                // normal e velocidade como numa batida real: dragão vindo da câmera
                ImpactVFX.Foliage(p + Vector3.up * 2.5f * scale, -StageForward(),
                                  StageForward() * 14f, strength, scale);
                break;

            case TestMode.Lascas:
                ImpactVFX.RockChips(p + Vector3.up * 1.5f * scale, -StageForward(),
                                    strength, scale);
                break;

            case TestMode.Splash:
                ImpactVFX.Splash(p, strength, scale);
                break;
        }

        if (mode == TestMode.Tudo) cycle++;
    }

    /// <summary>Spray contínuo deslizando de um lado ao outro do palco.</summary>
    void EmitSpray()
    {
        float slide = Mathf.PingPong(Time.time * 4f, 8f) - 4f;
        Vector3 p = StagePoint() + StageRight() * slide;
        float scale = dragon != null ? Mathf.Max(0.2f, dragon.transform.lossyScale.y) : 1f;
        ImpactEffects.Skim(new ImpactEvent
        {
            kind = ImpactKind.WaterSkim,
            position = p, normal = Vector3.up,
            velocity = StageRight() * 8f,
            strength01 = 0.8f, scale = scale,
        });
    }

    Vector3 StagePoint()
    {
        Vector3 basePos = dragon != null
            ? dragon.transform.position + StageForward() * stageDistance
            : transform.position;
        // palco cravado no chão do mundo (os efeitos nascem da superfície)
        if (InfiniteTerrain.Instance != null)
            basePos.y = InfiniteTerrain.Instance.HeightAt(basePos.x, basePos.z) + 0.2f;
        return basePos;
    }

    Vector3 StageForward()
    {
        if (dragon == null) return Vector3.forward;
        Vector3 f = dragon.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
    }

    Vector3 StageRight() => Vector3.Cross(Vector3.up, StageForward());
}
