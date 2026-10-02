using System;
using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // the hardware name matcher:
    // matches an LHM HardwareName to a WMI device where several can exist (GPU, storage, network) and LHM has
    // no shared id like PnpDeviceId
    // scores the whole words in common; fine for one or two devices, two identical models can swap
    public static class HardwareNameMatcher
    {
        // default(T) without candidates
        public static T FindBestMatch<T>(string hardwareName, IEnumerable<T> candidates, Func<T, string> getCandidateName)
        {
            T best = default;
            int bestScore = -1;

            foreach (var candidate in candidates)
            {
                int score = ScoreMatch(hardwareName, getCandidateName(candidate));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static int ScoreMatch(string hardwareName, string candidateName)
        {
            if (string.IsNullOrWhiteSpace(hardwareName) || string.IsNullOrWhiteSpace(candidateName)) return 0;

            var hardwareWords = hardwareName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int score = 0;

            foreach (var word in hardwareWords)
            {
                if (candidateName.Contains(word, StringComparison.OrdinalIgnoreCase)) score++;
            }

            return score;
        }
    }
}
