using UnityEngine;

/// <summary>
/// Números do MODO MIRA do dragão (tecla E): retícula que segue o mouse, a cabeça
/// seguindo o alvo e as ações apontando pra lá. Data-driven como todo balanceamento —
/// edite <b>Assets/Scriptables/Resources/Balance/DragonAim.asset</b> (crie com
/// Tools &gt; Everwyrm &gt; Balanceamento — Recriar Assets Faltantes, ou o
/// DragonAim usa defaults em memória até o asset existir).
/// </summary>
[BalanceAsset("Balance/DragonAim")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Mira do Dragão", fileName = "DragonAim")]
public class AimProfile : BalanceProfile
{
    [Header("Ações na mira")]
    [Tooltip("Raio (m × escala do dragão) ao redor do ponto da retícula em que um animal " +
             "é 'agarrado' (soft-lock): o ataque mira nele em vez do ponto cru. Sem alvo " +
             "por perto, o ataque cai no ponto sob a retícula.")]
    public float softLockRadius = 6f;

    [Header("Retícula / raycast")]
    [Tooltip("Distância (m) do ponto de mira quando o raio da câmera não bate em nada " +
             "(céu) — mira num ponto distante nessa direção.")]
    public float aimRayDistance = 300f;

    [Tooltip("O que a mira ENXERGA no raycast (terreno + fauna). Padrão: tudo; o próprio " +
             "dragão é sempre descartado em runtime.")]
    public LayerMask aimMask = ~0;

    [Header("Cabeça (head-look)")]
    [Tooltip("Rapidez com que a cabeça segue o alvo (1/s da suavização exponencial).")]
    public float headTurnSpeed = 12f;

    [Tooltip("Ângulo MÁXIMO (graus) que a cabeça vira em relação ao rumo do corpo — " +
             "impede o pescoço de 'quebrar' em alvos muito laterais/atrás.")]
    [Range(0f, 120f)] public float headMaxAngle = 70f;

    [Tooltip("Segundos p/ a cabeça entrar/sair da mira (blend do peso 0↔1).")]
    public float headBlendTime = 0.18f;

    [Header("Corpo no chão (over-the-shoulder)")]
    [Tooltip("Quão rápido (°/s) o corpo encara o rumo horizontal da retícula no chão.")]
    public float bodyTurnRate = 220f;

    [Tooltip("Velocidade do strafe lateral (A/D) como fração da corrida efetiva.")]
    [Range(0f, 1f)] public float strafeFraction = 0.7f;
}
