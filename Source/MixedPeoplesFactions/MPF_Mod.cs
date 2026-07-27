using UnityEngine;
using Verse;

namespace MixedPeoplesFactions
{
    public sealed class MPF_Mod : Mod
    {
        public static MPF_Mod Instance;
        public static MPF_Settings Settings;

        public MPF_Mod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<MPF_Settings>();
        }

        public override string SettingsCategory()
        {
            return "FRD_ModName".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings?.DrawWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            if (Settings != null)
            {
                Settings.NormalizeAllSettings();
            }
            base.WriteSettings();
            MPF_Injector.ApplyAll(Settings);
        }
    }
}
