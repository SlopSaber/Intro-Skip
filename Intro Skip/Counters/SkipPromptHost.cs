using TMPro;

namespace IntroSkip.Counters
{
    /// <summary>
    /// Holds the skip prompt created by the Counters+ counter, when that counter is enabled.
    /// Kept free of Counters+ types so Intro Skip runs without it.
    /// </summary>
    internal static class SkipPromptHost
    {
        internal const string PromptText = "Press Trigger To Skip";

        internal static TMP_Text? Text { get; set; }
    }
}
