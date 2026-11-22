using System;
using System.Collections.Generic;
using System.Linq;
using Silverpine.ModdingTools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverpineSkills;

internal sealed class SkillWindow : ModToolBehaviour
{
    private static readonly Color Pine = new(.08f, .14f, .13f), Panel = new(.12f, .20f, .18f);
    private static readonly Color Gold = new(.95f, .81f, .52f), Sage = new(.65f, .88f, .73f);
    private static readonly Color Muted = new(.75f, .79f, .75f), Line = new(.29f, .38f, .34f);
    private static readonly Vector2[] Slots = { new(454, 238), new(912, 238), new(454, 361), new(912, 361), new(683, 484) };
    private const int NodeWidth = 364, NodeHeight = 78;
    private SkillTree selected = SkillTree.Mining;
    private int inspected, epoch;
    private Button template, action;
    private readonly Dictionary<SkillTree, TextMeshProUGUI> paths = new();
    private readonly Button[] nodes = new Button[Slots.Length];
    private readonly Image[] markers = new Image[Slots.Length];
    private readonly List<(Image line, int parent)> links = new();
    private TextMeshProUGUI heading, training, progress, points, detailTitle, effect, requirements;
    private Image meter;

    internal static void Open(InventoryUI inventory, ModToolSession session)
    {
        var overlay = ModUi.CreateOverlay(inventory, "Player Skills", new Vector2(1390, 850));
        var window = overlay.Panel.AddComponent<SkillWindow>();
        window.AttachSession(session);
        try { window.Build(inventory); }
        catch { session.Close(); throw; }
    }

    private void Build(InventoryUI inventory)
    {
        epoch = SkillsApi.Epoch;
        template = ModUi.GetInventoryButtonTemplate(inventory);
        var background = GetComponent<Image>();
        background.sprite = null;
        background.color = Pine;
        Box("Paths", 0, 0, 339, 850, Panel);
        Label("Player Skills", 32, 24, 300, 53, 36);
        Box("Divider", 25, 89, 288, 2, Line);
        int y = 107;
        var visible = SkillTree.All.Where(tree => Plugin.Available(tree) || SkillsApi.GetExperience(Player.Instance, tree.Id) > 0).ToArray();
        int rowStep = Math.Min(51, (850 - y - 18) / visible.Length);
        foreach (var tree in visible)
        {
            var row = ButtonAt(tree.Name, 20, y, 299, rowStep - 5, () => { selected = tree; inspected = 0; Refresh(); });
            var text = TextOf(row);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.margin = new Vector4(14, 0, 4, 0);
            paths.Add(tree, text);
            y += rowStep;
        }
        heading = Label("", 386, 22, 775, 60, 38);
        ButtonAt("Close", 1242, 30, 108, 43, () => FrameworkSession.Close());
        Box("Divider", 386, 89, 964, 2, Line);
        training = Label("", 386, 97, 966, 49, 23);
        progress = Label("", 386, 148, 390, 40, 22);
        points = Label("", 779, 148, 570, 40, 22);
        points.alignment = TextAlignmentOptions.MidlineRight; points.color = Gold;
        Box("XP track", 386, 197, 964, 5, Line);
        meter = Box("XP", 386, 197, 964, 5, Gold);

        // Both branches feed the mastery. The two incoming lines meet different ports
        // so each prerequisite remains legible when only one branch is learned.
        for (int i = 2; i < Slots.Length; i++)
        {
            foreach (string parentId in selected.Perks[i].Prerequisites)
            {
                int parent = Array.FindIndex(selected.Perks.ToArray(), perk => perk.Id == parentId);
                var from = Slots[parent] + new Vector2(NodeWidth / 2, NodeHeight);
                var to = Slots[i] + new Vector2(NodeWidth / 2, 0);
                if (i == 4) to.x += parent == 2 ? -105 : 105;
                float midY = (from.y + to.y) / 2;
                Link(parent, from.x, from.y, 4, midY - from.y + 2);
                if (from.x != to.x) Link(parent, Mathf.Min(from.x, to.x), midY, Mathf.Abs(to.x - from.x) + 4, 4);
                Link(parent, to.x, midY, 4, to.y - midY);
            }
        }
        for (int i = 0; i < nodes.Length; i++)
        {
            int slot = i;
            var pos = Slots[i];
            nodes[i] = ButtonAt("Perk", pos.x, pos.y, NodeWidth, NodeHeight, () => { inspected = slot; Refresh(); });
            nodes[i].GetComponentInChildren<TextMeshProUGUI>().enableWordWrapping = false;
            markers[i] = Box("Selected perk", pos.x, pos.y, 5, NodeHeight, Gold);
        }
        Box("Perk details", 375, 590, 985, 236, Panel);
        detailTitle = Label("", 399, 603, 920, 47, 30);
        effect = Label("", 399, 655, 927, 70, 23);
        requirements = Label("", 399, 738, 643, 72, 20); requirements.color = Muted;
        action = ButtonAt("Perk action", 1058, 748, 272, 52, Apply);
        SkillsApi.SkillChanged += Changed;
        Refresh();
    }

    private void Apply()
    {
        string id = selected.Perks[inspected].Id;
        if (SkillsApi.GetPerkRank(Player.Instance, selected.Id, id) > 0) SkillsApi.RefundPerk(Player.Instance, selected.Id, id);
        else SkillsApi.LearnPerk(Player.Instance, selected.Id, id);
    }

    private void Changed(Player player, string id, bool benefitsChanged) => Refresh();

    private void Refresh()
    {
        var player = Player.Instance;
        foreach (var pair in paths)
        {
            pair.Value.text = $"{pair.Key.Name}   <size=80%>Lv. {SkillsApi.GetLevel(player, pair.Key.Id)}</size>";
            pair.Value.color = pair.Key == selected ? Gold : Color.white;
        }
        int level = SkillsApi.GetLevel(player, selected.Id), xp = SkillsApi.GetExperience(player, selected.Id);
        int start = SkillsApi.Threshold(level), next = SkillsApi.Threshold(level + 1);
        bool mastered = level == SkillsApi.MaxLevel;
        int budget = SkillsApi.GetAvailablePoints(player, selected.Id);
        heading.text = $"{selected.Name}    <size=65%>Level {level}</size>";
        progress.text = mastered ? "Maximum level" : $"{xp - start} / {next - start} XP to level {level + 1}";
        points.text = $"{budget} perk point{(budget == 1 ? "" : "s")}" + (mastered ? "" : $"   ·   Next at level {SkillsApi.NextPointLevel(level)}");
        meter.rectTransform.sizeDelta = new Vector2(mastered ? 964 : 964f * (xp - start) / (next - start), 5);
        bool available = Plugin.Available(selected);
        training.text = available ? selected.Training : "Progress saved. Enable the associated mod to use this tree.";
        var learned = SkillsApi.GetLearnedPerks(player, selected.Id);
        foreach (var (line, parent) in links) line.color = learned.Contains(selected.Perks[parent].Id) ? Sage : Line;
        for (int i = 0; i < nodes.Length; i++)
        {
            var perk = selected.Perks[i];
            int rank = SkillsApi.GetPerkRank(player, selected.Id, perk.Id);
            bool eligible = available && level >= perk.RequiredLevel && perk.Prerequisites.All(learned.Contains) && budget > 0;
            string status = rank > 0 ? (perk.Prerequisites.Count == 0 ? $"Learned · Rank {rank}" : "Learned") :
                level < perk.RequiredLevel ? $"Level {perk.RequiredLevel}" : !perk.Prerequisites.All(learned.Contains) ? "Prerequisite needed" :
                eligible ? "Available" : budget == 0 ? "Point needed" : "Unavailable";
            var button = nodes[i];
            button.name = perk.Id;
            var text = TextOf(button);
            text.text = $"{perk.Name}\n<size=77%>{status}</size>";
            text.color = rank > 0 ? Sage : eligible ? Gold : Muted;
            ((Image)button.targetGraphic).color = rank > 0 ? new Color(.18f, .32f, .25f) : Panel;
            markers[i].enabled = inspected == i;
        }
        var focus = selected.Perks[inspected];
        bool owned = learned.Contains(focus.Id);
        var dependent = selected.Perks.FirstOrDefault(p => learned.Contains(p.Id) && p.Prerequisites.Contains(focus.Id));
        detailTitle.text = focus.Name;
        effect.text = focus.Effect;
        string parents = string.Join(" + ", focus.Prerequisites.Select(id => selected.ByPerkId[id].Name));
        requirements.text = owned && dependent != null ? "Refund " + dependent.Name + " first." :
            $"Level {focus.RequiredLevel}" + (parents.Length == 0 ? "" : $"   ·   Requires {parents}");
        TextOf(action).text = owned ? "Refund 1 point" : "Learn · 1 point";
        action.interactable = available && (owned ? dependent == null :
            budget > 0 && level >= focus.RequiredLevel && focus.Prerequisites.All(learned.Contains));
    }

    private static TextMeshProUGUI TextOf(Button button) => button.GetComponentInChildren<TextMeshProUGUI>(true);
    private Button ButtonAt(string label, float x, float y, int width, int height, Action click)
    {
        var button = ModUi.CloneButton(template, transform, label, click, height);
        button.name = label;
        Place((RectTransform)button.transform, x, y, width, height);
        var plate = (Image)button.targetGraphic;
        plate.sprite = null; plate.color = new Color(.21f, .30f, .26f);
        var text = TextOf(button);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(6, 0); text.rectTransform.offsetMax = new Vector2(-6, 0);
        text.richText = true; text.enableAutoSizing = true;
        text.fontSizeMin = 18; text.fontSizeMax = 25;
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private TextMeshProUGUI Label(string value, float x, float y, int width, int height, int size)
    {
        var label = ModUi.CloneTitle(template, transform, value);
        Place(label.rectTransform, x, y, width, height);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.richText = true; label.enableAutoSizing = false; label.fontSize = size;
        label.enableWordWrapping = true; label.color = Color.white; label.raycastTarget = false;
        return label;
    }

    private void Link(int parent, float x, float y, float width, float height) =>
        links.Add((Box("Prerequisite", x, y, width, height, Line), parent));
    private Image Box(string name, float x, float y, float width, float height, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(transform, false);
        Place(image.rectTransform, x, y, width, height);
        image.color = color; image.raycastTarget = false;
        return image;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private void Update()
    {
        if (epoch != SkillsApi.Epoch || !SkillsApi.Current(Player.Instance)) FrameworkSession?.Close();
    }
    protected override void OnDestroy()
    {
        SkillsApi.SkillChanged -= Changed;
        base.OnDestroy();
    }
}
