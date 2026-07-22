using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Controlador do dragão (Unka) — chão, decolagem, voo, pouso, combate e sobrevivência.
///
/// CONTROLES
///  Chão : W/S anda · A/D vira · corrida é AUTOMÁTICA: andando pra frente o dragão
///         acelera até correr (dragão gordo/grande demora mais — peso!)
///         Space parado = bate as asas · Space andando = DECOLA
///         LMB combo (mordida/garras) · Q cauda · E asas · F fogo · T rugido
///         Alt esquiva · R descansar · G comer carcaça próxima
///  Voo  : W acelera · S freia (SEGURE p/ pousar quando houver chão) · A/D vira
///         Space sobe — soltar perto do fim da batida dá impulso extra (timing!)
///         Ctrl/C mergulha
///         Alt esquiva aérea · Sem bater asas ~1s = planar
///         Colisão tem consequência FÍSICA (sem dano): raspão desvia e freia;
///         batida forte derruba a sustentação — desequilíbrio até recuperar.
///         Voando rente ao chão sem pedir altura, pousa sozinho (nada de raspar).
///         Peso importa: gordo sobe mal e afunda planando; grande plana melhor
///         Energia zerada = estol e queda!
///         DANO só de QUEDA: acima de ~70% da maior árvore da floresta.
///  Água : voo rasante sobre lago/rio faz spray e ondulações e NÃO derruba —
///         só encostar na lâmina vira nado (queda amortecida, sem dano).
///         Entra andando em lago fundo ou pousando na água.
///         W/S nada · A/D vira · Space decola da água. Gordo nada mais devagar.
///  Morto: Enter renasce.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class DragonController : MonoBehaviour
{
    [Header("Chão")]
    [SerializeField] float walkSpeed = 3.84f;   // +20% geral na locomoção terrestre
    [SerializeField] float runSpeed = 11.4f;
    [SerializeField] float reverseSpeed = 1.7f;
    [SerializeField] float groundAccel = 9f;
    [SerializeField] float momentumDecay = 1.6f;   // freio ao soltar W
    [SerializeField] float turnSpeedGround = 120f;
    [SerializeField] float gravity = 28f;
    [SerializeField] float hopImpulse = 6.5f;

    [Header("Decolagem")]
    [SerializeField] float takeoffMinSpeed = 1.5f;

    [Header("Voo")]
    [SerializeField] float minFlySpeed = 6f;
    [SerializeField] float cruiseSpeed = 14f;
    [SerializeField] float maxFlySpeed = 26f;
    [SerializeField] float flyAccel = 9f;
    [SerializeField] float turnSpeedAir = 75f;
    [SerializeField] float bankAngle = 42f;
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
    [SerializeField] float hopCost = 4f;
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
    static readonly int P_Flap       = Animator.StringToHash("Flap");
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

    CharacterController cc;
    Animator anim;
    DragonVitals vitals;                 // opcional
    DragonGrowth growth;                 // opcional
    DragonAttributes attrs;              // opcional
    DragonFlight flight;                 // opcional (voo skill-based)

    bool flying, gliding, stalling, resting, dead, swimming;
    float planarSpeed, flySpeed, verticalVel;
    float momentum;                      // 0..1 — corrida automática
    float yaw, pitch, roll;
    float turnSmoothed, vertInput;
    float lastFlapTime, takeoffTime = -99f;
    float actionLockUntil;
    Vector3 flightVel, pendingNormal, pendingPoint;  // colisão em voo
    Collider pendingCollider;
    float pendingImpact, lastImpactTime = -99f, staggerUntil = -99f;
    float fallFromY = float.NaN;                     // Y do início da queda DESCONTROLADA
    float flinchTime = -99f, flinchDir = 1f;         // "acusar o golpe" em voo
    const float FlinchDuration = 0.45f;
    const float FlinchAngle = 9f;                    // graus de rolagem no pico
    int meleeCombo;
    bool tailLeft, wingLeft;
    float nextIdleChange, nextHintCheck;
    string currentHint = "";

    public bool IsFlying => flying;
    public bool ActionsLocked => Locked;                   // p/ DragonAbilities
    public bool IsStaggered => Time.time < staggerUntil;   // desequilíbrio pós-colisão
    public bool IsResting => resting;
    public bool IsDead => dead;
    public bool IsSwimming => swimming;
    public float MaxGroundSpeed => EffRunSpeed;          // p/ menu de atributos
    public float MaxFlightSpeed => maxFlySpeed * S;
    public float TimeToRun => AccelTime;
    public float Speed01 => flying ? Mathf.InverseLerp(0f, maxFlySpeed * S, flySpeed)
                                   : Mathf.InverseLerp(0f, EffRunSpeed, Mathf.Abs(planarSpeed));

    /// <summary>Observer: dica contextual para o HUD ("G — comer" etc.).</summary>
    public event Action<string> OnHintChanged;

    bool Locked => Time.time < actionLockUntil;
    bool Exhausted => vitals != null && vitals.IsExhausted;

    // ---- Peso/tamanho (DragonGrowth) + atributos — neutros se não existirem
    float AttrSpeed => attrs != null ? attrs.SpeedMul : 1f;   // Velocidade
    float AttrAccel => attrs != null ? attrs.AccelMul : 1f;   // Velocidade + Resistência
    float S => (growth != null ? growth.SpeedScale : 1f) * AttrSpeed;
    float CostMul => growth != null ? growth.EnergyCostMul : 1f;
    float AccelTime => (growth != null ? growth.AccelTime : 2.5f) / AttrAccel;
    float RunMul => growth != null ? growth.RunSpeedMul : 1f;
    float ClimbMul => growth != null ? growth.ClimbMul : 1f;
    float SinkMul => growth != null ? growth.SinkMul : 1f;
    float EffRunSpeed => runSpeed * RunMul * S;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        anim = GetComponent<Animator>();
        vitals = GetComponent<DragonVitals>();
        growth = GetComponent<DragonGrowth>();
        attrs = GetComponent<DragonAttributes>();
        flight = GetComponent<DragonFlight>();
        if (flight == null) flight = gameObject.AddComponent<DragonFlight>(); // garante o módulo de voo
        if (GetComponent<DragonSounds>() == null)
            gameObject.AddComponent<DragonSounds>(); // receptor dos AnimationEvents "PlaySound" dos FBX
        if (vitals != null && GetComponent<DragonDamageFeedback>() == null)
            gameObject.AddComponent<DragonDamageFeedback>(); // shake/vinheta/som de dano
        anim.applyRootMotion = false;
        yaw = transform.eulerAngles.y;

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

        if (dead)
        {
            if (!cc.isGrounded) cc.Move(Vector3.down * 10f * dt);
            if (Input.GetKeyDown(KeyCode.Return))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        float h = Locked ? 0f : Input.GetAxis("Horizontal");
        float v = Locked ? 0f : Input.GetAxis("Vertical");

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

        // ---- corrida automática: momentum cresce andando pra frente.
        //      Peso manda: gordo/grande demora mais para engatar a corrida.
        if (v > 0.1f && !Exhausted)
            momentum = Mathf.Min(1f, momentum + dt / AccelTime);
        else
            momentum = Mathf.Max(0f, momentum - momentumDecay * dt);

        float topSpeed = Mathf.Lerp(walkSpeed, runSpeed * RunMul, momentum * momentum);
        float target = v > 0.01f ? v * topSpeed
                     : v < -0.01f ? v * reverseSpeed
                     : 0f;
        target *= S;
        planarSpeed = Mathf.MoveTowards(planarSpeed, target, groundAccel * S * dt);

        if (planarSpeed > (walkSpeed + 0.5f) * S) vitals?.Drain(runCost * CostMul);

        yaw += h * turnSpeedGround * dt;

        if (grounded)
        {
            // pisou o chão: cobra a altura se veio de uma queda de verdade
            // (despencar de um penhasco andando conta igual a cair voando)
            float severity = ResolveFall();
            if (severity > 0f)
            {
                Lock(1.2f);
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

        if (!Locked && Input.GetKeyDown(KeyCode.Space))
        {
            if (grounded && v > 0.1f && planarSpeed >= takeoffMinSpeed * S)
            {
                if (Spend(takeoffCost * CostMul)) EnterFlight();
            }
            else if (grounded)
            {
                if (Spend(hopCost * CostMul))
                {
                    verticalVel = hopImpulse;
                    anim.SetTrigger(P_Flap);
                }
            }
            else if (Spend(takeoffCost * CostMul)) EnterFlight();
        }

        if (grounded && Mathf.Abs(planarSpeed) < 0.1f && Time.time > nextIdleChange)
        {
            nextIdleChange = Time.time + UnityEngine.Random.Range(6f, 12f);
            anim.SetFloat(P_IdleVar, UnityEngine.Random.Range(0, 3));
        }

        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        cc.Move((fwd * planarSpeed + Vector3.up * verticalVel) * dt);

        // andou até água funda: começa a nadar
        if (DeepWaterAt(transform.position, out float surface) &&
            transform.position.y < surface)
            EnterSwim();
    }

    // ------------------------------------------------------------------- VOO
    // Voo como habilidade: 1 toque de Space = 1 batida, 2 por ciclo (DragonFlight).
    // Entre batidas: planeio com sustentação pela velocidade; updrafts ajudam.
    void FlightUpdate(float dt, float h, float v)
    {
        bool diving = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);

        float s = S;
        ProcessFlightImpact(s);   // consequências da colisão do frame anterior

        float target = cruiseSpeed * s;
        if (v > 0.1f) target = Mathf.Lerp(cruiseSpeed, maxFlySpeed, v) * s;
        else if (v < -0.1f) target = Mathf.Lerp(cruiseSpeed, minFlySpeed, -v) * s;
        if (diving) target = maxFlySpeed * s;
        if (stalling) target = minFlySpeed * s;
        flySpeed = Mathf.MoveTowards(flySpeed, target, flyAccel * s * dt);

        float turnFactor = Mathf.Lerp(1.25f, 0.8f, Speed01);
        yaw += h * turnSpeedAir * turnFactor * dt;

        // ---- pouso controlado: segurar S = descida DECIDIDA rumo ao solo.
        //      Alto (sem chão no alcance da sondagem) desce na taxa cheia; com o
        //      chão à vista faz o gradiente até o "flare" suave do toque, e as
        //      condições de pouso abaixo completam a transição naturalmente.
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

        // ---- ciclo de batidas de asa
        bool flapPressed = !Locked && Input.GetKeyDown(KeyCode.Space);
        bool held = Input.GetKey(KeyCode.Space);
        float vy = flight != null
            ? flight.Tick(dt, flapPressed, held, diving, Speed01, s,
                          transform.position, ref flySpeed, landingSink)
            : Time.time - takeoffTime < 0.9f ? climbRate * s     // fallback sem módulo
            : diving ? -diveRate * s : -glideSink * SinkMul;

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

        // pitch e animação seguem o movimento vertical REAL
        vertInput = Mathf.MoveTowards(vertInput,
            Mathf.Clamp(vy / Mathf.Max(1f, climbRate * s), -1f, 1f), 5f * dt);

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

        // carência maior pós-decolagem e pouso só em DESCIDA REAL (vy < -1.5):
        // o afundamento suave do planeio rápido (~-0.9) não força pouso.
        // Com intenção de pouso (S + chão perto), o flare gentil também conta.
        if (Time.time - takeoffTime < 1.5f) return;
        if (cc.isGrounded) { Land(); return; }

        // ---- POUSO AUTOMÁTICO: voando rente ao chão sem intenção de subir, o
        //      dragão pousa de verdade em vez de "raspar" o terreno voando.
        //      Nunca contraria o jogador: segurar Space, qualquer subida real ou
        //      a fase de decolagem cancelam. Só em chão pousável e fora da água
        //      (sobre lago o voo rasante é livre — é o skim visual acima).
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
        if (Time.time - takeoffTime < 1.5f) return;   // carência pós-decolagem

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
            Lock(staggerTime);
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
        vitals?.Damage(fallDamageAtDouble * excess);
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

        if (Input.GetKeyDown(KeyCode.LeftAlt) && Spend(dodgeCost * CostMul))
        {
            float dir = Input.GetAxisRaw("Horizontal");
            anim.SetFloat(P_DodgeDir, dir < -0.01f ? -1f : 1f);
            anim.SetTrigger(P_Dodge);
            Lock(0.7f);
            return;
        }

        if (!grounded) return;

        if (Input.GetKeyDown(KeyCode.G))                            // comer
        {
            var food = Carcass.FindNearest(transform.position, eatRange * S);
            if (food != null)
            {
                anim.SetInteger(P_AttackType, 0);                   // mordida
                anim.SetTrigger(P_Attack);
                Lock(1.0f);
                float n = food.Consume();
                vitals?.Eat(n);
                growth?.NotifyAte(n);
            }
        }
        else if (Input.GetMouseButtonDown(0) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, meleeCombo);
            anim.SetTrigger(P_Attack);
            meleeCombo = (meleeCombo + 1) % 4;
            Lock(1.0f);
            StrikeWildlife(2.2f, 3.0f, 16f);
        }
        else if (Input.GetKeyDown(KeyCode.Q) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, tailLeft ? 4 : 5);
            tailLeft = !tailLeft;
            anim.SetTrigger(P_Attack);
            Lock(1.2f);
            StrikeWildlife(0f, 4.0f, 12f);      // cauda varre ao redor
        }
        else if (Input.GetKeyDown(KeyCode.E) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, wingLeft ? 6 : 7);
            wingLeft = !wingLeft;
            anim.SetTrigger(P_Attack);
            Lock(1.1f);
            StrikeWildlife(1.2f, 3.5f, 10f);
        }
        else if (Input.GetKeyDown(KeyCode.F) && Spend(fireCost * CostMul))
        {
            anim.SetTrigger(P_Fire);
            Lock(1.9f);
        }
        else if (Input.GetKeyDown(KeyCode.T))
        {
            anim.SetTrigger(P_Roar);
            Lock(2.4f);
        }
        else if (Input.GetKeyDown(KeyCode.R) && Mathf.Abs(planarSpeed) < 0.5f)
        {
            resting = true;
            anim.SetBool(P_Rest, true);
        }
    }

    void UpdateHint()
    {
        if (Time.time < nextHintCheck) return;
        nextHintCheck = Time.time + 0.25f;

        string hint = "";
        if (flying && flight != null && flight.CurrentWind.y > 1.5f)
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
        momentum = 0f;
        if (cc.isGrounded) verticalVel = -4f;
        cc.Move(Vector3.up * verticalVel * Time.deltaTime);

        if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space) ||
            Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
            Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f)
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
        // gordo nada pior (mesma penalidade da corrida); exausto se arrasta
        float mul = RunMul * (Exhausted ? 0.55f : 1f);
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

        vitals?.Drain(swimCost * CostMul * (Mathf.Abs(planarSpeed) > 0.3f ? 1f : 0.35f));

        // Space: decola da água (explosão de asas — custa um pouco mais)
        if (!Locked && Input.GetKeyDown(KeyCode.Space) &&
            Spend(takeoffCost * 1.3f * CostMul))
        {
            ExitSwim();
            EnterFlight();
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
        SetStall(false);
        fallFromY = float.NaN;      // água amortece: queda não machuca
        momentum = 0f;
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
    void EnterFlight()
    {
        flying = true;
        gliding = false;
        SetStall(false);
        takeoffTime = Time.time;
        lastFlapTime = Time.time;
        flySpeed = Mathf.Max(planarSpeed, (minFlySpeed + 2f) * S);
        vertInput = 1f;
        flight?.OnEnterFlight(9f * S);   // impulso de decolagem + ciclo de batidas cheio
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
        if (wasStalling) Lock(1.2f);

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

        flying = false;
        gliding = false;
        SetStall(false);

        // toca o chão CORRENDO: um pouso rápido vira corrida e desacelera sozinho
        // (cortar direto para caminhada dava um solavanco). Queda feia estanca.
        if (wasStalling || severity > 0f)
        {
            planarSpeed = Mathf.Min(flySpeed, walkSpeed * S);
            momentum = 0f;
        }
        else
        {
            planarSpeed = Mathf.Min(flySpeed, EffRunSpeed);
            momentum = Mathf.Clamp01(planarSpeed / Mathf.Max(0.01f, EffRunSpeed));
        }
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
        Lock(0.8f);
    }

    void Die()
    {
        dead = true;
        resting = false;
        flying = false;
        swimming = false;
        anim.SetBool(P_Swim, false);
        anim.SetFloat(P_DeathVar, UnityEngine.Random.Range(0, 2));
        anim.SetTrigger(P_Die);
    }

    bool Spend(float amount) => vitals == null || vitals.TrySpend(amount);
    void Lock(float seconds) => actionLockUntil = Time.time + seconds;

    /// <summary>Escala corporal efetiva (crescimento × atributos) — combate usa
    /// para dimensionar dano, alcance e origem dos projéteis.</summary>
    public float BodyScale => S;

    /// <summary>Trava ações/entradas por alguns segundos (habilidades 1-4).</summary>
    public void LockActions(float seconds) => Lock(seconds);

    /// <summary>Golpe corpo a corpo atinge a fauna viva (caça de verdade — GDD).
    /// Presas abatidas viram carcaças que se comem com G, como sempre.</summary>
    void StrikeWildlife(float forwardOffset, float radius, float damage)
    {
        Vector3 p = transform.position + transform.forward * (forwardOffset * S);
        AnimalAgent.DamageNearest(p, radius * S, damage * S, transform);
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
        turnSmoothed = Mathf.MoveTowards(turnSmoothed, h, 4f * dt);
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
    }
}
