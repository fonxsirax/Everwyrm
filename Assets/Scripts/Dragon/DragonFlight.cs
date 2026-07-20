using System;
using UnityEngine;

/// <summary>
/// Voo como HABILIDADE (não elevador): ciclo de batidas de asa com timing.
///
///  - 1 toque de Space = 1 batida. Até `flapsPerCycle` (2) por ciclo.
///  - TIMING: soltar Space perto do FIM da batida rende impulso extra — máximo
///    exatamente na conclusão da animação. Segurar além do fim perde o bônus.
///  - Esgotou? Segurar não faz nada: solte e aguarde `cycleRecovery` para renovar.
///  - Entre batidas o dragão plana: voar RÁPIDO conserva altitude, voar lento
///    afunda (sustentação pela velocidade). Mergulhar troca altitude por speed.
///  - Peso importa: gordo/grande sobe menos por batida e afunda mais.
///  - Correntes de ar (AirflowField, ex.: updrafts de montanha) devolvem altitude.
///
/// Todos os números vêm do FlightProfile (ScriptableObject) — modular para
/// espécies, upgrades de asa e clima no futuro.
/// </summary>
public class DragonFlight : MonoBehaviour
{
    [SerializeField] FlightProfile profile;   // vazio = Resources/FlightProfile

    DragonVitals vitals;
    DragonGrowth growth;
    DragonAttributes attrs;

    float vy;                 // velocidade vertical atual
    int flapsLeft;
    float lastFlapTime = -99f;
    float takeoffClimbUntil = -99f;
    bool releasedSinceFlap = true;
    bool bonusPending;        // batida em andamento aguardando a soltura (timing)

    public int FlapsLeft => flapsLeft;
    public int FlapsPerCycle => profile.flapsPerCycle;
    public bool IsGliding => Time.time - lastFlapTime > profile.glideAnimDelay;
    public float VerticalSpeed => vy;
    public Vector3 CurrentWind { get; private set; }

    /// <summary>Observer: (restantes, total) — HUD desenha os "pips" de asa.</summary>
    public event Action<int, int> OnFlapsChanged;
    public event Action OnFlapped;
    /// <summary>Observer: qualidade (0..1) do bônus de soltura — feedback no HUD.</summary>
    public event Action<float> OnFlapBonus;

    float ClimbMul => growth != null ? growth.ClimbMul : 1f;
    float SinkMul => growth != null ? growth.SinkMul : 1f;
    float CostMul => growth != null ? growth.EnergyCostMul : 1f;
    float TakeoffMul => attrs != null ? attrs.TakeoffClimbMul : 1f;

    /// <summary>Duração efetiva da fase de subida de decolagem (Resistência estica).</summary>
    public float TakeoffClimbTime => profile.takeoffClimbTime * TakeoffMul;

    void Awake()
    {
        vitals = GetComponent<DragonVitals>();
        growth = GetComponent<DragonGrowth>();
        attrs = GetComponent<DragonAttributes>();
        if (profile == null) profile = Resources.Load<FlightProfile>("FlightProfile");
        if (profile == null) profile = ScriptableObject.CreateInstance<FlightProfile>();
    }

    /// <summary>Chamado na decolagem: ciclo cheio + impulso inicial de subida.</summary>
    public void OnEnterFlight(float initialClimb)
    {
        vy = initialClimb;
        flapsLeft = profile.flapsPerCycle;
        lastFlapTime = Time.time;
        takeoffClimbUntil = Time.time + TakeoffClimbTime;   // Resistência estica a fase
        releasedSinceFlap = true;
        bonusPending = false;
        OnFlapsChanged?.Invoke(flapsLeft, profile.flapsPerCycle);
    }

    /// <summary>Fase híbrida: logo após decolar, segurar Space sobe contínuo.</summary>
    public bool InTakeoffClimb => Time.time < takeoffClimbUntil;

    /// <summary>Tranco vertical externo (colisões em voo): soma direto no vy.</summary>
    public void Knock(float deltaVy) => vy += deltaVy;

    /// <summary>
    /// Um passo da física de voo. Retorna a velocidade vertical.
    /// `flapPressed` = KeyDown deste frame; `held` = Space segurado.
    /// `landingSink` > 0 = pouso controlado: desce pelo menos nessa taxa (m/s).
    /// </summary>
    public float Tick(float dt, bool flapPressed, bool held, bool diving,
                      float speed01, float sizeScale, Vector3 worldPos, ref float forwardSpeed,
                      float landingSink = 0f)
    {
        var p = profile;
        CurrentWind = AirflowField.Sample(worldPos);

        // ---- FASE DE DECOLAGEM (híbrido): segurar Space = subida contínua.
        //      Soltar (ou o tempo acabar) entrega o voo ao ciclo de batidas.
        if (Time.time < takeoffClimbUntil)
        {
            if (held && (vitals == null || !vitals.IsExhausted))
            {
                vitals?.Drain(p.takeoffClimbEnergyPerSec * CostMul);
                lastFlapTime = Time.time;   // sem anim de glide, ciclo renova depois
                vy = Mathf.MoveTowards(vy, p.takeoffClimbRate * ClimbMul * sizeScale,
                                       p.verticalResponse * 2f * dt);
                return vy;
            }
            takeoffClimbUntil = Time.time;  // soltou: encerra a fase de decolagem
        }

        // ---- bônus de timing: soltar Space perto do FIM da batida = impulso
        //      extra, crescendo até o máximo na conclusão exata da animação.
        //      Segurar além do fim desperdiça (t > duração ⇒ sem bônus).
        if (!held)
        {
            if (bonusPending)
            {
                float t = Time.time - lastFlapTime;
                if (t <= p.flapAnimDuration)
                {
                    float q = Mathf.Pow(Mathf.Clamp01(t / p.flapAnimDuration),
                                        p.flapBonusPower);
                    vy = Mathf.Min(vy + p.flapBonusLift * q * ClimbMul * sizeScale,
                                   Mathf.Max(vy, p.maxRiseSpeed * sizeScale));
                    forwardSpeed += p.flapBonusForward * q * sizeScale;
                    OnFlapBonus?.Invoke(q);
                }
                bonusPending = false;
            }
            releasedSinceFlap = true;
        }

        // ---- renovação do ciclo: exige SOLTAR o botão + tempo de recuperação
        if (flapsLeft < p.flapsPerCycle && releasedSinceFlap &&
            Time.time - lastFlapTime >= p.cycleRecovery)
        {
            flapsLeft = p.flapsPerCycle;
            OnFlapsChanged?.Invoke(flapsLeft, p.flapsPerCycle);
        }

        // ---- batida: 1 toque = 1 batida (respeitando o intervalo mínimo)
        if (flapPressed && flapsLeft > 0 &&
            Time.time - lastFlapTime >= p.flapMinInterval &&
            (vitals == null || vitals.TrySpend(p.energyPerFlap * CostMul)))
        {
            // teto de subida — mas nunca REDUZ um vy já alto (ex.: decolagem)
            vy = Mathf.Min(vy + p.flapLift * ClimbMul * sizeScale,
                           Mathf.Max(vy, p.maxRiseSpeed * sizeScale));
            forwardSpeed += p.flapForwardBoost * sizeScale;
            flapsLeft--;
            lastFlapTime = Time.time;
            releasedSinceFlap = false;
            bonusPending = true;      // a soltura desta batida pode render bônus
            OnFlapsChanged?.Invoke(flapsLeft, p.flapsPerCycle);
            OnFlapped?.Invoke();
        }

        // ---- planeio: sustentação vem da VELOCIDADE
        float sink;
        if (diving) sink = p.diveSink * sizeScale;
        else
        {
            float slowness = Mathf.Pow(1f - Mathf.Clamp01(speed01), p.slownessPower);
            sink = Mathf.Lerp(p.sinkAtSpeed, p.sinkAtStall, slowness) * SinkMul;
        }

        float targetVy = -sink + CurrentWind.y * p.windInfluence;

        // pouso controlado (S): garante descida mínima rumo ao solo, com
        // resposta TRIPLICADA — o mergulho de pouso engata rápido e decidido.
        // Nem updraft segura um dragão decidido a pousar.
        float response = p.verticalResponse;
        if (landingSink > 0f)
        {
            targetVy = Mathf.Min(targetVy, -landingSink);
            response *= 3f;
        }

        vy = Mathf.MoveTowards(vy, targetVy, response * dt);
        return vy;
    }
}
