using System.Collections.Generic;
using Microsoft.Windows.ApplicationModel.Resources;


namespace FluentSensors.Common.Localization
{
    // the technical terms:
    // labels like Cache, Rank or Logical Processors, from Strings/<language>/Terms.resw; in the app language, or in
    // english with TechnicalTermsInEnglish on, so a german or czech UI can keep the terms of datasheets and reviews
    // XAML reads them through x:Bind (x:Uid knows only the process language); fixed at startup like the language
    public static class AppTerms
    {
        private const string EnglishTag = "en-US";

        // the english terms, read once while no language override is set yet; (null with the setting off)
        private static Dictionary<string, string>? _english;
        private static ResourceMap? _map;

        // the setting the running process applied, see AppLanguage.StartupSetting
        public static bool StartedInEnglish { get; private set; }

        // from App(), before AppLanguage.Apply
        //
        // an override set by PrimaryLanguageOverride wins over the Language qualifier of every ResourceContext, so an
        // english lookup after it still returns the app language; the english set is read before it instead
        public static void Configure(bool useEnglish)
        {
            StartedInEnglish = useEnglish;
            if (!useEnglish) return;

            try
            {
                var manager = new ResourceManager();
                var context = manager.CreateResourceContext();
                context.QualifierValues["Language"] = EnglishTag;
                var map = manager.MainResourceMap.GetSubtree("Terms");

                var english = new Dictionary<string, string>();
                for (uint i = 0; i < map.ResourceCount; i++)
                {
                    var entry = map.GetValueByIndex(i, context);
                    english[entry.Key] = entry.Value.ValueAsString;
                }
                _english = english;
            }
            catch { /* the terms stay in the app language */ }
        }

        // a missing key shows the key itself, like AppStrings
        public static string Get(string key)
        {
            if (_english != null && _english.TryGetValue(key, out string? englishValue)) return englishValue;

            try
            {
                _map ??= new ResourceManager().MainResourceMap.GetSubtree("Terms");
                string? value = _map.TryGetValue(key)?.ValueAsString;
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch
            {
                return key;
            }
        }

        // the same count forms and placeholder rules as AppStrings
        public static string Plural(string key, int count) => Format($"{key}_{AppStrings.PluralForm(count)}", count);

        public static string Format(string key, params object[] args) => AppStrings.SafeFormat(Get(key), args);
    }
}
