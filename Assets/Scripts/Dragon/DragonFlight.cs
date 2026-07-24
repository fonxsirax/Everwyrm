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
///  - A SUBIDA por batida é generosa e quem a modula é o PESO ATUAL, não a fase
///    da vida (DragonGrowth.FlapLiftMul): magro sobe muito, gordo mal decola.
///    Filhote e colossal ganham praticamente os mesmos metros por batida.
///  - Correntes de ar (AirflowField, ex.: updrafts de montanha) devolvem altitude.
///  - TETO DE VOO (Resistência): perto do teto o ar rarefeito rende cada vez
///    menos sustentação; acima dele nada segura o dragão. Sem parede invisível.
///
/// Todos os números vêm do FlightProfile (ScriptableObject) — modular para
/// espécies, upgrades de asa e clima no futuro.
/// </summary>
public class DragonFlight : MonoBehaviour
{
    [SerializeField] FlightProfile profile;   // vazio = Resources/Balance/FlightProfile

    DragonVitals vitals;
    DragonGrowth growth;
    DragonAttributes attrs;
    DragonTraits traitsCache;
    /// <summary>Getter preguiçoso — o DragonTraits nasce no Awake do Controller.</summary>
    DragonTraits Traits => traitsCache != null ? traitsCache : (traitsCache = GetComponent<DragonTraits>());

    float vy;                 // velocidade vertical atual
    int flapsLeft;
    float lastFlapTime = -99f;
    float takeoffClimbUntil = -99f;
    bool releasedSinceFlap = true;
    bool bonusPending;        // batida em andamento aguardando a soltura (timing)
    bool wasDiving;           // p/ detectar a SAÍDA do mergulho (swoop)
    float swoopUntil = -99f;  // janela pós-mergulho com resposta dobrada
    float scriptedClimbUntil = -99f;   // salto parado: sobe sem depender do botão

    public int FlapsLeft => flapsLeft;
    public int FlapsPerCycle => profile.flapsPerCycle;
    public bool IsGliding => Time.time - lastFlapTime > profile.glideAnimDelay;
    public float VerticalSpeed => vy;
    public Vector3 CurrentWind { get; private set; }
    /// <summary>0 = ar pleno · 1 = no teto (sem sustentação). HUD pode avisar.</summary>
    public float ThinAir01 { get; private set; }
    /// <summary>Teto de voo atual (m, altura do mundo) — vem do Fôlego; Guelras o
    /// abaixam (o dragão aquático troca ar por água).</summary>
    public float Ceiling => attrs != null
        ? attrs.MaxAltitude * (Traits != null ? Traits.CeilingMul : 1f)
        : float.PositiveInfinity;
    /// <summary>Mergulhando a velocidade fura o máximo (× maxFlySpeed).</summary>
    public float DiveOverspeed => profile.diveOverspeed;

    /// <summary>Observer: (restantes, total) — HUD desenha os "pips" de asa.</summary>
    public event Action<int, int> OnFlapsChanged;
    public event Action OnFlapped;
    /// <summary>Observer: qualidade (0..1) do bônus de soltura — feedback no HUD.</summary>
    public event Action<float> OnFlapBonus;

    /// <summary>Sustentação da batida: vem do PESO atual (ver DragonGrowth.FlapLiftMul).
    /// É de propósito que ela NÃO multiplica o tamanho do corpo — a altura ganhada
    /// por batida quase não muda ao longo da vida, só com o peso.</summary>
    float LiftMul => growth != null ? growth.FlapLiftMul : 1f;
    /// <summary>Afundamento no planeio: peso (growth) × Ossos Ocos (plana melhor).</summary>
    float SinkMul => (growth != null ? growth.SinkMul : 1f)
                   * (Traits != null ? Traits.GlideSinkMul : 1f);
    /// <summary>Custo de energia: peso (growth) × EFICIÊNCIA do Fôlego (attrs).</summary>
    float CostMul => (growth != null ? growth.EnergyCostMul : 1f)
                   * (attrs != null ? attrs.EnergyCostMul : 1f);
    float TakeoffMul => attrs != null ? attrs.TakeoffClimbMul : 1f;
    /// <summary>Aproveitamento das correntes de ar: Instinto (attrs) × Termonauta (traço).</summary>
    float WindReadMul => (attrs != null ? attrs.WindReadMul : 1f)
                       * (Traits != null ? Traits.WindReadMul : 1f);

    /// <summary>Duração efetiva da fase de subida de decolagem (Resistência estica).</summary>
    public float TakeoffClimbTime => profile.takeoffClimbTime * TakeoffMul;

    /// <summary>Subida (m/s) que UMA batida acrescenta AGORA — o número que o peso
    /// atual manda. Ficha e janela de balanceamento mostram este valor.</summary>
    public float FlapLift => profile.flapLift * LiftMul;
    /// <summary>Velocidade de subida máxima acumulável no peso atual (m/s).</summary>
    public float MaxRiseSpeed => profile.maxRiseSpeed * LiftMul;

    void Awake()
    {
        vitals = GetComponent<DragonVitals>();
        growth = GetComponent<DragonGrowth>();
        attrs = GetComponent<DragonAttributes>();
        if (profile == null) profile = Balance.Default<FlightProfile>();
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

    /// <summary>Salto de decolagem parado: enquanto a animação roda, a subida é
    /// do DRAGÃO, não do botão (o jogador está travado no clipe). `maxSeconds` é
    /// só uma rédea de segurança — quem encerra de fato é o Launch.</summary>
    public void BeginScriptedClimb(float maxSeconds) =>
        scriptedClimbUntil = Time.time + maxSeconds;

    /// <summary>BOTE das asas no fim do salto: joga o dragão pra cima de verdade.
    /// É o clímax do salto — antes o impulso vinha todo no PRIMEIRO frame e
    /// chegava gasto no voo, e a decolagem parecia perder força justo aqui.
    /// Nunca reduz um vy já maior, e devolve o ciclo de batidas cheio.</summary>
    public void Launch(float climbSpeed)
    {
        vy = Mathf.Max(vy, climbSpeed);
        scriptedClimbUntil = Time.time;      // o roteiro acabou: o voo é do jogador
        flapsLeft = profile.flapsPerCycle;
        lastFlapTime = Time.time;
        releasedSinceFlap = true;
        bonusPending = false;
        OnFlapsChanged?.Invoke(flapsLeft, profile.flapsPerCycle);
        OnFlapped?.Invoke();
    }

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

        // ---- AR RAREFEITO: a sustentação esvai na faixa abaixo do teto de voo
        //      (Resistência) e zera nele — batidas, decolagem e updrafts rendem
        //      cada vez menos até nada. Acima do teto só resta afundar.
        float ceiling = Ceiling;
        ThinAir01 = float.IsPositiveInfinity(ceiling) ? 0f
                  : Mathf.Clamp01(1f - (ceiling - worldPos.y) / Mathf.Max(1f, p.ceilingSoftBand));
        float airLift = 1f - ThinAir01;

        // ---- FASE DE DECOLAGEM (híbrido): segurar Space = subida contínua.
        //      Soltar (ou o tempo acabar) entrega o voo ao ciclo de batidas.
        //      No SALTO PARADO a subida é roteirizada: o jogador está preso na
        //      animação, então o dragão sobe sozinho — soltar não o derruba no
        //      meio do próprio bote (Launch fecha essa janela no fim do clipe).
        if (Time.time < takeoffClimbUntil)
        {
            bool scripted = Time.time < scriptedClimbUntil;
            if ((held || scripted) && (vitals == null || !vitals.IsExhausted))
            {
                if (!scripted) vitals?.Drain(p.takeoffClimbEnergyPerSec * CostMul);
                lastFlapTime = Time.time;   // sem anim de glide, ciclo renova depois
                vy = Mathf.MoveTowards(vy, p.takeoffClimbRate * LiftMul * airLift,
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
                    vy = Mathf.Min(vy + p.flapBonusLift * q * LiftMul * airLift,
                                   Mathf.Max(vy, p.maxRiseSpeed * LiftMul));
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
            vy = Mathf.Min(vy + p.flapLift * LiftMul * airLift,
                           Mathf.Max(vy, p.maxRiseSpeed * LiftMul));
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
            // SWOOP: sair do mergulho converte a queda em velocidade à frente e
            // arremata com resposta dobrada — o loop de energia mergulho→rasante
            if (wasDiving && vy < -4f)
            {
                forwardSpeed += -vy * p.swoopConversion;
                swoopUntil = Time.time + 0.5f;
            }
            float slowness = Mathf.Pow(1f - Mathf.Clamp01(speed01), p.slownessPower);
            sink = Mathf.Lerp(p.sinkAtSpeed, p.sinkAtStall, slowness) * SinkMul;
        }
        wasDiving = diving;

        // updraft também perde força no ar rarefeito: nem térmica fura o teto
        float targetVy = -sink + CurrentWind.y * p.windInfluence * WindReadMul * airLift;

        // pouso controlado (S): garante descida mínima rumo ao solo, com
        // resposta TRIPLICADA — o mergulho de pouso engata rápido e decidido.
        // Nem updraft segura um dragão decidido a pousar.
        float response = p.verticalResponse;
        if (Time.time < swoopUntil) response *= 2f;   // recuperação RÁPIDA pós-mergulho
        if (landingSink > 0f)
        {
            targetVy = Mathf.Min(targetVy, -landingSink);
            response *= 3f;
        }

        vy = Mathf.MoveTowards(vy, targetVy, response * dt);
        return vy;
    }
}
