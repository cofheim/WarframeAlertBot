using System.Text.Json;
using WarframeAlertBot.Models;

namespace WarframeAlertBot.Services
{
    public class JsonStorageService
    {
        private readonly string _dataPath;
        private readonly ILogger<JsonStorageService> _logger;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public JsonStorageService(IWebHostEnvironment environment, ILogger<JsonStorageService> logger)
        {
            _dataPath = Path.Combine(environment.ContentRootPath, "Data");
            _logger = logger;
            
            if (!Directory.Exists(_dataPath))
            {
                Directory.CreateDirectory(_dataPath);
            }
        }

        private string GetFilePath(string fileName) => Path.Combine(_dataPath, fileName);

        public async Task SaveAsync<T>(string fileName, T data)
        {
            try
            {
                await _semaphore.WaitAsync();
                var filePath = GetFilePath(fileName);
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving data to {FileName}", fileName);
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<T?> LoadAsync<T>(string fileName) where T : class
        {
            try
            {
                await _semaphore.WaitAsync();
                var filePath = GetFilePath(fileName);
                
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data from {FileName}", fileName);
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<List<T>> LoadListAsync<T>(string fileName)
        {
            var result = await LoadAsync<List<T>>(fileName);
            return result ?? new List<T>();
        }

        public async Task AppendToListAsync<T>(string fileName, T item)
        {
            try
            {
                await _semaphore.WaitAsync();
                var list = await LoadListAsync<T>(fileName);
                list.Add(item);
                await SaveAsync(fileName, list);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
} 