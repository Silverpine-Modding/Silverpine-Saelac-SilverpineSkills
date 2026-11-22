using System;
using System.Collections.Generic;
using HarmonyLib;
using Silverpine.ModdingTools;
using UnityEngine;
using static SilverpineSkills.Checks.Checks;

namespace SilverpineSkills.Checks;

internal static class SocialChecks
{
    private const string Id = Prefix + "social";
    private static Action response;
    private static bool CaptureGenerator(Action successCallback)
    {
        response = successCallback;
        return false;
    }

    internal static void Replies(Player player)
    {
        var solo = AccessTools.Method(typeof(NeuralNPC), "GenerateDialog");
        var group = AccessTools.Method(typeof(NeuralNPC), "GenerateMultiDialog");
        var capture = new HarmonyMethod(typeof(SocialChecks), nameof(CaptureGenerator)) { after = new[] { Plugin.Id } };
        var harness = new Harmony("silverpine.skills.checks");
        harness.Patch(solo, prefix: capture);
        harness.Patch(group, prefix: capture);
        var oldBox = DialogBox.Instance;
        var oldGroup = NeuralNPC.multiDialogParticipants;
        var box = DialogBox.Instance = Inactive("Training dialog").AddComponent<DialogBox>();
        var speaker = Inactive("Speaker").AddComponent<NeuralNPC>();
        var other = Inactive("Listener").AddComponent<NeuralNPC>();
        NeuralNPC.multiDialogParticipants = new List<NeuralNPC> { speaker, other };
        var submitted = AccessTools.Field(typeof(DialogBox), "playerInputAnything");
        int nativeCallbacks = 0;
        void Request(SpeakerType who, string line, bool multiple = false)
        {
            speaker.dialogElements.Add(new NeuralNPC.DialogElement(who, line, 0));
            Action onSuccess = () => nativeCallbacks++;
            if (multiple) group.Invoke(null, new object[] { speaker, null, onSuccess });
            else solo.Invoke(speaker, new object[] { null, onSuccess });
        }
        int Xp() => SkillsApi.GetExperience(player, Id);
        try
        {
            Request(SpeakerType.Player, "Hello."); response();
            submitted.SetValue(box, true);
            Request(SpeakerType.System, "Traded."); response();
            Request(SpeakerType.NPC, "NPC speaking", true); response();
            Request(SpeakerType.Player, "  "); response();
            Check(Xp() == 0 && nativeCallbacks == 4, "greeting, trade, group continuation and blank input preserve callbacks without XP");
            Request(SpeakerType.Player, "How are you?");
            Check(Xp() == 0, "unanswered dialogue grants no Social XP");
            response(); response();
            Check(Xp() == 10 && nativeCallbacks == 6, "one-on-one reply awards XP once and keeps the game's callback");
            Request(SpeakerType.Player, "Anyone seen the mayor?", true); response();
            Check(Xp() == 25 && SkillsApi.LearnPerk(player, Id, "good-company"), "group reply pays once and unlocks a Social perk");
            player.sanity = new Player.PlayerResource(10, 100, Color.magenta, true, "sanity");
            Request(SpeakerType.Player, "Good morning."); response();
            Check(player.sanity.Value == 11, "rank one Good company restores one sanity");
            SkillsApi.AwardExperience(player, Id, 1125);
            foreach (string id in new[] { "rapport", "first-impressions", "heart-to-heart", "life-of-the-party" })
                Check(SkillsApi.LearnPerk(player, Id, id), "learn Social perk " + id);
            player.energy.Value = 10;
            speaker.relationshipLevel = 14;
            Request(SpeakerType.Player, "How is work?"); response();
            Check(player.sanity.Value == 14 && player.energy.Value == 12, "mastered solo benefits apply with an acquaintance even at the XP cap");
            speaker.relationshipLevel = 15;
            Request(SpeakerType.Player, "How is everyone?", true); response();
            Check(player.sanity.Value == 19 && player.energy.Value == 17, "friendship threshold and group mastery stack");
            Request(SpeakerType.Player, "Until next time.");
            ModSaveData.ResetForNewGame();
            response();
            Check(Xp() == 0 && player.sanity.Value == 19 && player.energy.Value == 17, "an old reply cannot reward a different save");
        }
        finally
        {
            DialogBox.Instance = oldBox; NeuralNPC.multiDialogParticipants = oldGroup;
            harness.Unpatch(solo, HarmonyPatchType.Prefix, harness.Id);
            harness.Unpatch(group, HarmonyPatchType.Prefix, harness.Id);
        }
    }

    internal static void Reactions()
    {
        var npc = Inactive("Friendly NPC").AddComponent<NeuralNPC>();
        npc.npcName = NPCName.Hayden;
        var previous = NeuralNPC.currentActiveDialogNeuralNPC;
        NeuralNPC.currentActiveDialogNeuralNPC = npc;
        try
        {
            var bar = (Component)AccessTools.Field(typeof(DialogBox), "relationshipBar").GetValue(DialogBox.Instance);
            if (!RelationshipBar.Instance)
                AccessTools.Method(typeof(RelationshipBar), "Awake").Invoke(bar.GetComponent<RelationshipBar>(), null);
            npc.relationshipLevelDailyCap = 18;
            npc.relationshipLevelTotalCap = 100;
            var reaction = new NPCFunction_IncreaseRelationship { owner = npc };
            npc.relationshipLevel = 2;
            reaction.Invoke().GetAwaiter().GetResult();
            Check(npc.relationshipLevel == 7, "native favorable reaction stacks Rapport and First impressions");
            npc.relationshipLevel = 15;
            npc.relationshipLevelDailyCap = 25;
            reaction.Invoke().GetAwaiter().GetResult();
            Check(npc.relationshipLevel == 19, "First impressions stops at friendship");
            npc.relationshipLevelDailyCap = 21;
            reaction.Invoke().GetAwaiter().GetResult();
            Check(npc.relationshipLevel == 21 && bar.GetComponent<UnityEngine.UI.Slider>().value == 21, "native daily cap and relationship bar agree");
            npc.relationshipLevelDailyCap = 100;
            npc.relationshipLevelTotalCap = 21;
            reaction.Invoke().GetAwaiter().GetResult();
            Check(npc.relationshipLevel == 21, "native total cap remains binding");
        }
        finally { NeuralNPC.currentActiveDialogNeuralNPC = previous; }
    }
}
