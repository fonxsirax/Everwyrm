using UnityEngine;

/// <summary>
/// Temperamento do dragão — o "Nature" do Pokémon aplicado aos SEIS atributos do
/// rework (Força · Chama · Agilidade · Vigor · Fôlego · Instinto). Cada natureza
/// SOBE um atributo e BAIXA outro por uma fração tunável (natureModifier, definido
/// no consumidor DragonAttributes, para ficar balanceável antes do lançamento).
/// Três naturezas são NEUTRAS (não mexem em nada), como as neutras do Pokémon.
///
/// É só DADO: o efeito real (o multiplicador) é aplicado em DragonAttributes, que
/// possui a magnitude. Nada aqui é hard-coded em porcentagem.
/// </summary>
/// <remarks>
/// A ORDEM é o contrato de serialização: os DragonRecord gravam a natureza como
/// índice (`nature: 3`), não como texto. Inserir ou reordenar aqui muda o
/// temperamento de todo dragão já salvo em
/// Assets/Scriptables/Resources/Entities/Dragons — só ACRESCENTE no
/// fim. Os índices 0..8 são os do sistema antigo de 3 atributos; só o EFEITO deles
/// foi remapeado para os 6 novos (Velocidade→Agilidade, Poder→Força,
/// Resistência→Vigor). Os índices 9+ são as naturezas novas dos atributos que o
/// sistema de 3 não tinha (Chama, Fôlego, Instinto).
/// </remarks>
public enum DragonNature
{
    // Neutras — sem viés (0..2)
    Balanced,
    Serene,
    Steady,

    // Herdadas do sistema de 3 — efeito remapeado (3..8)
    Fierce,     // +Força    -Vigor
    Brutal,     // +Força    -Agilidade
    Agile,      // +Agilidade -Força
    Stealthy,   // +Agilidade -Vigor
    Sturdy,     // +Vigor    -Agilidade
    Tenacious,  // +Vigor    -Força

    // Novas — cobrem Chama, Fôlego e Instinto (9..14)
    Fiery,      // +Chama    -Fôlego
    Smoldering, // +Chama    -Vigor
    Tireless,   // +Fôlego   -Força
    Windborne,  // +Fôlego   -Agilidade
    Cunning,    // +Instinto -Força
    Feral,      // +Instinto -Fôlego
}

public static class DragonNatureTable
{
    /// <summary>Qual atributo a natureza sobe e qual ela baixa (null/null = neutra).</summary>
    public static (DragonAttributes.Attribute? up, DragonAttributes.Attribute? down) Effect(DragonNature n)
    {
        var Agi = DragonAttributes.Attribute.Agility;
        var Mig = DragonAttributes.Attribute.Might;
        var Vig = DragonAttributes.Attribute.Vigor;
        var Ard = DragonAttributes.Attribute.Ardor;
        var Win = DragonAttributes.Attribute.Wind;
        var Ins = DragonAttributes.Attribute.Instinct;
        return n switch
        {
            DragonNature.Fierce     => (Mig, Vig),
            DragonNature.Brutal     => (Mig, Agi),
            DragonNature.Agile      => (Agi, Mig),
            DragonNature.Stealthy   => (Agi, Vig),
            DragonNature.Sturdy     => (Vig, Agi),
            DragonNature.Tenacious  => (Vig, Mig),
            DragonNature.Fiery      => (Ard, Win),
            DragonNature.Smoldering => (Ard, Vig),
            DragonNature.Tireless   => (Win, Mig),
            DragonNature.Windborne  => (Win, Agi),
            DragonNature.Cunning    => (Ins, Mig),
            DragonNature.Feral      => (Ins, Win),
            _ => (null, null),   // neutras
        };
    }

    /// <summary>Multiplicador do atributo para a natureza: 1+mod (favorecido),
    /// 1-mod (prejudicado) ou 1 (neutro/indiferente). `modifier` vem do balanceamento.</summary>
    public static float Multiplier(DragonNature n, DragonAttributes.Attribute attr, float modifier)
    {
        var (up, down) = Effect(n);
        if (up == attr) return 1f + modifier;
        if (down == attr) return 1f - modifier;
        return 1f;
    }

    /// <summary>Rótulo PT curto de um atributo, para o HUD/ficha.</summary>
    public static string Label(DragonAttributes.Attribute a) => a switch
    {
        DragonAttributes.Attribute.Might => "Força",
        DragonAttributes.Attribute.Ardor => "Chama",
        DragonAttributes.Attribute.Agility => "Agilidade",
        DragonAttributes.Attribute.Vigor => "Vigor",
        DragonAttributes.Attribute.Wind => "Fôlego",
        DragonAttributes.Attribute.Instinct => "Instinto",
        _ => a.ToString(),
    };

    /// <summary>Rótulo curto para o HUD/ficha ("Feroz +Força −Vigor").</summary>
    public static string Describe(DragonNature n)
    {
        var (up, down) = Effect(n);
        if (up == null) return $"{n} (neutra)";
        return $"{n} (+{Label(up.Value)} −{Label(down.Value)})";
    }
}
