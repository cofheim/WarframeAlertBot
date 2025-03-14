using WarframeAlertBot.Models;

namespace WarframeAlertBot.Services
{
    public class UserSettingsService
    {
        private const string FileName = "users.json";
        private readonly JsonStorageService _storage;
        private readonly ILogger<UserSettingsService> _logger;

        public UserSettingsService(JsonStorageService storage, ILogger<UserSettingsService> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<UserSettings?> GetUserSettingsAsync(long userId)
        {
            var users = await _storage.LoadListAsync<UserSettings>(FileName);
            return users.FirstOrDefault(u => u.TelegramUserId == userId);
        }

        public async Task SaveUserSettingsAsync(UserSettings settings)
        {
            var users = await _storage.LoadListAsync<UserSettings>(FileName);
            var existingUser = users.FirstOrDefault(u => u.TelegramUserId == settings.TelegramUserId);
            
            if (existingUser != null)
            {
                users.Remove(existingUser);
            }
            
            users.Add(settings);
            await _storage.SaveAsync(FileName, users);
        }

        public async Task<List<UserSettings>> GetAllUsersAsync()
        {
            return await _storage.LoadListAsync<UserSettings>(FileName);
        }

        public async Task AddRewardFilterAsync(long userId, string filter)
        {
            var settings = await GetUserSettingsAsync(userId);
            if (settings == null)
            {
                _logger.LogWarning("User {UserId} not found when adding filter", userId);
                return;
            }

            if (!settings.RewardFilters.Contains(filter, StringComparer.OrdinalIgnoreCase))
            {
                settings.RewardFilters.Add(filter);
                await SaveUserSettingsAsync(settings);
            }
        }

        public async Task RemoveRewardFilterAsync(long userId, string filter)
        {
            var settings = await GetUserSettingsAsync(userId);
            if (settings == null)
            {
                _logger.LogWarning("User {UserId} not found when removing filter", userId);
                return;
            }

            settings.RewardFilters.RemoveAll(f => f.Equals(filter, StringComparison.OrdinalIgnoreCase));
            await SaveUserSettingsAsync(settings);
        }
    }
} 