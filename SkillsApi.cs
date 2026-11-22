using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SilverpineSkills;

internal sealed class SkillRecord
{
    public int Xp;
    [JsonProperty(Required = Required.Always)]
    public List<string> Learned = new();
}

/// <summary>Queries and awards for the active player. Call from the Unity main thread.</summary>
public static class SkillsApi
{
    public const int MaxLevel = 10;
    public static IReadOnlyList<string> SkillIds { get; } = Array.AsReadOnly(SkillTree.All.Select(tree => tree.Id).ToArray());
    /// <summary>Fires after XP or perk selection changes. The final argument flags a level or selection change.</summary>
    public static event Action<Player, string, bool> SkillChanged;
    private static Dictionary<string, SkillRecord> records = new();
    internal static int Epoch { get; private set; }
    internal static bool Current(Player player) => player && player == Player.Instance && !SerializationManager.loadingSave
        && !(MainMenuUI.Instance && MainMenuUI.Instance.IsOpen());

    public static int GetLevel(Player player, string skillId) =>
        Current(player) && skillId != null && SkillTree.ById.ContainsKey(skillId) ? Level(Xp(skillId)) : 0;
    public static int GetExperience(Player player, string skillId) =>
        Current(player) && skillId != null ? Xp(skillId) : 0;
    public static IReadOnlyList<Perk> GetPerks(string skillId) =>
        skillId != null && SkillTree.ById.TryGetValue(skillId, out var tree) ? tree.Perks : Array.Empty<Perk>();
    public static IReadOnlyList<string> GetLearnedPerks(Player player, string skillId) =>
        Current(player) && skillId != null && records.TryGetValue(skillId, out var record) ? record.Learned.ToArray() : Array.Empty<string>();
    /// <summary>Compatibility query returning the most recently learned perk still owned.</summary>
    public static string GetSelectedPerk(Player player, string skillId) =>
        Current(player) && skillId != null && records.TryGetValue(skillId, out var record) ? record.Learned.LastOrDefault() : null;
    public static int GetPerkRank(Player player, string skillId, string perkId)
    {
        if (!Current(player) || skillId == null || perkId == null || !records.TryGetValue(skillId, out var record) ||
            !record.Learned.Contains(perkId) || !SkillTree.ById.TryGetValue(skillId, out var tree)) return 0;
        return tree.ByPerkId[perkId].Prerequisites.Count == 0 ? Rank(Level(record.Xp)) : 1;
    }

    private static readonly int[] PointLevels = { 2, 4, 5, 7, 10 };
    private static int EarnedPoints(int level) => PointLevels.Count(milestone => milestone <= level);
    internal static int NextPointLevel(int level) => Array.Find(PointLevels, milestone => milestone > level);
    public static int GetAvailablePoints(Player player, string skillId)
    {
        int level = GetLevel(player, skillId);
        return level == 0 ? 0 : EarnedPoints(level) - (records.TryGetValue(skillId, out var record) ? record.Learned.Count : 0);
    }

    internal static int Threshold(int level) => 25 * level * (level - 1) / 2;
    private static int Xp(string id) => records.TryGetValue(id, out var record) ? record.Xp : 0;
    private static int Level(int xp)
    {
        int level = 1;
        while (level < MaxLevel && xp >= Threshold(level + 1)) level++;
        return level;
    }
    internal static int Rank(int level) => level >= 10 ? 3 : level >= 5 ? 2 : level >= 2 ? 1 : 0;

    public static bool AwardExperience(Player player, string skillId, int amount)
    {
        if (!Current(player) || amount <= 0 || skillId == null || !SkillTree.ById.TryGetValue(skillId, out var tree)) return false;
        int before = Xp(skillId);
        int after = (int)Math.Min(Threshold(MaxLevel), (long)before + amount);
        if (after == before) return false;
        if (!records.TryGetValue(skillId, out var record)) records.Add(skillId, record = new SkillRecord());
        record.Xp = after;
        int level = Level(after);
        int previousLevel = Level(before);
        if (level != previousLevel)
        {
            string extra = EarnedPoints(level) > EarnedPoints(previousLevel) ? " — perk point earned." : "";
            if (UpperNotificationUI.Instance) UpperNotificationUI.Instance.OneOff($"{tree.Name} reached level {level}{extra}");
        }
        Notify(player, skillId, level != previousLevel);
        return true;
    }

    public static bool SelectPerk(Player player, string skillId, string perkId) => LearnPerk(player, skillId, perkId);

    public static bool LearnPerk(Player player, string skillId, string perkId)
    {
        if (perkId == null || GetAvailablePoints(player, skillId) < 1) return false;
        var tree = SkillTree.ById[skillId];
        var record = records[skillId];
        if (!Plugin.Available(tree) || !tree.ByPerkId.TryGetValue(perkId, out var perk) || record.Learned.Contains(perkId) ||
            Level(record.Xp) < perk.RequiredLevel || !perk.Prerequisites.All(record.Learned.Contains)) return false;
        record.Learned.Add(perkId);
        Notify(player, skillId, true);
        return true;
    }

    public static bool RefundPerk(Player player, string skillId, string perkId)
    {
        if (GetPerkRank(player, skillId, perkId) == 0) return false;
        var tree = SkillTree.ById[skillId];
        var record = records[skillId];
        if (!Plugin.Available(tree) || record.Learned.Any(id => tree.ByPerkId[id].Prerequisites.Contains(perkId))) return false;
        record.Learned.Remove(perkId);
        Notify(player, skillId, true);
        return true;
    }

    private static void Notify(Player player, string id, bool benefitsChanged)
    {
        var handlers = SkillChanged;
        if (handlers == null) return;
        foreach (Action<Player, string, bool> handler in handlers.GetInvocationList())
            try { handler(player, id, benefitsChanged); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Skill observer {handler.Method} failed: {ex}"); }
    }

    internal static string Capture() => JsonConvert.SerializeObject(records);
    internal static void Reset() { Epoch++; records = new(); }
    internal static string Migrate(int from, string payload)
    {
        if (from != 1) throw new InvalidDataException("Unsupported player skills save version.");
        var saved = JObject.Parse(payload);
        foreach (var entry in saved.Properties())
        {
            if (!(entry.Value is JObject record)) throw new InvalidDataException("Invalid player skill progression.");
            var old = (string)record["Perk"];
            record["Learned"] = old == null ? new JArray() : new JArray(old);
            record.Remove("Perk");
        }
        return saved.ToString(Formatting.None);
    }
    internal static void Restore(string payload)
    {
        var saved = JsonConvert.DeserializeObject<Dictionary<string, SkillRecord>>(payload);
        if (saved == null || saved.Any(pair => !ValidRecord(pair.Key, pair.Value)))
            throw new InvalidDataException("Invalid player skill progression.");
        records = saved;
    }

    private static bool ValidRecord(string id, SkillRecord record)
    {
        if (record == null || record.Xp < 0 || record.Learned == null) return false;
        if (!SkillTree.ById.TryGetValue(id, out var tree)) return true;
        int level = Level(record.Xp);
        if (record.Xp > Threshold(MaxLevel) || record.Learned.Count > EarnedPoints(level) ||
            record.Learned.Distinct().Count() != record.Learned.Count) return false;
        return record.Learned.All(key => key != null && tree.ByPerkId.TryGetValue(key, out var perk) &&
            level >= perk.RequiredLevel && perk.Prerequisites.All(record.Learned.Contains));
    }
}
