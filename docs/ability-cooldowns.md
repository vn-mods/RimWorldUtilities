# Ability and ritual cooldowns

Open **Utilities → Abilities → Disable Cooldowns**. This checkbox controls
standard ability timers and the shared/source-ability gates for **Leader Speech**
and **Conversion Ritual**. It defaults to off and uses the existing global mod
settings file, including write verification and failure reporting. It persists
across saves and restarts; it is not a per-colony save setting.
The separate Fast Biosculpter Cycles checkbox does not change this setting.

Enabling takes effect on the next availability query, even while paused or with
an existing cooldown. Stored timers continue to run normally. Disabling, losing
player control, or transferring the ability to another role holder reveals any
remaining native cooldown. No map scans or new save-game state are needed.

## Scope

Eligible pawns must currently be alive, spawned, unsuspended, free humanlike
player colonists with `IsColonistPlayerControlled`, no host faction, and no active
guest status. Prisoners, slaves (including controlled slaves), quest lodgers,
mental states, mechanoids, enemies, neutral visitors, caravans, and temporarily
unspawned pawns are excluded. Returning to eligible status is detected dynamically.
Permanent recruits qualify after their quest/guest restrictions end.

Supported abilities are exact `RimWorld.Ability` and `RimWorld.Psycast` instances
using the standard timer. Charge abilities, per-charge regeneration, externally
managed cooldowns, and unverified derived ability classes are excluded. Royal
permits, genes with their own timers, and other separate ability systems are not
part of this feature. Targeting, component restrictions (including hemogen),
psyfocus, entropy, warmup, jobs, charge consumption, and regeneration stay native.

Ritual bypass requires a player UI interaction on a player-owned map, a currently
eligible leader/moral guide on that same map, the matching ideology, and the
matching supported source ability owned by that role holder. Right-click menus
also require an eligible selected pawn on that map. Rituals on non-player maps
are deliberately excluded. Other ritual types receive no shared-deadline bypass.

## Implementation and patch audit

Files changed:

- `src/CustomStackLimitSettings.cs`: persisted boolean, copies and verification.
- `src/RimWorldUtilitiesMod.cs`: service creation and patch installation.
- `src/UI/MainTabWindow_Utilities.cs`, `src/UI/AbilitiesPanel.cs`: section and scroll space.
- `src/Services/AbilityCooldownService.cs`: settings and centralized live eligibility.
- `src/Services/RitualCooldownService.cs`: contextual ritual ownership and confirmation.
- `src/Patches/AbilityCooldownPatch.cs`, `src/Patches/RitualCooldownPatch.cs`: hooks below.
- `tests/RimWorldUtilities.Tests/AbilityCooldownChecks.cs`, `Program.cs`: regression checks.
- `README.md`, `tests/README.md`, this report and `docs/game-api-notes.md`: scope and research.

All patches use Harmony owner `slava.rimworldutilities.abilities.cooldowns`.

| Exact target | Intervention | NPC/shared-state audit |
| --- | --- | --- |
| `RimWorld.Ability.get_CanCast()` | Transpile exactly one `inCooldown` field read into an effective cooldown check. | Rechecks settings, supported type and pawn eligibility at the read. NPCs get the original field value. Component and environment checks remain intact. |
| `RimWorld.Ability.get_OnCooldown()` | Postfix returns false only for an eligible supported ability. | No fields changed; NPC/unsupported results pass through. |
| `RimWorld.Ability.get_CooldownTicksRemaining()` | Postfix returns zero only for an eligible supported ability. | Updates the normal ability indicator without changing stored deadlines. NPC/unsupported results pass through. |
| `RimWorld.Ability.CooldownTick()` | Transpile exactly one remaining-time getter call. For eligible abilities, use the raw timer for native expiry; otherwise call the original getter. | Prevents the display override from prematurely clearing stored state. Native notifications and charge logic execute as before. NPCs/unsupported abilities retain the getter path, including other mods' hooks. |
| `RimWorld.Command_Ritual.ValidateDisabledState()` | Transpile both shared-deadline reads into player-command-context reads. | Requires player map and eligible current role holder. Does not change the precept. All native validation and source-ability checks execute. |
| `MoveNext()` for `FloatMenuOptionProvider_StartRitual.GetOptionsFor(Thing, FloatMenuContext)` | Transpile all four shared-deadline reads with clicked-target and selected-pawn context. | Also verifies selected pawn. No iterator-wide/global bypass scope. Native options, targeting and restrictions are retained. |
| `Precept_Ritual.GetRitualBeginWindow(TargetInfo, RitualObligation, Action, Pawn, Dictionary<string, Pawn>, Pawn)` | Postfix wraps the action of a dialog opened in a verified, enabled player context. | Revalidates current assigned cooldown-role holders before invoking the original callback. A stale dialog cannot lend a bypass to an NPC role assignment. NPC-created dialogs and direct worker invocations are unchanged. |

Transpilers are necessary because `CanCast` reads a private field directly and
ritual UI methods read a public shared field without an overridable getter.
Overriding the entire result would skip unrelated failures. The tick adjustment
is necessary to keep presentation-only remaining time from expiring raw state.
Each rewrite checks its exact count and rejects duplicate/ambiguous patterns.
The iterator is resolved using Harmony state-machine metadata; live input fields
are resolved by type and actual reads, without generated method/field names.
Installation failure disables the whole feature and removes its hooks. A later
rewrite failure disables all bypasses and logs a warning.

### Execution and isolation

1. Ability commands, cast-job failure checks and AI eligibility consult the
   patched base `CanCast`. Only its cooldown branch changes; `Psycast.CanCast`
   continues its native resource checks. `CanQueueCast` still protects jobs.
2. `PreActivate`, `StartCooldown(int)`, `Notify_GroupStartedCooldown`, cast-job
   finish actions and `Verb_AbilityShoot` still assign native cooldowns. No
   definition, ability or precept timer is reset by the mod.
3. Altar commands and right-click options read an effective shared deadline only
   within their verified player interaction. The source ability uses the same
   pawn-specific ordinary ability policy, so both cooldown gates are addressed.
4. A verified player dialog checks its actual assigned leader/moral guide again
   immediately before calling the original action. Disabling while it is open
   restores raw cooldown gates. Other non-cooldown failures still reach native
   validation. A failed recheck asks the user to reopen the dialog.
5. The original dialog action calls `RitualBehaviorWorker.TryExecuteOn(...,
   playerForced: true)`. The inspected implementation validates
   `CanStartRitualNow` and `CanExecuteOn`, **but contains no shared or source-ability
   cooldown check**. It creates the lord, prepares participants, notifies group
   abilities and sets the shared precept deadline. This worker is not patched;
   direct/NPC invocations retain their exact original behavior. There is no
   underlying global cooldown bypass that another invocation can inherit.
6. `Notify_CooldownFromAbilityStarted(int)` always writes its original deadline,
   including after a player action while enabled. NPCs sharing that **same Ideo
   and Precept_Ritual object** still see it. Role-ability objects can also be
   reused after reassignment; preserving their raw timers avoids transferring a
   cleared timer to an NPC. All mutations remain native.
7. Game serialization still saves native ability fields and the original shared
   ritual deadline. Only the checkbox is new persisted data. No restoration list,
   temporary shared-state mutation, or bypass flag survives a callback/save/load.

The mod never patches repeat quality, `lastFinishedTick`, obligations, ritual
history, outcomes, role requirements, conversion effects, duration, or concurrent
ritual checks. The 20-day repeat-quality warning/bar and penalty remain visible.
More frequent player use can still update the shared deadline through the game's
normal notifications; suppressing those would incorrectly relax NPC cooldowns.

## Validation

The Release build succeeds with zero warnings/errors. The core suite passes 988
assertions, including the new cooldown checks and actual local assembly IL checks.
The optional native run could not load `Brutal.Core.Logging.dll`: the configured
`RIMWORLD_GAME_DIR` does not exist. Native Harmony execution, Scribe/fresh-process
checks and complete gameplay are therefore not claimed as validated here.

The added optional checks exercise real cooldown reads/ticks, faction changes,
charge/external exclusions, shared-ideology ownership, stale assignments and
settings round-trips. They do not run complete cast jobs, ritual dialogs or
ritual lords. Other mods that replace these methods or add independent timers
need in-game verification. Source-level preservation and a passing build cannot
establish complete runtime correctness.

## Manual checks in a disposable existing save

1. Start with the checkbox off. Use an ability and observe normal cooldowns.
   Enable while paused with an active timer: availability and its countdown bar
   should update. Finish normal casts and reuse the ability. Check psyfocus,
   entropy, hemogen, targeting and warmup still restrict use; test a charge ability
   and an external-timer ability to confirm their native behavior.
2. Test matching abilities on enemy and neutral pawns. Test prisoners, visitors,
   controlled slaves, quest lodgers and mental states. Their timers must remain
   native. Transfer a pawn's faction during a cooldown and check its stored timer
   reappears immediately. Despawn/send a colonist into a caravan and return them.
3. Perform Leader Speech and Conversion through both altar commands and
   right-click menus with existing precept/source-ability cooldowns. Repeat after
   completion: allowed while enabled, with the repeat-quality penalty intact.
   Check required participants, guide/leader roles, conversion outcomes and
   concurrent-ritual restrictions. Also test the role-holder's ability command.
4. Keep a ritual dialog open, then disable the feature, change role holder or
   change that pawn's faction. Confirming must not grant a stale cooldown bypass;
   reopen after correcting eligibility. Reassign a role normally and check its
   reused source ability retains its stored cooldown when the new holder is NPC.
5. Use a second faction with the **same ideology object**. Compare its raw
   precept deadline and source ability before/after player interactions: both
   remain stored and NPC paths get native results. Repeat on two player maps,
   with the role holder on each map, and on an NPC map (excluded).
6. Save/reload with the checkbox enabled and with it disabled. Check the global
   setting and stored timers persist. Disable during an existing cooldown: its
   remaining native time must reappear. Check Utilities scrolling and the single
   new checkbox at narrow window sizes.

No game launch, deployment, configuration change, commit, or push is part of this implementation.
