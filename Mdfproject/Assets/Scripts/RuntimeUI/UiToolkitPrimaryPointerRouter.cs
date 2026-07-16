using System;
using UnityEngine.UIElements;

namespace MDF.Runtime.UI
{
    /// <summary>
    /// Registers a primary-button release route that can be disposed with its controller.
    /// Centralizing this rule prevents touch/mouse handlers from drifting across screens.
    /// </summary>
    public static class UiToolkitPrimaryPointerRouter
    {
        public static IDisposable Register(
            VisualElement element,
            Action<PointerUpEvent> handler,
            bool stopPropagation = true)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return new Registration(element, handler, stopPropagation);
        }

        public static bool IsPrimary(PointerUpEvent evt) =>
            evt != null && evt.isPrimary && evt.button == 0;

        private sealed class Registration : IDisposable
        {
            private VisualElement _element;
            private readonly Action<PointerUpEvent> _handler;
            private readonly bool _stopPropagation;
            private readonly EventCallback<PointerUpEvent> _callback;

            public Registration(
                VisualElement element,
                Action<PointerUpEvent> handler,
                bool stopPropagation)
            {
                _element = element;
                _handler = handler;
                _stopPropagation = stopPropagation;
                _callback = OnPointerUp;
                _element.RegisterCallback(_callback);
            }

            public void Dispose()
            {
                VisualElement element = _element;
                if (element == null)
                {
                    return;
                }

                _element = null;
                element.UnregisterCallback(_callback);
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (!IsPrimary(evt))
                {
                    return;
                }

                _handler(evt);
                if (_stopPropagation)
                {
                    evt.StopPropagation();
                }
            }
        }
    }
}
