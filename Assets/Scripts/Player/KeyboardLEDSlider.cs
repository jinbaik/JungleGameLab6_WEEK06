using System;

using UnityEngine;
using UnityEngine.UI;

namespace Game.Player
{
    public sealed class KeyboardLEDSlider : Slider
    {
        public event Action<float> ValueChanged;

        /// <summary>
        /// input을 소수 첫째 자리 단위로 적용하고 실제 사용자 변경을 C# 이벤트로 알린다.
        /// sendCallback이 false이면 UI 동기화만 수행하며 기존 Slider 표시와 값 제한을 유지한다.
        /// </summary>
        protected override void Set(float input, bool sendCallback = true)
        {
            float previous = value;
            base.Set(Mathf.Round(input * 10f) / 10f, sendCallback);
            if (sendCallback && !Mathf.Approximately(previous, value))
                ValueChanged?.Invoke(value);
        }
    }
}
