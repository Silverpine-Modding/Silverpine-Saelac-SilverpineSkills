using System;
using System.Collections.Generic;
using System.Linq;

namespace SilverpineSkills;

public sealed class Perk
{
    public string Id { get; }
    public string Name { get; }
    public string Effect { get; }
    public int RequiredLevel { get; }
    public IReadOnlyList<string> Prerequisites { get; }

    internal Perk(string id, string name, string effect, int level = 2, params string[] prerequisites)
    {
        Id = id; Name = name; Effect = effect; RequiredLevel = level;
        Prerequisites = Array.AsReadOnly(prerequisites);
    }
}

internal sealed class SkillTree
{
    internal readonly string Id, Name, Training, Source;
    internal readonly IReadOnlyList<Perk> Perks;
    internal readonly Dictionary<string, Perk> ByPerkId;
    private SkillTree(string key, string name, string training, string source, params Perk[] perks)
    {
        Id = Plugin.Id + "." + key;
        Name = name; Training = training; Source = source;
        Perks = Array.AsReadOnly(perks);
        ByPerkId = perks.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    internal static readonly SkillTree Mining = Field("mining", "Mining", "Mine an ore vein or quarry rock.", "Efficient prospector",
        "Vein reader", "Save another 15% of work turns when mining an ore vein.", "Quarry rhythm",
        "Recover 4 additional energy after quarrying rock.", "Master miner");
    internal static readonly SkillTree Woodcutting = Field("woodcutting", "Woodcutting", "Fell a tree with an axe or saw a plank.", "Efficient woodsman",
        "Felling cuts", "Save another 15% of work turns when felling a standing tree.", "Sawyer’s rhythm",
        "Recover 4 additional energy after sawing a plank.", "Master forester");
    internal static readonly SkillTree Construction = Field("construction", "Construction", "Finish a supplied construction site with a hammer.", "Efficient builder",
        "Framing square", "Save another 15% of work turns on a build with at least 20 turns of work remaining.", "Sure footing",
        "Recover 4 additional energy after completing a building.", "Master builder");

    internal static readonly SkillTree Social = new("social", "Social", "Speak to an NPC. Each reply to your line earns 10 XP, or 15 XP in a group.", null,
        new("rapport", "Rapport", "When an NPC likes what you said, gain 1 / 2 / 3 extra relationship at levels 2 / 5 / 10, up to their cap."),
        new("good-company", "Good company", "Recover 1 / 2 / 3 sanity when an NPC answers you at levels 2 / 5 / 10."),
        new("first-impressions", "First impressions", "Gain 1 more relationship on a favorable reaction if the NPC began below 15 relationship.", 5, "rapport"),
        new("heart-to-heart", "Heart-to-heart", "Recover 2 more sanity when the NPC answering you is a friend (15 or more relationship).", 5, "good-company"),
        new("life-of-the-party", "Life of the party", "Recover 2 energy when an NPC answers you one-on-one, or 5 energy in a group.", 10, "first-impressions", "heart-to-heart"));

    private static SkillTree Field(string key, string name, string training, string speed, string focus, string focusEffect,
        string recovery, string recoveryEffect, string mastery) => new(key, name, training, null,
        new("efficient", speed, "Save 10 / 20 / 30% of work turns at levels 2 / 5 / 10. Work takes at least one turn."),
        new("second-wind", "Second wind", "Recover 2 / 4 / 6 energy when work finishes at levels 2 / 5 / 10."),
        new("technique", focus, focusEffect, 5, "efficient"),
        new("rhythm", recovery, recoveryEffect, 5, "second-wind"),
        new("mastery", mastery, "Save another 10% of work turns and recover 2 additional energy on every completion.", 10, "technique", "rhythm"));

    internal static readonly SkillTree Guild = new("guildcraft", "Guildcraft", "Collect payment for a guild contract you completed.", Plugin.Guild,
        new("negotiator", "Negotiator", "Request 10 / 20 / 30% extra gold when signing at levels 2 / 5 / 10, subject to the treasury."),
        new("distinction", "Distinction", "Request 1 / 2 / 3 extra merit at levels 2 / 5 / 10, up to the contract’s base merit."),
        new("broker", "Supply broker", "Request another 10% gold on procurement and courier contracts.", 5, "negotiator"),
        new("hazard-pay", "Hazard pay", "Request 10 additional gold for bounty and rescue contracts.", 5, "distinction"),
        new("patron", "Guild patron", "Request another 10% gold on every contract, subject to the guild’s payout limit.", 10, "broker", "hazard-pay"));
    internal static readonly SkillTree Leadership = new("leadership", "Leadership", "Travel across an overworld tile with a recruit or help a downed recruit back up.", Plugin.Party,
        new("quartermaster", "Quartermaster", "Recruits can carry 4 / 8 / 12 more bulk at levels 2 / 5 / 10."),
        new("vanguard", "Vanguard", "Recruits deal 1 / 2 / 3 more attack damage at levels 2 / 5 / 10."),
        new("trail-rations", "Trail rations", "Recruits gain 10 maximum energy.", 5, "quartermaster"),
        new("shield-drill", "Shield drill", "Recruits gain 2 armor against normal damage.", 5, "vanguard"),
        new("rally", "Rally", "Recruits gain 12 maximum health.", 10, "trail-rations", "shield-drill"));
    internal static readonly SkillTree Gardening = new("gardening", "Gardening", "Plant a seed or harvest a crop or herb yourself.", Plugin.Growables,
        new("abundance", "Abundance", "Each harvest item has a 10 / 20 / 30% chance of an extra copy at levels 2 / 5 / 10."),
        new("selective", "Selective harvest", "At levels 2 / 5 / 10, quality-bearing harvests have a 20 / 40 / 60% chance to rise one tier."),
        new("fruit-set", "Fruit set", "Perennials gain another 20 percentage points on the Abundance roll.", 5, "abundance"),
        new("herbalist", "Herbalist", "Native herbs with quality gain one tier, up to Legendary. Selective harvest can add another.", 5, "selective"),
        new("harvest-vigor", "Harvest vigor", "Recover 4 energy after each successful harvest.", 10, "fruit-set", "herbalist"));

    private static SkillTree Craft(string key, string name, string finish, string mastery) => new(key, name, "Complete a craft at the " + name + " station.", Plugin.Crafting,
        new("artisan", "Artisan", "Crafted goods gain 10 / 20 / 30% value at levels 2 / 5 / 10."),
        new("batch", "Batch work", "At levels 2 / 5 / 10, a batch has a 10 / 20 / 30% chance of one extra copy of an eligible item."),
        new("finishing", finish, "Improve crafted goods by one quality tier. Goods without quality gain another 10% value.", 5, "artisan"),
        new("steady-hands", "Steady hands", "Recover 3 energy for each completed batch.", 5, "batch"),
        new("masterwork", mastery, "Gain one extra copy of eligible outputs per batch. Batch work can award another.", 10, "finishing", "steady-hands"));

    internal static readonly SkillTree Handicraft = Craft("handicraft", "Custom Crafting", "Fine assembly", "Master artisan");
    internal static readonly SkillTree Alchemy = Craft("alchemy", "Alchemy", "Distillation", "Master alchemist");
    internal static readonly SkillTree Cooking = Craft("cooking", "Cooking", "Seasoning", "Master cook");
    internal static readonly SkillTree Joinery = Craft("joinery", "Joinery", "Fine joinery", "Master joiner");
    internal static readonly SkillTree Tailoring = Craft("tailoring", "Tailoring", "Fine stitching", "Master tailor");
    internal static readonly SkillTree Baking = Craft("baking", "Baking", "Perfect proof", "Master baker");
    internal static readonly SkillTree Blacksmithing = Craft("blacksmithing", "Blacksmithing", "Tempering", "Master smith");
    internal static readonly SkillTree Smelting = Craft("smelting", "Smelting", "Refining", "Master smelter");
    internal static readonly SkillTree[] All = { Mining, Woodcutting, Construction, Social, Guild, Leadership, Gardening,
        Handicraft, Alchemy, Cooking, Joinery, Tailoring, Baking, Blacksmithing, Smelting };
    internal static readonly Dictionary<string, SkillTree> ById = All.ToDictionary(tree => tree.Id, StringComparer.Ordinal);
}
