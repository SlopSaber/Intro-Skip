using CountersPlus.Custom;
using TMPro;

namespace IntroSkip.Counters
{
    public class SkipPromptCounterPreview : ICounterPreview
    {
        public void Render(CounterPreviewContext context)
        {
            TMP_Text text = context.CreateText();
            text.text = SkipPromptHost.PromptText;
        }
    }
}
