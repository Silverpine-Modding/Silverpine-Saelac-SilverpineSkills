# Silverpine Skills 0.3.0

Open **Inventory → Mods → Player Skills**, pick a path, and select a node to inspect its effect and prerequisites. **Learn** spends one point in that path. Every learned perk stays active alongside the others.

Each path earns one point at levels **2, 4, 5, 7 and 10**. The two starting perks open at level 2 and their effects improve at levels 5 and 10. Each starting perk leads to a level-5 follow-up. Learning both follow-ups opens the level-10 mastery. You can earn all five nodes. Select a learned node and choose **Refund** to recover its point. Refund any dependent perks first.

Level 2 takes 25 total XP and level 10 takes 1,125. When loading a 0.1.0 save, each path keeps its XP and learned starting perk. Any other points earned by that level are available to spend.

| Path | How to train | Branches and mastery |
| --- | --- | --- |
| Mining | Mine a quarry rock or ore vein for 10 XP. | Efficient prospector → Vein reader and Second wind → Quarry rhythm lead to Master miner. |
| Woodcutting | Fell a tree or saw a plank for 10 XP. | Efficient woodsman → Felling cuts and Second wind → Sawyer’s rhythm lead to Master forester. |
| Construction | Finish a supplied site with a hammer for 15 XP. | Efficient builder → Framing square and Second wind → Sure footing lead to Master builder. |
| Social | Send a line and receive an NPC reply for 10 XP one-on-one or 15 XP in a group. | Rapport → First impressions and Good company → Heart-to-heart lead to Life of the party. |
| Guildcraft | Collect a guild payout for 15 XP plus 10 per tier above Beginner. | Negotiator → Supply broker and Distinction → Hazard pay lead to Guild patron. |
| Leadership | Cross an overworld tile with recruits for 6 XP per group, or help up a downed recruit for 12 XP. | Quartermaster → Trail rations and Vanguard → Shield drill lead to Rally. |
| Gardening | Plant a seed for 4 XP or harvest a crop or herb for 8 XP. | Abundance → Fruit set and Selective harvest → Herbalist lead to Harvest vigor. |
| Custom Crafting, Alchemy, Cooking, Joinery, Tailoring, Baking, Blacksmithing, Smelting | Finish a recipe for 5 XP plus 1 per five work minutes, up to 25 XP per batch. Each station has its own tree. | Artisan leads to the trade’s finishing perk. Batch work leads to Steady hands. Together they unlock a guaranteed extra copy of eligible crafted items at mastery. |

## Installation

Install the skills DLL in `BepInEx/plugins/SilverpineSkills/` with BepInEx 5 and Modding Tools 1.13.0 or later. XP and learned perks use the framework's save companion. Worlds without skills data start at level 1. Loading an earlier save restores its progression.

Mining, Woodcutting, Construction and Social use native Silverpine actions. The four gameplay integrations are BepInEx soft dependencies. Install any combination. The menu shows trainable paths and previously trained paths whose source is currently unavailable.

| Optional mod | Minimum version for this integration |
| --- | --- |
| Adventurers’ Guild | 1.7.0 |
| Companion Party | 0.2.6 |
| Custom Crafting Recipes | 1.7.0 |
| Custom Growables | 1.9.0 |

## Mod API

Reference `SilverpineSkills.dll` and the game's `Assembly-CSharp.dll`. The plugin ID is `silverpine.skills`. On the Unity main thread, use `SilverpineSkills.SkillsApi.SkillIds` to enumerate stable IDs. `GetLevel` and `GetExperience` read the active player's progression. `AwardExperience` credits a completed action.

`GetPerks(skillId)` lists each node’s ID, name, effect, required level and direct prerequisites. `GetLearnedPerks`, `GetAvailablePoints` and `GetPerkRank` query player state. Unlearned perks return rank zero. `LearnPerk` and `RefundPerk` enforce the same rules as the menu and return true on a change. The 0.1.0 method `SelectPerk` forwards to `LearnPerk`. `GetSelectedPerk` returns the most recently learned perk still owned.

`SkillChanged` supplies the player, skill ID and a flag indicating whether the level or learned perks changed. Listen to Modding Tools' `ModSaveData.Loaded` as well if you cache results across loads.

Each ID starts with `silverpine.skills.`. Suffixes are `mining`, `woodcutting`, `construction`, `social`, `guildcraft`, `leadership`, `gardening`, `handicraft`, `alchemy`, `cooking`, `joinery`, `tailoring`, `baking`, `blacksmithing` and `smelting`.

Custom Crafting Recipes registers levels for available paths. Recipe packs can use a schema-3 requirement such as `"skillRequirements": [{ "skill": "silverpine.skills.blacksmithing", "minLevel": 5 }]`. The skills mod supplies levels and leaves the choice of gated recipes to pack authors.

Build with `dotnet build SilverpineSkills.csproj -c Release -m:1` from this project in the Silverpine development tree. The sibling gameplay projects supply compile-time API definitions. Only deploy `SilverpineSkills.dll` from this project's build output.

For regression checks, build `Tests/Checks.csproj` in Release and run `python Tests/run_checks.py`. Add `--installed` to exercise the installed plugin binaries. The runner uses a separate game copy and test profile for the base game, each optional mod alone, all four together, and an inventory and party travel check in the game scene.
