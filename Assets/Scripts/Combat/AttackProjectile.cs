using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Projétil genérico data-driven: TODO o comportamento vem do DragonAttackData
/// (velocidade, vida, raio de acerto, explosão, área, incêndio, visual).
///
/// Detecção por DISTÂNCIA contra AnimalAgent.All — o padrão de combate do
/// projeto (nada usa Physics para dano) — + impacto no terreno procedural via
/// InfiniteTerrain.HeightAt. Single target: atinge apenas UM inimigo por vez.
/// </summary>
public class AttackProjectile : MonoBehaviour
{
    DragonAttackData data;
    Transform source;
    Vector3 velocity;
    float dieAt, damage, areaRadius, hitRadius;

    public static AttackProjectile Launch(DragonAttackData data, Vector3 origin,
                                          Vector3 dir, float damage, float areaRadius,
                                          float scale, Transform source)
    {
        var go = new GameObject($"Projétil {data.attackName}");
        go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir));

        var p = go.AddComponent<AttackProjectile>();
        p.data = data;
        p.source = source;
        p.velocity = dir.normalized * data.projectileSpeed;
        p.dieAt = Time.time + data.EffectiveLifetime;
        p.damage = damage;
        p.areaRadius = areaRadius;
        p.hitRadius = data.projectileHitRadius * Mathf.Max(1f, scale * 0.8f);

        if (data.projectilePrefab != null)
            Instantiate(data.projectilePrefab, origin, go.transform.rotation, go.transform);
        else
            CombatVFX.ProjectileGlow(go.transform,
                data.isFire ? CombatVFX.FireColor : new Color(0.8f, 0.9f, 1f),
                0.35f * Mathf.Max(1f, scale * 0.7f));
        return p;
    }

    void Update()
    {
        transform.position += velocity * Time.deltaTime;

        if (Time.time >= dieAt) { Impact(null, ground: false); return; }

        // um inimigo por vez: o mais próximo dentro do raio de acerto
        var hit = AnimalAgent.FindNearest(transform.position, hitRadius);
        if (hit != null) { Impact(hit, ground: false); return; }

        // terreno procedural (fallback: raycast para cenas sem InfiniteTerrain)
        var world = InfiniteTerrain.Instance;
        float ground = world != null
            ? world.HeightAt(transform.position.x, transform.position.z)
            : (Physics.Raycast(transform.position, Vector3.down, out var rc, 0.6f)
                ? rc.point.y : float.MinValue);
        if (transform.position.y <= ground + 0.2f) Impact(null, ground: true);
    }

    void Impact(AnimalAgent direct, bool ground)
    {
        Vector3 p = transform.position;

        if (data.explodesOnImpact || data.areaDamage)
        {
            // explosão: dano em área + incêndio em todos os afetados
            CombatDamage.DamageArea(p, areaRadius, damage, source, data);

            if (data.vfxPrefab != null)
                Destroy(Instantiate(data.vfxPrefab, p, Quaternion.identity), 6f);
            else
                CombatVFX.Burst(p, areaRadius,
                    data.isFire ? CombatVFX.FireColor : new Color(1f, 0.9f, 0.6f));

            if (data.spawnsFireArea)
                FireArea.Spawn(p, areaRadius, data.fireAreaDuration,
                               data.burnDamagePerSecond, data.burnDuration, data.vfxPrefab);
        }
        else if (direct != null)
        {
            // single target (FireBall): só este inimigo leva o dano
            direct.TakeHit(damage, source != null ? source.position : p);
            if (data.appliesBurn && !direct.IsDead)
                BurningStatus.Apply(direct, data.burnDamagePerSecond, data.burnDuration);
            CombatVFX.Burst(p, 1.2f,
                data.isFire ? CombatVFX.FireColor : new Color(1f, 0.9f, 0.6f));
        }
        else if (ground && data.isFire)
        {
            CombatVFX.Burst(p, 1f, CombatVFX.FireColor);   // fogo espirra no chão
        }

        Destroy(gameObject);
    }
}

/// <summary>Dano em área compartilhado por projéteis, Incinerate e afins.</summary>
public static class CombatDamage
{
    /// <summary>Fere todos os animais vivos no raio; aplica incêndio se o ataque pedir.</summary>
    public static int DamageArea(Vector3 center, float radius, float damage,
                                 Transform source, DragonAttackData data)
    {
        int hits = 0;
        // snapshot: TakeHit pode matar/alterar a lista global durante a iteração
        var snapshot = new List<AnimalAgent>(AnimalAgent.All);
        foreach (var a in snapshot)
        {
            if (a == null || a.IsDead) continue;
            if ((a.transform.position - center).sqrMagnitude > radius * radius) continue;

            a.TakeHit(damage, source != null ? source.position : center);
            if (data != null && data.appliesBurn && !a.IsDead)
                BurningStatus.Apply(a, data.burnDamagePerSecond, data.burnDuration);
            hits++;
        }
        return hits;
    }
}
