using System;
using Microsoft.Windows.ApplicationModel.Resources;


namespace FluentSensors.Common.Localization
{
    // the strings code sets, from Strings/<language>/Resources.resw; XAML reads the same file through x:Uid
    // created on first use, after AppLanguage.Apply has picked the language
    public static class AppStrings
    {
        private static ResourceLoader? _loader;

        // a missing key shows the key itself, so a gap in a translation is visible instead of blank
        public static string Get(string key)
        {
            try
            {
                _loader ??= new ResourceLoader();
                string value = _loader.GetString(key);
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch
            {
                return key;
            }
        }

        // a count with its noun, from <key>_One, <key>_Few (2 to 4) and <key>_Many; the few form is the czech one,
        // a language without it repeats the many text there
        public static string Plural(string key, int count) => Format($"{key}_{PluralForm(count)}", count);

        public static string Format(string key, params object[] args) => SafeFormat(Get(key), args);

        // shared with AppTerms
        internal static string PluralForm(int count) => count == 1 ? "One" : count is >= 2 and <= 4 ? "Few" : "Many";

        // a translation with broken placeholders shows unformatted instead of throwing
        internal static string SafeFormat(string format, object[] args)
        {
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
    }
}
