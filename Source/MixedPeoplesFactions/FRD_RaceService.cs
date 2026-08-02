using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MixedPeoplesFactions
{
    public static class FRD_RaceService
    {
        private sealed class RaceChoice
        {
            public ThingDef Race;
            public PawnKindDef Kind;
            public RaceXenotypeSettings Xenotypes;
            public float Weight;
            public bool Manual;
        }

        public static bool TryApplyAtomicSelection(ref PawnGenerationRequest request)
        {
            PawnKindDef originalKind = request.KindDef;
            Faction requestFaction = request.Faction;
            Faction faction = FRD_PawnGroupContext.EffectiveFactionFor(requestFaction);
            if (originalKind == null || faction?.def == null || originalKind.race == null || !FRD_RaceRegistry.IsRealPawnRace(originalKind.race))
            {
                return false;
            }

            MPF_Settings settings = MPF_Mod.Settings;
            FactionRaceSettings config = settings?.GetFactionSettings(faction.def.defName);
            FRD_FactionRecord record = FRD_FactionRegistry.Get(faction.def.defName);
            if (config == null || record == null || !record.SupportsRaces)
            {
                return false;
            }

            string factionKey = faction.def.defName;
            FRD_Diagnostics.RecordRequest(factionKey);

            bool leaderRequest = IsFixedLeader(originalKind, faction);
            bool ordinaryGroup = FRD_PawnGroupContext.IsOrdinaryGroupFor(faction);
            bool requireCombatKind = originalKind.isFighter
                || request.MustBeCapableOfViolence
                || FRD_PawnGroupContext.IsCombatGroupFor(faction);
            if (IsSpecialRequest(request, originalKind, ordinaryGroup))
            {
                FRD_Diagnostics.RecordSpecial(factionKey);
                FRD_Diagnostics.RecordPreserved(factionKey);
                FRD_Diagnostics.RecordFallbackDetail(factionKey, "special", originalKind);
                return false;
            }

            bool hardForcedXenotype = ModsConfig.BiotechActive && request.ForcedXenotype != null && !ordinaryGroup;
            bool foundCompatibleKind = false;
            bool rejectedByHardXenotype = false;
            bool rejectedByMissingXenotype = false;
            List<RaceChoice> choices = new List<RaceChoice>();

            foreach (ThingDef targetRace in FRD_RaceRegistry.HumanlikeRaces)
            {
                float weight = GetWeight(config.raceWeights, targetRace.defName);
                if (weight <= 0f)
                {
                    continue;
                }

                PawnKindDef candidate;
                bool manualCandidate = false;
                if (ReferenceEquals(targetRace, originalKind.race) && (!requireCombatKind || originalKind.isFighter))
                {
                    candidate = originalKind;
                }
                else if (FRD_CompatibilityRegistry.TryGetMappedKind(faction.def, targetRace, originalKind, out PawnKindDef mappedKind)
                    && IsManualCandidateUsable(originalKind, mappedKind, targetRace, request, requireCombatKind))
                {
                    candidate = mappedKind;
                    manualCandidate = true;
                }
                else
                {
                    candidate = FRD_CompatibilityRegistry.AllowsAutomaticFallback(targetRace)
                        ? FindBestCandidate(originalKind, targetRace, request, leaderRequest, requireCombatKind)
                        : null;
                    if (candidate == null
                        && ordinaryGroup
                        && FRD_CompatibilityRegistry.TryGetFallbackKind(faction.def, targetRace, originalKind, request, requireCombatKind, out PawnKindDef fallbackKind))
                    {
                        candidate = fallbackKind;
                        manualCandidate = true;
                    }
                }
                if (candidate == null)
                {
                    continue;
                }
                foundCompatibleKind = true;

                if (hardForcedXenotype)
                {
                    if (!FRD_XenotypeService.IsCompatibleWithRace(request.ForcedXenotype, targetRace, requireCombatKind))
                    {
                        rejectedByHardXenotype = true;
                        continue;
                    }
                    choices.Add(new RaceChoice { Race = targetRace, Kind = candidate, Weight = weight, Manual = manualCandidate });
                    continue;
                }

                RaceXenotypeSettings raceXenotypes = null;
                if (ModsConfig.BiotechActive)
                {
                    raceXenotypes = settings.GetOrCreateRaceXenotypeSettings(faction.def, config, targetRace);
                    if (raceXenotypes == null
                        || !raceXenotypes.overrideEnabled
                        || FRD_XenotypeService.ActiveWeightTotal(raceXenotypes.weights, targetRace, requireCombatKind) <= 0f)
                    {
                        rejectedByMissingXenotype = true;
                        continue;
                    }
                }

                choices.Add(new RaceChoice
                {
                    Race = targetRace,
                    Kind = candidate,
                    Xenotypes = raceXenotypes,
                    Weight = weight,
                    Manual = manualCandidate
                });
            }

            RaceChoice selected = SelectWeighted(choices);
            if (selected == null)
            {
                if (leaderRequest)
                {
                    FRD_Diagnostics.RecordFixedLeader(factionKey);
                }
                if (rejectedByHardXenotype)
                {
                    FRD_Diagnostics.RecordHardXenotypeIncompatible(factionKey);
                }
                else if (rejectedByMissingXenotype)
                {
                    FRD_Diagnostics.RecordNoRaceXenotype(factionKey);
                }
                else if (!foundCompatibleKind)
                {
                    FRD_Diagnostics.RecordNoCompatibleKind(factionKey);
                }
                else
                {
                    FRD_Diagnostics.RecordNoCompatibleKind(factionKey);
                }
                FRD_Diagnostics.RecordPreserved(factionKey);
                FRD_Diagnostics.RecordFallbackDetail(factionKey, rejectedByHardXenotype ? "hard xenotype" : rejectedByMissingXenotype ? "no xenotype pool" : leaderRequest ? "leader kind" : "no PawnKind", originalKind);
                return false;
            }

            FRD_XenotypeChoice selectedXenotype = null;
            if (ModsConfig.BiotechActive && !hardForcedXenotype)
            {
                selectedXenotype = FRD_XenotypeService.SelectWeightedChoice(
                    selected.Xenotypes?.weights,
                    selected.Race,
                    requireCombatKind);
                if (selectedXenotype == null)
                {
                    FRD_Diagnostics.RecordNoRaceXenotype(factionKey);
                    FRD_Diagnostics.RecordPreserved(factionKey);
                    return false;
                }
            }

            request.PawnKindDefGetter = null;
            request.KindDef = selected.Kind;
            if (ModsConfig.BiotechActive && !hardForcedXenotype)
            {
                // Ordinary pawn groups arrive here with a xenotype preselected for the original Human PawnKind.
                // It is a soft group choice and is atomically replaced by the selected Race's own pool.
                request.ForcedXenotype = selectedXenotype.Xenotype;
                request.ForcedCustomXenotype = selectedXenotype.CustomXenotype;
                request.AllowedXenotypes = null;
                request.ForceBaselinerChance = 0f;
            }

            if (ReferenceEquals(selected.Kind.race, originalKind.race))
            {
                FRD_Diagnostics.RecordPreserved(factionKey);
                return false;
            }

            FRD_Diagnostics.RecordSuccess(factionKey, selected.Manual);
            return true;
        }

        public static bool IsConfiguredRaceAllowed(Faction faction, ThingDef race)
        {
            FactionRaceSettings config = MPF_Mod.Settings?.GetFactionSettings(faction?.def?.defName);
            return config == null || GetWeight(config.raceWeights, race?.defName) > 0f;
        }

        public static bool ConfiguredRacesSupportRequiredRaidRole(Faction faction, RaidStrategyWorker_WithRequiredPawnKinds worker)
        {
            FactionRaceSettings config = MPF_Mod.Settings?.GetFactionSettings(faction?.def?.defName);
            if (config == null || worker == null)
            {
                return true;
            }

            System.Reflection.MethodInfo matcher = null;
            for (Type type = worker.GetType(); type != null && matcher == null; type = type.BaseType)
            {
                matcher = type.GetMethod("MatchesRequiredPawnKind", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            }
            if (matcher == null)
            {
                return true;
            }

            try
            {
                foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces)
                {
                    if (GetWeight(config.raceWeights, race.defName) <= 0f)
                    {
                        continue;
                    }
                    RaceXenotypeSettings pool = ModsConfig.BiotechActive ? config.GetRaceXenotypeSettings(race.defName) : null;
                    if (ModsConfig.BiotechActive && FRD_XenotypeService.ActiveWeightTotal(pool?.weights, race, true) <= 0f)
                    {
                        continue;
                    }
                    FRD_FactionRecord record = FRD_FactionRegistry.Get(faction.def.defName);
                    if (record != null)
                    {
                        foreach (PawnKindDef sourceKind in record.SourceKinds)
                        {
                            if ((bool)matcher.Invoke(worker, new object[] { sourceKind })
                                && FRD_CompatibilityRegistry.TryGetMappedKind(faction.def, race, sourceKind, out PawnKindDef mappedKind)
                                && mappedKind != null
                                && (bool)matcher.Invoke(worker, new object[] { mappedKind }))
                            {
                                return true;
                            }
                        }
                    }
                    if (FRD_CompatibilityRegistry.AllowsAutomaticFallback(race)
                        && FRD_RaceRegistry.GetKinds(race).Any(kind => FRD_RaceRegistry.IsOrdinarySafeKind(kind) && (bool)matcher.Invoke(worker, new object[] { kind })))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception exception)
            {
                Log.ErrorOnce("[FactionRaceDiversity] Required raid-role compatibility check failed; the original raid strategy result was preserved. " + exception, 174832503);
                return true;
            }
        }

        private static bool IsSpecialRequest(PawnGenerationRequest request, PawnKindDef kind, bool ordinaryGroup)
        {
            if (request.Context == PawnGenerationContext.PlayerStarter
                || request.ForcedCustomXenotype != null
                || request.ForcedMutant != null
                || request.IsCreepJoiner
                || kind.mutant != null
                || kind is CreepJoinerFormKindDef
                || kind.isBoss
                || request.AllowedDevelopmentalStages.Newborn())
            {
                return true;
            }
            if (ordinaryGroup)
            {
                return false;
            }
            return request.PawnKindDefGetter != null
                || request.FixedTitle != null
                || !request.ForcedXenogenes.NullOrEmpty()
                || !request.ForcedEndogenes.NullOrEmpty()
                || !request.AllowedXenotypes.NullOrEmpty()
                || request.ForceBaselinerChance > 0f;
        }

        private static bool IsFixedLeader(PawnKindDef kind, Faction faction)
        {
            return kind.factionLeader || (faction.def.fixedLeaderKinds != null && faction.def.fixedLeaderKinds.Contains(kind));
        }


        private static bool IsManualCandidateUsable(PawnKindDef source, PawnKindDef candidate, ThingDef targetRace, PawnGenerationRequest request, bool requireCombatKind)
        {
            bool universalRoleFallback = FRD_CompatibilityRegistry.AllowsUniversalRoleFallback(targetRace);
            if (!FRD_RaceRegistry.IsOrdinarySafeKind(candidate)
                || !ReferenceEquals(candidate.race, targetRace)
                || (!universalRoleFallback && source?.trader == true && !candidate.trader)
                || (!universalRoleFallback && source?.factionLeader == true && !candidate.factionLeader)
                || (!universalRoleFallback && requireCombatKind && !candidate.isFighter)
                || (!universalRoleFallback && source?.isFighter == true && !candidate.isFighter)
                || !FRD_CompatibilityRegistry.SupportsRequiredSpecialRole(source, candidate))
            {
                return false;
            }
            if (FRD_PawnGroupContext.IsOrdinaryGroupFor(FRD_PawnGroupContext.EffectiveFactionFor(request.Faction)))
            {
                return true;
            }
            if (candidate.pawnGroupDevelopmentStage.HasValue
                && !request.AllowedDevelopmentalStages.Has(candidate.pawnGroupDevelopmentStage.Value))
            {
                return false;
            }
            float candidateMin = Math.Max(0f, candidate.minGenerationAge);
            float candidateMax = Math.Max(candidateMin, candidate.maxGenerationAge);
            if (request.FixedBiologicalAge.HasValue)
            {
                float age = request.FixedBiologicalAge.Value;
                return age >= candidateMin && age <= candidateMax;
            }
            if (request.BiologicalAgeRange.HasValue)
            {
                FloatRange range = request.BiologicalAgeRange.Value;
                return range.max >= candidateMin && range.min <= candidateMax;
            }
            return true;
        }

        private static PawnKindDef FindBestCandidate(PawnKindDef original, ThingDef targetRace, PawnGenerationRequest request, bool leaderRequest, bool requireCombatKind)
        {
            PawnKindDef best = null;
            double bestScore = double.MaxValue;
            foreach (PawnKindDef candidate in FRD_RaceRegistry.GetKinds(targetRace))
            {
                if (!IsCompatibleRole(original, candidate, request, leaderRequest, requireCombatKind))
                {
                    continue;
                }

                double score = CompatibilityScore(original, candidate);
                if (best == null || score < bestScore
                    || (Math.Abs(score - bestScore) < 0.0001
                        && string.Compare(candidate.defName, best.defName, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool IsCompatibleRole(PawnKindDef original, PawnKindDef candidate, PawnGenerationRequest request, bool leaderRequest, bool requireCombatKind)
        {
            if (!FRD_RaceRegistry.IsOrdinarySafeKind(candidate)
                || candidate.trader != original.trader
                || (requireCombatKind ? !candidate.isFighter : candidate.isFighter != original.isFighter)
                || (leaderRequest ? !candidate.factionLeader : candidate.factionLeader != original.factionLeader)
                || (original.canBeSapper && !candidate.canBeSapper)
                || (original.isGoodBreacher && !candidate.isGoodBreacher)
                || (original.isGoodPsychicRitualInvoker && !candidate.isGoodPsychicRitualInvoker))
            {
                return false;
            }
            // useFactionXenotypes is deliberately not compared. HAR races commonly keep their
            // own xenotype set on PawnKindDef and set this flag to false.
            if (original.pawnGroupDevelopmentStage.HasValue
                && candidate.pawnGroupDevelopmentStage.HasValue
                && original.pawnGroupDevelopmentStage.Value != candidate.pawnGroupDevelopmentStage.Value)
            {
                return false;
            }
            if (candidate.pawnGroupDevelopmentStage.HasValue
                && !request.AllowedDevelopmentalStages.Has(candidate.pawnGroupDevelopmentStage.Value))
            {
                return false;
            }
            return AgeRangesOverlap(original, candidate, request);
        }

        private static bool AgeRangesOverlap(PawnKindDef original, PawnKindDef candidate, PawnGenerationRequest request)
        {
            float candidateMin = Math.Max(0, candidate.minGenerationAge);
            float candidateMax = Math.Max(candidateMin, candidate.maxGenerationAge);
            if (request.FixedBiologicalAge.HasValue)
            {
                float age = request.FixedBiologicalAge.Value;
                return age >= candidateMin && age <= candidateMax;
            }
            if (request.BiologicalAgeRange.HasValue)
            {
                FloatRange range = request.BiologicalAgeRange.Value;
                return range.max >= candidateMin && range.min <= candidateMax;
            }
            float originalMin = Math.Max(0, original.minGenerationAge);
            float originalMax = Math.Max(originalMin, original.maxGenerationAge);
            return originalMax >= candidateMin && originalMin <= candidateMax;
        }

        private static double CompatibilityScore(PawnKindDef original, PawnKindDef candidate)
        {
            double score = 0d;
            if (original.combatPower > 0f && candidate.combatPower > 0f)
            {
                score += Math.Abs(Math.Log(candidate.combatPower / original.combatPower)) * 20d;
            }
            else if (original.combatPower != candidate.combatPower)
            {
                score += 10d;
            }

            double originalMidAge = (Math.Max(0, original.minGenerationAge) + Math.Max(original.minGenerationAge, original.maxGenerationAge)) * 0.5d;
            double candidateMidAge = (Math.Max(0, candidate.minGenerationAge) + Math.Max(candidate.minGenerationAge, candidate.maxGenerationAge)) * 0.5d;
            score += Math.Min(10d, Math.Abs(originalMidAge - candidateMidAge) * 0.1d);
            if (!original.weaponTags.NullOrEmpty() && !candidate.weaponTags.NullOrEmpty())
            {
                score -= Math.Min(3, original.weaponTags.Intersect(candidate.weaponTags).Count());
            }
            return score;
        }

        private static RaceChoice SelectWeighted(List<RaceChoice> choices)
        {
            float total = choices.Sum(choice => Math.Max(0f, choice.Weight));
            if (total <= 0f)
            {
                return null;
            }
            float pick = Rand.Value * total;
            foreach (RaceChoice choice in choices)
            {
                pick -= Math.Max(0f, choice.Weight);
                if (pick <= 0f)
                {
                    return choice;
                }
            }
            return choices[choices.Count - 1];
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




