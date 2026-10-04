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

        private static bool _useEnglish;
        private static ResourceMap? _map;
        private static ResourceContext? _context;

        // from App(), before the first lookup
        public static void Configure(bool useEnglish) => _useEnglish = useEnglish;

        // a missing key shows the key itself, like AppStrings
        public static string Get(string key)
        {
            try
            {
                if (_map == null)
                {
                    var manager = new ResourceManager();
                    _context = manager.CreateResourceContext();
                    if (_useEnglish) _context.QualifierValues["Language"] = EnglishTag;
                    _map = manager.MainResourceMap.GetSubtree("Terms");
                }

                string? value = _map.TryGetValue(key, _context)?.ValueAsString;
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
