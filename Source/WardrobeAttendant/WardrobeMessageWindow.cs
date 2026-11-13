using System;
using UnityEngine;
using Verse;

namespace WardrobeAttendant
{
    public class WardrobeMessageWindow : Window
    {
        private const float MinWidth = 440f;
        private const float MaxWidth = 760f;
        private const float TextWidthReserve = 80f;
        private const float HeightReserve = 110f;

        private readonly string _message;
        private Vector2 _calculatedSize;

        public WardrobeMessageWindow(string message)
        {
            _message = message ?? string.Empty;

            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = false;
            draggable = false;
            resizeable = false;
        }

        public override Vector2 InitialSize
        {
            get
            {
                if (_calculatedSize == default)
                {
                    CalculateSize();
                }

                return _calculatedSize;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            Rect textRect = inRect.ContractedBy(24f);

            Widgets.Label(textRect, _message);

            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void CalculateSize()
        {
            Text.Font = GameFont.Small;

            float preferredWidth =
                Text.CalcSize(_message).x + TextWidthReserve;

            float width = Mathf.Clamp(preferredWidth, MinWidth, MaxWidth);

            float textHeight = Text.CalcHeight(
                _message,
                width - TextWidthReserve);

            float height = textHeight + HeightReserve;

            _calculatedSize = new Vector2(width, height);
        }
    }
}