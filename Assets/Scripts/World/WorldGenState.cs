using System;
using UnityEngine;

/// <summary>
/// A IDENTIDADE serializável do mundo procedural — o conjunto MÍNIMO de dados do
/// qual o InfiniteTerrain reconstrói o mundo inteiro, tile por tile, idêntico.
///
/// O contrato de dados do mundo tem três camadas:
///  1. IDENTIDADE (este objeto): o que varia de mundo para mundo — a seed e o
///     que foi decidido em runtime na criação (hoje, só o centro do lago
///     inicial). É o que um save grava e o que um servidor autoritativo
///     enviaria para os clientes reconstruírem o terreno (poucos bytes).
///  2. CONFIGURAÇÃO DE AUTORIA: todos os sliders/curvas/prefabs do
///     InfiniteTerrain no Inspector. Viajam com a build — todo cliente da mesma
///     versão tem exatamente os mesmos valores, então NÃO entram aqui de
///     propósito (um save não deve congelar tuning de design antigo).
///  3. PROJEÇÃO: os tiles de Terrain na cena. São descartáveis por design —
///     destruídos ao ficarem longe e reconstruídos bit a bit ao voltar. Nunca
///     são serializados.
///
/// Uso: InfiniteTerrain.CaptureState() ao salvar; ApplyState() após carregar a
/// cena (antes ou depois do primeiro tile — tiles existentes são descartados e
/// renascem do estado novo).
/// </summary>
[Serializable]
public class WorldGenState
{
    /// <summary>Versão do formato — incremente ao mudar os campos para migrar saves antigos.</summary>
    public int version = 1;

    /// <summary>Seed de onde TODO o resto do mundo deriva (alturas, biomas, riachos, vegetação).</summary>
    public int seed;

    /// <summary>O lago garantido do spawn foi definido? (Decisão de runtime da primeira sessão.)</summary>
    public bool startLakeSet;

    /// <summary>Centro XZ do lago garantido — sem isso um load moveria o lago (e o terreno) do spawn.</summary>
    public Vector2 startLakeCenter;

    public string ToJson() => JsonUtility.ToJson(this);
    public static WorldGenState FromJson(string json) => JsonUtility.FromJson<WorldGenState>(json);
}
