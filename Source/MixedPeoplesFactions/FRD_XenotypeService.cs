using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public sealed class FRD_XenotypeChoice
    {
        public XenotypeDef Xenotype { get; }
        public CustomXenotype CustomXenotype { get; }
        public string Key { get; }

        public bool IsCustom => CustomXenotype != null;

        public string Label => IsCustom
            ? (!CustomXenotype.name.NullOrEmpty()
                ? CustomXenotype.name
                : !CustomXenotype.fileName.NullOrEmpty() ? CustomXenotype.fileName : Key).CapitalizeFirst()
            : Xenotype?.LabelCap.ToString();

        public bool CanGenerateAsCombatant => !IsCustom
            ? Xenotype?.canGenerateAsCombatant == true
            : CustomXenotype.genes.NullOrEmpty()
                || CustomXenotype.genes.All(gene => gene == null || !gene.disabledWorkTags.HasFlag(WorkTags.Violent));

        internal FRD_XenotypeChoice(XenotypeDef xenotype, CustomXenotype customXenotype, string key)
        {
            Xenotype = xenotype;
            CustomXenotype = customXenotype;
            Key = key;
        }
    }

    public static class FRD_XenotypeService
    {
        private const string CustomKeyPrefix = "FRD_CustomXenotype_";
        private static readonly Dictionary<ThingDef, List<XenotypeDef>> allowedByRace = new Dictionary<ThingDef, List<XenotypeDef>>();
        private static readonly Dictionary<ThingDef, List<FRD_XenotypeChoice>> choicesByRace = new Dictionary<ThingDef, List<FRD_XenotypeChoice>>();
        private static readonly Dictionary<ThingDef, HashSet<XenotypeDef>> nativeByRace = new Dictionary<ThingDef, HashSet<XenotypeDef>>();
        private static readonly HashSet<XenotypeDef> claimedByNonHumanRace = new HashSet<XenotypeDef>();
        private static List<CustomXenotype> savedCustomSource;
        private static int savedCustomCount = -1;
        private static List<FRD_XenotypeChoice> customChoices = new List<FRD_XenotypeChoice>();
        private static bool associationsBuilt;

        public static void ClearCaches()
        {
            allowedByRace.Clear();
            choicesByRace.Clear();
            nativeByRace.Clear();
            claimedByNonHumanRace.Clear();
            savedCustomSource = null;
            savedCustomCount = -1;
            customChoices.Clear();
            associationsBuilt = false;
            FRD_HarCompatibility.ClearCaches();
        }

        public static IReadOnlyList<XenotypeDef> GetAllowedXenotypes(ThingDef race)
        {
            if (!ModsConfig.BiotechActive || race == null)
            {
                return Array.Empty<XenotypeDef>();
            }
            if (allowedByRace.TryGetValue(race, out List<XenotypeDef> cached))
            {
                return cached;
            }

            EnsureAssociations();
            List<XenotypeDef> allowed = DefDatabase<XenotypeDef>.AllDefsListForReading
                .Where(xenotype => IsSelectableForRace(xenotype, race, false))
                .OrderBy(xenotype => xenotype.LabelCap.ToString(), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(xenotype => xenotype.defName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            allowedByRace[race] = allowed;
            return allowed;
        }

        public static IReadOnlyList<FRD_XenotypeChoice> GetAllowedChoices(ThingDef race)
        {
            if (!ModsConfig.BiotechActive || race?.race?.Humanlike != true)
            {
                return Array.Empty<FRD_XenotypeChoice>();
            }

            EnsureCustomChoices();
            if (choicesByRace.TryGetValue(race, out List<FRD_XenotypeChoice> cached))
            {
                return cached;
            }

            List<FRD_XenotypeChoice> choices = GetAllowedXenotypes(race)
                .Select(xenotype => new FRD_XenotypeChoice(xenotype, null, KeyFor(xenotype)))
                .Concat(customChoices)
                .ToList();
            choicesByRace[race] = choices;
            return choices;
        }

        public static bool IsCompatibleWithRace(XenotypeDef xenotype, ThingDef race, bool requireCombatant)
        {
            return ModsConfig.BiotechActive
                && xenotype != null
                && race != null
                && (!requireCombatant || xenotype.canGenerateAsCombatant)
                && FRD_HarCompatibility.CanUseXenotype(xenotype, race);
        }

        public static bool IsSafeForRace(XenotypeDef xenotype, ThingDef race, bool requireCombatant)
        {
            return IsSelectableForRace(xenotype, race, requireCombatant);
        }

        public static Dictionary<string, float> BuildDefaultWeights(FactionDef faction, ThingDef race)
        {
            Dictionary<string, float> result = new Dictionary<string, float>();
            if (!ModsConfig.BiotechActive || race == null)
            {
                return result;
            }

            if (ReferenceEquals(race, ThingDefOf.Human))
            {
                AddFilteredWeights(result, MPF_Injector.GetBaselineWeights(faction), race);
                if (ActiveWeightTotal(result, race, true) > 0f)
                {
                    return result;
                }
            }
            else
            {
                XenotypeSet pawnKindSet = FRD_RaceRegistry.GetKinds(race)
                    .Where(kind => kind?.xenotypeSet != null)
                    .OrderByDescending(kind => ReferenceEquals(kind.modContentPack, race.modContentPack))
                    .ThenBy(kind => kind.defName, StringComparer.OrdinalIgnoreCase)
                    .Select(kind => kind.xenotypeSet)
                    .FirstOrDefault(set => SetHasSafePositiveEntry(set, race));
                AddWeightsFromSet(result, pawnKindSet, race);
                if (ActiveWeightTotal(result, race, true) > 0f)
                {
                    return result;
                }

                FactionDef nativeFaction = FRD_FactionRegistry.Records
                    .Where(record => record?.Def != null
                        && record.SourceKinds.Any(kind => ReferenceEquals(kind.race, race))
                        && record.Def.xenotypeSet != null)
                    .OrderByDescending(record => ReferenceEquals(FRD_RaceRegistry.DefaultRaceFor(record.Def), race))
                    .ThenByDescending(record => ReferenceEquals(record.Def.modContentPack, race.modContentPack))
                    .ThenBy(record => record.Label, StringComparer.CurrentCultureIgnoreCase)
                    .Select(record => record.Def)
                    .FirstOrDefault(candidate => HasSafePositiveWeight(MPF_Injector.GetBaselineWeights(candidate), race));
                if (nativeFaction != null)
                {
                    AddFilteredWeights(result, MPF_Injector.GetBaselineWeights(nativeFaction), race);
                }
                if (ActiveWeightTotal(result, race, true) > 0f)
                {
                    return result;
                }
            }

            List<XenotypeDef> sameSource = GetAllowedXenotypes(race)
                .Where(xenotype => ReferenceEquals(xenotype.modContentPack, race.modContentPack))
                .ToList();
            if (sameSource.Count > 0)
            {
                foreach (XenotypeDef xenotype in sameSource)
                {
                    result[KeyFor(xenotype)] = 1f;
                }
                return result;
            }

            XenotypeDef fallback = GetAllowedXenotypes(race).FirstOrDefault();
            if (fallback != null)
            {
                result[KeyFor(fallback)] = 100f;
            }
            return result;
        }

        public static void EnsureAllowedKeys(RaceXenotypeSettings settings, ThingDef race)
        {
            if (settings == null || race == null)
            {
                return;
            }
            settings.weights = settings.weights ?? new Dictionary<string, float>();
            HashSet<string> allowedKeys = new HashSet<string>(GetAllowedChoices(race).Select(choice => choice.Key));
            foreach (string staleKey in settings.weights.Keys.Where(key => !allowedKeys.Contains(key) && !IsCustomKey(key)).ToList())
            {
                settings.weights.Remove(staleKey);
            }
            foreach (string key in GetAllowedXenotypes(race).Select(KeyFor))
            {
                if (!settings.weights.ContainsKey(key))
                {
                    settings.weights[key] = 0f;
                }
            }
        }

        public static float ActiveWeightTotal(Dictionary<string, float> weights, ThingDef race, bool requireCombatant)
        {
            if (weights == null || race == null || !ModsConfig.BiotechActive)
            {
                return 0f;
            }
            return GetAllowedChoices(race)
                .Where(choice => !requireCombatant || choice.CanGenerateAsCombatant)
                .Sum(choice => GetWeight(weights, choice.Key));
        }

        public static XenotypeDef SelectWeighted(Dictionary<string, float> weights, ThingDef race, bool requireCombatant)
        {
            List<KeyValuePair<XenotypeDef, float>> choices = GetAllowedXenotypes(race)
                .Where(xenotype => !requireCombatant || xenotype.canGenerateAsCombatant)
                .Select(xenotype => new KeyValuePair<XenotypeDef, float>(xenotype, GetWeight(weights, KeyFor(xenotype))))
                .Where(choice => choice.Value > 0f)
                .ToList();

            float total = choices.Sum(choice => choice.Value);
            if (total <= 0f)
            {
                return null;
            }
            float pick = Rand.Value * total;
            foreach (KeyValuePair<XenotypeDef, float> choice in choices)
            {
                pick -= choice.Value;
                if (pick <= 0f)
                {
                    return choice.Key;
                }
            }
            return choices[choices.Count - 1].Key;
        }

        public static FRD_XenotypeChoice SelectWeightedChoice(Dictionary<string, float> weights, ThingDef race, bool requireCombatant)
        {
            List<KeyValuePair<FRD_XenotypeChoice, float>> choices = GetAllowedChoices(race)
                .Where(choice => !requireCombatant || choice.CanGenerateAsCombatant)
                .Select(choice => new KeyValuePair<FRD_XenotypeChoice, float>(choice, GetWeight(weights, choice.Key)))
                .Where(choice => choice.Value > 0f)
                .ToList();

            float total = choices.Sum(choice => choice.Value);
            if (total <= 0f)
            {
                return null;
            }
            float pick = Rand.Value * total;
            foreach (KeyValuePair<FRD_XenotypeChoice, float> choice in choices)
            {
                pick -= choice.Value;
                if (pick <= 0f)
                {
                    return choice.Key;
                }
            }
            return choices[choices.Count - 1].Key;
        }

        public static string KeyFor(XenotypeDef xenotype)
        {
            return xenotype == XenotypeDefOf.Baseliner ? MPF_Settings.BaselinerKey : xenotype?.defName;
        }

        public static string KeyFor(CustomXenotype xenotype)
        {
            if (xenotype == null)
            {
                return null;
            }

            string identity = !xenotype.fileName.NullOrEmpty()
                ? "file|" + xenotype.fileName.Trim().ToUpperInvariant()
                : "value|" + xenotype.name + "|" + xenotype.inheritable + "|" + string.Join("|", (xenotype.genes ?? new List<GeneDef>())
                    .Where(gene => gene != null)
                    .Select(gene => gene.defName)
                    .OrderBy(defName => defName, StringComparer.Ordinal));
            return CustomKeyPrefix + StableHash(identity).ToString("X16");
        }

        public static bool IsCustomKey(string key)
        {
            return key != null && key.StartsWith(CustomKeyPrefix, StringComparison.Ordinal);
        }

        private static bool IsSelectableForRace(XenotypeDef xenotype, ThingDef race, bool requireCombatant)
        {
            if (!IsCompatibleWithRace(xenotype, race, requireCombatant))
            {
                return false;
            }
            EnsureAssociations();
            if (ReferenceEquals(race, ThingDefOf.Human))
            {
                return !claimedByNonHumanRace.Contains(xenotype);
            }
            return nativeByRace.TryGetValue(race, out HashSet<XenotypeDef> native) && native.Contains(xenotype);
        }

        private static void EnsureAssociations()
        {
            if (associationsBuilt || !ModsConfig.BiotechActive)
            {
                return;
            }
            associationsBuilt = true;

            foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces.Where(race => !ReferenceEquals(race, ThingDefOf.Human)))
            {
                HashSet<XenotypeDef> native = new HashSet<XenotypeDef>();
                foreach (PawnKindDef kind in FRD_RaceRegistry.GetKinds(race))
                {
                    AddFromSet(native, kind?.xenotypeSet, race);
                }
                foreach (FRD_FactionRecord record in FRD_FactionRegistry.Records.Where(record => record?.Def != null && record.SourceKinds.Any(kind => ReferenceEquals(kind.race, race))))
                {
                    AddFromWeights(native, MPF_Injector.GetBaselineWeights(record.Def), race);
                }
                foreach (XenotypeDef xenotype in DefDatabase<XenotypeDef>.AllDefsListForReading.Where(xenotype => xenotype != null && ReferenceEquals(xenotype.modContentPack, race.modContentPack)))
                {
                    if (IsCompatibleWithRace(xenotype, race, false))
                    {
                        native.Add(xenotype);
                    }
                }
                foreach (XenotypeDef xenotype in FRD_CompatibilityRegistry.GetExplicitXenotypes(race))
                {
                    if (xenotype != null && IsCompatibleWithRace(xenotype, race, false))
                    {
                        native.Add(xenotype);
                    }
                }
                foreach (XenotypeDef xenotype in FRD_HarCompatibility.GetExplicitXenotypes(race))
                {
                    if (!IsOfficialXenotype(xenotype) && IsCompatibleWithRace(xenotype, race, false))
                    {
                        native.Add(xenotype);
                    }
                }
                IReadOnlyCollection<XenotypeDef> excluded = FRD_CompatibilityRegistry.GetExcludedXenotypes(race);
                native.ExceptWith(excluded);
                if (native.Count == 0
                    && !excluded.Contains(XenotypeDefOf.Baseliner)
                    && IsCompatibleWithRace(XenotypeDefOf.Baseliner, race, false))
                {
                    native.Add(XenotypeDefOf.Baseliner);
                }
                nativeByRace[race] = native;
                claimedByNonHumanRace.UnionWith(native.Where(xenotype => xenotype != XenotypeDefOf.Baseliner));
            }
        }

        private static bool IsOfficialXenotype(XenotypeDef xenotype)
        {
            string packageId = xenotype?.modContentPack?.PackageIdPlayerFacing;
            return string.IsNullOrEmpty(packageId) || packageId.StartsWith("Ludeon.RimWorld", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureCustomChoices()
        {
            List<CustomXenotype> saved = null;
            try
            {
                saved = CharacterCardUtility.CustomXenotypesForReading;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[FactionRaceDiversity] Player-created xenotype presets could not be loaded. " + exception, 174832504);
            }

            int savedCount = saved?.Count ?? 0;
            if (ReferenceEquals(saved, savedCustomSource)
                && savedCount == savedCustomCount)
            {
                return;
            }

            savedCustomSource = saved;
            savedCustomCount = savedCount;

            Dictionary<string, FRD_XenotypeChoice> discovered = new Dictionary<string, FRD_XenotypeChoice>();
            foreach (CustomXenotype custom in saved ?? new List<CustomXenotype>())
            {
                string key = KeyFor(custom);
                if (custom != null && key != null && !discovered.ContainsKey(key))
                {
                    discovered[key] = new FRD_XenotypeChoice(null, custom, key);
                }
            }

            customChoices = discovered.Values
                .OrderBy(choice => choice.Label, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(choice => choice.Key, StringComparer.Ordinal)
                .ToList();
            choicesByRace.Clear();
        }

        private static ulong StableHash(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (char character in value ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }
            return hash;
        }

        private static void AddFromSet(HashSet<XenotypeDef> target, XenotypeSet set, ThingDef race)
        {
            if (set == null)
            {
                return;
            }
            for (int i = 0; i < set.Count; i++)
            {
                XenotypeChance chance = set[i];
                XenotypeDef xenotype = chance?.xenotype;
                if (xenotype != null && chance.chance > 0f && IsCompatibleWithRace(xenotype, race, false))
                {
                    target.Add(xenotype);
                }
            }
            if (set.BaselinerChance > 0f && IsCompatibleWithRace(XenotypeDefOf.Baseliner, race, false))
            {
                target.Add(XenotypeDefOf.Baseliner);
            }
        }

        private static void AddFromWeights(HashSet<XenotypeDef> target, Dictionary<string, float> weights, ThingDef race)
        {
            foreach (XenotypeDef xenotype in DefDatabase<XenotypeDef>.AllDefsListForReading)
            {
                if (xenotype != null && GetWeight(weights, KeyFor(xenotype)) > 0f && IsCompatibleWithRace(xenotype, race, false))
                {
                    target.Add(xenotype);
                }
            }
        }

        private static void AddWeightsFromSet(Dictionary<string, float> target, XenotypeSet set, ThingDef race)
        {
            if (set == null)
            {
                return;
            }
            for (int i = 0; i < set.Count; i++)
            {
                XenotypeChance chance = set[i];
                if (chance?.xenotype != null && chance.chance > 0f && IsSelectableForRace(chance.xenotype, race, false))
                {
                    target[KeyFor(chance.xenotype)] = chance.chance * 100f;
                }
            }
            if (set.BaselinerChance > 0f && IsSelectableForRace(XenotypeDefOf.Baseliner, race, false))
            {
                target[MPF_Settings.BaselinerKey] = set.BaselinerChance * 100f;
            }
        }

        private static void AddFilteredWeights(Dictionary<string, float> target, Dictionary<string, float> source, ThingDef race)
        {
            if (source == null)
            {
                return;
            }
            foreach (XenotypeDef xenotype in GetAllowedXenotypes(race))
            {
                string key = KeyFor(xenotype);
                float weight = GetWeight(source, key);
                if (weight > 0f)
                {
                    target[key] = weight;
                }
            }
        }

        private static bool SetHasSafePositiveEntry(XenotypeSet set, ThingDef race)
        {
            Dictionary<string, float> weights = new Dictionary<string, float>();
            AddWeightsFromSet(weights, set, race);
            return ActiveWeightTotal(weights, race, false) > 0f;
        }

        private static bool HasSafePositiveWeight(Dictionary<string, float> weights, ThingDef race)
        {
            return ActiveWeightTotal(weights, race, false) > 0f;
        }

        private static float GetWeight(Dictionary<string, float> weights, string key)
        {
            if (weights == null || key == null || !weights.TryGetValue(key, out float value) || float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }
            return Math.Max(0f, value);
        }
    }
}
