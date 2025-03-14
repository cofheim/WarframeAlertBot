using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using WarframeAlertBot.Models;
using Microsoft.Extensions.Logging;

namespace WarframeAlertBot.Services
{
    public class WarframeApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WarframeApiService> _logger;
        private const string BaseUrl = "https://api.warframestat.us/pc/";
        private readonly JsonSerializerOptions _jsonOptions;

        public WarframeApiService(HttpClient httpClient, ILogger<WarframeApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WarframeAlertBot/1.0");
            _httpClient.DefaultRequestHeaders.Add("Language", "en");
            _httpClient.BaseAddress = new Uri(BaseUrl);
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<GameState> GetGameStateAsync()
        {
            var gameState = new GameState();

            try
            {
                var tasks = new[]
                {
                    FetchInvasions(gameState),
                    FetchVoidTrader(gameState),
                    FetchArchonHunt(gameState),
                    FetchNightwave(gameState),
                    FetchWorldCycles(gameState)
                };

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении данных из API");
            }

            return gameState;
        }

        private WorldCycle ConvertWorldCycle(WarframeWorldCycle cycle)
        {
            return new WorldCycle
            {
                State = cycle.State ?? "Неизвестно",
                TimeLeft = cycle.Expiry,
                IsDay = cycle.IsDay
            };
        }

        // Вспомогательные классы для десериализации JSON
        private class WarframeWorldState
        {
            public List<WarframeEvent>? Events { get; set; }
            public List<WarframeInvasion>? Invasions { get; set; }
            public WarframeSeasonInfo? SeasonInfo { get; set; }
            public WarframeVoidTrader? VoidTrader { get; set; }
            public WarframeArchonHunt? ArchonHunt { get; set; }
            public WarframeWorldCycle? EarthCycle { get; set; }
            public WarframeWorldCycle? CetusCycle { get; set; }
            public WarframeWorldCycle? VallisCycle { get; set; }
            public WarframeWorldCycle? CambionCycle { get; set; }
        }

        private class WarframeSeasonInfo
        {
            public List<NightwaveChallengeInfo>? ActiveChallenges { get; set; }
        }

        private async Task FetchEvents(GameState state)
        {
            try
            {
                var response = await _httpClient.GetAsync("events");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Events API response: {Content}", content);

                var events = await JsonSerializer.DeserializeAsync<List<WarframeEvent>>(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                    _jsonOptions);

                if (events != null)
                {
                    state.Events = events.Select(e => new Event
                    {
                        Id = e.Id ?? Guid.NewGuid().ToString(),
                        Description = e.Description ?? "Нет описания",
                        Faction = e.Faction ?? "Неизвестно",
                        Node = e.Node ?? "Неизвестно",
                        Expiry = e.Expiry,
                        Active = e.Active,
                        Reward = e.Reward?.ToString() ?? "Нет награды"
                    }).ToList();

                    _logger.LogInformation("Parsed {Count} events", state.Events.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching events");
            }
        }

        private async Task FetchInvasions(GameState state)
        {
            try
            {
                var response = await _httpClient.GetAsync("invasions");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Raw Invasions API response: {Content}", content);

                using var document = JsonDocument.Parse(content);
                var invasions = new List<WarframeInvasion>();

                _logger.LogInformation("Starting to parse invasions array. IsArray: {IsArray}", document.RootElement.ValueKind == JsonValueKind.Array);
                _logger.LogInformation("Number of elements: {Count}", document.RootElement.GetArrayLength());

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    try
                    {
                        _logger.LogInformation("Processing invasion element: {Element}", element.ToString());

                        var invasion = new WarframeInvasion
                        {
                            Id = element.TryGetProperty("id", out var id) ? id.GetString() : Guid.NewGuid().ToString(),
                            Node = element.TryGetProperty("node", out var node) ? node.GetString() : "Неизвестно",
                            Description = element.TryGetProperty("desc", out var desc) ? desc.GetString() : null,
                            AttackingFaction = element.TryGetProperty("attackingFaction", out var attackingFaction) ? attackingFaction.GetString() : "Неизвестно",
                            DefendingFaction = element.TryGetProperty("defendingFaction", out var defendingFaction) ? defendingFaction.GetString() : "Неизвестно",
                            Completion = element.TryGetProperty("completion", out var completion) ? completion.GetSingle() : 0,
                            Completed = element.TryGetProperty("completed", out var completed) ? completed.GetBoolean() : false
                        };

                        if (element.TryGetProperty("attackerReward", out var attackerReward))
                        {
                            _logger.LogInformation("Raw attacker reward: {Reward}", attackerReward.ToString());
                            invasion.AttackerReward = ParseReward(attackerReward);
                        }

                        if (element.TryGetProperty("defenderReward", out var defenderReward))
                        {
                            _logger.LogInformation("Raw defender reward: {Reward}", defenderReward.ToString());
                            invasion.DefenderReward = ParseReward(defenderReward);
                        }

                        _logger.LogInformation(
                            "Parsed invasion:\n" +
                            "Node: {Node}\n" +
                            "AttackingFaction: {AttackingFaction}\n" +
                            "DefendingFaction: {DefendingFaction}\n" +
                            "Completed: {Completed}\n" +
                            "Completion: {Completion}",
                            invasion.Node,
                            invasion.AttackingFaction,
                            invasion.DefendingFaction,
                            invasion.Completed,
                            invasion.Completion);

                        invasions.Add(invasion);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing invasion element");
                    }
                }

                var activeInvasions = invasions.Where(i => !i.Completed).ToList();
                _logger.LogInformation("Found {Count} active invasions", activeInvasions.Count);

                state.Invasions = activeInvasions
                    .Select(i =>
                    {
                        var attackerReward = FormatReward(i.AttackerReward);
                        var defenderReward = FormatReward(i.DefenderReward);

                        _logger.LogInformation(
                            "Formatted invasion rewards for {Node}:\n" +
                            "AttackerReward: {AttackerReward}\n" +
                            "DefenderReward: {DefenderReward}",
                            i.Node,
                            attackerReward,
                            defenderReward);

                        return new Invasion
                        {
                            Id = i.Id ?? Guid.NewGuid().ToString(),
                            Node = i.Node ?? "Неизвестно",
                            Description = i.Description ?? "Нет описания",
                            AttackingFaction = i.AttackingFaction ?? "Неизвестно",
                            DefendingFaction = i.DefendingFaction ?? "Неизвестно",
                            AttackerReward = attackerReward,
                            DefenderReward = defenderReward,
                            Completion = Math.Abs(i.Completion),
                            Completed = i.Completed
                        };
                    }).ToList();

                _logger.LogInformation("Final number of active invasions: {Count}", state.Invasions.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching invasions");
            }
        }

        private RewardInfo? ParseReward(JsonElement element)
        {
            try
            {
                _logger.LogInformation("Parsing reward element: {Element}", element.ToString());

                var reward = new RewardInfo();

                if (element.TryGetProperty("countedItems", out var countedItems) && countedItems.ValueKind == JsonValueKind.Array)
                {
                    reward.CountedItems = new List<CountedItem>();
                    foreach (var item in countedItems.EnumerateArray())
                    {
                        try
                        {
                            var countedItem = new CountedItem
                            {
                                Count = item.TryGetProperty("count", out var count) ? count.GetInt32() : 1,
                                Type = item.TryGetProperty("type", out var type) ? type.GetString() ?? "" : ""
                            };
                            _logger.LogInformation("Parsed counted item: {Count}x {Type}", countedItem.Count, countedItem.Type);
                            reward.CountedItems.Add(countedItem);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error parsing counted item: {Item}", item.ToString());
                        }
                    }
                }

                if (element.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    reward.Items = new List<string>();
                    foreach (var item in items.EnumerateArray())
                    {
                        try
                        {
                            var itemStr = item.GetString() ?? "";
                            _logger.LogInformation("Parsed item: {Item}", itemStr);
                            if (!string.IsNullOrWhiteSpace(itemStr))
                            {
                                reward.Items.Add(itemStr);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error parsing item: {Item}", item.ToString());
                        }
                    }
                }

                if (element.TryGetProperty("credits", out var credits))
                {
                    reward.Credits = credits.GetInt32();
                    _logger.LogInformation("Parsed credits: {Credits}", reward.Credits);
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    reward.AsString = element.GetString();
                    _logger.LogInformation("Parsed reward as string: {AsString}", reward.AsString);
                }

                return reward;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing reward element: {Element}", element.ToString());
                return null;
            }
        }

        private string FormatReward(RewardInfo? reward)
        {
            if (reward == null)
            {
                _logger.LogInformation("Reward object is null");
                return "Нет награды";
            }

            if (!string.IsNullOrWhiteSpace(reward.AsString))
            {
                _logger.LogInformation("Using reward AsString value: {AsString}", reward.AsString);
                return reward.AsString;
            }

            _logger.LogInformation(
                "Raw reward data:\n" +
                "CountedItems: {@CountedItems}\n" +
                "Items: {@Items}\n" +
                "Credits: {Credits}",
                reward.CountedItems,
                reward.Items,
                reward.Credits);

            var rewards = new List<string>();

            if (reward.CountedItems?.Any() == true)
            {
                foreach (var item in reward.CountedItems.Where(i => !string.IsNullOrWhiteSpace(i.Type)))
                {
                    _logger.LogInformation("Processing CountedItem: Count={Count}, Type={Type}", item.Count, item.Type);
                    rewards.Add($"{item.Count}x {item.Type.Trim()}");
                }
            }

            if (reward.Items?.Any() == true)
            {
                foreach (var item in reward.Items.Where(i => !string.IsNullOrWhiteSpace(i)))
                {
                    _logger.LogInformation("Processing Item: {Item}", item);
                    rewards.Add(item.Trim());
                }
            }

            if (reward.Credits > 0)
            {
                _logger.LogInformation("Processing Credits: {Credits}", reward.Credits);
                rewards.Add($"{reward.Credits:N0} кредитов");
            }

            if (!rewards.Any())
            {
                _logger.LogInformation("No rewards found in the reward object");
                return "Нет награды";
            }

            var result = string.Join(", ", rewards);
            _logger.LogInformation("Final formatted reward: {Result}", result);
            return result;
        }

        private async Task FetchNightwave(GameState state)
        {
            try
            {
                var response = await _httpClient.GetAsync("nightwave");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Nightwave API response: {Content}", content);

                var nightwave = await JsonSerializer.DeserializeAsync<WarframeNightwave>(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                    _jsonOptions);

                if (nightwave?.ActiveChallenges != null)
                {
                    state.NightwaveChallenges = nightwave.ActiveChallenges.Select(c => new NightwaveChallenge
                    {
                        Id = c.Id ?? Guid.NewGuid().ToString(),
                        Description = c.Description ?? "Нет описания",
                        Title = c.Title ?? "Без названия",
                        Standing = c.Standing,
                        IsDaily = c.IsDaily,
                        IsElite = c.IsElite,
                        Expiry = c.Expiry
                    }).ToList();

                    _logger.LogInformation("Parsed {Count} nightwave challenges", state.NightwaveChallenges.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching nightwave");
            }
        }

        private async Task FetchVoidTrader(GameState state)
        {
            try
            {
                var response = await _httpClient.GetAsync("voidTrader");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Void Trader API response: {Content}", content);

                var trader = await JsonSerializer.DeserializeAsync<WarframeVoidTrader>(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                    _jsonOptions);

                if (trader != null)
                {
                    state.VoidTrader = new VoidTrader
                    {
                        Location = trader.Location ?? "Неизвестно",
                        Arrival = trader.Activation,
                        Departure = trader.Expiry,
                        Active = trader.Active,
                        Inventory = trader.Inventory?.Select(i => new VoidTraderItem
                        {
                            Name = i.ItemName ?? "Неизвестно",
                            DucatPrice = i.DucatPrice,
                            CreditPrice = i.CreditPrice
                        }).ToList() ?? new List<VoidTraderItem>()
                    };

                    _logger.LogInformation("Parsed void trader data: Active={Active}, Location={Location}", 
                        state.VoidTrader.Active, state.VoidTrader.Location);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching void trader");
            }
        }

        private async Task FetchArchonHunt(GameState state)
        {
            try
            {
                var response = await _httpClient.GetAsync("archonHunt");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Archon Hunt API response: {Content}", content);

                var hunt = await JsonSerializer.DeserializeAsync<WarframeArchonHunt>(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                    _jsonOptions);

                if (hunt != null)
                {
                    state.ArchonHunt = new ArchonHunt
                    {
                        Boss = hunt.Boss ?? "Неизвестно",
                        Missions = hunt.Missions?.Select(m => m.Type ?? "Неизвестно").ToList() ?? new List<string>(),
                        Expiry = hunt.Expiry
                    };

                    _logger.LogInformation("Parsed archon hunt data: Boss={Boss}, MissionCount={Count}", 
                        state.ArchonHunt.Boss, state.ArchonHunt.Missions.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching archon hunt");
            }
        }

        private async Task FetchWorldCycles(GameState state)
        {
            try
            {
                var cycles = new[] { "earthCycle", "cetusCycle", "vallisCycle", "cambionCycle" };
                foreach (var cycle in cycles)
                {
                    var response = await _httpClient.GetAsync(cycle);
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("{Cycle} API response: {Content}", cycle, content);

                    var worldCycle = await JsonSerializer.DeserializeAsync<WarframeWorldCycle>(
                        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                        _jsonOptions);

                    if (worldCycle != null)
                    {
                        state.WorldCycles[cycle] = new WorldCycle
                        {
                            State = worldCycle.State ?? "Неизвестно",
                            TimeLeft = worldCycle.Expiry,
                            IsDay = worldCycle.IsDay
                        };

                        _logger.LogInformation("Parsed {Cycle} data: State={State}, IsDay={IsDay}", 
                            cycle, state.WorldCycles[cycle].State, state.WorldCycles[cycle].IsDay);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching world cycles");
            }
        }

        public async Task<List<Alert>> GetActiveAlertsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("alerts");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var warframeAlerts = await JsonSerializer.DeserializeAsync<List<WarframeAlert>>(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                    _jsonOptions);

                if (warframeAlerts == null)
                    return new List<Alert>();

                return warframeAlerts.Select(wa => new Alert
                {
                    Id = wa.Id ?? Guid.NewGuid().ToString(),
                    Mission = wa.Mission?.Type ?? "Неизвестно",
                    MissionType = wa.Mission?.Type ?? "Неизвестно",
                    Reward = string.Join(", ", wa.Mission?.Reward?.Items ?? new List<string>()),
                    Location = wa.Mission?.Node ?? "Неизвестно",
                    Enemy = wa.Mission?.Faction ?? "Неизвестно",
                    MinEnemyLevel = wa.Mission?.MinEnemyLevel ?? 1,
                    MaxEnemyLevel = wa.Mission?.MaxEnemyLevel ?? 100,
                    StartTime = wa.Activation,
                    EndTime = wa.Expiry,
                    Active = true
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching alerts from Warframe API");
                return new List<Alert>();
            }
        }

        // Вспомогательные классы для десериализации JSON
        private class WarframeAlert
        {
            public string? Id { get; set; }
            public DateTime Activation { get; set; }
            public DateTime Expiry { get; set; }
            public MissionInfo? Mission { get; set; }
        }

        private class MissionInfo
        {
            public string? Node { get; set; }
            public string? Type { get; set; }
            public string? Faction { get; set; }
            public int MinEnemyLevel { get; set; }
            public int MaxEnemyLevel { get; set; }
            public RewardInfo? Reward { get; set; }
        }

        private class WarframeEvent
        {
            public string? Id { get; set; }
            public string? Description { get; set; }
            public string? Faction { get; set; }
            public string? Node { get; set; }
            public DateTime Expiry { get; set; }
            public bool Active { get; set; }
            public RewardInfo? Reward { get; set; }
        }

        private class WarframeInvasion
        {
            public string? Id { get; set; }
            public string? Node { get; set; }
            public string? Description { get; set; }
            public string? AttackingFaction { get; set; }
            public string? DefendingFaction { get; set; }
            public RewardInfo? AttackerReward { get; set; }
            public RewardInfo? DefenderReward { get; set; }
            public float Completion { get; set; }
            public bool Completed { get; set; }
            public string? AttackerRewardString { get; set; }
            public string? DefenderRewardString { get; set; }
        }

        private class RewardInfo
        {
            public List<CountedItem>? CountedItems { get; set; }
            public List<string>? Items { get; set; }
            public int Credits { get; set; }
            public string? AsString { get; set; }

            public override string ToString()
            {
                if (!string.IsNullOrWhiteSpace(AsString))
                {
                    return AsString;
                }

                var rewards = new List<string>();

                if (CountedItems?.Any() == true)
                {
                    rewards.AddRange(CountedItems
                        .Where(i => !string.IsNullOrWhiteSpace(i.Type))
                        .Select(i => $"{i.Count}x {i.Type.Trim()}"));
                }

                if (Items?.Any() == true)
                {
                    rewards.AddRange(Items
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item.Trim()));
                }

                if (Credits > 0)
                {
                    rewards.Add($"{Credits:N0} кредитов");
                }

                if (!rewards.Any())
                {
                    return "Нет награды";
                }

                return string.Join(", ", rewards);
            }
        }

        private class CountedItem
        {
            public string Type { get; set; } = "";
            public int Count { get; set; }

            public override string ToString()
            {
                return $"{Count}x {Type}";
            }
        }

        private class WarframeNightwave
        {
            public List<NightwaveChallengeInfo>? ActiveChallenges { get; set; }
        }

        private class NightwaveChallengeInfo
        {
            public string? Id { get; set; }
            public string? Description { get; set; }
            public string? Title { get; set; }
            public int Standing { get; set; }
            public bool IsDaily { get; set; }
            public bool IsElite { get; set; }
            public DateTime Expiry { get; set; }
        }

        private class WarframeVoidTrader
        {
            public string? Location { get; set; }
            public DateTime Activation { get; set; }
            public DateTime Expiry { get; set; }
            public bool Active { get; set; }
            public List<VoidTraderItemInfo>? Inventory { get; set; }
        }

        private class VoidTraderItemInfo
        {
            public string? ItemName { get; set; }
            public int DucatPrice { get; set; }
            public int CreditPrice { get; set; }
        }

        private class WarframeArchonHunt
        {
            public string? Boss { get; set; }
            public List<MissionInfo>? Missions { get; set; }
            public DateTime Expiry { get; set; }
        }

        private class WarframeWorldCycle
        {
            public string? State { get; set; }
            public DateTime Expiry { get; set; }
            public bool IsDay { get; set; }
        }
    }
} 