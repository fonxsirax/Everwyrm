/// <summary>
/// TRAÇOS herdáveis — a camada qualitativa da linhagem (rework jul/2026, inspirado
/// nas passivas do Palworld). Cada dragão nasce com 0..maxSlots traços SORTEADOS;
/// a reprodução os HERDA com chance (DragonRecord.Breed). Todo traço tem um efeito
/// nomeado e, quase sempre, um TRADE-OFF — é o que gera "olha o dragão que eu criei".
///
/// É só DADO. O efeito real é composto em DragonTraits (o componente), que os
/// sistemas consultam. Cada traço e seus números estão documentados no GDD
/// (seção "Traços herdáveis").
/// </summary>
/// <remarks>
/// A ORDEM é contrato de serialização: os DragonRecord gravam o traço como índice.
/// Só ACRESCENTE no fim — nunca reordene nem remova (zeraria os traços de todo
/// dragão já salvo). `None` é o índice 0 e o vazio de slot.
/// </remarks>
public enum DragonTrait
{
    None = 0,
    ColdBlood,       // Sangue Frio
    HollowBones,     // Ossos Ocos
    IronStomach,     // Estômago de Ferro
    Thermonaut,      // Termonauta
    Insomniac,       // Insone
    ForgeLungs,      // Fôlego de Forja
    VestigialGills,  // Guelras Vestigiais
    Carapace,        // Couraça
}

/// <summary>Descrições legíveis dos traços (ficha e GDD). Os NÚMEROS do efeito
/// vivem em DragonTraits (o componente que os aplica); aqui é só o texto.</summary>
public static class DragonTraitInfo
{
    /// <summary>Todos os traços SORTEÁVEIS (exclui None) — usado pelo nascimento.</summary>
    public static readonly DragonTrait[] Rollable =
    {
        DragonTrait.ColdBlood, DragonTrait.HollowBones, DragonTrait.IronStomach,
        DragonTrait.Thermonaut, DragonTrait.Insomniac, DragonTrait.ForgeLungs,
        DragonTrait.VestigialGills, DragonTrait.Carapace,
    };

    public static string Name(DragonTrait t) => t switch
    {
        DragonTrait.ColdBlood => "Sangue Frio",
        DragonTrait.HollowBones => "Ossos Ocos",
        DragonTrait.IronStomach => "Estômago de Ferro",
        DragonTrait.Thermonaut => "Termonauta",
        DragonTrait.Insomniac => "Insone",
        DragonTrait.ForgeLungs => "Fôlego de Forja",
        DragonTrait.VestigialGills => "Guelras Vestigiais",
        DragonTrait.Carapace => "Couraça",
        _ => "—",
    };

    public static string Describe(DragonTrait t) => t switch
    {
        DragonTrait.ColdBlood => "Gasta menos energia no frio, mais no calor",
        DragonTrait.HollowBones => "Leve, plana melhor · vida menor, cai mais forte",
        DragonTrait.IronStomach => "Carniça velha nutre como fresca",
        DragonTrait.Thermonaut => "Aproveita o dobro das correntes de ar",
        DragonTrait.Insomniac => "Não precisa descansar · sente mais fome",
        DragonTrait.ForgeLungs => "Sopro sem cooldown · vida menor",
        DragonTrait.VestigialGills => "Nada rápido, respira na água · teto de voo menor",
        DragonTrait.Carapace => "Recebe menos dano · menos ágil",
        _ => "",
    };
}
