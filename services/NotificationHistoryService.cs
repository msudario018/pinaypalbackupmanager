using System;
using System.Collections.Generic;

namespace PinayPalBackupManager.Services
{
    public record NotificationEntry(DateTime Time, string Title, string Message, string Type);

    public static class NotificationHistoryService
    {
        private const int MaxEntries = 20;
        private static readonly List<NotificationEntry> _entries = new();

        public static IReadOnlyList<NotificationEntry> Entries => _entries.AsReadOnly();
        public static int UnreadCount { get; private set; }

        public static event Action? OnNewNotification;

        public static void Add(string title, string message, string type)
        {
            _entries.Insert(0, new NotificationEntry(DateTime.Now, title, message, type));
            if (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
            UnreadCount++;
            OnNewNotification?.Invoke();
        }

        public static void MarkAllRead() => UnreadCount = 0;

        public static void ClearAll()
        {
            _entries.Clear();
            UnreadCount = 0;
        }
    }
}
