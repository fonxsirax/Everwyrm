using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// O CATÁLOGO das blend shapes do avatar Unka — os nomes REAIS do mesh
/// (Unka.FBX), escritos exatamente como a Malbers os gravou, typos inclusos.
///
/// Existe porque o casamento por SUBSTRING que o DragonGrowth usava era frágil e
/// silenciosamente errado:
///   · "Fat" só pegava "Belly Fat" — as CINCO chaves "* Thick" nunca engordavam nada;
///   · "Thin" pegava "Thing Wing Bones" por acidente ("Thin" ⊂ "Thing"), não por design.
/// Aqui os grupos são explícitos e os índices resolvidos UMA vez (DragonShapeRig),
/// então aplicar peso vira acesso direto — sem varrer nomes todo frame.
///
/// Typos do MODELO que temos que respeitar ao pé da letra:
///   · "Thing Wing Bones" e "Thing Wing Forearm" — é "Thi<b>ng</b>", não "Thin".
///   · "Head1" sem espaço, enquanto "Head 2" e "Head 3" têm.
/// </summary>
public static class DragonBlendShapes
{
    // ---------------------------------------------------------------- CONDIÇÃO
    /// <summary>Magreza — dirigida por DragonGrowth abaixo da condição saudável
    /// (e reforçada pela fome extrema). A asa tem TRÊS chaves, não uma.</summary>
    public static readonly string[] Thin =
    {
        "Arm Thin", "Forearm Thin", "Thigh Thin", "Legs Thin",
        "Neck Thin", "Chest Thin", "Belly Thin", "Thin Tail",
        "Thin Wing Arm", "Thing Wing Forearm", "Thing Wing Bones",
    };

    /// <summary>Gordura — dirigida acima da condição saudável. Note que o peito NÃO
    /// tem versão gorda (só "Chest Thin") e a barriga se chama "Belly Fat", não
    /// "Belly Thick": o mesh é assimétrico e não há como inventar as que faltam.</summary>
    public static readonly string[] Thick =
    {
        "Arm Thick", "Forearm Thick", "Thigh Thick", "Legs Thick",
        "Neck Thick", "Belly Fat",
    };

    // --------------------------------------------------------------- GENÉTICA
    /// <summary>Espinhos — contínuos e independentes (cada um é um gene 0..100).</summary>
    public static readonly string[] Spikes =
    {
        "Arm Spikes", "Legs Spikes", "Spikes Neck", "Spikes Body",
    };

    /// <summary>Membrana entre os dedos da asa — gene contínuo solitário.</summary>
    public const string WingFingers = "Wing Fingers";

    /// <summary>Cabeças — variantes EXCLUSIVAS da mesma parte: o genoma escolhe uma
    /// (ou nenhuma = a cabeça base). Índice do gene: 0 = base, 1..3 = este array.</summary>
    public static readonly string[] Heads = { "Head1", "Head 2", "Head 3" };

    /// <summary>Chifres — idem: 0 = nenhum, 1..2 = este array.</summary>
    public static readonly string[] Horns = { "Horns 1", "Horns 2" };

    // ---------------------------------------------------------------- IGNORAR
    /// <summary>Chaves que o jogo NUNCA dirige — pertencem à animação/rig facial.</summary>
    public static readonly string[] Ignored = { "Blink", "Eye_Fix" };
}

/// <summary>
/// Os índices das blend shapes do catálogo já resolvidos contra os
/// SkinnedMeshRenderer do avatar. Construído uma vez por possessão (o mesh só muda
/// se a skin trocar o renderer) e consultado direto daí em diante.
/// </summary>
public sealed class DragonShapeRig
{
    /// <summary>Um destino concreto: qual renderer e qual índice de shape.</summary>
    public readonly struct Slot
    {
        public readonly SkinnedMeshRenderer renderer;
        public readonly int index;
        public Slot(SkinnedMeshRenderer r, int i) { renderer = r; index = i; }
        public bool Valid => renderer != null && index >= 0;
        public void Set(float weight01to100)
        {
            if (renderer != null && index >= 0) renderer.SetBlendShapeWeight(index, weight01to100);
        }
    }

    public Slot[] Thin { get; private set; } = System.Array.Empty<Slot>();
    public Slot[] Thick { get; private set; } = System.Array.Empty<Slot>();
    public Slot[] Spikes { get; private set; } = System.Array.Empty<Slot>();
    /// <summary>Alinhado com DragonBlendShapes.Heads (gene 1..3 → índice 0..2).</summary>
    public Slot[] Heads { get; private set; } = System.Array.Empty<Slot>();
    /// <summary>Alinhado com DragonBlendShapes.Horns (gene 1..2 → índice 0..1).</summary>
    public Slot[] Horns { get; private set; } = System.Array.Empty<Slot>();
    public Slot WingFingers { get; private set; }

    /// <summary>Resolve o catálogo contra os renderers. `warn` avisa UMA vez, com a
    /// lista completa do que faltou — o sinal de que o avatar não é o Unka realista
    /// (o "Unka Poly Art", por exemplo, usa "Arms Thin" no plural e não casaria).</summary>
    public static DragonShapeRig Build(IList<SkinnedMeshRenderer> renderers, bool warn = true)
    {
        var rig = new DragonShapeRig();
        var missing = warn ? new List<string>() : null;

        rig.Thin = ResolveAll(renderers, DragonBlendShapes.Thin, missing);
        rig.Thick = ResolveAll(renderers, DragonBlendShapes.Thick, missing);
        rig.Spikes = ResolveAll(renderers, DragonBlendShapes.Spikes, missing);
        rig.Heads = ResolveAll(renderers, DragonBlendShapes.Heads, missing);
        rig.Horns = ResolveAll(renderers, DragonBlendShapes.Horns, missing);
        rig.WingFingers = Resolve(renderers, DragonBlendShapes.WingFingers, missing);

        if (missing != null && missing.Count > 0)
        {
            var sb = new StringBuilder("DragonShapeRig: blend shapes não encontradas no avatar (")
                .Append(missing.Count).Append("): ");
            for (int i = 0; i < missing.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(missing[i]).Append('"');
            }
            sb.Append(". O catálogo é do 'Unka.FBX' (realista) — outro modelo Unka " +
                      "(Poly Art/Toon) usa outros nomes.");
            Debug.LogWarning(sb.ToString());
        }
        return rig;
    }

    /// <summary>Mantém o comprimento do array de nomes (slots inválidos ficam no
    /// lugar) — é o que deixa Heads/Horns alinhados com o índice do gene.</summary>
    static Slot[] ResolveAll(IList<SkinnedMeshRenderer> renderers, string[] names, List<string> missing)
    {
        var slots = new Slot[names.Length];
        for (int i = 0; i < names.Length; i++) slots[i] = Resolve(renderers, names[i], missing);
        return slots;
    }

    static Slot Resolve(IList<SkinnedMeshRenderer> renderers, string name, List<string> missing)
    {
        if (renderers != null)
            foreach (var r in renderers)
            {
                var mesh = r != null ? r.sharedMesh : null;
                if (mesh == null) continue;
                int i = mesh.GetBlendShapeIndex(name);
                if (i >= 0) return new Slot(r, i);
            }
        missing?.Add(name);
        return default;
    }

    /// <summary>Zera TODAS as chaves do catálogo — o estado base do dragão
    /// ("todas as chaves como default: 0"), de onde condição e genes partem.</summary>
    public void ResetAll()
    {
        SetAll(Thin, 0f);
        SetAll(Thick, 0f);
        SetAll(Spikes, 0f);
        SetAll(Heads, 0f);
        SetAll(Horns, 0f);
        WingFingers.Set(0f);
    }

    public static void SetAll(Slot[] slots, float weight)
    {
        for (int i = 0; i < slots.Length; i++) slots[i].Set(weight);
    }
}
