using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SilverpineSkills;

// Wrap the timed work scheduled by these four native player interactions. NPC work,
// explosions and cancellation take other paths. The original completion still owns the work.
[HarmonyPatch]
internal static class FieldWork
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var type in new[] { typeof(Tree), typeof(ResourceNode), typeof(QuarryResourceNode), typeof(ConstructionSite) })
            yield return AccessTools.Method(type, "OnInteract", new[] { typeof(GameObject) });
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var start = AccessTools.Method(typeof(Player), nameof(Player.StartLongInteraction));
        int found = 0;
        foreach (var instruction in instructions)
        {
            if (!instruction.Calls(start)) { yield return instruction; continue; }
            found++;
            yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FieldWork), nameof(Start)));
        }
        if (found != 1) throw new InvalidOperationException("Expected one native timed field interaction.");
    }

    private static void Start(Player player, Action complete, int turns, Action cancel, Action finish, MonoBehaviour source)
    {
        SkillTree tree = source switch {
            Tree => SkillTree.Woodcutting,
            QuarryResourceNode quarry => quarry.InteractionName == "Saw" ? SkillTree.Woodcutting : SkillTree.Mining,
            ResourceNode node when node.resourceNodeType == ResourceNodeType.Ore => SkillTree.Mining,
            ConstructionSite => SkillTree.Construction,
            _ => null
        };
        if (tree != null && SkillsApi.Current(player))
        {
            int Rank(string id) => SkillsApi.GetPerkRank(player, tree.Id, id);
            int mastery = Rank("mastery");
            int speed = 10 * (Rank("efficient") + mastery);
            int recovery = 2 * (Rank("second-wind") + mastery);
            if (source is Tree || source is ResourceNode || source is ConstructionSite && turns >= 20)
                speed += 15 * Rank("technique");
            if (source is QuarryResourceNode || source is ConstructionSite)
                recovery += 4 * Rank("rhythm");
            if (turns > 0 && speed > 0) turns = Math.Max(1, (int)((long)turns * (100 - speed) / 100));
            int epoch = SkillsApi.Epoch;
            bool credited = false;
            complete += () => {
                if (credited || epoch != SkillsApi.Epoch || !SkillsApi.Current(player)) return;
                credited = true;
                if (recovery > 0) player.energy.Value += recovery;
                SkillsApi.AwardExperience(player, tree.Id, tree == SkillTree.Construction ? 15 : 10);
            };
        }
        player.StartLongInteraction(complete, turns, cancel, finish);
    }
}
