using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Threading.Tasks;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class NotificationItem : UserControl
    {
        private Point _startPoint;
        private bool _isDragging;
        private double _dismissThreshold = 100; // Pixels to swipe for dismiss

        public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<NotificationItem, string>(nameof(Title));
        public static readonly StyledProperty<string> MessageProperty = AvaloniaProperty.Register<NotificationItem, string>(nameof(Message));
        public static readonly StyledProperty<string> TimestampProperty = AvaloniaProperty.Register<NotificationItem, string>(nameof(Timestamp));
        public static readonly StyledProperty<IBrush> IconBrushProperty = AvaloniaProperty.Register<NotificationItem, IBrush>(nameof(IconBrush));
        public static readonly StyledProperty<StreamGeometry> IconDataProperty = AvaloniaProperty.Register<NotificationItem, StreamGeometry>(nameof(IconData));

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Message
        {
            get => GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public string Timestamp
        {
            get => GetValue(TimestampProperty);
            set => SetValue(TimestampProperty, value);
        }

        public IBrush IconBrush
        {
            get => GetValue(IconBrushProperty);
            set => SetValue(IconBrushProperty, value);
        }

        public StreamGeometry IconData
        {
            get => GetValue(IconDataProperty);
            set => SetValue(IconDataProperty, value);
        }

        public event EventHandler? Dismissed;

        public NotificationItem()
        {
            InitializeComponent();
            this.PointerPressed += OnPointerPressed;
            this.PointerMoved += OnPointerMoved;
            this.PointerReleased += OnPointerReleased;
            this.PointerExited += OnPointerExited;
        }

        private void InitializeComponent()
        {
            Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TitleProperty) this.FindControl<TextBlock>("PART_Title").Text = Title;
            if (change.Property == MessageProperty) this.FindControl<TextBlock>("PART_Message").Text = Message;
            if (change.Property == TimestampProperty) this.FindControl<TextBlock>("PART_Timestamp").Text = Timestamp;
            if (change.Property == IconBrushProperty) this.FindControl<PathIcon>("PART_Icon").Foreground = IconBrush;
            if (change.Property == IconDataProperty) this.FindControl<PathIcon>("PART_Icon").Data = IconData;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isDragging = true;
                _startPoint = e.GetPosition(this.Parent as Visual);
                e.Handled = true;
            }
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isDragging)
            {
                var currentPoint = e.GetPosition(this.Parent as Visual);
                var deltaX = currentPoint.X - _startPoint.X;
                this.RenderTransform = new TranslateTransform(deltaX, 0);
                this.Opacity = 1 - Math.Abs(deltaX) / _dismissThreshold * 0.5;
            }
        }

        private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                var currentPoint = e.GetPosition(this.Parent as Visual);
                var deltaX = currentPoint.X - _startPoint.X;

                if (Math.Abs(deltaX) > _dismissThreshold)
                {
                    await AnimateDismiss(deltaX > 0);
                    Dismissed?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    await AnimateReturn();
                }
                e.Handled = true;
            }
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                AnimateReturn();
            }
        }

        private async Task AnimateDismiss(bool toRight)
        {
            double targetX = toRight ? this.Bounds.Width : -this.Bounds.Width;
            // Quick slide-out animation
            this.RenderTransform = new TranslateTransform(targetX, 0);
            this.Opacity = 0;
            await Task.Delay(200); // Animation duration
        }

        private async Task AnimateReturn()
        {
            // Spring back animation
            this.RenderTransform = new TranslateTransform(0, 0);
            this.Opacity = 1;
            await Task.Delay(200); // Animation duration
        }
    }
}
