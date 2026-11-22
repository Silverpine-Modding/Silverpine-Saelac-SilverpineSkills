using System;
using System.Collections.Generic;
using System.Linq;
using AdventurersGuild;
using CompanionParty;
using HarmonyLib;
using SilverpineMods.CustomCraftingRecipes;
using SilverpineMods.CustomGrowables;
using static SilverpineSkills.Checks.Checks;

namespace SilverpineSkills.Checks;

internal static class IntegrationChecks
{
    private static void Master(Player player, string id)
    {
        SkillsApi.AwardExperience(player, id, int.MaxValue);
        foreach (var perk in SkillsApi.GetPerks(id))
            if (SkillsApi.GetPerkRank(player, id, perk.Id) == 0)
                Check(SkillsApi.LearnPerk(player, id, perk.Id), "learned " + id + "/" + perk.Id);
    }

    internal static void Guild(Player player)
    {
        const string id = Prefix + "guildcraft";
        var charter = new Charter();
        var job = new Contract { Gold = 40, RequiredRank = GuildRank.Intermediate };
        AccessTools.Method(typeof(GuildApi), "Accepted").Invoke(null, new object[] { job, charter });
        Check(SkillsApi.GetExperience(player, id) == 0, "signing alone gives no guild XP");
        AccessTools.Method(typeof(GuildApi), "Completed").Invoke(null, new object[] { job, charter, GuildRank.Beginner });
        Check(SkillsApi.GetExperience(player, id) == 25 && SkillsApi.SelectPerk(player, id, "negotiator"), "guild completion trains Guildcraft");
        AccessTools.Method(typeof(GuildApi), "Sign").Invoke(null, new object[] { job, charter });
        Check(job.SkillGold == 4 && charter.Treasury == 296, "guild perk reserves bonus from the treasury");
        Master(player, id);
        foreach (var work in new[] { Work.Procurement, Work.Bounty, Work.Survey })
        {
            var offer = new Contract { Gold = 200, Work = work, RequiredRank = GuildRank.Intermediate };
            var funds = new Charter { Treasury = 300 };
            AccessTools.Method(typeof(GuildApi), "Sign").Invoke(null, new object[] { offer, funds });
            int expected = work == Work.Procurement ? 100 : work == Work.Bounty ? 90 : 80;
            Check(offer.SkillGold == expected && funds.Treasury == 300 - expected && offer.SkillMerit == 3,
                work + " applies the matching signing perks together");
        }
    }

    internal static void Party(Player player)
    {
        const string id = Prefix + "leadership";
        var npc = Inactive("traveller").AddComponent<NeuralNPC>();
        var members = new List<NeuralNPC> { npc, Inactive("second traveller").AddComponent<NeuralNPC>() };
        var activity = AccessTools.Method(typeof(PartySkillApi), "Completed");
        activity.Invoke(null, new object[] { PartyActivity.TravelledTogether, members });
        Check(SkillsApi.GetExperience(player, id) == 6, "one Leadership award for a group crossing");
        activity.Invoke(null, new object[] { PartyActivity.HelpedUp, new List<NeuralNPC> { npc } });
        Check(SkillsApi.GetExperience(player, id) == 18, "player-assisted recovery trains Leadership");
        SkillsApi.AwardExperience(player, id, 7);
        SkillsApi.SelectPerk(player, id, "quartermaster");
        var bonuses = (PartySkillBonuses[])AccessTools.Method(typeof(PartySkillApi), "Resolve").Invoke(null, new object[] { npc });
        Check(bonuses.Length == 1 && bonuses[0].CarryCapacity == 4, "companion API resolves selected carrying perk");
        Master(player, id);
        bonuses = (PartySkillBonuses[])AccessTools.Method(typeof(PartySkillApi), "Resolve").Invoke(null, new object[] { npc });
        var active = bonuses.Single();
        Check(active.MaxHealth == 12 && active.MaxEnergy == 10 && active.NormalArmor == 2 && active.CarryCapacity == 12 && active.AttackDamage == 3,
            "all five Leadership perks contribute to recruited companion stats");
    }

    internal static void Gardening(Player player)
    {
        const string id = Prefix + "gardening";
        var go = Inactive("native herb");
        go.AddComponent<ResourceNode>().resourceNodeType = ResourceNodeType.Herb;
        var plant = AccessTools.Method(typeof(GrowableApi), "NotifyPlanted");
        plant.Invoke(null, new object[] { go, go, null });
        Check(SkillsApi.GetExperience(player, id) == 0, "NPC planting excluded");
        plant.Invoke(null, new object[] { go, player.gameObject, null });
        Check(GrowableApi.TryGetPlant(go, out var info) && SkillsApi.GetExperience(player, id) == 4, "native player planting trains Gardening");
        var bag = new Inventory { owner = player.gameObject };
        var empty = new List<Item>();
        var harvest = AccessTools.Method(typeof(GrowableApi), "NotifyHarvested");
        harvest.Invoke(null, new object[] { info, bag, empty, empty });
        Check(SkillsApi.GetExperience(player, id) == 4, "empty harvest excluded");
        harvest.Invoke(null, new object[] { info, bag, new List<Item> { new Item(), new Item() }, empty });
        Check(SkillsApi.GetExperience(player, id) == 12, "multi-item harvest gives one completion award");
        Master(player, id);
        var herb = new Item();
        var quality = new ItemComponent_Ore();
        herb.AddItemComponent(quality);
        AccessTools.Method(typeof(GrowableApi), "PrepareOutput").Invoke(null, new object[] { info, bag, herb });
        Check(quality.Quality > Quality.Normal, "Herbalist raises native herb quality");
        player.energy.Value = 10;
        harvest.Invoke(null, new object[] { info, bag, new List<Item> { herb, new Item() }, empty });
        Check(player.energy.Value == 14, "Harvest vigor restores energy once per successful harvest");
    }

    internal static void PartyScene(Player player)
    {
        const string id = Prefix + "leadership";
        var map = DungeonGenerationManager.Instance;
        var origin = map.currentPlayerWorldTilePosition;
        var recruits = NeuralNPC.neuralNPCs.Values.Where(npc => npc && npc.isActiveAndEnabled && !npc.sleeping &&
            map.GetPositionWorldTile(npc.transform.GetVector2IntPosition()) == map.worldTiles[origin])
            .Distinct().Where(PartyApi.Recruit).Take(2).ToArray();
        Check(recruits.Length == 2, "recruited two residents from the live scene");
        var destination = map.worldTiles.Keys.First(pos => (pos - origin).sqrMagnitude == 1 && !map.worldTiles[pos].IsDungeonInstance());
        int before = SkillsApi.GetExperience(player, id);
        map.EnterWorldTile(destination, map.GetTravelTargetPosition(destination));
        Check(recruits.All(npc => map.GetPositionWorldTile(npc.transform.GetVector2IntPosition()) == map.worldTiles[destination]) &&
            SkillsApi.GetExperience(player, id) == before + 6, "two travelling recruits produce a single Leadership award");
        var party = typeof(PartyApi).Assembly;
        var person = (Companion)AccessTools.Method(party.GetType("CompanionParty.State"), "Get").Invoke(null, new object[] { recruits[0] });
        person.DownSince = WorldInfoManager.Instance.TotalTurnCount; person.HP = 0;
        var revive = AccessTools.Method(party.GetType("CompanionParty.Supplies"), "Revive");
        revive.Invoke(null, new object[] { person, true });
        revive.Invoke(null, new object[] { person, true });
        Check(person.HP > 0 && SkillsApi.GetExperience(player, id) == before + 18, "helping a downed recruit grants XP once");
    }

    internal static void Craft(Player player)
    {
        const string id = Prefix + "blacksmithing";
        var bag = new Inventory { owner = player.gameObject };
        var input = new Item { name = "Ingot" };
        var preview = new ResultData("Test blade", new Item { name = "Blade", value = 40 }, 20);
        bag.TryAddItem(input);
        var deliver = AccessTools.Method(typeof(CraftingApi), "TryDeliver");
        bool Deliver() => (bool)deliver.Invoke(null, new object[] { player, CraftingStation.Blacksmithing, preview, bag, new[] { input }, null, null, null, null, null });
        bag.maxItems = 0;
        Check(!Deliver() && SkillsApi.GetExperience(player, id) == 0, "rejected delivery gives no crafting XP");
        bag.maxItems = 2;
        Check(Deliver() && SkillsApi.GetExperience(player, id) == 9, "crafting receipt trains the matching station");
        SkillsApi.AwardExperience(player, id, 16);
        SkillsApi.SelectPerk(player, id, "artisan");
        preview = new ResultData("Test blade", new Item { name = "Blade", value = 40 }, 20);
        bag.TryAddItem(input);
        Check(Deliver() && bag.items.Any(i => i.value == 44) && preview.resultItem.value == 40, "artisan modifies the delivered item only");
        var registry = (System.Collections.IDictionary)AccessTools.Field(typeof(CraftingApi), "skills").GetValue(null);
        var provider = registry[id];
        var read = (Func<Player,int>)AccessTools.Field(provider.GetType(), "Level").GetValue(provider);
        Check(read(player) == 2, "recipe requirements see current station level");
        Master(player, id);
        bag.items.Clear(); bag.maxItems = 6; bag.TryAddItem(input);
        preview = new ResultData("Test blade", new Item { name = "Blade", value = 40 }, 20);
        player.energy.Value = 10;
        Check(Deliver() && bag.items.Count >= 2 && bag.items.All(i => i.value == 56) && player.energy.Value == 13,
            "craft mastery, finishing, artisan and recovery stack on one delivered batch");
        bag.items.Clear(); bag.TryAddItem(input);
        preview.resultItem.AddItemComponent(new ItemComponent_Ore());
        Check(Deliver() && bag.items.All(i => (int)i.GetItemComponent<ItemComponent_Ore>().Quality == 1 && i.value == 52),
            "finishing improves quality-bearing outputs and their extra copies");
    }
}
