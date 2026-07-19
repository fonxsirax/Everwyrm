using UnityEngine;

/// <summary>
/// Perfil de voo — TODOS os números da mecânica de voo num asset.
/// Edite `Assets/Scriptables/Resources/FlightProfile.asset` sem tocar em código/cena.
/// Futuro: um perfil por espécie de dragão, upgrades de asa = trocar o asset.
/// </summary>
[CreateAssetMenu(menuName = "Everwyrm/Flight Profile", fileName = "FlightProfile")]
public class FlightProfile : ScriptableObject
{
    [Header("Fase de decolagem (segurar Space = subida contínua)")]
    [Tooltip("Segundos após decolar em que SEGURAR Space sobe continuamente")]
    public float takeoffClimbTime = 3f;
    [Tooltip("Velocidade de subida na fase de decolagem (m/s)")]
    public float takeoffClimbRate = 6.5f;
    [Tooltip("Energia por segundo segurando a subida de decolagem")]
    public float takeoffClimbEnergyPerSec = 4f;

    [Header("Ciclo de batidas (skill)")]
    [Tooltip("Batidas disponíveis por ciclo — soltar Space + recuperação renova")]
    public int flapsPerCycle = 2;
    [Tooltip("Intervalo mínimo entre batidas (s)")]
    public float flapMinInterval = 0.32f;
    [Tooltip("Tempo após a última batida para o ciclo renovar (precisa soltar o botão)")]
    public float cycleRecovery = 0.45f;

    [Header("Força da batida")]
    [Tooltip("Impulso vertical por batida (m/s)")]
    public float flapLift = 9f;
    [Tooltip("Empurrão pra frente por batida (m/s)")]
    public float flapForwardBoost = 1.1f;
    [Tooltip("Velocidade máxima de subida acumulável")]
    public float maxRiseSpeed = 12f;

    [Header("Planeio (sustentação pela velocidade)")]
    [Tooltip("Afundamento planando RÁPIDO (m/s) — voar rápido conserva altitude")]
    public float sinkAtSpeed = 0.7f;
    [Tooltip("Afundamento planando LENTO (m/s) — voar devagar despenca")]
    public float sinkAtStall = 3.8f;
    [Tooltip("Curva da transição rápido→lento (maior = punição só bem devagar)")]
    public float slownessPower = 1.4f;
    [Tooltip("Rapidez com que o impulso da batida decai rumo ao planeio (m/s²)")]
    public float verticalResponse = 4.5f;
    [Tooltip("Segundos após a última batida para a animação de planar")]
    public float glideAnimDelay = 1f;

    [Header("Mergulho")]
    public float diveSink = 14f;

    [Header("Custo de energia")]
    [Tooltip("Energia por batida (multiplicada pelo peso do dragão)")]
    public float energyPerFlap = 2f;

    [Header("Vento")]
    [Tooltip("Quanto correntes de ar (updrafts/térmicas) afetam o dragão")]
    public float windInfluence = 1f;
}
