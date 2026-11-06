# Reset & Trim: research and proposed design

This is an unimplemented scan-and-trim design for RimWorld 1.7.4998b. The current
feature instead offers an explicitly destructive reset on the next full process
restart, without scanning or trimming; see [game-api-notes.md](game-api-notes.md#destructive-reset-stack-limit-on-full-process-restart).
The coverage requirements below belong to this separate proposed design.

## Coverage conclusion

The native `IThingHolder` graph covers many ordinary storage locations, but it is
not a complete enumeration of the saved game. Quest parts and transport-ship
managers can retain unspawned Things outside that graph. Custom mods may keep
additional private collections without exposing `IThingHolder` at all.

A generic holder traversal therefore cannot certify that every relevant instance
has been found. A future reset must refuse when any required storage provider is
unknown, inaccessible, throws during enumeration, or lacks an audited adapter.
Zero discovered stacks is not proof of complete coverage.
The holder interface itself is in `Verse/IThingHolder.cs`; it exposes parent,
direct-container, and child-holder access, not a global completeness contract.

## Verified roots and traversal routes

All source paths below are relative to `refs/decompiled/Assembly-CSharp/`.

| Location | Actual API / route | Important behavior |
| --- | --- | --- |
| All loaded maps | `Verse.Find.Maps`; each `Verse.Map` implements `IThingHolder` | `Map.GetDirectlyHeldThings()` returns `spawnedThings` in this version. `GetChildHolders()` includes holder Things, passing ships, and holder map components. No current-map or player-faction filter. Sources: `Verse/Map.cs`, `Verse/Find.cs`. |
| Pawn inventory, carried items, equipment, apparel | `Verse.Pawn.GetChildHolders()` | Adds inventory, carry tracker, equipment, and apparel holders. Their `GetDirectlyHeldThings()` methods expose the respective containers. Sources: `Verse/Pawn.cs`, `Verse/Pawn_InventoryTracker.cs`, `Verse/Pawn_CarryTracker.cs`, `Verse/Pawn_EquipmentTracker.cs`, `RimWorld/Pawn_ApparelTracker.cs`. |
| World objects and world components | `RimWorld.Planet.World.GetChildHolders()` | Includes `IThingHolder` world objects, their holder comps, and holder world components. This does **not** add all world pawns. Source: `RimWorld/Planet/World.cs`. |
| World pawns, including dormant/dead ones | `RimWorld.Planet.WorldPawns.AllPawnsAliveOrDead`, plus `ForcefullyKeptPawns` as a defensive union | `AllPawnsAlive` includes mothballed pawns. Snapshot the returned scratch list before traversing. Caravans and corpses can expose the same pawn, so deduplication is required. Source: `RimWorld/Planet/WorldPawns.cs`. |
| Caravans | `RimWorld.Planet.Caravan.GetDirectlyHeldThings()` | Returns its pawn owner; child traversal then reaches pawn inventories and equipment. Source: `RimWorld/Planet/Caravan.cs`. |
| Loaded map parents / settlements | `MapParent.GetChildHolders()`; `Settlement.GetChildHolders()` | Map parents add their loaded Map; settlements also add their trader tracker. The same Map is thus reachable through multiple roots. Sources: `RimWorld/Planet/MapParent.cs`, `RimWorld/Planet/Settlement.cs`. |
| Existing trader stock | `Settlement_TraderTracker.GetDirectlyHeldThings()` and `RimWorld.TradeShip` | Direct holder access returns existing stock. **Do not call `StockListForReading` or `Settlement.Goods` during a read-only scan:** the stock-list getter calls `RegenerateStock()` if stock is missing/empty. Sources: `RimWorld/Planet/Settlement_TraderTracker.cs`, `RimWorld/Planet/Settlement.cs`, `RimWorld/TradeShip.cs`. |
| Nested buildings, Things, and comps | `Verse.ThingOwnerUtility.AppendThingHoldersFromThings()` | Discovers both a Thing implementing `IThingHolder` and holder comps on `ThingWithComps`. The proposed scanner should inspect every encountered Thing and its comps, not rely solely on a cached map request group. Source: `Verse/ThingOwnerUtility.cs`. |
| Corpses, minified Things, pawn flyers | Their `IThingHolder` implementations | Traverse them even though they are not eligible editable items. Their contents may lead to a pawn, inventory, or another container holding the target definition. Sources: `Verse/Corpse.cs`, `RimWorld/MinifiedThing.cs`, `RimWorld/PawnFlyer.cs`. |
| Transporters / shuttles / in-flight pods | `RimWorld.CompTransporter`, `RimWorld.ActiveTransporter`, `RimWorld.ActiveTransporterInfo`, `RimWorld.Planet.TravellingTransporters` | Comp transporters expose `innerContainer`; active transporters add their contents holder; travelling transporters add each pod's contents holder. Pawn inventory traversal remains necessary inside those containers. Sources: `RimWorld/CompTransporter.cs`, `RimWorld/ActiveTransporter.cs`, `RimWorld/ActiveTransporterInfo.cs`, `RimWorld/Planet/TravellingTransporters.cs`. |
| Unspawned managed transport ships | `RimWorld.TransportShipManager.AllTransportShips` → `TransportShip.shipThing` | This manager is not an `IThingHolder`. `TransportShip.ExposeData()` deep-saves the ship Thing when it is neither spawned nor owned elsewhere. Add the ship Thing as an explicit root and inspect its holder comps. Sources: `RimWorld/TransportShipManager.cs`, `RimWorld/TransportShip.cs`. |
| Game components | `Verse.Current.Game.components` | `Game` is not a holder root. Add components implementing `IThingHolder`; require explicit coverage declarations/adapters for other components that can own Things. Source: `Verse/Game.cs`. |
| Quest-owned unspawned items | `RimWorld.QuestManager.QuestsListForReading` → `RimWorld.Quest.PartsListForReading` → audited part adapters | `QuestPart_SpawnThing.thing` can be deep-saved before spawning. `QuestPart_GiveNearPawn.Things` / `QuestPart_DropPods.Things` expose pending items/pawns despite those parts not implementing `IThingHolder`. Additional part state/reference lists need auditing. Sources: `RimWorld/QuestManager.cs`, `RimWorld/Quest.cs`, `RimWorld/QuestPart_SpawnThing.cs`, `RimWorld/QuestPart_GiveNearPawn.cs`, `RimWorld/QuestPart_DropPods.cs`. |

`RimWorld.HiddenItemsManager` tracks discovery flags keyed by ThingDef, not hidden
Thing instances; it is not an item-container root. Source:
`RimWorld/HiddenItemsManager.cs`.

## Why the native recursive helper is insufficient on its own

`Verse.ThingOwnerUtility.GetAllThingsRecursively(IThingHolder, List<Thing>, bool,
Predicate<IThingHolder>)` follows the advertised child holders and directly held
Things. It uses shared scratch collections, has no visited-holder/Thing sets, and
does not report coverage gaps. Its map overload combines spawned results with
child-holder results; combining overlapping roots without global deduplication
can overcount.

`ThingOwnerUtility.AreImmediateContentsReal()` classifies corpse/minified contents
as unreal for some native queries. Reset coverage must not use that filtering to
drop traversal routes. Source for these behaviors: `Verse/ThingOwnerUtility.cs`.

## Proposed read-only enumeration

1. Require a fully loaded game, paused ticks (`Verse.TickManager.Pause()` /
   `Paused`), inactive Scribe, and no map/world transition. Snapshot the relevant
   root lists. Pausing alone does not prevent mods or UI callbacks from changing
   containers, so a preview cannot be treated as permanently valid.
2. Use an iterative work queue with local scratch lists. Keep separate
   reference-equality sets for Things, holders, and containers. Do not deduplicate
   by Thing ID alone: IDs are useful diagnostics but invalid/modded objects can
   have missing or duplicate IDs. Shared roots should not inflate totals; cycles
   or inconsistent ownership should be reported.
3. Visit every root above. For each holder, snapshot its direct container and
   child-holder list. For each encountered Thing, record the reference once and
   enqueue the Thing itself if it is a holder, plus holder comps. Never filter
   traversal to the target def: a different Thing/pawn/building can contain it.
   Do not limit results to spawned Things or the player's faction.
4. Add explicit providers for non-holder storage such as transport-ship managers
   and quest parts. Each adapter must enumerate only existing objects, avoid
   lazy generation, and declare its read and future mutation coverage. Unknown
   component/quest-part/mod storage remains unsupported until audited. Arbitrary
   reflection over private fields or properties is not a completeness proof and
   can follow templates, stale objects, or mutating getters.
5. Collect paths and coverage diagnostics. Normal null direct containers mean
   empty contents (Pawn/World/active pod roots can legitimately return null).
   Exceptions, unavailable roots, disposed storage still referenced by the game,
   invalid target counts, or unaudited storage providers make the result
   incomplete. Do not silently skip them and report success.

Proposed result data, not implemented API:

- Target defName, saved original limit, current effective/persisted limits.
- Unique affected stack references, IDs, counts, and owner/root paths.
- Checked 64-bit quantity totals and
  `discarded = sum(max(0L, (long)stackCount - originalLimit))`.
- Coverage status, inspected root/provider types, and explicit unsupported or
  inaccessible storage diagnostics. The status must describe the **loaded game**,
  not all saves or all objects in the process heap.

## Future confirmed operation

The destructive action is intentionally absent in this iteration. It should:

1. Produce the complete read-only preview first. If coverage is incomplete or the
   original limit is unavailable/invalid, refuse without changing quantities,
   settings, or definitions.
2. Show the affected stack count and exact total units to discard. Require an
   explicit confirmation naming the item type and loss. State that NPC/trader
   stocks and held items in this loaded game are included where applicable.
3. Immediately revalidate the roots, coverage, stack identities/counts/ownership,
   limits, and settings after confirmation. If the preview changed, abort and
   generate a new preview rather than applying stale totals.
4. Keep the raised effective/persisted limit throughout processing. Trim only the
   confirmed stacks above the original limit. Holder-specific adapters must handle
   necessary bookkeeping: map mesh/merge/resource refresh versus held-container,
   pod/caravan cache updates. Refuse mutation for providers without that support.
5. Verify every planned result and total. Only after successful processing may
   settings and the shared definition be reduced. A partial failure must retain
   the raised floor and report any quantities already trimmed; never silently
   lower the limit after a partial pass. Crash/save ordering and recovery need
   their own design before implementation.

The saved original limit is a **historical resolved baseline**, potentially
including another mod's override. It is not necessarily vanilla's value or the
current startup value before our override. Assigning that historical value during
reset and deleting our saved floor does not guarantee the same limit on the next
restart. A future reset needs the current raw resolved baseline as well as saved
metadata, must verify which limit will survive without our override, and must
refuse an unproven/conflicting downgrade. A public `stackLimit` field alone does
not identify which mod last wrote it. The current service intentionally never
removes a saved floor; reset provenance checks are not implemented.

**Other save files on disk are not scanned.** Mod settings are shared across
saves, so lowering a limit for this game can make an older/unconverted save lose
resources at its next spawn/load. A complete active-game scan does not make
uninstallation or loading every other save safe. That limitation must appear in
the future confirmation flow; safe converted-save persistence and recovery are
separate from a successful in-memory trim. No automatic disable or uninstall
guarantee should be offered.
