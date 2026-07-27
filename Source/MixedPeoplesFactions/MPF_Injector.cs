using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public static class MPF_Injector
    {
        private sealed class XenotypeOwnership
        {
            public XenotypeSet Original;
            public XenotypeSet Applied;
        }

        private static readonly FieldInfo XenotypeChancesField = typeof(XenotypeSet).GetField("xenotypeChances", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CachedDescriptionField = typeof(FactionDef).GetField("cachedDescription", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Dictionary<FactionDef, XenotypeOwnership> OwnershipByFaction = new Dictionary<FactionDef, XenotypeOwnership>();
        private static bool reflectionWarningLogged;

        public static void CaptureBaselines()
        {
            foreach (FRD_FactionRecord record in FRD_FactionRegistry.Records)
            {
                CaptureBaseline(record.Def);
            }
        }

        public static void ApplyAll(MPF_Settings settings = null)
        {
            settings = settings ?? MPF_Mod.Settings;
            if (settings == null)
            {
                return;
            }
            CaptureBaselines();

            foreach (FRD_FactionRecord record in FRD_FactionRegistry.Records)
            {
                FactionDef faction = record.Def;
                FactionRaceSettings config = settings.GetFactionSettings(faction.defName);
                Dictionary<string, float> projectedWeights = GetProjectedFactionWeights(faction, config);
                bool shouldApply = ModsConfig.BiotechActive
                    && record.SupportsXenotypes
                    && projectedWeights != null
                    && ActiveXenotypeWeightTotal(projectedWeights, config != null && config.raceOverrideEnabled ? ThingDefOf.Human : null) > 0f;

                if (shouldApply)
                {
                    ApplyFaction(faction, projectedWeights, config != null && config.raceOverrideEnabled ? ThingDefOf.Human : null);
                }
                else
                {
                    RestoreFaction(faction);
                }
            }
        }

        public static void RestoreFaction(FactionDef faction)
        {
            if (faction == null || !OwnershipByFaction.TryGetValue(faction, out XenotypeOwnership ownership) || ownership.Applied == null)
            {
                return;
            }
            if (ReferenceEquals(faction.xenotypeSet, ownership.Applied))
            {
                faction.xenotypeSet = ownership.Original;
                ClearDescriptionCache(faction);
            }
            else
            {
                ownership.Original = faction.xenotypeSet;
            }
            ownership.Applied = null;
        }

        public static Dictionary<string, float> GetBaselineWeights(FactionDef faction)
        {
            Dictionary<string, float> weights = new Dictionary<string, float>();
            if (!ModsConfig.BiotechActive)
            {
                return weights;
            }

            CaptureBaseline(faction);
            XenotypeSet baseline = faction != null && OwnershipByFaction.TryGetValue(faction, out XenotypeOwnership ownership)
                ? ownership.Original
                : null;
            float nonBaselinerTotal = 0f;
            if (baseline != null)
            {
                for (int i = 0; i < baseline.Count; i++)
                {
                    XenotypeChance chance = baseline[i];
                    if (chance?.xenotype == null || chance.xenotype == XenotypeDefOf.Baseliner || chance.chance <= 0f)
                    {
                        continue;
                    }
                    weights[chance.xenotype.defName] = chance.chance * 100f;
                    nonBaselinerTotal += chance.chance;
                }
            }
            weights[MPF_Settings.BaselinerKey] = Math.Max(0f, 1f - nonBaselinerTotal) * 100f;
            return weights;
        }

        public static bool IsXenotypeOverrideEnabled(FactionDef faction)
        {
            if (faction == null || !ModsConfig.BiotechActive)
            {
                return false;
            }
            FactionRaceSettings config = MPF_Mod.Settings?.GetFactionSettings(faction.defName);
            return GetProjectedFactionWeights(faction, config) != null;
        }

        public static XenotypeDef FindSafeFallbackXenotype(FactionDef faction, ThingDef race)
        {
            if (!ModsConfig.BiotechActive || race == null)
            {
                return null;
            }

            FactionRaceSettings config = MPF_Mod.Settings?.GetFactionSettings(faction?.defName);
            RaceXenotypeSettings racePool = config?.GetRaceXenotypeSettings(race.defName);
            XenotypeDef configured = FRD_XenotypeService.SelectWeighted(racePool?.weights, race, false);
            if (configured != null)
            {
                return configured;
            }

            CaptureBaseline(faction);
            List<XenotypeDef> candidates = new List<XenotypeDef>();
            if (faction != null && OwnershipByFaction.TryGetValue(faction, out XenotypeOwnership ownership) && ownership.Original != null)
            {
                for (int i = 0; i < ownership.Original.Count; i++)
                {
                    XenotypeDef xenotype = ownership.Original[i]?.xenotype;
                    if (xenotype != null && !candidates.Contains(xenotype))
                    {
                        candidates.Add(xenotype);
                    }
                }
            }
            if (XenotypeDefOf.Baseliner != null && !candidates.Contains(XenotypeDefOf.Baseliner))
            {
                candidates.Add(XenotypeDefOf.Baseliner);
            }
            return candidates.FirstOrDefault(xenotype => FRD_XenotypeService.IsSafeForRace(xenotype, race, false))
                ?? FRD_XenotypeService.GetAllowedXenotypes(race).FirstOrDefault();
        }

        public static bool ValidateSettingsAndRebuild()
        {
            MPF_Settings settings = MPF_Mod.Settings;
            if (settings == null)
            {
                return false;
            }
            bool valid = settings.AllStoredWeightsAreValid();
            settings.NormalizeAllSettings();
            ApplyAll(settings);
            return valid;
        }

        public static string BuildDebugReport()
        {
            MPF_Settings settings = MPF_Mod.Settings;
            List<string> lines = new List<string>
            {
                "FRD_DebugReportHeader".Translate().ToString(),
                "Factions discovered: " + FRD_FactionRegistry.Records.Count.ToString(CultureInfo.InvariantCulture),
                "Humanlike races discovered: " + FRD_RaceRegistry.HumanlikeRaces.Count.ToString(CultureInfo.InvariantCulture)
            };
            if (settings == null || settings.factionSettings == null)
            {
                lines.Add("No settings loaded.");
                return string.Join("\n", lines);
            }

            int configuredCount = 0;
            foreach (KeyValuePair<string, FactionRaceSettings> pair in settings.factionSettings.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                FactionRaceSettings config = pair.Value;
                if (!MPF_Settings.IsActivelyConfigured(config))
                {
                    continue;
                }

                configuredCount++;
                FRD_FactionRecord record = FRD_FactionRegistry.Get(pair.Key);
                FactionDef faction = record?.Def;
                lines.Add((faction?.LabelCap.ToString() ?? pair.Key) + " [" + pair.Key + "]");
                lines.Add("  legacyXenotypeOverride=" + config.xenotypeOverrideEnabled + ", raceOverride=" + config.raceOverrideEnabled);
                if (!config.raceOverrideEnabled && config.xenotypeOverrideEnabled)
                {
                    lines.Add("  faction xenotypes: " + FormatWeights(config.xenotypeWeights));
                }
                if (config.raceOverrideEnabled)
                {
                    lines.Add("  races: " + FormatWeights(config.raceWeights));
                    if (config.xenotypeSettingsByRace != null)
                    {
                        foreach (KeyValuePair<string, RaceXenotypeSettings> pool in config.xenotypeSettingsByRace.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            if (pool.Value != null && pool.Value.overrideEnabled)
                            {
                                lines.Add("    " + pool.Key + " xenotypes: " + FormatWeights(pool.Value.weights));
                            }
                        }
                    }
                }
                if (faction != null && IsXenotypeOverrideEnabled(faction))
                {
                    AppendRuntimeXenotypeReport(lines, faction);
                }
                lines.AddRange(FRD_Diagnostics.FormatLines(pair.Key));
            }
            lines.Add("Configured factions: " + configuredCount.ToString(CultureInfo.InvariantCulture));
            return string.Join("\n", lines);
        }

        private static Dictionary<string, float> GetProjectedFactionWeights(FactionDef faction, FactionRaceSettings config)
        {
            if (config == null || !ModsConfig.BiotechActive)
            {
                return null;
            }
            if (!config.raceOverrideEnabled)
            {
                return config.xenotypeOverrideEnabled ? config.xenotypeWeights : null;
            }

            RaceXenotypeSettings humanPool = config.GetRaceXenotypeSettings(ThingDefOf.Human.defName);
            return humanPool != null && humanPool.overrideEnabled ? humanPool.weights : null;
        }

        private static void CaptureBaseline(FactionDef faction)
        {
            if (faction != null && !OwnershipByFaction.ContainsKey(faction))
            {
                OwnershipByFaction[faction] = new XenotypeOwnership { Original = faction.xenotypeSet };
            }
        }

        private static void ApplyFaction(FactionDef faction, Dictionary<string, float> weights, ThingDef filterRace)
        {
            if (faction == null || weights == null || XenotypeChancesField == null)
            {
                if (XenotypeChancesField == null)
                {
                    LogReflectionWarning();
                }
                return;
            }

            CaptureBaseline(faction);
            XenotypeOwnership ownership = OwnershipByFaction[faction];
            if (ownership.Applied != null && !ReferenceEquals(faction.xenotypeSet, ownership.Applied))
            {
                ownership.Original = faction.xenotypeSet;
                ownership.Applied = null;
            }

            XenotypeSet applied = CreateXenotypeSet(weights, filterRace);
            ownership.Applied = applied;
            faction.xenotypeSet = applied;
            ClearDescriptionCache(faction);
        }

        private static XenotypeSet CreateXenotypeSet(Dictionary<string, float> weights, ThingDef filterRace)
        {
            float total = ActiveXenotypeWeightTotal(weights, filterRace);
            List<XenotypeChance> chances = new List<XenotypeChance>();
            if (total > 0f)
            {
                foreach (XenotypeDef xenotype in DefDatabase<XenotypeDef>.AllDefsListForReading)
                {
                    if (xenotype == null || xenotype == XenotypeDefOf.Baseliner || (filterRace != null && !FRD_XenotypeService.IsSafeForRace(xenotype, filterRace, false)))
                    {
                        continue;
                    }
                    float weight = GetWeight(weights, xenotype.defName);
                    if (weight > 0f)
                    {
                        chances.Add(new XenotypeChance(xenotype, weight / total));
                    }
                }
            }
            XenotypeSet set = new XenotypeSet();
            XenotypeChancesField.SetValue(set, chances);
            return set;
        }

        private static float ActiveXenotypeWeightTotal(Dictionary<string, float> weights, ThingDef filterRace = null)
        {
            float total = filterRace == null || FRD_XenotypeService.IsSafeForRace(XenotypeDefOf.Baseliner, filterRace, false)
                ? GetWeight(weights, MPF_Settings.BaselinerKey)
                : 0f;
            if (ModsConfig.BiotechActive)
            {
                foreach (XenotypeDef xenotype in DefDatabase<XenotypeDef>.AllDefsListForReading)
                {
                    if (xenotype != null
                        && xenotype != XenotypeDefOf.Baseliner
                        && (filterRace == null || FRD_XenotypeService.IsSafeForRace(xenotype, filterRace, false)))
                    {
                        total += GetWeight(weights, xenotype.defName);
                    }
                }
            }
            return total;
        }

        private static float GetWeight(Dictionary<string, float> weights, string key)
        {
            return weights != null && key != null && weights.TryGetValue(key, out float value) && !float.IsNaN(value) && !float.IsInfinity(value)
                ? Math.Max(0f, value)
                : 0f;
        }

        private static void ClearDescriptionCache(FactionDef faction)
        {
            if (faction != null && CachedDescriptionField != null)
            {
                CachedDescriptionField.SetValue(faction, null);
            }
        }

        private static string FormatWeights(Dictionary<string, float> weights)
        {
            if (weights == null)
            {
                return "(none)";
            }
            return string.Join(", ", weights
                .Where(pair => pair.Value > 0f && !float.IsNaN(pair.Value) && !float.IsInfinity(pair.Value))
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + "=" + pair.Value.ToString("0.##", CultureInfo.InvariantCulture)));
        }

        private static void AppendRuntimeXenotypeReport(List<string> lines, FactionDef faction)
        {
            lines.Add("  runtime Human projection Baseliner=" + faction.BaselinerChance.ToStringPercent());
            if (faction.xenotypeSet == null)
            {
                return;
            }
            for (int i = 0; i < faction.xenotypeSet.Count; i++)
            {
                XenotypeChance chance = faction.xenotypeSet[i];
                if (chance?.xenotype != null)
                {
                    lines.Add("    " + chance.xenotype.defName + "=" + chance.chance.ToStringPercent());
                }
            }
        }

        private static void LogReflectionWarning()
        {
            if (reflectionWarningLogged)
            {
                return;
            }
            reflectionWarningLogged = true;
            Log.Warning("[FactionRaceDiversity] Could not access XenotypeSet.xenotypeChances; xenotype overrides were skipped.");
        }
    }
}




