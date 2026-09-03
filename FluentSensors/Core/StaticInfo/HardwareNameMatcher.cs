using System;
using System.Collections.Generic;


namespace FluentSensors.Core.StaticInfo
{
    // best-effort name matching between an LHM-reported HardwareName and a WMI-reported device name, for hardware
    // categories where more than one instance can exist (GPU, Storage, Network) and WinStaticInfoService exposes no
    // shared identifier (PnpDeviceId etc.) on the LHM side to match against directly
    // not exact: scores each candidate by how many whole words from hardwareName also appear in its name; good
    // enough for typical 1-2 device systems, can occasionally mismatch multiple visually identical models (e.g. two
    // GPUs of the exact same model)
    public static class HardwareNameMatcher
    {
        // returns the best-scoring candidate for hardwareName, or default(T) if candidates is empty
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
