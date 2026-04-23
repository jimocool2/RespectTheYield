using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace RespectTheYield
{
    [FileLocation(nameof(RespectTheYield))]
    [SettingsUIGroupOrder(kGeneralGroup, kRulesGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kRulesGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kRulesGroup = "Rules";

        public Setting(IMod mod) : base(mod) { }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool ModEnabled { get; set; } = true;

        [SettingsUISection(kSection, kRulesGroup)]
        public bool RightHandRuleEnabled { get; set; } = true;

        [SettingsUISection(kSection, kRulesGroup)]
        public bool LeftTurnYieldEnabled { get; set; } = true;

        [SettingsUISection(kSection, kRulesGroup)]
        public bool UnsafeLaneYieldEnabled { get; set; } = true;

        public override void SetDefaults()
        {
            ModEnabled = true;
            RightHandRuleEnabled = true;
            LeftTurnYieldEnabled = true;
            UnsafeLaneYieldEnabled = true;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Respect The Yield" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ModEnabled)), "Enable mod" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ModEnabled)), "Toggle the entire mod on or off." },

                { m_Setting.GetOptionGroupLocaleID(Setting.kRulesGroup), "Rules" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.RightHandRuleEnabled)), "Right-Hand Rule" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.RightHandRuleEnabled)), "Vehicles yield to traffic approaching from the right at uncontrolled intersections (equal-priority roads only)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.LeftTurnYieldEnabled)), "Left-Turn Yield" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.LeftTurnYieldEnabled)), "Vehicles turning left yield to oncoming traffic going straight or turning right (equal-priority roads only)." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UnsafeLaneYieldEnabled)), "Unsafe Lane Yield" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UnsafeLaneYieldEnabled)), "Vehicles on unsafe lanes (e.g. side roads merging without a yield sign) yield to all other traffic at the intersection." },
            };
        }

        public void Unload() { }
    }
}
