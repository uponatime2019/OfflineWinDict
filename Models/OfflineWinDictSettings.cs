using OfflineWinDict.Helpers;

namespace OfflineWinDict.Models
{
    public class OfflineWinDictSettings : OfflineSettingsBase<OfflineWinDictSettings>
    {
        public override string AppId { get; set; } = "OfflineWinDict_bf1f640e";

        // Appearance: Light, Dark, System
        public string Theme { get; set; } = "Light";

        // Active dictionary (language code from DictionaryCatalog)
        public string DictionaryCode { get; set; } = "en";

        // Pronunciation behavior
        public bool AutoPlayPronunciation { get; set; } = false;
        public bool PreferTtsPronunciation { get; set; } = false;
        public string TtsVoiceId { get; set; } = "";

        // Navigation
        public string LastPage { get; set; } = "Home";
        public bool RestoreLastPage { get; set; } = true;
    }
}
