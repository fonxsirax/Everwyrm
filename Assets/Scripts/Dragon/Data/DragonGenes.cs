using System;
using UnityEngine;

/// <summary>
/// O GENOMA VISUAL de um dragão — o que a linhagem passa adiante (GDD v2.0: dinastia
/// + genética). Guarda o que é herdado e não muda na vida do indivíduo:
///
///  · MORFOLOGIA — espinhos e membrana da asa como genes CONTÍNUOS (0..100), cabeça e
///    chifres como variantes EXCLUSIVAS (o dragão tem UMA cabeça, não três somadas).
///  · COR — qual material do corpo da paleta, o Tint (que MULTIPLICA o albedo no
///    MalbersStandardPack, então só o claro do espectro serve) e o Hue das asas.
///
/// Não confundir com a CONDIÇÃO (magro/gordo), que é estado de vida e mora no
/// DragonState: gene é o que nasce com o dragão e sobrevive a ele.
///
/// Fica dentro do DragonRecord, então o save modular já leva o genoma junto.
/// </summary>
[Serializable]
public class DragonGenes
{
    /// <summary>Piso por canal do Tint (175/255). Abaixo disso o Multiply do shader
    /// começa a escurecer o dragão inteiro em vez de tingir.</summary>
    public const float DefaultTintFloor = 175f / 255f;

    [Header("Morfologia — contínuos (0..100)")]
    [Range(0f, 100f)] public float armSpikes;
    [Range(0f, 100f)] public float legsSpikes;
    [Range(0f, 100f)] public float neckSpikes;
    [Range(0f, 100f)] public float bodySpikes;
    [Range(0f, 100f)] public float wingFingers;

    [Header("Morfologia — variantes exclusivas")]
    [Tooltip("0 = cabeça base · 1..3 = Head1 / Head 2 / Head 3.")]
    [Range(0, 3)] public int headVariant;
    [Range(0f, 100f)] public float headWeight = 100f;
    [Tooltip("0 = sem chifres · 1..2 = Horns 1 / Horns 2.")]
    [Range(0, 2)] public int hornVariant;
    [Range(0f, 100f)] public float hornWeight = 100f;

    [Header("Cor")]
    [Tooltip("Índice na paleta de materiais do corpo da DragonSkin (Black/Brown/Green).")]
    public int bodyMaterial;
    [Tooltip("_Tint — MULTIPLICA o albedo. Sempre perto do branco (ver tintFloor).")]
    public Color tint = Color.white;
    [Tooltip("_Hue do material das asas (0..1).")]
    [Range(0f, 1f)] public float wingHue;

    /// <summary>Sorteia um genoma novo. `paletteSize` é quantos materiais de corpo a
    /// skin oferece; `tintFloor` é o quão escuro o Tint pode chegar.</summary>
    public static DragonGenes Random(int paletteSize = 3, float tintFloor = DefaultTintFloor)
    {
        int heads = DragonBlendShapes.Heads.Length;
        int horns = DragonBlendShapes.Horns.Length;
        return new DragonGenes
        {
            armSpikes = RollSpike(),
            legsSpikes = RollSpike(),
            neckSpikes = RollSpike(),
            bodySpikes = RollSpike(),
            wingFingers = RollSpike(),

            headVariant = UnityEngine.Random.Range(0, heads + 1),   // 0 = base
            headWeight = UnityEngine.Random.Range(60f, 100f),
            hornVariant = UnityEngine.Random.Range(0, horns + 1),   // 0 = nenhum
            hornWeight = UnityEngine.Random.Range(60f, 100f),

            bodyMaterial = paletteSize > 0 ? UnityEngine.Random.Range(0, paletteSize) : 0,
            tint = RandomTint(tintFloor),
            wingHue = UnityEngine.Random.value,
        };
    }

    /// <summary>Sorteia a partir da skin (usa a paleta e o piso de tint dela).</summary>
    public static DragonGenes Random(DragonSkin skin) =>
        skin == null ? Random()
                     : Random(skin.BodyPaletteSize, skin.tintFloor);

    /// <summary>REPRODUÇÃO — cada gene vem de um dos pais (sorteio mendeliano simples),
    /// com `mutationChance` de sortear um valor inteiramente novo e um leve desvio nos
    /// contínuos, para que irmãos nunca saiam idênticos. A reprodução em si (quem cruza
    /// com quem, quando) é feature posterior; o cruzamento dos dados já vive aqui.</summary>
    public static DragonGenes Breed(DragonGenes a, DragonGenes b,
                                    float mutationChance = 0.08f,
                                    int paletteSize = 3,
                                    float tintFloor = DefaultTintFloor)
    {
        if (a == null && b == null) return Random(paletteSize, tintFloor);
        a ??= b; b ??= a;

        var fresh = Random(paletteSize, tintFloor);   // doador de mutações
        var c = new DragonGenes
        {
            armSpikes = MixContinuous(a.armSpikes, b.armSpikes, fresh.armSpikes, mutationChance),
            legsSpikes = MixContinuous(a.legsSpikes, b.legsSpikes, fresh.legsSpikes, mutationChance),
            neckSpikes = MixContinuous(a.neckSpikes, b.neckSpikes, fresh.neckSpikes, mutationChance),
            bodySpikes = MixContinuous(a.bodySpikes, b.bodySpikes, fresh.bodySpikes, mutationChance),
            wingFingers = MixContinuous(a.wingFingers, b.wingFingers, fresh.wingFingers, mutationChance),

            headVariant = MixDiscrete(a.headVariant, b.headVariant, fresh.headVariant, mutationChance),
            headWeight = MixContinuous(a.headWeight, b.headWeight, fresh.headWeight, mutationChance),
            hornVariant = MixDiscrete(a.hornVariant, b.hornVariant, fresh.hornVariant, mutationChance),
            hornWeight = MixContinuous(a.hornWeight, b.hornWeight, fresh.hornWeight, mutationChance),

            bodyMaterial = MixDiscrete(a.bodyMaterial, b.bodyMaterial, fresh.bodyMaterial, mutationChance),
            wingHue = MixHue(a.wingHue, b.wingHue, fresh.wingHue, mutationChance),
        };

        // Tint canal a canal: um filho pode puxar o vermelho da mãe e o verde do pai.
        var pa = Coin() ? a.tint : b.tint;
        c.tint = new Color(
            MixChannel(a.tint.r, b.tint.r, fresh.tint.r, mutationChance, tintFloor),
            MixChannel(a.tint.g, b.tint.g, fresh.tint.g, mutationChance, tintFloor),
            MixChannel(a.tint.b, b.tint.b, fresh.tint.b, mutationChance, tintFloor),
            pa.a);
        return c;
    }

    /// <summary>Cruza usando a paleta/piso da skin do filho.</summary>
    public static DragonGenes Breed(DragonGenes a, DragonGenes b, DragonSkin skin,
                                    float mutationChance = 0.08f) =>
        skin == null ? Breed(a, b, mutationChance)
                     : Breed(a, b, mutationChance, skin.BodyPaletteSize, skin.tintFloor);

    /// <summary>Nem todo dragão tem espinhos: ~45% nascem sem, o resto varia forte.</summary>
    static float RollSpike() =>
        UnityEngine.Random.value < 0.45f ? 0f : UnityEngine.Random.Range(30f, 100f);

    static Color RandomTint(float floor)
    {
        floor = Mathf.Clamp01(floor);
        return new Color(UnityEngine.Random.Range(floor, 1f),
                         UnityEngine.Random.Range(floor, 1f),
                         UnityEngine.Random.Range(floor, 1f), 1f);
    }

    static bool Coin() => UnityEngine.Random.value < 0.5f;

    static float MixContinuous(float a, float b, float mutated, float mutationChance)
    {
        if (UnityEngine.Random.value < mutationChance) return mutated;
        float v = Coin() ? a : b;
        return Mathf.Clamp(v + UnityEngine.Random.Range(-6f, 6f), 0f, 100f);   // desvio de irmão
    }

    static int MixDiscrete(int a, int b, int mutated, float mutationChance) =>
        UnityEngine.Random.value < mutationChance ? mutated : (Coin() ? a : b);

    static float MixChannel(float a, float b, float mutated, float mutationChance, float floor)
    {
        if (UnityEngine.Random.value < mutationChance) return mutated;
        float v = Coin() ? a : b;
        return Mathf.Clamp(v + UnityEngine.Random.Range(-0.05f, 0.05f), Mathf.Clamp01(floor), 1f);
    }

    /// <summary>Hue é circular: 0.98 e 0.02 são vizinhos, então o desvio dá a volta.</summary>
    static float MixHue(float a, float b, float mutated, float mutationChance)
    {
        if (UnityEngine.Random.value < mutationChance) return mutated;
        float v = Coin() ? a : b;
        return Mathf.Repeat(v + UnityEngine.Random.Range(-0.04f, 0.04f), 1f);
    }
}
