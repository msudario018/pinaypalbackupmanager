using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace PinayPalBackupManager.UI.UserControls
{
    /// <summary>
    /// A settings card that stays collapsed until the user clicks its header.
    /// Only the header is realised while collapsed, so large forms cost nothing until opened.
    /// </summary>
    public partial class CollapsibleCardControl : UserControl
    {
        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<CollapsibleCardControl, string>(nameof(Title), string.Empty);

        public static readonly StyledProperty<string> SubtitleProperty =
            AvaloniaProperty.Register<CollapsibleCardControl, string>(nameof(Subtitle), string.Empty);

        public static readonly StyledProperty<string> IconDataProperty =
            AvaloniaProperty.Register<CollapsibleCardControl, string>(nameof(IconData), string.Empty);

        public static readonly StyledProperty<string> AccentProperty =
            AvaloniaProperty.Register<CollapsibleCardControl, string>(nameof(Accent), string.Empty);

        public static readonly StyledProperty<string> BadgeProperty =
            AvaloniaProperty.Register<CollapsibleCardControl, string>(nameof(Badge), string.Empty);

        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<CollapsibleCardControl, bool>(nameof(IsExpanded), false);

        public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

        /// <summary>SVG path data rendered in the header icon badge.</summary>
        public string IconData { get => GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }

        /// <summary>Accent colour (hex) used for the stripe and title.</summary>
        public string Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

        public string Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }

        public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

        /// <summary>Raised whenever the card opens or closes.</summary>
        public event EventHandler<bool>? ExpansionChanged;

        private Border? _cardRoot;
        private Button? _headerButton;
        private Border? _contentHost;
        private TextBlock? _titleText;
        private TextBlock? _subtitleText;
        private TextBlock? _badgeText;
        private Border? _badgeBorder;
        private PathIcon? _headerIcon;
        private PathIcon? _chevron;
        private Border? _accentStripe;
        public CollapsibleCardControl()
        {
            AvaloniaXamlLoader.Load(this);

            _cardRoot = this.FindControl<Border>("CardRoot");
            _headerButton = this.FindControl<Button>("HeaderButton");
            _contentHost = this.FindControl<Border>("ContentHost");
            _titleText = this.FindControl<TextBlock>("HeaderTitleText");
            _subtitleText = this.FindControl<TextBlock>("HeaderSubtitleText");
            _badgeText = this.FindControl<TextBlock>("HeaderBadgeText");
            _badgeBorder = this.FindControl<Border>("HeaderBadge");
            _headerIcon = this.FindControl<PathIcon>("HeaderIcon");
            _chevron = this.FindControl<PathIcon>("ChevronIcon");
            _accentStripe = this.FindControl<Border>("AccentStripe");

            if (_headerButton != null)
            {
                _headerButton.Click += (_, _) => Toggle();
            }

            TitleProperty.Changed.AddClassHandler<CollapsibleCardControl>((c, _) => c.ApplyHeader());
            SubtitleProperty.Changed.AddClassHandler<CollapsibleCardControl>((c, _) => c.ApplyHeader());
            IconDataProperty.Changed.AddClassHandler<CollapsibleCardControl>((c, _) => c.ApplyHeader());
            AccentProperty.Changed.AddClassHandler<CollapsibleCardControl>((c, _) => c.ApplyHeader());
            BadgeProperty.Changed.AddClassHandler<CollapsibleCardControl>((c, _) => c.ApplyHeader());
            IsExpandedProperty.Changed.AddClassHandler<CollapsibleCardControl>((c, _) => c.ApplyExpansion());

            ApplyHeader();
            ApplyExpansion();
        }

        /// <summary>Flips the open/closed state.</summary>
        public void Toggle()
        {
            IsExpanded = !IsExpanded;
        }

        /// <summary>Opens or closes the card programmatically.</summary>
        public void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
        }
        private IBrush AccentBrush() => new SolidColorBrush(Color.Parse(string.IsNullOrWhiteSpace(Accent) ? "#38BDF8" : Accent));

        private IBrush IdleBrush()
        {
            if (Application.Current != null && Application.Current.TryFindResource("AppBorder", out var res))
            {
                if (res is IBrush b) return b;
            }
            return new SolidColorBrush(Color.Parse("#253550"));
        }

        private void ApplyHeader()
        {
            if (_titleText != null)
            {
                _titleText.Text = Title ?? "";
                _titleText.Foreground = AccentBrush();
            }

            if (_subtitleText != null)
            {
                _subtitleText.Text = Subtitle ?? "";
                _subtitleText.IsVisible = !string.IsNullOrWhiteSpace(Subtitle);
            }

            if (_headerIcon != null)
            {
                _headerIcon.Data = string.IsNullOrWhiteSpace(IconData) ? null : Geometry.Parse(IconData);
                _headerIcon.Foreground = AccentBrush();
            }

            if (_badgeText != null) _badgeText.Text = Badge ?? "";
            if (_badgeBorder != null) _badgeBorder.IsVisible = !string.IsNullOrWhiteSpace(Badge);
        }

        private void ApplyExpansion()
        {
            var accent = AccentBrush();
            var idle = IdleBrush();

            if (_contentHost != null) _contentHost.IsVisible = IsExpanded;
            if (_cardRoot != null) _cardRoot.BorderBrush = IsExpanded ? accent : idle;
            if (_accentStripe != null) _accentStripe.Background = IsExpanded ? accent : idle;

            // Point the chevron down while the card is open.
            if (_chevron != null) _chevron.RenderTransform = new RotateTransform(IsExpanded ? 180 : 0);

            ExpansionChanged?.Invoke(this, IsExpanded);
        }
    }
}