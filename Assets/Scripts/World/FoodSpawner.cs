using UnityEngine;

/// <summary>
/// Mantém carcaças espalhadas ao redor do jogador (placeholder até existir caça).
/// Spawna sobre o terreno via raycast, respeitando um limite ativo.
/// </summary>
public class FoodSpawner : MonoBehaviour
{
    public Transform target;                       // o dragão
    [SerializeField] int maxActive = 8;
    [SerializeField] float spawnRadius = 130f;
    [SerializeField] float minDistance = 20f;
    [SerializeField] float spawnInterval = 6f;
    [SerializeField] Vector2 nutritionRange = new(25f, 55f);
    [SerializeField] LayerMask groundMask = 0;

    float nextSpawn;

    void Start()
    {
        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
        }
        if (groundMask == 0)
            groundMask = Physics.DefaultRaycastLayers &
                         ~(target != null ? 1 << target.gameObject.layer : 0);

        // leva inicial para o mundo não nascer vazio
        for (int i = 0; i < maxActive / 2; i++) TrySpawn();
    }

    void Update()
    {
        if (Time.time < nextSpawn || Carcass.All.Count >= maxActive) return;
        nextSpawn = Time.time + spawnInterval;
        TrySpawn();
    }

    void TrySpawn()
    {
        if (target == null) return;

        Vector2 dir = Random.insideUnitCircle.normalized;
        float dist = Random.Range(minDistance, spawnRadius);
        Vector3 pos = target.position + new Vector3(dir.x, 0f, dir.y) * dist;

        // mundo procedural: altura exata da superfície gerada (nada de comida voando)
        if (InfiniteTerrain.Instance != null)
        {
            var world = InfiniteTerrain.Instance;
            pos.y = world.HeightAt(pos.x, pos.z);
            if (world.HasLakes && pos.y < world.WaterLevel + 0.3f) return;   // não spawna no lago
            Carcass.Spawn(pos, Random.Range(nutritionRange.x, nutritionRange.y));
            return;
        }

        // cena com terrain fixo: raycast
        if (Physics.Raycast(pos + Vector3.up * 150f, Vector3.down, out RaycastHit hit, 400f,
                groundMask, QueryTriggerInteraction.Ignore))
            Carcass.Spawn(hit.point, Random.Range(nutritionRange.x, nutritionRange.y));
    }
}
