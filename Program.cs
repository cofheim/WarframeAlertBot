using Telegram.Bot;
using WarframeAlertBot.Services;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();

// Configure services
builder.Services.AddSingleton<JsonStorageService>();
builder.Services.AddSingleton<UserSettingsService>();
builder.Services.AddSingleton<AlertService>();

// Configure Telegram bot
var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") 
    ?? builder.Configuration["TelegramBot:Token"]
    ?? throw new InvalidOperationException("Токен Telegram бота не найден");

logger.LogInformation("Инициализация бота с токеном: {BotToken}", botToken[..6] + "..." + botToken[^6..]);

builder.Services.AddSingleton<TelegramBotClient>(provider => 
{
    var bot = new TelegramBotClient(botToken);
    logger.LogInformation("Telegram бот клиент создан");
    return bot;
});

builder.Services.AddHttpClient<WarframeApiService>();
builder.Services.AddHostedService<TelegramBotService>();
builder.Services.AddHostedService<AlertProcessingService>();

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

logger.LogInformation("Запуск приложения...");
app.Run();
