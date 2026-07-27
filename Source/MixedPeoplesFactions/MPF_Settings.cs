using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MixedPeoplesFactions
{
    public sealed class MPF_Settings : ModSettings
    {
        public const string BaselinerKey = "Baseliner";
        private const int CurrentSchemaVersion = 6;
        private static readonly string[] LegacyFactionDefNames = { "MPF_MixedCivil", "MPF_MixedRough" };
        private static readonly Color ConfiguredColor = new Color(0.35f, 1f, 0.45f);
        private static readonly Color ConfiguredUnsupportedColor = new Color(0.28f, 0.72f, 0.34f);

        public Dictionary<string, float> xenotypeWeights = new Dictionary<string, float>();
        public int settingsSchemaVersion;
        public Dictionary<string, FactionRaceSettings> factionSettings = new Dictionary<string, FactionRaceSettings>();
        public bool showHiddenFactions;
        public bool showUnsupportedFactions;

        private Vector2 factionScrollPosition;
        private Vector2 detailScrollPosition;
        private string factionSearch = string.Empty;
        private string selectedFactionDefName;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref xenotypeWeights, "MPF_xenotypeWeights", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref settingsSchemaVersion, "FRD_settingsSchemaVersion", 0);
            Scribe_Collections.Look(ref factionSettings, "FRD_factionSettings", LookMode.Value, LookMode.Deep);
            Scribe_Values.Look(ref showHiddenFactions, "FRD_showHiddenFactions", false);
            Scribe_Values.Look(ref showUnsupportedFactions, "FRD_showUnsupportedFactions", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                xenotypeWeights = xenotypeWeights ?? new Dictionary<string, float>();
                factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();
                RemoveInvalidFactionEntries();
            }
        }

        public void MigrateLegacySettings()
        {
            factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();

            if (settingsSchemaVersion < 2 && xenotypeWeights != null && xenotypeWeights.Count > 0)
            {
                Dictionary<string, float> migratedWeights = new Dictionary<string, float>(xenotypeWeights);
                foreach (string factionDefName in LegacyFactionDefNames)
                {
                    if (!factionSettings.ContainsKey(factionDefName))
                    {
                        factionSettings[factionDefName] = new FactionRaceSettings
                        {
                            xenotypeOverrideEnabled = true,
                            xenotypeWeights = new Dictionary<string, float>(migratedWeights)
                        };
                    }
                }
            }

            foreach (FactionRaceSettings config in factionSettings.Values.Where(value => value != null))
            {
                config.MigrateLegacyXenotypesToHuman();
            }
            if (settingsSchemaVersion < 6)
            {
                MigrateToDirectRaceSettings();
            }
            EnsureBuiltInFactionDefaults();
            settingsSchemaVersion = CurrentSchemaVersion;
        }

        public FactionRaceSettings GetFactionSettings(string factionDefName)
        {
            if (factionSettings == null || string.IsNullOrEmpty(factionDefName))
            {
                return null;
            }
            factionSettings.TryGetValue(factionDefName, out FactionRaceSettings config);
            return config;
        }

        public FactionRaceSettings GetOrCreateFactionSettings(FactionDef faction)
        {
            if (faction == null)
            {
                return null;
            }
            factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();
            if (!factionSettings.TryGetValue(faction.defName, out FactionRaceSettings config) || config == null)
            {
                config = CreateDefaultRaceSettings(faction, false);
                factionSettings[faction.defName] = config;
            }
            config.raceOverrideEnabled = true;
            config.xenotypeOverrideEnabled = false;
            return config;
        }

        public RaceXenotypeSettings GetOrCreateRaceXenotypeSettings(FactionDef faction, FactionRaceSettings config, ThingDef race)
        {
            if (config == null || race == null || !ModsConfig.BiotechActive)
            {
                return null;
            }
            config.xenotypeSettingsByRace = config.xenotypeSettingsByRace ?? new Dictionary<string, RaceXenotypeSettings>();
            if (!config.xenotypeSettingsByRace.TryGetValue(race.defName, out RaceXenotypeSettings pool) || pool == null)
            {
                pool = new RaceXenotypeSettings
                {
                    overrideEnabled = true,
                    weights = FRD_XenotypeService.BuildDefaultWeights(faction, race)
                };
                config.xenotypeSettingsByRace[race.defName] = pool;
            }

            pool.overrideEnabled = true;
            pool.weights = pool.weights ?? new Dictionary<string, float>();
            NormalizeDictionary(pool.weights);
            FRD_XenotypeService.EnsureAllowedKeys(pool, race);
            if (FRD_XenotypeService.ActiveWeightTotal(pool.weights, race, false) <= 0f)
            {
                pool.weights = FRD_XenotypeService.BuildDefaultWeights(faction, race);
                FRD_XenotypeService.EnsureAllowedKeys(pool, race);
            }
            return pool;
        }

        public void NormalizeAllSettings()
        {
            factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();
            RemoveInvalidFactionEntries();
            EnsureBuiltInFactionDefaults();
            foreach (KeyValuePair<string, FactionRaceSettings> pair in factionSettings)
            {
                FactionDef faction = DefDatabase<FactionDef>.GetNamedSilentFail(pair.Key);
                FactionRaceSettings config = pair.Value;
                config.xenotypeWeights = config.xenotypeWeights ?? new Dictionary<string, float>();
                config.raceWeights = config.raceWeights ?? new Dictionary<string, float>();
                config.xenotypeSettingsByRace = config.xenotypeSettingsByRace ?? new Dictionary<string, RaceXenotypeSettings>();
                config.MigrateLegacyXenotypesToHuman();
                config.raceOverrideEnabled = true;
                config.xenotypeOverrideEnabled = false;
                NormalizeDictionary(config.xenotypeWeights);
                NormalizeDictionary(config.raceWeights);

                foreach (RaceXenotypeSettings pool in config.xenotypeSettingsByRace.Values.Where(value => value != null))
                {
                    pool.overrideEnabled = true;
                    pool.weights = pool.weights ?? new Dictionary<string, float>();
                    NormalizeDictionary(pool.weights);
                }

                EnsureRaceKeys(faction, config);
                if (config.autoBalanceRaces)
                {
                    SetEqualRaceWeights(config);
                }
                if (ActiveRaceWeightTotal(config) <= 0f)
                {
                    SetDefaultRaceFallback(faction, config);
                }

                GetOrCreateRaceXenotypeSettings(faction, config, ThingDefOf.Human);
                foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces)
                {
                    if (GetWeight(config.raceWeights, race.defName) > 0f)
                    {
                        GetOrCreateRaceXenotypeSettings(faction, config, race);
                    }
                }
            }
        }

        public bool AllStoredWeightsAreValid()
        {
            if (!DictionaryWeightsAreValid(xenotypeWeights))
            {
                return false;
            }
            if (factionSettings == null)
            {
                return true;
            }
            return factionSettings.Values.All(config => config != null
                && DictionaryWeightsAreValid(config.xenotypeWeights)
                && DictionaryWeightsAreValid(config.raceWeights)
                && (config.xenotypeSettingsByRace == null
                    || config.xenotypeSettingsByRace.Values.All(pool => pool != null && DictionaryWeightsAreValid(pool.weights))));
        }

        public static bool IsActivelyConfigured(FactionRaceSettings config)
        {
            return config != null && !config.autoBalanceRaces;
        }

        public void EnsureGlobalXenotypeKeys(FactionDef faction, FactionRaceSettings config)
        {
            if (config == null || !ModsConfig.BiotechActive)
            {
                return;
            }
            config.xenotypeWeights = config.xenotypeWeights ?? new Dictionary<string, float>();
            if (config.xenotypeWeights.Count == 0)
            {
                foreach (KeyValuePair<string, float> pair in MPF_Injector.GetBaselineWeights(faction))
                {
                    config.xenotypeWeights[pair.Key] = pair.Value;
                }
            }
            if (!config.xenotypeWeights.ContainsKey(BaselinerKey))
            {
                config.xenotypeWeights[BaselinerKey] = 0f;
            }
            foreach (XenotypeDef xenotype in ActiveXenotypes())
            {
                if (!config.xenotypeWeights.ContainsKey(xenotype.defName))
                {
                    config.xenotypeWeights[xenotype.defName] = 0f;
                }
            }
        }

        public void EnsureRaceKeys(FactionDef faction, FactionRaceSettings config)
        {
            if (config == null)
            {
                return;
            }
            config.raceWeights = config.raceWeights ?? new Dictionary<string, float>();
            foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces)
            {
                if (!config.raceWeights.ContainsKey(race.defName))
                {
                    config.raceWeights[race.defName] = 0f;
                }
            }
            if (ActiveRaceWeightTotal(config) <= 0f)
            {
                SetDefaultRaceFallback(faction, config);
            }
        }

        public float ActiveGlobalXenotypeWeightTotal(FactionRaceSettings config)
        {
            if (config?.xenotypeWeights == null || !ModsConfig.BiotechActive)
            {
                return 0f;
            }
            float total = GetWeight(config.xenotypeWeights, BaselinerKey);
            foreach (XenotypeDef xenotype in ActiveXenotypes())
            {
                total += GetWeight(config.xenotypeWeights, xenotype.defName);
            }
            return total;
        }

        public float ActiveRaceWeightTotal(FactionRaceSettings config)
        {
            if (config?.raceWeights == null)
            {
                return 0f;
            }
            return FRD_RaceRegistry.HumanlikeRaces.Sum(race => GetWeight(config.raceWeights, race.defName));
        }

        public void DrawWindowContents(Rect inRect)
        {
            if (FRD_FactionRegistry.Records.Count == 0)
            {
                FRD_FactionRegistry.Refresh();
            }

            float leftWidth = Mathf.Clamp(inRect.width * 0.37f, 300f, 430f);
            Rect leftRect = new Rect(inRect.x, inRect.y, leftWidth, inRect.height);
            Rect rightRect = new Rect(leftRect.xMax + 8f, inRect.y, inRect.width - leftWidth - 8f, inRect.height);
            Widgets.DrawMenuSection(leftRect);
            Widgets.DrawMenuSection(rightRect);
            List<FRD_FactionRecord> visible = DrawFactionList(leftRect.ContractedBy(8f));
            SelectFallbackFaction(visible);
            DrawFactionDetails(rightRect.ContractedBy(8f));
        }

        private List<FRD_FactionRecord> DrawFactionList(Rect rect)
        {
            float y = rect.y;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "FRD_FactionSearch".Translate());
            y += 25f;
            factionSearch = Widgets.TextField(new Rect(rect.x, y, rect.width, 28f), factionSearch ?? string.Empty);
            y += 34f;
            Widgets.CheckboxLabeled(new Rect(rect.x, y, rect.width, 24f), "FRD_ShowHiddenFactions".Translate(), ref showHiddenFactions);
            y += 25f;
            Widgets.CheckboxLabeled(new Rect(rect.x, y, rect.width, 24f), "FRD_ShowUnsupportedFactions".Translate(), ref showUnsupportedFactions);
            y += 30f;

            string search = (factionSearch ?? string.Empty).Trim();
            List<FRD_FactionRecord> visible = FRD_FactionRegistry.Records
                .Where(record => IsFactionVisible(record)
                    && (search.Length == 0
                        || record.Label.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                        || record.Def.defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(record => IsActivelyConfigured(GetFactionSettings(record.Def.defName)))
                .ThenBy(record => record.IsSupported ? 0 : 1)
                .ThenBy(record => record.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Rect scrollOut = new Rect(rect.x, y, rect.width, rect.yMax - y);
            Rect view = new Rect(0f, 0f, scrollOut.width - 16f, Math.Max(scrollOut.height, visible.Count * 34f));
            Widgets.BeginScrollView(scrollOut, ref factionScrollPosition, view);
            for (int i = 0; i < visible.Count; i++)
            {
                FRD_FactionRecord record = visible[i];
                Rect row = new Rect(0f, i * 34f, view.width, 32f);
                bool selected = record.Def.defName == selectedFactionDefName;
                if (selected)
                {
                    Widgets.DrawHighlightSelected(row);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }

                DrawFactionIcon(new Rect(row.x + 4f, row.y + 4f, 24f, 24f), record.Def);
                bool configured = IsActivelyConfigured(GetFactionSettings(record.Def.defName));
                Color oldColor = GUI.color;
                if (configured)
                {
                    GUI.color = record.IsSupported ? ConfiguredColor : ConfiguredUnsupportedColor;
                }
                else if (!record.IsSupported)
                {
                    GUI.color = Color.gray;
                }
                Widgets.Label(new Rect(row.x + 34f, row.y + 5f, row.width - 38f, 24f), record.Label);
                GUI.color = oldColor;
                if (!record.IsSupported && !string.IsNullOrEmpty(record.UnsupportedReasonKey))
                {
                    TooltipHandler.TipRegion(row, record.UnsupportedReasonKey.Translate());
                }
                if (Widgets.ButtonInvisible(row))
                {
                    selectedFactionDefName = record.Def.defName;
                    detailScrollPosition = Vector2.zero;
                }
            }
            Widgets.EndScrollView();
            if (visible.Count == 0)
            {
                Widgets.Label(scrollOut, "FRD_NoFactionMatches".Translate());
            }
            return visible;
        }

        private bool IsFactionVisible(FRD_FactionRecord record)
        {
            bool configured = IsActivelyConfigured(GetFactionSettings(record.Def.defName));
            if (configured)
            {
                return true;
            }
            if (record.Def.hidden && !showHiddenFactions)
            {
                return false;
            }
            return record.IsSupported || showUnsupportedFactions;
        }

        private void SelectFallbackFaction(List<FRD_FactionRecord> visible)
        {
            if (visible.Count == 0)
            {
                selectedFactionDefName = null;
                return;
            }
            if (visible.All(record => record.Def.defName != selectedFactionDefName))
            {
                selectedFactionDefName = visible[0].Def.defName;
            }
        }

        private void DrawFactionDetails(Rect rect)
        {
            FRD_FactionRecord record = FRD_FactionRegistry.Get(selectedFactionDefName);
            if (record == null)
            {
                Widgets.Label(rect, "FRD_SelectFaction".Translate());
                return;
            }

            FactionRaceSettings storedConfig = GetFactionSettings(record.Def.defName);
            FactionRaceSettings displayConfig = storedConfig ?? CreateDefaultRaceSettings(record.Def, false);
            EnsureRaceKeys(record.Def, displayConfig);
            float estimatedHeight = EstimateDetailHeight(displayConfig);
            Rect view = new Rect(0f, 0f, rect.width - 16f, Math.Max(rect.height, estimatedHeight));
            Widgets.BeginScrollView(rect, ref detailScrollPosition, view);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(view);

            Text.Font = GameFont.Medium;
            listing.Label("FRD_CurrentFaction".Translate(record.Label));
            Text.Font = GameFont.Small;
            listing.Label("FRD_FactionSource".Translate(record.Def.modContentPack?.Name ?? "FRD_CoreSource".Translate().ToString()));
            if (!string.IsNullOrEmpty(record.UnsupportedReasonKey))
            {
                listing.Label(record.UnsupportedReasonKey.Translate());
            }
            listing.GapLine();

            bool oldGuiEnabled = GUI.enabled;
            GUI.enabled = record.SupportsRaces;
            listing.Label("FRD_RaceSection".Translate());
            bool raceChanged = false;
            float raceTotal = ActiveRaceWeightTotal(displayConfig);
            foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces)
            {
                raceChanged |= DrawWeightSlider(listing, race.LabelCap.ToString(), displayConfig.raceWeights, race.defName, raceTotal, "FRD_RaceWeightTooltip");
            }
            if (raceChanged)
            {
                displayConfig.raceOverrideEnabled = true;
                displayConfig.xenotypeOverrideEnabled = false;
                displayConfig.autoBalanceRaces = false;
                factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();
                factionSettings[record.Def.defName] = displayConfig;
                storedConfig = displayConfig;
            }

            if (!ModsConfig.BiotechActive)
            {
                listing.Label("FRD_BiotechUnavailable".Translate());
            }
            else
            {
                bool xenotypeChanged = false;
                foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces.Where(race => GetWeight(displayConfig.raceWeights, race.defName) > 0f))
                {
                    RaceXenotypeSettings pool = GetOrCreateRaceXenotypeSettings(record.Def, displayConfig, race);
                    listing.GapLine();
                    listing.Label("FRD_RaceXenotypeSection".Translate(race.LabelCap));
                    float total = FRD_XenotypeService.ActiveWeightTotal(pool.weights, race, false);
                    foreach (XenotypeDef xenotype in FRD_XenotypeService.GetAllowedXenotypes(race))
                    {
                        xenotypeChanged |= DrawWeightSlider(listing, xenotype.LabelCap.ToString(), pool.weights, FRD_XenotypeService.KeyFor(xenotype), total, "FRD_XenotypeWeightTooltip");
                    }
                }
                if (xenotypeChanged)
                {
                    displayConfig.raceOverrideEnabled = true;
                    displayConfig.xenotypeOverrideEnabled = false;
                    displayConfig.autoBalanceRaces = false;
                    factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();
                    factionSettings[record.Def.defName] = displayConfig;
                    storedConfig = displayConfig;
                }
            }
            GUI.enabled = oldGuiEnabled;

            listing.GapLine();
            if (listing.ButtonText("FRD_RestoreOriginalRules".Translate()))
            {
                if (IsBuiltInMixedFaction(record.Def))
                {
                    ResetBuiltInFactionToEqualDefaults(record.Def);
                }
                else
                {
                    factionSettings?.Remove(record.Def.defName);
                    MPF_Injector.RestoreFaction(record.Def);
                }
                NormalizeAllSettings();
                MPF_Injector.ApplyAll(this);
                Messages.Message("FRD_OriginalRulesRestored".Translate(record.Label), MessageTypeDefOf.NeutralEvent, false);
            }
            listing.End();
            Widgets.EndScrollView();
        }

        private float EstimateDetailHeight(FactionRaceSettings config)
        {
            float height = 250f + FRD_RaceRegistry.HumanlikeRaces.Count * 32f;
            if (ModsConfig.BiotechActive && config != null)
            {
                foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces.Where(race => GetWeight(config.raceWeights, race.defName) > 0f))
                {
                    height += 55f + FRD_XenotypeService.GetAllowedXenotypes(race).Count * 32f;
                }
            }
            return height;
        }

        private static void DrawFactionIcon(Rect rect, FactionDef faction)
        {
            if (faction == null)
            {
                return;
            }
            Color oldColor = GUI.color;
            try
            {
                GUI.color = Color.white;
                if (!faction.factionIconPath.NullOrEmpty())
                {
                    Widgets.DefIcon(rect, faction);
                }
                else if (!faction.settlementTexturePath.NullOrEmpty())
                {
                    GUI.color = faction.DefaultColor;
                    Widgets.DrawTextureFitted(rect, faction.SettlementTexture, 1f);
                }
                else
                {
                    Widgets.DrawBoxSolid(rect.ContractedBy(3f), faction.DefaultColor);
                }
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        private static bool DrawWeightSlider(Listing_Standard listing, string label, Dictionary<string, float> weights, string key, float total, string tooltipKey)
        {
            float value = GetWeight(weights, key);
            float percentage = total <= 0f ? 0f : value / total * 100f;
            string displayedLabel = label + "  " + percentage.ToString("0.#", CultureInfo.InvariantCulture) + "%";
            float next = listing.SliderLabeled(displayedLabel, value, 0f, 100f, 0.58f, tooltipKey.Translate());
            weights[key] = Mathf.Clamp(next, 0f, 100f);
            return Math.Abs(weights[key] - value) > 0.0001f;
        }

        private static bool IsBuiltInMixedFaction(FactionDef faction)
        {
            return faction != null && LegacyFactionDefNames.Contains(faction.defName);
        }

        private void MigrateToDirectRaceSettings()
        {
            foreach (string factionDefName in factionSettings.Keys.ToList())
            {
                FactionDef faction = DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName);
                FactionRaceSettings config = factionSettings[factionDefName];
                if (faction == null || config == null)
                {
                    continue;
                }

                if (IsBuiltInMixedFaction(faction) && !config.raceOverrideEnabled && !config.xenotypeOverrideEnabled)
                {
                    factionSettings[factionDefName] = CreateDefaultRaceSettings(faction, true);
                    continue;
                }

                config.MigrateLegacyXenotypesToHuman();
                if (config.xenotypeOverrideEnabled && !config.raceOverrideEnabled)
                {
                    config.raceOverrideEnabled = true;
                    EnsureRaceKeys(faction, config);
                    ThingDef defaultRace = FRD_RaceRegistry.DefaultRaceFor(faction);
                    if (defaultRace != null)
                    {
                        config.xenotypeSettingsByRace[defaultRace.defName] = new RaceXenotypeSettings
                        {
                            overrideEnabled = true,
                            weights = new Dictionary<string, float>(config.xenotypeWeights ?? new Dictionary<string, float>())
                        };
                    }
                }
                else if (!config.raceOverrideEnabled)
                {
                    if (!RaceWeightsDifferFromOriginal(faction, config))
                    {
                        factionSettings.Remove(factionDefName);
                        continue;
                    }
                    config.raceOverrideEnabled = true;
                }
                config.xenotypeOverrideEnabled = false;
            }
        }

        private bool RaceWeightsDifferFromOriginal(FactionDef faction, FactionRaceSettings config)
        {
            if (config?.raceWeights == null || config.raceWeights.Count == 0)
            {
                return false;
            }
            ThingDef defaultRace = FRD_RaceRegistry.DefaultRaceFor(faction);
            foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces)
            {
                float expected = ReferenceEquals(race, defaultRace) ? 100f : 0f;
                if (Math.Abs(GetWeight(config.raceWeights, race.defName) - expected) > 0.0001f)
                {
                    return true;
                }
            }
            return false;
        }

        private void EnsureBuiltInFactionDefaults()
        {
            factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();
            foreach (string factionDefName in LegacyFactionDefNames)
            {
                FactionDef faction = DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName);
                if (faction == null)
                {
                    continue;
                }
                if (!factionSettings.TryGetValue(factionDefName, out FactionRaceSettings config) || config == null)
                {
                    factionSettings[factionDefName] = CreateDefaultRaceSettings(faction, true);
                }
                else if (config.autoBalanceRaces)
                {
                    config.raceOverrideEnabled = true;
                    config.xenotypeOverrideEnabled = false;
                    SetEqualRaceWeights(config);
                }
            }
        }

        private FactionRaceSettings ResetBuiltInFactionToEqualDefaults(FactionDef faction)
        {
            FactionRaceSettings config = CreateDefaultRaceSettings(faction, true);
            factionSettings = factionSettings ?? new Dictionary<string, FactionRaceSettings>();
            factionSettings[faction.defName] = config;
            return config;
        }

        private FactionRaceSettings CreateDefaultRaceSettings(FactionDef faction, bool equalRaces)
        {
            FactionRaceSettings config = new FactionRaceSettings
            {
                raceOverrideEnabled = true,
                xenotypeOverrideEnabled = false,
                autoBalanceRaces = equalRaces
            };
            EnsureRaceKeys(faction, config);
            if (equalRaces)
            {
                SetEqualRaceWeights(config);
            }
            if (ModsConfig.BiotechActive)
            {
                foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces.Where(race => GetWeight(config.raceWeights, race.defName) > 0f))
                {
                    GetOrCreateRaceXenotypeSettings(faction, config, race);
                }
            }
            return config;
        }

        private static void SetEqualRaceWeights(FactionRaceSettings config)
        {
            if (config == null)
            {
                return;
            }
            config.raceWeights = config.raceWeights ?? new Dictionary<string, float>();
            HashSet<string> activeRaces = new HashSet<string>(FRD_RaceRegistry.HumanlikeRaces.Select(race => race.defName));
            foreach (string staleKey in config.raceWeights.Keys.Where(key => !activeRaces.Contains(key)).ToList())
            {
                config.raceWeights.Remove(staleKey);
            }
            foreach (string raceDefName in activeRaces)
            {
                config.raceWeights[raceDefName] = 100f;
            }
        }

        private void SetBaselineXenotypeFallback(FactionDef faction, FactionRaceSettings config)
        {
            config.xenotypeWeights = MPF_Injector.GetBaselineWeights(faction);
            EnsureGlobalXenotypeKeys(faction, config);
            if (ActiveGlobalXenotypeWeightTotal(config) <= 0f)
            {
                config.xenotypeWeights[BaselinerKey] = 100f;
            }
        }

        private void SetDefaultRaceFallback(FactionDef faction, FactionRaceSettings config)
        {
            ThingDef defaultRace = FRD_RaceRegistry.DefaultRaceFor(faction);
            foreach (ThingDef race in FRD_RaceRegistry.HumanlikeRaces)
            {
                config.raceWeights[race.defName] = ReferenceEquals(race, defaultRace) ? 100f : 0f;
            }
        }

        private void RemoveInvalidFactionEntries()
        {
            if (factionSettings == null)
            {
                return;
            }
            foreach (string key in factionSettings.Where(pair => string.IsNullOrEmpty(pair.Key) || pair.Value == null).Select(pair => pair.Key).ToList())
            {
                factionSettings.Remove(key);
            }
        }

        private static void NormalizeDictionary(Dictionary<string, float> weights)
        {
            if (weights == null)
            {
                return;
            }
            foreach (string key in weights.Keys.ToList())
            {
                float value = weights[key];
                weights[key] = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, 100f);
            }
        }

        private static bool DictionaryWeightsAreValid(Dictionary<string, float> weights)
        {
            return weights == null || weights.Values.All(value => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 100f);
        }

        private static float GetWeight(Dictionary<string, float> weights, string key)
        {
            if (weights == null || key == null || !weights.TryGetValue(key, out float value) || float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }
            return Math.Max(0f, value);
        }

        private static Dictionary<string, float> BuildLegacyDefaultXenotypeWeights()
        {
            Dictionary<string, float> weights = new Dictionary<string, float>
            {
                ["Hussar"] = 5f,
                ["Dirtmole"] = 5f,
                ["Genie"] = 2.5f,
                ["Neanderthal"] = 2.5f
            };
            float nonBaselinerTotal = 15f;
            if (DefDatabase<XenotypeDef>.GetNamedSilentFail("Starjack") != null)
            {
                weights["Starjack"] = 2.5f;
                nonBaselinerTotal += 2.5f;
            }
            weights[BaselinerKey] = 100f - nonBaselinerTotal;
            return weights;
        }

        private static IEnumerable<XenotypeDef> ActiveXenotypes()
        {
            return !ModsConfig.BiotechActive
                ? Enumerable.Empty<XenotypeDef>()
                : DefDatabase<XenotypeDef>.AllDefsListForReading.Where(xenotype => xenotype != null && xenotype != XenotypeDefOf.Baseliner);
        }
    }
}


