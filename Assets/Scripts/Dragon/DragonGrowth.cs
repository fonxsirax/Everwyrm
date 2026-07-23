using System;
using UnityEngine;

/// <summary>
/// Crescimento e condição corporal (GDD: Crescimento Visível + Peso).
///
///  TAMANHO — cresce com o tempo, MUITO mais rápido bem alimentado.
///    Filhote → Adulto → Colossal (escala real do modelo).
///
///  CONDIÇÃO CORPORAL (0=esquelético · 0.5=saudável · 1=gordo) — muda DEVAGAR,
///    acompanhando a média de alimentação ao longo de minutos, não uma refeição.
///    Usa as blend shapes do modelo: "Belly Fat" para engordar e as "* Thin"
///    (barriga, peito, pescoço, pernas, cauda, asas...) para definhar.
///
///  PESO — derivado de tamanho³ x condição. Afeta:
///    aceleração terrestre, velocidade máxima, subida no voo, planeio e custo
///    de energia (dragões maiores gastam mais, mas planam melhor — GDD).
///
/// Publica eventos (observer) para o HUD e outros sistemas.
/// </summary>
[RequireComponent(typeof(DragonVitals))]
public class DragonGrowth : MonoBehaviour
{
    public enum LifeStage { Filhote, Adulto, Colossal }

    [Header("Tamanho")]
    [SerializeField] float hatchlingScale = 0.45f;
    [SerializeField] float colossalScale = 1.7f;
    [SerializeField] float fullGrowthMinutes = 25f;  // filhote→colossal sempre alimentado
    [SerializeField, Range(0f, 1f)] float startGrowth = 0f;
    [SerializeField] float adultAt = 0.4f;
    [SerializeField] float colossalAt = 0.85f;
    [SerializeField] float mealGrowthBonus = 0.0012f; // por ponto de nutrição

    [Header("Condição Corporal — taxas em KG (sutil)")]
    [SerializeField, Range(0f, 1f)] float startCondition = 0.5f;
    [SerializeField] float gainKgPerMinute = 1f;          // satisfeito (fome > 65%), sem comer
    [SerializeField] float lossKgPerMinute = 1f;          // fome abaixo de 50%
    [SerializeField] float starvingLossKgPerMinute = 3f;  // fome abaixo de 20% (~1 kg/20 s)
    [SerializeField] float mealWeightNudgeKg = 2f;        // kg ganhos na hora por refeição

    [Header("Peso")]
    [SerializeField] float adultHealthyWeightKg = 950f;

    DragonVitals vitals;
    SkinnedMeshRenderer[] renderers;
    float growth01;      // 0 filhote → 1 colossal
    float condition;     // 0 magro → 1 gordo
    float lastEmittedGrowth = -1f, lastEmittedCondition = -1f;
    LifeStage stage;

    public LifeStage Stage => stage;
    public float Growth01 => growth01;
    public float Condition01 => condition;
    public float AgeSeconds { get; private set; }
    public float Scale => Mathf.Lerp(hatchlingScale, colossalScale, growth01);
    public float WeightKg => adultHealthyWeightKg * Mathf.Pow(Scale, 3f) * (0.65f + 0.7f * condition);

    // ---- Multiplicadores consumidos pelo DragonController
    /// <summary>Escala geral de velocidades (dragão maior anda/voa mais rápido em absoluto).</summary>
    public float SpeedScale => Scale;
    /// <summary>Segundos andando até atingir corrida plena. Gordo/grande demora mais.</summary>
    public float AccelTime => Mathf.Lerp(2.0f, 5.5f, condition) * Mathf.Lerp(0.85f, 1.25f, growth01);
    /// <summary>Velocidade máxima de corrida. Gordo corre menos.</summary>
    public float RunSpeedMul => Mathf.Lerp(1.08f, 0.82f, condition);
    /// <summary>Subida no voo. Gordo sobe muito pior.</summary>
    public float ClimbMul => Mathf.Lerp(1.15f, 0.6f, condition);
    /// <summary>Afundamento no planeio. Gordo afunda mais; grande plana melhor (GDD).</summary>
    public float SinkMul => Mathf.Lerp(0.85f, 1.6f, condition) * Mathf.Lerp(1.2f, 0.75f, growth01);
    /// <summary>Custo de energia. Gordo e grande gastam mais.</summary>
    public float EnergyCostMul => Mathf.Lerp(0.9f, 1.4f, condition) * Mathf.Lerp(0.85f, 1.25f, growth01);

    // ---- Agilidade por idade (rework hack and slash): o peso modula a
    //      EXPLOSÃO e o giro, nunca cria espera. Filhote = ágil e furtivo;
    //      colossal = ariete que conserva velocidade.
    /// <summary>Velocidade de giro (chão e voo). Filhote vira no lugar.</summary>
    public float TurnAgilityMul => Mathf.Lerp(1.25f, 0.85f, growth01);
    /// <summary>Aceleração terrestre: filhote arranca; gordo empurra mais devagar.</summary>
    public float AccelAgilityMul => Mathf.Lerp(1.3f, 0.9f, growth01) * Mathf.Lerp(1.15f, 0.75f, condition);
    /// <summary>Cooldown do dash (filhote ~0.45 s · adulto 0.6 · colossal 0.75).</summary>
    public float DashCooldownMul => Mathf.Lerp(0.75f, 1.25f, growth01);
    /// <summary>Força do Wing Boost — a batida do colossal é um aríete.</summary>
    public float BoostMul => Mathf.Lerp(0.7f, 1.15f, growth01);

    // ---- Observer
    public event Action<DragonGrowth> OnGrowthChanged;   // tamanho/condição/peso
    public event Action<LifeStage> OnStageChanged;

    void Awake()
    {
        vitals = GetComponent<DragonVitals>();
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        growth01 = startGrowth;
        condition = startCondition;
        stage = StageFor(growth01);
        ApplyScale();
        ApplyBlendShapes();
    }

    void Update()
    {
        if (vitals.IsDead) return;
        float dt = Time.deltaTime;
        AgeSeconds += dt;

        // ---- crescimento: só floresce comendo bem (GDD)
        float fedFactor = vitals.Hunger01 > 0.6f ? 1f
                        : vitals.Hunger01 > 0.25f ? 0.35f
                        : 0.05f;
        // corpo saudável cresce melhor que esquelético ou obeso
        float healthFactor = 1f - Mathf.Abs(condition - 0.5f) * 0.8f;
        growth01 = Mathf.Min(1f, growth01 + fedFactor * healthFactor * dt / (fullGrowthMinutes * 60f));

        // ---- condição corporal em KG por minuto (bem sutil):
        //      satisfeito sem comer = engorda devagar · fome < 50% = emagrece
        //      fome < 20% = emagrece mais rápido (pior caso ~1 kg a cada 20 s)
        float kgPerMin = vitals.Hunger01 > 0.65f ? gainKgPerMinute
                       : vitals.Hunger01 > 0.5f ? 0f
                       : vitals.Hunger01 > 0.2f ? -lossKgPerMinute
                       : -starvingLossKgPerMinute;
        condition = Mathf.Clamp01(condition + kgPerMin / KgPerFullCondition / 60f * dt);

        ApplyScale();

        // ---- emitir eventos apenas em mudança perceptível
        if (Mathf.Abs(growth01 - lastEmittedGrowth) > 0.002f ||
            Mathf.Abs(condition - lastEmittedCondition) > 0.004f)
        {
            lastEmittedGrowth = growth01;
            lastEmittedCondition = condition;
            ApplyBlendShapes();
            OnGrowthChanged?.Invoke(this);
        }

        var newStage = StageFor(growth01);
        if (newStage != stage)
        {
            stage = newStage;
            OnStageChanged?.Invoke(stage);
        }
    }

    /// <summary>Quantos kg equivalem à faixa toda de condição (magro→gordo) no tamanho atual.</summary>
    float KgPerFullCondition => Mathf.Max(1f, adultHealthyWeightKg * Mathf.Pow(Scale, 3f) * 0.7f);

    /// <summary>Chamado ao comer: nutrição acelera crescimento e engorda um pouquinho.</summary>
    public void NotifyAte(float nutrition)
    {
        growth01 = Mathf.Min(1f, growth01 + nutrition * mealGrowthBonus);
        condition = Mathf.Clamp01(condition + mealWeightNudgeKg / KgPerFullCondition);
    }

    LifeStage StageFor(float g) =>
        g >= colossalAt ? LifeStage.Colossal :
        g >= adultAt ? LifeStage.Adulto : LifeStage.Filhote;

    void ApplyScale() => transform.localScale = Vector3.one * Scale;

    /// <summary>"Belly Fat" acima do saudável; shapes "Thin" abaixo dele.</summary>
    void ApplyBlendShapes()
    {
        float fatW = Mathf.Clamp01((condition - 0.5f) * 2f) * 100f;
        float thinW = Mathf.Clamp01((0.5f - condition) * 2f) * 100f;

        foreach (var r in renderers)
        {
            var mesh = r.sharedMesh;
            if (mesh == null) continue;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string n = mesh.GetBlendShapeName(i);
                if (n.Contains("Fat")) r.SetBlendShapeWeight(i, fatW);
                else if (n.Contains("Thin")) r.SetBlendShapeWeight(i, thinW);
            }
        }
    }
}
