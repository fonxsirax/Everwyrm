using UnityEngine;

/// <summary>
/// A "cola" da possessão — vive no avatar único Unka e é o elo entre o dado
/// (DragonRecord) e os componentes vivos. Possuir um dragão = aplicar a skin,
/// carregar o estado em cada componente, reviver e posicionar. Ao morrer (vida→0
/// OU velhice), grava o estado de volta, dissolve o corpo e avisa a Base para
/// assumir o próximo — sem nunca recarregar a cena.
///
/// Overrides de material (cor/float/textura da skin) entram por MaterialPropertyBlock:
/// não corrompem o asset e permitem vários dragões sobre o mesmo material.
/// </summary>
[RequireComponent(typeof(DragonController))]
public class DragonPossession : MonoBehaviour
{
    DragonController controller;
    DragonVitals vitals;
    DragonGrowth growth;
    DragonAttributes attrs;
    DragonAbilities abilities;
    DragonTraits traits;
    DragonDissolve dissolve;
    SkinnedMeshRenderer[] renderers;
    MaterialPropertyBlock mpb;

    public DragonRecord Current { get; private set; }
    bool down;   // guard: uma só descida por possessão (morte não reentra)

    void Awake()
    {
        controller = GetComponent<DragonController>();
        vitals = GetComponent<DragonVitals>();
        growth = GetComponent<DragonGrowth>();
        attrs = GetComponent<DragonAttributes>();
        abilities = GetComponent<DragonAbilities>();
        traits = GetComponent<DragonTraits>();
        dissolve = GetComponent<DragonDissolve>();
        if (dissolve == null) dissolve = gameObject.AddComponent<DragonDissolve>();
        renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        if (vitals != null) vitals.OnDeath += OnVitalsDeath;
        if (growth != null) growth.OnNaturalDeath += OnOldAge;
    }

    void OnDisable()
    {
        if (vitals != null) vitals.OnDeath -= OnVitalsDeath;
        if (growth != null) growth.OnNaturalDeath -= OnOldAge;
    }

    /// <summary>Possui um dragão: aplica skin, carrega estado, revive e posiciona.</summary>
    public void Bind(DragonRecord record, Vector3 position, Quaternion rotation)
    {
        if (record == null) return;
        if (Current != null && Current != record) WriteBack();   // sela o anterior

        Current = record;
        down = false;

        ApplySkin(record.skin, record.genes);
        dissolve?.ResetDissolve();

        // ordem importa: growth primeiro (idade/crescimento definem a MATURIDADE),
        // depois atributos (derivados dela), TRAÇOS (a vida/energia máximas dependem
        // deles), vitais (frações × máximos) e o loadout. O DragonTraits nasce no
        // Awake do Controller, mas o Bind roda bem depois de todos os Awake — buscar
        // aqui (não no Awake) evita cachear null pela ordem indefinida de Awake.
        if (traits == null) traits = GetComponent<DragonTraits>();
        growth?.LoadFrom(record);
        attrs?.LoadFrom(record);
        traits?.LoadFrom(record);
        vitals?.LoadFrom(record);
        abilities?.LoadFrom(record);

        transform.SetPositionAndRotation(position, rotation);
        controller.Revive();
        vitals?.Revive();

        WriteBack();   // primeiro nascimento: sela os defaults no state
    }

    /// <summary>Grava o estado vivo do avatar de volta no record atual (save/troca/morte).</summary>
    public void WriteBack()
    {
        if (Current == null) return;
        // atributos não entram: são derivados do corpo (crescimento/idade), que o
        // DragonGrowth grava — não há nível nem pontos para persistir
        var s = Current.state;
        growth?.WriteTo(s);
        vitals?.WriteTo(s);
        abilities?.WriteTo(s);
    }

    // ------------------------------------------------------------------ SKIN
    /// <summary>Veste o avatar com a skin e a cor do genoma. Tudo POR SLOT: o renderer
    /// do corpo do Unka tem dois materiais (0 = corpo, 1 = Unka Wings) e o dos olhos tem
    /// um só — escrever em todos os renderers de uma vez apagaria asas e olhos.</summary>
    void ApplySkin(DragonSkin skin, DragonGenes genes)
    {
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;

            // só o renderer do CORPO tem o layout completo de slots; os olhos ficam intactos
            bool isBody = skin != null && mats.Length >= Mathf.Max(1, skin.bodyRendererSlots);
            if (!isBody) continue;

            var body = skin.BodyMaterialFor(genes);
            bool changed = false;
            if (body != null && InRange(skin.bodySlot, mats.Length)) { mats[skin.bodySlot] = body; changed = true; }
            if (skin.wingMaterial != null && InRange(skin.wingSlot, mats.Length))
            { mats[skin.wingSlot] = skin.wingMaterial; changed = true; }
            if (changed) r.sharedMaterials = mats;

            ApplyMaterialOverrides(r, mats.Length, skin, genes);
        }

        growth?.SetSkin(skin);   // reconstrói o rig de blend shapes na malha vestida
    }

    static bool InRange(int slot, int count) => slot >= 0 && slot < count;

    /// <summary>Cor por slot via MaterialPropertyBlock — por-dragão, sem tocar o asset,
    /// então três dragões dividem "Unka Body Black" com tinturas diferentes.
    ///
    ///  · CORPO — _Tint (multiplica o albedo no MalbersStandardPack; a skin garante o
    ///    piso para não escurecer o dragão) mais os overrides autorais da skin.
    ///  · ASAS  — só o _Hue, o giro de matiz que o genoma herda.
    ///
    /// Os blocos são reescritos inteiros a cada possessão: nada do dragão anterior fica.</summary>
    void ApplyMaterialOverrides(SkinnedMeshRenderer r, int slotCount, DragonSkin skin, DragonGenes genes)
    {
        if (InRange(skin.bodySlot, slotCount))
        {
            mpb.Clear();
            if (genes != null && !string.IsNullOrEmpty(skin.tintProperty))
                mpb.SetColor(skin.tintProperty, skin.ClampTint(genes.tint));
            foreach (var c in skin.colors)
                if (!string.IsNullOrEmpty(c.property)) mpb.SetColor(c.property, c.value);
            foreach (var f in skin.floats)
                if (!string.IsNullOrEmpty(f.property)) mpb.SetFloat(f.property, f.value);
            foreach (var t in skin.textures)
                if (!string.IsNullOrEmpty(t.property) && t.value != null) mpb.SetTexture(t.property, t.value);
            r.SetPropertyBlock(mpb, skin.bodySlot);
        }

        if (InRange(skin.wingSlot, slotCount))
        {
            mpb.Clear();
            if (genes != null && !string.IsNullOrEmpty(skin.hueProperty))
                mpb.SetFloat(skin.hueProperty, Mathf.Clamp01(genes.wingHue));
            r.SetPropertyBlock(mpb, skin.wingSlot);
        }
    }

    // ------------------------------------------------------------------ MORTE
    void OnVitalsDeath() => HandleDown(false);
    void OnOldAge() => HandleDown(true);

    /// <summary>O dragão atual caiu (combate ou velhice): grava, marca morto, dissolve
    /// e passa a vez para o próximo via Base. O guard evita reentrância (Kill dispara
    /// OnDeath de novo).</summary>
    void HandleDown(bool oldAge)
    {
        if (down) return;
        down = true;

        if (Current != null)
        {
            if (oldAge) Current.state.isDeadOfOldAge = true;
            if (vitals != null && !vitals.IsDead) vitals.Kill();  // velhice: impõe 0 de vida
            WriteBack();
            Current.state.isDead = true;                          // fica guardado, morto
        }

        if (dissolve != null) dissolve.Play(() => DragonBase.Instance?.OnCurrentDown());
        else DragonBase.Instance?.OnCurrentDown();
    }
}
