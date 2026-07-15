using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MDF.Runtime.UI
{
    /// <summary>Applies resolved attack-card state to the live uGUI controls.</summary>
    public sealed class AttackMonsterCardView : MonoBehaviour
    {
        private Image _portrait;
        private TMP_Text _count;
        private Image _background;
        private Button _button;
        private Color _normal;
        private Color _selected;
        private Color _empty;

        public void Configure(
            Image portrait,
            TMP_Text count,
            Image background,
            Button button,
            Color normal,
            Color selected,
            Color empty)
        {
            _portrait = portrait;
            _count = count;
            _background = background;
            _button = button;
            _normal = normal;
            _selected = selected;
            _empty = empty;
        }

        public void Apply(AttackMonsterCardPolicyState state, bool selected)
        {
            if (_count != null)
            {
                _count.text = state.CountText;
                Color color = _count.color;
                color.a = state.CanInteract ? 1f : 0.3f;
                _count.color = color;
            }

            if (_portrait != null)
            {
                Color color = _portrait.color;
                color.a = state.CanInteract ? 1f : 0.3f;
                _portrait.color = color;
            }

            if (_background != null)
            {
                _background.color = state.IsExhausted
                    ? _empty
                    : selected ? _selected : _normal;
            }

            if (_button != null)
            {
                _button.interactable = state.CanInteract;
            }
        }

        public void ApplyEmpty()
        {
            if (_portrait != null)
            {
                _portrait.sprite = null;
                _portrait.color = _empty;
            }
            if (_count != null)
            {
                _count.text = string.Empty;
            }
            if (_background != null)
            {
                _background.color = _empty;
            }
            if (_button != null)
            {
                _button.interactable = false;
            }
        }
    }
}
