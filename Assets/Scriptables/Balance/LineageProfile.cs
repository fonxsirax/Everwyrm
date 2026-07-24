using UnityEngine;

/// <summary>
/// Balanceamento da LINHAGEM: como um dragão nasce (talento sorteado) e como uma
/// ninhada muta. Era o corpo de [SerializeField]s do DragonBase — que fica na CENA,
/// então esses números viviam presos ao Main.unity.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/Lineage.asset</b>.
/// O que continua no DragonBase é só wiring de cena: a lista de dragões, o avatar,
/// o spawnPoint, a skin de fallback e o toggle de laboratório (randomEveryPlay).
/// </summary>
[BalanceAsset("Balance/Lineage")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Linhagem", fileName = "Lineage")]
public class LineageProfile : BalanceProfile
{
    [Header("Nascimento")]
    [Tooltip("Teto do talento bruto sorteado no nascimento (os IVs). Deve bater com " +
             "DragonAttributeProfile.maxIV, que é quem CONVERTE o IV em atributo.")]
    public int maxIV = 31;

    [Header("Mutação (DragonRecord.Breed)")]
    [Tooltip("Chance de natureza sortear de novo e o genoma visual mutar (por ninhada).")]
    [Range(0f, 1f)] public float traitMutationChance = 0.08f;
    [Tooltip("Chance de UM atributo virar outlier (acima do teto) — 1 rolagem por " +
             "ninhada, não mais uma por IV.")]
    [Range(0f, 1f)] public float statMutationChance = 0.05f;
    [Tooltip("Chance do prêmio PREFERIDO: +1 slot de ataque (DragonAbilities). Mais " +
             "raro que o outlier de stat — é o que faz a linhagem valer a pena criar.")]
    [Range(0f, 1f)] public float extraSlotMutationChance = 0.02f;
    [Tooltip("Quanto o outlier de stat ultrapassa o teto normal do IV (1..isto).")]
    public int ivOutlierBonus = 4;
}
