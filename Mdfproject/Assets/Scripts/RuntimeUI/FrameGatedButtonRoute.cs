using System;
using UnityEngine.UI;

namespace MDF.Runtime.UI
{
    /// <summary>Routes a uGUI click and fallback pointer release through one frame gate.</summary>
    public sealed class FrameGatedButtonRoute : IDisposable
    {
        private Button _button;
        private readonly FrameDispatchGate _gate;
        private readonly Func<bool> _canDispatch;
        private readonly Func<bool> _dispatch;

        public FrameGatedButtonRoute(
            Button button,
            FrameDispatchGate gate,
            Func<bool> canDispatch,
            Func<bool> dispatch)
        {
            _button = button ?? throw new ArgumentNullException(nameof(button));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _canDispatch = canDispatch ?? throw new ArgumentNullException(nameof(canDispatch));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            _button.onClick.AddListener(OnClicked);
        }

        public bool TryDispatch(int frame)
        {
            if (!_canDispatch() || !_gate.TryBegin(frame))
            {
                return false;
            }

            bool accepted = _dispatch();
            if (!accepted)
            {
                _gate.Complete();
            }
            return accepted;
        }

        public void Dispose()
        {
            Button button = _button;
            if (button == null)
            {
                return;
            }

            _button = null;
            button.onClick.RemoveListener(OnClicked);
        }

        private void OnClicked() => TryDispatch(UnityEngine.Time.frameCount);
    }
}
