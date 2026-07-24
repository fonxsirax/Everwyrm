using UnityEditor;
using UnityEngine;

/// <summary>
/// Setup do sistema de combate por habilidades (teclas 1-4).
/// Menu: Tools > Dragão > Combate — Habilidades (1-4)
///  1. Cria a pasta Resources/Entities/Attacks e os ataques de exemplo
///     (FireBall, Great Fire Ball, Incinerate + um melee físico);
///  2. Regenera o Animator (transições de saída p/ voo) e o prefab do dragão
///     (agora com DragonAbilities).
/// Rodar de novo é seguro: não sobrescreve assets que já existem.
/// </summary>
public static class DragonCombatSetup
{
    const string Dir = "Assets/Scriptables/Resources/Entities/Attacks";

    // (menu removido na limpeza de jul/2026 — assets de ataque já criados em
    //  Resources/Entities/Attacks; para rodar de novo, re-adicione o [MenuItem] aqui)
    public static void Setup()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/Resources", "Attacks");

        int created = 0;

        created += CreateIfMissing("FireBall", a =>
        {
            a.attackName = "Bola de Fogo";
            a.description = "Cospe uma bola de fogo veloz que atinge UM alvo.";
            a.unlockLevel = 2;
            a.energyCost = 12f;
            a.baseDamage = 18f;
            a.cooldown = 2.5f;
            a.castTime = 0.55f;
            a.animationLock = 1.2f;
            a.animation = DragonAttackAnimation.FireBreath;
            a.usableInFlight = true;
            a.isPhysical = false;
            a.isFire = true;
            a.range = 60f;                    // distância máxima do projétil
            a.usesProjectile = true;
            a.projectileSpeed = 32f;
            a.projectileHitRadius = 1.1f;
            a.explodesOnImpact = false;       // single target, sem área
            a.areaDamage = false;
            a.appliesBurn = false;
        });

        created += CreateIfMissing("GarrasSelvagens", a =>
        {
            a.attackName = "Garras Selvagens";
            a.description = "Golpe físico pesado de garras — um alvo à frente.";
            a.unlockLevel = 3;
            a.energyCost = 6f;
            a.baseDamage = 26f;
            a.cooldown = 1.6f;
            a.castTime = 0.35f;
            a.animationLock = 1.0f;
            a.animation = DragonAttackAnimation.ClawsLeft;
            a.usableInFlight = false;
            a.isPhysical = true;
            a.isFire = false;
            a.range = 5f;
        });

        created += CreateIfMissing("GreatFireBall", a =>
        {
            a.attackName = "Grande Bola de Fogo";
            a.description = "Bola de fogo lenta que EXPLODE no impacto, " +
                            "queimando todos na área.";
            a.unlockLevel = 4;
            a.energyCost = 25f;
            a.baseDamage = 30f;
            a.cooldown = 6f;
            a.castTime = 0.7f;
            a.animationLock = 1.5f;
            a.animation = DragonAttackAnimation.FireBreath;
            a.usableInFlight = true;
            a.isPhysical = false;
            a.isFire = true;
            a.range = 70f;
            a.usesProjectile = true;
            a.projectileSpeed = 24f;
            a.projectileHitRadius = 1.5f;
            a.explodesOnImpact = true;
            a.areaDamage = true;
            a.areaRadius = 7f;
            a.appliesBurn = true;
            a.burnDamagePerSecond = 4f;
            a.burnDuration = 5f;
        });

        created += CreateIfMissing("Incinerate", a =>
        {
            a.attackName = "Incinerar";
            a.description = "O chão ao redor do dragão entra em combustão — " +
                            "quem entrar na área pega fogo.";
            a.unlockLevel = 6;
            a.energyCost = 35f;
            a.baseDamage = 22f;
            a.cooldown = 12f;
            a.castTime = 1.0f;
            a.animationLock = 1.9f;
            a.animation = DragonAttackAnimation.FireBreath;
            a.usableInFlight = false;
            a.isPhysical = false;
            a.isFire = true;
            a.range = 0f;
            a.areaDamage = true;
            a.areaRadius = 9f;
            a.appliesBurn = true;
            a.burnDamagePerSecond = 5f;
            a.burnDuration = 6f;
            a.spawnsFireArea = true;
            a.fireAreaDuration = 10f;
        });

        // Animator (saídas de ataque p/ voo) + prefab (DragonAbilities)
        DragonSetup.RegenerateAnimatorAndPrefab();
        AssetDatabase.SaveAssets();

        Debug.Log($"<b>Combate pronto!</b> {created} ataque(s) criado(s) em {Dir}. " +
                  "Eles destravam por nível e entram sozinhos nas teclas 1-4. " +
                  "Novo ataque = novo asset (Create > Everwyrm > Dragon Attack) na mesma pasta.");
    }

    /// <summary>1 se criou, 0 se já existia (idempotente).</summary>
    static int CreateIfMissing(string file, System.Action<DragonAttackData> init)
    {
        string path = $"{Dir}/{file}.asset";
        if (AssetDatabase.LoadAssetAtPath<DragonAttackData>(path) != null) return 0;
        var a = ScriptableObject.CreateInstance<DragonAttackData>();
        init(a);
        AssetDatabase.CreateAsset(a, path);
        return 1;
    }
}
