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
    [Tooltip("Velocidade de subida na fase de decolagem (m/s) — precisa ser ALTA: " +
             "abaixo do impulso de entrada, segurar Space PUXA o dragão pra baixo")]
    public float takeoffClimbRate = 9f;
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
    [Tooltip("Impulso vertical EXTRA máximo, soltando exatamente no fim da batida (m/s)")]
    public float flapBonusLift = 9f;
    [Tooltip("Empurrão pra frente extra no bônus máximo (m/s)")]
    public float flapBonusForward = 0.8f;
    [Tooltip("Curva do bônus (maior = só soltura quase perfeita vale muito)")]
    public float flapBonusPower = 2f;

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
    [Tooltip("Energia por batida (multiplicada pelo peso do dragão)")]
    public float energyPerFlap = 2f;

    [Header("Vento")]
    [Tooltip("Quanto correntes de ar (updrafts/térmicas) afetam o dragão")]
    public float windInfluence = 1f;

    [Header("Teto de voo (ar rarefeito)")]
    [Tooltip("Faixa abaixo do teto (m) em que a sustentação vai sumindo — teto suave, sem parede")]
    public float ceilingSoftBand = 40f;
}
