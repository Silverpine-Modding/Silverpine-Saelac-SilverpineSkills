using SilverpineMods.CustomGrowables;
using UnityEngine;

namespace SilverpineSkills;

internal static class GardeningSkills
{
    private static bool IsPlayer(GameObject actor) => SkillsApi.Current(Player.Instance) && actor == Player.Instance.gameObject;

    internal static void Install()
    {
        var tree = SkillTree.Gardening;
        GrowableApi.Planted += (_, args) => {
            if (IsPlayer(args.Actor)) SkillsApi.AwardExperience(Player.Instance, tree.Id, 4);
        };
        GrowableApi.Harvested += (_, args) => {
            if (args.ItemCount <= 0 || !IsPlayer(args.Actor)) return;
            if (SkillsApi.GetPerkRank(Player.Instance, tree.Id, "harvest-vigor") > 0) Player.Instance.energy.Value += 4;
            SkillsApi.AwardExperience(Player.Instance, tree.Id, 8);
        };
        GrowableApi.PreparingHarvestItem += (_, args) => {
            if (args.Count <= 0 || !IsPlayer(args.Actor)) return;
            int Rank(string id) => SkillsApi.GetPerkRank(Player.Instance, tree.Id, id);
            int yieldChance = 10 * Rank("abundance") + (args.Plant.IsPerennial ? 20 * Rank("fruit-set") : 0);
            if (yieldChance > 0 && args.Count < GrowableApi.MaximumOutputCopies && Random.value < .01f * yieldChance) args.Count++;
            if (!args.SupportsQuality) return;
            if (args.Plant.Kind == GrowableKind.NativeHerb && args.Quality < Quality.Legendary && Rank("herbalist") > 0)
                args.Quality++;
            int quality = Rank("selective");
            if (quality > 0 && args.Quality < Quality.Legendary && Random.value < .2f * quality)
                args.Quality++;
        };
    }
}
