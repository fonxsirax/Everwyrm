using UnityEngine;

/// <summary>
/// Área de fogo TEMPORÁRIA deixada por ataques (Incinerate, Great Fire Ball
/// com spawnsFireArea): gruda no chão, incendeia (BurningStatus) todo animal
/// que entrar, e se apaga sozinha ao fim da duração.
///
/// Criada por código: FireArea.Spawn(...) — nenhum prefab necessário.
/// </summary>
public class FireArea : MonoBehaviour
{
    const float TickInterval = 0.4f;

    float radius, endsAt, burnDps, burnDuration, nextTick;
    bool fading;

    public static FireArea Spawn(Vector3 center, float radius, float duration,
                                 float burnDps, float burnDuration, GameObject vfxPrefab)
    {
        // assenta no terreno procedural (fallback: raycast)
        var world = InfiniteTerrain.Instance;
        if (world != null)
            center.y = world.HeightAt(center.x, center.z);
        else if (Physics.Raycast(center + Vector3.up * 30f, Vector3.down, out var hit, 120f))
            center.y = hit.point.y;

        var go = new GameObject("Área de Fogo");
        go.transform.position = center;

        var area = go.AddComponent<FireArea>();
        area.radius = radius;
        area.endsAt = Time.time + duration;
        area.burnDps = burnDps;
        area.burnDuration = burnDuration;

        if (vfxPrefab != null)
        {
            var vfx = Instantiate(vfxPrefab, center, Quaternion.identity, go.transform);
            vfx.transform.localScale = Vector3.one * Mathf.Max(1f, radius * 0.35f);
        }
        else
            CombatVFX.GroundFire(go.transform, radius);

        return area;
    }

    void Update()
    {
        if (Time.time >= endsAt) { FadeOut(); return; }
        if (Time.time < nextTick) return;
        nextTick = Time.time + TickInterval;

        // quem entrar na área pega fogo — a queimadura continua mesmo se sair
        foreach (var a in AnimalAgent.All)
        {
            if (a == null || a.IsDead) continue;
            Vector3 d = a.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= radius * radius)
                BurningStatus.Apply(a, burnDps, burnDuration);
        }
    }

    void FadeOut()
    {
        if (fading) return;
        fading = true;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>())
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(gameObject, 2.5f);
    }
}
