using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;

namespace SilverpineSkills;

[BepInPlugin(Id, "Silverpine Skills", "0.3.0")]
[BepInDependency("Saelac.Silverpine.ModdingTools", "1.13.0")]
[BepInDependency(Guild, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(Crafting, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(Growables, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(Party, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "silverpine.skills";
    internal const string Guild = "silverpine.adventurersguild", Party = "silverpine.companionparty";
    internal const string Crafting = "renegadex.silverpine.customcraftingrecipes", Growables = "renegadex.silverpine.customgrowables";
    internal static ManualLogSource Log;
    private static readonly HashSet<string> connected = new();
    internal static bool Available(SkillTree tree) => tree.Source == null || connected.Contains(tree.Source);

    private void Awake()
    {
        Log = Logger;
        ModSaveData.Register(Id, new ModSaveDataDefinition {
            Id = Id, CurrentVersion = 2, Migrate = SkillsApi.Migrate,
            Capture = SkillsApi.Capture, Restore = SkillsApi.Restore, Reset = SkillsApi.Reset
        });
        InventoryModTools.RegisterSession(Id, "Player Skills", SkillWindow.Open, order: 145);
        var harmony = new Harmony(Id);
        harmony.CreateClassProcessor(typeof(FieldWork)).Patch();
        harmony.CreateClassProcessor(typeof(SocialSkills)).Patch();
        harmony.CreateClassProcessor(typeof(SocialReaction)).Patch();
        Connect(Guild, new Version(1,7,0), GuildSkills.Install);
        Connect(Party, new Version(0,2,6), PartySkills.Install);
        Connect(Growables, new Version(1,9,0), GardeningSkills.Install);
        Connect(Crafting, new Version(1,7,0), CraftSkills.Install);
        Log.LogInfo($"Player Skills ready with {connected.Count} optional integrations.");
    }

    // Only enter an adapter's typed code after BepInEx verifies its provider.
    private static void Connect(string id, Version minimum, Action install)
    {
        if (!Chainloader.PluginInfos.TryGetValue(id, out var info)) return;
        if (info.Metadata.Version < minimum)
        {
            Log.LogInfo($"Skipping {id} {info.Metadata.Version}. Its skills integration requires {minimum}.");
            return;
        }
        try { install(); connected.Add(id); }
        catch (Exception ex) { Log.LogError($"Could not initialize skills for {id}: {ex}"); }
    }
}
