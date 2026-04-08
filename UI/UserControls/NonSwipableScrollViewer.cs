using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace PinayPalBackupManager.UI.UserControls
{
    public class NonSwipableScrollViewer : ScrollViewer
    {
        private Point _startPoint;
        private bool _isScrolling = false;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _startPoint = e.GetPosition(this);
                _isScrolling = false;
            }
            base.OnPointerPressed(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !_isScrolling)
            {
                var currentPoint = e.GetPosition(this);
                var delta = currentPoint - _startPoint;

                if (Math.Abs(delta.X) > Math.Abs(delta.Y) && Math.Abs(delta.X) > 10)
                {
                    // Horizontal swipe detected, do not initiate scroll
                    return;
                }
                if (Math.Abs(delta.Y) > 10)
                {
                    _isScrolling = true;
                }
            }

            if (_isScrolling || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                base.OnPointerMoved(e);
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _isScrolling = false;
            base.OnPointerReleased(e);
        }
    }
}
