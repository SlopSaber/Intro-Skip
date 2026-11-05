using CountersPlus.Counters.Interfaces;
using CountersPlus.Custom;
using CountersPlus.Utils;
using TMPro;
using UnityEngine;

namespace IntroSkip.Counters
{
    public class SkipPromptCounter : ICounter
    {
        private readonly CanvasUtility _canvasUtility;
        private readonly CustomConfigModel _settings;
        private TMP_Text? _text;

        public SkipPromptCounter(CanvasUtility canvasUtility, CustomConfigModel settings)
        {
            _canvasUtility = canvasUtility;
            _settings = settings;
        }

        public void CounterInit()
        {
            _text = _canvasUtility.CreateTextFromSettings(_settings);
            _text.text = SkipPromptHost.PromptText;
            _text.color = _text.color.ColorWithAlpha(0f);
            _text.gameObject.SetActive(false);
            SkipPromptHost.Text = _text;
        }

        public void CounterDestroy()
        {
            if (SkipPromptHost.Text == _text)
            {
                SkipPromptHost.Text = null;
            }

            _text = null;
        }
    }
}
