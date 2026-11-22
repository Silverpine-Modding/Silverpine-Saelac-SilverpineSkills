using CompanionParty;
using Silverpine.ModdingTools;

namespace SilverpineSkills;

internal static class PartySkills
{
    internal static void Install()
    {
        var tree = SkillTree.Leadership;
        PartySkillApi.Register(tree.Id, (player, npc) => new PartySkillBonuses {
            CarryCapacity = 4 * SkillsApi.GetPerkRank(player, tree.Id, "quartermaster"),
            AttackDamage = SkillsApi.GetPerkRank(player, tree.Id, "vanguard"),
            MaxEnergy = 10 * SkillsApi.GetPerkRank(player, tree.Id, "trail-rations"),
            NormalArmor = 2 * SkillsApi.GetPerkRank(player, tree.Id, "shield-drill"),
            MaxHealth = 12 * SkillsApi.GetPerkRank(player, tree.Id, "rally")
        });
        PartySkillApi.PlayerActivity += (player, activity, members) => {
            if (activity == PartyActivity.TravelledTogether) SkillsApi.AwardExperience(player, tree.Id, 6);
            if (activity == PartyActivity.HelpedUp) SkillsApi.AwardExperience(player, tree.Id, 12);
        };
        SkillsApi.SkillChanged += (_, id, benefitsChanged) => {
            if (benefitsChanged && id == tree.Id) PartySkillApi.Refresh();
        };
        ModSaveData.Loaded += PartySkillApi.Refresh;
    }
}
