using LudeonTK;
using Verse;

namespace MixedPeoplesFactions
{
    [StaticConstructorOnStartup]
    public static class MPF_Startup
    {
        static MPF_Startup()
        {
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                FRD_FactionRegistry.Refresh();

                MPF_Settings settings = MPF_Mod.Settings;
                if (settings != null)
                {
                    settings.MigrateLegacySettings();
                    settings.NormalizeAllSettings();
                }

                MPF_Injector.CaptureBaselines();
                MPF_Injector.ApplyAll(settings);
                FRD_HarmonyBootstrap.Apply();
            });
        }

        [DebugAction("Faction Race Diversity", "Output configured faction race report", allowedGameStates = AllowedGameStates.Playing)]
        private static void OutputConfiguredFactionReport()
        {
            FRD_FactionRegistry.Refresh();
            Log.Message(MPF_Injector.BuildDebugReport());
        }

        [DebugAction("Faction Race Diversity", "Validate settings and reapply overrides", allowedGameStates = AllowedGameStates.Playing)]
        private static void ValidateSettingsAndRebuild()
        {
            FRD_FactionRegistry.Refresh();
            bool valid = MPF_Injector.ValidateSettingsAndRebuild();
            Log.Message(valid ? "FRD_DebugSettingsValid".Translate().ToString() : "FRD_DebugSettingsInvalid".Translate().ToString());
            Log.Message(MPF_Injector.BuildDebugReport());
        }
    }
}
