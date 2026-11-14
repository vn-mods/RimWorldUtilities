# One-hour biosculpter cycles

Open **Utilities → Abilities → Fast Biosculpter Cycles (1 Hour)**. This independent,
default-off setting requires Ideology. It uses the existing global mod settings
file and verified save-before-apply behavior; it is not stored per colony.

Medic, bioregeneration, age reversal and pleasure target **2,500 powered processing
ticks**, or one in-game hour. The rate is the current cycle's configured
`durationDays * 60000 / 2500`: vanilla rates are respectively 144, 600, 192 and 96.
Fast mode deliberately overrides cleanliness, ideology and biotuning speed
modifiers. Disabling immediately restores the native calculation, including those
modifiers and other mods' contributions.

When enabled at successful Age Reversal completion, the same setting also halves
biological age in ticks. Medic, bioregeneration and pleasure retain their existing
effects. Ordinary humans have a 13-year lower limit; a higher applicable native
minimum is respected. The result can never increase the original age or replace
a stronger native rejuvenation with an older age:

```text
halvingTarget = max(13 years, native minimum, original biological ticks / 2)
finalAge = min(original biological ticks, native treatment result, halvingTarget)
```

Thus 80/60/40/30/26/20/13-year humans become 40/30/20/15/13/13/13 respectively.
Fractional ages retain tick precision; an odd tick count rounds down by half a tick.
Pawns already below the minimum are never aged up by the enhancement. Chronological
age is unchanged. No life-stage definitions, genes or health conditions are edited.

## Eligibility and runtime behavior

Every speed query requires a living free humanlike colonist of the actual player
faction, a spawned player-owned pod, and a map whose `ParentFaction` is the player.
The queried pawn must be the current occupant and actually held in that pod's
container. Despawned and suspended occupants qualify. Prisoners, slaves, active
guests, hosted pawns, quest lodgers/quest-hosted pawns, mental states, mechanoids,
other NPCs and unverified custom cycle classes are excluded. Downed free colonists
can qualify. Ownership and status changes are checked live.

Enabling during treatment accelerates the remaining fraction of work; it does not
restart a one-hour countdown. Disabling, losing eligibility or transferring pod
ownership preserves all earned work and returns future processing to native speed.
Power outages still pause work and advance the native cumulative outage counter.
Preparation, hauling, entry delays and interruptions retain their native timing.
The age enhancement checks the setting and the same eligibility policy again at
completion, including the exact vanilla component type, `ageReversal` key and
current pod context.
Toggling during treatment never changes age immediately; disabling before completion
uses vanilla rejuvenation. Previously completed rejuvenation is never undone.
No original-age snapshot is stored in a save: the original age is read immediately
before vanilla assigns the new age. Interrupted treatment does not run this handler.

Active-cycle remaining-time estimates use the same effective speed as processing.
Empty-pod tooltips and entry previews retain native estimates because the pawn is
not yet contained; the checkbox tooltip explains this distinction. As in vanilla,
an unpowered pod's estimate represents processing time rather than time including
an unknown future outage.
The generic vanilla age-cycle description also retains its native reduction text;
the Utilities checkbox tooltip explains the additional rejuvenation.

No remaining-work fields or global cycle definitions are rewritten. Healing and
random condition selection, resources/research, biotuning ownership/timer, native
power draw, interruption effects and common completion remain vanilla. Native
age-reversal scheduling runs once using the final biological age. Shorter operation
naturally uses less total electricity and causes completion-dependent events to
happen earlier.

The original four integer-day durations have integral rates and reach the native
completion boundary in exactly 2,500 ticks in isolated tests. Fractional durations
introduced by other mods can round by a tick because vanilla stores remaining work
as a `float`. No compensating timer writes or extra completion calls are added.

## Patch and compatibility

The unchanged processing patch is a Postfix on
`float CompBiosculpterPod.GetCycleSpeedFactorForPawn(Pawn p)`, owned by
`slava.rimworldutilities.biosculpter.cycles`. Installation and availability are
independent of the cooldown patches. The service reads the current cycle's live
duration and supports the exact four vanilla component types. Invalid/nonpositive
rates and unsupported cycles retain the original result. Ineligible results are
passed through without numerical changes.

The age enhancement adds one narrow Transpiler on
`void CompBiosculpterPod_AgeReversalCycle.CycleCompleted(Pawn pawn)`, owned by
`slava.rimworldutilities.biosculpter.age`. It adjusts only the value passed to the
single native `Pawn_AgeTracker.AgeBiologicalTicks` setter. The setter still runs
once, retaining growth/life-stage recalculation, then the original
`ResetAgeReversalDemand(ViaTreatment)` and completion message observe the final age.
Chronological age and the native difficulty reduction calculation are untouched.
No whole-method replacement or global age/scheduler patch is used.

The rewrite requires exactly one setter before exactly one demand reset and rejects
duplicate rewrites. A mismatch disables only enhanced rejuvenation, retains native
age behavior and reports its unavailable status; one-hour processing remains usable.

Relevant metadata and token-resolved IL were compared between reference build
1.7.4998 and installed 1.7.5005 rev1082. The shared helper, occupied getter, tick,
current-cycle/occupant/container APIs, inspect estimate, acceptance and serialization
methods match. The cycle duration field is `float`. No reference files were changed.
The age-completion method, biological-age getter/setter, native minimum, growth and
life-stage recalculation, and demand reset also match in those two assemblies.
The native human minimum is 13: the teenager stage carries the default `Adult`
developmental flag even though the later stage labelled adult starts at 18.

Mods replacing processing or overwriting the result in a later Postfix may conflict.
Custom cycle implementations require separate verification. Loading an existing
cycle needs no migration: vanilla still saves the cycle key and remaining work.

## Validation limits and manual checks

The focused native tests execute Harmony and the native processing controller in
an isolated test process. Stat inputs, graphics and completion side effects are
isolated; this is not an in-game treatment-outcome test. Real Scribe tests retain
the cycle key and fractional work in a pod record, reattach a fixture occupant and
resume processing; they do not serialize a complete gameplay pawn or colony.
Settings are also checked across fresh test processes and with legacy XML. The
age tests execute the actual native completion method, public setter and scheduling
reset, with translations, message delivery and map/renderer-dependent life-stage
callbacks isolated. They check original/final ticks, chronological age, growth/stage
indices, one reset/notification, final-age deadlines, exclusions and toggles. Full
life-stage callback behavior and complete treatment outcomes still need in-game tests.
See [test commands](../tests/README.md).

Implementation validation: the Release build has no warnings/errors; the core
suite passes 1,071 assertions. Focused biosculpter checks pass 716 assertions with
both the reference DLLs and a temporary test output containing the installed
1.7.5005 game DLLs and installed Harmony 3. The full optional native suite stops
in the existing ability/ritual checks when `ModsConfig` encounters a null save-data
path. That same failure was reproduced from unmodified HEAD in a temporary copy;
no game configuration was changed to work around it.

In a disposable save:

1. Confirm both Abilities checkboxes start off and work independently; check the
   taller panel and status tooltips at narrow window sizes and with Ideology off.
2. Run all four cycles with steady power and compare approximately one hour of
   processing, active countdowns and progress bars. Repeat in dirty/clean rooms,
   with accelerated ideology and with/without existing biotuning.
3. Confirm native healing/condition selection, regeneration and pleasure memory.
   With fast mode enabled, check age reversal at 80, 60, 40, 30, 26, 20 and 13 years,
   plus a fractional age, unchanged chronological age and native life-stage updates.
   Disable before completion to verify vanilla reduction; repeat with a stronger
   adult-aging difficulty modifier and check the demand deadline/message against
   the final age. Check medicine/nutrition, biotuning restrictions and power draw.
4. Toggle during an active cycle and while paused; save/reload in both modes.
   Check that work never resets, completion happens once, and disabling restores
   native remaining time and speed.
5. Treat a colony prisoner in a player pod, then exercise slave, visitor, quest
   and NPC contexts. Transfer occupant faction, pod ownership and map ownership:
   excluded contexts must retain native speed.
6. Cut/reconnect power, including repeated outages; verify paused work, cumulative
   outage timing and native interruption/ejection effects. Also interrupt manually.

No deployment or game launch was performed. Passing isolated tests and a build
does not establish complete runtime correctness for the new age enhancement.
