using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aparência de um dragão — a "skin" que a possessão aplica ao avatar Unka. Como
/// todos os dragões da Base compartilham o mesmo rig (avatar único + troca de skin),
/// a variação visual mora aqui.
///
/// Divisão de trabalho com o genoma:
///  · A SKIN define o ESPAÇO possível — quais materiais o corpo pode usar, qual
///    material vai nas asas, quão escuro o Tint pode chegar.
///  · O GENOMA (DragonGenes, no DragonRecord) escolhe UM ponto desse espaço e o
///    passa aos filhos.
///
/// Os materiais entram POR SLOT: o renderer do corpo do Unka tem dois
/// (0 = corpo, 1 = Unka Wings) e o dos olhos tem um só. Escrever em todos os
/// renderers de uma vez apagaria asas e olhos.
///
/// Cor e Hue entram por MaterialPropertyBlock por slot: não corrompem o asset e
/// deixam vários dragões dividirem o mesmo material só mudando as propriedades.
///
/// Menu: Create > Everwyrm > Dragon Skin
/// </summary>
[CreateAssetMenu(menuName = "Everwyrm/Dragon Skin", fileName = "NovaSkin")]
public class DragonSkin : ScriptableObject
{
    [Header("Identidade")]
    public string skinName = "Nova Skin";
    public Sprite icon;                 // opcional — para a Base/ficha

    [Header("Renderer — layout dos slots de material")]
    [Tooltip("Slot do material do CORPO no renderer (Unka: 0).")]
    public int bodySlot = 0;
    [Tooltip("Slot do material das ASAS no renderer (Unka: 1). -1 = não mexer.")]
    public int wingSlot = 1;
    [Tooltip("Quantos slots o renderer do CORPO tem (Unka: 2 — corpo + asas). " +
             "Renderers com menos que isso (os olhos) não são tocados.")]
    public int bodyRendererSlots = 2;

    [Header("Material — paleta (a genética escolhe UM material de corpo)")]
    [Tooltip("Materiais possíveis do corpo, ex.: Unka Body Black / Brown / Green. " +
             "Vazio = mantém o material atual do avatar.")]
    public Material[] bodyPalette = Array.Empty<Material>();
    [Tooltip("Material das asas (Unka Wings). O genoma só varia o _Hue dele. " +
             "Vazio = mantém o atual.")]
    public Material wingMaterial;

    [Header("Cor — limites da variação genética")]
    [Tooltip("_Tint MULTIPLICA o albedo, então RGB baixo escurece o dragão inteiro em " +
             "vez de tingi-lo. Este é o piso por canal: 0.686 = 175/255.")]
    [Range(0f, 1f)] public float tintFloor = DragonGenes.DefaultTintFloor;
    [Tooltip("Propriedade de cor multiplicativa do MalbersStandardPack.")]
    public string tintProperty = "_Tint";
    [Tooltip("Propriedade de matiz do MalbersStandardPack (0..1).")]
    public string hueProperty = "_Hue";

    [Header("Overrides autorais de material (opcionais, aplicados no slot do corpo)")]
    [Tooltip("Cores por propriedade do shader (ex.: _EmissionColor).")]
    public List<MaterialColor> colors = new();
    [Tooltip("Valores float por propriedade (ex.: _Metallic, _Saturation).")]
    public List<MaterialFloat> floats = new();
    [Tooltip("Texturas por propriedade (ex.: _BaseMap, _BumpMap, _MaskMap).")]
    public List<MaterialTexture> textures = new();

    [Header("Aparência — override autoral de blend shapes")]
    [Tooltip("Aplicadas DEPOIS do genoma, por nome EXATO do mesh (ver " +
             "DragonBlendShapes). Serve para travar um visual autoral; deixe vazio " +
             "para que a genética mande sozinha.")]
    public List<AppearanceKey> appearanceKeys = new();

    public int BodyPaletteSize => bodyPalette != null ? bodyPalette.Length : 0;

    /// <summary>O material de corpo que este genoma escolheu (null = manter o atual).</summary>
    public Material BodyMaterialFor(DragonGenes genes)
    {
        if (bodyPalette == null || bodyPalette.Length == 0) return null;
        int i = genes != null ? genes.bodyMaterial : 0;
        return bodyPalette[Mathf.Clamp(i, 0, bodyPalette.Length - 1)];
    }

    /// <summary>Tint do genoma já respeitando o piso desta skin — a última linha de
    /// defesa caso um asset tenha sido editado à mão com uma cor escura demais.</summary>
    public Color ClampTint(Color c)
    {
        float f = Mathf.Clamp01(tintFloor);
        return new Color(Mathf.Clamp(c.r, f, 1f), Mathf.Clamp(c.g, f, 1f), Mathf.Clamp(c.b, f, 1f), c.a);
    }

    [Serializable] public struct MaterialColor { public string property; public Color value; }
    [Serializable] public struct MaterialFloat { public string property; public float value; }
    [Serializable] public struct MaterialTexture { public string property; public Texture value; }
    [Serializable] public struct AppearanceKey { public string shapeName; [Range(0f, 100f)] public float weight; }
}
