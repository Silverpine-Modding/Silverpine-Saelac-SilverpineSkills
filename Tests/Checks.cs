using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;
using Silverpine.ModdingTools;
using UnityEngine;

namespace SilverpineSkills.Checks;

[BepInPlugin("silverpine.skills.checks", "Skills checks", "0.1.0")]
[BepInDependency(Plugin.Id)]
public sealed class Checks : BaseUnityPlugin
{
    internal const string Prefix = "silverpine.skills.";
    private const string Mining = Prefix + "mining";
    private static readonly List<string> passed = new();
    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed.Add(name);
    }
    internal static GameObject Inactive(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return go;
    }
    private static Action success, failure;
    private static int workTurns;
    private static bool CaptureWork(Action successCallback, int turns, Action failureCallback)
    {
        success = successCallback; failure = failureCallback; workTurns = turns;
        return false;
    }
    private void Awake()
    {
        string marker = Path.Combine(Paths.GameRootPath, "skills-test-only");
        if (!Application.isBatchMode || !File.Exists(marker)) return;
        if (Environment.GetCommandLineArgs().Contains("-skills-scene"))
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneLoaded;
            return;
        }
        string report = Path.Combine(Paths.GameRootPath, "skills-results");
        try
        {
            var go = Inactive("Skill test player");
            var player = Player.Instance = go.AddComponent<Player>();
            var enabled = File.ReadAllLines(marker);
            var connected = (HashSet<string>)AccessTools.Field(typeof(Plugin), "connected").GetValue(null);
            Check(connected.Count == enabled.Length, "expected optional integrations connected");
            TestFieldAndSave(player);
            ModSaveData.ResetForNewGame();
            SocialChecks.Replies(player);
            ModSaveData.ResetForNewGame();
            if (enabled.Contains("AdventurersGuild")) IntegrationChecks.Guild(player);
            if (enabled.Contains("CompanionParty")) IntegrationChecks.Party(player);
            if (enabled.Contains("CustomGrowables")) IntegrationChecks.Gardening(player);
            if (enabled.Contains("CustomCraftingRecipes")) IntegrationChecks.Craft(player);
            passed.Add("PASS");
        }
        catch (Exception ex) { passed.Add(ex.ToString()); }
        File.WriteAllLines(report + ".pending", passed);
        File.Move(report + ".pending", report);
        System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
    }

    private static void SceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneLoaded;
        new GameObject("Skills scene checks").AddComponent<SceneRunner>().StartCoroutine(RunScene());
    }

    private static IEnumerator RunScene()
    {
        var steps = SceneSteps();
        while (true)
        {
            try { if (!steps.MoveNext()) { passed.Add("PASS"); break; } }
            catch (Exception ex) { passed.Add(ex.ToString()); break; }
            yield return steps.Current;
        }
        string report = Path.Combine(Paths.GameRootPath, "skills-results");
        File.WriteAllLines(report + ".pending", passed);
        File.Move(report + ".pending", report);
    }

    private static IEnumerator SceneSteps()
    {
        yield return null;
        yield return null;
        Check(Player.Instance && MainMenuUI.Instance && ActionQueue.Instance, "native player and menu initialized");
        MainMenuUI.Instance.Close();
        var player = Player.Instance;
        var tree = UnityEngine.Object.FindObjectsOfType<Tree>().First();
        var axe = ItemLibrary.GetItemByName("Axe", true);
        player.worldInventory.inventory.TryAddItem(axe);
        const string wood = Prefix + "woodcutting";
        SkillsApi.AwardExperience(player, wood, 25);
        SkillsApi.SelectPerk(player, wood, "efficient");
        AccessTools.Field(typeof(Tree), "hp").SetValue(tree, 3);
        tree.OnInteract(player.gameObject);
        Check(player.statusEffectsTarget.TryGetStatusEffectOfType<StatusEffect_Interacting>(out var work) &&
            work.remainingTurns == 2 && SkillsApi.GetExperience(player, wood) == 25, "native axe work schedules reduced time without early XP");
        float end = Time.realtimeSinceStartup + 15;
        while (SkillsApi.GetExperience(player, wood) == 25 && Time.realtimeSinceStartup < end) yield return null;
        Check(SkillsApi.GetExperience(player, wood) == 35 && axe.GetItemComponent<ItemComponent_Durability>().Durability < 50,
            "native axe action completes, consumes durability and grants Woodcutting XP");
        Check(InventoryModTools.TryOpen(Plugin.Id), "inventory opens Player Skills");
        yield return null;
        yield return null;
        var window = GameObject.Find("Player Skills")?.GetComponent<ModToolBehaviour>();
        Check(window && window.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text.StartsWith("Mining    ")), "skill details appear in the live inventory session");
        var buttons = window.GetComponentsInChildren<UnityEngine.UI.Button>();
        buttons.First(b => b.name == "Woodcutting").onClick.Invoke();
        var spend = buttons.Single(b => b.name == "Perk action");
        void Inspect(string node) => buttons.Single(b => b.name == node).onClick.Invoke();
        Inspect("second-wind");
        Check(!spend.interactable, "unearned points cannot be spent from the tree");
        SkillsApi.AwardExperience(player, wood, 215);
        Check(spend.interactable && SkillsApi.GetPerkRank(player, wood, "second-wind") == 0, "inspecting a node previews it before spending");
        spend.onClick.Invoke();
        Check(SkillsApi.GetLearnedPerks(player, wood).Count == 2 && SkillsApi.GetPerkRank(player, wood, "efficient") == 2, "both branches active after learning through the menu");
        Inspect("mastery");
        Check(!spend.interactable && window.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Any(t => t.text.Contains("Requires Felling cuts + Sawyer")), "mastery displays both prerequisites");
        Inspect("technique");
        string preview = Environment.GetEnvironmentVariable("SKILLS_TREE_PREVIEW");
        if (!string.IsNullOrEmpty(preview)) CaptureTree(window.GetComponentInParent<Canvas>(), preview);
        spend.onClick.Invoke();
        Inspect("efficient");
        Check(!spend.interactable, "an owned dependent protects the parent");
        Inspect("technique"); spend.onClick.Invoke();
        Check(SkillsApi.GetPerkRank(player, wood, "technique") == 0 && SkillsApi.GetAvailablePoints(player, wood) == 1, "leaf refund immediately restores a point");
        buttons.Single(b => b.name == "Social").onClick.Invoke();
        Check(buttons.Any(b => b.name == "first-impressions") && buttons.Any(b => b.name == "heart-to-heart"), "Social opens both branches in inventory");
        const string social = Prefix + "social";
        SkillsApi.AwardExperience(player, social, 1125);
        foreach (string id in new[] { "rapport", "good-company", "first-impressions", "heart-to-heart" })
        {
            Inspect(id); spend.onClick.Invoke();
            Check(SkillsApi.GetPerkRank(player, social, id) > 0, "Social menu learns " + id);
        }
        Inspect("life-of-the-party");
        Check(spend.interactable, "Social mastery opens after both branches");
        string socialPreview = Environment.GetEnvironmentVariable("SKILLS_SOCIAL_PREVIEW");
        if (!string.IsNullOrEmpty(socialPreview)) CaptureTree(window.GetComponentInParent<Canvas>(), socialPreview);
        buttons.First(b => b.name == "Close").onClick.Invoke();
        yield return null;
        Check(!window && InventoryModTools.ActiveToolId == null, "closing Player Skills releases the inventory");
        PauseMenuManager.Instance.Close();
        SocialChecks.Reactions();
        IntegrationChecks.PartyScene(player);
    }

    private static void TestFieldAndSave(Player player)
    {
        Check(SkillsApi.SkillIds.Distinct().Count() == 15 && SkillsApi.GetLevel(player, Mining) == 1, "skill catalog and new-world level");
        Check(!SkillsApi.SelectPerk(player, Mining, "efficient") && !SkillsApi.AwardExperience(null, Mining, 10), "level and player guards");
        foreach (var type in new[] { typeof(Tree), typeof(ResourceNode), typeof(QuarryResourceNode), typeof(ConstructionSite) })
            Check(Harmony.GetPatchInfo(AccessTools.Method(type, "OnInteract")).Transpilers.Any(p => p.owner == Plugin.Id), "native work hook on " + type.Name);
        new Harmony("silverpine.skills.checks").Patch(AccessTools.Method(typeof(Player), nameof(Player.StartLongInteraction)),
            prefix: new HarmonyMethod(typeof(Checks), nameof(CaptureWork)));
        var source = Inactive("Test vein").AddComponent<ResourceNode>();
        source.resourceNodeType = ResourceNodeType.Ore;
        var wrap = AccessTools.Method(typeof(Plugin).Assembly.GetType("SilverpineSkills.FieldWork"), "Start");
        int completed = 0, cancelled = 0;
        void Schedule(MonoBehaviour work) => wrap.Invoke(null, new object[] { player, (Action)(() => completed++), 40, (Action)(() => cancelled++), null, work });
        Schedule(source);
        failure();
        Check(cancelled == 1 && SkillsApi.GetExperience(player, Mining) == 0, "cancelled action has no XP");
        Schedule(source); success();
        Check(completed == 1 && SkillsApi.GetExperience(player, Mining) == 10, "completed player fieldwork pays XP");
        SkillsApi.AwardExperience(player, Mining, 15);
        Check(SkillsApi.GetLevel(player, Mining) == 2 && SkillsApi.SelectPerk(player, Mining, "efficient"), "level two unlocks the first choice");
        Check(SkillsApi.GetAvailablePoints(player, Mining) == 0 && !SkillsApi.LearnPerk(player, Mining, "second-wind") &&
            !SkillsApi.LearnPerk(player, Mining, "efficient"), "cannot double-spend a point or buy a perk twice");
        Schedule(source);
        Check(workTurns == 36, "selected efficiency perk reduces actual scheduled turns");
        SkillsApi.AwardExperience(player, Mining, 225);
        Check(!SkillsApi.LearnPerk(player, Mining, "rhythm") && !SkillsApi.LearnPerk(player, Mining, "mastery"), "parent and level gates apply to the API");
        Check(SkillsApi.GetPerkRank(player, Mining, "efficient") == 2 && SkillsApi.SelectPerk(player, Mining, "second-wind"), "level five upgrades and second branch");
        player.energy.Value = 10;
        Schedule(source); success();
        Check(workTurns == 32 && player.energy.Value == 14 && SkillsApi.GetPerkRank(player, Mining, "efficient") == 2, "recovery and efficiency apply to the same action");
        Check(SkillsApi.LearnPerk(player, Mining, "technique") && !SkillsApi.RefundPerk(player, Mining, "efficient"), "child requires continued ownership of parent");
        Schedule(source);
        Check(workTurns == 26 && SkillsApi.GetAvailablePoints(player, Prefix + "woodcutting") == 0, "advanced mining technique modifies ore work only and points stay local");
        Schedule(Inactive("tree").AddComponent<Tree>()); success();
        Schedule(Inactive("site").AddComponent<ConstructionSite>()); success();
        Check(SkillsApi.GetExperience(player, Prefix + "woodcutting") == 10 && SkillsApi.GetExperience(player, Prefix + "construction") == 15, "independent native paths");
        Check(SkillsApi.AwardExperience(player, Mining, int.MaxValue) && SkillsApi.GetExperience(player, Mining) == 1125 &&
            SkillsApi.GetPerkRank(player, Mining, "second-wind") == 3 && !SkillsApi.AwardExperience(player, Mining, 1), "mastery cap and overflow");
        Check(!SkillsApi.LearnPerk(player, Mining, "mastery") && SkillsApi.LearnPerk(player, Mining, "rhythm") &&
            SkillsApi.LearnPerk(player, Mining, "mastery"), "mastery requires both advanced perks");
        player.energy.Value = 10; Schedule(source); success();
        Check(workTurns == 18 && player.energy.Value == 18 && SkillsApi.GetAvailablePoints(player, Mining) == 0, "mastery and earlier perks stack at level ten");
        string slot = Path.Combine(Paths.GameRootPath, "TestData", "slot-a");
        File.WriteAllText(slot, "skills check");
        AccessTools.Method(typeof(ModSaveData), "AfterSave").Invoke(null, new object[] { slot });
        Schedule(source);
        ModSaveData.ResetForNewGame(); success();
        Check(SkillsApi.GetLevel(player, Mining) == 1 && SkillsApi.GetSelectedPerk(player, Mining) == null, "new world and stale callback isolation");
        AccessTools.Method(typeof(ModSaveData), "BeforeLoad").Invoke(null, new object[] { slot });
        AccessTools.Method(typeof(ModSaveData), "AfterLoad").Invoke(null, null);
        Check(SkillsApi.GetLevel(player, Prefix + "social") == 1 && SkillsApi.GetLearnedPerks(player, Prefix + "social").Count == 0,
            "an existing save begins Social at level one");
        Check(SkillsApi.GetLevel(player, Mining) == 10 && SkillsApi.GetLearnedPerks(player, Mining).Count == 5 &&
            SkillsApi.GetSelectedPerk(player, Mining) == "mastery", "per-save XP and full perk tree round trip");
        TestOldSave(player, slot);
        SerializationManager.loadingSave = true;
        try { Check(!SkillsApi.AwardExperience(player, Prefix + "construction", 10), "no XP during restore"); }
        finally { SerializationManager.loadingSave = false; }
    }

    private static void TestOldSave(Player player, string slot)
    {
        using (var writer = new BinaryWriter(File.Create(ModSaveData.CompanionPath(slot))))
        using (var sha = SHA256.Create())
        {
            writer.Write("ModdingTools.SaveData.1"); writer.Write(sha.ComputeHash(File.ReadAllBytes(slot)));
            writer.Write(1); writer.Write(Plugin.Id); writer.Write(Plugin.Id); writer.Write(1);
            writer.Write("{\"silverpine.skills.mining\":{\"Xp\":250,\"Perk\":\"second-wind\"},\"silverpine.skills.woodcutting\":{\"Xp\":150,\"Perk\":null}}");
        }
        AccessTools.Method(typeof(ModSaveData), "BeforeLoad").Invoke(null, new object[] { slot });
        AccessTools.Method(typeof(ModSaveData), "AfterLoad").Invoke(null, null);
        Check(SkillsApi.GetPerkRank(player, Mining, "second-wind") == 2 && SkillsApi.GetExperience(player, Mining) == 250 &&
            SkillsApi.GetAvailablePoints(player, Mining) == 2, "0.1.0 chosen perk keeps its rank and leaves two points at level five");
        Check(SkillsApi.GetLearnedPerks(player, Prefix + "woodcutting").Count == 0 &&
            SkillsApi.GetAvailablePoints(player, Prefix + "woodcutting") == 2, "unspent legacy level awards the full point budget");
        Check(SkillsApi.RefundPerk(player, Mining, "second-wind") && SkillsApi.GetAvailablePoints(player, Mining) == 3, "migrated perks can be refunded");
    }

    private static void CaptureTree(Canvas canvas, string path)
    {
        var camera = new GameObject("Tree capture").AddComponent<Camera>();
        var target = new RenderTexture(1600, 900, 24);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var nodes = canvas.GetComponentsInChildren<Transform>(true);
        var layers = nodes.Select(t => t.gameObject.layer).ToArray();
        try
        {
            foreach (var node in nodes) node.gameObject.layer = 30;
            camera.cullingMask = 1 << 30; camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .06f, .055f);
            camera.targetTexture = target; camera.transform.position = new Vector3(0, 0, -2000);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 8;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            for (int i = 0; i < nodes.Length; i++) nodes[i].gameObject.layer = layers[i];
            camera.targetTexture = null; target.Release();
            UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(camera.gameObject);
        }
    }
}

internal sealed class SceneRunner : MonoBehaviour { }
