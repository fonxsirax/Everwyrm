using UnityEngine;

/// <summary>
/// Números da locomoção de CHÃO do dragão — passo/corrida, esquiva de chão, cancel
/// de golpes, dano de queda, natação, alimentação e custos de energia de chão/combate.
/// Era o corpo de [SerializeField]s do DragonController; agora é um asset.
///
/// Tudo o que só existe COM O DRAGÃO NO AR (locomoção de voo, manobras, decolagem,
/// pouso e colisão em voo) migrou para o <b>FlightProfile</b> na separação chão×voo
/// (jul/2026) — o DragonController lê esses números do FlightProfile resolvido.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonMovement.asset</b> — o
/// DragonController lê daqui e não guarda mais nenhum número.
///
/// Os NOMES dos campos são os mesmos de antes de propósito: os comentários do GDD, a
/// janela de balanceamento e a memória de quem tunou o dragão continuam valendo.
/// </summary>
[BalanceAsset("Balance/DragonMovement")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Movimento do Dragão", fileName = "DragonMovement")]
public class DragonMovementProfile : BalanceProfile
{
    [Header("Chão")]
    public float walkSpeed = 3.84f;   // passo de caçada (Stealth) — era o "andar"
    public float runSpeed = 11.4f;
    public float reverseSpeed = 1.7f; // ré (só no nado — no chão o corpo vira)
    public float groundAccel = 35f;   // 90% da corrida em ~0.35 s (hack and slash)
    public float groundDecel = 25f;   // soltar a direção: derrapada curta (peso)
    public float turnRateIdle = 540f; // giro parado/lento (°/s)
    public float turnRateRun = 240f;  // giro em corrida plena (°/s) — arco
    public float gravity = 28f;

    [Header("Esquiva lateral (Shift + A/D no chão)")]
    // A esquiva de chão é um empurrão LATERAL idêntico ao da esquiva simples de
    // voo (AirDodge): NÃO gira o rumo nem a câmera. A intensidade do deslize vem
    // do FlightProfile.airDodgeSpeed (compartilhado p/ chão e ar ficarem iguais),
    // por isso não há mais um "quanto o corpo vira" aqui.
    public float dashDuration = 0.4f;
    public float dashCooldown = 0.6f;
    public float dashIFrames = 0.3f;
    [Tooltip("Duração (s) do giro de câmera que acompanha a esquiva de VOO completa " +
             "(FlightProfile.flyDodge*) de volta às costas do dragão. Só o voo usa; " +
             "mora aqui por herança da separação chão×voo.")]
    public float dodgeCamCatchup = 0.2f;

    [Header("Cancel de golpes")]
    [Tooltip("Fração do golpe após a qual dash/decolagem podem cancelá-lo")]
    [Range(0f, 1f)] public float attackCancelWindow = 0.4f;

    [Header("Queda (dano só de altura REAL)")]
    [Tooltip("Altura segura = esta fração da MAIOR árvore da floresta — a " +
             "vegetação define o limite, então trocar as árvores recalibra sozinho")]
    public float safeFallTreeFraction = 0.7f;
    [Tooltip("Altura segura (m) se o mundo procedural não estiver disponível")]
    public float safeFallFallback = 18f;
    [Tooltip("Dano ao despencar do DOBRO da altura segura (cresce linear a partir dela)")]
    public float fallDamageAtDouble = 35f;

    [Header("Natação")]
    public float swimSpeed = 3.4f;        // nado pra frente
    public float swimBackSpeed = 1.2f;
    public float swimTurnSpeed = 85f;
    public float swimAccel = 4.5f;
    public float buoyDepth = 0.85f;       // quanto do corpo fica submerso (m × escala)
    public float minSwimDepth = 1.1f;     // profundidade mínima p/ boiar (senão anda)
    public float swimCost = 2f;           // energia/s nadando

    [Header("Alimentação")]
    public float eatRange = 4.5f;

    // A batida de asa e a subida de decolagem cobram energia pelo FlightProfile
    // (energyPerFlap · takeoffClimbEnergyPerSec) desde que o voo virou módulo — os
    // antigos flapCost/climbCost daqui não eram lidos por ninguém e foram removidos
    // na refatoração de dados (jul/2026). O mesmo valeu para glideDelay, que o
    // FlightProfile.glideAnimDelay substituiu. Os custos de VOO (glideCost/takeoffCost)
    // também migraram para o FlightProfile na separação chão×voo (jul/2026).
    [Header("Custos de Energia")]
    public float runCost = 3.5f;    // corrida no chão
    public float dodgeCost = 8f;    // esquiva de chão (GroundDash)
    public float fireCost = 15f;    // sopro de fogo
    public float attackCost = 3f;   // golpe corpo-a-corpo
}
