using System.Collections.Generic;

using Colossal;
using Colossal.IO.AssetDatabase;

using Game.Modding;
using Game.Settings;
using Game.UI;

using Unity.Mathematics;

namespace ByeByeHomelessMod
{
    [FileLocation("ModSettings/ByeByeHomeless/setting.coc")]
    public class Setting : ModSetting
    {
        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISlider(min = 0.1f, max = 10, step = 0.05f, scalarMultiplier = 1, unit = Unit.kFloatSingleFraction)]
        public float TimeInterval { get; set; } = 1.5f;

        public override void SetDefaults()
        {
            TimeInterval = 1.5f;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting _mSetting;
        public LocaleEN(Setting setting)
        {
            _mSetting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { _mSetting.GetSettingsLocaleID(), "Bye Bye Homeless" },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.TimeInterval)), "Time Interval" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.TimeInterval)), "Decide how often to clear the homeless population. When the period is equal to 1.5, it is 1.5 hours to clean the population." }
            };
        }

        public void Unload()
        {
        }
    }
}