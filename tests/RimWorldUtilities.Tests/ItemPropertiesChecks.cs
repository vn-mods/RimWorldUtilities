using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorldUtilities.Services;
using Verse;

internal static class ItemPropertiesChecks
{
    public static void RunCore(Action<bool, string> check)
    {
        var cases = new (string Text, int Maximum, bool Valid)[]
        {
            ("1", 1, true), ("1", 100, true), ("100", 100, true), (" 25 ", 100, true),
            ("0", 100, false), ("-1", 100, false), ("101", 100, false), ("", 100, false),
            ("text", 100, false), ("1.5", 100, false), ("1,5", 100, false),
            ("2147483648", int.MaxValue, false), ("1", 0, false), ("1", -1, false)
        };
        foreach (var test in cases)
        {
            var valid = ItemPropertiesService.TryParseHitPoints(test.Text, test.Maximum, out var hp, out var error);
            check(valid == test.Valid, "HP range/integer validation: " + test.Text);
            check(valid == (error.Length == 0), "HP validation message");
            check(!valid || hp >= 1 && hp <= test.Maximum, "HP stays within the current maximum");
        }

        var weapon = Item<TrackingItem>();
        check(ItemEditorService.IsOrdinaryItem(weapon), "Ordinary selection does not require stacking");
        check(!ItemEditorService.CanEditStackCount(weapon), "Non-stackable equipment cannot edit stacks");
        check(ItemPropertiesService.CanEditHitPoints(weapon), "Non-stackable equipment can edit HP");
        check(ItemPropertiesService.GetEditableQuality(weapon) != null, "Non-stackable equipment can edit quality");
        weapon.def.useHitPoints = false;
        check(!ItemPropertiesService.CanEditHitPoints(weapon), "Respect authoritative useHitPoints flag");
        check(ItemPropertiesService.GetEditableQuality(weapon) != null, "Quality eligibility is independent of HP");
        weapon.def.stackLimit = 75;
        check(ItemEditorService.CanEditStackCount(weapon), "Stackability is checked separately");

        var book = Item<TrackingBook>();
        check(ItemEditorService.IsOrdinaryItem(book) && ItemPropertiesService.CanEditHitPoints(book), "Book HP remains supported");
        check(ItemPropertiesService.GetEditableQuality(book) == null, "Book quality is excluded, including subclasses");
        var minified = Item<MinifiedThing>();
        check(!ItemEditorService.IsOrdinaryItem(minified) && !ItemPropertiesService.CanEditHitPoints(minified) &&
            ItemPropertiesService.GetEditableQuality(minified) == null, "Minified items remain unsupported");
        weapon.def.category = ThingCategory.Building;
        check(!ItemEditorService.IsOrdinaryItem(weapon), "Unsupported categories remain excluded");

        var qualities = Enum.GetValues<QualityCategory>();
        check(qualities.Length == 7 && qualities.All(ItemPropertiesService.IsValidQuality), "All seven native quality levels");
        check(!ItemPropertiesService.IsValidQuality((QualityCategory)255), "Reject invalid quality values");

        var plain = Item<TrackingItem>();
        typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(plain, null);
        plain.compQuality = null!;
        check(ItemPropertiesService.GetEditableQuality(plain) == null, "No quality component means no editable dropdown");
    }

    public static void RunNative(Action<bool, string> check)
    {
        // Native stat/comp calls only. No game, map, UI, or global definition database is started.
        var binding = typeof(DefOfHelper).GetField("bindingNow", BindingFlags.Static | BindingFlags.NonPublic)!;
        var wasBinding = binding.GetValue(null);
        try
        {
            binding.SetValue(null, true);
            RuntimeHelpers.RunClassConstructor(typeof(StatDefOf).TypeHandle);
        }
        finally
        {
            binding.SetValue(null, wasBinding);
        }

        var previousStat = StatDefOf.MaxHitPoints;
        try
        {
            var stat = (StatDef)RuntimeHelpers.GetUninitializedObject(typeof(StatDef));
            stat.immutable = true; // Avoid a TickManager; use the real worker's per-Thing cache.
            stat.cacheable = true;
            var worker = new HitPointsWorker();
            worker.InitSetStat(stat);
            worker.SetCacheability(true);
            typeof(StatDef).GetField("workerInt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(stat, worker);
            StatDefOf.MaxHitPoints = stat;

            var item = Item<TrackingItem>();
            var other = Item<TrackingItem>();
            item.HitPoints = 75;
            other.HitPoints = 60;
            Thing? current = item;
            var service = new ItemPropertiesService((Thing expected, out string error) =>
            {
                error = ReferenceEquals(expected, current) ? string.Empty : "Changed selection";
                return error.Length == 0 ? current : null;
            });

            check(item.MaxHitPoints == 100 && other.MaxHitPoints == 100, "Warm native MaxHitPoints caches");
            var temporary = (IDictionary)typeof(StatWorker)
                .GetField("temporaryStatCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(worker)!;
            var entryType = temporary.GetType().GetGenericArguments()[1];
            temporary[item] = RuntimeHelpers.GetUninitializedObject(entryType);
            temporary[other] = RuntimeHelpers.GetUninitializedObject(entryType);

            check(service.TrySetQuality(item, QualityCategory.Legendary, out _), "Apply quality through native CompQuality");
            check(item.MaxHitPoints == 200 && item.HitPoints == 75, "Recompute maximum; preserve absolute HP without healing");
            check(item.QualityCallbacks == 1, "Call native PostQualitySet exactly once");
            check(!temporary.Contains(item) && temporary.Contains(other), "Clear only target's temporary stat cache");
            check(other.MaxHitPoints == 100 && worker.Evaluations[other] == 1 && other.HitPoints == 60,
                "Other item's HP and cache remain unchanged");
            check(service.TrySetQuality(item, QualityCategory.Legendary, out _) && item.QualityCallbacks == 1,
                "Same quality performs no native setter/callback");
            check(!service.TrySetQuality(item, (QualityCategory)255, out _) && item.QualityCallbacks == 1,
                "Reject invalid quality before mutation");

            check(service.TrySetHitPoints(item, "175", out _) && item.HitPoints == 175, "Apply valid absolute HP");
            check(service.TrySetQuality(item, QualityCategory.Awful, out _) && item.MaxHitPoints == 50 && item.HitPoints == 50,
                "Quality downgrade clamps HP to the new maximum");
            check(service.TrySetQuality(item, QualityCategory.Normal, out _) && item.MaxHitPoints == 100 && item.HitPoints == 50,
                "Quality upgrade preserves damage");
            check(!service.TrySetHitPoints(item, "0", out _) && item.HitPoints == 50, "Never apply zero HP");
            check(!service.TrySetHitPoints(item, "-1", out _) && item.HitPoints == 50, "Never apply negative HP");
            check(!service.TrySetHitPoints(item, "101", out _) && item.HitPoints == 50, "Enforce current HP maximum");

            worker.BaseMaximum = 120;
            check(item.MaxHitPoints == 100, "Native cache can contain a previous maximum");
            check(service.TrySetMaxHitPoints(item, out _) && item.HitPoints == 120 && item.MaxHitPoints == 120,
                "Max forces a fresh stat calculation");

            current = other;
            var callbacks = item.QualityCallbacks;
            check(!service.TrySetHitPoints(item, "1", out _) && item.HitPoints == 120, "Changed selection rejects HP Apply");
            check(!service.TrySetMaxHitPoints(item, out _) && item.HitPoints == 120, "Changed selection rejects Max");
            check(!service.TrySetQuality(item, QualityCategory.Good, out _) && item.QualityCallbacks == callbacks,
                "Stale dropdown choice rejects quality mutation");

            var book = Item<TrackingBook>();
            current = book;
            check(!service.TrySetQuality(book, QualityCategory.Legendary, out _) && book.QualityCallbacks == 0,
                "Never invoke book regeneration callbacks");

            var noHp = Item<TrackingItem>();
            noHp.def.useHitPoints = false;
            current = noHp;
            check(service.TrySetQuality(noHp, QualityCategory.Legendary, out _) && noHp.HitPoints == 75,
                "Quality-only items do not receive HP edits");

            current = item;
            item.OnQualitySet = () => current = other;
            check(!service.TrySetQuality(item, QualityCategory.Good, out _) && item.HitPoints == 120,
                "Revalidate after native callbacks before a subsequent HP write");
        }
        finally
        {
            StatDefOf.MaxHitPoints = previousStat;
        }
    }

    private static int nextId = 10000;

    private static T Item<T>() where T : ThingWithComps
    {
        var item = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        definition.defName = typeof(T).Name;
        definition.category = ThingCategory.Item;
        definition.thingClass = typeof(T);
        definition.stackLimit = 1;
        definition.useHitPoints = true;
        item.def = definition;
        item.stackCount = 1;
        item.thingIDNumber = nextId++;
        typeof(Thing).GetField("mapIndexOrState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, (sbyte)-1);
        item.HitPoints = 75;
        var quality = new CompQuality { parent = item };
        // A real art comp without generation state also proves source:null never initializes art.
        var art = (CompArt)RuntimeHelpers.GetUninitializedObject(typeof(CompArt));
        art.parent = item;
        typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(item, new List<ThingComp> { quality, art });
        item.compQuality = quality;
        return item;
    }

    private sealed class TrackingItem : ThingWithComps
    {
        public int QualityCallbacks;
        public Action? OnQualitySet;
        public override void PostQualitySet()
        {
            QualityCallbacks++;
            OnQualitySet?.Invoke();
        }
    }

    private sealed class TrackingBook : Book
    {
        public int QualityCallbacks;
        public override void PostQualitySet() => QualityCallbacks++;
    }

    private sealed class HitPointsWorker : StatWorker
    {
        public int BaseMaximum = 100;
        public readonly Dictionary<Thing, int> Evaluations = new();
        public override float GetValueUnfinalized(StatRequest request, bool applyPostProcess = true)
        {
            Evaluations.TryGetValue(request.Thing, out var count);
            Evaluations[request.Thing] = count + 1;
            return BaseMaximum * (request.Thing.TryGetComp<CompQuality>()!.Quality switch
            {
                QualityCategory.Awful => 0.5f,
                QualityCategory.Legendary => 2f,
                _ => 1f
            });
        }

        public override void FinalizeValue(StatRequest request, ref float value, bool applyPostProcess)
        {
        }
    }
}
