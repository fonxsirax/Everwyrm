using UnityEngine;

/// <summary>
/// Câmera third-person orbital para o dragão.
///  Mouse = orbitar · Scroll = zoom · Esc = liberar cursor · Clique = travar de novo.
/// Em voo: FOV aumenta com a velocidade e a câmera se alinha atrás do dragão
/// quando o mouse fica parado.
/// </summary>
[RequireComponent(typeof(Camera))]
public class DragonCamera : MonoBehaviour
{
    [Header("Alvo")]
    public Transform target;                 // auto: tag "Player"
    [Header("Balanceamento")]
    [Tooltip("O game feel da câmera vive no asset " +
             "Assets/Scriptables/Resources/Balance/DragonCamera.asset. Vazio = " +
             "esse padrão; arraste outro DragonCameraProfile para outro estilo.")]
    [SerializeField] DragonCameraProfile cameraProfile;

    /// <summary>O perfil resolvido, sob demanda: o campo acima quando preenchido,
    /// senão o asset padrão. PREGUIÇOSO de propósito — a janela de balanceamento
    /// (Tools > Everwyrm > Balanço do Dragão) lê estes números direto no PREFAB,
    /// fora do Play, onde nenhum Awake rodou.</summary>
    DragonCameraProfile cfgCache;
    DragonCameraProfile cfg => cfgCache != null ? cfgCache : (cfgCache = Balance.Resolve(cameraProfile));

    float distance;                                // zoom ATUAL (do profile, depois o scroll manda)
    Camera cam;
    DragonController dragon;
    LayerMask collisionMask;
    float camYaw, camPitch = 15f;
    float lastMouseTime;
    Vector3 smoothPos;
    float trauma;                                  // 0..1 — amplitude = trauma²
    float turnLead, divePitch;                     // antecipação suavizada
    float camTurnRemaining, camTurnRate;            // giro forçado (esquiva) em andamento

    /// <summary>A câmera do dragão — o controller dispara shakes de dash,
    /// decolagem e golpes conectados por aqui.</summary>
    public static DragonCamera Instance { get; private set; }

    /// <summary>Micro-shake de impacto (DragonDamageFeedback). Amplitude cresce
    /// com trauma², então golpes fracos são quase subliminares.</summary>
    public void AddShake(float amount) => trauma = Mathf.Clamp01(trauma + amount);

    /// <summary>Giro forçado da câmera (esquiva de chão/voo): soma graus ao
    /// rumo suavemente ao longo de duration — acompanha o giro do dodge de
    /// volta às costas do dragão sem esperar o auto-align lento do voo.</summary>
    public void SyncTurn(float degrees, float duration)
    {
        camTurnRemaining = degrees;
        camTurnRate = degrees / Mathf.Max(0.01f, duration);
    }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        distance = cfg.distance;
        cam = GetComponent<Camera>();

        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
        }
        if (target != null)
        {
            dragon = target.GetComponentInParent<DragonController>();
            camYaw = target.eulerAngles.y;
            collisionMask = Physics.DefaultRaycastLayers & ~(1 << target.gameObject.layer);
            smoothPos = transform.position;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (DragonStatsMenu.IsOpen) return; // a ficha controla o cursor

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mx) > 0.01f || Mathf.Abs(my) > 0.01f) lastMouseTime = Time.time;
            camYaw += mx * cfg.sensitivity;
            camPitch = Mathf.Clamp(camPitch - my * cfg.sensitivity, cfg.minPitch, cfg.maxPitch);
        }

        // em voo, sem mexer o mouse, alinha atrás do dragão
        if (dragon != null && dragon.IsFlying && Time.time - lastMouseTime > cfg.autoAlignDelay)
        {
            camYaw = Mathf.LerpAngle(camYaw, target.eulerAngles.y, cfg.autoAlignSpeed * dt);
            camPitch = Mathf.Lerp(camPitch, 12f, cfg.autoAlignSpeed * dt);
        }

        // giro forçado da esquiva: acompanha o dragão de volta às costas dele
        if (Mathf.Abs(camTurnRemaining) > 0.01f)
        {
            float step = camTurnRate * dt;
            if (Mathf.Abs(step) > Mathf.Abs(camTurnRemaining)) step = camTurnRemaining;
            camYaw += step;
            camTurnRemaining -= step;
        }

        distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * cfg.zoomSpeed * distance * 0.35f,
                               cfg.minDistance, cfg.maxDistance);

        // ---- antecipação: em curva a câmera adianta na direção do giro; no
        //      mergulho inclina para baixo — a câmera "lê" a intenção do voo
        bool flying = dragon != null && dragon.IsFlying;
        float leadTarget = flying ? Input.GetAxisRaw("Horizontal") * cfg.turnLeadAngle : 0f;
        turnLead = Mathf.Lerp(turnLead, leadTarget, 3f * dt);
        float diveTarget = flying && dragon.IsDiving ? cfg.divePitchBias : 0f;
        divePitch = Mathf.Lerp(divePitch, diveTarget, 3f * dt);

        // acompanha o crescimento do dragão (filhote = câmera perto, colossal = longe)
        float sizeScale = Mathf.Max(0.2f, target.lossyScale.y);

        Vector3 pivot = target.position + Vector3.up * cfg.pivotHeight * sizeScale;
        Quaternion rot = Quaternion.Euler(camPitch + divePitch, camYaw + turnLead, 0f);
        float dist = distance * sizeScale;

        // não atravessar paredes/terreno
        if (Physics.SphereCast(pivot, 0.35f, rot * Vector3.back, out RaycastHit hit,
                dist, collisionMask, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(hit.distance - 0.1f, cfg.minDistance * 0.5f);

        Vector3 desired = pivot + rot * Vector3.back * dist;
        smoothPos = Vector3.Lerp(smoothPos, desired, 1f - Mathf.Exp(-cfg.followLag * dt));
        transform.position = smoothPos;
        transform.rotation = Quaternion.LookRotation(pivot - smoothPos, Vector3.up);

        // shake rotacional por ruído Perlin — sem deslocar a posição (realista)
        if (trauma > 0f)
        {
            trauma = Mathf.Max(0f, trauma - cfg.shakeDecay * dt);
            float amp = trauma * trauma * cfg.maxShakeAngle;
            float t = Time.time * cfg.shakeFrequency;
            transform.rotation *= Quaternion.Euler(
                (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * amp,
                (Mathf.PerlinNoise(0.6f, t) - 0.5f) * 2f * amp,
                (Mathf.PerlinNoise(t, t) - 0.5f) * amp * 0.6f);
        }

        // FOV com sensação de velocidade (+ chute extra no mergulho)
        float targetFov = flying
            ? Mathf.Lerp(cfg.baseFov, cfg.flightFov, dragon.Speed01)
              + (dragon.IsDiving ? cfg.diveFovKick : 0f)
            : cfg.baseFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 3f * dt);
    }
}
