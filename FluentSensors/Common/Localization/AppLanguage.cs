using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Windows.Globalization;
using Windows.System.UserProfile;


namespace FluentSensors.Common.Localization
{
    // the app language:
    // the languages with a Strings folder, and the override that picks one of them for the whole process
    // applied once before the first resource lookup, so a change takes effect on the next start
    public static class AppLanguage
    {
        // the setting value that follows the Windows display language
        public const string SystemDefault = "Default";

        // tag and name; the name stays in its own language, so it is never translated
        public static readonly IReadOnlyList<(string Tag, string Name)> Supported = new[]
        {
            ("en-US", "English"),
            ("de-DE", "Deutsch"),
            ("cs-CZ", "Čeština"),
        };

        // the setting the running process applied; the settings page compares against it to offer a restart
        public static string StartupSetting { get; private set; } = SystemDefault;

        public static bool IsSupported(string? tag) =>
            Supported.Any(language => string.Equals(language.Tag, tag, StringComparison.OrdinalIgnoreCase));

        // an unknown tag counts as Default, the same way Apply treats it
        public static string Normalize(string? tag) => IsSupported(tag) ? tag! : SystemDefault;

        // whether a setting ends up in english on the next start; Default walks the Windows language list the way the
        // resource lookup does, the first language with a Strings folder wins, and none of them means english
        public static bool ResolvesToEnglish(string? setting)
        {
            if (IsSupported(setting)) return IsEnglish(setting!);

            foreach (string language in GlobalizationPreferences.Languages)
            {
                string primary = language.Split('-')[0];
                var match = Supported.FirstOrDefault(supported =>
                    string.Equals(supported.Tag.Split('-')[0], primary, StringComparison.OrdinalIgnoreCase));
                if (match.Tag != null) return IsEnglish(match.Tag);
            }
            return true;
        }

        private static bool IsEnglish(string tag) => tag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        // an empty override clears one a packaged install persisted, so Default really follows Windows again
        public static void Apply(string? tag)
        {
            StartupSetting = Normalize(tag);

            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = IsSupported(tag) ? tag : "";
            }
            catch { /* the Windows language stays in charge */ }
        }
    }
}
