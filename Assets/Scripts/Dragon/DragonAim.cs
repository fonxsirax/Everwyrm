using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MODO MIRA do dragão (tecla E, ver <see cref="DragonInput.AimToggleDown"/>).
///
/// Enquanto ativo: o cursor vira uma RETÍCULA que segue o mouse, a CABEÇA do dragão
/// segue o ponto do mundo sob a retícula (head-look manual — o rig é Generic, então
/// não dá para IK de Animator), a CÂMERA vai pro ombro (<see cref="DragonCamera"/>) e
/// as AÇÕES (sopro/habilidades, ver <see cref="DragonAbilities"/>) miram no alvo.
///
/// Os NÚMEROS moram no <see cref="AimProfile"/> (Balance/DragonAim). O componente é
/// criado sozinho pelo <see cref="DragonController"/> se faltar (igual ao DragonFlight),
/// então não precisa reconfigurar o prefab.
/// </summary>
[RequireComponent(typeof(DragonController))]
public class DragonAim : MonoBehaviour
{
    [Tooltip("Vazio = o asset padrão Balance/DragonAim. Arraste outro AimProfile para " +
             "dar a ESTE dragão/espécie uma mira própria.")]
    [SerializeField] AimProfile aimProfile;

    // PREGUIÇOSO como os outros profiles; se o asset ainda não existe, cai num
    // AimProfile em MEMÓRIA com os defaults dos campos — a mira funciona na hora e
    // passa a ler o asset assim que ele for criado (Recriar Assets Faltantes).
    AimProfile cfgCache;
    AimProfile cfg => cfgCache != null ? cfgCache
        : (cfgCache = Balance.TryDefault<AimProfile>() ?? aimProfile ?? ScriptableObject.CreateInstance<AimProfile>());

    DragonController dragon;
    Camera cam;
    Transform head;                 // bone da cabeça (rig Generic — achado por nome)

    // Cadeia do PESCOÇO (Neck..Neck4) + a cabeça, da BASE até a ponta. O head-look
    // distribui a mira por todos: a cabeça sozinha não basta — os bones do pescoço
    // continuam tocando o balanço do idle/andar por baixo e a cabeça "flutua" (bug no
    // chão). 'restToBody' guarda a rotação de bind de cada bone RELATIVA ao corpo: o
    // neutro ESTÁVEL sobre o qual a mira gira, sem herdar a animação de pescoço/cabeça.
    Transform[] lookChain;
    Quaternion[] restToBody;

    int aimMaskEff;                 // aimMask do profile SEM a layer do próprio dragão

    // ---- retícula (UI própria, criada por código como os outros HUDs)
    Canvas reticleCanvas;
    RectTransform reticle;
    Graphic[] reticleParts;

    bool active;
    Vector3 aimPoint;               // ponto do mundo sob a retícula
    AnimalAgent locked;             // alvo agarrado (só no modo SoftLock)
    float headWeight;               // blend 0↔1 do head-look
    Vector3 headDir;                // direção suavizada da cabeça (mundo)

    /// <summary>Mira ligada AGORA?</summary>
    public bool Active => active;
    /// <summary>Liga/desliga a mira POR CÓDIGO (ex.: <see cref="DragonAbilities"/> abre o
    /// modo mira quando uma habilidade de projétil com <c>aimBeforeFire</c> é acionada).
    /// Reusa exatamente o mesmo caminho da tecla E — câmera de ombro, retícula e cursor.</summary>
    public void SetAimActive(bool on) => SetActive(on);
    /// <summary>Ponto do mundo sob a retícula.</summary>
    public Vector3 AimPoint => aimPoint;
    /// <summary>Animal agarrado pelo soft-lock (null quando não há fauna perto do ponto).</summary>
    public AnimalAgent LockedAgent => locked;
    /// <summary>Onde as ações devem acertar: o alvo agarrado, senão o ponto da retícula.</summary>
    public Vector3 AimTargetPoint => locked != null ? locked.transform.position + Vector3.up * 0.6f : aimPoint;
    /// <summary>Direção normalizada de <paramref name="origin"/> até o alvo de mira.</summary>
    public Vector3 AimDirectionFrom(Vector3 origin)
    {
        Vector3 d = AimTargetPoint - origin;
        return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
    }

    // ---- tunables que o DragonController consulta p/ a locomoção de mira no chão
    public float BodyTurnRate => cfg.bodyTurnRate;
    public float StrafeFraction => cfg.strafeFraction;

    void Awake()
    {
        dragon = GetComponent<DragonController>();
        aimMaskEff = cfg.aimMask.value & ~(1 << gameObject.layer);
        headDir = transform.forward;

        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Head") { head = t; break; }   // rig Generic: acha o bone por nome

        BuildLookChain();
    }

    // Monta a cadeia de bones do head-look: a cabeça + todos os ancestrais "Neck*"
    // logo acima dela (para no primeiro que não é pescoço — Spine/Chest). Ordena da
    // BASE até a ponta. Awake roda ANTES da 1ª avaliação do Animator, então cada bone
    // ainda está na pose de bind: guarda a rotação de cada um RELATIVA ao corpo — o
    // neutro estável do head-look.
    void BuildLookChain()
    {
        if (head == null) return;

        var chain = new System.Collections.Generic.List<Transform> { head };
        for (Transform t = head.parent; t != null && t.name.StartsWith("Neck"); t = t.parent)
            chain.Add(t);                       // sobe: Neck4, Neck3, ... Neck
        chain.Reverse();                        // base -> ponta: Neck .. Neck4, Head

        lookChain = chain.ToArray();
        restToBody = new Quaternion[lookChain.Length];
        Quaternion invBody = Quaternion.Inverse(transform.rotation);
        for (int i = 0; i < lookChain.Length; i++)
            restToBody[i] = invBody * lookChain[i].rotation;
    }

    void Update()
    {
        if (DragonStatsMenu.IsOpen) return;   // ficha controla o cursor

        // não entra mirando morto/descansando/nadando; se cair nesses estados, sai
        bool allowed = !dragon.IsDead && !dragon.IsResting && !dragon.IsSwimming;
        if (active && !allowed) SetActive(false);

        if (allowed && DragonInput.AimToggleDown) SetActive(!active);

        if (!active) return;

        Cursor.lockState = CursorLockMode.Confined;   // reafirma todo frame (menu pode ter mexido)
        Cursor.visible = false;

        UpdateAimPoint();
        UpdateReticle();
    }

    void UpdateAimPoint()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        aimPoint = Physics.Raycast(ray, out RaycastHit hit, cfg.aimRayDistance, aimMaskEff,
                                   QueryTriggerInteraction.Ignore)
                 ? hit.point
                 : ray.GetPoint(cfg.aimRayDistance);

        // soft-lock: agarra a fauna perto do ponto (× escala do dragão). Sem alvo
        // por perto, 'locked' fica null e as ações caem no ponto cru da retícula.
        locked = null;
        var a = AnimalAgent.FindNearest(aimPoint, cfg.softLockRadius * dragon.BodyScale);
        if (a != null && !a.IsDead) locked = a;
    }

    // ------------------------------------------------------- head-look (LateUpdate)
    // Depois do Animator: SUBSTITUI a pose do PESCOÇO INTEIRO + cabeça, distribuindo a
    // mira pela cadeia. Não SOMA por cima da animação (antes fazia `delta * head.rotation`
    // e o balanço de cabeça sobrava) nem mexe só na cabeça (o pescoço continuava tocando
    // o idle/andar por baixo e a cabeça "flutuava"). Cada bone parte de uma base ESTÁVEL
    // (sua pose de bind girada com o corpo) e recebe uma FATIA da rotação de mira — do
    // pouco na base ao total na cabeça —, formando um arco de pescoço que fixa no alvo.
    // FromToRotation em vez de LookRotation porque o eixo "frente" do bone Generic é
    // desconhecido: girar o rumo do CORPO até o alvo dispensa saber o eixo local.
    void LateUpdate()
    {
        float target = active ? 1f : 0f;
        float blend = cfg.headBlendTime > 0.001f ? Time.deltaTime / cfg.headBlendTime : 1f;
        headWeight = Mathf.MoveTowards(headWeight, target, blend);
        if (lookChain == null || lookChain.Length == 0 || headWeight <= 0.001f) return;

        Vector3 neutral = transform.forward;                       // rumo neutro = corpo
        Vector3 want = (AimTargetPoint - head.position);
        if (want.sqrMagnitude < 0.0001f) want = neutral; else want.Normalize();

        // clamp num cone à frente do corpo — nada de pescoço torcido 180°
        float ang = Vector3.Angle(neutral, want);
        if (ang > cfg.headMaxAngle)
            want = Vector3.RotateTowards(neutral, want, cfg.headMaxAngle * Mathf.Deg2Rad, 0f).normalized;

        float k = 1f - Mathf.Exp(-cfg.headTurnSpeed * Time.deltaTime);
        headDir = Vector3.Slerp(headDir.sqrMagnitude < 0.0001f ? neutral : headDir.normalized, want, k);

        // rotação total de mira (rumo-do-corpo -> alvo), fatiada pela cadeia
        Quaternion full = Quaternion.FromToRotation(neutral, headDir);
        int n = lookChain.Length;

        // BASE -> ponta: cada bone.rotation é ABSOLUTA, então é preciso fixar o pai
        // antes do filho (o filho guarda local contra o pai já posto). Fração sobe
        // linear (pouco no início do pescoço, total na cabeça): arco suave que fixa
        // no alvo. Blend por headWeight entra/sai da animação em headBlendTime, sem flicker.
        for (int i = 0; i < n; i++)
        {
            float frac = (i + 1f) / n;                                       // ... 1.0 na cabeça
            Quaternion slice = Quaternion.Slerp(Quaternion.identity, full, frac);
            Quaternion restWorld = transform.rotation * restToBody[i];       // neutro estável do bone
            Quaternion aimed = slice * restWorld;
            lookChain[i].rotation = Quaternion.Slerp(lookChain[i].rotation, aimed, headWeight);
        }
    }

    // ---------------------------------------------------------------- liga/desliga
    void SetActive(bool on)
    {
        if (active == on) return;
        active = on;

        if (DragonCamera.Instance != null) DragonCamera.Instance.AimMode = on;
        EnsureReticle();
        if (reticleCanvas != null) reticleCanvas.enabled = on;

        if (on)
        {
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = false;
        }
        else
        {
            locked = null;
            Cursor.lockState = CursorLockMode.Locked;   // volta ao padrão de gameplay
            Cursor.visible = false;
        }
    }

    void OnDisable()
    {
        // some com estado modal se o dragão for despossuído/desativado mirando
        if (active) SetActive(false);
    }

    // ---------------------------------------------------------------- retícula UI
    void UpdateReticle()
    {
        if (reticle == null) return;
        reticle.position = Input.mousePosition;
        Color c = locked != null ? new Color(1f, 0.35f, 0.3f, 0.95f)   // vermelho: alvo agarrado
                                  : new Color(1f, 1f, 1f, 0.85f);
        foreach (var g in reticleParts) if (g != null) g.color = c;
    }

    void EnsureReticle()
    {
        if (reticleCanvas != null) return;

        var go = new GameObject("Aim Reticle");
        go.transform.SetParent(transform, false);
        reticleCanvas = go.AddComponent<Canvas>();
        reticleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        reticleCanvas.sortingOrder = 50;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var root = new GameObject("Crosshair").AddComponent<RectTransform>();
        root.SetParent(reticleCanvas.transform, false);
        reticle = root;

        // 4 tracinhos + ponto central — cruz simples, sem sprite de asset
        reticleParts = new Graphic[]
        {
            Tick(root, new Vector2(16f, 3f), new Vector2(-14f, 0f)),  // esquerda
            Tick(root, new Vector2(16f, 3f), new Vector2( 14f, 0f)),  // direita
            Tick(root, new Vector2(3f, 16f), new Vector2(0f, -14f)),  // baixo
            Tick(root, new Vector2(3f, 16f), new Vector2(0f,  14f)),  // cima
            Tick(root, new Vector2(4f, 4f),  Vector2.zero),           // ponto central
        };
    }

    static Graphic Tick(RectTransform parent, Vector2 size, Vector2 pos)
    {
        var img = new GameObject("Tick").AddComponent<Image>();
        img.rectTransform.SetParent(parent, false);
        img.rectTransform.sizeDelta = size;
        img.rectTransform.anchoredPosition = pos;
        img.raycastTarget = false;
        return img;
    }
}
