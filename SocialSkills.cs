using System;
using System.Threading.Tasks;
using HarmonyLib;

namespace SilverpineSkills;

[HarmonyPatch]
internal static class SocialSkills
{
    // Silverpine calls its generators for greetings, trades and NPC-to-NPC lines too.
    private static readonly AccessTools.FieldRef<DialogBox, bool> SubmittedInput =
        AccessTools.FieldRefAccess<DialogBox, bool>("playerInputAnything");
    internal const int FriendLevel = 15;

    [HarmonyPrefix, HarmonyPatch(typeof(NeuralNPC), "GenerateDialog")]
    private static void Solo(NeuralNPC __instance, ref Action successCallback) =>
        OnReply(__instance, false, ref successCallback);

    [HarmonyPrefix, HarmonyPatch(typeof(NeuralNPC), "GenerateMultiDialog")]
    private static void Group(NeuralNPC speaker, ref Action successCallback) =>
        OnReply(speaker, NeuralNPC.multiDialogParticipants?.Count > 1, ref successCallback);

    private static void OnReply(NeuralNPC speaker, bool group, ref Action successCallback)
    {
        var player = Player.Instance;
        if (!SkillsApi.Current(player) || !speaker || !DialogBox.Instance || !SubmittedInput(DialogBox.Instance)) return;
        var history = speaker.dialogElements;
        if (history.Count == 0) return;
        var line = history[history.Count - 1];
        if (line.speakerType != SpeakerType.Player || string.IsNullOrWhiteSpace(line.contents)) return;

        int epoch = SkillsApi.Epoch;
        bool credited = false;
        successCallback += () => {
            if (credited || !speaker || epoch != SkillsApi.Epoch || !SkillsApi.Current(player)) return;
            credited = true;
            var id = SkillTree.Social.Id;
            int sanity = SkillsApi.GetPerkRank(player, id, "good-company");
            if (speaker.relationshipLevel >= FriendLevel)
                sanity += 2 * SkillsApi.GetPerkRank(player, id, "heart-to-heart");
            if (sanity > 0) player.sanity.ChangeQuiet(sanity);
            if (SkillsApi.GetPerkRank(player, id, "life-of-the-party") > 0)
                player.energy.ChangeQuiet(group ? 5 : 2);
            SkillsApi.AwardExperience(player, id, group ? 15 : 10);
        };
    }
}

[HarmonyPatch(typeof(NPCFunction_IncreaseRelationship), nameof(NPCFunction_IncreaseRelationship.Invoke))]
internal static class SocialReaction
{
    private static void Prefix(NPCFunction_IncreaseRelationship __instance, out (Player player, int epoch, int before, int bonus) __state)
    {
        var player = Player.Instance;
        int bonus = SkillsApi.GetPerkRank(player, SkillTree.Social.Id, "rapport");
        int before = __instance.owner.relationshipLevel;
        if (bonus > 0 && before < SocialSkills.FriendLevel)
            bonus += SkillsApi.GetPerkRank(player, SkillTree.Social.Id, "first-impressions");
        __state = (player, SkillsApi.Epoch, before, bonus);
    }

    private static void Postfix(NPCFunction_IncreaseRelationship __instance, (Player player, int epoch, int before, int bonus) __state, ref Task __result)
    {
        if (__state.bonus > 0 && __result != null)
            __result = AfterIncrease(__result, __instance.owner, __state);
    }

    private static async Task AfterIncrease(Task reaction, NeuralNPC npc, (Player player, int epoch, int before, int bonus) state)
    {
        await reaction;
        if (!npc || state.epoch != SkillsApi.Epoch || !SkillsApi.Current(state.player) || npc.relationshipLevel <= state.before) return;
        int cap = npc.GetRelationshipLevelCap();
        if (npc.relationshipLevel >= cap) return;
        npc.relationshipLevel = Math.Min(npc.relationshipLevel + state.bonus, cap);
        if (RelationshipBar.Instance && NeuralNPC.currentActiveDialogNeuralNPC == npc)
            RelationshipBar.Instance.SetValue(npc.relationshipLevel, cap);
    }
}
