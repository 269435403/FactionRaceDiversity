using System.Collections.Generic;
using Verse;

namespace MixedPeoplesFactions
{
    public sealed class RaceXenotypeSettings : IExposable
    {
        public bool overrideEnabled = true;
        public Dictionary<string, float> weights = new Dictionary<string, float>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref overrideEnabled, "overrideEnabled", true);
            Scribe_Collections.Look(ref weights, "weights", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && weights == null)
            {
                weights = new Dictionary<string, float>();
            }
        }

        public RaceXenotypeSettings DeepCopy()
        {
            return new RaceXenotypeSettings
            {
                overrideEnabled = overrideEnabled,
                weights = weights == null ? new Dictionary<string, float>() : new Dictionary<string, float>(weights)
            };
        }
    }

    public sealed class FactionRaceSettings : IExposable
    {
        // Schema 1/2 compatibility fields. They remain serialized so old settings can be read.
        public bool xenotypeOverrideEnabled;
        public Dictionary<string, float> xenotypeWeights = new Dictionary<string, float>();

        public bool raceOverrideEnabled;
        public bool autoBalanceRaces;
        public Dictionary<string, float> raceWeights = new Dictionary<string, float>();
        public Dictionary<string, RaceXenotypeSettings> xenotypeSettingsByRace = new Dictionary<string, RaceXenotypeSettings>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref xenotypeOverrideEnabled, "xenotypeOverrideEnabled", false);
            Scribe_Collections.Look(ref xenotypeWeights, "xenotypeWeights", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref raceOverrideEnabled, "raceOverrideEnabled", false);
            Scribe_Values.Look(ref autoBalanceRaces, "autoBalanceRaces", false);
            Scribe_Collections.Look(ref raceWeights, "raceWeights", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref xenotypeSettingsByRace, "xenotypeSettingsByRace", LookMode.Value, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                xenotypeWeights = xenotypeWeights ?? new Dictionary<string, float>();
                raceWeights = raceWeights ?? new Dictionary<string, float>();
                xenotypeSettingsByRace = xenotypeSettingsByRace ?? new Dictionary<string, RaceXenotypeSettings>();
            }
        }

        public void MigrateLegacyXenotypesToHuman()
        {
            xenotypeWeights = xenotypeWeights ?? new Dictionary<string, float>();
            xenotypeSettingsByRace = xenotypeSettingsByRace ?? new Dictionary<string, RaceXenotypeSettings>();
            if (xenotypeWeights.Count == 0 || xenotypeSettingsByRace.ContainsKey("Human"))
            {
                return;
            }

            xenotypeSettingsByRace["Human"] = new RaceXenotypeSettings
            {
                overrideEnabled = xenotypeOverrideEnabled,
                weights = new Dictionary<string, float>(xenotypeWeights)
            };
        }

        public RaceXenotypeSettings GetRaceXenotypeSettings(string raceDefName)
        {
            if (xenotypeSettingsByRace == null || string.IsNullOrEmpty(raceDefName))
            {
                return null;
            }
            xenotypeSettingsByRace.TryGetValue(raceDefName, out RaceXenotypeSettings settings);
            return settings;
        }

        public FactionRaceSettings DeepCopy()
        {
            Dictionary<string, RaceXenotypeSettings> raceXenotypes = new Dictionary<string, RaceXenotypeSettings>();
            if (xenotypeSettingsByRace != null)
            {
                foreach (KeyValuePair<string, RaceXenotypeSettings> pair in xenotypeSettingsByRace)
                {
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                    {
                        raceXenotypes[pair.Key] = pair.Value.DeepCopy();
                    }
                }
            }

            return new FactionRaceSettings
            {
                xenotypeOverrideEnabled = xenotypeOverrideEnabled,
                xenotypeWeights = xenotypeWeights == null ? new Dictionary<string, float>() : new Dictionary<string, float>(xenotypeWeights),
                raceOverrideEnabled = raceOverrideEnabled,
                autoBalanceRaces = autoBalanceRaces,
                raceWeights = raceWeights == null ? new Dictionary<string, float>() : new Dictionary<string, float>(raceWeights),
                xenotypeSettingsByRace = raceXenotypes
            };
        }
    }
}
