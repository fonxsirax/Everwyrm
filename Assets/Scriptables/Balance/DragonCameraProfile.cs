using UnityEngine;

/// <summary>
/// Game feel da câmera third-person: órbita, suavização, antecipação, FOV e shake.
/// Era o corpo de [SerializeField]s do DragonCamera — que ficava na CENA, então o
/// tuning se perdia a cada troca de cena. Agora é um asset.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonCamera.asset</b>.
/// O único campo que continua na cena é o `target` (referência de objeto, não número).
/// </summary>
[BalanceAsset("Balance/DragonCamera")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Câmera do Dragão", fileName = "DragonCamera")]
public class DragonCameraProfile : BalanceProfile
{
    [Header("Alvo")]
    public float pivotHeight = 2.4f;

    [Header("Órbita")]
    [Tooltip("Distância INICIAL — o scroll do jogador manda a partir daqui (runtime).")]
    public float distance = 10f;
    public float minDistance = 3f;
    public float maxDistance = 20f;
    public float sensitivity = 3f;
    public float zoomSpeed = 5f;
    public float minPitch = -25f;
    public float maxPitch = 70f;

    [Header("Suavização")]
    public float followLag = 10f;
    public float autoAlignDelay = 1.5f;
    public float autoAlignSpeed = 1.6f;

    [Header("Antecipação (game feel)")]
    [Tooltip("Em voo, a câmera adianta alguns graus na direção da curva")]
    public float turnLeadAngle = 6f;
    [Tooltip("No mergulho, a câmera inclina para baixo antecipando o alvo")]
    public float divePitchBias = 10f;
    public float diveFovKick = 6f;

    [Header("FOV")]
    public float baseFov = 60f;
    public float flightFov = 74f;

    [Header("Shake de dano")]
    public float maxShakeAngle = 2.2f;   // graus no trauma máximo
    public float shakeDecay = 2.2f;      // trauma perdido por segundo
    public float shakeFrequency = 14f;
}
