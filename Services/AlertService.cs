using WarframeAlertBot.Models;

namespace WarframeAlertBot.Services
{
    public class AlertService
    {
        private const string AlertsFileName = "alerts.json";
        private const string CompletedFileName = "completed.json";
        private readonly JsonStorageService _storage;
        private readonly ILogger<AlertService> _logger;

        public AlertService(JsonStorageService storage, ILogger<AlertService> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task SaveAlertsAsync(List<Alert> alerts)
        {
            await _storage.SaveAsync(AlertsFileName, alerts);
        }

        public async Task<List<Alert>> GetActiveAlertsAsync()
        {
            var alerts = await _storage.LoadListAsync<Alert>(AlertsFileName);
            return alerts.Where(a => a.Active && a.EndTime > DateTime.UtcNow).ToList();
        }

        public async Task MarkAlertAsCompletedAsync(string alertId, long userId)
        {
            var completed = new CompletedAlert
            {
                AlertId = alertId,
                UserId = userId,
                CompletedAt = DateTime.UtcNow
            };

            await _storage.AppendToListAsync(CompletedFileName, completed);
        }

        public async Task<bool> IsAlertCompletedAsync(string alertId, long userId)
        {
            var completed = await _storage.LoadListAsync<CompletedAlert>(CompletedFileName);
            return completed.Any(c => c.AlertId == alertId && c.UserId == userId);
        }

        public async Task<List<Alert>> GetCompletedAlertsAsync(long userId)
        {
            var completed = await _storage.LoadListAsync<CompletedAlert>(CompletedFileName);
            var alerts = await _storage.LoadListAsync<Alert>(AlertsFileName);
            
            var completedIds = completed
                .Where(c => c.UserId == userId)
                .Select(c => c.AlertId)
                .ToHashSet();

            return alerts.Where(a => completedIds.Contains(a.Id)).ToList();
        }
    }
} 