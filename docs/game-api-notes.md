# RimWorld 1.7.4998b — API Research Notes

## Item Editor

### Map selection and opening main tabs

- `RimWorld.Selector.SingleSelectedThing` returns a `Verse.Thing` only when
  exactly one selected object is a Thing; otherwise it returns null.
  `RimWorld.Selector.NumSelected` also counts non-Thing selections.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/Selector.cs`.
- `RimWorld.MainButtonWorker_ToggleTab.Activate()` calls
  `RimWorld.MainTabsRoot.ToggleTab(MainButtonDef, bool)`, which removes the
  previous tab and adds the new tab without clearing map selection.
  Sources: `refs/decompiled/Assembly-CSharp/RimWorld/MainButtonWorker_ToggleTab.cs`
  and `refs/decompiled/Assembly-CSharp/RimWorld/MainTabsRoot.cs`.
- `RimWorld.MainTabWindow` uses `Verse.WindowLayer.GameUI`.
  `Verse.Window.PreOpen()` does not clear selection; even its Dialog-specific
  selector notification only ends drag/goto interactions. Closing the ordinary
  inspection tab has no selection-clearing override.
  Sources: `refs/decompiled/Assembly-CSharp/RimWorld/MainTabWindow.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/Window.cs`, and
  `refs/decompiled/Assembly-CSharp/RimWorld/MainTabWindow_Inspect.cs`.
- `RimWorld.Selector.SelectorOnGUI()` automatically opens Inspect only when
  something is selected and no main tab is open. An open Utilities tab is not
  replaced simply because an item remains selected.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/Selector.cs`.

For 1.7.4998b, Utilities can read `Verse.Find.Selector.SingleSelectedThing`
directly after opening. No cached fallback selection, custom button worker, or
Harmony patch is needed. Revalidate the live selection on Apply; never fall
back to an old item after selection is cleared or changed. These conclusions
are from local source/assembly inspection, not an in-game test.

### Stack count and validity

- `Verse.Thing.stackCount` is a public integer field, not a property with
  automatic notifications. `Verse.ThingDef.stackLimit` is the definition's
  shared integer limit (default 1); changing it would affect every item of that
  definition. Stack-count Apply changes only the selected Thing's count; the
  separate persistent-limit action intentionally raises a shared definition.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs` and
  `refs/decompiled/Assembly-CSharp/Verse/ThingDef.cs`.
- `Verse.Thing.Destroyed`, `Verse.Thing.Spawned`, and `Verse.Thing.Map` identify
  whether the object is still spawned on the current map. A held/despawned or
  destroyed item must not remain an editable target.
  Source: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`.
- Ordinary selection requires one spawned `Verse.ThingCategory.Item` with a
  positive current count. Stack editing separately requires
  `Verse.ThingDef.stackLimit > 1`; HP/quality can support non-stackable equipment.
  Reject pawns, buildings,
  corpses, and minified things explicitly. Corpses and wrapped/minified objects
  are outside the initial ordinary-item feature even if another mod changes
  their limits. Desired counts must be integers in `1..item.def.stackLimit`.

### Notifications after a count change

- `Verse.Thing.TryAbsorbStack(Thing, bool)` and `Verse.Thing.SplitOff(int)`
  directly change counts, call `Verse.Thing.DirtyMapMesh(Map)`, and notify
  `RimWorld.ListerMergeables.Notify_ThingStackChanged(Thing)` for spawned stacks.
  Split/absorb also affect other objects, so they are not setters for this UI.
  Source: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`.
- `Verse.Thing.DirtyMapMesh(Map)` marks occupied cells' Things mesh dirty,
  skipping `Verse.DrawerType.RealtimeOnly`. This refreshes stack-dependent
  static graphics without despawning or replacing the item.
  Source: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`.
- `RimWorld.ListerMergeables.Notify_ThingStackChanged(Thing)` reevaluates
  mergeability, including whether a stack has reached its definition limit.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/ListerMergeables.cs`.
- `RimWorld.ResourceCounter.CheckUpdateResource(Thing)` conditionally calls
  `RimWorld.ResourceCounter.UpdateResourceCounts()` for countable resources.
  The editor invokes it once per actual change for prompt resource display
  refresh, including while paused; it is not a per-frame UI call.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/ResourceCounter.cs`.

These public members were also checked against the local reference assemblies.
There is no general-purpose stack-count setter in the inspected Thing API.
Other mods' custom stack rules/callbacks are not covered by this initial feature.

### Input fields and window sizing

- `Verse.Widgets.TextField(Rect, string)` returns the current text without
  changing game state. The editor keeps this raw buffer and parses with
  `int.TryParse` and invariant culture, allowing invalid/intermediate input to
  stay visible with a validation message. It resets the buffer on target
  changes and normalizes it after a successful Apply.
- `Verse.Widgets.TextFieldNumeric<T>(Rect, ref T, ref string, float, float)`
  parses into its referenced value during editing and filters input. Its bounds
  are floats. Using a separate raw buffer avoids coupling edits to mutations
  and keeps integer validation explicit.
  Source for both: `refs/decompiled/Assembly-CSharp/Verse/Widgets.cs`.
- `RimWorld.MainTabWindow.InitialSize` clamps requested width to the screen
  width and height to screen height minus the 35-unit main toolbar. The expanded
  Utilities content uses a scroll view when its sections cannot fit vertically.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/MainTabWindow.cs`.

The compact Item Editor uses `Verse.TexButton.Info` with
`Verse.Widgets.DrawTextureFitted(Rect, Texture, float, float)` and
`Verse.TooltipHandler.TipRegion(Rect, TipSignal)`. This displays native information
artwork with our warning tooltip, without opening an unrelated information card.
`Verse.Widgets.CheckboxLabeled(Rect, string, ref bool, bool, ...)` supports the
compact per-type "Large stacks" control. Checking enables the saved override;
unchecking requests a native destructive confirmation. While reset is pending,
it is unchecked with a "Restart required" badge, even though the displayed active
limit stays high. Re-checking cancels the pending reset. State is read from the
service on every draw, so cancelled dialogs and failed writes do not leave a false
checkbox state. Sources: `refs/decompiled/Assembly-CSharp/Verse/TexButton.cs`,
`refs/decompiled/Assembly-CSharp/Verse/Widgets.cs`, and
`refs/decompiled/Assembly-CSharp/Verse/TooltipHandler.cs`.

`Verse.Text.Font` falls back from Tiny to Small for languages without tiny-font
support. Inline labels are measured after setting the font. In this version,
`Verse.Text.ClampTextWithEllipsis(Rect, string)` reserves 13 extra units inside
the rectangle; precisely measured inline limit values use `Widgets.Label`
directly to avoid unnecessary truncation. Sources:
`refs/decompiled/Assembly-CSharp/Verse/Text.cs` and
`refs/decompiled/Assembly-CSharp/Verse/Widgets.cs`.

Panel drawing now restores `Text.Font`, `Text.Anchor`, `Text.WordWrap`,
`GUI.color`, and `GUI.enabled` in `finally`. Styling is scoped to each panel;
shared GUIStyle objects are not modified. Success feedback uses
`UnityEngine.Time.realtimeSinceStartup` to expire after three seconds even while
paused (also used by the native widget/tooltip implementations). Validation and
persistence errors remain available as text/tooltips. This is presentation only;
custom stack-limit persistence is independent of the styling.

### Hit Points and Quality

`ItemEditorService.GetSelectedItem()` now accepts ordinary non-stackable items;
`CanEditStackCount()` separately gates the original stack controls and mutation
entry point. `GetSelectedItem(expectedItem, out error)` rechecks live selection,
reference identity, destruction/spawn state, map, and ordinary-item eligibility.
`ItemPropertiesService` uses that resolver for every HP/quality mutation, including
callbacks from an already-open quality menu. Equipped/inventory items are still
despawned targets and are not editable; drop equipment onto the map first.

#### HP property and maximum

- `Verse.Thing.HitPoints` is a public virtual integer property whose base setter
  simply writes `hitPointsInt`; it does not validate or clamp the value.
- `Verse.Thing.MaxHitPoints` rounds
  `GetStatValue(StatDefOf.MaxHitPoints, applyPostProcess: true, cacheStaleAfterTicks: 10)`.
  Use this actual stat result, not a definition's base stat or a hardcoded limit.
- `Verse.ThingDef.useHitPoints` is the authoritative HP support flag. The editor
  accepts only whole numbers in `1..MaxHitPoints`; maxima below one are invalid.
  Apply and Max clear the target's MaxHitPoints cache before reading the maximum,
  so the action does not trust a stale displayed/cached maximum while paused.
  Max applies immediately; HP text edits require their own Apply.
- HP changes refresh the spawned Thing's mesh through `Thing.DirtyMapMesh(Map)`;
  no damage event, destruction, or zero-HP intermediate state is used.

Sources: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`,
`refs/decompiled/Assembly-CSharp/Verse/ThingDef.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/StatExtension.cs`.

#### Quality setter, callbacks, and HP consistency

- `Verse.ThingCompUtility.TryGetComp<RimWorld.CompQuality>(Thing)` supports
  quality-bearing `ThingWithComps`. Missing quality comps are not editable.
  Source: `refs/decompiled/Assembly-CSharp/Verse/ThingCompUtility.cs`.
- `RimWorld.CompQuality.SetQuality(QualityCategory, ArtGenerationContext?)` writes
  the quality, optionally initializes art only if the nullable source has a value,
  then calls `parent.PostQualitySet()` and the facility-quality callback. The
  editor passes `source: null` and does not call either native callback again.
  Requests for the already-active category return without calling SetQuality.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/CompQuality.cs`.
- `Verse.Book.PostQualitySet()` calls `GenerateBook()`. All Book instances and
  subclasses are excluded from quality editing to avoid regenerating content.
  Books can still expose HP if their definition supports it.
  Source: `refs/decompiled/Assembly-CSharp/Verse/Book.cs`.
- `RimWorld.StatWorker.ClearCacheForThing(Thing)` removes only that Thing from
  both the temporary and immutable stat caches. After SetQuality the editor calls
  `RimWorld.StatDefOf.MaxHitPoints.Worker.ClearCacheForThing(item)` and recomputes
  the maximum. It preserves the previous absolute HP, clamping to `1..new maximum`
  when required; it does not preserve percentage damage or heal on an upgrade.
  Sources: `refs/decompiled/Assembly-CSharp/RimWorld/StatWorker.cs` and
  `refs/decompiled/Assembly-CSharp/RimWorld/StatDefOf.cs`.
- Native callbacks can have mod-specific side effects. The service revalidates
  selection after those callbacks before writing HP. If a callback invalidates
  the target, or the new maximum is invalid, it reports failure without a further
  HP edit; it does not attempt a quality rollback that would invoke callbacks again.

#### Native dropdown and input state

`Verse.FloatMenu(List<FloatMenuOption>)` is the native quality dropdown. Each
choice captures the original target Thing, rather than the panel's mutable
selection field. All seven categories come from `RimWorld.QualityCategory` and
use localized `RimWorld.QualityUtility.GetLabel(QualityCategory)` labels. The
active category has a disabled/null action; the service also enforces idempotence.
The displayed quality is read from the current item's comp each draw.

HP and stack text buffers are independent. Selection changes reset both. HP Apply
and Max normalize only the HP buffer. A quality change refreshes an unedited HP
buffer after any clamp; a pending HP draft remains intact and is validated against
the new maximum. Live current/maximum HP is always shown separately. Existing
panel state restoration covers the new controls; the other sections are unchanged.

Sources: `refs/decompiled/Assembly-CSharp/Verse/FloatMenu.cs`,
`refs/decompiled/Assembly-CSharp/Verse/FloatMenuOption.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/QualityCategory.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/QualityUtility.cs`.

Focused tests cover HP bounds (including maximum one), ordinary non-stackable
selection eligibility, separate stack eligibility, absent quality comps, and
book/minified exclusions. Native fixtures exercise CompQuality callbacks with
source:null, same-quality no-ops, per-Thing stat-cache invalidation, HP preservation
and clamping, fresh Max calculations, and changed-selection rejection. These are
isolated test objects with a test StatWorker, not an actual game/map run.

Manual checks: use a damaged ground weapon and armor; test valid/invalid HP, Max,
all seven localized quality choices, upgrade/downgrade HP consistency while paused,
pending HP text through a quality change, target changes/despawning while a menu
is open, and book HP without an editable quality menu. Recheck Stack/Large stacks
on a stackable item, both placeholder sections, and save/reload of edited values.

### Persistent large stacks: source research

Persistent definition-level limits support eligible ordinary stackable item types.
Session-only oversized counts were rejected because the excess can be lost.
The findings below are specific to the local 1.7.4998b sources; source inspection
and isolated settings tests are not an in-game save/load test.

#### Serialization and the load-time truncation

- `Verse.Thing.ExposeData()` saves `stackCount` using
  `Verse.Scribe_Values.Look<int>(..., forceSave: true)` for stackable definitions.
  During `LoadingVars`, it resets a spawned thing's map state to unspawned.
  `Verse.Scribe_Values.Look<T>()` / `Verse.ScribeExtractor.ValueFromNode<T>()`
  read the saved integer without checking the definition's stack limit.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/Scribe_Values.cs`, and
  `refs/decompiled/Assembly-CSharp/Verse/ScribeExtractor.cs`.
- `Verse.Map.FinalizeLoading()` respawns loaded ordinary things through
  `Verse.GenSpawn.Spawn(..., respawningAfterLoad: true)`, which calls
  `Verse.Thing.SpawnSetup(Map, bool)`. The latter logs an error and assigns
  `stackCount = def.stackLimit` whenever the count exceeds the definition limit.
  The truncation is **not** conditional on `respawningAfterLoad`. Thus a saved
  map-spawned stack of 50,000 units loaded with an effective limit of 75 loses
  49,925 units on the normal map load path instead of being split. Held stacks
  have a different loading path, described in the reset notes below.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Map.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/GenSpawn.cs`, and
  `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`.
- `Verse.BackCompatibility.CheckSpawnBackCompatibleThingAfterLoading()` has an
  oversized-stack special case for saves from version 0.18 or earlier. It does
  not protect current 1.7 saves.
  Source: `refs/decompiled/Assembly-CSharp/Verse/BackCompatibility.cs`.
- `Verse.Map.ExposeData()` saves non-compressed things in full. The separate
  `Verse.MapFileCompressor` path stores definition short hashes, not quantities.
  `Verse.CompressibilityDeciderUtility.IsSaveCompressible(Thing)` has no count
  check. A general large-stack feature should exclude `saveCompressible` item
  definitions unless their serialization is separately established as safe.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Map.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/MapFileCompressor.cs`, and
  `refs/decompiled/Assembly-CSharp/Verse/CompressibilityDeciderUtility.cs`.

#### Splitting, merging, and placement

- `Verse.Thing.SplitOff(int)` subtracts the requested quantity into a new Thing,
  or despawns/returns the whole Thing when taking the entire stack. It does not
  itself cap a partial split to `def.stackLimit`. `Verse.ThingWithComps.SplitOff`
  also invokes component `PostSplitOff` callbacks. Any resulting thing later
  spawned still encounters `Thing.SpawnSetup`'s limit.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs` and
  `refs/decompiled/Assembly-CSharp/Verse/ThingWithComps.cs`.
- `Verse.ThingUtility.TryAbsorbStackNumToTake(Thing, Thing, bool)` computes
  `min(source count, destination limit - destination count)` when respecting
  limits, without clamping negative free space to zero. `Verse.Thing.TryAbsorbStack`
  applies that result to both counts; `Verse.ThingWithComps.TryAbsorbStack` passes
  it to component callbacks first. An oversized destination can therefore cause
  a negative transfer, not just a harmless failed merge.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/ThingUtility.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`, and
  `refs/decompiled/Assembly-CSharp/Verse/ThingWithComps.cs`.
- `Verse.GenPlace.TryPlaceDirect()` attempts merges into compatible things in
  the destination cell. `Verse.GenPlace.SplitAndSpawnOneStackOnCell()` splits
  quantities above `def.stackLimit` into legal chunks before spawning;
  `Verse.GenPlace.TryPlaceThing()` in Near/Radius mode continues while placement
  makes progress, but can fail when space is exhausted. A future migration must
  retain unplaced remainders and verify total quantity; a placement attempt is
  not an atomic guarantee that everything was placed.
  Source: `refs/decompiled/Assembly-CSharp/Verse/GenPlace.cs`.
- `RimWorld.ListerMergeables.ShouldBeMergeable()` excludes counts equal to the
  definition limit, rather than counts greater than or equal to it. This is
  another assumption that stacks do not exceed their effective definition limit.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/ListerMergeables.cs`.

#### Hauling, storage, and inventories

- `Verse.Pawn_CarryTracker.MaxStackSpaceEver(ThingDef)` returns the smaller of
  `def.stackLimit` and `round(CarryingCapacity / VolumePerUnit)`.
  `Verse.Pawn_CarryTracker.TryStartCarry(Thing, int, bool)` clamps the amount and
  uses `Thing.SplitOff`. `Verse.AI.Toils_Haul.StartCarryThing()` also clamps to
  job count, available carry space, and source count. Raising the definition
  limit does not grant unlimited carrying capacity or alter `VolumePerUnit`.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Pawn_CarryTracker.cs` and
  `refs/decompiled/Assembly-CSharp/Verse/AI/Toils_Haul.cs`.
- `Verse.AI.HaulAIUtility.HaulToCellStorageJob()` considers storage-cell free
  space and definition limits. An empty target uses a 99,999 initial job count;
  that is a job batching limit, not a serialization or stack-size limit.
  `RimWorld.WorkGiver_Merge.JobOnThing()` considers target free space based on
  `heldThing.def.stackLimit` when creating merge jobs.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/AI/HaulAIUtility.cs` and
  `refs/decompiled/Assembly-CSharp/RimWorld/WorkGiver_Merge.cs`.
- `RimWorld.StoreUtility.NoStorageBlockersIn()` treats a compatible stack below
  its definition limit as available space. Higher definition limits therefore
  increase resource density in stockpiles/shelves without adding storage slots.
  Source: `refs/decompiled/Assembly-CSharp/RimWorld/StoreUtility.cs`.
- `Verse.ThingOwner<T>.TryAdd()` and `Verse.ThingOwner.GetCountCanAccept()` use
  the definition limit for merge room and finite container stack capacity.
  `RimWorld.MassUtility.InventoryMass(Pawn)` still sums per-unit mass times
  actual count. A larger limit does not make caravan inventory mass disappear.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/ThingOwner.cs` and
  `refs/decompiled/Assembly-CSharp/RimWorld/MassUtility.cs`.
- Large integers are not a universal safety guarantee: the native HP-weighted
  merge calculation multiplies two integers before conversion to float, and
  finite container capacity multiplies stack slots by `def.stackLimit` using
  integers. Extreme counts, high modded HP, or unusually large containers can
  overflow those intermediates. The current default is a conservative 100,000
  for eligible ordinary stackable items, not a blanket one-million limit for all
  ThingDefs. Custom item/storage logic can still require compatibility testing.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs` and
  `refs/decompiled/Assembly-CSharp/Verse/ThingOwner.cs`.

#### Approach comparison and recommendation

1. **Fixed definition override** (for example Steel's effective limit 100,000):
   the smallest persistent model. Native spawning, placement, splitting, merging,
   storage, and containers use the same limit. It affects every instance of that
   definition, including future/generated items, rather than one edited stack.
   An XML `Verse.PatchOperationReplace` can supply a fixed value; its ordering
   relative to other mods' XML patches matters.
   Source: `refs/decompiled/Assembly-CSharp/Verse/PatchOperationReplace.cs`.
2. **Configurable per-definition overrides**: the same native model, with a
   settings layer and a captured baseline for display. This is the recommended
   direction, now generalized: opt in individual eligible item definitions at
   100,000, keeping counts such as 50,000 below the effective limit. Capture the
   resolved baseline before our override, including
   earlier mods' values; apply raises before save loading. Avoid silently lowering
   a higher limit supplied by another mod. Startup restoration occurs before maps
   spawn; explicit runtime enabling refreshes every affected stack's merge lister.
   `Verse.Mod.GetSettings<T>()` and `Verse.ModSettings.ExposeData()` provide
   settings persistence. `Verse.PlayDataLoader.DoPlayLoad()` resolves definitions
   before scheduling `Verse.StaticConstructorOnStartupUtility.CallAll()`; a
   `[StaticConstructorOnStartup]` bootstrap is an available integration point.
   Sources: `refs/decompiled/Assembly-CSharp/Verse/Mod.cs`,
   `refs/decompiled/Assembly-CSharp/Verse/ModSettings.cs`,
   `refs/decompiled/Assembly-CSharp/Verse/PlayDataLoader.cs`, and
   `refs/decompiled/Assembly-CSharp/Verse/StaticConstructorOnStartupUtility.cs`.
   Other mods' startup mutations may still conflict; their ordering and final
   effective values need compatibility testing.
3. **Individual-stack Harmony exceptions**: significantly more complex. Preventing
   the spawn clamp alone does not fix negative merges or the many definition-level
   reads in placement, hauling, storage, and containers. Per-stack state would
   also need persistence and propagation through splitting. Not recommended for
   the initial feature; no such patches have been added.
4. **Vanilla-size stacks / resource reserve**: splitting the quantity into legal
   physical stacks preserves vanilla mechanics, but needs space and can create
   hundreds/thousands of Things. A serialized virtual reserve avoids physical
   stack overflow but is not directly available to vanilla hauling/recipes/trade
   without further integration. Neither gives one ordinary 50,000-item stack.

A save-only restore is not a substitute for the recommended model:
`Verse.MapComponent.ExposeData()` could retain a backup quantity and
`Verse.MapComponent.FinalizeInit()` could restore it after loading. However,
`Verse.Map.FinalizeInit()` invokes component finalization after the spawn clamp
and initial resource/wealth recounts, and it does not cover subsequent respawns
or invalid merges. It would need additional lifecycle safeguards and bookkeeping.
Sources: `refs/decompiled/Assembly-CSharp/Verse/MapComponent.cs` and
`refs/decompiled/Assembly-CSharp/Verse/Map.cs`.

Editor Apply enforces `1 <= stackCount <= effective ThingDef.stackLimit`.
The original limit is retained separately for UI/reference; it is not temporarily
swapped during spawn or GUI calls. The editor has persistent enabling and an
explicitly destructive reset on the next full process restart, described below.
Reset does not guarantee that existing stacks meet the lower limit. A future
one-million editor ceiling would require the definition's effective limit to be
at least that large, plus item-specific validation/testing.

Existing saves with ordinary stacks retain their counts under raised limits;
native serialization needs no custom quantity format. The override must be
reapplied before every load. Disabling/removing the mod, lowering a setting, or
another mod lowering the limit can reintroduce truncation on the next spawn/load.
To reduce limits or uninstall without losing quantities, a future migration
must normalize all affected map and held/world/inventory stacks, preserve all
remainders, and save the converted game. Older unconverted saves remain unsafe
to load without the corresponding effective limits. A restart alone is not a
safe downgrade, and post-load restoration cannot protect a save loaded after
the mod is removed.

Still required before implementation can be called reliable: in-game round trips
with 50,000 Steel, hauling/merging/splitting, shelves and containers, saving during
carry/inventory transfers, resource-total checks, and migration/conflict tests.

Research verification: the unchanged Release build passed with no warnings or
errors. An isolated console harness passed 26 existing-service numeric validation
cases and six calls to the original assembly's
`Verse.ThingUtility.TryAbsorbStackNumToTake()`. The latter confirmed a transfer
of -49,925 for a destination of 50,000 with limit 75 and source 10, versus a
transfer of 10 with effective limit 100,000. The fixtures did not use the global
definition database or launch the game. No actual save/load cycle was run.

### Generalized initialization, settings, and Steel compatibility

1. `Verse.LoadedModManager.LoadAllActiveMods()` calls `CreateModClasses()` before
   loading mod XML. `Verse.Mod.GetSettings<T>()` synchronously calls
   `LoadedModManager.ReadModSettings<T>()` and returns a fully loaded settings
   object. Our mod constructor captures that settings object and creates the
   custom-limit service; it does not access or change ThingDefs yet.
   Sources: `refs/decompiled/Assembly-CSharp/Verse/LoadedModManager.cs` and
   `refs/decompiled/Assembly-CSharp/Verse/Mod.cs`.
2. `Verse.PlayDataLoader.DoPlayLoad()` resolves ThingDefs before its queued
   `Verse.StaticConstructorOnStartupUtility.CallAll()` callback runs.
   Our `CustomStackLimitStartup` appends an initialization callback through
   `Verse.LongEventHandler.ExecuteWhenFinished()` from its startup constructor.
   The callback snapshots resolved ThingDefs and their original limits before
   applying any override. Saved original metadata takes precedence for display.
   It first consumes saved reset markers without changing those definitions,
   then restores only other enabled item keys using `max(current limit, saved limit)`; malformed
   nonpositive saved limits fall back to the initial 100,000 default. Unenabled
   definitions are left untouched.
   Sources: `refs/decompiled/Assembly-CSharp/Verse/PlayDataLoader.cs` and
   `refs/decompiled/Assembly-CSharp/Verse/StaticConstructorOnStartupUtility.cs`.
3. `Verse.LongEventHandler.ExecuteToExecuteWhenFinished()` uses a loop over the
   live callback-list Count, so appended callbacks run in the same completion
   pass before the next long event. `Verse.Root.Start()` queues play-data loading
   before interface initialization; `Verse.Root_Play.Start()` queues saved-game
   loading afterward. This also covers the autostart-save path in the inspected
   game version: saved limits are applied before map Things spawn.
   Sources: `refs/decompiled/Assembly-CSharp/Verse/LongEventHandler.cs`,
   `refs/decompiled/Assembly-CSharp/Verse/Root.cs`, and
   `refs/decompiled/Assembly-CSharp/Verse/Root_Play.cs`.

`CustomStackLimitSettings : Verse.ModSettings` persists dictionaries keyed by
`ThingDef.defName`: enabled effective limits and original-limit metadata, plus a
set of pending reset keys.
`CustomStackLimitService.DefaultCustomLimit` is currently 100,000 for new opt-ins;
saved per-key limits are retained, leaving the internal model open to future
configuration. Original values are captured from resolved definitions, never
hardcoded. Original metadata is first written on enabling a key and retained
after reset, avoiding unrelated configuration changes. Unresolved definitions'
records are retained unless explicitly reset.

Eligibility requires an ordinary `ThingCategory.Item`, an original/current limit
greater than one, a valid Thing class, and non-compressed saving. Pawns, buildings,
corpses, minified Things, and their subclasses are excluded by class as well as
category. `Verse.ThingDef.thingClass`, `category`, `stackLimit`, and
`saveCompressible` are the relevant fields.
Source: `refs/decompiled/Assembly-CSharp/Verse/ThingDef.cs`.

The Steel-only release already used the XML dictionary tags `enabledStackLimits`
and `originalStackLimits`. Those tags, the settings class, and its filename are
unchanged. Migration is format-preserving: the same Steel entry is restored,
including values above 100,000 and its saved original metadata, without a rewrite
when nothing needs upgrading. Enabling additional types adds keys without
replacing Steel. Existing ordinary item records keep their raised floor even if
another mod later marks them save-compressible; new edits are blocked in that
case. Invalid/non-item records are not applied to unrelated definitions.

The settings path matches the private
`Verse.LoadedModManager.GetSettingsFilename(string, string)` implementation:
`GenFilePaths.ConfigFolderPath` plus the sanitized
`Mod_{Content.FolderName}_{ModClassName}.xml`. This is the game's active profile
inside its sandbox at runtime, not a host-side project file. Keep the mod's folder
and class name stable; changing them changes the native settings lookup path.
Source: `refs/decompiled/Assembly-CSharp/Verse/LoadedModManager.cs`.

The ordinary native writer uses `FileMode.Create`, and
`Verse.Scribe_Deep.Look<T>()` catches some serialization exceptions. Our settings
store instead writes the same `SettingsBlock/ModSettings` format to a temporary
file in the target directory, calls `ExposeData()` directly, finalizes it, reads
it back through Scribe, compares both dictionaries and the reset set, flushes it to disk, and then
replaces the target. The previous settings remain intact if serialization or
verification fails. Settings writes are rejected while Scribe is already active;
they never force-stop an unrelated game save/load.
Sources: `refs/decompiled/Assembly-CSharp/Verse/ScribeSaver.cs`,
`refs/decompiled/Assembly-CSharp/Verse/Scribe_Deep.cs`, and
`refs/decompiled/Assembly-CSharp/Verse/Scribe_Collections.cs`.

Enabling the checkbox persists successfully before changing that type's definition and
never changes quantities. Apply still changes quantity only after selection and
integer validation. Startup records higher observed limits for already-enabled
item types, so they survive later restarts even without an editor Apply. Apply also
persists any higher current limit supplied by another mod, then restores any
previously saved floor if needed. Startup upgrade write failures retain the existing
limit and produce one error; failed limit persistence blocks quantity edits until a
retry succeeds. There are no per-frame writes. Runtime raises
notify `ListerMergeables.Notify_ThingStackChanged()` for that type's stacks across all
loaded maps, since previously full stacks may now have merge room. Other item
definitions and quantities are untouched. Persistence errors are tracked per key:
a successful write for another type does not hide the failed type's error.
The native checkbox also offers a confirmed restart-only reset and cancellation,
as described below; neither action changes the active definition or any quantities.

New opt-ins exclude save-compressible definitions. Other mods can
still introduce custom storage behavior, overwrite limits after our startup
callback, or hot-reload definitions; those interactions are not guaranteed by
this feature. The service preserves higher limits it observes and persists them
before our edits, but it cannot prevent an external mod from lowering a limit
later. Removing the mod/settings or loading an unmigrated save without the saved
effective limits remains unsafe. No scanner, manual trimming, or lossless migration
is implemented. The separate, unimplemented coverage research and scan/trim design
are in [reset-and-trim-design.md](reset-and-trim-design.md).

### Destructive Reset Stack Limit on full process restart

`CustomStackLimitSettings.PendingResets` uses the additional optional XML tag
`pendingStackLimitResets`, serialized by
`Verse.Scribe_Collections.Look<string>(ref HashSet<string>, string, LookMode.Value)`.
Missing tags load as an empty set, preserving old Steel and multi-type settings.
Copies, equality checks, and atomic store verification include the set.
Source: `refs/decompiled/Assembly-CSharp/Verse/Scribe_Collections.cs`.

`CustomStackLimitService.TryScheduleReset()` persists the chosen key's marker
before reporting success. It retains that key's enabled floor and original
metadata for the rest of the process. Ordinary Apply and persistence of higher
observed limits preserve the marker. `TryCancelReset()` durably removes only the
marker; neither action lowers the current limit, changes a count, or refreshes a
map. Repeated initialization and in-process save loading do not consume it.
`IsEnabled()` describes the active override; `IsResetPending()` describes the
scheduled reset. The UI shows the desired next-start state while pending.

The existing startup hook consumes markers after definition resolution, before
save loading. For each reset type, it skips our override and atomically removes
that enabled entry and marker, retaining original-limit metadata. The actual
definition stays at its freshly resolved native/other-mod limit; we never assign
historical original metadata over it. Consequently a higher other-mod limit is
respected, and changed mod configurations may yield an effective limit different
from the historical "Original" display. After startup the checkbox is unchecked
and the restart badge is gone. Explicit enabling can opt the type in again.

If startup cleanup cannot be persisted, the saved marker still suppresses that
override in memory and on subsequent process starts. It is not shown as a new
pending reset after it has taken effect. An error is reported once by the startup
hook and exposed in the editor; a future explicit enable must durably clear the
marker first. Failed schedule/cancel writes leave the previous state intact.
No new Harmony patch or settings filename/schema rename is required.

`Verse.Dialog_MessageBox.CreateConfirmation(TaggedString, Action, bool destructive,
string title, WindowLayer)` supplies native Confirm/Go back behavior and destructive
button styling. Only Confirm schedules a reset. The callback captures the
confirmed ThingDef, so later selection changes cannot reset another type.
Source: `refs/decompiled/Assembly-CSharp/Verse/Dialog_MessageBox.cs`.

For compact confirmation text, the editor shortens the native dialog to 260
units high after adding it, preserving its default width and centering.
`Verse.WindowStack.Add()` calls `Window.PreOpen()` synchronously, which initializes
the public `Window.windowRect`; changing that rectangle afterward preserves the
factory's buttons and callbacks. The native message body scrolls for long names.
`Verse.Def.LabelCap` supplies the selected type's display name.
Sources: `refs/decompiled/Assembly-CSharp/Verse/WindowStack.cs`,
`refs/decompiled/Assembly-CSharp/Verse/Window.cs`, and
`refs/decompiled/Assembly-CSharp/Verse/Def.cs`.

#### Held stacks do not all truncate on reload

- `Verse.ThingOwner<T>.ExposeData()` loads `innerList` through Scribe collections,
  reconnects `holdingOwner`, and removes null/invalid minified entries in
  `PostLoadInit`. It does **not** call `TryAdd()`, `SpawnSetup()`, or cap counts.
  `Verse.ThingOwner.ExposeData()` only serializes container options. Nested deep
  holders can therefore retain oversized item counts after a reset and reload.
  Source: `refs/decompiled/Assembly-CSharp/Verse/ThingOwner.cs`.
- `Verse.Pawn_InventoryTracker.ExposeData()` and
  `Verse.Pawn_CarryTracker.ExposeData()` deep-save their `innerContainer`.
  `RimWorld.ActiveTransporterInfo.ExposeData()` and
  `RimWorld.CompTransporter.PostExposeData()` do likewise for ordinary held items;
  their additional world-pawn reference handling does not normalize those counts.
  This also matters for pawn inventories nested in caravans or transporters.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/Pawn_InventoryTracker.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/Pawn_CarryTracker.cs`,
  `refs/decompiled/Assembly-CSharp/RimWorld/ActiveTransporterInfo.cs`, and
  `refs/decompiled/Assembly-CSharp/RimWorld/CompTransporter.cs`.
- `Verse.ThingOwner.TryDrop()` delegates to `Verse.GenDrop.TryDropSpawn()`;
  placement may split into legal chunks, fail for lack of room, or later encounter
  the direct spawn clamp. The previously verified `Thing.SpawnSetup()` clamp
  discards excess for a directly spawned oversized Thing, including normal map
  reloads. Held Things do not necessarily take that path during loading.
  Sources: `refs/decompiled/Assembly-CSharp/Verse/ThingOwner.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/GenDrop.cs`,
  `refs/decompiled/Assembly-CSharp/Verse/Thing.cs`, and
  `refs/decompiled/Assembly-CSharp/Verse/GenPlace.cs`.
- Remaining oversized stacks violate assumptions in merge/haul/container code:
  negative merge transfers are possible as documented above. Reset is an
  explicitly destructive removal of our override, not guaranteed automatic trim
  or a safe migration. Unusual modded storage may behave differently.

Settings affect the item type across **all saves in this game profile**. No map,
inventory, world object, nested holder, or other save file on disk is scanned or
modified by reset. Data loss can occur when any such save is loaded or during
later gameplay. Removing the mod/settings carries the same class of risks.
Definition hot reload is not a supported substitute for a full process restart.

#### Focused verification and full-restart manual check

`tests/RimWorldUtilities.Tests` is a package-free console test project. The core
suite covers numeric bounds, opt-in defaults, resolved baseline capture, failed
writes, multiple definition keys, unsupported types, monotonic limits, per-type
errors, Steel settings migration, other mods' higher limits, scheduling/cancelling
resets, retained markers after failed writes, and restart-only activation. With
`RIMWORLD_GAME_DIR` pointing to the installed game DLL directory, it additionally
uses native Scribe serialization and loads the saved settings in a fresh test
process. Runtime DLLs are read-only; fixture settings live in a temporary folder.
See `tests/README.md` for the commands and the distinction from launching RimWorld.

The full suite, including HP/quality checks, passed 284 assertions plus 28
assertions across five fresh processes. Fresh-process checks cover cancelled
resets, reset completion, and another restart after completion.
It does not exercise a game map or constitute a full RimWorld restart test.

Manual verification after the user deploys the build:

1. With fresh mod settings, select Steel, Wood, and Medicine in turn. Check their
   original/effective limits match the resolved definitions and each starts
   unchecked. Ineligible/non-stackable selections must not offer new opt-ins.
2. Enable "Large stacks" for two types. Check each effective limit becomes 100,000 (or
   retains a higher existing limit), the original value remains visible, and the
   quantity/input buffer do not change. A third unenabled type must remain unchanged.
3. Apply 50,000, save the game, fully quit RimWorld, then start a new RimWorld
   process and load that save. Check the quantity before opening Utilities;
   then verify both types' checked status and displayed limits. Repeat save/load.
4. Haul, merge, split, and store Steel on shelves/in containers; verify total
   quantity is preserved. Include saving/restarting while a pawn carries Steel
   or it is held in an inventory. Carrying capacity must still constrain hauling.
5. Reject zero, negatives, text, and values above the effective limit. Confirm
   unrelated quantities, HP/quality controls, and the other two panels remain unchanged.
6. In a disposable test save with two enabled types, uncheck one type. Go back
   must leave it checked. Confirm must show "Restart required", an unchecked box,
   and the same high effective limit and quantity. Save/reload without quitting:
   reset must still be pending and the high limit must remain active.
7. Re-check before quitting: the badge must disappear with the high limit intact.
   Fully quit/start and verify the raised limit persists. Then schedule again,
   fully quit/start, and verify the selected type is unchecked at its resolved
   native/other-mod limit while the other type remains enabled. Restart again to
   verify it stays off. Original metadata must remain visible.
8. Only on disposable saves, include oversized map stacks, carried/inventory
   items, and nested containers/transporters. Record counts before/after restart
   and later unloading/hauling/merging. Map stacks may lose excess immediately;
   held stacks may remain oversized. Do not expect normalization or preservation
   of quantities after confirmed reset. Check any higher limit from another mod
   is retained. Other save files are affected by the settings but never scanned.

Rot Progress editing is not implemented.

## Crafting

### Always Legendary: crafted items and constructed furniture

`CraftingService` owns eligibility, the enabled setting, persistence errors and
patch availability. `CraftingPanel` presents one native "Always Legendary"
checkbox, off by default, with a short tooltip. A successful settings write takes
effect immediately; a failed write retains the prior setting. An unavailable
quality path shows a compact status with diagnostic tooltip; the control remains
available while either crafting or construction is supported.
The existing `UtilitiesPanel` restores GUI/text state after drawing.

The single existing `CustomStackLimitSettings : Verse.ModSettings` now also
serializes `AlwaysLegendaryCrafting` under `alwaysLegendaryCrafting`, default
false. Its copy/equality methods include the flag, so atomic persistence verifies
it alongside stack limits and pending resets. Both services copy the current
shared settings before saving, preserving each other's changes. Existing class,
filename and stack XML tags remain unchanged; legacy Steel settings default the
new feature off. A second `ModSettings` type is deliberately not introduced:
`Verse.Mod.GetSettings<T>()` rejects a second settings class for the same Mod.
Source: `refs/decompiled/Assembly-CSharp/Verse/Mod.cs`.

### Native call and exact patch target

`RimWorld.QualityUtility.GenerateQualityCreatedByPawn(Pawn pawn, SkillDef
relevantSkill, bool consumeInspiration = true)` performs native RNG, uses a
mechanoid's fixed skill or the pawn's skill, applies ideology quality effects,
and consumes creativity inspiration when applicable. The integer overload has
no worker context. The pawn overload is also called by
`RimWorld.Frame.CompleteConstruction(Pawn worker)` and
`RimWorld.JobDriver_BuildCubeSculpture.PlaceAndFinish()`, so it must not be patched
globally for this feature.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/QualityUtility.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Frame.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_BuildCubeSculpture.cs`.

The recipe Harmony transpiler targets the private static method:

```csharp
Thing Verse.GenRecipe.PostProcessProduct(
    Thing product, RecipeDef recipeDef, Pawn worker,
    Precept_ThingStyle precept = null, ThingStyleDef style = null,
    int? overrideGraphicIndex = null)
```

This method checks `product.TryGetComp<CompQuality>()` before generating quality.
It then calls `CompQuality.SetQuality(q, ArtGenerationContext.Colony)`, sets up
art attribution, records applicable tales, sends quality notifications, assigns
styles, and wraps minifiable products. Rewriting only its generation call lets
all those operations observe the final quality without repeating callbacks.
The Item Editor's map-selection restrictions are not reused for newly created,
unspawned recipe products; quality-capable furniture remains supported before
native minification. No quality component is added to unsupported products.
Sources: `refs/decompiled/Assembly-CSharp/Verse/GenRecipe.cs` and
`refs/decompiled/Assembly-CSharp/RimWorld/CompQuality.cs`.

`LegendaryCraftingPatch.GenerateQuality(...)` calls the original pawn overload
exactly once with its original pawn, skill and inspiration flag, then asks the
service whether to return Legendary instead. Disabled/unavailable/ineligible
cases retain the generated result. Inspiration consumption and RNG are not
skipped even when Legendary is enabled. Only newly produced eligible products
are affected; existing items and unrelated quality generation are untouched.

### Eligibility and worker identity

`Verse.AI.Toils_Recipe.FinishRecipeAndStartStoringProduct(TargetIndex)` passes its
current `actor` to `GenRecipe.MakeRecipeProducts(...)`. That worker finishes the
current recipe; it is not inferred from the product, map, bill owner or an
unfinished item's original creator. `GenRecipe.PostProcessProduct` is also used
by `FinalizeGestatedPawns`, and DoBill also handles medical bills, so recipe
arguments alone are not treated as proof of eligible crafting.
Sources: `refs/decompiled/Assembly-CSharp/Verse/AI/Toils_Recipe.cs`,
`refs/decompiled/Assembly-CSharp/Verse/GenRecipe.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/Bill_Medical.cs`.

The recipe predicate requires:

- A non-destroyed, non-Pawn product supporting `CompQuality` and a recipe with
  `workSkill` and declared products or special products.
- A non-destroyed, non-prisoner worker whose actual non-null faction equals
  `Faction.OfPlayer`. The production resolver returns no player faction outside
  a loaded game; tests inject only that resolver.
- The worker's active `JobDriver_DoBill`, with the same worker, current Job and
  exact RecipeDef reference as the product-processing arguments.
- A `Bill_Production` that is not `Bill_Autonomous`, a live
  `Building_WorkTable` target, and `RecipeDef.IsSurgery == false`.

For recipes, this includes quality-bearing products from ordinary production
bills and special-product recipes; it excludes construction, medical operations,
autonomous gestation, destruction/cremation jobs, and product generation outside
the matching active bill context. Non-quality meals/resources receive no quality
change. `RecipeDef.IsSurgery` considers whether any resolved recipe user is a
Pawn, so a custom dual-purpose recipe is conservatively excluded.
Sources: `refs/decompiled/Assembly-CSharp/Verse/RecipeDef.cs`,
`refs/decompiled/Assembly-CSharp/Verse/AI/JobDriver_DoBill.cs`,
`refs/decompiled/Assembly-CSharp/Verse/AI/Job.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/Bill_Autonomous.cs`.

Player slaves are included because `Pawn_GuestTracker.SetGuestStatus(...,
GuestStatus.Slave)` changes their actual faction to the host. Prisoners normally
retain their original faction, but the explicit `IsPrisoner` exclusion also
protects imprisoned player colonists. Foreign guests/NPCs and factionless pawns
are excluded. Temporary quest workers are included if their actual faction is
the player. Player-faction mechanoids are supported without requiring humanlike
status, `IsColonist`, a skills tracker, or current overseer control.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Pawn_GuestTracker.cs`,
`refs/decompiled/Assembly-CSharp/Verse/Pawn.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/QuestPart_JoinPlayer.cs`.

### Construction quality path (1.7.4998b)

`public void RimWorld.Frame.CompleteConstruction(Pawn worker)` is the normal
on-map construction completion path. `Blueprint_Build.MakeSolidThing(...)`
creates a Frame; `JobDriver_ConstructFinishFrame.MakeNewToils()` calls its
completion method with the current actor. The frame creates the actual building,
copies its faction, and checks its live `CompQuality`. Only when that component
exists does it call `QualityUtility.GenerateQualityCreatedByPawn(worker,
SkillDefOf.Construction, consumeInspiration: true)`, then `CompQuality.SetQuality`
with `ArtGenerationContext.Colony` and `QualityUtility.SendCraftNotification`.
Art attribution, styles, source transfer, proportional HP, spawning, storage
settings, records, tales, quest signals and lord notifications stay native.
This is capability-based, covering beds, chairs and any other constructible
building with quality support; tables/buildings without it remain unsupported.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Frame.cs:262`,
`refs/decompiled/Assembly-CSharp/RimWorld/Blueprint_Build.cs:70`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_ConstructFinishFrame.cs:77`.

Important ordering: `CompleteConstruction` destroys its Frame before quality
generation. `LegendaryConstructionPatch` therefore passes the existing `this`
receiver to its helper and reads its retained definition/faction; it must not
reject that frame merely because it is destroyed. The helper generates vanilla
quality exactly once with the original arguments, then uses the shared
`CraftingService` setting and worker predicate. Construction additionally
requires matching frame/worker faction and a building definition supporting
`CompQuality`; terrain, items and non-quality buildings are excluded. It has no
production-bill requirement, since this call site already supplies construction
context. No quality components are created and no existing buildings are scanned.

The exact new Harmony 3 target is the public instance method
`RimWorld.Frame.CompleteConstruction(Verse.Pawn)`, returning `void`. Owner ID:
`slava.rimworldutilities.construction.legendary`. Its transpiler requires exactly
one unmodified call to the pawn/skill/bool generation overload, inserts `ldarg.0`,
and redirects only that call. No generated local slots are assumed. Branch and
exception metadata are preserved as in the recipe patch. Actual assembly IL is
checked by the integration suite, including unchanged remaining native calls.

`Blueprint_Install.MakeSolidThing(...)` returns the existing inner building;
`TryReplaceWithSolidThing(...)` installs it without generating new quality.
Reinstallation must retain existing quality. Ordinary sculptures made at an art
bench already use `GenRecipe`; Anomaly
`JobDriver_BuildCubeSculpture.PlaceAndFinish()` has a separate obsession/mental
state path and intentionally remains unpatched. Searching the local pawn-quality
generator call sites found no additional ordinary construction path requiring a
patch. Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Blueprint_Install.cs:98`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_BuildCubeSculpture.cs:100`,
`refs/decompiled/Assembly-CSharp/Verse/GenRecipe.cs:79`.

Both quality paths reuse `AlwaysLegendaryCrafting` and the existing persistence
format. The checkbox remains "Always Legendary"; its tooltip is
"Legendary quality for crafted items and constructed furniture." Recipe and
construction availability are independent. A missing/ambiguous/already redirected
construction pattern leaves its input IL unchanged, marks only construction
unavailable and warns once. Existing emitted redirects recheck that availability.
Recipe failure likewise leaves supported construction operational; only failure
of both quality paths disables the checkbox. Crafting speed remains independent.

Construction checks cover player/NPC/factionless/prisoner/slave workers,
non-humanlike workers, building ownership, quality support, terrain, disabled
settings, destroyed-frame timing and independence from recipe availability.
Native checks install the real Frame patch once and execute a patched probe with
the same stack/receiver shape, verifying one generator call, original argument
forwarding, one native quality callback, and fail-closed behavior. Only the native
generator call inside our test helper is stubbed; global `QualityUtility` is never
patched. Full construction on a map, real RNG/inspiration consumption, art,
letters, styles and other mods that skip/replace completion remain manual checks.

### Harmony 3 integration and failure handling

The installed Harmony package (`brrainz.harmony`) declares version 3.0.0.0; its
`1.7/Assemblies/0Harmony.dll` is copied unchanged to the ignored local
`refs/0Harmony.dll` for compilation. The production reference has `Private=false`,
so no Harmony DLL is bundled. About.xml declares the dependency and load-after
order. Tests copy the reference into their own build output only.

`RimWorldUtilitiesMod` registers the patch once in its constructor, after loading
settings and creating the service. This does not resolve ThingDefs or query a
player faction during initialization. Owner ID:
`slava.rimworldutilities.crafting.legendary`. The installed API supports
`Harmony.Patch(MethodBase, HarmonyMethod prefix, HarmonyMethod postfix,
HarmonyMethod transpiler, HarmonyMethod finalizer)`.

The transpiler matches exactly one `call` to the precise pawn/skill/bool overload
before rewriting any instructions. It adds the existing static method's product
and recipe arguments and redirects to the five-argument helper. It uses neither
fixed instruction indices nor compiler-generated names. All instructions are
copied; branch labels move to the first added argument, exception begin markers
also move there, and exception end markers remain after the redirect call.
Harmony 3's `Emitter.MarkBlockBefore/MarkBlockAfter` behavior was verified against
the installed DLL. Its `CodeInstruction(CodeInstruction)` constructor preserves
metadata, whereas `Clone()` clears labels and exception blocks.

A missing/ambiguous pattern returns the entire original instruction stream and
marks the corresponding quality path unavailable. Installation errors also
disable only the affected path.
One warning per process explains the failure; there is no per-frame logging.
The helper rechecks availability, so a later incompatible Harmony rebuild also
blocks an already-emitted redirect. Other mods replacing/skipping production or
changing quality after this point can bypass/override the feature; those cases
require in-game compatibility testing.

### Verification and remaining in-game checks

Release build: zero warnings/errors. The full suite including furniture
construction passes 807 assertions plus 70 assertions across ten fresh test
processes. The core suite also covers
eligibility, opt-in behavior, failed writes, settings copying and IL metadata.
Native integration checks read and rewrite the actual target IL, install Harmony
3 in the test process, verify single registration, execute a patched probe, check
original argument forwarding and single quality callbacks, and exercise
unavailable states. Scribe/fresh-process checks cover crafting enable/disable,
legacy Steel defaults, and preservation of settings written by both services.

The probe substitutes only the native-generation call inside our helper in the
test process; it never patches global `QualityUtility` and does not initialize or
read the player's profile. Native RNG/inspiration behavior, art, letters and a
complete real crafting job remain manual checks. No RimWorld process was launched
for automated testing. The user separately reported that their deployed code
works; individual scenarios below are not assumed verified by that report.

Manual checks on a test save:

1. Verify "Always Legendary" starts off with old/fresh settings. Enable it, fully
   restart RimWorld and check it stays enabled. Disable it and repeat. Check
   stack-limit settings, Item Editor controls and pending resets remain intact.
2. With it enabled, craft a quality-bearing weapon, apparel item and sculpture.
   Check Legendary quality, native art/notifications and a single completed bill
   iteration with ordinary ingredient consumption. Non-quality products must
   remain unchanged. Disable it and check normal generation resumes.
3. Include an inspired crafter, a player slave and a crafting mechanoid where
   available. Check inspiration is consumed normally. Foreign workers and
   prisoners supplied by other mods must retain native quality generation.
4. Construct a quality-bearing bed/chair and any quality-capable modded building:
   enabled player construction should be Legendary; disabled/NPC/prisoner cases
   must preserve native generation. Check creativity inspiration consumption,
   art attribution and notifications. Non-quality tables/walls and terrain must
   stay unchanged. Reinstall an existing non-Legendary item and verify its quality
   is preserved; cube-obsession sculptures also retain native behavior.
5. Test other crafting/construction/quality mods. An incompatible call pattern
   should produce one warning, retain original IL, and disable only that path.
   The shared checkbox should remain usable if the other quality path works.

### Crafting Speed Multiplier: production work only

`CraftingService.SpeedMultiplier` supports exactly 1, 2, 3, 5, 10 and 20.
`CustomStackLimitSettings.CraftingSpeedMultiplier` is serialized under
`craftingSpeedMultiplier`, default 1, and included in copy/equality verification.
Old settings without the tag retain normal speed. Invalid saved values are
treated as 1 by the service; unsupported setter values are rejected. The native
dropdown shows `1× (Normal)`, `2×`, `3×`, `5×`, `10×`, `20×`. A successful atomic
write applies to future work intervals and survives process restarts.

Existing `IsAvailable`/`UnavailableReason` still describe Legendary;
`IsSpeedAvailable`/`SpeedUnavailableReason` describe only speed. A failure of
either patch does not disable the other. Controls are disabled individually,
with compact status/diagnostic tooltips. Both settings operations copy the latest
shared settings, preserving each other and the stack-limit/reset records.

#### Callback resolution and exact progress patch

`Verse.AI.JobDriver_DoBill.MakeNewToils()` uses
`Verse.AI.Toils_Recipe.DoRecipeWork()`, which assigns an `Action<int>` to
`Verse.AI.Toil.tickIntervalAction`. `Verse.AI.JobDriver.DriverTickInterval(int
delta)` invokes it with the real elapsed interval. This callback adds delta to
`ticksSpentDoingRecipeWork`, awards applicable skill XP, computes native work
speed and subtracts `num * (float)delta` from `JobDriver_DoBill.workLeft`. It then
synchronizes `UnfinishedThing.workLeft`, gains comfort and checks completion.
Sources: `refs/decompiled/Assembly-CSharp/Verse/AI/JobDriver_DoBill.cs`,
`refs/decompiled/Assembly-CSharp/Verse/AI/Toils_Recipe.cs`,
`refs/decompiled/Assembly-CSharp/Verse/AI/Toil.cs`, and
`refs/decompiled/Assembly-CSharp/Verse/AI/JobDriver.cs`.

`CraftingSpeedPatch` inspects the factory's original IL through Harmony 3
`PatchProcessor.GetOriginalInstructions()`. It requires exactly one assignment
to the precise interval field, preceded by `ldftn` and construction of
`Action<int>`, and validates the referenced method as `void(int)`. This resolves
the generated callback without its compiler-generated name or constructing a
Toil. Registration in the mod constructor needs no initialized game definitions.
Owner ID: `slava.rimworldutilities.crafting.speed`. No new DLL dependency is added.

The verified callback IL pattern is:

```text
load driver; dup; ldfld JobDriver_DoBill.workLeft;
load rate; load delta; conv.r4; mul; sub; stfld JobDriver_DoBill.workLeft
```

The matcher requires exactly one such pattern and exactly one subtract/store
decrement of this field, and rejects an existing speed helper. Positions are
relative to the matching operations, not absolute instruction indices. It copies
the actual receiver-load opcode/operand without assuming a local slot or closure
field name. Immediately before `sub`, it reloads that driver and calls
`CraftingSpeedPatch.ScaleWork(float nativeWork, JobDriver_DoBill driver)` once.
The original subtraction/store, delta, rate calculation, counters, XP,
unfinished-item synchronization and completion branches stay intact. Branch
labels and exception begin markers move to the inserted driver load; exception
end markers remain after the original subtraction. Other metadata is copied.

At 1×, when unavailable, or for an ineligible job, the helper returns the original
float unchanged, without multiplication. Otherwise it multiplies native work
once. Changing speeds mid-job affects future progress only: neither the initial
work amount nor the remaining-work field is rescaled. Native saved work units
therefore remain valid when resuming unfinished recipes.

Missing/ambiguous callback or decrement patterns, or installation errors, disable
only speed. Incompatible IL is returned entirely unchanged, with one warning per
process. The helper checks availability on each call, including after a failed
Harmony rebuild. There is no fallback to changing global work-speed stats.

#### Eligibility and gameplay semantics

`CraftingService.IsEligibleProduction(...)` now supplies the shared worker/bill
predicate, preserving Legendary's previous checks. Speed additionally requires
the supplied driver to still be the worker's current driver. Both require the
player's actual faction, exclude prisoners, require a matching active production
bill at a live worktable, exclude `Bill_Autonomous` and `RecipeDef.IsSurgery`, and
require normal/special products. Player slaves and player-faction mechanoids
remain eligible.

Speed has no quality-component or work-skill requirement. It is not limited to
the Crafting skill or work type: ordinary tailoring, art, cooking, butchering,
smithing, smelting and stonecutting bills share the path. Medical/surgical bills,
autonomous processing, destruction/cremation, research, construction, movement and
unrelated labor are excluded. No global pawn stat or ThingDef is modified.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/WorkGiver_DoBill.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Bill_Medical.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Bill_Autonomous.cs`, and
`refs/decompiled/Assembly-CSharp/Verse/RecipeDef.cs`.

XP retains its native rate per elapsed tick; faster completion normally means
less XP per product. Ingredient collection/consumption, product counts,
storage/hauling and finalization are unchanged. Total job duration need not scale
by the selected factor because non-production stages still take time and
completion is interval-based. Native debug fast-crafting (30×) and other mods'
rate/stat factors compose with this multiplier. Autonomous
`Bill_Autonomous.BillTick()` forming timers are not accelerated.

Mods altering the decrement may fail the strict matcher. A mod replacing the
factory's delegate or the work path entirely can bypass the resolved original
callback; runtime delegate replacement is not followed automatically. These
combinations require in-game compatibility testing.

#### Speed verification and manual checks

Release build: zero warnings/errors. The complete suite passes 543 assertions
plus 46 across eight fresh test processes; the core-only suite passes 419.
Tests cover each factor, invalid values, bit-preserving 1× results, faction/job
exclusions, shared settings and independent availability. Native checks resolve
and patch the actual game callback, verify single registration, and execute the
transpiler on a small progress probe using real `JobDriver_DoBill.workLeft`.
The probe checks unchanged elapsed/XP tick counters, unfinished-work values,
completion and NPC behavior at every factor. It does not run a live skill tracker,
map, ingredient pipeline or complete bill job. Fresh-process Scribe checks cover
Legendary with 20×, Legendary off with 3×, return to 1×, legacy defaults and stack
writes retaining both preferences. No RimWorld process is launched by tests.

Manual checks after user deployment:

1. Check every dropdown choice and persistence after fully restarting RimWorld.
   Combine 1×/20× with Legendary independently on/off. Verify Item Editor and
   persistent stack/reset settings remain intact.
2. With debug fast crafting off, compare progress at 1×, 2× and 20× using the same
   pawn/stats/recipe. Include crafting, tailoring, art, cooking, smithing and a
   no-quality or no-work-skill recipe. Check XP per worked tick, ingredients per
   iteration, product counts and ordinary hauling.
3. Change factors mid-job and save/reload an unfinished project, including a full
   process restart. Remaining work must resume without repeated scaling of its
   stored value. Check a player slave/mechanoid where available; foreign workers
   and prisoners must retain native speed.
4. Check surgery, research, construction and autonomous forming remain unchanged.
   Test other work/recipe mods. A matcher failure should yield one warning and an
   unavailable speed control while Legendary remains usable.

### Building Speed Multiplier: normal Frame construction (1.7.4998b)

`CustomStackLimitSettings.BuildingSpeedMultiplier` is serialized under
`buildingSpeedMultiplier`, default 1. It supports the same 1/2/3/5/10/20 choices
as crafting speed, with independent availability and preference. Invalid saved
values resolve to 1; unsupported setter values are rejected. Copy, CopyFrom and
Matches include the new field. The existing atomic candidate-save/read-back
mechanism preserves all Item Editor, crafting and Basic/Special Needs settings.
The visible `CraftingPanel` title is now **Craft / Build** with Always Legendary
and separately labelled Crafting Speed/Building Speed dropdowns. The enclosing
window reserves enough height for all three controls and their compact error
status; its existing scroll and rendering-state guards remain in use.

#### Native work and botch model

`RimWorld.WorkGiver_ConstructFinishFrames.JobOnThing(Pawn, Thing, bool)` returns
`JobDefOf.FinishFrame` for eligible, material-ready Frames. Its driver is
`RimWorld.JobDriver_ConstructFinishFrame`. The driver's protected
`IEnumerable<Toil> MakeNewToils()` creates a toil with an `Action<int>` assigned
to `Verse.AI.Toil.tickIntervalAction`.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/WorkGiver_ConstructFinishFrames.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_ConstructFinishFrame.cs`, and
the game's `Data/Core/Defs/JobDefs/Jobs_Work.xml`.

The callback gets the actual worker from `build.actor` and its Frame from the
driver's current target A. It awards conditional Construction XP at
`0.25 * delta`, then computes `ConstructionSpeed * 1.7 * delta`, multiplied by
the Frame Stuff's `ConstructionSpeedFactor` when present. This same work amount
feeds both `1 - Mathf.Pow(ConstructSuccessChance, work / frame.WorkToBuild)` and
`frame.workDone += work`. Scaling only the latter would artificially improve
success per work performed. Our helper scales the common amount after material
modifiers, before retrieving WorkToBuild and before the failure roll.

The native ConstructSuccessChance stat, tutorial bypass, RNG call, failure path,
terrain snow/sand clearing, progress addition, completion branch and
`Frame.CompleteConstruction(Pawn)` call remain unchanged. XP rate and real delta
are unchanged; faster completion means less total XP per building. Larger work
steps can overshoot remaining work, as native construction already allows, so
exact RNG sequences/project-level botch probabilities are not invariant under
speed changes. The native work-based probability model is preserved.

`RimWorld.Frame.workDone` is saved by `Frame.ExposeData()` under `workDone`.
Changing the multiplier never rescales existing progress or WorkToBuild.
`Frame.CompleteConstruction(Pawn worker)` retains material destruction, building
or terrain creation, quality/art/notification handling, records and spawning.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Frame.cs` and the driver above.

#### Patch and scope

`BuildingSpeedPatch` uses the installed Harmony 3
`AccessTools.EnumeratorMoveNext(MethodBase)` to resolve MakeNewToils' iterator.
It reuses the existing exact-one interval-delegate assignment resolver on that
MoveNext method's original IL. No generated type/method names are hardcoded and
no Toil or game definition is instantiated. Owner ID:
`slava.rimworldutilities.building.speed`. The resolved `void(int)` callback is
the only target, registered once in the mod constructor.

The transpiler verifies one WorkToBuild getter, one Pow call, one workDone store
and one CompleteConstruction call. It checks the full native rate/material
block and its null-Stuff branch joining immediately before WorkToBuild. It
derives the work/total locals and actor/Frame loads from native uses, verifies
the work local feeds both the probability fraction and progress addition, and
rejects additional writes or an existing helper. It injects one
`work = ScaleWork(work, actor, frame)` at that common join. Branch labels and
exception begin markers move to the inserted first load; end markers and all
other metadata stay with the original instructions. Incompatible input is
returned entirely unchanged, with a single warning and only Building Speed
marked unavailable. Already emitted helpers also honor unavailable state.

`CraftingService.IsEligibleBuildingProgress(Pawn, Frame)` reuses crafting's
worker policy: non-destroyed, non-null player faction, not a prisoner. Player
slaves and player-faction mechanoids are included. The Frame must be spawned,
not destroyed, have matching faction, and represent a building or terrain. The
current driver must be exactly JobDriver_ConstructFinishFrame (special
subclasses excluded), reference the actual worker/current job, and target this
Frame. Buildings without quality and terrain do not need CompQuality.

Walls, furniture, worktables, attachments, floors, bridges and foundations using
this normal Frame path are included. Repair, deconstruction, smoothing,
uninstall/reinstall, hauling, movement and bills are not patched. Roof work has
a separate `JobDriver_AffectRoof.MakeNewToils()` workLeft accumulator; snowmen
and cube sculptures use dedicated drivers and also remain unchanged.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_AffectRoof.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_Repair.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_RemoveBuilding.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_SmoothWall.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Toils_Construct.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Blueprint_Install.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_BuildSnowman.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_BuildCubeSculpture.cs`.

`LegendaryConstructionPatch` continues patching the separate
`Frame.CompleteConstruction(Pawn)` method. The speed patch neither replaces
that call nor its quality pipeline. Availability/failure states for building
speed, crafting speed and both quality paths are independent. Normal modded
definitions using the vanilla Frame driver inherit the effect. Mods replacing
the iterator delegate, construction driver or work block can bypass it or
trigger the strict unavailable fallback; autonomous builders are not covered.

#### Verification and manual checks

Release build passes with zero warnings/errors. The full suite passes 1,133
assertions plus 151 across 20 fresh test processes.

Tests inspect and patch the real installed iterator callback, check exactly-one
registration, local relocation, material-branch labels, exception metadata,
missing/changed/duplicate patterns and unchanged rejected IL. An executable
native-shaped probe uses the emitted helper at all six multipliers, with
different material factors and no Stuff. It verifies the shared botch/progress
amount, failure/completion branches, XP/elapsed counters and faction exclusion.
Probe stat providers and completion/failure side effects are test-only stand-ins
to avoid initializing profiles/maps/RNG; full pawn skill and material/spawn
pipelines still require in-game testing. Fresh test processes round-trip all six
preferences and exercise every shared-settings writer while retaining Basic and
Special Needs, raised limits, pending resets and crafting preferences.

After user deployment, test all dropdown choices on ordinary and quality
furniture, differently stuffed walls, attachments and terrain. Check a low-skill
builder's botches and normal resource handling; inspect XP per elapsed tick.
Verify prisoners/NPCs, repair, deconstruction, smoothing, roofs and hauling stay
unchanged; include an eligible slave and construction mechanoid. Change speeds
mid-job, save/reload partially completed Frames, fully restart to check settings,
and test Legendary on/off independently. Check narrow/UI-scaled scrolling and
compatibility with other construction mods. Total job time includes unaffected
travel/hauling, and completion remains interval-based rather than exactly 20x.

## Research Speed Multiplier (1.7.4998b)

### Verified ordinary research path

`RimWorld.JobDriver_Research.MakeNewToils()` (protected,
`IEnumerable<Verse.AI.Toil>`) assigns an `Action<int>` to the research toil's
`tickIntervalAction`. Its actual `research.actor` contributes
`ResearchSpeed * bench.ResearchSpeedFactor * delta` through the public instance
`void RimWorld.ResearchManager.ResearchPerformed(float amount, Pawn researcher)`.
Intellectual XP is a separate `0.1 * delta` call, followed by native comfort gain.
The workgiver/toil retain their ordinary project/bench checks, reservations,
movement, fail conditions and 4,000-tick job duration.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_Research.cs` and
`refs/decompiled/Assembly-CSharp/RimWorld/WorkGiver_Researcher.cs`.

`ResearchManager.ResearchPerformed` converts work by `0.00825`, applies
`Storyteller.difficulty.researchSpeedFactor`, divides by the current project's
`CostFactor(researcher.Faction.def.techLevel)`, and retains the native debug
fast-research 500x factor. It records the credited points, adds progress and
calls `FinishProject` if the project's native `IsFinished` becomes true.
`ResearchProjectDef.Cost` remains unchanged. The public completion method is
`void FinishProject(ResearchProjectDef proj, bool doCompletionDialog = false,
Pawn researcher = null, bool doCompletionLetter = true)`; prerequisite handling,
unlocks, letters/dialogs, tales and active-project clearing remain entirely native.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/ResearchManager.cs` and
`refs/decompiled/Assembly-CSharp/Verse/ResearchProjectDef.cs`.

A selective call-site search found only JobDriver_Research invoking
ResearchPerformed in the installed decompiled sources. Native books instead use
`AddProgress(ResearchProjectDef, float, Pawn source = null)` in
`ReadingOutcomeDoerGainResearch.OnReadingTick(...)`; research-granting items use
`FinishProject` in `CompUseEffect_FinishRandomResearchProject.FinishInstantly`.
Anomaly uses the separate `ApplyKnowledge` APIs, including Anomaly books, and
study jobs use their own CompStudiable pipeline. These entry points are not
patched. The patch is narrower than a global ResearchPerformed prefix: even a
direct external call while its researcher has an active research job is unscaled.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/ReadingOutcomeDoerGainResearch.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/CompUseEffect_FinishRandomResearchProject.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/ReadingOutcomeDoerGainAnomalyResearch.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_StudyItem.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/JobDriver_StudyInteract.cs`.

### Scoped patch, policy and persistence

`ResearchSpeedPatch` resolves the iterator using Harmony 3
`AccessTools.EnumeratorMoveNext`, then reuses the exact-one Action<int> delegate
assignment resolver. The target is the resolved interval callback, with no
generated names or fixed instruction offsets. Its transpiler requires exactly
one call to the exact ResearchPerformed overload and rejects an existing
redirect. It substitutes a static helper taking the same stack arguments:
`(ResearchManager, float, Pawn)`. Labels and exception blocks stay on that single
instruction. The helper scales the work amount and calls the original manager
method exactly once; no global manager patch, pawn stat or project-cost edit is
installed. Owner ID: `slava.rimworldutilities.research.speed`.

`ResearchSpeedService` receives `CraftingService.IsEligibleWorker`, keeping the
same non-destroyed, non-null player-faction and non-prisoner policy. It adds an
exact ordinary JobDriver_Research/current-job/worker match, a live spawned
Building_ResearchBench target and an active project with positive baseCost,
no knowledgeCategory and zero knowledgeCost. Special driver subclasses and
specialized projects are excluded. Normal eligible slaves/mechanoids are not
excluded by race/colonist status; native job capability still determines who can
actually perform this work. Multiple researchers each scale only their own
contribution. At 1x or when unavailable/ineligible, the original float is returned
without arithmetic. XP per elapsed tick stays native; less total XP per completed
project is expected at faster speeds.

`CustomStackLimitSettings.ResearchSpeedMultiplier` uses the stable
`researchSpeedMultiplier` tag, default 1, and the existing 1/2/3/5/10/20 choices.
Invalid saved values fall back to 1. Candidate copying, Scribe serialization,
CopyFrom and Matches include the field. Atomic persistence retains all stack
limits/resets, Legendary, Crafting Speed, Building Speed and Basic/Special Needs.
A compact Research panel follows Craft / Build in the same scroll view; other
panels' content is unchanged. Runtime matching failures leave the original IL
untouched, warn once and disable only Research Speed. Its existing helper also
honors subsequent unavailable state. All UI rendering state is restored through
UtilitiesPanel, and the new dropdown has independent availability/error tooltips.

### Verification and remaining runtime checks

Release build passes with zero warnings/errors. The full suite passes 1,292
assertions plus 186 across 27 fresh test processes.

Tests cover all choices, exact 1x float identity, failed saves, worker/job/project
exclusions, independent availability, labels/exception blocks, exact-one matches
and unchanged rejected IL. They resolve and install the actual game callback.
An emitted contribution probe executes the real ResearchPerformed body, testing
native difficulty and tech factors, debug composition, records, two researchers,
direct unscaled calls, book AddProgress and native completion dispatch.
Test-only record and completion prefixes isolate DefMap/UI/world effects; the
completion decision runs natively but the full FinishProject side effects and
real skill tracker are not exercised. In-memory Game/RecordDef/debug fixtures are
restored afterward; no player profile or save is used. Fresh processes verify
each multiplier and cross-service settings writes, including legacy defaults.

After user deployment, compare work at all six speeds, check native Intellectual
XP per tick, and run two eligible researchers at different benches. Finish a
small project and verify exactly one normal completion dialog/unlock, unchanged
prerequisites and no automatic project selection. Test prisoners/NPCs, reading,
research-granting items, quest/debug rewards and Anomaly studying for unchanged
behavior. Change speed mid-job, save/reload progress, fully restart for settings,
and check scrolling at different UI scales. Mods that replace the research
delegate/driver can bypass the patched callback; mods changing its call pattern
can trigger the unavailable fallback. Specialized/custom research paths are not
promised support, and other mods' research factors may compose with this one.

## Colonist Needs

### Phase 1: Basic Keep Full

`ColonistNeedsService` manages eight independent identifiers: `Food`, `Rest`,
`Joy` (UI: Recreation), `Mood`, `Comfort`, `Beauty`, `Outdoors` and `Indoors`.
All default off. The existing `CustomStackLimitSettings` serializes a string set
under `enabledColonistNeeds`; missing old tags load as empty. Copying and atomic
read-back verification include the set. Unknown saved identifiers are preserved
for compatibility but cannot activate this phase's refill logic. No second
`ModSettings` type or new assembly dependency is introduced.

The service saves a candidate copied from the latest shared settings before
enabling/disabling a toggle. Failed writes retain the previous state. Successful
enable immediately fills only that need for eligible accessible pawns; disable
stops future writes without resetting its current value or reversing gameplay
effects. Item Editor settings, pending resets and independent crafting flags are
retained in either direction when another service writes shared settings.

### Strict eligibility, separate from crafting

`ColonistNeedsService.IsEligible(Pawn)` requires all of the following:

- Alive and not destroyed; actual non-null faction equal to `Faction.OfPlayer`.
- `Pawn.IsColonist`, and explicitly neither `IsPrisoner` nor `IsSlave`.
- `HostFaction == null`, nullable `Pawn.GuestStatus == null`, and
  `QuestUtility.IsQuestLodger(pawn) == false`.
- Not a mechanoid and not `Pawn.Suspended`.

`Pawn.IsColonist` requires player-faction humanlike status but can include secure
slaves, so it is insufficient by itself. It has no age restriction: children and
babies qualify. There is no Spawned, current-map or player-control requirement,
so unspawned travelers and downed colonists remain eligible. Animals, foreign
visitors/enemies, guests, temporary lodgers and arrested player pawns are excluded.
This intentionally differs from crafting's inclusion of player slaves/mechanoids.
Sources: `refs/decompiled/Assembly-CSharp/Verse/Pawn.cs`,
`refs/decompiled/Assembly-CSharp/Verse/RaceProperties.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Pawn_GuestTracker.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/QuestUtility.cs`.

Use the nullable **Pawn** guest-status property rather than rejecting the guest
tracker's default `Guest` enum: ordinary colonists can have a tracker in that
default state without actually being guests. `IsQuestLodger()` checks extra home
or mini factions from quest parts. `Pawn.Suspended` includes suspended holders
through `Thing.Suspended`, and quest-reserved world pawns through `WorldPawns`.
Sources: `refs/decompiled/Assembly-CSharp/Verse/Thing.cs` and the files above.

### Public levels and native 150-tick integration

`RimWorld.Need.CurLevel` is a public virtual float property; its native setter
clamps to `0..MaxLevel`. `Need.MaxLevel` is virtual and must be read for each
refill, not assumed to be 1. In particular, `Need_Food.MaxLevel` can reflect pawn
size/life stage or the MaxNutrition stat. The service reads the live tracker list,
checks existing NeedDef identifiers, and sets `CurLevel = MaxLevel` only when
different. Nonfinite/negative maxima and missing definitions are ignored. It
never adds/removes Needs, changes NeedDefs, calls `NeedInterval()` manually, or
retains Need references between passes.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Need.cs` and
`refs/decompiled/Assembly-CSharp/RimWorld/Need_Food.cs`.

`ColonistNeedsPatch` registers one Harmony prefix/postfix pair on the exact
public instance method:

```csharp
void RimWorld.Pawn_NeedsTracker.NeedsTrackerTickInterval(int delta)
```

Owner ID: `slava.rimworldutilities.needs.basic`. Registration occurs once in the
mod constructor after creating the service; missing method/owner field or patch
errors mark the feature unavailable and log one warning. Prefix/postfix do not
skip or replace the original method. The verified private `pawn` field is passed
by Harmony field injection. The prefix rejects detached trackers and uses the
same native `pawn.IsHashIntervalTick(150, delta)` gate as the original method.
It fills enabled existing needs before the native loop. A bool `__state` enables
the postfix, which re-reads current needs, toggle state and eligibility after
native callbacks. Removed/replaced needs cannot leave stale references; newly
added needs can be filled without another interval call. Disabling or becoming
ineligible during the native update prevents postfix refill.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Pawn_NeedsTracker.cs` and
`refs/decompiled/Assembly-CSharp/Verse/Gen.cs`.

`Gen.IsHashIntervalTick(Thing, int interval, int delta)` uses
`Math.Abs(HashOffsetTicks() % interval) < delta`, including signed hash offsets.
Do not substitute global `TicksGame % 150`, assume every pawn has the same phase,
or manually catch up intervals. With all supported toggles off, the patch takes
a short path without eligibility scans; periodic passes allocate no snapshots
or collections. Only enabling a toggle creates a temporary reference-identity
set for immediate application, deduplicating overlapping finder routes.

Mood uses the same current-level property. `Need_Mood.NeedInterval()` and its
`Need_Seeker` base continue executing normally, including native thought/memory
updates. The mod does not write thoughts, memories, instant/target mood or mental
states, and does not end existing mental breaks or clear health conditions.
Needs may change through unrelated events between native refill intervals;
this is not a per-frame freeze.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Need_Mood.cs` and
`refs/decompiled/Assembly-CSharp/RimWorld/Need_Seeker.cs`.

### Loaded maps and traveling colonists

Immediate enable uses
`RimWorld.PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive`, followed by
the service's stricter eligibility. The broad finder combines every loaded map,
caravan and traveling transporter, plus supported gravship pawns. Its `AllMaps`
route uses `Verse.MapPawns.AllPawns`, which includes spawned pawns and unspawned
pawns found recursively in map holders, such as carried pawns. Finder lists are
shared scratch results and are not retained by the service.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/PawnsFinder.cs` and
`refs/decompiled/Assembly-CSharp/Verse/MapPawns.cs`.

`RimWorld.Planet.WorldPawns.WorldPawnsTick()` calls `Thing.DoTick()` on active
world pawns. `Verse.Pawn.TickInterval(int)` calls the same needs-tracker interval
for living pawns, and `WorldPawns.ShouldMothball(Pawn)` excludes caravan members
and pawns traveling in transport pods. `Caravan.AddPawn(...)` registers carried
pawns with WorldPawns where applicable. Thus vanilla caravan colonists receive
the same hook: no second refill loop, duplicate native interval call, or global
GameComponent scan is required. `Caravan_NeedsTracker` separately satisfies
food/rest/joy; that behavior is preserved.
Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Planet/WorldPawns.cs`,
`refs/decompiled/Assembly-CSharp/Verse/Thing.cs`,
`refs/decompiled/Assembly-CSharp/Verse/Pawn.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Planet/Caravan.cs`, and
`refs/decompiled/Assembly-CSharp/RimWorld/Planet/Caravan_NeedsTracker.cs`.

Coverage is not a guarantee for unknown custom world holders, mothballed pawns
outside the supported travel paths, or mods replacing/skipping the tracker
update. Suspended pawns remain intentionally excluded. No all-save-file or
arbitrary-holder completeness claim is made.

### UI and verification

`ColonistNeedsPanel` displays Basic with two checkbox columns when content width
is at least 360 units, otherwise one column. Each has a short Keep Full tooltip.
`MainTabWindow_Utilities` reserves the required needs-panel height and scrolls
the three sections; Item Editor and Crafting contents are preserved. Rendering
state is restored by the existing `UtilitiesPanel` guard. Only runtime/settings
errors add a compact status line. Biotech's Special controls are described below.

Release build passes with zero warnings/errors. Needs checks cover each
toggle, native level clamping/changed maxima, strict eligibility, missing/added/
removed needs, immediate application, disabled behavior and shared persistence.
The native integration test patches the actual tracker and runs instrumented
Need subclasses through its real hash gate and interval loop. It verifies a
single original update, before/after refill, batched delta, no off-phase writes,
no manual interval for a new need, changed eligibility, mid-update disable and
detached tracker handling. In-memory fixtures restore their Current.Game and
DefOf bindings; no player profile, map or RimWorld process is started.

Manual checks after user deployment:

1. Confirm all eight defaults are off with older settings. Enable each in turn
   and verify immediate fill of that existing need only. Disable it and check
   normal decline resumes without resetting the previous full value. Include
   babies/children lacking some adult needs; no extra needs should appear.
2. Check free colonists on multiple loaded maps and in a caravan, including
   unspawned/carried pawns where available. Prisoners (including player pawns),
   slaves, visitors, quest lodgers, animals, mechanoids and suspended pawns must
   remain unaffected. Resume a suspended eligible colonist and check the next
   native interval handles them normally.
3. With Mood enabled, inspect current mood, thoughts, target mood and any existing
   mental break: only the current level is maintained; native thoughts/mental
   states continue. Check normal caravan food/rest behavior and other needs mods.
4. Save, fully restart and reload with several toggles on. Verify their state,
   stack-limit/reset settings, Always Legendary and crafting speed. Check panel
   scrolling and both column layouts at different UI scales.

### Special Phase 1: Learning and Play (Biotech)

`ColonistNeedsService.SpecialNeeds` adds the independent identifiers `Learning`
and `Play` to the existing `EnabledColonistNeeds` set. Both default off in older
settings, including Basic-only settings. The existing Scribe string-set format,
copying, atomic save/read-back verification and shared settings writes already
support these keys; no schema migration or second settings class is needed.
`Verse.ModsConfig.BiotechActive` gates enabling and refilling both special needs.
Saved preferences are retained when Biotech is inactive, while the Special group
is hidden and Basic needs remain usable. Disabling never reverses the last refill.
Source: `refs/decompiled/Assembly-CSharp/Verse/ModsConfig.cs`.

The resolved vanilla definitions in the game's
`Data/Biotech/Defs/NeedDefs/Needs.xml` use:

| NeedDef | Class | Developmental stage | Vanilla faction restriction |
| --- | --- | --- | --- |
| `Learning` | `RimWorld.Need_Learning` | Child | `colonistsOnly` |
| `Play` | `RimWorld.Need_Play` | Baby | `colonistAndPrisonersOnly` |

`RimWorld.Pawn_NeedsTracker.ShouldHaveNeed(NeedDef)` checks
`developmentalStageFilter.Has(pawn.DevelopmentalStage)` when maintaining the
tracker. Our service only reads existing needs: it neither duplicates this
lifecycle nor adds missing needs to babies, children or adults. The unchanged
strict `IsEligible` filter still rejects prisoners even though vanilla allows
them to have Play, and rejects slaves/guests/lodgers/animals/mechanoids/dead or
suspended pawns. There is no new pawn-location restriction.
Source: `refs/decompiled/Assembly-CSharp/RimWorld/Pawn_NeedsTracker.cs`.

Both classes inherit the public `Need.CurLevel` setter and actual `MaxLevel`.
They require no auxiliary state writes for Keep Full:

- `RimWorld.Need_Learning.NeedInterval()` normally subtracts `0.00045000002`
  unless frozen, then adds `pawn.ageTracker.GrowthPointsPerDay * 0.0025` when
  `canGainGrowthPoints` is true. Its frozen check also includes Deathrest.
- `RimWorld.Need_Play.NeedInterval()` normally subtracts `0.00025` unless frozen.
  `Need_Play.Play(float)` is a separate gameplay action and is not called.
- `Verse.Pawn_AgeTracker.GrowthPointsPerDay` uses current Learning when applicable;
  `GrowthPointsPerDayAtLearningLevel(float)` scales that level by native growth
  and child-aging factors. Keeping Learning full therefore increases growth
  through vanilla's existing calculation. Prefix refill is followed by vanilla
  decay before growth is evaluated, and postfix refill restores the maximum.
  No growth points, growth flags, skills, learning desires or care jobs are
  written directly. Native behavior that depends on satisfied needs remains in
  effect; this does not force learning or caregiver activities to continue.

Sources: `refs/decompiled/Assembly-CSharp/RimWorld/Need_Learning.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Need_Play.cs`,
`refs/decompiled/Assembly-CSharp/RimWorld/Need.cs`, and
`refs/decompiled/Assembly-CSharp/Verse/Pawn_AgeTracker.cs`.

The existing 150-tick `ColonistNeedsPatch` prefix/postfix and immediate
`PawnsFinder` pass handle both identifiers, including Special-only settings.
There are no additional Harmony patches, timers or retained Need references.
Native `NeedInterval()` still runs once. The same supported maps/caravan/travel
coverage and custom-holder limitations documented above apply.
The panel shares the existing group renderer, two-column threshold and rendering
state guard; its requested height includes Special only with Biotech active.

Verification: Release build has zero warnings/errors; the complete suite passes
927 assertions plus 116 across 13 fresh test processes. Coverage includes
independent Special toggles, actual changing maxima, missing/replaced needs,
stage-appropriate fixture lists, all strict eligibility cases, Biotech absence,
immediate/periodic refill, failed saves, Basic compatibility and shared settings.
Native tests execute the actual Learning/Play interval bodies through the patched
tracker and verify one native growth increment per interval. Headless subclasses
isolate frozen-state lookups, and a test-only fixed growth-rate provider avoids
profile/stat initialization; the complete growth formula is not simulated.
Fresh test processes verify Basic-only defaults, all needs on/off, Learning only,
and Play only while retaining stack limits, pending resets and crafting settings.

Manual checks after user deployment:

1. With Biotech active, verify Special appears below Basic, defaults off, and
   remains compact at both column widths. Without Biotech active, verify it is
   hidden and Basic remains functional.
2. Enable Learning for a free child and Play for a free baby independently. Check
   immediate fill, sustained levels through native intervals and normal decline
   after disabling. Adults and pawns missing the need must gain no new needs.
3. Observe native child growth points and age transitions, learning desires and
   baby-care behavior. Repeat on another loaded map and in a caravan. Prisoners,
   slaves, lodgers and suspended pawns must remain untouched.
4. Save, fully exit RimWorld, restart and reload with each Special toggle enabled
   alone, then together with Basic. Check persistence and existing Crafting and
   Item Editor settings. Other mods replacing need updates still need runtime
   compatibility checks; unknown custom world holders remain outside coverage.

Deathrest, chemical needs and Hemogen are not implemented.
