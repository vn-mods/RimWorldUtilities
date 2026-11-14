# RimWorldUtilities — Game API Notes

**Research baseline:** RimWorld 1.7.4998b. Findings below were extracted from the project's earlier source-research notes; they have **not** been independently rechecked against the current repository or tested in-game for this cleanup. Revalidate API signatures, IL patterns, and behavior when upgrading RimWorld or Harmony.

**Purpose:** Preserve non-obvious RimWorld behavior, reasons for compatibility-sensitive decisions, and entry points for future investigation. Implementation details that are obvious from our own source code and historical test counts do not belong here.

Paths below are relative to `refs/decompiled/Assembly-CSharp/` unless otherwise stated. This tree is read-only and excluded from Git.

## Biosculpter cycles (rechecked against installed 1.7.5005 rev1082)

- `CompBiosculpterPod.TryAcceptPawn(Pawn, CompBiosculpterPod_Cycle)` initializes
  `currentCycleTicksRemaining` to `cycle.Props.durationDays * 60000f`. All four
  vanilla cycle types share this work counter; their completion handlers are
  separate. `CompProperties_BiosculpterPod_BaseCycle.durationDays` is a `float`.
- `GetCycleSpeedFactorForPawn(Pawn p)` returns `max(0.1f, pod stat * biotuning *
  occupant stat)`. The occupied `CycleSpeedFactor` getter delegates to it;
  `CompTick()` subtracts that factor only while powered and handles completion at
  zero. `CompInspectStringExtra()` divides remaining work by the same factor.
  A Postfix can set a fixed processing rate without touching stored progress.
- `Occupant` temporarily returns `pawnEnteringBiosculpter` during entry; verify
  `ParentHolder` and `GetDirectlyHeldThings()` as well when requiring actual
  containment. Contained pawns are despawned/suspended. `IsColonistPlayerControlled`
  cannot qualify them. Reject prisoners/slaves before consulting `IsColonist`.
- Native carry-to-pod menus admit colony prisoners. Pod faction alone cannot
  establish eligibility. Check actual pawn faction, free-colonist/guest/quest
  status, actual container and map `ParentFaction` dynamically.
- Biotuning's 4,800,000-tick timer, the cumulative 60,000-tick power-outage counter
  and biological-age-based age-reversal scheduling are independent of processing
  speed. Preserve those paths. `PostExposeData()` saves the raw work counter.
- Relevant signatures and normalized IL match between reference assembly version
  `1.7.9803.38985` (game 1.7.4998) and installed `1.7.9810.32479` (1.7.5005).
  See [implemented behavior, numerical limits and manual checks](biosculpter-cycles.md).
- `CompBiosculpterPod_AgeReversalCycle.CycleCompleted(Pawn)` has one biological-age
  assignment followed by `ResetAgeReversalDemand(ViaTreatment)` and its message.
  Adjust the setter's input to preserve native difficulty reduction, execute age
  invariants once and schedule from the final age. A late Postfix would otherwise
  leave the deadline/message based on an intermediate age or require another reset.
- `Pawn_AgeTracker.AgeBiologicalTicks` is the public mutation API: its setter calls
  `CalculateInitialGrowth()` and `RecalculateLifeStageIndex()`, including native
  stage-change notifications. Chronological age uses separate `birthAbsTicksInt`.
  Do not write serialized age fields directly. These implementations and the
  completion method were rechecked against the installed assembly as well.
- `AdultMinAge` uses the **first Adult developmental flag**, not the stage name.
  Installed human XML starts `HumanlikeTeenager` at 13; its omitted developmental
  field defaults to Adult. The later `HumanlikeAdult` at 18 is not the treatment
  minimum. Respect the live API for modded races/minimums; do not edit definitions.
- `ResetAgeReversalDemand(ViaTreatment)` sets the biological-age deadline to
  `max(final age, 25 years) + 60 days`; younger rejuvenated pawns therefore get a
  longer remaining interval until demand. Native completion invokes the cycle
  while the pawn is still contained, before ejection. Verify actual component
  identity and current containment, not only its parent building or pawn faction.

## Ability and ritual cooldowns (rechecked against current local sources/assembly)

- `Pawn.IsColonist` includes secure slaves; `IsColonistPlayerControlled` additionally
  requires spawn and no mental state but still admits controlled slaves and some
  quest colonists. `IsPlayerControlled` also admits colony mechanoids/subhumans.
  Faction equality alone does not establish colonist control. `GuestStatus.Guest`
  on an inactive guest tracker with no host returns a null `Pawn.GuestStatus`.
  Source: `Verse/Pawn.cs`.
- `Ability.CanCast` reads private `inCooldown` directly. `OnCooldown` and
  `CooldownTicksRemaining` are separate getters. The latter also drives
  `Command_Ability.GizmoOnGUI`'s cooldown overlay. `CooldownTick` consumes that
  getter and clears `inCooldown`/regenerates charges on expiry: returning zero
  globally without protecting the tick path can destroy raw cooldown state.
  `StartCooldown(int)` assigns the deadline and may refill charges;
  `ResetCooldown()` only clears `inCooldown`. Neither is a safe global substitute
  for contextual reads. Sources: `RimWorld/Ability.cs`, `Command_Ability.cs`.
- `PreActivate` notifies group abilities **and ideology ritual precepts**;
  `JobDriver_CastAbility` can start cooldown again in its finish action, and
  `Verb_AbilityShoot.TryCastShot` assigns one after a successful shot. Preserving
  these paths retains jobs, group effects and NPC-visible shared deadlines.
  `Psycast.CanCast` calls base first, then checks entropy/psyfocus; its `Activate`
  still consumes resources. Sources: `RimWorld/Ability.cs`, `Psycast.cs`,
  `Verse/AI/JobDriver_CastAbility.cs`, `Verse/Verb_AbilityShoot.cs`.
- `Command_Ritual.ValidateDisabledState` has two shared-deadline field reads;
  `FloatMenuOptionProvider_StartRitual.GetOptionsFor`'s iterator has four. Both
  also consult the assigned single role's source ability. These counts are
  verified by the regression suite against the current binary IL.
- `Precept_Ritual.GetRitualBeginWindow` builds a `Dialog_BeginRitual` callback
  that calls `RitualBehaviorWorker.TryExecuteOn(..., playerForced: true)`.
  The worker revalidates `CanStartRitualNow` and `CanExecuteOn`; it has **no
  cooldown gate**. Speech and Conversion worker overrides add role/convertee
  checks, not cooldown validation. Startup then notifies assigned group abilities
  and calls `Notify_CooldownFromAbilityStarted(int)` on the shared precept.
  Sources: `Precept_Ritual.cs`, `Dialog_BeginRitual.cs`, `RitualBehaviorWorker.cs`,
  `RitualBehaviorWorker_Speech.cs`, `RitualBehaviorWorker_Conversion.cs`.
- Precept deadlines belong to the ideology, potentially shared across factions.
  `Faction.OfPlayer.ideos.Has(ritual.ideo)` alone is insufficient ownership proof.
  `Precept_Role.FillOrUpdateAbilityList` reuses role abilities and changes their
  `pawn`/caster on reassignment, so even clearing a currently player-owned role
  ability can transfer a cleared timer to a future NPC holder. Preserve raw state.
- Ability `ExposeData` writes the raw cooldown fields. Ritual serialization saves
  `abilityOnCooldownUntilTick` separately from `lastFinishedTick`; the latter
  drives the 1,200,000-tick (20-day) repeat-quality penalty. Neither is a disposable
  player-only cache. See [implementation and manual checks](ability-cooldowns.md).

## Item Editor

### Selection and edit targets

- `RimWorld.Selector.SingleSelectedThing` returns a `Verse.Thing` only when precisely one selected object is a Thing. `NumSelected` also counts non-Thing objects. Source: `RimWorld/Selector.cs`.

- `RimWorld.MainButtonWorker_ToggleTab.Activate()` / `MainTabsRoot.ToggleTab(...)` do **not** clear map selection. `SelectorOnGUI()` opens Inspect automatically only if there is no open main tab. Utilities can read `Verse.Find.Selector.SingleSelectedThing` without caching an old selection or patching the tab system. Sources: `RimWorld/MainButtonWorker_ToggleTab.cs`, `RimWorld/MainTabsRoot.cs`, `RimWorld/Selector.cs`.

- Before **every mutation**, revalidate selected Thing identity, current-map spawn state, and eligibility. Do not edit destroyed, despawned, held/equipped, or previously selected items. Ordinary targets are spawned `ThingCategory.Item` objects; exclude pawns, buildings, corpses, and minified things. Stack editing additionally requires `ThingDef.stackLimit > 1`; HP/quality can apply to non-stackable equipment lying on the map. Source: `Verse/Thing.cs`.

### Stack count and notifications

- `Verse.Thing.stackCount` is a public field with **no automatic notification**. Validate `1..item.def.stackLimit` and mutate only the selected Thing. `Verse.ThingDef.stackLimit` is shared by **all** Things of that definition: changing it is a separate, global per-type operation. Sources: `Verse/Thing.cs`, `Verse/ThingDef.cs`.

- After changing a spawned stack count, refresh `Thing.DirtyMapMesh(map)`, `ListerMergeables.Notify_ThingStackChanged(thing)`, and (for applicable resources) `ResourceCounter.CheckUpdateResource(thing)`. `SplitOff()` and `TryAbsorbStack()` have other side effects and are **not** drop-in count setters. Sources: `Verse/Thing.cs`, `RimWorld/ListerMergeables.cs`, `RimWorld/ResourceCounter.cs`.

### HP and quality

- `Verse.Thing.HitPoints` does not clamp or validate values. Only edit definitions with `ThingDef.useHitPoints`; validate `1..Thing.MaxHitPoints`, where the maximum comes from the live `MaxHitPoints` stat (not just base def data). Clear `StatDefOf.MaxHitPoints.Worker` cache for the target before an Apply/Max calculation. Sources: `Verse/Thing.cs`, `Verse/ThingDef.cs`, `RimWorld/StatWorker.cs`.

- Quality requires `ThingCompUtility.TryGetComp<CompQuality>(thing)`. `CompQuality.SetQuality(category, source: null)` already invokes native quality callbacks; do not call them twice. A no-op change should not invoke the setter. `Verse.Book.PostQualitySet()` regenerates book content; exclude `Book` and subclasses from quality editing. Sources: `Verse/ThingCompUtility.cs`, `RimWorld/CompQuality.cs`, `Verse/Book.cs`.

- After a quality change, invalidate the target's MaxHitPoints cache and recompute its maximum; retain **absolute** HP, clamped if necessary, rather than preserving damage percentage. Revalidate the selected target after native callbacks before making any follow-up HP change. Source: `RimWorld/StatWorker.cs`.

- `Verse.Widgets.TextField(...)` allows a raw text buffer independent of game state; validate when applying rather than mutating as the player types. A `FloatMenu` action must capture the original Thing and revalidate it on invocation. Sources: `Verse/Widgets.cs`, `Verse/FloatMenu.cs`.

## Persistent large stacks — save-safety rules

### Why a definition-level limit is required

- **Critical data-loss risk:** `Verse.Thing.ExposeData()` serializes a stack count without applying the current limit, but `Thing.SpawnSetup()` clamps `stackCount` to `def.stackLimit` when the Thing is spawned. Ordinary map Things are spawned during `Map.FinalizeLoading()`. A saved map stack of **50,000** loaded with a limit of **75** can lose **49,925** units, rather than splitting into smaller stacks. Sources: `Verse/Thing.cs`, `Verse/Map.cs`, `Verse/GenSpawn.cs`, `Verse/Scribe_Values.cs`.

- `ThingOwner<T>.ExposeData()` loads held Things without necessarily calling `SpawnSetup()`; inventory, carried, caravan, and transporter stacks may remain oversized after a reset, then fail or lose quantities later during placement/merging. **A successful load is not proof that a downgrade is safe.** Sources: `Verse/ThingOwner.cs`, `Verse/Pawn_InventoryTracker.cs`, `Verse/Pawn_CarryTracker.cs`, `Verse/GenDrop.cs`.

- `ThingUtility.TryAbsorbStackNumToTake(...)` may compute **negative** transferable quantities for a destination already above its limit; the existing notes confirmed this in an isolated original-assembly check. `ListerMergeables.ShouldBeMergeable()` also assumes counts do not exceed the effective limit. Sources: `Verse/ThingUtility.cs`, `Verse/Thing.cs`, `RimWorld/ListerMergeables.cs`.

- Placement and hauling are not lossless normalization tools: `GenPlace` may split but fail for lack of space; a spawned remainder still meets the clamp. Native hauling, storage, and `ThingOwner` use `def.stackLimit` in different places. Mass and carrying capacity remain per-unit constraints. Sources: `Verse/GenPlace.cs`, `Verse/Pawn_CarryTracker.cs`, `Verse/ThingOwner.cs`, `RimWorld/MassUtility.cs`.

### Stack limit reset and save safety

Resetting a custom stack limit does not automatically normalize existing oversized stacks.

Important findings for RimWorld 1.7.4998b:

- `Thing.SpawnSetup()` can permanently truncate stack counts exceeding the effective `ThingDef.stackLimit`.

- Held or nested items may retain oversized counts after loading, potentially causing problems during subsequent spawning or merging.

- `IThingHolder` traversal does not guarantee complete coverage of all saved Things. Quest parts, transport-ship managers, and third-party mods may maintain additional storage.

- Mod settings are shared across saves. Resetting limits while one save is loaded does not make other saves safe.

- A complete, lossless migration and safe uninstallation mechanism are not implemented.

Do not describe the existing reset as lossless or automatically safe for existing saves.

If automatic stack normalization is implemented in the future, it requires separate API research, comprehensive storage coverage, quantity verification, and recovery handling. A generic `IThingHolder` traversal alone is insufficient.

### Current approach and boundaries

- Use **per-ThingDef persistent overrides**, not per-stack exceptions. Increase the effective `ThingDef.stackLimit` **before maps load** so vanilla spawning, merging, storage, and hauling agree on the same value. Default opt-in limit in the notes: **100,000**, while respecting higher limits from other mods. Only support eligible ordinary stackable, non-`saveCompressible` item definitions. Sources: `Verse/ThingDef.cs`, `Verse/PlayDataLoader.cs`.

- `Verse.Mod.GetSettings<T>()` loads mod settings when the Mod object is created; definitions are resolved later. The `[StaticConstructorOnStartup]` / `LongEventHandler.ExecuteWhenFinished()` path is used to apply saved overrides after definitions are resolved but before a saved map spawns. Startup ordering is version-sensitive and must be checked after upgrades. Sources: `Verse/LoadedModManager.cs`, `Verse/PlayDataLoader.cs`, `Verse/LongEventHandler.cs`, `Verse/Root.cs`, `Verse/Root_Play.cs`.

- Capture a resolved pre-override baseline for display. Preserve higher effective limits supplied by other mods; do not silently lower them. A runtime raise must notify affected stacks in each loaded map through `ListerMergeables.Notify_ThingStackChanged()`. Unknown modded storage/stack behavior is not guaranteed compatible.

- `Verse.MapFileCompressor` has a separate compressed path without arbitrary stack quantities. Avoid opting `saveCompressible` definitions into large stacks unless their save format is separately verified. Source: `Verse/MapFileCompressor.cs`.

- Extremely large counts can overflow native integer intermediates in weighted merges and finite-container capacity calculations. Treat 100,000 as a **conservative configured default**, not a safety guarantee for every modded ThingDef. Sources: `Verse/Thing.cs`, `Verse/ThingOwner.cs`.

### Reset and uninstall are destructive without migration

- The reset UI **schedules** removal of our per-type override for the **next full process restart**; it does not lower a limit mid-session or normalize stacks. A confirmation can be cancelled; re-enabling before exit cancels a pending reset. The startup pass consumes the marker and leaves the freshly resolved vanilla/other-mod limit in place.

- Reset affects effective rules across **all saves in the active game profile**, not just the current map. Old saves and held items are not scanned or converted. Removing the mod, deleting settings, or another mod lowering a limit can trigger truncation/invalid merges later. **Never promise a lossless reset, downgrade, or uninstall.**

## Shared settings and persistence

- `Verse.Mod.GetSettings<T>()` supports the mod's shared `ModSettings` object; the notes record `CustomStackLimitSettings` as the common store for multiple features. Each writer must copy the **latest** settings, change only its own fields, and verify persistence without overwriting unrelated preferences. Source: `Verse/Mod.cs`.

- Keep the established settings class, mod folder/class names, and XML identifiers stable for migration. The native filename is based on `Mod_{Content.FolderName}_{ModClassName}.xml` inside the active game profile. Source: `Verse/LoadedModManager.cs`.

- Existing recorded tags: `enabledStackLimits`, `originalStackLimits`, `pendingStackLimitResets`, `alwaysLegendaryCrafting`, `craftingSpeedMultiplier`, `buildingSpeedMultiplier`, `researchSpeedMultiplier`, `enabledColonistNeeds`. Maintain backwards-compatible defaults for absent tags.

- Settings writes in the notes use a temporary file, Scribe read-back verification, and replacement, instead of overwriting the only settings file before verifying it. Do not interrupt another active Scribe save/load. Preserve failed-write state and avoid per-frame writes. Sources: `Verse/ScribeSaver.cs`, `Verse/Scribe_Deep.cs`, `Verse/Scribe_Collections.cs`.

## Crafting, construction, and research

### Always Legendary: two narrow quality paths

- **Recipe products:** patch the quality-generation call inside private static `Verse.GenRecipe.PostProcessProduct(Thing, RecipeDef, Pawn, Precept_ThingStyle, ThingStyleDef, int?)`. Do **not** globally patch `QualityUtility.GenerateQualityCreatedByPawn(Pawn, SkillDef, bool)`: it is shared with unrelated paths. Generate vanilla quality **once** (including RNG/inspiration effects), then optionally substitute Legendary before native `CompQuality.SetQuality` and its art/notification processing. Source: `Verse/GenRecipe.cs`.

- **Construction:** patch the generation call in `RimWorld.Frame.CompleteConstruction(Pawn)`, not every quality generator call. The Frame may already be destroyed by the time quality is generated: do **not** use its destroyed status as an eligibility rejection at that point. Installation of existing buildings does not regenerate quality; cube-sculpture construction follows a separate path. Sources: `RimWorld/Frame.cs`, `RimWorld/Blueprint_Install.cs`, `RimWorld/JobDriver_BuildCubeSculpture.cs`.

- For recipe crafting, require a matching active `JobDriver_DoBill`, ordinary `Bill_Production`, actual worktable, non-surgery recipe, supported quality-bearing product, and a qualifying player-faction worker. Construction instead matches the Frame completion context and a quality-bearing building. **Exclude** prisoners, NPCs, autonomous/medical jobs and unrelated products; player-faction slaves/mechanoids can be eligible. Sources: `Verse/AI/JobDriver_DoBill.cs`, `Verse/RecipeDef.cs`, `RimWorld/Frame.cs`.

- Each Harmony transpiler verifies exactly one expected native call/pattern, preserves labels and exception metadata, and returns original IL unchanged on missing/ambiguous input. Quality-path failures are independent; warn once rather than spamming logs. Other mods can still replace or bypass these paths.

### Crafting speed

- `Verse.AI.Toils_Recipe.DoRecipeWork()` creates the interval callback used by `JobDriver_DoBill`; the callback subtracts native work from `JobDriver_DoBill.workLeft` and updates unfinished work. Patch **only the computed decrement**, not `delta`, skill XP, the work-speed stat, or the saved `workLeft` field. Sources: `Verse/AI/Toils_Recipe.cs`, `Verse/AI/JobDriver_DoBill.cs`.

- Resolve the actual `Action<int>` callback from the original IL rather than a compiler-generated method name. Strictly match one native subtract/store pattern; fail closed on incompatible IL. Multipliers: `1, 2, 3, 5, 10, 20` (1× leaves the native float unchanged).

- Scope: eligible ordinary production bills (including art, tailoring, cooking, smithing, etc.), **not** surgery, autonomous forming, research, construction, hauling, or unrelated work. XP per tick remains native, so total XP per finished product can decrease. Changing speed mid-job affects future intervals only.

### Building speed

- Normal construction goes through `RimWorld.JobDriver_ConstructFinishFrame.MakeNewToils()` and a generated interval callback. Its native work value feeds **both** botch probability and `Frame.workDone`. Scale the shared work value **after** material factors and **before both uses**. Scaling progress alone would change the failure model. Sources: `RimWorld/JobDriver_ConstructFinishFrame.cs`, `RimWorld/Frame.cs`.

- Resolve the iterator with Harmony `AccessTools.EnumeratorMoveNext`, then locate the interval callback by IL structure, not generated names. Require the expected shared work/probability/progress pattern; on mismatch keep native IL and disable only Building Speed.

- Affect ordinary Frame construction, including terrain and non-quality buildings. Do not affect repairs, deconstruction, smoothing, roof work, relocation, or separate construction drivers. `Frame.workDone` persists natively; changing the multiplier must not rescale existing progress. XP per tick remains native; larger intervals can alter realized RNG outcomes even if the native work-based botch formula is preserved.

### Research speed

- `RimWorld.JobDriver_Research.MakeNewToils()` builds an interval callback that sends work through `RimWorld.ResearchManager.ResearchPerformed(float, Pawn)`. Patch **that callback's call site**, not the global research manager or project cost. Let `ResearchPerformed` retain native difficulty, tech, debug, progress, and completion logic. Sources: `RimWorld/JobDriver_Research.cs`, `RimWorld/ResearchManager.cs`.

- Research gained via books (`AddProgress`), instant-completion effects, Anomaly knowledge, and other special study paths is separate and must remain unscaled. Sources: `RimWorld/ReadingOutcomeDoerGainResearch.cs`, `RimWorld/CompUseEffect_FinishRandomResearchProject.cs`, `RimWorld/JobDriver_StudyItem.cs`.

- Require an exact ordinary `JobDriver_Research`/worker/current-job match, spawned research bench, and an ordinary active research project (positive base cost, no knowledge category/cost). Scale only that worker's work contribution; preserve Intellectual XP per tick. Use the same six speed choices, fail closed on changed IL, and keep speed-path availability independent.

### Shared worker rule for production/research

- The recorded policy is an alive, non-destroyed **actual player-faction** worker who is not a prisoner. It does **not** require `Pawn.IsColonist` or humanlike status; player-faction slaves and mechanoids may qualify. Each feature adds its own exact job, target, and product/project requirements. **Do not reuse this broad policy for Colonist Needs.** Source: `Verse/Pawn.cs`.

## Colonist Needs — Keep Full

### Eligibility and native update

- Needs use a **stricter** rule: living actual player-faction colonists (`Pawn.IsColonist`) who are not prisoners, slaves, guests, quest lodgers, mechanoids, or suspended. Children/babies and eligible unspawned travelers are included; there is no map/spawn requirement. Check nullable `Pawn.GuestStatus`, not only a guest tracker's default enum. Sources: `Verse/Pawn.cs`, `RimWorld/Pawn_GuestTracker.cs`, `RimWorld/QuestUtility.cs`.

- `RimWorld.Need.CurLevel` clamps to the live `Need.MaxLevel`; the maximum is **not always 1**, especially for food. Refill only existing matching needs using `CurLevel = MaxLevel`; do not create Need instances or bypass their normal lifecycle. Sources: `RimWorld/Need.cs`, `RimWorld/Need_Food.cs`.

- The native `Pawn_NeedsTracker.NeedsTrackerTickInterval(int delta)` uses `pawn.IsHashIntervalTick(150, delta)`. Prefix/postfix may refill before/after the original native loop but must neither skip it nor call `NeedInterval()` manually. Re-read need references and eligibility afterward; other mods may add/remove needs or change a pawn's state. The hash gate is pawn-specific, **not** `TicksGame % 150`. Sources: `RimWorld/Pawn_NeedsTracker.cs`, `Verse/Gen.cs`.

- Immediate enabling can use `PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive` with the stricter filter. Its lists are temporary shared results; do not retain them. Traveling pawns on native world tick paths receive the same tracker update without a second global tick loop. Custom world holders are not guaranteed covered. Sources: `RimWorld/PawnsFinder.cs`, `RimWorld/Planet/WorldPawns.cs`, `Verse/Pawn.cs`.

- Basic identifiers: `Food`, `Rest`, `Joy`, `Mood`, `Comfort`, `Beauty`, `Outdoors`, `Indoors`. Each is independent and off by default. This changes the **current** need level, not thoughts, memories, target mood, mental states, or existing conditions; native updates and intervening decay remain possible.

### Biotech: Learning and Play

- Optional identifiers: `Learning` (children) and `Play` (babies). Gate enabling/refilling through `ModsConfig.BiotechActive`; preserve saved choices when Biotech is absent, but do not expose active controls or create missing Need instances. Vanilla development-stage filters determine who possesses each need. Sources: `Verse/ModsConfig.cs`, `RimWorld/Pawn_NeedsTracker.cs`, `Data/Biotech/Defs/NeedDefs/Needs.xml` (game content).

- `Need_Learning.NeedInterval()` participates in native growth-point calculations; keeping Learning full affects growth **through vanilla behavior**, without directly writing growth points, jobs, or age. Play is similarly refilled through `CurLevel`, without calling `Need_Play.Play(float)`. Sources: `RimWorld/Need_Learning.cs`, `RimWorld/Need_Play.cs`, `Verse/Pawn_AgeTracker.cs`.

- Deathrest, chemical needs, and Hemogen were explicitly listed as **not implemented** in the source notes.

## Verification boundaries and high-risk manual checks

The original notes report many builds, isolated native-assembly probes, and fresh-process settings tests. **Those are not full game/map/save round trips**; historical assertion counts are intentionally omitted here. After behavior changes, verify in-game as appropriate:

1. **High priority — save integrity:** enable large stacks, save 50,000-unit map and held stacks, fully restart, reload, then haul/merge/split/store and compare total counts. Test reset/uninstall only on disposable copies of saves; check map, pawn inventory, caravan, and nested containers.

2. **Item editing:** change map selection with Utilities/quality menus open; edit HP, quality, counts while paused; inspect book exclusions and adjusted max HP.

3. **Harmony paths:** craft and construct quality-bearing/non-quality products; compare each speed at 1× and higher; check ingredients, progress, XP, normal native notifications/botches, worker eligibility, and isolated failures of individual patches.

4. **Research and needs:** verify ordinary research completion and unaffected books/Anomaly paths; test child Learning, baby Play, caravans, missing needs, prisoners/slaves/guests, and behavior with Biotech disabled.

5. **Compatibility and persistence:** fully restart to test settings, pending resets, and mid-job progress; test combinations with mods changing stack limits, quality, work callbacks, research, and needs. A compiling mod is not proof of gameplay compatibility.

When new research produces an actionable, non-obvious finding, add only the API member, relevant source path, observed behavior, version caveat, and practical consequence. Do not append task diaries or test-run transcripts.
