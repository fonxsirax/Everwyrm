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
///  Voo  : W acelera · S freia · A/D vira · Space sobe · Ctrl/C mergulha
///         Alt esquiva aérea · Sem bater asas ~1s = planar
///         Peso importa: gordo sobe mal e afunda planando; grande plana melhor
///         Energia zerada = estol e queda!
///  Morto: Enter renasce.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class DragonController : MonoBehaviour
{
    [Header("Chão")]
    [SerializeField] float walkSpeed = 3.2f;
    [SerializeField] float runSpeed = 9.5f;
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
    [SerializeField] LayerMask groundMask = 0;

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

    CharacterController cc;
    Animator anim;
    DragonVitals vitals;                 // opcional
    DragonGrowth growth;                 // opcional
    DragonAttributes attrs;              // opcional

    bool flying, gliding, stalling, resting, dead;
    float planarSpeed, flySpeed, verticalVel;
    float momentum;                      // 0..1 — corrida automática
    float yaw, pitch, roll;
    float turnSmoothed, vertInput;
    float lastFlapTime, takeoffTime = -99f;
    float actionLockUntil;
    int meleeCombo;
    bool tailLeft, wingLeft;
    float nextIdleChange, nextHintCheck;
    string currentHint = "";

    public bool IsFlying => flying;
    public bool IsResting => resting;
    public bool IsDead => dead;
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
        else if (flying) FlightUpdate(dt, h, v);
        else GroundUpdate(dt, h, v);

        if (!resting && !Locked) HandleActions();
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

        if (grounded) verticalVel = -4f;
        else verticalVel -= gravity * dt;

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
    }

    // ------------------------------------------------------------------- VOO
    void FlightUpdate(float dt, float h, float v)
    {
        bool climbing = Input.GetKey(KeyCode.Space) && !Exhausted;
        bool diving = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);

        float s = S;
        float target = cruiseSpeed * s;
        if (v > 0.1f) target = Mathf.Lerp(cruiseSpeed, maxFlySpeed, v) * s;
        else if (v < -0.1f) target = Mathf.Lerp(cruiseSpeed, minFlySpeed, -v) * s;
        if (diving) target = maxFlySpeed * s;
        if (climbing) target *= 0.8f;
        if (stalling) target = minFlySpeed * s;
        flySpeed = Mathf.MoveTowards(flySpeed, target, flyAccel * s * dt);

        float turnFactor = Mathf.Lerp(1.25f, 0.8f, Speed01);
        yaw += h * turnSpeedAir * turnFactor * dt;

        bool takeoffPush = Time.time - takeoffTime < 0.7f;
        float targetVert = climbing || takeoffPush ? 1f : diving || stalling ? -1f : 0f;
        vertInput = Mathf.MoveTowards(vertInput, targetVert, 4f * dt);

        if (climbing || takeoffPush) { lastFlapTime = Time.time; gliding = false; }
        else if (Time.time - lastFlapTime > glideDelay) gliding = true;

        if (Exhausted)
        {
            gliding = true;
            if (!stalling && flySpeed < (minFlySpeed + 1f) * s) SetStall(true);
        }
        else if (stalling) SetStall(false);
        vitals?.Drain((climbing ? climbCost : gliding ? glideCost : flapCost) * CostMul);

        // peso no voo: gordo sobe mal (ClimbMul) e afunda planando (SinkMul);
        // dragão grande plana melhor (SinkMul cai com o tamanho)
        float vy = stalling ? -stallSink
                 : vertInput > 0.01f ? climbRate * ClimbMul * s * vertInput
                 : vertInput < -0.01f ? diveRate * s * vertInput
                 : gliding ? -glideSink * SinkMul : -0.6f;

        Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        cc.Move((fwd * flySpeed + Vector3.up * vy) * dt);

        if (Time.time - takeoffTime < 0.8f) return;
        if (cc.isGrounded) { Land(); return; }

        if (vy < 0f && (stalling || flySpeed <= landMaxSpeed * s))
        {
            Vector3 origin = transform.position + cc.center;
            if (Physics.SphereCast(origin, cc.radius * 0.9f, Vector3.down,
                    out _, landProbeDistance * s + cc.height * 0.5f,
                    groundMask, QueryTriggerInteraction.Ignore))
                Land();
        }
    }

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
        }
        else if (Input.GetKeyDown(KeyCode.Q) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, tailLeft ? 4 : 5);
            tailLeft = !tailLeft;
            anim.SetTrigger(P_Attack);
            Lock(1.2f);
        }
        else if (Input.GetKeyDown(KeyCode.E) && Spend(attackCost * CostMul))
        {
            anim.SetInteger(P_AttackType, wingLeft ? 6 : 7);
            wingLeft = !wingLeft;
            anim.SetTrigger(P_Attack);
            Lock(1.1f);
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
        if (!flying && !resting && !dead)
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
        anim.SetBool(P_Flying, true);
        anim.SetBool(P_Glide, false);
    }

    void Land()
    {
        flying = false;
        gliding = false;
        bool wasStalling = stalling;
        SetStall(false);
        planarSpeed = Mathf.Min(flySpeed, walkSpeed * S);
        momentum = 0f;
        verticalVel = -4f;
        vertInput = 0f;
        anim.SetBool(P_Flying, false);
        anim.SetBool(P_Glide, false);
        if (wasStalling)
        {
            vitals?.Damage(8f);
            Lock(1.2f);
        }
    }

    void SetStall(bool value)
    {
        stalling = value;
        anim.SetBool(P_Falling, value);
    }

    void OnStageUp(DragonGrowth.LifeStage newStage)
    {
        if (dead || flying || resting) return;
        anim.SetTrigger(P_Roar);          // celebra crescer de fase rugindo
        Lock(2.4f);
    }

    public void OnDamaged(float amount)
    {
        if (dead || flying || amount < 2f) return;
        anim.SetFloat(P_HitVar, UnityEngine.Random.Range(0, 4));
        anim.SetTrigger(P_Hit);
        Lock(0.8f);
    }

    void Die()
    {
        dead = true;
        resting = false;
        flying = false;
        anim.SetFloat(P_DeathVar, UnityEngine.Random.Range(0, 2));
        anim.SetTrigger(P_Die);
    }

    bool Spend(float amount) => vitals == null || vitals.TrySpend(amount);
    void Lock(float seconds) => actionLockUntil = Time.time + seconds;

    // -------------------------------------------------------- VISUAL / ANIM
    void ApplyRotation(float dt, float h)
    {
        float targetPitch = flying ? -vertInput * pitchAngle : 0f;
        float targetRoll = flying ? -h * bankAngle : 0f;

        pitch = Mathf.LerpAngle(pitch, targetPitch, 4f * dt);
        roll = Mathf.LerpAngle(roll, targetRoll, 4f * dt);
        transform.rotation = Quaternion.Euler(pitch, yaw, roll);
    }

    void UpdateAnimator(float dt, float h)
    {
        turnSmoothed = Mathf.MoveTowards(turnSmoothed, h, 4f * dt);
        anim.SetFloat(P_Turn, turnSmoothed);

        float animSpeed = planarSpeed >= 0f
            ? planarSpeed / EffRunSpeed * 3f
            : -Mathf.InverseLerp(0f, reverseSpeed * S, -planarSpeed);
        anim.SetFloat(P_Speed, animSpeed, 0.12f, dt);

        anim.SetFloat(P_Vertical, vertInput, 0.15f, dt);
        anim.SetBool(P_Glide, flying && gliding && !stalling);
    }
}
