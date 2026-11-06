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
- Editor policy: require one spawned `Verse.ThingCategory.Item`, a positive
  current count, and `Verse.ThingDef.stackLimit > 1`. Reject pawns, buildings,
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
`Verse.Widgets.ButtonText(..., drawBackground: false, ...)` supports the compact,
one-way Steel enable action. Sources: `refs/decompiled/Assembly-CSharp/Verse/TexButton.cs`,
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
stack validation, persistence, and mutation services are unchanged.

### Persistent large stacks: source research

Steel's persistent definition-level limit is now implemented as described below.
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
  The truncation is **not** conditional on `respawningAfterLoad`. Thus, saving
  50,000 units with an effective limit of 75 does not preserve that count on
  reload: the normal load path discards 49,925 units instead of splitting them.
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
  overflow those intermediates. Prefer an explicit resource-type allowlist and
  conservative configured limits over a blanket one-million limit for all items.
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
   direction, now implemented: opt in only Steel at 100,000, keeping 50,000 below the
   effective limit. Capture the resolved baseline before our override, including
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

The implemented invariant is `1 <= stackCount <= effective ThingDef.stackLimit`.
The original limit is retained separately for UI/reference; it is not temporarily
swapped during spawn or GUI calls. The editor has a persistent enable action and
no disable/reset action. Apply validates the current effective limit. A future
one-million editor ceiling would require the definition's effective limit to be
at least that large, plus item-specific validation/testing.

Existing saves with ordinary stacks retain their counts under raised limits;
native serialization needs no custom quantity format. The override must be
reapplied before every load. Disabling/removing the mod, lowering a setting, or
another mod lowering the limit can reintroduce truncation on the next spawn/load.
Before reducing limits or uninstalling, a future resource-preserving migration
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

### Implemented Steel initialization and settings contract

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
   The callback captures Steel's resolved limit (unless original metadata was
   already saved) and applies `max(current limit, saved limit, 100000)` only if
   Steel was previously enabled. Disabled Steel is left untouched.
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

`CustomStackLimitSettings : Verse.ModSettings` persists dictionaries of enabled
definition names/effective limits and original-limit metadata. Only Steel is
allowlisted; unknown settings entries do not enable other definitions. The
original value is obtained from the resolved definition, never a hardcoded 75,
and its saved metadata is not replaced by our raised value on later restarts.

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
it back through Scribe, compares both dictionaries, flushes it to disk, and then
replaces the target. The previous settings remain intact if serialization or
verification fails. Settings writes are rejected while Scribe is already active;
they never force-stop an unrelated game save/load.
Sources: `refs/decompiled/Assembly-CSharp/Verse/ScribeSaver.cs`,
`refs/decompiled/Assembly-CSharp/Verse/Scribe_Deep.cs`, and
`refs/decompiled/Assembly-CSharp/Verse/Scribe_Collections.cs`.

The enable action persists successfully before changing Steel's definition and
never changes quantities. Apply still changes quantity only after selection and
integer validation. Startup records higher observed limits for already-enabled
Steel, so they survive later restarts even without an editor Apply. Apply also
persists any higher current limit supplied by another mod, then restores any
previously saved floor if needed. Startup write failures retain the existing
limit and produce one error; failed persistence blocks quantity edits until a
retry succeeds. There are no per-frame writes. Runtime raises
notify `ListerMergeables.Notify_ThingStackChanged()` for Steel stacks across all
loaded maps, since previously full stacks may now have merge room. Other item
definitions and quantities are untouched. There is no disable/reset operation.

Initial support excludes a save-compressible Steel definition. Other mods can
still introduce custom storage behavior, overwrite limits after our startup
callback, or hot-reload definitions; those interactions are not guaranteed by
this feature. The service preserves higher limits it observes and persists them
before our edits, but it cannot prevent an external mod from lowering a limit
later. Removing the mod/settings or loading an unmigrated save without the saved
effective limits remains unsafe. Quantity-preserving migration is not implemented.

#### Focused verification and full-restart manual check

`tests/RimWorldUtilities.Tests` is a package-free console test project. The core
suite covers numeric bounds, opt-in defaults, resolved baseline capture, failed
writes, unsupported types, monotonic limits, and other mods' higher limits. With
`RIMWORLD_GAME_DIR` pointing to the installed game DLL directory, it additionally
uses native Scribe serialization and loads the saved settings in a fresh test
process. Runtime DLLs are read-only; fixture settings live in a temporary folder.
See `tests/README.md` for the commands and the distinction from launching RimWorld.

The full suite passed 113 assertions plus three assertions in the fresh process.
It does not exercise a game map or constitute a full RimWorld restart test.

Manual verification after the user deploys the build:

1. With fresh mod settings, select Steel. Check its original/effective limits
   match the resolved definition. Other resources must have no enable action.
2. Enable the persistent limit. Check the effective limit becomes 100,000 (or
   retains a higher existing limit), the original value remains visible, and the
   quantity/input buffer do not change. There must be no disable/reset action.
3. Apply 50,000, save the game, fully quit RimWorld, then start a new RimWorld
   process and load that save. Check the quantity before opening Utilities;
   then verify the enabled status and both displayed limits. Repeat save/load.
4. Haul, merge, split, and store Steel on shelves/in containers; verify total
   quantity is preserved. Include saving/restarting while a pawn carries Steel
   or it is held in an inventory. Carrying capacity must still constrain hauling.
5. Reject zero, negatives, text, and values above the effective limit. Confirm
   unrelated quantities and the other two panels remain unchanged. Do not remove
   this mod/settings on a valuable save containing large stacks; no safe migration
   or uninstall action exists yet.

HP, quality, and rot mutation APIs have not been researched or implemented yet.

## Crafting

To be investigated.

## Colonist Needs

To be investigated.
