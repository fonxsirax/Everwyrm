using UnityEngine;

/// <summary>
/// Um ataque do dragão como DADO — mesmo padrão data-driven do AnimalDefinition:
/// criar um asset novo em Resources/Attacks = ataque novo no jogo, sem tocar em
/// código. O DragonAbilities carrega todos no Play e desbloqueia por nível.
///
/// MELEE × RANGED — a combinação das flags define o comportamento:
///  · usesProjectile = false, areaDamage = false → MELEE single target
///    (fere o animal mais próximo à frente, dentro de "range")
///  · usesProjectile = false, areaDamage = true  → MELEE em área
///    (explosão ao redor do dragão — ex.: Incinerate)
///  · usesProjectile = true,  explodesOnImpact = false → RANGED single target
///    (projétil atinge UM inimigo — ex.: FireBall)
///  · usesProjectile = true,  explodesOnImpact = true  → RANGED em área
///    (projétil explode no impacto — ex.: Great Fire Ball)
///  appliesBurn / spawnsFireArea compõem com qualquer combinação acima.
///
/// Menu: Create > Everwyrm > Dragon Attack
/// </summary>
[CreateAssetMenu(menuName = "Everwyrm/Dragon Attack", fileName = "NovoAtaque")]
public class DragonAttackData : ScriptableObject
{
    [Header("Identidade")]
    public string attackName = "Novo Ataque";
    [TextArea] public string description = "";
    public Sprite icon;                        // opcional — sem ele o HUD mostra as iniciais

    [Header("Desbloqueio")]
    // O nome do campo é contrato de serialização (assets já gravados) — o que
    // mudou é o significado: hoje é o DEGRAU DE MATURIDADE (DragonAttributes.Tier,
    // 1..10), que o dragão sobe crescendo e envelhecendo, não gastando pontos.
    [Tooltip("Degrau de maturidade (DragonAttributes.Tier, 1..10) em que o ataque " +
             "destrava. Ao destravar ele entra sozinho no primeiro slot livre (1-4).")]
    public int unlockLevel = 2;

    [Header("Custos e Tempos")]
    public float energyCost = 10f;
    public float baseDamage = 15f;             // multiplicado por Poder (DamageMul) e escala
    public float cooldown = 3f;
    [Tooltip("Segundos entre o início da animação e o efeito disparar (dano/projétil).")]
    public float castTime = 0.5f;
    [Tooltip("Tempo total em que o dragão fica travado executando o ataque.")]
    public float animationLock = 1.2f;

    [Header("Animação (clips do Unka — número finito)")]
    public DragonAttackAnimation animation = DragonAttackAnimation.BiteFront;

    [Header("Regras")]
    public bool usableInFlight = false;
    public bool isPhysical = true;
    public bool isFire = false;                // Poder também amplia o raio (FlameSizeMul)

    [Header("Alcance / Área")]
    [Tooltip("Melee: raio de acerto à frente. Projétil: distância máxima percorrida.")]
    public float range = 4f;
    [Tooltip("Causa dano em ÁREA (explosão, Incinerate) em vez de um alvo só.")]
    public bool areaDamage = false;
    public float areaRadius = 6f;

    [Header("Incêndio")]
    [Tooltip("Alvos atingidos pegam fogo: dano de queimadura contínuo (BurningStatus).")]
    public bool appliesBurn = false;
    public float burnDamagePerSecond = 4f;
    public float burnDuration = 5f;
    [Tooltip("Deixa uma ÁREA DE FOGO temporária no chão — quem entrar nela pega fogo.")]
    public bool spawnsFireArea = false;
    public float fireAreaDuration = 8f;

    [Header("Projétil")]
    public bool usesProjectile = false;
    public float projectileSpeed = 30f;
    [Tooltip("Vida do projétil em segundos. 0 = derivada de range ÷ velocidade.")]
    public float projectileLifetime = 0f;
    [Tooltip("Explode ao colidir, aplicando o dano em área acima (Great Fire Ball).")]
    public bool explodesOnImpact = false;
    [Tooltip("Raio de acerto do projétil — atinge apenas UM inimigo por vez " +
             "(detecção por distância, o padrão de combate do projeto).")]
    public float projectileHitRadius = 1.2f;

    [Header("Prefabs (opcionais — sem eles o efeito é gerado por código)")]
    public GameObject projectilePrefab;        // visual do projétil (filho do GO em voo)
    public GameObject vfxPrefab;               // explosão / área de fogo / impacto

    public float EffectiveLifetime =>
        projectileLifetime > 0.01f ? projectileLifetime
                                   : range / Mathf.Max(1f, projectileSpeed);
}

/// <summary>
/// Animações de ataque existentes no "Dragon Player.controller" (DragonSetup).
/// Enum fechado de propósito: os clipes do Unka são um conjunto finito.
/// </summary>
public enum DragonAttackAnimation
{
    BiteFront,      // UAttack Bite Front
    Bite2,          // UAttack Bite 2
    ClawsLeft,      // UAttack Claws L
    ClawsRight,     // UAttack Claws R
    TailLeft,       // UAttack Tail L
    TailRight,      // UAttack Tail R
    WingLeft,       // UAttack Wing L
    WingRight,      // UAttack Wings R
    FireBreath,     // Fire Breath (UAttack FireBreath L)
    Roar            // Roar (U ROAR)
}
