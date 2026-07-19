using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Campo de correntes de ar do mundo. Qualquer sistema pode registrar um provedor
/// (updrafts de montanha, térmicas, clima, rasante...) e o voo consulta
/// AirflowField.Sample(posição) — modular por design.
/// </summary>
public interface IAirflowProvider
{
    Vector3 Sample(Vector3 worldPos);
}

public static class AirflowField
{
    static readonly List<IAirflowProvider> providers = new();

    public static void Register(IAirflowProvider p) { if (!providers.Contains(p)) providers.Add(p); }
    public static void Unregister(IAirflowProvider p) => providers.Remove(p);

    public static Vector3 Sample(Vector3 worldPos)
    {
        Vector3 sum = Vector3.zero;
        foreach (var p in providers) sum += p.Sample(worldPos);
        return sum;
    }
}

/// <summary>
/// Updrafts nas Montanhas Rochosas (GDD: "correntes de vento fortes").
/// Planar sobre encostas de montanha empurra o dragão pra cima — recuperar
/// altitude de graça, se o jogador souber usar o relevo.
/// </summary>
public class MountainUpdrafts : MonoBehaviour, IAirflowProvider
{
    [SerializeField] float strength = 5f;          // m/s no núcleo da corrente
    [SerializeField] float maxHeightAboveGround = 130f;
    [SerializeField, Range(0f, 1f)] float minMountainWeight = 0.25f;

    void OnEnable() => AirflowField.Register(this);
    void OnDisable() => AirflowField.Unregister(this);

    public Vector3 Sample(Vector3 pos)
    {
        var world = InfiniteTerrain.Instance;
        if (world == null) return Vector3.zero;

        world.BiomeWeights(pos.x, pos.z, out _, out _, out float mount, out _, out _);
        if (mount < minMountainWeight) return Vector3.zero;

        float above = pos.y - world.HeightAt(pos.x, pos.z);
        if (above < 0f || above > maxHeightAboveGround) return Vector3.zero;

        float falloff = 1f - above / maxHeightAboveGround;   // forte perto da encosta
        return Vector3.up * (strength * mount * falloff);
    }
}
