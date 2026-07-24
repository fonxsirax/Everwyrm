using System;
using UnityEngine;

/// <summary>
/// Controlador do dragão (Unka) — chão, decolagem, voo, pouso, combate e sobrevivência.
/// Rework hack and slash: resposta INSTANTÂNEA ao input; o peso vira derrapada,
/// câmera e VFX — nunca atraso. Buffer de comandos + cancel de golpes por dash/voo.
///
/// CONTROLES (ver DragonInput — bindings centralizados)
///  Chão : WASD move RELATIVO À CÂMERA (o corpo gira sozinho — fechado devagar,
///         arco em corrida) · o dragão SEMPRE CORRE
///         Ctrl SEGURADO = Stealth: passo de caçada, fauna quase não percebe
///         Space no chão DECOLA sempre — parado inclusive: o dragão dá o salto
///         (UJump Up) e as asas o arrancam do chão no fim do clipe. Em corrida
///         plena nem toca essa animação: abre as asas e já está voando. O salto
///         é uma animação FECHADA — travada e estável, não gira nem é cancelada
///         Shift + A/D = DASH lateral com i-frames — cancela golpes após ~40% do
///         swing (as duas teclas precisam ser um toque FRESCO e próximo: segurar
///         a direcional de antes não conta, solte e aperte as duas de novo)
///         LMB combo (mordida/garras) · Q cauda · E asas · RMB/F fogo · T rugido
///         R descansar · G comer carcaça próxima
///  Voo  : W acelera · S freia (SEGURE p/ pousar quando houver chão) · A/D vira
///         (curva fechada devagar, ampla em alta velocidade)
///         Space sobe MUITO por batida (quem limita é o PESO atual, não a idade) —
///         soltar perto do fim da batida dá impulso extra (timing!)
///         Shift toque = WING BOOST (batida forte: aceleração instantânea, i-frames)
///         Shift + A/D = esquiva simples: desvia o rumo, sem animação dedicada
///         A/D + Shift + SPACE = ESQUIVA COMPLETA: toca o Fly Dodge L/R exato e
///         VIRA O VOO para o novo rumo (não é empurrão lateral — muda de direção)
///         Shift SEGURADO = mergulho (passa da vel. máx.; sair converte a queda
///         em velocidade — swoop)
///         Sem bater asas ~1s = planar · Energia zerada = estol e queda!
///         Colisão tem consequência FÍSICA (sem dano), e bater FORTE derruba o
///         dragão num tombo (Fly Fall Death) · DANO só de QUEDA alta.
///  Água : encostar na lâmina vira nado · W/S nada · A/D vira · Space decola.
///  Morto: Enter renasce.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class DragonController : MonoBehaviour
{
    [Header("Chão")]
    [SerializeField] float walkSpeed = 3.84f;   // passo de caçada (Stealth) — era o "andar"
    [SerializeField] float runSpeed = 11.4f;
    [SerializeField] float reverseSpeed = 1.7f; // ré (só no nado — no chão o corpo vira)
    [SerializeField] float groundAccel = 35f;   // 90% da corrida em ~0.35 s (hack and slash)
    [SerializeField] float groundDecel = 25f;   // soltar a direção: derrapada curta (peso)
    [SerializeField] float turnRateIdle = 540f; // giro parado/lento (°/s)
    [SerializeField] float turnRateRun = 240f;  // giro em corrida plena (°/s) — arco
    [SerializeField] float gravity = 28f;

    [Header("Decolagem")]
    [Tooltip("Acima desta fração da corrida, decola DIRETO pro voo (sem anim de salto)")]
    [SerializeField] float runningTakeoffFraction = 0.5f;
    [Tooltip("Trava mínima (s) no 1º frame do salto parado, até o Animator confirmar " +
             "que entrou no estado TakeOff (depois disso a trava segue o clipe real)")]
    [SerializeField] float takeoffLockMinimum = 0.15f;
    [Tooltip("Subida durante o salto (m/s) — o dragão sobe sozinho enquanto o clipe roda")]
    [SerializeField] float takeoffHopClimb = 6f;
    [Tooltip("BOTE no fim do salto: velocidade de subida imposta (m/s) — é aqui " +
             "que a decolagem parada ganha altura de verdade")]
    [SerializeField] float takeoffLaunchClimb = 12f;
    [Tooltip("Empurrão pra frente no bote (m/s) — sai do salto já com sustentação")]
    [SerializeField] float takeoffLaunchForward = 3f;
    [Tooltip("Rédea de segurança: se o clipe do salto não terminar até aqui, solta (s)")]
    [SerializeField] float takeoffMaxLock = 2.5f;
    [Tooltip("Carência pós-decolagem p/ auto-pouso e colisão (s)")]
    [SerializeField] float takeoffGrace = 0.8f;

    [Header("Dash (Shift + A/D no chão)")]
    [SerializeField] float dashSpeedMul = 1.7f;    // × velocidade de corrida
    [SerializeField] float dashDuration = 0.35f;
    [SerializeField] float dashCooldown = 0.6f;
    [SerializeField] float dashIFrames = 0.3f;

    [Header("Wing Boost (Shift no ar)")]
    [SerializeField] float boostImpulse = 8f;        // m/s à frente (× escala do corpo)
    [SerializeField] float boostMaxOverspeed = 1.25f;// × vel. máx. de voo
    [SerializeField] float boostCooldown = 1f;
    [SerializeField] float boostIFrames = 0.25f;
    [SerializeField] float boostCost = 6f;
    [Tooltip("Esquiva simples (Shift + A/D no ar): desvio do rumo, sem animação " +
             "dedicada — o juke que escapa da linha do ataque (graus)")]
    [SerializeField] float airDodgeYaw = 18.2f;

    [Header("Esquiva de voo COMPLETA (A/D + Shift + Space)")]
    [Tooltip("Quanto o voo VIRA — não é empurrão lateral, é mudar de rumo (graus)")]
    [SerializeField] float flyDodgeTurn = 90f;
    [Tooltip("Tempo da virada — a animação Fly Dodge L/R toca inteira nela (s)")]
    [SerializeField] float flyDodgeDuration = 0.45f;
    [SerializeField] float flyDodgeIFrames = 0.4f;
    [SerializeField] float flyDodgeCost = 8f;

    [Header("Cancel de golpes")]
    [Tooltip("Fração do golpe após a qual dash/decolagem podem cancelá-lo")]
    [SerializeField, Range(0f, 1f)] float attackCancelWindow = 0.4f;

    [Header("Voo")]
    [SerializeField] float minFlySpeed = 6f;
    [SerializeField] float cruiseSpeed = 14f;
    [SerializeField] float maxFlySpeed = 26f;
    [SerializeField] float flyAccel = 22f;      // engata rápido; perder velocidade é lento
    [SerializeField] float turnSpeedAir = 95f;
    [SerializeField] float bankAngle = 48f;
    [SerializeField] float pitchAngle = 28f;
    [SerializeField] float climbRate = 7.5f;
    [SerializeField] float diveRate = 14f;
    [SerializeField] float glideSink = 1.6f;
    [SerializeField] float glideDelay = 1.1f;
    [SerializeField] float stallSink = 9f;

    [Header("Pouso")]
    [SerializeField] float landProbeDistance = 3.5f;
    [SerializeField] float landMaxSpeed = 11f;
    [SerializeField] float landApproachProbe = 16f;  // segurando S: busca chão até aqui
    [SerializeField] float landDescendRate = 16f;    // descida na aproximação (longe do chão)
    [SerializeField] float landFlareRate = 3f;       // descida perto do chão ("flare" suave)
    [Tooltip("Voando rente ao chão SEM intenção de subir: pousa em vez de raspar (m)")]
    [SerializeField] float autoLandHeight = 1.6f;
    [Tooltip("Inclinação máxima que aceita pouso automático (1 = plano, 0.7 ≈ 45°)")]
    [SerializeField] float autoLandMaxSlope = 0.7f;
    [SerializeField] LayerMask groundMask = 0;

    [Header("Queda (dano só de altura REAL)")]
    [Tooltip("Altura segura = esta fração da MAIOR árvore da floresta — a " +
             "vegetação define o limite, então trocar as árvores recalibra sozinho")]
    [SerializeField] float safeFallTreeFraction = 0.7f;
    [Tooltip("Altura segura (m) se o mundo procedural não estiver disponível")]
    [SerializeField] float safeFallFallback = 18f;
    [Tooltip("Dano ao despencar do DOBRO da altura segura (cresce linear a partir dela)")]
    [SerializeField] float fallDamageAtDouble = 35f;

    [Header("Água (visual — não interrompe o voo)")]
    [Tooltip("Barriga a esta distância da lâmina: spray/ondulações acompanhando o voo (m)")]
    [SerializeField] float waterSkimHeight = 1.8f;

    [Header("Colisão em voo")]
    [SerializeField] float impactLight = 0.2f;       // fração da vel. máx.: abaixo é raspão
    [SerializeField] float impactHeavy = 0.5f;       // fração da vel. máx.: desequilíbrio
    [SerializeField] float impactSpeedLoss = 0.6f;   // perda de velocidade × impacto
    [SerializeField] float impactKnockDown = 6f;     // tranco p/ baixo no impacto máx. (m/s)
    [SerializeField] float staggerTime = 1.2f;       // duração do desequilíbrio (s)
    [SerializeField] float impactCooldown = 0.4f;    // intervalo mínimo entre reações
    [SerializeField] float impactEnergyCost = 6f;    // energia da colisão leve × impacto

    [Header("Natação")]
    [SerializeField] float swimSpeed = 3.4f;        // nado pra frente
    [SerializeField] float swimBackSpeed = 1.2f;
    [SerializeField] float swimTurnSpeed = 85f;
    [SerializeField] float swimAccel = 4.5f;
    [SerializeField] float buoyDepth = 0.85f;       // quanto do corpo fica submerso (m × escala)
    [SerializeField] float minSwimDepth = 1.1f;     // profundidade mínima p/ boiar (senão anda)
    [SerializeField] float swimCost = 2f;           // energia/s nadando

    [Header("Alimentação")]
    [SerializeField] float eatRange = 4.5f;

    [Header("Custos de Energia")]
    [SerializeField] float runCost = 3.5f;
    [SerializeField] float flapCost = 3.5f;
    [SerializeField] float climbCost = 7f;
    [SerializeField] float glideCost = 0.3f;
    [SerializeField] float takeoffCost = 6f;
    [SerializeField] float dodgeCost = 8f;
    [SerializeField] float fireCost = 15f;
    [SerializeField] float attackCost = 3f;

    // ---- Parâmetros do Animator
    static readonly int P_Speed      = Animator.StringToHash("Speed");
    static readonly int P_Turn       = Animator.StringToHash("Turn");
    static readonly int P_Vertical   = Animator.StringToHash("Vertical");
    static readonly int P_Flying     = Animator.StringToHash("Flying");
    static readonly int P_Glide      = Animator.StringToHash("Glide");
    // "Flap" (estado Flap Ground) era o bate-asas parado do antigo hop — o Space
    // parado agora decola de verdade, então o trigger não é mais disparado.
    static readonly int P_Rest       = Animator.StringToHash("Rest");
    static readonly int P_Roar       = Animator.StringToHash("Roar");
    static readonly int P_Attack     = Animator.StringToHash("Attack");
    static readonly int P_AttackType = Animator.StringToHash("AttackType");
    static readonly int P_Fire       = Animator.StringToHash("Fire");
    static readonly int P_Dodge      = Animator.StringToHash("Dodge");
    static readonly int P_DodgeDir   = Animator.StringToHash("DodgeDir");
    static readonly int P_Hit        = Animator.StringToHash("Hit");
    static readonly int P_HitVar     = Animator.StringToHash("HitVar");
    static readonly int P_Falling    = Animator.StringToHash("Falling");
    static readonly int P_Die        = Animator.StringToHash("Die");
    static readonly int P_DeathVar   = Animator.StringToHash("DeathVar");
    static readonly int P_IdleVar    = Animator.StringToHash("IdleVar");
    static readonly int P_Swim       = Animator.StringToHash("Swimming");
    static readonly int P_Stealth    = Animator.StringToHash("Stealth");

    // Estados alcançados por CrossFade direto (precisão que o trigger não dá)
    const string FallDeathState = "Fly Fall Death";   // tombo da colisão em voo
    const string TakeOffState = "TakeOff";            // salto da decolagem parada
    const string FlyDodgeLState = "Fly Dodge L";
    const string FlyDodgeRState = "Fly Dodge R";

    /// <summary>O que travou as ações: golpes podem ser CANCELADOS por dash/voo
    /// (após a janela); desequilíbrio/queda (Stagger), nunca.</summary>
    enum LockKind { Action, Attack, Stagger }

    CharacterController cc;
    Animator anim;
    Transform camT;                      // câmera p/ movimento relativo (auto)
    DragonVitals vitals;                 // opcional
    DragonGrowth growth;                 // opcional
    DragonAttributes attrs;              // opcional
    DragonFlight flight;                 // opcional (voo skill-based)
    DragonTraits traits;                 // opcional (traços herdáveis)

    bool flying, gliding, stalling, resting, dead, swimming, stealth, diving;
    float planarSpeed, flySpeed, verticalVel;
    float yaw, pitch, roll, prevYaw;
    float turnSmoothed, vertInput;
    float takeoffTime = -99f;
    bool inStationaryTakeoff;             // salto parado: trava estável até o Animator sair dele
    float actionLockUntil, lockStart;
    LockKind lockKind = LockKind.Action;
    float dashUntil = -99f, dashReadyAt = -99f, boostReadyAt = -99f;
    Vector3 dashVel;
    float flyDodgeUntil = -99f, flyDodgeYawTarget;
    float lastAirDodgeTime = -99f, lastAirDodgeYaw;  // p/ o combo absorver a esquiva simples
    Vector3 flightVel, pendingNormal, pendingPoint;  // colisão em voo
    Collider pendingCollider;
    float pendingImpact, lastImpactTime = -99f, staggerUntil = -99f;
    float fallFromY = float.NaN;                     // Y do início da queda DESCONTROLADA
    float flinchTime = -99f, flinchDir = 1f;         // "acusar o golpe" em voo
    const float FlinchDuration = 0.45f;
    const float FlinchAngle = 9f;                    // graus de rolagem no pico
    int meleeCombo;
    bool tailLeft, wingLeft;
    bool hasStealthParam, hasFallDeathState;
    float nextIdleChange, nextHintCheck;
    string currentHint = "";

    public bool IsFlying => flying;
    public bool ActionsLocked => Locked;                   // p/ DragonAbilities
    public bool IsStaggered => Time.time < staggerUntil;   // desequilíbrio pós-colisão
    public bool IsResting => resting;
    public bool IsDead => dead;
    public bool IsSwimming => swimming;
    /// <summary>Shift segurado no chão: passo de caçada — a fauna mal percebe.</summary>
    public bool IsStealth => stealth;
    /// <summary>Ctrl segurado em voo: mergulho (câmera acompanha).</summary>
    public bool IsDiving => flying && diving;
    public bool IsDashing => Time.time < dashUntil;
    public float MaxGroundSpeed => EffRunSpeed;          // p/ menu de atributos
    public float MaxFlightSpeed => maxFlySpeed * S;
    // Bases CRUAS (antes de qualquer multiplicador) — a janela de balanceamento
    // (Tools > Everwyrm > Balanço do Dragão) lê daqui para simular sem entrar em Play.
    public float BaseRunSpeed => runSpeed;
    public float BaseMaxFlySpeed => maxFlySpeed;
    public float TimeToRun => EffRunSpeed / Mathf.Max(0.01f, EffGroundAccel);
    public float Speed01 => flying ? Mathf.InverseLerp(0f, maxFlySpeed * S, flySpeed)
                                   : Mathf.InverseLerp(0f, EffRunSpeed, Mathf.Abs(planarSpeed));

    /// <summary>Observer: dica contextual para o HUD ("G — comer" etc.).</summary>
    public event Action<string> OnHintChanged;

    bool Locked => Time.time < actionLockUntil;
    bool Exhausted => vitals != null && vitals.IsExhausted;

    /// <summary>Golpe em andamento já pode ser cancelado por dash/decolagem?</summary>
    bool CanCancelAttack => Locked && lockKind == LockKind.Attack &&
        Time.time - lockStart >= (actionLockUntil - lockStart) * attackCancelWindow;

    // ---- Peso/tamanho (DragonGrowth) + atributos — neutros se não existirem
    float AttrSpeed => attrs != null ? attrs.SpeedMul : 1f;   // Agilidade
    float AttrAccel => attrs != null ? attrs.AccelMul : 1f;   // Agilidade + Vigor
    float S => (growth != null ? growth.SpeedScale : 1f) * AttrSpeed;
    /// <summary>Custo de energia: PESO (crescimento/condição) × EFICIÊNCIA do Fôlego ×
    /// bioma do Sangue Frio. O dragão de Fôlego voa a tarde inteira; no calor o
    /// Sangue Frio custa mais caro.</summary>
    float CostMul => (growth != null ? growth.EnergyCostMul : 1f)
                   * (attrs != null ? attrs.EnergyCostMul : 1f)
                   * (traits != null ? traits.EnergyCostBiomeMul(transform.position) : 1f);
    float RunMul => growth != null ? growth.RunSpeedMul : 1f;
    float ClimbMul => growth != null ? growth.ClimbMul : 1f;
    /// <summary>Sustentação de asa pelo PESO atual — a mesma do DragonFlight.</summary>
    float LiftMul => growth != null ? growth.FlapLiftMul : 1f;
    float SinkMul => growth != null ? growth.SinkMul : 1f;
    /// <summary>Giro: agilidade do PESO/idade × atributo Agilidade × traço (Couraça enrijece).</summary>
    float TurnMul => (growth != null ? growth.TurnAgilityMul : 1f)
                   * (attrs != null ? attrs.TurnMul : 1f)
                   * (traits != null ? traits.TurnMul : 1f);
    /// <summary>Fator das janelas de i-frame (Instinto) — dash/boost/esquiva.</summary>
    float IFrameMul => attrs != null ? attrs.IFrameMul : 1f;
    float EffRunSpeed => runSpeed * RunMul * S;
    /// <summary>Aceleração terrestre efetiva: peso e idade modulam a EXPLOSÃO,
    /// não criam espera (filhote arranca ligeiro; gordo/colossal empurram mais).</summary>
    float EffGroundAccel => groundAccel * S * AttrAccel *
        (growth != null ? growth.AccelAgilityMul : 1f);
    float GroundSpeed01 => Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, EffRunSpeed));

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        vitals = GetComponent<DragonVitals>();
        growth = GetComponent<DragonGrowth>();
        attrs = GetComponent<DragonAttributes>();
        flight = GetComponent<DragonFlight>();
        if (flight == null) flight = gameObject.AddComponent<DragonFlight>(); // garante o módulo de voo
        traits = GetComponent<DragonTraits>();
        if (traits == null) traits = gameObject.AddComponent<DragonTraits>(); // traços herdáveis (vazio sem record)
        if (GetComponent<DragonSounds>() == null)
            gameObject.AddComponent<DragonSounds>(); // receptor dos AnimationEvents "PlaySound" dos FBX
        if (vitals != null && GetComponent<DragonDamageFeedback>() == null)
            gameObject.AddComponent<DragonDamageFeedback>(); // shake/vinheta/som de dano
        anim.applyRootMotion = false;
        yaw = prevYaw = transform.eulerAngles.y;

        // o parâmetro "Stealth" e o estado "Fly Fall Death" só existem após rodar
        // o DragonAnimatorTuner — sem eles tudo funciona igual, só sem a postura
        // de caça no idle e sem o tombo da colisão em voo
        foreach (var p in anim.parameters)
            if (p.name == "Stealth") { hasStealthParam = true; break; }
        hasFallDeathState = anim.HasState(0, Animator.StringToHash(FallDeathState));

        if (groundMask == 0)
            groundMask = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);

        foreach (var col in GetComponentsInChildren<Collider>())
            if (!(col is CharacterController))
                Physics.IgnoreCollision(cc, col, true);
    }

    void Start()
    {
        if (vitals != null) vitals.OnDeath += Die;
        if (growth != null) growth.OnStageChanged += OnStageUp;
    }

    void OnDestroy()
    {
        if (vitals != null) vitals.OnDeath -= Die;
        if (growth != null) growth.OnStageChanged -= OnStageUp;
    }

    void Update()
    {
        if (DragonStatsMenu.IsOpen) return; // menu pausa o jogo

        float dt = Time.deltaTime;
        DragonInput.Sample();

        if (dead)
        {
            if (!cc.isGrounded) cc.Move(Vector3.down * 10f * dt);
            // Morte NÃO recomeça a cena (fim do "game over do The Isle"): a Base assume
            // o próximo dragão. Enter força a troca na mão (debug/atalho de possessão).
            if (DragonInput.RespawnDown) DragonBase.Instance?.PossessNext();
            return;
        }

        // salto de decolagem parado: refaz a trava TODO frame enquanto o Animator
        // ainda estiver nele — h/v saem zerados abaixo (Locked), corpo estável
        if (inStationaryTakeoff) UpdateStationaryTakeoffLock();

        float h = Locked ? 0f : DragonInput.Horizontal;
        float v = Locked ? 0f : DragonInput.Vertical;

        stealth = !flying && !swimming && !resting && DragonInput.StealthHeld;

        // dash/boost ANTES do movimento: se disparar agora, "dashing" já nasce
        // true e o GroundUpdate deste MESMO frame pula o giro normal — senão o
        // corpo vira um pouco rumo ao input antes do dash sequer ser detectado
        if (!resting && !swimming) HandleBurst();          // cancela golpes também

        if (resting) RestUpdate();
        else if (swimming) SwimUpdate(dt, h, v);
        else if (flying) FlightUpdate(dt, h, v);
        else GroundUpdate(dt, h, v);

        if (!resting && !swimming && !Locked) HandleActions();
        UpdateHint();

        ApplyRotation(dt, h);
        UpdateAnimator(dt, h);
    }

    // ------------------------------------------------------------------ CHÃO
    void GroundUpdate(float dt, float h, float v)
    {
        bool grounded = cc.isGrounded;
        bool dashing = Time.time < dashUntil;

        Vector3 planarVel;
        if (dashing)
        {
            // rajada LATERAL pura: decai suave no fim, corpo travado (yaw parado).
            // planarSpeed (só usado p/ anim de corrida p/ frente) fica intocado —
            // a corrida retoma de onde estava assim que o dash termina.
            float t01 = 1f - Mathf.Clamp01((dashUntil - Time.time) / dashDuration);
            planarVel = dashVel * (1f - t01 * t01 * 0.55f);
        }
        else
        {
            // ---- direção desejada RELATIVA À CÂMERA (hack and slash)
            Vector3 wish = CameraRelativeInput(h, v);
            float mag = Mathf.Clamp01(wish.magnitude);

            // o corpo gira rumo ao input: fechado devagar, arco em corrida (peso VISÍVEL)
            if (mag > 0.05f)
            {
                float desiredYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                float rate = Mathf.Lerp(turnRateIdle, turnRateRun, GroundSpeed01) * TurnMul;
                yaw = Mathf.MoveTowardsAngle(yaw, desiredYaw, rate * dt);
            }

            // ---- SEMPRE CORRE; Shift = passo de caçada; exausto não engata corrida
            float top = stealth ? walkSpeed * S : EffRunSpeed;
            if (Exhausted) top = Mathf.Min(top, walkSpeed * S);
            float target = mag > 0.05f ? top * mag : 0f;

            float decel = groundDecel * S;
            if (Locked && lockKind == LockKind.Attack) decel *= 0.35f;  // desliza no golpe
            float rate2 = target > planarSpeed ? EffGroundAccel : decel;
            planarSpeed = Mathf.MoveTowards(planarSpeed, target, rate2 * dt);

            Vector3 fwdMove = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            planarVel = fwdMove * planarSpeed;
        }

        if (planarSpeed > (walkSpeed + 0.5f) * S) vitals?.Drain(runCost * CostMul);

        if (grounded)
        {
            // pisou o chão: cobra a altura se veio de uma queda de verdade
            // (despencar de um penhasco andando conta igual a cair voando)
            float severity = ResolveFall();
            if (severity > 0f)
            {
                Lock(1.2f, LockKind.Stagger);
                ImpactEffects.Emit(new ImpactEvent
                {
                    kind = ImpactKind.HardLanding,
                    position = transform.position,
                    normal = Vector3.up,
                    velocity = Vector3.down * Mathf.Abs(verticalVel),
                    strength01 = severity,
                    scale = VfxScale,
                });
            }
            verticalVel = -4f;
        }
        else
        {
            verticalVel -= gravity * dt;
            TrackFall(verticalVel < -2f);   // do ápice em diante: é queda
        }

        // ---- decolagem (bufferizada; cancela golpe após a janela — nunca um Stagger)
        if (!dashing && (!Locked || CanCancelAttack) &&
            DragonInput.Consume(DragonInput.Act.Flap))
        {
            // Space no chão SEMPRE decola — parado inclusive. Antes, parar de vez
            // caía num "bate asas no lugar" (UPFly Stand) que ficava horrível: o
            // salto bom (UJump Up) exigia estar em movimento, então quem estava
            // parado nunca o via. No ar, coyote de penhasco: abrir as asas vale.
            if (Spend(takeoffCost * CostMul)) BeginTakeoff();
        }
        if (flying) return;   // decolou neste frame

        if (grounded && Mathf.Abs(planarSpeed) < 0.1f && Time.time > nextIdleChange)
        {
            nextIdleChange = Time.time + UnityEngine.Random.Range(6f, 12f);
            anim.SetFloat(P_IdleVar, UnityEngine.Random.Range(0, 3));
        }

        cc.Move((planarVel + Vector3.up * verticalVel) * dt);

        // andou até água funda: começa a nadar
        if (DeepWaterAt(transform.position, out float surface) &&
            transform.position.y < surface)
            EnterSwim();
    }

    /// <summary>Input WASD no plano da câmera (fallback: relativo ao corpo).</summary>
    Vector3 CameraRelativeInput(float h, float v)
    {
        if (camT == null && Camera.main != null) camT = Camera.main.transform;
        if (camT == null)
            return Vector3.ClampMagnitude(Quaternion.Euler(0f, yaw, 0f) * new Vector3(h, 0f, v), 1f);

        Vector3 f = camT.forward; f.y = 0f; f.Normalize();
        Vector3 r = camT.right; r.y = 0f; r.Normalize();
        return Vector3.ClampMagnitude(f * v + r * h, 1f);
    }

    /// <summary>Space no chão/ar: em corrida plena abre as asas DIRETO pro voo;
    /// devagar/parado dá o salto clássico — travado e ESTÁVEL até o fim (ver
    /// UpdateStationaryTakeoffLock): não gira nem é interrompido por outra ação.</summary>
    void BeginTakeoff()
    {
        bool canceling = Locked;              // só chega aqui via CanCancelAttack
        if (canceling) CancelLock();

        // decide pelo MESMO valor que as transições do Animator testam (o
        // parâmetro Speed é amortecido): controller e state machine nunca
        // divergem no instante da decolagem. 1.5 = metade da corrida
        // (runningTakeoffFraction × 3, a escala do blend de locomoção).
        bool runningStart = anim.GetFloat(P_Speed) >= runningTakeoffFraction * 3f;
        if (!runningStart)
        {
            inStationaryTakeoff = true;
            // trava mínima já no frame 0: cobre o blend de entrada até o Animator
            // confirmar "TakeOff" (Stagger: nem dash/ataque cancela — estável de verdade)
            Lock(takeoffLockMinimum, LockKind.Stagger);
        }
        EnterFlight(runningStart);
        // a subida do salto é roteirizada (EnterFlight acabou de zerar a fase):
        // sobe sozinho durante o clipe, e o bote vem no fim, em TakeoffLaunch
        if (!runningStart) flight?.BeginScriptedClimb(takeoffMaxLock);

        // cancelou um golpe: corta a animação na mão (não há transição saindo
        // dos estados de ataque antes do exit time — o CrossFade resolve)
        if (canceling)
            anim.CrossFadeInFixedTime(runningStart ? "Fly" : "TakeOff", 0.1f, 0);
    }

    /// <summary>Mantém o lock (rotação zerada + ações bloqueadas) enquanto o
    /// Animator estiver de fato no salto de decolagem — a duração real do
    /// CLIPE, não um número chutado. Sai sozinho no instante em que o Animator
    /// avança para Fly, sem depender de contar segundos manualmente.</summary>
    void UpdateStationaryTakeoffLock()
    {
        var info = anim.GetCurrentAnimatorStateInfo(0);
        bool stillInClip = info.IsName(TakeOffState) && info.normalizedTime < 0.98f;
        bool enteringClip = anim.IsInTransition(0) &&
            anim.GetNextAnimatorStateInfo(0).IsName(TakeOffState);
        // o Animator leva 1-2 frames para entrar no estado: sem esta carência o
        // salto seria "encerrado" no primeiro frame, antes mesmo de começar
        bool settling = Time.time - takeoffTime < takeoffLockMinimum;
        bool ranAway = Time.time - takeoffTime > takeoffMaxLock;   // rédea de segurança

        if ((stillInClip || enteringClip || settling) && !ranAway)
            Lock(takeoffLockMinimum, LockKind.Stagger);   // refresca — nunca cancelável
        else
        {
            inStationaryTakeoff = false;
            TakeoffLaunch();
        }
    }

    /// <summary>O clímax do salto: o bote das asas que arranca o dragão do chão.
    /// Vem no FIM do clipe, exatamente na virada para o voo — antes todo o
    /// impulso era dado no primeiro frame e chegava gasto aqui, então a
    /// decolagem parecia PERDER força justo quando devia ganhar.</summary>
    void TakeoffLaunch()
    {
        // o bote segue a MESMA regra do voo (DragonFlight): quem manda na altura
        // é o peso atual, não o tamanho — decolar não pode render um pulo de
        // filhote e um foguete de colossal
        flight?.Launch(takeoffLaunchClimb * LiftMul);
        flySpeed += takeoffLaunchForward * S;
        vertInput = 1f;

        DragonCamera.Instance?.AddShake(0.18f);
        ImpactEffects.Emit(new ImpactEvent
        {
            kind = ImpactKind.WingBoost,
            position = transform.position,
            normal = Vector3.up,
            velocity = flightVel,
            strength01 = 0.85f,
            scale = VfxScale,
        });
    }

    // ------------------------------------------------------------------- VOO
    // Voo como habilidade: 1 toque de Space = 1 batida, 2 por ciclo (DragonFlight).
    // Entre batidas: planeio com sustentação pela velocidade; updrafts ajudam.
    void FlightUpdate(float dt, float h, float v)
    {
        // Shift segurado = mergulho, MENOS durante a esquiva completa (o combo
        // exige Shift segurado: mergulhar junto arruinaria a virada)
        diving = DragonInput.Held(DragonInput.Act.Burst) && Time.time >= flyDodgeUntil;

        float s = S;
        ProcessFlightImpact(s);   // consequências da colisão do frame anterior

        float target = cruiseSpeed * s;
        if (v > 0.1f) target = Mathf.Lerp(cruiseSpeed, maxFlySpeed, v) * s;
        else if (v < -0.1f) target = Mathf.Lerp(cruiseSpeed, minFlySpeed, -v) * s;
        if (diving) target = maxFlySpeed * flight.DiveOverspeed * s;   // mergulho FURA o teto
        if (stalling) target = minFlySpeed * s;

        // engata rápido, sangra devagar: conservar velocidade é o prazer do voo
        float accel = target > flySpeed ? flyAccel * (diving ? 2f : 1f) * s
                                        : flyAccel * 0.35f * s;
        flySpeed = Mathf.MoveTowards(flySpeed, target, accel * dt);

        if (Time.time < flyDodgeUntil)
        {
            // esquiva completa: o rumo vira DECIDIDO até o alvo — taxa = ângulo
            // que falta ÷ tempo que resta, então a virada fecha exatamente no fim
            float rate = Mathf.Abs(Mathf.DeltaAngle(yaw, flyDodgeYawTarget)) /
                         Mathf.Max(0.01f, flyDodgeUntil - Time.time);
            yaw = Mathf.MoveTowardsAngle(yaw, flyDodgeYawTarget, rate * dt);
        }
        else
        {
            // curva fechada devagar, ampla em alta — predador, não avião
            float turnFactor = Mathf.Lerp(1.5f, 0.85f, Speed01) * TurnMul;
            yaw += h * turnSpeedAir * turnFactor * dt;
        }

        // ---- pouso controlado: segurar S = descida DECIDIDA rumo ao solo.
        float landingSink = 0f;
        if (v < -0.1f && !stalling)
        {
            Vector3 approach = transform.position + cc.center;
            if (Physics.SphereCast(approach, cc.radius * 0.9f, Vector3.down,
                    out var ground, landApproachProbe * s + cc.height * 0.5f,
                    groundMask, QueryTriggerInteraction.Ignore))
            {
                float far01 = Mathf.Clamp01(ground.distance / (landApproachProbe * s));
                landingSink = Mathf.Lerp(landFlareRate, landDescendRate, far01) * s;
            }
            else landingSink = landDescendRate * s;
        }

        // ---- ciclo de batidas de asa (Space bufferizado: nunca "come" a batida)
        bool flapPressed = !Locked && DragonInput.Consume(DragonInput.Act.Flap, 0.15f);
        bool held = DragonInput.Held(DragonInput.Act.Flap);
        float vy = flight != null
            ? flight.Tick(dt, flapPressed, held, diving, Speed01, s,
                          transform.position, ref flySpeed, landingSink)
            : Time.time - takeoffTime < 0.9f ? climbRate * s     // fallback sem módulo
            : diving ? -diveRate * s : -glideSink * SinkMul;

        // swoop/boost podem passar do máximo — trava no overspeed permitido
        flySpeed = Mathf.Min(flySpeed,
            maxFlySpeed * Mathf.Max(flight != null ? flight.DiveOverspeed : 1f,
                                    boostMaxOverspeed) * s);

        gliding = flight == null || flight.IsGliding;

        // ---- estol: sem energia pra bater asas e devagar demais.
        //      Desequilíbrio pós-colisão também segura o estol até passar.
        if (Exhausted)
        {
            if (!stalling && flySpeed < (minFlySpeed + 1f) * s) SetStall(true);
        }
        else if (stalling && !IsStaggered) SetStall(false);
        if (stalling) vy = -stallSink;

        vitals?.Drain(glideCost * CostMul);   // sustentação passiva: custo mínimo

        // pitch e animação seguem o movimento vertical REAL, normalizado pela
        // subida MÁXIMA que este corpo alcança — senão as batidas fortes do
        // sistema novo saturariam o pitch em toda batida
        float riseRef = flight != null ? Mathf.Max(1f, flight.MaxRiseSpeed)
                                       : Mathf.Max(1f, climbRate * s);
        vertInput = Mathf.MoveTowards(vertInput, Mathf.Clamp(vy / riseRef, -1f, 1f), 5f * dt);

        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        flightVel = fwd * flySpeed + Vector3.up * vy;   // OnControllerColliderHit mede o impacto daqui
        cc.Move(flightVel * dt);

        // rastreia queda descontrolada (estol/desequilíbrio) p/ o dano de altura
        TrackFall(stalling || IsStaggered);

        bool overDeepWater = DeepWaterAt(transform.position, out float surface);

        // ÁGUA: só o CONTATO real com a lâmina interrompe o voo — proximidade
        // nunca força pouso. Encostou, vira nado (a água amortece, sem dano).
        if (overDeepWater && vy < 0f && transform.position.y <= surface + 0.3f)
        {
            EnterSwim();
            return;
        }

        // barriga rente à lâmina: spray e ondulações — puramente visual
        if (overDeepWater) UpdateWaterSkim(surface);

        // carência pós-decolagem e pouso só em DESCIDA REAL (vy < -1.5):
        // o afundamento suave do planeio rápido (~-0.9) não força pouso.
        if (Time.time - takeoffTime < takeoffGrace) return;
        if (cc.isGrounded) { Land(); return; }

        // ---- POUSO AUTOMÁTICO: voando rente ao chão sem intenção de subir, o
        //      dragão pousa de verdade em vez de "raspar" o terreno voando.
        bool wantsAltitude = held || vy > 0.5f || (flight != null && flight.InTakeoffClimb);
        if (!wantsAltitude && !overDeepWater)
        {
            Vector3 near = transform.position + cc.center;
            if (Physics.SphereCast(near, cc.radius * 0.9f, Vector3.down, out var touch,
                    autoLandHeight * s + cc.height * 0.5f,
                    groundMask, QueryTriggerInteraction.Ignore) &&
                touch.normal.y > autoLandMaxSlope && IsRealGround(touch.point))
            {
                Land();
                return;
            }
        }

        if ((stalling || flySpeed <= landMaxSpeed * s) &&
            (vy < -1.5f || (landingSink > 0f && vy < -0.5f)))
        {
            Vector3 origin = transform.position + cc.center;
            if (Physics.SphereCast(origin, cc.radius * 0.9f, Vector3.down,
                    out _, landProbeDistance * s + cc.height * 0.5f,
                    groundMask, QueryTriggerInteraction.Ignore))
                Land();
        }
    }

    // -------------------------------------------------- DASH / WING BOOST
    /// <summary>Chão: Shift + A/D = dash lateral (chord — as duas teclas frescas
    /// e próximas). Ar: Shift = Wing Boost/esquiva/mergulho. Cancela golpes após
    /// a janela de ataque.</summary>
    void HandleBurst()
    {
        if (dead || Time.time < dashUntil) return;
        if (Locked && !CanCancelAttack) return;

        if (flying)
        {
            // ESQUIVA COMPLETA: A/D + Shift + Space. Shift e direcional são
            // MODIFICADORES (segurados), Space é o gatilho — sem chord por tempo,
            // então sai no frame exato do toque. Consumir o Flap aqui impede a
            // batida de asa de sair junto (o HandleBurst roda antes do voo).
            float steer = DragonInput.Horizontal;
            if (Mathf.Abs(steer) > 0.5f && DragonInput.Held(DragonInput.Act.Burst) &&
                DragonInput.Consume(DragonInput.Act.Flap, 0.15f))
            {
                FlyDodge(steer < 0f ? -1f : 1f);
                return;
            }

            if (Time.time < boostReadyAt) return;
            if (!DragonInput.Consume(DragonInput.Act.Burst)) return;
            AirBurst();
            return;
        }

        if (!cc.isGrounded || Time.time < dashReadyAt) return;
        bool right = DragonInput.ConsumeDashRight();
        bool left = !right && DragonInput.ConsumeDashLeft();
        if (right || left) GroundDash(right ? 1f : -1f);
    }

    /// <summary>Dash lateral puro (side: +1 direita, -1 esquerda) — mesma
    /// animação Dodge L/R do dash aéreo (era o pedido: "mesma animação de voo").</summary>
    void GroundDash(float side)
    {
        if (!Spend(dodgeCost * CostMul)) return;
        if (Locked) CancelLock();

        Vector3 rightDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        dashVel = rightDir * side * EffRunSpeed * dashSpeedMul;
        dashUntil = Time.time + dashDuration;
        dashReadyAt = Time.time +
            dashCooldown * (growth != null ? growth.DashCooldownMul : 1f);
        vitals?.GrantIFrames(dashIFrames * IFrameMul);   // Instinto estica a janela

        anim.SetFloat(P_DodgeDir, side);
        anim.SetTrigger(P_Dodge);

        DragonCamera.Instance?.AddShake(0.15f);
        ImpactEffects.Emit(new ImpactEvent
        {
            kind = ImpactKind.DashBurst,
            position = transform.position,
            normal = Vector3.up,
            velocity = dashVel,
            strength01 = 0.7f,
            scale = VfxScale,
        });
    }

    void AirBurst()
    {
        if (vitals != null && !vitals.TrySpend(boostCost * CostMul)) return;
        if (Locked) CancelLock();
        boostReadyAt = Time.time + boostCooldown;
        vitals?.GrantIFrames(boostIFrames * IFrameMul);   // Instinto

        float side = DragonInput.Horizontal;
        if (Mathf.Abs(side) > 0.5f)
        {
            // esquiva SIMPLES: desvia o rumo e escapa da linha do ataque, sem
            // animação dedicada — a Fly Dodge L/R é exclusiva do combo com Space
            // (era o trigger daqui que saía impreciso, disputando o blend de voo).
            lastAirDodgeYaw = (side < 0f ? -1f : 1f) * airDodgeYaw;
            lastAirDodgeTime = Time.time;
            yaw += lastAirDodgeYaw;
        }
        else
        {
            // WING BOOST: batida forte — aceleração instantânea (fantasia de dragão).
            // O visual vem do tranco de sustentação (Knock) refletido no blend de voo.
            // Peso/idade (BoostMul) × Força (o aríete das asas do dragão possante).
            float mul = (growth != null ? growth.BoostMul : 1f) * (attrs != null ? attrs.BoostMul : 1f);
            flySpeed = Mathf.Min(flySpeed + boostImpulse * S * mul,
                                 maxFlySpeed * boostMaxOverspeed * S);
            flight?.Knock(1.5f * mul);
        }

        DragonCamera.Instance?.AddShake(0.2f);
        ImpactEffects.Emit(new ImpactEvent
        {
            kind = ImpactKind.WingBoost,
            position = transform.position,
            normal = Vector3.up,
            velocity = flightVel,
            strength01 = 0.8f,
            scale = VfxScale,
        });
    }

    /// <summary>Esquiva de voo COMPLETA (A/D + Shift + Space): toca o clipe exato
    /// Fly Dodge L/R e VIRA O VOO INTEIRO para o novo rumo — não é um empurrão
    /// lateral mantendo a direção, é trocar de direção. Se vier logo depois de
    /// uma esquiva simples (o Shift do próprio combo dispara aquela primeiro),
    /// ABSORVE-A: desfaz o juke e não cobra energia de novo — o combo é UMA ação.</summary>
    void FlyDodge(float side)
    {
        bool absorbing = Time.time - lastAirDodgeTime < 0.3f;
        if (!absorbing && vitals != null && !vitals.TrySpend(flyDodgeCost * CostMul)) return;
        if (absorbing) yaw -= lastAirDodgeYaw;
        lastAirDodgeTime = -99f;
        if (Locked) CancelLock();

        flyDodgeYawTarget = yaw + side * flyDodgeTurn * TurnMul;
        flyDodgeUntil = Time.time + flyDodgeDuration;
        diving = false;                    // o Shift do combo não vira mergulho
        vitals?.GrantIFrames(flyDodgeIFrames * IFrameMul);   // Instinto

        // CrossFade no estado EXATO: o trigger dependia da transição vencer o
        // blend do voo, e era isso que fazia a animação sair imprecisa
        anim.ResetTrigger(P_Dodge);
        anim.SetFloat(P_DodgeDir, side);
        anim.CrossFadeInFixedTime(side < 0f ? FlyDodgeLState : FlyDodgeRState, 0.08f, 0);

        DragonCamera.Instance?.AddShake(0.18f);
    }

    // -------------------------------------------------------- COLISÃO EM VOO
    /// <summary>Colisões do CharacterController: em voo, guarda o impacto mais
    /// forte do frame (árvore, rocha, penhasco) para o FlightUpdate reagir.</summary>
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!flying) return;
        if (hit.normal.y > 0.6f) return;   // superfície de pouso — Land() cuida

        // só a componente da velocidade que ENTRA no obstáculo conta:
        // raspão tangencial ≈ 0, batida frontal = velocidade cheia
        float impact = -Vector3.Dot(flightVel, hit.normal);
        if (impact > pendingImpact)
        {
            pendingImpact = impact;
            pendingNormal = hit.normal;
            pendingPoint = hit.point;
            pendingCollider = hit.collider;
        }
    }

    /// <summary>Consequência proporcional: raspão passa batido, colisão leve
    /// freia e desvia, colisão forte derruba a sustentação (desequilíbrio) —
    /// o jogador precisa recuperar velocidade pra voltar a voar. Bater em
    /// obstáculo NÃO tira vida: atrapalha o voo (e o que machuca é o chão,
    /// se a queda vier de alto o bastante).</summary>
    void ProcessFlightImpact(float s)
    {
        float impact = pendingImpact;
        Vector3 n = pendingNormal;
        Vector3 point = pendingPoint;
        var surface = pendingCollider;
        pendingImpact = 0f;
        pendingCollider = null;

        if (impact <= 0f || IsStaggered) return;
        if (Time.time - lastImpactTime < impactCooldown) return;
        if (Time.time - takeoffTime < takeoffGrace) return;   // carência pós-decolagem

        float impact01 = Mathf.Clamp01(impact / (maxFlySpeed * s));
        if (impact01 < impactLight) return;           // raspão: navegação rente é permitida
        lastImpactTime = Time.time;

        // deflexão: o nariz escorrega para a tangente do obstáculo
        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 slide = Vector3.ProjectOnPlane(fwd, n);
        slide.y = 0f;
        if (slide.sqrMagnitude > 0.001f)
        {
            float slideYaw = Mathf.Atan2(slide.x, slide.z) * Mathf.Rad2Deg;
            yaw = Mathf.LerpAngle(yaw, slideYaw, Mathf.Clamp01(0.35f + impact01 * 0.5f));
        }

        flySpeed *= 1f - impact01 * impactSpeedLoss;
        flight?.Knock(-impactKnockDown * impact01);

        // folhas, galhos, lascas — o VFX do obstáculo (ver ImpactEffects)
        ImpactEffects.Emit(new ImpactEvent
        {
            kind = ImpactKind.ObstacleStrike,
            position = point,
            normal = n,
            velocity = flightVel,
            strength01 = impact01,
            scale = VfxScale,
            surface = surface,
        });

        if (impact01 >= impactHeavy)
        {
            // desequilíbrio: perde sustentação, cai e fica sem controle um instante.
            // Sem dano: a árvore atrapalha o voo, quem machuca é o chão lá embaixo.
            staggerUntil = Time.time + staggerTime;
            SetStall(true);
            flySpeed = Mathf.Min(flySpeed, minFlySpeed * s);
            Lock(staggerTime, LockKind.Stagger);
            flyDodgeUntil = -99f;          // bateu no meio da esquiva: ela acaba aqui

            // TOMBO: bater forte derruba o dragão de verdade (UPFly Fall Death).
            // Só a COLISÃO usa este clipe — o estol por exaustão segue no Stall
            // Fall, que é uma queda controlada, não um tombo.
            if (hasFallDeathState) anim.CrossFadeInFixedTime(FallDeathState, 0.1f, 0);
        }
        else Spend(impactEnergyCost * impact01);
    }

    /// <summary>O que a sondagem achou é CHÃO mesmo, e não o topo de uma árvore
    /// ou de um rochedo? (evita "pousar" na copa da mata voando baixo)</summary>
    bool IsRealGround(Vector3 point)
    {
        var world = InfiniteTerrain.Instance;
        if (world == null || point.y <= world.HeightAt(point.x, point.z) + 1f) return true;
        // placa de gelo da Tundra: fica metros acima do leito do lago, mas é chão
        return world.IsWaterFrozenAt(point.x, point.z) && point.y <= world.WaterLevel + 1f;
    }

    // --------------------------------------------------- QUEDA E ÁGUA (VFX)
    /// <summary>Altura de queda que o dragão aguenta sem se machucar: ~70% da
    /// MAIOR árvore da floresta. Mexer na vegetação recalibra o limite sozinho.</summary>
    public float SafeFallHeight
    {
        get
        {
            float tree = InfiniteTerrain.Instance != null
                ? InfiniteTerrain.Instance.MaxForestTreeHeight : 0f;
            if (tree <= 0.5f) tree = safeFallFallback;   // mundo fixo/sem vegetação
            return tree * safeFallTreeFraction;
        }
    }

    /// <summary>Marca (e mantém) o Y onde uma queda DESCONTROLADA começou.
    /// Descida controlada não conta: planar até o chão nunca machuca.</summary>
    void TrackFall(bool uncontrolled)
    {
        if (uncontrolled) { if (float.IsNaN(fallFromY)) fallFromY = transform.position.y; }
        else fallFromY = float.NaN;
    }

    /// <summary>Fecha a queda ao tocar o chão e aplica o dano da altura.
    /// Retorna 0..1 = severidade (0 = pouso limpo) para o VFX/anim.</summary>
    float ResolveFall()
    {
        if (float.IsNaN(fallFromY)) return 0f;      // não havia queda em curso
        float drop = fallFromY - transform.position.y;
        fallFromY = float.NaN;

        float safe = SafeFallHeight;
        if (drop <= safe) return 0f;

        float excess = (drop - safe) / safe;             // 1 = caiu do dobro do seguro
        // Ossos Ocos amplia o dano de queda (o esqueleto leve quebra mais fácil).
        float fallMul = traits != null ? traits.FallDamageMul : 1f;
        vitals?.Damage(fallDamageAtDouble * excess * fallMul);
        return Mathf.Clamp01(excess);
    }

    /// <summary>Barriga rente ao lago/rio: spray e ondulações acompanhando o voo.
    /// Só visual — a água nunca força pouso nem tira o dragão do ar.</summary>
    void UpdateWaterSkim(float surfaceY)
    {
        float belly = transform.position.y + cc.center.y - cc.height * 0.5f;
        float gap = belly - surfaceY;
        float reach = waterSkimHeight * Mathf.Max(0.2f, transform.lossyScale.y);
        if (gap < 0f || gap > reach) return;

        float closeness = 1f - gap / reach;
        ImpactEffects.Skim(new ImpactEvent
        {
            kind = ImpactKind.WaterSkim,
            position = new Vector3(transform.position.x, surfaceY, transform.position.z),
            normal = Vector3.up,
            velocity = flightVel,
            strength01 = closeness * Mathf.Max(0.35f, Speed01),
            scale = VfxScale,
        });
    }

    /// <summary>Tamanho do dragão — dimensiona os efeitos (filhote ≠ adulto).</summary>
    float VfxScale => Mathf.Max(0.2f, transform.lossyScale.y);

    // ---------------------------------------------------------------- AÇÕES
    void HandleActions()
    {
        bool grounded = !flying && cc.isGrounded;
        if (!grounded) return;   // golpes aéreos vivem no DragonAbilities (1-4)

        if (DragonInput.Consume(DragonInput.Act.Eat))               // comer
        {
            var food = Carcass.FindNearest(transform.position, eatRange * S);
            if (food != null)
            {
                anim.SetInteger(P_AttackType, 0);                   // mordida
                anim.SetTrigger(P_Attack);
                Lock(1.0f, LockKind.Attack);
                // Estômago de Ferro rende mais de cada refeição (carniça velha inclusive).
                float n = food.Consume() * (traits != null ? traits.EatNutritionMul : 1f);
                vitals?.Eat(n);
                growth?.NotifyAte(n);
            }
        }
        else if (DragonInput.Consume(DragonInput.Act.Melee) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, meleeCombo);
            anim.SetTrigger(P_Attack);
            meleeCombo = (meleeCombo + 1) % 4;
            Lock(1.0f, LockKind.Attack);
            StrikeWildlife(2.2f, 3.0f, 16f);
        }
        else if (DragonInput.Consume(DragonInput.Act.Tail) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, tailLeft ? 4 : 5);
            tailLeft = !tailLeft;
            anim.SetTrigger(P_Attack);
            Lock(1.2f, LockKind.Attack);
            StrikeWildlife(0f, 4.0f, 12f);      // cauda varre ao redor
        }
        else if (DragonInput.Consume(DragonInput.Act.Wing) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, wingLeft ? 6 : 7);
            wingLeft = !wingLeft;
            anim.SetTrigger(P_Attack);
            Lock(1.1f, LockKind.Attack);
            StrikeWildlife(1.2f, 3.5f, 10f);
        }
        else if (DragonInput.Consume(DragonInput.Act.Fire) && Spend(fireCost * CostMul))
        {
            anim.SetTrigger(P_Fire);
            Lock(1.9f, LockKind.Attack);
        }
        else if (DragonInput.Consume(DragonInput.Act.Roar))
        {
            anim.SetTrigger(P_Roar);
            Lock(2.4f, LockKind.Attack);   // rugido também cancela com dash
        }
        else if (Mathf.Abs(planarSpeed) < 0.5f && DragonInput.Consume(DragonInput.Act.Rest))
        {
            resting = true;
            stealth = false;
            anim.SetBool(P_Rest, true);
        }
    }

    void UpdateHint()
    {
        if (Time.time < nextHintCheck) return;
        nextHintCheck = Time.time + 0.25f;

        string hint = "";
        // ar rarefeito perto do teto de voo (Resistência): avisa ANTES de estolar
        // por falta de sustentação — prioridade sobre a corrente ascendente, porque
        // é o updraft que deixa de ajudar justo aqui
        if (flying && flight != null && flight.ThinAir01 > 0.55f)
            hint = "Ar rarefeito — perdendo sustentação!";
        else if (flying && flight != null && flight.CurrentWind.y > 1.5f)
            hint = "^ Corrente ascendente — plane nela!";
        else if (!flying && !resting && !dead)
        {
            if (Carcass.FindNearest(transform.position, eatRange * S) != null)
                hint = "G — comer";
            else if (attrs != null)
            {
                // Dominância Territorial (Poder): faro detecta comida ao longe
                var far = Carcass.FindNearest(transform.position, attrs.DominanceRadius);
                if (far != null)
                {
                    float d = Vector3.Distance(transform.position, far.transform.position);
                    hint = $"Faro: comida a {d:0} m";
                }
            }
        }

        if (hint != currentHint)
        {
            currentHint = hint;
            OnHintChanged?.Invoke(hint);
        }
    }

    void RestUpdate()
    {
        planarSpeed = 0f;
        if (cc.isGrounded) verticalVel = -4f;
        cc.Move(Vector3.up * verticalVel * Time.deltaTime);

        if (DragonInput.Consume(DragonInput.Act.Rest) ||
            DragonInput.Consume(DragonInput.Act.Flap) ||
            Mathf.Abs(DragonInput.Horizontal) > 0.1f ||
            Mathf.Abs(DragonInput.Vertical) > 0.1f)
        {
            resting = false;
            anim.SetBool(P_Rest, false);
            Lock(1.0f);
        }
    }

    // ------------------------------------------------------------------ NADO
    /// <summary>Água funda o bastante para boiar? (consulta o nível global de lagos)</summary>
    bool DeepWaterAt(Vector3 pos, out float surfaceY)
    {
        surfaceY = 0f;
        var world = InfiniteTerrain.Instance;
        if (world == null || !world.HasLakes) return false;
        surfaceY = world.WaterLevel;
        return world.HeightAt(pos.x, pos.z) < surfaceY - minSwimDepth
            && !world.IsWaterFrozenAt(pos.x, pos.z);   // Tundra: lâmina congelada = chão, não água
    }

    void SwimUpdate(float dt, float h, float v)
    {
        float s = S;
        // gordo nada pior (mesma penalidade da corrida); exausto se arrasta.
        // Guelras Vestigiais nadam bem mais rápido (o dragão aquático da linhagem).
        float mul = RunMul * (Exhausted ? 0.55f : 1f) * (traits != null ? traits.SwimSpeedMul : 1f);
        float target = v > 0.01f ? v * swimSpeed * mul
                     : v < -0.01f ? v * swimBackSpeed
                     : 0f;
        target *= s;
        planarSpeed = Mathf.MoveTowards(planarSpeed, target, swimAccel * s * dt);
        yaw += h * swimTurnSpeed * dt;

        // boia na linha d'água com um balanço sutil
        float surface = InfiniteTerrain.Instance != null ? InfiniteTerrain.Instance.WaterLevel : 0f;
        float floatY = surface - buoyDepth * transform.lossyScale.y
                     + Mathf.Sin(Time.time * 1.3f) * 0.08f;
        float vyMove = Mathf.Clamp(floatY - transform.position.y, -3f * dt, 2.5f * dt);

        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        cc.Move(fwd * planarSpeed * dt + Vector3.up * vyMove);

        // Guelras Vestigiais respiram submerso: nadar não custa energia.
        if (traits == null || !traits.BreathesUnderwater)
            vitals?.Drain(swimCost * CostMul * (Mathf.Abs(planarSpeed) > 0.3f ? 1f : 0.35f));

        // Space: decola da água (explosão de asas — custa um pouco mais)
        if (!Locked && DragonInput.Consume(DragonInput.Act.Flap) &&
            Spend(takeoffCost * 1.3f * CostMul))
        {
            ExitSwim();
            EnterFlight(false);
            return;
        }

        // chegou ao raso: sai andando
        if (!DeepWaterAt(transform.position, out _))
        {
            // borda congelada (Tundra): o gelo é PAREDE pro nado — recua em vez
            // de "sair andando", senão o dragão afunda e fica preso SOB a placa
            var world = InfiniteTerrain.Instance;
            if (world != null && world.IsWaterFrozenAt(transform.position.x, transform.position.z))
            {
                cc.Move(fwd * (-planarSpeed * dt));
                planarSpeed = 0f;
            }
            else
            {
                ExitSwim();
                verticalVel = -4f;
            }
        }
    }

    void EnterSwim()
    {
        if (swimming) return;

        // splash de entrada: mais forte quanto mais rápido o corpo bateu na lâmina
        float surfaceY = InfiniteTerrain.Instance != null
            ? InfiniteTerrain.Instance.WaterLevel : transform.position.y;
        ImpactEffects.Emit(new ImpactEvent
        {
            kind = ImpactKind.WaterEntry,
            position = new Vector3(transform.position.x, surfaceY, transform.position.z),
            normal = Vector3.up,
            velocity = flying ? flightVel : Vector3.down * Mathf.Abs(verticalVel),
            strength01 = Mathf.Clamp01(Mathf.Abs(flying ? flightVel.y : verticalVel) / 14f),
            scale = VfxScale,
        });

        swimming = true;
        flying = false;
        gliding = false;
        stealth = false;
        SetStall(false);
        fallFromY = float.NaN;      // água amortece: queda não machuca
        verticalVel = 0f;
        vertInput = 0f;
        planarSpeed = Mathf.Min(Mathf.Abs(planarSpeed), swimSpeed * S);
        anim.SetBool(P_Swim, true);
        anim.SetBool(P_Flying, false);
        anim.SetBool(P_Glide, false);
    }

    void ExitSwim()
    {
        swimming = false;
        anim.SetBool(P_Swim, false);
    }

    // -------------------------------------------------------------- ESTADOS
    /// <summary>`running` = decolagem em corrida: já entra voando pra frente,
    /// sem a fase de salto (o Animator vai direto Locomotion→Fly pelo Speed).</summary>
    void EnterFlight(bool running)
    {
        flying = true;
        gliding = false;
        stealth = false;
        SetStall(false);
        takeoffTime = Time.time;
        flyDodgeUntil = -99f;      // decolagem nova nunca herda uma esquiva pendente
        flySpeed = running ? Mathf.Max(planarSpeed, cruiseSpeed * 0.8f * S)
                           : Mathf.Max(planarSpeed, (minFlySpeed + 2f) * S);
        vertInput = running ? 0.35f : 1f;
        // parado, este é só o SALTO — o impulso de verdade vem do bote das asas
        // no fim do clipe (TakeoffLaunch). Correndo, as asas já abrem em movimento.
        flight?.OnEnterFlight((running ? 5f : takeoffHopClimb) * LiftMul);
        anim.SetBool(P_Flying, true);
        anim.SetBool(P_Glide, false);
    }

    void Land()
    {
        // Dano SÓ por altura de queda (ResolveFall), e ANTES de flying=false: o
        // guard de OnDamaged suprime a reação "Get Hit" — a queda já foi contada
        // por Stall Fall + Land, reagir de novo em pé parecia um espasmo.
        bool wasStalling = stalling;
        float severity = ResolveFall();
        if (wasStalling) Lock(1.2f, LockKind.Stagger);

        ImpactEffects.Emit(new ImpactEvent
        {
            kind = severity > 0f ? ImpactKind.HardLanding : ImpactKind.Landing,
            position = transform.position,
            normal = Vector3.up,
            velocity = flightVel,
            strength01 = severity > 0f ? severity
                       : Mathf.Clamp01(flySpeed / Mathf.Max(1f, maxFlySpeed * S)),
            scale = VfxScale,
        });
        if (severity > 0f) DragonCamera.Instance?.AddShake(0.25f * severity);

        flying = false;
        gliding = false;
        diving = false;
        SetStall(false);

        // toca o chão CORRENDO: pouso rápido vira corrida e desacelera sozinho
        // (aterrissagem inteligente — nunca a parada brusca). Queda feia estanca.
        planarSpeed = wasStalling || severity > 0f
            ? Mathf.Min(flySpeed, walkSpeed * S)
            : Mathf.Min(flySpeed, EffRunSpeed);
        verticalVel = -4f;
        vertInput = 0f;
        anim.SetBool(P_Flying, false);
        anim.SetBool(P_Glide, false);
    }

    void SetStall(bool value)
    {
        stalling = value;
        anim.SetBool(P_Falling, value);
    }

    void OnStageUp(DragonGrowth.LifeStage newStage)
    {
        if (dead || flying || resting || swimming) return;
        if (newStage == DragonGrowth.LifeStage.Elder) return;  // a velhice não se comemora
        anim.SetTrigger(P_Roar);          // celebra crescer de fase rugindo
        Lock(2.4f);
    }

    public void OnDamaged(float amount)
    {
        if (dead || amount < 2f) return;
        if (flying)
        {
            // acusar o golpe no ar: rolagem breve + tranco de sustentação, sem
            // travar o voo (o Get Hit completo só existe no chão). Em estol ou
            // desequilíbrio o dragão já está tombando — reagir de novo é espasmo.
            if (stalling || IsStaggered) return;
            flinchTime = Time.time;
            flinchDir = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            flight?.Knock(-Mathf.Min(3f, amount * 0.12f));
            return;
        }
        anim.SetFloat(P_HitVar, UnityEngine.Random.Range(0, 4));
        anim.SetTrigger(P_Hit);
        Lock(0.8f, LockKind.Stagger);
    }

    void Die()
    {
        dead = true;
        resting = false;
        flying = false;
        swimming = false;
        stealth = false;
        anim.SetBool(P_Swim, false);
        anim.SetFloat(P_DeathVar, UnityEngine.Random.Range(0, 2));
        anim.SetTrigger(P_Die);
    }

    /// <summary>Ressuscita o avatar para uma nova possessão (troca de dragão da Base):
    /// zera a morte e o estado volátil de voo/lock e volta o Animator ao default. A
    /// skin e os stats vêm da DragonPossession (LoadFrom nos componentes).</summary>
    public void Revive()
    {
        dead = flying = gliding = stalling = resting = swimming = stealth = diving = false;
        planarSpeed = flySpeed = verticalVel = vertInput = 0f;
        actionLockUntil = staggerUntil = dashUntil = flyDodgeUntil = -99f;
        takeoffTime = -99f;
        inStationaryTakeoff = false;
        fallFromY = float.NaN;
        pitch = roll = 0f;
        anim.Rebind();          // limpa o Death e volta ao estado default (Locomotion)
        anim.Update(0f);
    }

    bool Spend(float amount) => vitals == null || vitals.TrySpend(amount);

    void Lock(float seconds, LockKind kind = LockKind.Action)
    {
        lockStart = Time.time;
        actionLockUntil = Time.time + seconds;
        lockKind = kind;
    }

    /// <summary>Encerra o lock atual (cancel por dash/decolagem).</summary>
    void CancelLock() => actionLockUntil = Time.time;

    /// <summary>Escala corporal efetiva (crescimento × atributos) — combate usa
    /// para dimensionar dano, alcance e origem dos projéteis.</summary>
    public float BodyScale => S;

    /// <summary>Trava ações por alguns segundos (habilidades 1-4). É um lock de
    /// GOLPE: dash e decolagem cancelam após a janela — profundidade mecânica.</summary>
    public void LockActions(float seconds) => Lock(seconds, LockKind.Attack);

    /// <summary>Golpe corpo a corpo atinge a fauna viva (caça de verdade — GDD).
    /// Presas abatidas viram carcaças que se comem com G. Acertou: hit stop +
    /// micro-shake — o impacto tem PESO (hack and slash).</summary>
    void StrikeWildlife(float forwardOffset, float radius, float damage)
    {
        Vector3 p = transform.position + transform.forward * (forwardOffset * S);
        if (AnimalAgent.DamageNearest(p, radius * S, damage * S, transform))
        {
            HitStop.Hit();
            DragonCamera.Instance?.AddShake(0.12f);
        }
    }

    // -------------------------------------------------------- VISUAL / ANIM
    void ApplyRotation(float dt, float h)
    {
        float targetPitch = flying ? -vertInput * pitchAngle : 0f;
        float targetRoll = flying ? -h * bankAngle : 0f;

        pitch = Mathf.LerpAngle(pitch, targetPitch, 4f * dt);
        roll = Mathf.LerpAngle(roll, targetRoll, 4f * dt);

        // flinch de dano em voo: meia-onda de rolagem (0 no início e no fim)
        float flinch = 0f;
        float ft = (Time.time - flinchTime) / FlinchDuration;
        if (ft < 1f) flinch = flinchDir * FlinchAngle * Mathf.Sin(ft * Mathf.PI);

        transform.rotation = Quaternion.Euler(pitch, yaw, roll + flinch);
    }

    void UpdateAnimator(float dt, float h)
    {
        // no chão o "Turn" vem do giro REAL do corpo (movimento câmera-relativo);
        // no ar e na água continua vindo do input (A/D giram)
        float turnTarget;
        if (flying || swimming) turnTarget = h;
        else
        {
            float yawRate = Mathf.DeltaAngle(prevYaw, yaw) / Mathf.Max(dt, 0.0001f);
            turnTarget = Mathf.Clamp(yawRate / 180f, -1f, 1f);
        }
        prevYaw = yaw;
        turnSmoothed = Mathf.MoveTowards(turnSmoothed, turnTarget, 6f * dt);
        anim.SetFloat(P_Turn, turnSmoothed);

        float animSpeed;
        if (swimming)
            animSpeed = planarSpeed >= 0f
                ? planarSpeed / (swimSpeed * S) * 2f          // 2 = nado rápido no blend
                : -Mathf.InverseLerp(0f, swimBackSpeed * S, -planarSpeed);
        else
            animSpeed = planarSpeed >= 0f
                ? planarSpeed / EffRunSpeed * 3f
                : -Mathf.InverseLerp(0f, reverseSpeed * S, -planarSpeed);
        anim.SetFloat(P_Speed, animSpeed, 0.12f, dt);

        anim.SetFloat(P_Vertical, vertInput, 0.15f, dt);
        anim.SetBool(P_Glide, flying && gliding && !stalling);
        if (hasStealthParam) anim.SetBool(P_Stealth, stealth);
    }
}
