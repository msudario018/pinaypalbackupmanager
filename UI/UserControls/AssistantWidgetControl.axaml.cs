using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PinayPalBackupManager.Services;

namespace PinayPalBackupManager.UI.UserControls
{
    public partial class AssistantWidgetControl : UserControl
    {
        public static Action? RequestOpenSettings { get; set; }

        private DispatcherTimer? _bubbleTimer;
        private bool _isProcessing = false;

        /// <summary>
        /// How close (in px) the viewport must be to the end of the message list to
        /// count as "following along". Small enough to feel instant, large enough
        /// that a 1-2px rounding gap doesn't flip the flag off.
        /// </summary>
        private const double ScrollStickThreshold = 24;

        /// <summary>
        /// When true, new messages auto-scroll into view. Set from the ScrollViewer
        /// so scrolling up to read older messages suppresses auto-scroll until the
        /// user returns to the bottom.
        /// </summary>
        private bool _stickToBottom = true;

        public AssistantWidgetControl()
        {
            InitializeComponent();
            InitializeWidget();
        }

        private void InitializeWidget()
        {
            // Subscribe to AI events
            AIAssistantService.OnNotificationBubble += OnSpeechBubbleTriggered;
            AIAssistantService.OnConfigChanged += config =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsVisible = config.EnableFloatingWidget;
                    UpdateProviderBadge();
                });
            };

            IsVisible = AIAssistantService.Config.EnableFloatingWidget;

            // Wire trigger buttons
            BtnAvatarTrigger.Click += (s, e) => ToggleDrawer();
            BtnCloseDrawer.Click += (s, e) => CloseDrawer();
            BtnCloseBubble.Click += (s, e) => HideSpeechBubble();
            BtnOpenFromBubble.Click += (s, e) =>
            {
                HideSpeechBubble();
                OpenDrawer();
            };

            BtnClearChat.Click += (s, e) => ClearChat();
            BtnSend.Click += async (s, e) => await SendUserMessageAsync();

            TxtInput.KeyDown += async (s, e) =>
            {
                if (e.Key == Key.Enter && !_isProcessing)
                {
                    e.Handled = true;
                    await SendUserMessageAsync();
                }
            };

            // Wire Quick Settings toggle & actions
            BtnToggleQuickSettings.Click += (s, e) =>
            {
                QuickSettingsBorder.IsVisible = !QuickSettingsBorder.IsVisible;
            };

            BtnOpenFullSettings.Click += (s, e) =>
            {
                CloseDrawer();
                RequestOpenSettings?.Invoke();
            };

            BtnProvHybrid.Click += (s, e) => SetProvider("hybrid");
            BtnProvOllama.Click += (s, e) => SetProvider("ollama");
            BtnProvCloud.Click += (s, e) => SetProvider("cloud");
            BtnProvHeuristics.Click += (s, e) => SetProvider("heuristics");

            BtnQuickTestOllama.Click += async (s, e) =>
            {
                TxtQuickSettingsStatus.Text = "Testing Ollama connection...";
                var (ok, msg) = await AIAssistantService.TestOllamaConnectionAsync();
                TxtQuickSettingsStatus.Text = msg;
            };

            // Horizontal Chip scrolling (Mouse Wheel & Arrow Buttons)
            BtnScrollChipsLeft.Click += (s, e) =>
            {
                var cur = ChipsScrollViewer.Offset.X;
                ChipsScrollViewer.Offset = new Vector(Math.Max(0, cur - 140), 0);
            };

            BtnScrollChipsRight.Click += (s, e) =>
            {
                var cur = ChipsScrollViewer.Offset.X;
                ChipsScrollViewer.Offset = new Vector(cur + 140, 0);
            };

            ChipsScrollViewer.PointerWheelChanged += (s, e) =>
            {
                var cur = ChipsScrollViewer.Offset.X;
                var delta = e.Delta.Y * 40;
                ChipsScrollViewer.Offset = new Vector(Math.Max(0, cur - delta), 0);
                e.Handled = true;
            };

            // Track whether the user is following the tail of the conversation, so a long
            // response doesn't yank them away from history they're reading.
            MessagesScrollViewer.ScrollChanged += (s, e) => _stickToBottom = IsNearBottom();

            // Wire all 12 Quick Prompt Chips
            ChipHealth.Click += async (s, e) => await SubmitPromptAsync("How is the system health?");
            ChipDisk.Click += async (s, e) => await SubmitPromptAsync("How much disk space is left?");
            ChipHistory.Click += async (s, e) => await SubmitPromptAsync("Show recent backup history");
            ChipRunAll.Click += async (s, e) => await SubmitPromptAsync("Run all backups now");
            ChipFtp.Click += async (s, e) => await SubmitPromptAsync("Run Website FTP backup");
            ChipSql.Click += async (s, e) => await SubmitPromptAsync("Run SQL database backup");
            ChipMailchimp.Click += async (s, e) => await SubmitPromptAsync("Run Mailchimp sync");
            ChipTunnel.Click += async (s, e) => await SubmitPromptAsync("Check Cloudflare and Tailscale tunnel status");
            ChipTailscale.Click += async (s, e) => await SubmitPromptAsync("Check Tailscale mesh status");
            ChipEmail.Click += async (s, e) => await SubmitPromptAsync("Send test email alert");
            ChipErrors.Click += async (s, e) => await SubmitPromptAsync("Inspect recent backup errors");
            ChipEmergency.Click += async (s, e) => await SubmitPromptAsync("Emergency stop all tasks");

            UpdateProviderBadge();
            AddInitialWelcomeMessage();
        }

        private void SetProvider(string provider)
        {
            var cfg = AIAssistantService.Config;
            cfg.Provider = provider;
            AIAssistantService.SaveConfig(cfg);
            UpdateProviderBadge();
            TxtQuickSettingsStatus.Text = $"Active provider set to: {provider.ToUpperInvariant()}";
        }

        private void UpdateProviderBadge()
        {
            var provider = AIAssistantService.Config.Provider?.ToUpperInvariant() ?? "HYBRID";
            TxtProviderBadge.Text = provider;
        }

        private void AddInitialWelcomeMessage()
        {
            AddAssistantBubble(
                "Hello! I am your **PinayPal AI Assistant**.\n\n" +
                "I monitor your backup daemons, server health, and storage in real time. " +
                "I operate strictly behind a **Zero-Leak Sanitizer** so your credentials and database records are never exposed.\n\n" +
                "How can I help you today?", null);
        }

        private void OnSpeechBubbleTriggered(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ChatDrawerBorder.IsVisible) return; // Don't show popup bubble if chat is already open

                TxtBubbleContent.Text = message;
                SpeechBubbleBorder.IsVisible = true;

                _bubbleTimer?.Stop();
                _bubbleTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(8)
                };
                _bubbleTimer.Tick += (s, e) =>
                {
                    _bubbleTimer.Stop();
                    SpeechBubbleBorder.IsVisible = false;
                };
                _bubbleTimer.Start();
            });
        }

        private void HideSpeechBubble()
        {
            _bubbleTimer?.Stop();
            SpeechBubbleBorder.IsVisible = false;
        }

        /// <summary>
        /// Scrolls the message list to the newest message.
        ///
        /// This has to be deferred: calling ScrollToEnd() straight after adding a
        /// child is a no-op, because Avalonia has not measured the new content yet
        /// so Extent/Viewport still describe the previous layout. Posting at
        /// DispatcherPriority.Loaded runs after the measure/arrange pass, at which
        /// point the offset can actually be applied.
        ///
        /// Sticky behaviour: if the user has scrolled up to read history we leave
        /// them where they are instead of yanking them back down.
        /// </summary>
        private void ScrollToBottomSoon()
        {
            // Capture the intent NOW. Adding a message grows Extent, which fires
            // ScrollChanged and makes IsNearBottom() report "false" before the
            // deferred callback ever runs -- so reading the flag inside the callback
            // would always cancel the scroll and the newest message would stay
            // clipped. Sample it before the layout pass instead.
            var shouldScroll = _stickToBottom;

            Dispatcher.UIThread.Post(() =>
            {
                if (!shouldScroll)
                    return;

                ScrollMessagesToEnd();

                // Stay pinned, so the next message keeps following down.
                _stickToBottom = true;
            }, DispatcherPriority.Loaded);
        }

        /// <summary>True when the viewport is within a few pixels of the end of the list.</summary>
        private bool IsNearBottom()
        {
            var sv = MessagesScrollViewer;
            if (double.IsInfinity(sv.Extent.Height) || sv.Viewport.Height <= 0)
                return true;

            var distanceFromBottom =
                sv.Extent.Height - sv.Viewport.Height - sv.Offset.Y;
            return distanceFromBottom <= ScrollStickThreshold;
        }

        public void OpenDrawer()
        {
            HideSpeechBubble();
            ChatDrawerBorder.IsVisible = true;
            UpdateProviderBadge();
            ScrollToBottomSoon();
            TxtInput.Focus();
        }

        public void CloseDrawer()
        {
            ChatDrawerBorder.IsVisible = false;
        }

        private void ToggleDrawer()
        {
            if (ChatDrawerBorder.IsVisible)
            {
                CloseDrawer();
            }
            else
            {
                OpenDrawer();
            }
        }

        private void ClearChat()
        {
            AIAssistantService.ClearSessionHistory();
            MessagesContainer.Children.Clear();
            AddInitialWelcomeMessage();
        }

        private async Task SubmitPromptAsync(string prompt)
        {
            if (_isProcessing) return;
            TxtInput.Text = prompt;
            await SendUserMessageAsync();
        }

        /// <summary>
        /// Scrolls the transcript to the newest message.
        ///
        /// Calling ScrollToEnd() synchronously right after adding a bubble does nothing
        /// useful: Avalonia has not run a layout pass yet, so the ScrollViewer still
        /// reports the old extent and the new message ends up clipped at the bottom.
        /// Scroll once immediately, then again once layout has been flushed.
        /// </summary>
        private void ScrollMessagesToEnd()
        {
            MessagesScrollViewer.ScrollToEnd();

            Dispatcher.UIThread.Post(() => MessagesScrollViewer.ScrollToEnd(), DispatcherPriority.Render);
            Dispatcher.UIThread.Post(() => MessagesScrollViewer.ScrollToEnd(), DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(60);
                MessagesScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
        private async Task SendUserMessageAsync()
        {
            var prompt = TxtInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(prompt) || _isProcessing) return;

            TxtInput.Text = "";
            _isProcessing = true;
            BtnSend.IsEnabled = false;

            // Render user bubble
            AddUserBubble(prompt);

            // Add typing indicator
            var typingBubble = CreateTypingBubble();
            MessagesContainer.Children.Add(typingBubble);
            ScrollToBottomSoon();

            try
            {
                var response = await AIAssistantService.ProcessUserMessageAsync(prompt);
                MessagesContainer.Children.Remove(typingBubble);
                AddAssistantBubble(response.Content, response.ProposedAction);
            }
            catch (Exception ex)
            {
                MessagesContainer.Children.Remove(typingBubble);
                AddAssistantBubble($"⚠️ An error occurred while processing your request: {ex.Message}", null);
            }
            finally
            {
                _isProcessing = false;
                BtnSend.IsEnabled = true;
                ScrollToBottomSoon();
            }
        }

        private void AddUserBubble(string text)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1E293B")),
                BorderBrush = new SolidColorBrush(Color.Parse("#334155")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14, 14, 2, 14),
                Padding = new Thickness(12, 9),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 310,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.Parse("#F8FAFC")),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 17
                }
            };

            MessagesContainer.Children.Add(border);
            ScrollToBottomSoon();
        }

        private Border CreateTypingBubble()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.Parse("#101726")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1E293B")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14, 14, 14, 2),
                Padding = new Thickness(12, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.Parse("#F59E0B")), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = "Thinking...", Foreground = new SolidColorBrush(Color.Parse("#94A3B8")), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
        }

        private void AddAssistantBubble(string text, AIProposedAction? action)
        {
            var container = new StackPanel { Spacing = 6, MaxWidth = 330, HorizontalAlignment = HorizontalAlignment.Left };

            var textBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#101726")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1E293B")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14, 14, 14, 2),
                Padding = new Thickness(12, 10),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(Color.Parse("#E2E8F0")),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 17
                }
            };
            container.Children.Add(textBorder);

            // If an action is proposed, render interactive Action Proposal Card
            if (action != null && !action.IsExecuted)
            {
                var actionCard = CreateActionCard(action);
                container.Children.Add(actionCard);
            }

            MessagesContainer.Children.Add(container);
            ScrollToBottomSoon();
        }

        private Border CreateActionCard(AIProposedAction action)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#080D1A")),
                BorderBrush = new SolidColorBrush(Color.Parse("#F59E0B")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Color.Parse("#F59E0B20") })
            };

            var stack = new StackPanel { Spacing = 8 };

            // Header with badge
            var headerGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*, Auto")
            };
            var title = new TextBlock
            {
                Text = "⚡ " + action.Title,
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#F59E0B")),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1E293B")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1),
                Child = new TextBlock
                {
                    Text = "REQUIRES CONFIRMATION",
                    FontSize = 8,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#F59E0B"))
                }
            };
            Grid.SetColumn(badge, 1);
            headerGrid.Children.Add(title);
            headerGrid.Children.Add(badge);
            stack.Children.Add(headerGrid);

            // Description
            var desc = new TextBlock
            {
                Text = action.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(desc);

            // Action Buttons
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 4)
            };

            var statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#38BDF8")),
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false
            };

            var btnApprove = new Button
            {
                Content = "Approve & Execute",
                Background = new SolidColorBrush(Color.Parse("#10B981")),
                Foreground = new SolidColorBrush(Color.Parse("#0A0E18")),
                FontWeight = FontWeight.Bold,
                FontSize = 11,
                Padding = new Thickness(12, 6),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Background = new SolidColorBrush(Color.Parse("#1E293B")),
                Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                FontSize = 11,
                Padding = new Thickness(10, 6),
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            btnApprove.Click += async (s, e) =>
            {
                btnApprove.IsEnabled = false;
                btnCancel.IsEnabled = false;
                btnApprove.Content = "Executing...";

                var (success, resultMsg) = await AIAssistantService.ExecuteActionAsync(action.ActionId, true);
                btnApprove.IsVisible = false;
                btnCancel.IsVisible = false;

                statusText.Text = (success ? "✅ " : "❌ ") + resultMsg;
                statusText.Foreground = new SolidColorBrush(success ? Color.Parse("#10B981") : Color.Parse("#F43F5E"));
                statusText.IsVisible = true;
            };

            btnCancel.Click += async (s, e) =>
            {
                btnApprove.IsEnabled = false;
                btnCancel.IsEnabled = false;

                await AIAssistantService.ExecuteActionAsync(action.ActionId, false);
                btnApprove.IsVisible = false;
                btnCancel.IsVisible = false;

                statusText.Text = "Cancelled by user.";
                statusText.Foreground = new SolidColorBrush(Color.Parse("#94A3B8"));
                statusText.IsVisible = true;
            };

            btnRow.Children.Add(statusText);
            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnApprove);
            stack.Children.Add(btnRow);

            card.Child = stack;
            return card;
        }
    }
}
