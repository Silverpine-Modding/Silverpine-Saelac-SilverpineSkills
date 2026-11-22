using System;
using System.Collections.Generic;
using Silverpine.ModdingTools;
using SilverpineMods.CustomCraftingRecipes;

namespace SilverpineSkills;

internal static class CraftSkills
{
    internal static void Install()
    {
        var handles = new List<IDisposable>();
        try
        {
            foreach (var tree in SkillTree.All)
                if (tree.Source == Plugin.Crafting || Plugin.Available(tree))
                    handles.Add(CraftingApi.RegisterSkill(tree.Id, tree.Name, player => SkillsApi.GetLevel(player, tree.Id)));
            handles.Add(CraftingApi.Register(Plugin.Id, Modify, Completed));
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
        SkillsApi.SkillChanged += (_, id, benefitsChanged) => { if (benefitsChanged) CraftingApi.NotifyRecipeAccessChanged(); };
        ModSaveData.Loaded += CraftingApi.NotifyRecipeAccessChanged;
    }

    private static SkillTree Tree(CraftingStation station) => station switch {
        CraftingStation.CustomCrafting => SkillTree.Handicraft,
        CraftingStation.Alchemy => SkillTree.Alchemy, CraftingStation.Cooking => SkillTree.Cooking,
        CraftingStation.Joinery => SkillTree.Joinery, CraftingStation.Tailoring => SkillTree.Tailoring,
        CraftingStation.Baking => SkillTree.Baking, CraftingStation.Blacksmithing => SkillTree.Blacksmithing,
        CraftingStation.Smelting => SkillTree.Smelting, _ => null
    };

    private static CraftBonus Modify(CraftContext craft)
    {
        var tree = Tree(craft.Station);
        if (tree == null) return default;
        int Rank(string id) => SkillsApi.GetPerkRank(craft.Player, tree.Id, id);
        int finishing = Rank("finishing");
        int value = 10 * (Rank("artisan") + (craft.SupportsQuality ? 0 : finishing));
        int extra = Rank("masterwork");
        int batch = Rank("batch");
        if (batch > 0 && UnityEngine.Random.value < .1f * batch) extra++;
        return new CraftBonus(extraItems: extra, qualitySteps: craft.SupportsQuality ? finishing : 0, valuePercent: value);
    }

    private static void Completed(CraftReceipt receipt)
    {
        var craft = receipt.Craft;
        var tree = Tree(craft.Station);
        if (tree == null) return;
        if (SkillsApi.GetPerkRank(craft.Player, tree.Id, "steady-hands") > 0) craft.Player.energy.Value += 3;
        SkillsApi.AwardExperience(craft.Player, tree.Id, 5 + Math.Min(20, Math.Max(0, craft.WorkMinutes) / 5));
    }
}
