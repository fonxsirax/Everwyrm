using UnityEngine;

/// <summary>
/// Efeito de INCÊNDIO num animal: dano de queimadura contínuo (DoT) com chamas
/// visuais presas ao corpo. Reaplicar renova a duração e mantém o maior DPS.
///
/// NOTA: o projeto NÃO possui um asset de fire-propagation instalado — este
/// componente é a implementação própria (leve). Se um asset for adicionado no
/// futuro, basta trocar o interior de Apply() pela chamada de ignição dele.
/// </summary>
public class BurningStatus : MonoBehaviour
{
    const float TickInterval = 0.8f;

    AnimalAgent agent;
    float dps, until, nextTick;
    GameObject flames;

    public static void Apply(AnimalAgent target, float damagePerSecond, float duration)
    {
        if (target == null || target.IsDead) return;
        var b = target.GetComponent<BurningStatus>();
        if (b == null) b = target.gameObject.AddComponent<BurningStatus>();
        b.agent = target;
        b.dps = Mathf.Max(b.dps, damagePerSecond);
        b.until = Mathf.Max(b.until, Time.time + duration);
        if (b.flames == null)
            b.flames = CombatVFX.Flames(target.transform,
                Mathf.Max(0.6f, target.transform.localScale.y));
        b.nextTick = Mathf.Min(b.nextTick <= 0f ? float.MaxValue : b.nextTick,
                               Time.time + TickInterval);
    }

    void Update()
    {
        if (agent == null || agent.IsDead || Time.time >= until) { Extinguish(); return; }

        if (Time.time >= nextTick)
        {
            nextTick = Time.time + TickInterval;
            // "from" = a própria posição: o animal entra em pânico sem direção fixa
            agent.TakeHit(dps * TickInterval, transform.position);
        }
    }

    void Extinguish()
    {
        if (flames != null)
        {
            foreach (var ps in flames.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(flames, 1.5f);   // deixa as últimas partículas morrerem
        }
        Destroy(this);
    }

    void OnDestroy()
    {
        if (flames != null) Destroy(flames, 1.5f);
    }
}
