using System.Collections.Generic;

namespace WarframeAlertBot.Models
{
    public class UserSettings
    {
        public long TelegramUserId { get; set; }
        public string ChatId { get; set; } = string.Empty;
        public List<string> RewardFilters { get; set; } = new();
        public bool NotificationsEnabled { get; set; } = true;
        public string Language { get; set; } = "ru";
    }
} 