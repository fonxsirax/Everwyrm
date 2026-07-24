using UnityEngine;

/// <summary>
/// Nomes de dragão sorteados por sílabas. Existe porque a Base gera dragões
/// aleatórios a cada Play (e o setup gera vários de uma vez): uma lista fixa
/// repetiria rápido demais e dois "Aurélio" na mesma pasta viram confusão na hora de
/// guardar os que prestaram.
/// </summary>
public static class DragonNames
{
    static readonly string[] Head = { "Aur", "Bris", "Corv", "Dral", "Emb", "Fen", "Gorm", "Hel",
                                      "Ith", "Kaer", "Lum", "Mor", "Nyx", "Oss", "Pyr", "Quor",
                                      "Rav", "Sil", "Thar", "Umb", "Vor", "Wyr", "Xar", "Zeph" };
    static readonly string[] Tail = { "élio", "isa", "us", "an", "or", "ith", "ara", "ion",
                                      "eth", "ux", "ynn", "ora", "iel", "ak", "une", "essa" };

    public static string Random() =>
        Head[UnityEngine.Random.Range(0, Head.Length)] +
        Tail[UnityEngine.Random.Range(0, Tail.Length)];
}
