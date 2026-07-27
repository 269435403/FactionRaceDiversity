using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public sealed class FRD_FactionCompatibilitySetDef : Def
    {
        public List<string> factionDefNames = new List<string>();
    }

    public sealed class FRD_PawnKindMapping
    {
        public List<string> sourceKindDefNames = new List<string>();
        public string targetKindDefName;
        public string variantId;
    }

    public sealed class FRD_PawnKindFallbackOverride
    {
        public string variantId;
        public string civilianFallbackKindDefName;
        public string combatFallbackKindDefName;
        public string traderFallbackKindDefName;
        public string leaderFallbackKindDefName;
    }

    public sealed class FRD_RaceCompatibilityDef : Def
    {
        public string raceDefName;
        public List<string> xenotypeDefNames = new List<string>();
        public List<string> excludedXenotypeDefNames = new List<string>();
        public List<string> supportedFactionDefNames = new List<string>();
        public List<string> supportedFactionSetDefNames = new List<string>();
        public List<FRD_PawnKindMapping> pawnKindMappings = new List<FRD_PawnKindMapping>();
        public string civilianFallbackKindDefName;
        public string combatFallbackKindDefName;
        public string traderFallbackKindDefName;
        public string leaderFallbackKindDefName;
        public List<FRD_PawnKindFallbackOverride> fallbackOverrides = new List<FRD_PawnKindFallbackOverride>();
        public bool allowAutomaticFallback = true;
        public bool allowUniversalRoleFallback;
    }

    public static class FRD_CompatibilityRegistry
    {
        private sealed class FallbackSet
        {
            public PawnKindDef Civilian;
            public PawnKindDef Combat;
            public PawnKindDef Trader;
            public PawnKindDef Leader;

            public FallbackSet Clone()
            {
                return new FallbackSet
                {
                    Civilian = Civilian,
                    Combat = Combat,
                    Trader = Trader,
                    Leader = Leader
                };
            }
        }

        private sealed class ResolvedProfile
        {
            public FRD_RaceCompatibilityDef Def;
            public ThingDef Race;
            public HashSet<string> SupportedFactions;
            public Dictionary<string, PawnKindDef> PawnKindsBySource;
            public Dictionary<string, Dictionary<string, PawnKindDef>> VariantPawnKindsById;
            public FallbackSet Fallbacks;
            public Dictionary<string, FallbackSet> VariantFallbacksById;
            public List<string> VariantIds;
            public List<XenotypeDef> Xenotypes;
            public HashSet<XenotypeDef> ExcludedXenotypes;
        }

        private static readonly Dictionary<ThingDef, ResolvedProfile> ProfilesByRace = new Dictionary<ThingDef, ResolvedProfile>();
        private static bool refreshed;

        public static void Refresh()
        {
            ProfilesByRace.Clear();
            refreshed = true;

            foreach (FRD_RaceCompatibilityDef profileDef in DefDatabase<FRD_RaceCompatibilityDef>.AllDefsListForReading)
            {
                if (profileDef == null || string.IsNullOrEmpty(profileDef.raceDefName))
                {
                    continue;
                }

                ThingDef race = DefDatabase<ThingDef>.GetNamedSilentFail(profileDef.raceDefName);
                if (!FRD_RaceRegistry.IsRealPawnRace(race))
                {
                    Log.Warning("[FactionRaceDiversity] Compatibility profile " + profileDef.defName + " was skipped because race " + profileDef.raceDefName + " is unavailable or not a humanlike pawn race.");
                    continue;
                }

                HashSet<string> supportedFactions = new HashSet<string>(
                    (profileDef.supportedFactionDefNames ?? new List<string>()).Where(name => !string.IsNullOrEmpty(name)),
                    StringComparer.OrdinalIgnoreCase);
                foreach (string setDefName in profileDef.supportedFactionSetDefNames ?? new List<string>())
                {
                    if (string.IsNullOrEmpty(setDefName))
                    {
                        continue;
                    }
                    FRD_FactionCompatibilitySetDef setDef = DefDatabase<FRD_FactionCompatibilitySetDef>.GetNamedSilentFail(setDefName);
                    if (setDef == null)
                    {
                        Log.Warning("[FactionRaceDiversity] Compatibility profile " + profileDef.defName + " ignored missing faction set " + setDefName + ".");
                        continue;
                    }
                    foreach (string factionDefName in setDef.factionDefNames ?? new List<string>())
                    {
                        if (!string.IsNullOrEmpty(factionDefName))
                        {
                            supportedFactions.Add(factionDefName);
                        }
                    }
                }

                ResolvedProfile resolved = new ResolvedProfile
                {
                    Def = profileDef,
                    Race = race,
                    SupportedFactions = supportedFactions,
                    PawnKindsBySource = new Dictionary<string, PawnKindDef>(StringComparer.OrdinalIgnoreCase),
                    VariantPawnKindsById = new Dictionary<string, Dictionary<string, PawnKindDef>>(StringComparer.OrdinalIgnoreCase),
                    VariantFallbacksById = new Dictionary<string, FallbackSet>(StringComparer.OrdinalIgnoreCase),
                    VariantIds = new List<string>(),
                    Xenotypes = new List<XenotypeDef>(),
                    ExcludedXenotypes = new HashSet<XenotypeDef>()
                };

                foreach (FRD_PawnKindMapping mapping in profileDef.pawnKindMappings ?? new List<FRD_PawnKindMapping>())
                {
                    if (mapping == null || string.IsNullOrEmpty(mapping.targetKindDefName))
                    {
                        continue;
                    }
                    PawnKindDef target = DefDatabase<PawnKindDef>.GetNamedSilentFail(mapping.targetKindDefName);
                    if (!FRD_RaceRegistry.IsOrdinarySafeKind(target) || !ReferenceEquals(target.race, race))
                    {
                        Log.Warning("[FactionRaceDiversity] Compatibility profile " + profileDef.defName + " ignored invalid target PawnKind " + mapping.targetKindDefName + ".");
                        continue;
                    }

                    Dictionary<string, PawnKindDef> destination = resolved.PawnKindsBySource;
                    if (!string.IsNullOrEmpty(mapping.variantId))
                    {
                        if (!resolved.VariantPawnKindsById.TryGetValue(mapping.variantId, out destination))
                        {
                            destination = new Dictionary<string, PawnKindDef>(StringComparer.OrdinalIgnoreCase);
                            resolved.VariantPawnKindsById[mapping.variantId] = destination;
                        }
                    }
                    foreach (string source in mapping.sourceKindDefNames ?? new List<string>())
                    {
                        if (!string.IsNullOrEmpty(source))
                        {
                            destination[source] = target;
                        }
                    }
                }

                resolved.Fallbacks = new FallbackSet
                {
                    Civilian = ResolveFallback(profileDef.civilianFallbackKindDefName, race, profileDef.defName),
                    Combat = ResolveFallback(profileDef.combatFallbackKindDefName, race, profileDef.defName),
                    Trader = ResolveFallback(profileDef.traderFallbackKindDefName, race, profileDef.defName),
                    Leader = ResolveFallback(profileDef.leaderFallbackKindDefName, race, profileDef.defName)
                };

                foreach (FRD_PawnKindFallbackOverride fallbackOverride in profileDef.fallbackOverrides ?? new List<FRD_PawnKindFallbackOverride>())
                {
                    if (fallbackOverride == null || !string.IsNullOrEmpty(fallbackOverride.variantId))
                    {
                        continue;
                    }
                    ApplyFallbackOverride(resolved.Fallbacks, fallbackOverride, race, profileDef.defName);
                }
                foreach (FRD_PawnKindFallbackOverride fallbackOverride in profileDef.fallbackOverrides ?? new List<FRD_PawnKindFallbackOverride>())
                {
                    if (fallbackOverride == null || string.IsNullOrEmpty(fallbackOverride.variantId))
                    {
                        continue;
                    }
                    if (!resolved.VariantFallbacksById.TryGetValue(fallbackOverride.variantId, out FallbackSet variantFallbacks))
                    {
                        variantFallbacks = resolved.Fallbacks.Clone();
                        resolved.VariantFallbacksById[fallbackOverride.variantId] = variantFallbacks;
                    }
                    ApplyFallbackOverride(variantFallbacks, fallbackOverride, race, profileDef.defName);
                }

                resolved.VariantIds = resolved.VariantPawnKindsById.Keys
                    .Concat(resolved.VariantFallbacksById.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (ModsConfig.BiotechActive)
                {
                    foreach (string xenotypeDefName in profileDef.xenotypeDefNames ?? new List<string>())
                    {
                        XenotypeDef xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(xenotypeDefName);
                        if (xenotype != null && FRD_HarCompatibility.CanUseXenotype(xenotype, race) && !resolved.Xenotypes.Contains(xenotype))
                        {
                            resolved.Xenotypes.Add(xenotype);
                        }
                    }
                    foreach (string xenotypeDefName in profileDef.excludedXenotypeDefNames ?? new List<string>())
                    {
                        XenotypeDef xenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail(xenotypeDefName);
                        if (xenotype != null)
                        {
                            resolved.ExcludedXenotypes.Add(xenotype);
                        }
                    }
                }

                ProfilesByRace[race] = resolved;
            }
        }

        public static bool TryGetMappedKind(FactionDef faction, ThingDef race, PawnKindDef source, out PawnKindDef target)
        {
            target = null;
            ResolvedProfile profile = GetProfile(race);
            if (profile == null || faction == null || source == null || !profile.SupportedFactions.Contains(faction.defName))
            {
                return false;
            }

            string variantId = SelectVariantId(profile, faction);
            if (!string.IsNullOrEmpty(variantId)
                && profile.VariantPawnKindsById.TryGetValue(variantId, out Dictionary<string, PawnKindDef> variantMappings)
                && variantMappings.TryGetValue(source.defName, out target)
                && target != null)
            {
                return true;
            }
            return profile.PawnKindsBySource.TryGetValue(source.defName, out target) && target != null;
        }

        public static bool TryGetFallbackKind(FactionDef faction, ThingDef race, PawnKindDef source, PawnGenerationRequest request, bool requireCombatKind, out PawnKindDef target)
        {
            target = null;
            ResolvedProfile profile = GetProfile(race);
            if (profile == null || faction == null || !profile.SupportedFactions.Contains(faction.defName))
            {
                return false;
            }

            FallbackSet fallbacks = profile.Fallbacks;
            string variantId = SelectVariantId(profile, faction);
            if (!string.IsNullOrEmpty(variantId) && profile.VariantFallbacksById.TryGetValue(variantId, out FallbackSet variantFallbacks))
            {
                fallbacks = variantFallbacks;
            }

            if (requireCombatKind)
            {
                if (profile.Def.allowUniversalRoleFallback)
                {
                    if (source?.trader == true)
                    {
                        target = fallbacks.Trader;
                    }
                    else if (source?.factionLeader == true)
                    {
                        target = fallbacks.Leader;
                    }
                    target = target ?? fallbacks.Combat ?? fallbacks.Civilian ?? fallbacks.Trader ?? fallbacks.Leader;
                    return target != null && SupportsRequiredSpecialRole(source, target);
                }
                if (source?.trader == true && fallbacks.Trader?.isFighter == true)
                {
                    target = fallbacks.Trader;
                }
                else if (source?.factionLeader == true && fallbacks.Leader?.isFighter == true)
                {
                    target = fallbacks.Leader;
                }
                target = target ?? fallbacks.Combat;
                return target?.isFighter == true && SupportsRequiredSpecialRole(source, target);
            }

            if (source?.trader == true)
            {
                target = fallbacks.Trader;
            }
            else if (source?.factionLeader == true)
            {
                target = fallbacks.Leader;
            }
            else if (source?.isFighter == true || request.MustBeCapableOfViolence)
            {
                target = fallbacks.Combat;
            }
            target = target ?? fallbacks.Civilian ?? fallbacks.Combat ?? fallbacks.Trader ?? fallbacks.Leader;
            return target != null && SupportsRequiredSpecialRole(source, target);
        }

        public static bool SupportsRequiredSpecialRole(PawnKindDef source, PawnKindDef target)
        {
            return source != null
                && target != null
                && (!source.canBeSapper || target.canBeSapper)
                && (!source.isGoodBreacher || target.isGoodBreacher)
                && (!source.isGoodPsychicRitualInvoker || target.isGoodPsychicRitualInvoker);
        }

        public static bool AllowsAutomaticFallback(ThingDef race)
        {
            ResolvedProfile profile = GetProfile(race);
            return profile == null || profile.Def.allowAutomaticFallback;
        }

        public static bool AllowsUniversalRoleFallback(ThingDef race)
        {
            ResolvedProfile profile = GetProfile(race);
            return profile?.Def.allowUniversalRoleFallback == true;
        }

        public static IReadOnlyList<XenotypeDef> GetExplicitXenotypes(ThingDef race)
        {
            ResolvedProfile profile = GetProfile(race);
            return profile != null ? (IReadOnlyList<XenotypeDef>)profile.Xenotypes : Array.Empty<XenotypeDef>();
        }

        public static IReadOnlyCollection<XenotypeDef> GetExcludedXenotypes(ThingDef race)
        {
            ResolvedProfile profile = GetProfile(race);
            return profile != null ? (IReadOnlyCollection<XenotypeDef>)profile.ExcludedXenotypes : Array.Empty<XenotypeDef>();
        }

        public static bool HasFactionProfile(FactionDef faction, ThingDef race)
        {
            ResolvedProfile profile = GetProfile(race);
            return faction != null && profile != null && profile.SupportedFactions.Contains(faction.defName);
        }

        private static void ApplyFallbackOverride(FallbackSet fallbacks, FRD_PawnKindFallbackOverride fallbackOverride, ThingDef race, string profileName)
        {
            fallbacks.Civilian = ResolveFallbackOverride(fallbackOverride.civilianFallbackKindDefName, race, profileName, fallbacks.Civilian);
            fallbacks.Combat = ResolveFallbackOverride(fallbackOverride.combatFallbackKindDefName, race, profileName, fallbacks.Combat);
            fallbacks.Trader = ResolveFallbackOverride(fallbackOverride.traderFallbackKindDefName, race, profileName, fallbacks.Trader);
            fallbacks.Leader = ResolveFallbackOverride(fallbackOverride.leaderFallbackKindDefName, race, profileName, fallbacks.Leader);
        }

        private static string SelectVariantId(ResolvedProfile profile, FactionDef faction)
        {
            if (profile == null || faction == null || profile.VariantIds == null || profile.VariantIds.Count == 0)
            {
                return null;
            }

            string packageId = faction.modContentPack?.PackageId;
            if (!string.IsNullOrEmpty(packageId))
            {
                string exact = profile.VariantIds.FirstOrDefault(id => string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(exact))
                {
                    return exact;
                }
            }

            uint hash = 2166136261u;
            string key = (profile.Race?.defName ?? string.Empty) + "|" + (faction.defName ?? string.Empty);
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= char.ToUpperInvariant(key[i]);
                hash *= 16777619u;
            }
            return profile.VariantIds[(int)(hash % (uint)profile.VariantIds.Count)];
        }

        private static PawnKindDef ResolveFallbackOverride(string defName, ThingDef race, string profileName, PawnKindDef current)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return current;
            }
            return ResolveFallback(defName, race, profileName) ?? current;
        }

        private static PawnKindDef ResolveFallback(string defName, ThingDef race, string profileName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return null;
            }
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
            if (!FRD_RaceRegistry.IsOrdinarySafeKind(kind) || !ReferenceEquals(kind.race, race))
            {
                Log.Warning("[FactionRaceDiversity] Compatibility profile " + profileName + " ignored invalid fallback PawnKind " + defName + ".");
                return null;
            }
            return kind;
        }

        private static ResolvedProfile GetProfile(ThingDef race)
        {
            if (!refreshed)
            {
                Refresh();
            }
            return race != null && ProfilesByRace.TryGetValue(race, out ResolvedProfile profile) ? profile : null;
        }
    }
}
