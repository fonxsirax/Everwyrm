using System.Collections;
using UnityEngine;

/// <summary>
/// Pausa de impacto (hit stop) — congela o tempo por alguns centésimos quando um
/// golpe CONECTA, vendendo o peso do impacto (padrão de hack and slash).
/// Usa tempo real para voltar, então funciona dentro do próprio slow.
/// Respeita a pausa da ficha (DragonStatsMenu controla o timeScale dele).
/// </summary>
public static class HitStop
{
    class Runner : MonoBehaviour { }

    static Runner runner;

    /// <summary>Hit stop padrão de golpe corpo a corpo.</summary>
    public static void Hit() => Do(0.06f, 0.25f);

    public static void Do(float duration, float scale)
    {
        if (DragonStatsMenu.IsOpen) return;        // ficha pausada manda no timeScale
        if (runner == null)
        {
            var go = new GameObject("Hit Stop") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }
        runner.StopAllCoroutines();
        runner.StartCoroutine(Co(duration, scale));
    }

    static IEnumerator Co(float duration, float scale)
    {
        Time.timeScale = scale;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = DragonStatsMenu.IsOpen ? 0f : 1f;
    }
}
