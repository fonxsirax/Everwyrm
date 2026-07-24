using UnityEngine;

/// <summary>
/// A curva de vida dos SEIS atributos e tudo que cada ponto compra.
/// Era o corpo de [SerializeField]s do DragonAttributes.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonAttributes.asset</b>, ou simule
/// fora do Play em <b>Tools > Everwyrm > Balanço do Dragão</b>.
///
/// A ANATOMIA (qual atributo dirige qual sistema) está no GDD, seção "Atributos do
/// Dragão"; o cálculo puro que consome estes números está em DragonAttributes.
/// </summary>
[BalanceAsset("Balance/DragonAttributes")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Atributos do Dragão", fileName = "DragonAttributes")]
public class DragonAttributeProfile : BalanceProfile
{
    [Header("Escala dos atributos")]
    [Tooltip("Valor de um atributo no auge da vida. A escala inteira do balanceamento.")]
    public float maxAttribute = 10f;

    [Header("Maturidade (o motor dos atributos)")]
    [Tooltip("Peso do CRESCIMENTO (tamanho, depende de comer bem) contra a IDADE pura. " +
             "1 = só o tamanho manda; 0 = só a idade.")]
    [Range(0f, 1f)] public float growthWeight = 0.65f;
    [Tooltip("Molda a maturidade crua (X) na efetiva (Y). Reta = amadurecer acompanha " +
             "o crescimento; curva em S = infância longa e explosão na adolescência.")]
    public AnimationCurve maturityCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Quanto a Velhice corrói os atributos no último suspiro (0.25 = -25%).")]
    [Range(0f, 0.9f)] public float elderDecline = 0.25f;

    [Header("Curvas por atributo (X = maturidade · Y = fração do máximo)")]
    [Tooltip("Agilidade amadurece CEDO — o filhote já é ligeiro.")]
    public AnimationCurve agilityCurve = Smooth(0f, 0.15f, 0.4f, 0.7f, 1f, 1f);
    [Tooltip("Força vem com a MASSA — quase toda no fim.")]
    public AnimationCurve mightCurve = Smooth(0f, 0f, 0.4f, 0.22f, 1f, 1f);
    [Tooltip("Vigor sobe parelho com a vida inteira.")]
    public AnimationCurve vigorCurve = Smooth(0f, 0.1f, 0.4f, 0.45f, 1f, 1f);
    [Tooltip("Chama desperta na adolescência — nada no filhote, forte no adulto.")]
    public AnimationCurve ardorCurve = Smooth(0f, 0f, 0.4f, 0.3f, 1f, 1f);
    [Tooltip("Fôlego (energia/teto) acompanha a vida.")]
    public AnimationCurve windCurve = Smooth(0f, 0.05f, 0.4f, 0.45f, 1f, 1f);
    [Tooltip("Instinto é quase inato — o filhote já fareja, e refina com a idade.")]
    public AnimationCurve instinctCurve = Smooth(0f, 0.2f, 0.4f, 0.6f, 1f, 1f);

    [Header("Identidade (linhagem)")]
    [Tooltip("Quanto a natureza favorece/prejudica um atributo (0.1 = ±10%, estilo Pokémon).")]
    [Range(0f, 0.5f)] public float natureModifier = 0.1f;
    [Tooltip("IV máximo sorteado no nascimento (o teto do talento bruto de cada atributo). " +
             "O mesmo número vive em LineageProfile.maxIV, que é quem SORTEIA — mantenha " +
             "os dois iguais (o balanço do dragão avisa se divergirem).")]
    public int maxIV = 31;
    [Tooltip("Quanto o IV cheio vale em fração de maxAttribute (0.25 = +2.5 pontos).")]
    [Range(0f, 1f)] public float ivInfluence = 0.25f;

    [Header("Ganhos por PONTO — Agilidade")]
    public float speedGain = 0.04f;        // +4% vel. máxima por ponto
    public float accelAgilityGain = 0.05f; // aceleração (Agilidade)
    public float turnGain = 0.03f;         // giro (chão e voo)

    [Header("Ganhos por PONTO — Força")]
    public float damageGain = 0.09f;
    public float boostGain = 0.05f;        // força do Wing Boost

    [Header("Ganhos por PONTO — Vigor")]
    public float healthGain = 0.10f;
    public float accelVigorGain = 0.02f;   // aceleração (o empurrão do corpo)
    public float hungerResistGain = 0.03f; // fome cai mais devagar

    [Header("Ganhos por PONTO — Chama")]
    public float flameGain = 0.06f;

    [Header("Ganhos por PONTO — Fôlego")]
    public float energyGain = 0.09f;            // duração do voo
    [Tooltip("Reduz o CUSTO de energia de tudo (voar, correr, atacar) — o dragão " +
             "de longa distância. Saturado em 0.5× de custo.")]
    public float energyEfficiencyGain = 0.03f;
    public float takeoffClimbGain = 0.06f;      // duração da subida de decolagem

    [Header("Ganhos por PONTO — Instinto")]
    public float dominanceBase = 30f;      // raio base do faro (m)
    public float dominanceGain = 12f;
    [Tooltip("Estica as janelas de invulnerabilidade (dash/boost/esquiva) — o dragão " +
             "que atravessa o golpe.")]
    public float iframeGain = 0.05f;
    [Tooltip("Lê melhor as correntes de ar: updrafts/térmicas rendem mais sustentação.")]
    public float windReadGain = 0.05f;

    [Header("Teto de voo (Fôlego)")]
    [Tooltip("Teto com 0 de Fôlego (m) — o filhote voa baixo.")]
    public float ceilingBase = 150f;
    public float ceilingGain = 15f;
    [Tooltip("Pontos de Fôlego até o teto cheio (satura aqui).")]
    public float ceilingCap = 10f;

    [Header("Degraus de maturidade (desbloqueio de ataques)")]
    [Tooltip("Em quantos degraus a maturidade é fatiada. É o número que o " +
             "DragonAttackData.unlockLevel compara — não é um 'nível' de RPG.")]
    public int maxTier = 10;

    /// <summary>Rede de segurança: uma curva sem chave avalia 0 SEMPRE, o que zeraria o
    /// atributo inteiro em silêncio. Acontece se o .asset for gravado à mão sem o bloco
    /// da curva (é assim que este asset nasceu na migração jul/2026), então reconstruir
    /// aqui é mais barato que caçar um dragão com Força 0 depois.</summary>
    void OnEnable()
    {
        Fix(ref maturityCurve, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        Fix(ref agilityCurve, Smooth(0f, 0.15f, 0.4f, 0.7f, 1f, 1f));
        Fix(ref mightCurve, Smooth(0f, 0f, 0.4f, 0.22f, 1f, 1f));
        Fix(ref vigorCurve, Smooth(0f, 0.1f, 0.4f, 0.45f, 1f, 1f));
        Fix(ref ardorCurve, Smooth(0f, 0f, 0.4f, 0.3f, 1f, 1f));
        Fix(ref windCurve, Smooth(0f, 0.05f, 0.4f, 0.45f, 1f, 1f));
        Fix(ref instinctCurve, Smooth(0f, 0.2f, 0.4f, 0.6f, 1f, 1f));
    }

    static void Fix(ref AnimationCurve curve, AnimationCurve fallback)
    {
        if (curve == null || curve.length == 0) curve = fallback;
    }

    /// <summary>Curva suave por 3 pontos — os defaults das curvas de maturação.</summary>
    static AnimationCurve Smooth(float x0, float y0, float x1, float y1, float x2, float y2)
    {
        var c = new AnimationCurve(new Keyframe(x0, y0), new Keyframe(x1, y1), new Keyframe(x2, y2));
        for (int i = 0; i < 3; i++) c.SmoothTangents(i, 0f);
        return c;
    }
}
