using UnityEngine;

/// <summary>
/// MODO BALANCEAMENTO — desliga (sem apagar) a variação POR DRAGÃO, para que qualquer
/// dragão possuído se comporte EXATAMENTE igual enquanto se afina o gameplay bruto
/// (voo, velocidade, movimento). Nada é destruído: IVs, natureza, traços, idade e
/// crescimento continuam existindo no DragonRecord e nos componentes — este asset só
/// manda os consumidores IGNORAREM essas fontes e usarem um dragão de REFERÊNCIA fixo.
///
/// COMO USAR:
///  1. `enabled` liga/desliga o modo inteiro (o MASTER).
///  2. Cada flag abaixo controla UM sistema. Comece tudo ligado (dragões idênticos) e,
///     conforme for validando o balanceamento base, DESLIGUE uma flag por vez para
///     reintroduzir aquele parâmetro (IVs, depois natureza, depois idade...).
///  3. O dragão de referência (maturidade/tamanho/condição) é ajustável ao vivo nos
///     campos de "Referência" — mexa e veja no Play sem recompilar.
///
/// Quem consome: DragonAttributes (maturidade/IVs/natureza/velhice), DragonGrowth
/// (corpo/tamanho/velhice/IV de Vigor) e DragonTraits (traços). Resolvido de forma
/// SILENCIOSA (Balance.TryDefault): se o asset não existir, o jogo roda como sempre.
///
/// Edite <b>Assets/Scriptables/Resources/Balance/DragonBalanceOverride.asset</b>
/// (crie com Tools > Everwyrm > Balanceamento — Recriar Assets Faltantes).
/// </summary>
[BalanceAsset("Balance/DragonBalanceOverride")]
[CreateAssetMenu(menuName = "Everwyrm/Balanceamento/Modo Balanceamento (Override)", fileName = "DragonBalanceOverride")]
public class DragonBalanceOverride : BalanceProfile
{
    [Header("MASTER")]
    [Tooltip("Liga o modo balanceamento inteiro. Desligado = o jogo se comporta como se " +
             "este asset não existisse (linhagem 100% ativa).")]
    public bool enabled = true;

    [Header("Identidade — desligar a variação de linhagem")]
    [Tooltip("Trata TODOS os IVs (talento por atributo) como 0 — inclusive o Vigor que " +
             "alonga a vida e acelera o crescimento.")]
    public bool neutralizeIVs = true;
    [Tooltip("Força a natureza para Balanced (nenhum atributo favorecido/prejudicado).")]
    public bool neutralizeNature = true;
    [Tooltip("Ignora os traços herdáveis (Ossos Ocos, Couraça, Guelras...) — todos os " +
             "efeitos voltam a 1×.")]
    public bool disableTraits = true;

    [Header("Idade — congelar a maturação e a velhice")]
    [Tooltip("Congela a MATURIDADE (o motor dos atributos) no valor de referência abaixo, " +
             "em vez de deixá-la subir com crescimento+idade.")]
    public bool freezeMaturity = true;
    [Tooltip("Zera o declínio da Velhice: atributos e corpo não se corroem no fim da vida, " +
             "e o dragão não morre de velho enquanto o modo estiver ligado.")]
    public bool disableElder = true;

    [Header("Corpo — congelar tamanho e condição")]
    [Tooltip("Congela crescimento (tamanho) e condição corporal nos valores de referência, " +
             "em vez de deixá-los mudar com alimentação/idade. Também fixa a ESCALA visual.")]
    public bool freezeBody = true;

    [Header("Referência — o 'dragão padrão' (ajustável ao vivo)")]
    [Tooltip("Maturidade congelada (0 = ovo · 1 = auge). Usada quando 'freezeMaturity'. " +
             "1 = atributos no topo da curva.")]
    [Range(0f, 1f)] public float maturity01 = 1f;
    [Tooltip("Crescimento congelado (0 = filhote · 1 = colossal). Usado quando 'freezeBody'. " +
             "0.4 = limiar de adulto (espelha DragonGrowthProfile.adultAt) — o 'adulto médio'.")]
    [Range(0f, 1f)] public float growth01 = 0.4f;
    [Tooltip("Condição corporal congelada (0 = esquelético · 0.5 = saudável · 1 = gordo). " +
             "Usada quando 'freezeBody'.")]
    [Range(0f, 1f)] public float condition01 = 0.5f;

    // ---- Atalhos de leitura para os consumidores (mantêm a lógica num lugar só) ----
    /// <summary>Modo ligado E este sistema ativo? Um helper por sistema evita repetir
    /// `enabled && flag` espalhado pelos componentes.</summary>
    public bool IvsNeutralized => enabled && neutralizeIVs;
    public bool NatureNeutralized => enabled && neutralizeNature;
    public bool TraitsDisabled => enabled && disableTraits;
    public bool MaturityFrozen => enabled && freezeMaturity;
    public bool ElderDisabled => enabled && disableElder;
    public bool BodyFrozen => enabled && freezeBody;
}
