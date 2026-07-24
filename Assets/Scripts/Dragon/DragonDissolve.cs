using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// O "dissolver" da morte: ao cair, o corpo do dragão se desfaz gradualmente antes de
/// a Base assumir o próximo. Se o material tiver uma propriedade de dissolve
/// (_DissolveAmount / _Dissolve / _DissolveValue), ela é animada de 0→1; senão a
/// animação de morte simplesmente roda pelo tempo da dissolução (sem efeito visual)
/// e o controle é passado adiante mesmo assim.
///
/// Usa INSTÂNCIAS de material (renderer.materials) para nunca escrever no asset
/// compartilhado da skin — a re-possessão troca o sharedMaterial e recomeça limpo.
/// </summary>
public class DragonDissolve : MonoBehaviour
{
    [SerializeField] float duration = 2f;
    [SerializeField] string[] dissolveProps = { "_DissolveAmount", "_Dissolve", "_DissolveValue" };

    SkinnedMeshRenderer[] renderers;
    readonly List<(Material mat, int prop)> targets = new();
    bool dissolving;
    float t;
    Action onComplete;

    void Awake() => renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);

    /// <summary>Começa a dissolver; chama `onComplete` ao terminar (a Base possui o próximo).</summary>
    public void Play(Action onComplete)
    {
        this.onComplete = onComplete;
        Collect();
        t = 0f;
        dissolving = true;
    }

    /// <summary>Zera o dissolve (nova possessão reusa o avatar).</summary>
    public void ResetDissolve()
    {
        dissolving = false;
        onComplete = null;
        Collect();
        foreach (var (mat, prop) in targets) if (mat != null) mat.SetFloat(prop, 0f);
    }

    void Collect()
    {
        targets.Clear();
        if (renderers == null) return;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            foreach (var m in r.materials)          // instâncias — não toca o asset da skin
            {
                if (m == null) continue;
                foreach (var pn in dissolveProps)
                    if (m.HasProperty(pn)) { targets.Add((m, Shader.PropertyToID(pn))); break; }
            }
        }
    }

    void Update()
    {
        if (!dissolving) return;
        t += Time.deltaTime;
        float k = duration <= 0f ? 1f : Mathf.Clamp01(t / duration);
        foreach (var (mat, prop) in targets) if (mat != null) mat.SetFloat(prop, k);
        if (k >= 1f)
        {
            dissolving = false;
            var cb = onComplete; onComplete = null;
            cb?.Invoke();
        }
    }
}
