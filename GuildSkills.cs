using AdventurersGuild;

namespace SilverpineSkills;

internal static class GuildSkills
{
    internal static void Install()
    {
        var tree = SkillTree.Guild;
        GuildApi.PlayerSigning += quest => {
            int Rank(string id) => SkillsApi.GetPerkRank(Player.Instance, tree.Id, id);
            int tenths = Rank("negotiator") + Rank("patron");
            if (quest.Objective is Work.Procurement or Work.Courier) tenths += Rank("broker");
            int gold = (int)((long)quest.Gold * tenths / 10);
            if (quest.Objective is Work.Bounty or Work.Rescue) gold += 10 * Rank("hazard-pay");
            return new GuildSkillBonus(gold, Rank("distinction"));
        };
        GuildApi.PlayerCompleted += quest =>
            SkillsApi.AwardExperience(Player.Instance, tree.Id, 15 + 10 * (int)quest.RequiredRank);
    }
}
