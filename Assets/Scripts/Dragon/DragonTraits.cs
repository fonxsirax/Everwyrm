using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Os TRAÇOS ativos de um dragão e a COMPOSIÇÃO dos seus efeitos (rework jul/2026).
/// É adicionado em runtime pelo DragonController (como DragonSounds/DamageFeedback)
/// e carregado pela possessão a partir do DragonRecord.
///
/// Cada acessor abaixo devolve o efeito COMBINADO de todos os traços ativos (1× =
/// nenhum efeito). Os sistemas multiplicam esse fator no ponto certo, então cada
/// traço vive num só lugar do código. Os NÚMEROS estão todos aqui, num só arquivo,
/// e documentados no GDD (seção "Traços herdáveis") — é onde se balanceia.
///
/// Sem record (cena de teste avulsa) a lista fica vazia e TUDO devolve 1×/false:
/// o dragão se comporta exatamente como antes dos traços.
/// </summary>
public class DragonTraits : MonoBehaviour
{
    readonly List<DragonTrait> active = new();

    public IReadOnlyList<DragonTrait> Active => active;
    public bool Has(DragonTrait t) => active.Contains(t);

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
        Mul(DragonTrait.HollowBones, 0.85f) * Mul(DragonTrait.ForgeLungs, 0.7f);
    /// <summary>Fome cai mais rápido: o Insone paga o não-descansar em apetite.</summary>
    public float HungerDecayMul => Mul(DragonTrait.Insomniac, 1.2f);
    /// <summary>Recuperação de energia parado: o Insone repõe rápido (dispensa o R).</summary>
    public float IdleRegenMul => Mul(DragonTrait.Insomniac, 1.6f);
    /// <summary>Dano RECEBIDO: a Couraça amortece.</summary>
    public float DamageTakenMul => Mul(DragonTrait.Carapace, 0.8f);

    // ---- Voo -------------------------------------------------------------
    /// <summary>Teto de voo: Guelras trocam ar por água.</summary>
    public float CeilingMul => Mul(DragonTrait.VestigialGills, 0.85f);
    /// <summary>Afundamento no planeio: Ossos Ocos planam melhor (afundam menos).</summary>
    public float GlideSinkMul => Mul(DragonTrait.HollowBones, 0.8f);
    /// <summary>Aproveitamento das correntes de ar: o Termonauta dobra.</summary>
    public float WindReadMul => Mul(DragonTrait.Thermonaut, 2f);

    // ---- Corpo / chão ----------------------------------------------------
    /// <summary>Peso: Ossos Ocos é leve (afeta subida, planeio, custo).</summary>
    public float WeightMul => Mul(DragonTrait.HollowBones, 0.85f);
    /// <summary>Dano de queda: o esqueleto oco quebra mais fácil.</summary>
    public float FallDamageMul => Mul(DragonTrait.HollowBones, 1.25f);
    /// <summary>Giro: a Couraça enrijece o dragão.</summary>
    public float TurnMul => Mul(DragonTrait.Carapace, 0.9f);
    /// <summary>Nutrição de uma refeição: Estômago de Ferro rende mais.</summary>
    public float EatNutritionMul => Mul(DragonTrait.IronStomach, 1.25f);

    // ---- Nado ------------------------------------------------------------
    public float SwimSpeedMul => Mul(DragonTrait.VestigialGills, 1.5f);
    /// <summary>Guelras: nadar não custa energia (respira submerso).</summary>
    public bool BreathesUnderwater => Has(DragonTrait.VestigialGills);

    // ---- Combate ---------------------------------------------------------
    /// <summary>Fôlego de Forja: o sopro ignora o cooldown (0×).</summary>
    public float FireCooldownMul => Has(DragonTrait.ForgeLungs) ? 0f : 1f;

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
        return Mathf.Clamp(1f - 0.3f * Mathf.Clamp01(cold) + 0.3f * Mathf.Clamp01(hot), 0.6f, 1.5f);
    }

    float Mul(DragonTrait t, float factor) => Has(t) ? factor : 1f;
}
