using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Windows.Globalization;


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

        public static bool IsSupported(string? tag) =>
            Supported.Any(language => string.Equals(language.Tag, tag, StringComparison.OrdinalIgnoreCase));

        // an empty override clears one a packaged install persisted, so Default really follows Windows again
        public static void Apply(string? tag)
        {
            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = IsSupported(tag) ? tag : "";
            }
            catch { /* the Windows language stays in charge */ }
        }
    }
}
