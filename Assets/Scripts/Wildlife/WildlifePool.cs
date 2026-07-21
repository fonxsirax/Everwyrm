using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pool de corpos da fauna — recicla GameObjects (skinned meshes são caras de
/// instanciar), nunca estado: quem renasce ganha um Init() completo no
/// AnimalAgent. Criado pelo WildlifeSpawner no Awake; Release aceita qualquer
/// objeto (se não nasceu do pool, destrói — o comportamento antigo).
/// </summary>
public class WildlifePool : MonoBehaviour
{
    static WildlifePool instance;

    readonly Dictionary<GameObject, Stack<GameObject>> free = new();
    readonly Dictionary<GameObject, GameObject> sourceOf = new();   // instância → prefab

    public static void Ensure(GameObject host)
    {
        if (instance == null)
        {
            instance = host.GetComponent<WildlifePool>();
            if (instance == null) instance = host.AddComponent<WildlifePool>();
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public static GameObject Get(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (instance != null && instance.free.TryGetValue(prefab, out var stack))
            while (stack.Count > 0)
            {
                var go = stack.Pop();
                if (go == null) continue;   // troca de cena destruiu o inativo
                go.transform.SetPositionAndRotation(pos, rot);
                go.SetActive(true);         // OnEnable re-registra em AnimalAgent.All
                return go;
            }

        var fresh = Instantiate(prefab, pos, rot);
        if (instance != null) instance.sourceOf[fresh] = prefab;
        return fresh;
    }

    public static void Release(GameObject go)
    {
        if (instance == null || !instance.sourceOf.TryGetValue(go, out var prefab))
        {
            Destroy(go);
            return;
        }
        go.SetActive(false);                // OnDisable sai de AnimalAgent.All
        if (!instance.free.TryGetValue(prefab, out var stack))
            instance.free[prefab] = stack = new Stack<GameObject>();
        stack.Push(go);
    }
}
