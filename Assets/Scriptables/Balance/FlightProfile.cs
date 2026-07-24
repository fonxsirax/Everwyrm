using UnityEngine;

/// <summary>
/// Perfil de voo — TODOS os números da mecânica de voo num asset.
/// Edite `Assets/Scriptables/Resources/Balance/FlightProfile.asset` sem tocar em código/cena.
/// Futuro: um perfil por espécie de dragão, upgrades de asa = trocar o asset.
///
/// Foi o PRIMEIRO profile do projeto; a refatoração de jul/2026 estendeu este mesmo
/// padrão para todo o resto do dragão (ver BalanceProfile).
/// </summary>
[BalanceAsset("Balance/FlightProfile")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Voo do Dragão", fileName = "FlightProfile")]
public class FlightProfile : BalanceProfile
{
    [Header("Fase de decolagem (segurar Space = subida contínua)")]
    [Tooltip("Segundos após decolar em que SEGURAR Space sobe continuamente")]
    public float takeoffClimbTime = 3f;
    [Tooltip("Velocidade de subida na fase de decolagem (m/s) — precisa ser ALTA: " +
             "abaixo do impulso de entrada, segurar Space PUXA o dragão pra baixo. " +
             "Multiplicada pelo PESO (DragonGrowth.FlapLiftMul), não pelo tamanho. " +
             "NERF jul/2026: a calibração anterior (12) ficou apelona.")]
    public float takeoffClimbRate = 6f;
    [Tooltip("Energia por segundo segurando a subida de decolagem")]
    public float takeoffClimbEnergyPerSec = 4f;

    [Header("Ciclo de batidas (skill)")]
    [Tooltip("Batidas disponíveis por ciclo — soltar Space + recuperação renova")]
    public int flapsPerCycle = 2;
    [Tooltip("Intervalo mínimo entre batidas (s)")]
    public float flapMinInterval = 0.22f;
    [Tooltip("Tempo após a última batida para o ciclo renovar (precisa soltar o botão)")]
    public float cycleRecovery = 0.3f;

    [Header("Bônus de timing (soltar Space no fim da batida)")]
    [Tooltip("Duração da batida de asa — janela para soltar e ganhar o bônus (s)")]
    public float flapAnimDuration = 1f;
    [Tooltip("Impulso vertical EXTRA máximo, soltando exatamente no fim da batida (m/s). " +
             "NERF jul/2026: escala junto com flapLift (mesma razão ÷4).")]
    public float flapBonusLift = 3.25f;
    [Tooltip("Empurrão pra frente extra no bônus máximo (m/s)")]
    public float flapBonusForward = 0.8f;
    [Tooltip("Curva do bônus (maior = só soltura quase perfeita vale muito)")]
    public float flapBonusPower = 2f;

    [Header("Força da batida")]
    [Tooltip("Impulso vertical por batida (m/s). Voando estável é o número que faz o " +
             "Space GANHAR ALTITUDE de verdade — multiplicado só pelo PESO atual " +
             "(DragonGrowth.FlapLiftMul), então varia pouco ao longo da vida. " +
             "NERF jul/2026: 1/4 do valor da calibração anterior (17) — ficou apelona; " +
             "voar de verdade agora pede ritmo (batidas + planeio), não 1 flap só.")]
    public float flapLift = 4.25f;
    [Tooltip("Empurrão pra frente por batida (m/s)")]
    public float flapForwardBoost = 1.1f;
    [Tooltip("Velocidade máxima de subida acumulável (m/s) — também escala com o peso. " +
             "NERF jul/2026: reduzida junto com flapLift (não é mais quase inatingível).")]
    public float maxRiseSpeed = 10f;

    [Header("Planeio (sustentação pela velocidade)")]
    [Tooltip("Afundamento planando RÁPIDO (m/s) — voar rápido conserva altitude")]
    public float sinkAtSpeed = 0.7f;
    [Tooltip("Afundamento planando LENTO (m/s) — voar devagar despenca")]
    public float sinkAtStall = 3f;
    [Tooltip("Curva da transição rápido→lento (maior = punição só bem devagar)")]
    public float slownessPower = 1.8f;
    [Tooltip("Rapidez com que o impulso da batida decai rumo ao planeio (m/s²) — " +
             "alto = subida/afundo engatam quase na hora (hack and slash)")]
    public float verticalResponse = 9f;
    [Tooltip("Segundos após a última batida para a animação de planar")]
    public float glideAnimDelay = 1f;

    [Header("Mergulho")]
    public float diveSink = 14f;
    [Tooltip("Mergulhando a velocidade pode passar do máximo (multiplicador)")]
    public float diveOverspeed = 1.3f;
    [Tooltip("Ao SAIR do mergulho, fração da queda convertida em velocidade à " +
             "frente (swoop) — o loop de energia mergulho→voo rasante")]
    public float swoopConversion = 0.35f;

    [Header("Custo de energia")]
    [Tooltip("Energia por batida (multiplicada pelo peso do dragão). Subiu junto com " +
             "o flapLift: batida que rende muita altitude tem que custar")]
    public float energyPerFlap = 3f;

    [Header("Vento")]
    [Tooltip("Quanto correntes de ar (updrafts/térmicas) afetam o dragão")]
    public float windInfluence = 1f;

    [Header("Teto de voo (ar rarefeito)")]
    [Tooltip("Faixa abaixo do teto (m) em que a sustentação vai sumindo — teto suave, sem parede")]
    public float ceilingSoftBand = 40f;

    // -----------------------------------------------------------------------
    // Números que VINHAM do DragonMovementProfile e eram lidos pelo
    // DragonController. Migraram para cá (jul/2026): tudo o que só existe com o
    // dragão no ar — locomoção de voo, manobras, decolagem, pouso e colisão em
    // voo. Os NOMES seguem idênticos de propósito (GDD, janela de balanceamento
    // e memória de tuning continuam valendo). O que NÃO é de voo (chão, esquiva
    // de chão, natação, dano de queda, custos de corrida/combate) ficou lá.
    // -----------------------------------------------------------------------

    [Header("Decolagem")]
    [Tooltip("Acima desta fração da corrida, decola DIRETO pro voo (sem anim de salto)")]
    public float runningTakeoffFraction = 0.5f;
    [Tooltip("Trava mínima (s) no 1º frame do salto parado, até o Animator confirmar " +
             "que entrou no estado TakeOff (depois disso a trava segue o clipe real)")]
    public float takeoffLockMinimum = 0.15f;
    [Tooltip("Subida durante o salto (m/s) — o dragão sobe sozinho enquanto o clipe roda")]
    public float takeoffHopClimb = 6f;
    [Tooltip("BOTE no fim do salto: velocidade de subida imposta (m/s) — é aqui " +
             "que a decolagem parada ganha altura de verdade")]
    public float takeoffLaunchClimb = 12f;
    [Tooltip("Empurrão pra frente no bote (m/s) — sai do salto já com sustentação")]
    public float takeoffLaunchForward = 3f;
    [Tooltip("Rédea de segurança: se o clipe do salto não terminar até aqui, solta (s)")]
    public float takeoffMaxLock = 2.5f;
    [Tooltip("Carência pós-decolagem p/ auto-pouso e colisão (s)")]
    public float takeoffGrace = 0.8f;

    [Header("Wing Boost (Shift no ar)")]
    public float boostImpulse = 8f;        // m/s à frente (× escala do corpo)
    public float boostMaxOverspeed = 1.25f;// × vel. máx. de voo
    public float boostCooldown = 1f;
    public float boostIFrames = 0.25f;
    public float boostCost = 6f;
    [Tooltip("Esquiva simples (Shift + A/D no ar): empurrão LATERAL instantâneo, " +
             "sem girar o rumo — o dragão continua voando reto (m/s)")]
    public float airDodgeSpeed = 14f;
    public float airDodgeDuration = 0.4f;

    [Header("Esquiva de voo COMPLETA (A/D + Shift + Space)")]
    [Tooltip("Quanto o voo VIRA — não é empurrão lateral, é mudar de rumo (graus)")]
    public float flyDodgeTurn = 90f;
    [Tooltip("Tempo da virada — a animação Fly Dodge L/R toca inteira nela (s)")]
    public float flyDodgeDuration = 0.45f;
    public float flyDodgeIFrames = 0.4f;
    public float flyDodgeCost = 8f;

    [Header("Voo")]
    public float minFlySpeed = 6f;
    public float cruiseSpeed = 14f;
    public float maxFlySpeed = 26f;
    public float flyAccel = 22f;      // engata rápido; perder velocidade é lento
    public float turnSpeedAir = 95f;
    public float bankAngle = 48f;
    public float pitchAngle = 28f;
    public float climbRate = 7.5f;
    public float diveRate = 14f;
    public float glideSink = 1.6f;
    public float stallSink = 9f;

    [Header("Pouso")]
    public float landProbeDistance = 3.5f;
    public float landMaxSpeed = 11f;
    public float landApproachProbe = 16f;  // segurando S: busca chão até aqui
    public float landDescendRate = 16f;    // descida na aproximação (longe do chão)
    public float landFlareRate = 3f;       // descida perto do chão ("flare" suave)
    [Tooltip("Voando rente ao chão SEM intenção de subir: pousa em vez de raspar (m)")]
    public float autoLandHeight = 1.6f;
    [Tooltip("Inclinação máxima que aceita pouso automático (1 = plano, 0.7 ≈ 45°)")]
    public float autoLandMaxSlope = 0.7f;
    [Tooltip("Camadas tratadas como CHÃO nos sondadores de pouso. 0 (Nothing) = o " +
             "DragonController usa Physics.DefaultRaycastLayers.")]
    public LayerMask groundMask = 0;

    [Header("Água (visual — não interrompe o voo)")]
    [Tooltip("Barriga a esta distância da lâmina: spray/ondulações acompanhando o voo (m)")]
    public float waterSkimHeight = 1.8f;

    [Header("Colisão em voo")]
    public float impactLight = 0.2f;       // fração da vel. máx.: abaixo é raspão
    public float impactHeavy = 0.5f;       // fração da vel. máx.: desequilíbrio
    public float impactSpeedLoss = 0.6f;   // perda de velocidade × impacto
    public float impactKnockDown = 6f;     // tranco p/ baixo no impacto máx. (m/s)
    public float staggerTime = 1.2f;       // duração do desequilíbrio (s)
    public float impactCooldown = 0.4f;    // intervalo mínimo entre reações
    public float impactEnergyCost = 6f;    // energia da colisão leve × impacto

    [Header("Custos de energia de voo")]
    public float glideCost = 0.3f;   // sustentação passiva no ar: custo mínimo/s
    public float takeoffCost = 6f;   // energia da decolagem
}
