using System;

namespace WarframeAlertBot.Models
{
    public class CompletedAlert
    {
        public string AlertId { get; set; } = string.Empty;
        public long UserId { get; set; }
        public DateTime CompletedAt { get; set; }
    }
} 