using UnityEngine;

/// <summary>
/// O que cada TRAÇO herdável custa e rende. Eram constantes cravadas no DragonTraits
/// (que, por sinal, se anunciava como "onde se balanceia") — agora são um asset.
///
/// A COMPOSIÇÃO continua no DragonTraits: cada acessor de lá multiplica os fatores dos
/// traços ativos e cada traço vive num só ponto do código. Aqui ficam só os números.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonTraits.asset</b>.
/// A lista de traços que existem é o enum DragonTrait; o texto de cada um está no
/// DragonTraitInfo e no GDD (seção "Traços herdáveis").
/// </summary>
[BalanceAsset("Balance/DragonTraits")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Traços do Dragão", fileName = "DragonTraits")]
public class DragonTraitProfile : BalanceProfile
{
    [Header("Ossos Ocos — leve, planador, quebradiço")]
    public float hollowBonesHealth = 0.85f;
    public float hollowBonesGlideSink = 0.8f;   // afunda menos planando
    public float hollowBonesWeight = 0.85f;
    public float hollowBonesFallDamage = 1.25f; // o esqueleto oco quebra mais fácil

    [Header("Fôlego de Forja — sopro sem cooldown, corpo frágil")]
    public float forgeLungsHealth = 0.7f;
    [Tooltip("Multiplicador do cooldown do sopro. 0 = ignora o cooldown por completo.")]
    public float forgeLungsFireCooldown = 0f;

    [Header("Insone — repõe energia parado, come mais")]
    public float insomniacHungerDecay = 1.2f;
    public float insomniacIdleRegen = 1.6f;

    [Header("Couraça — amortece o golpe, enrijece o giro")]
    public float carapaceDamageTaken = 0.8f;
    public float carapaceTurn = 0.9f;

    [Header("Guelras Vestigiais — água sim, altitude não")]
    public float gillsCeiling = 0.85f;
    public float gillsSwimSpeed = 1.5f;

    [Header("Termonauta — lê as correntes de ar")]
    public float thermonautWindRead = 2f;

    [Header("Estômago de Ferro — aproveita a carne")]
    public float ironStomachNutrition = 1.25f;

    [Header("Sangue Frio — o bioma manda no gasto de energia")]
    [Tooltip("Desconto de energia no frio pleno (Tundra/Montanha). 0.3 = -30%.")]
    [Range(0f, 1f)] public float coldBloodColdBonus = 0.3f;
    [Tooltip("Acréscimo de energia no calor pleno (Deserto). 0.3 = +30%.")]
    [Range(0f, 1f)] public float coldBloodHeatPenalty = 0.3f;
    [Tooltip("Piso do multiplicador de custo — o quanto o frio pode baratear no máximo.")]
    public float coldBloodMin = 0.6f;
    [Tooltip("Teto do multiplicador de custo — o quanto o calor pode encarecer no máximo.")]
    public float coldBloodMax = 1.5f;
}
