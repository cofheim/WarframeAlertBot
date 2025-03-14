using System;

namespace WarframeAlertBot.Models
{
    public class Alert
    {
        public string Id { get; set; } = string.Empty;
        public string Mission { get; set; } = string.Empty;
        public string MissionType { get; set; } = string.Empty;
        public string Reward { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Enemy { get; set; } = string.Empty;
        public int MinEnemyLevel { get; set; }
        public int MaxEnemyLevel { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool Active { get; set; }
    }
} 