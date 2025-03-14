using System;
using System.Collections.Generic;

namespace WarframeAlertBot.Models
{
    public class Event
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;
        public string Node { get; set; } = string.Empty;
        public DateTime? Expiry { get; set; }
        public bool Active { get; set; }
        public string Reward { get; set; } = string.Empty;
    }

    public class Invasion
    {
        public string Id { get; set; } = string.Empty;
        public string Node { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AttackingFaction { get; set; } = string.Empty;
        public string DefendingFaction { get; set; } = string.Empty;
        public string AttackerReward { get; set; } = string.Empty;
        public string DefenderReward { get; set; } = string.Empty;
        public double Completion { get; set; }
        public bool Completed { get; set; }
    }

    public class NightwaveChallenge
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Standing { get; set; }
        public bool IsDaily { get; set; }
        public bool IsElite { get; set; }
        public DateTime? Expiry { get; set; }
    }

    public class VoidTrader
    {
        public string Location { get; set; } = string.Empty;
        public DateTime? Arrival { get; set; }
        public DateTime? Departure { get; set; }
        public bool Active { get; set; }
        public List<VoidTraderItem> Inventory { get; set; } = new();
    }

    public class VoidTraderItem
    {
        public string Name { get; set; } = string.Empty;
        public int DucatPrice { get; set; }
        public int CreditPrice { get; set; }
    }

    public class ArchonHunt
    {
        public string Boss { get; set; } = string.Empty;
        public List<string> Missions { get; set; } = new();
        public DateTime? Expiry { get; set; }
    }

    public class WorldCycle
    {
        public string State { get; set; } = string.Empty;
        public DateTime? TimeLeft { get; set; }
        public bool IsDay { get; set; }
    }

    public class GameState
    {
        public List<Event> Events { get; set; } = new();
        public List<Invasion> Invasions { get; set; } = new();
        public List<NightwaveChallenge> NightwaveChallenges { get; set; } = new();
        public VoidTrader? VoidTrader { get; set; }
        public ArchonHunt? ArchonHunt { get; set; }
        public Dictionary<string, WorldCycle> WorldCycles { get; set; } = new();
    }
} 