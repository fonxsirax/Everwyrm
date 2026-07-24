using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Os TRAÇOS ativos de um dragão e a COMPOSIÇÃO dos seus efeitos (rework jul/2026).
/// É adicionado em runtime pelo DragonController (como DragonSounds/DamageFeedback)
/// e carregado pela possessão a partir do DragonRecord.
///
/// Cada acessor abaixo devolve o efeito COMBINADO de todos os traços ativos (1× =
/// nenhum efeito). Os sistemas multiplicam esse fator no ponto certo, então cada
/// traço vive num só lugar do código. Os NÚMEROS ficam no DragonTraitProfile
/// (Assets/Scriptables/Resources/Balance/DragonTraits.asset) — é lá que se balanceia;
/// a documentação está no GDD, seção "Traços herdáveis".
///
/// Sem record (cena de teste avulsa) a lista fica vazia e TUDO devolve 1×/false:
/// o dragão se comporta exatamente como antes dos traços.
/// </summary>
public class DragonTraits : MonoBehaviour
{
    readonly List<DragonTrait> active = new();

    /// <summary>Os NÚMEROS de cada traço (DragonTraitProfile). Este componente nasce em
    /// runtime pelo DragonController, então não há Inspector para arrastar um override:
    /// vem sempre o asset padrão.</summary>
    DragonTraitProfile cfgCache;
    DragonTraitProfile cfg =>
        cfgCache != null ? cfgCache : (cfgCache = Balance.Default<DragonTraitProfile>());

    /// <summary>Modo balanceamento (opcional): quando ligado com 'disableTraits', os traços
    /// são IGNORADOS — a lista continua carregada, mas Has/Active se fazem de vazios, então
    /// TODO acessor abaixo cai em 1×/false sem tocar em cada um. Ausente = normal.</summary>
    static readonly DragonTrait[] NoTraits = System.Array.Empty<DragonTrait>();
    bool TraitsDisabled { get { var ov = Balance.TryDefault<DragonBalanceOverride>(); return ov != null && ov.TraitsDisabled; } }

    public IReadOnlyList<DragonTrait> Active => TraitsDisabled ? NoTraits : active;
    public bool Has(DragonTrait t) => !TraitsDisabled && active.Contains(t);

    /// <summary>Carrega os traços do record (possessão). Ignora None/duplicados.</summary>
    public void LoadFrom(DragonRecord record)
    {
        active.Clear();
        if (record == null || record.traits == null) return;
        foreach (var t in record.traits)
            if (t != DragonTrait.None && !active.Contains(t)) active.Add(t);
    }

    // ---- Vitais ----------------------------------------------------------
    /// <summary>Vida máxima: Ossos Ocos e Fôlego de Forja cobram vida.</summary>
    public float HealthMul =>
        Mul(DragonTrait.HollowBones, cfg.hollowBonesHealth) *
        Mul(DragonTrait.ForgeLungs, cfg.forgeLungsHealth);
    /// <summary>Fome cai mais rápido: o Insone paga o não-descansar em apetite.</summary>
    public float HungerDecayMul => Mul(DragonTrait.Insomniac, cfg.insomniacHungerDecay);
    /// <summary>Recuperação de energia parado: o Insone repõe rápido (dispensa o R).</summary>
    public float IdleRegenMul => Mul(DragonTrait.Insomniac, cfg.insomniacIdleRegen);
    /// <summary>Dano RECEBIDO: a Couraça amortece.</summary>
    public float DamageTakenMul => Mul(DragonTrait.Carapace, cfg.carapaceDamageTaken);

    // ---- Voo -------------------------------------------------------------
    /// <summary>Teto de voo: Guelras trocam ar por água.</summary>
    public float CeilingMul => Mul(DragonTrait.VestigialGills, cfg.gillsCeiling);
    /// <summary>Afundamento no planeio: Ossos Ocos planam melhor (afundam menos).</summary>
    public float GlideSinkMul => Mul(DragonTrait.HollowBones, cfg.hollowBonesGlideSink);
    /// <summary>Aproveitamento das correntes de ar: o Termonauta dobra.</summary>
    public float WindReadMul => Mul(DragonTrait.Thermonaut, cfg.thermonautWindRead);

    // ---- Corpo / chão ----------------------------------------------------
    /// <summary>Peso: Ossos Ocos é leve (afeta subida, planeio, custo).</summary>
    public float WeightMul => Mul(DragonTrait.HollowBones, cfg.hollowBonesWeight);
    /// <summary>Dano de queda: o esqueleto oco quebra mais fácil.</summary>
    public float FallDamageMul => Mul(DragonTrait.HollowBones, cfg.hollowBonesFallDamage);
    /// <summary>Giro: a Couraça enrijece o dragão.</summary>
    public float TurnMul => Mul(DragonTrait.Carapace, cfg.carapaceTurn);
    /// <summary>Nutrição de uma refeição: Estômago de Ferro rende mais.</summary>
    public float EatNutritionMul => Mul(DragonTrait.IronStomach, cfg.ironStomachNutrition);

    // ---- Nado ------------------------------------------------------------
    public float SwimSpeedMul => Mul(DragonTrait.VestigialGills, cfg.gillsSwimSpeed);
    /// <summary>Guelras: nadar não custa energia (respira submerso).</summary>
    public bool BreathesUnderwater => Has(DragonTrait.VestigialGills);

    // ---- Combate ---------------------------------------------------------
    /// <summary>Fôlego de Forja: o sopro ignora o cooldown (0× no asset padrão).</summary>
    public float FireCooldownMul => Mul(DragonTrait.ForgeLungs, cfg.forgeLungsFireCooldown);

    // ---- Bioma (Sangue Frio) --------------------------------------------
    /// <summary>Custo de energia MODULADO pelo bioma: Sangue Frio gasta menos no
    /// frio (Tundra/Montanha) e mais no calor (Deserto). 1× sem o traço ou sem
    /// mundo procedural.</summary>
    public float EnergyCostBiomeMul(Vector3 worldPos)
    {
        if (!Has(DragonTrait.ColdBlood)) return 1f;
        var world = InfiniteTerrain.Instance;
        if (world == null) return 1f;
        float cold = world.BiomeWeightOf(InfiniteTerrain.Biome.Tundra, worldPos.x, worldPos.z)
                   + world.BiomeWeightOf(InfiniteTerrain.Biome.Montanha, worldPos.x, worldPos.z);
        float hot = world.BiomeWeightOf(InfiniteTerrain.Biome.Deserto, worldPos.x, worldPos.z);
        return Mathf.Clamp(1f - cfg.coldBloodColdBonus * Mathf.Clamp01(cold)
                              + cfg.coldBloodHeatPenalty * Mathf.Clamp01(hot),
                           cfg.coldBloodMin, cfg.coldBloodMax);
    }

    float Mul(DragonTrait t, float factor) => Has(t) ? factor : 1f;
}
