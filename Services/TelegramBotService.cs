using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Polling;
using WarframeAlertBot.Models;

namespace WarframeAlertBot.Services
{
    public class TelegramBotService : IHostedService
    {
        private readonly TelegramBotClient _botClient;
        private readonly ILogger<TelegramBotService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ReplyKeyboardMarkup _mainMenu;

        public TelegramBotService(
            TelegramBotClient botClient,
            ILogger<TelegramBotService> logger,
            IServiceProvider serviceProvider)
        {
            _botClient = botClient;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _mainMenu = CreateMainMenu();
        }

        private ReplyKeyboardMarkup CreateMainMenu()
        {
            return new ReplyKeyboardMarkup(new[]
            {
                new[]
                {
                    new KeyboardButton("🚨 Алерты"),
                    new KeyboardButton("⚔️ Вторжения")
                },
                new[]
                {
                    new KeyboardButton("🌟 Найтвейв"),
                    new KeyboardButton("👑 Баро")
                },
                new[]
                {
                    new KeyboardButton("🎭 Архонт"),
                    new KeyboardButton("🌍 Циклы")
                },
                new[]
                {
                    new KeyboardButton("⚙️ Настройки"),
                    new KeyboardButton("❓ Помощь")
                }
            })
            {
                ResizeKeyboard = true
            };
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try 
            {
                _logger.LogInformation("Начало запуска бота...");
                
                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = new[] { UpdateType.Message }
                };
                
                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandlePollingErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: cancellationToken
                );

                var me = await _botClient.GetMeAsync(cancellationToken);
                _logger.LogInformation("Бот успешно запущен: {BotName} (ID: {BotId})", me.Username, me.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при запуске бота");
                throw;
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation(
                    "Получено обновление {UpdateId} типа {UpdateType}", 
                    update.Id, 
                    update.Type);

                if (update.Type != UpdateType.Message)
                {
                    _logger.LogInformation("Пропущено обновление {UpdateId} типа {UpdateType}", update.Id, update.Type);
                    return;
                }

                if (update.Message?.Text == null)
                {
                    _logger.LogInformation("Пропущено сообщение без текста, UpdateId: {UpdateId}", update.Id);
                    return;
                }

                var message = update.Message;
                var userId = message.From?.Id ?? 0;
                
                if (userId == 0)
                {
                    _logger.LogWarning("Получено сообщение без ID пользователя, UpdateId: {UpdateId}", update.Id);
                    return;
                }

                _logger.LogInformation(
                    "Обработка сообщения: '{MessageText}' от пользователя {UserId} в чате {ChatId}, UpdateId: {UpdateId}",
                    message.Text,
                    userId,
                    message.Chat.Id,
                    update.Id);

                using var scope = _serviceProvider.CreateScope();
                var userService = scope.ServiceProvider.GetRequiredService<UserSettingsService>();
                var warframeService = scope.ServiceProvider.GetRequiredService<WarframeApiService>();

                var command = message.Text.Split(' ')[0].ToLower();
                if (command.StartsWith("/"))
                {
                    _logger.LogInformation(
                        "Обработка команды '{Command}' от пользователя {UserId}",
                        command,
                        userId);
                        
                    await HandleCommand(message, command, userService, warframeService, cancellationToken);
                }
                else
                {
                    _logger.LogInformation(
                        "Обработка кнопки '{Button}' от пользователя {UserId}",
                        message.Text,
                        userId);
                        
                    await HandleButton(message, userService, warframeService, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке обновления {UpdateId}", update.Id);
                
                if (update.Message?.Chat != null)
                {
                    try 
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: update.Message.Chat.Id,
                            text: "Произошла ошибка при обработке команды. Пожалуйста, попробуйте позже.",
                            cancellationToken: cancellationToken);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Ошибка при отправке сообщения об ошибке в чат {ChatId}", update.Message.Chat.Id);
                    }
                }
            }
        }

        private async Task HandleCommand(Message message, string command, UserSettingsService userService, WarframeApiService warframeService, CancellationToken cancellationToken)
        {
            switch (command)
            {
                case "/start":
                    await HandleStartCommand(message, userService, cancellationToken);
                    break;

                case "/stop":
                    await HandleStopCommand(message, userService, cancellationToken);
                    break;

                case "/addfilter":
                    await HandleAddFilterCommand(message, userService, cancellationToken);
                    break;

                case "/removefilter":
                    await HandleRemoveFilterCommand(message, userService, cancellationToken);
                    break;

                case "/filters":
                    await HandleFiltersCommand(message, userService, cancellationToken);
                    break;

                case "/help":
                    await HandleHelpCommand(message, cancellationToken);
                    break;
            }
        }

        private async Task HandleButton(Message message, UserSettingsService userService, WarframeApiService warframeService, CancellationToken cancellationToken)
        {
            if (message.Chat == null) return;

            var gameState = await warframeService.GetGameStateAsync();
            var response = message.Text switch
            {
                "🚨 Алерты" => FormatAlerts(gameState),
                "⚔️ Вторжения" => FormatInvasions(gameState),
                "🌟 Найтвейв" => FormatNightwave(gameState),
                "👑 Баро" => FormatVoidTrader(gameState),
                "🎭 Архонт" => FormatArchonHunt(gameState),
                "🌍 Циклы" => FormatWorldCycles(gameState),
                "⚙️ Настройки" => await FormatSettings(message.From?.Id ?? 0, userService),
                "❓ Помощь" => GetHelpText(),
                _ => null
            };

            if (response != null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: response,
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleStartCommand(Message message, UserSettingsService userService, CancellationToken cancellationToken)
        {
            if (message.From == null || message.Chat == null) return;

            var settings = new UserSettings
            {
                TelegramUserId = message.From.Id,
                ChatId = message.Chat.Id.ToString(),
                NotificationsEnabled = true
            };

            await userService.SaveUserSettingsAsync(settings);

            var response = "Привет! Я буду отправлять вам уведомления о событиях в Warframe.\n" +
                         "Используйте кнопки меню для получения информации.";

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: response,
                replyMarkup: _mainMenu,
                cancellationToken: cancellationToken);
        }

        private string FormatAlerts(GameState state)
        {
            var alerts = state.Events.Where(e => e.Active).ToList();
            if (!alerts.Any())
                return "Сейчас нет активных алертов.";

            return "🚨 Активные алерты:\n\n" + string.Join("\n\n", alerts.Select(alert =>
                $"📍 {alert.Node}\n" +
                $"👾 {alert.Faction}\n" +
                $"💎 Награда: {alert.Reward}\n" +
                $"⏰ До: {alert.Expiry:HH:mm}"));
        }

        private string FormatInvasions(GameState state)
        {
            if (!state.Invasions.Any())
            {
                _logger.LogInformation("Нет активных вторжений");
                return "Сейчас нет активных вторжений.";
            }

            _logger.LogInformation("Форматирование {Count} вторжений", state.Invasions.Count());

            return "⚔️ Активные вторжения:\n\n" + string.Join("\n\n", state.Invasions.Select(invasion =>
            {
                _logger.LogInformation(
                    "Обработка вторжения:\n" +
                    "Node={Node}\n" +
                    "AttackingFaction={AttackingFaction}\n" +
                    "DefendingFaction={DefendingFaction}\n" +
                    "AttackerReward={AttackerReward}\n" +
                    "DefenderReward={DefenderReward}\n" +
                    "Completion={Completion}",
                    invasion.Node,
                    invasion.AttackingFaction,
                    invasion.DefendingFaction,
                    invasion.AttackerReward,
                    invasion.DefenderReward,
                    invasion.Completion);

                var attackerReward = string.IsNullOrWhiteSpace(invasion.AttackerReward) ? "Нет награды" : invasion.AttackerReward.Trim();
                var defenderReward = string.IsNullOrWhiteSpace(invasion.DefenderReward) ? "Нет награды" : invasion.DefenderReward.Trim();
                var attackingFaction = string.IsNullOrWhiteSpace(invasion.AttackingFaction) ? "Неизвестно" : invasion.AttackingFaction.Trim();
                var defendingFaction = string.IsNullOrWhiteSpace(invasion.DefendingFaction) ? "Неизвестно" : invasion.DefendingFaction.Trim();

                _logger.LogInformation(
                    "Отформатированные данные вторжения:\n" +
                    "AttackingFaction={AttackingFaction}\n" +
                    "AttackerReward={AttackerReward}\n" +
                    "DefendingFaction={DefendingFaction}\n" +
                    "DefenderReward={DefenderReward}",
                    attackingFaction,
                    attackerReward,
                    defendingFaction,
                    defenderReward);

                return $"📍 {invasion.Node}\n" +
                       $"🔵 {attackingFaction} ({attackerReward})\n" +
                       $"🔴 {defendingFaction} ({defenderReward})\n" +
                       $"📊 Прогресс: {Math.Abs(invasion.Completion):F1}%";
            }));
        }

        private string FormatNightwave(GameState state)
        {
            if (!state.NightwaveChallenges.Any())
                return "Сейчас нет активных заданий Найтвейв.";

            var daily = state.NightwaveChallenges.Where(c => c.IsDaily);
            var weekly = state.NightwaveChallenges.Where(c => !c.IsDaily && !c.IsElite);
            var elite = state.NightwaveChallenges.Where(c => c.IsElite);

            return "🌟 Задания Найтвейв:\n\n" +
                   FormatChallenges("Ежедневные", daily) +
                   FormatChallenges("Еженедельные", weekly) +
                   FormatChallenges("Элитные еженедельные", elite);
        }

        private string FormatChallenges(string title, IEnumerable<NightwaveChallenge> challenges)
        {
            var formatted = challenges.Select(c =>
                $"• {c.Title} ({c.Standing:N0})\n  {c.Description}\n  ⏰ До: {c.Expiry:g}");
            return $"== {title} ==\n" + string.Join("\n\n", formatted) + "\n\n";
        }

        private string FormatVoidTrader(GameState state)
        {
            if (state.VoidTrader == null)
                return "Информация о Баро Ки'Тиир недоступна.";

            var trader = state.VoidTrader;
            var response = $"👑 Баро Ки'Тиир\n\n" +
                          $"📍 Локация: {trader.Location}\n";

            if (trader.Active && trader.Inventory.Any())
            {
                response += "\nТовары:\n" + string.Join("\n", trader.Inventory.Select(item =>
                    $"• {item.Name} ({item.DucatPrice} дукат, {item.CreditPrice:N0} кр)"));
            }
            else
            {
                var arrival = trader.Arrival?.ToLocalTime();
                response += arrival.HasValue
                    ? $"\n⏰ Прибытие: {arrival:g}"
                    : "\nВремя прибытия неизвестно";
            }

            return response;
        }

        private string FormatArchonHunt(GameState state)
        {
            if (state.ArchonHunt == null)
                return "Информация об Охоте на Архонта недоступна.";

            var hunt = state.ArchonHunt;
            return $"🎭 Охота на Архонта\n\n" +
                   $"👑 Босс: {hunt.Boss}\n\n" +
                   "Миссии:\n" + string.Join("\n", hunt.Missions.Select((m, i) => $"{i + 1}. {m}")) +
                   $"\n\n⏰ До: {hunt.Expiry:g}";
        }

        private string FormatWorldCycles(GameState state)
        {
            if (!state.WorldCycles.Any())
                return "Информация о циклах недоступна.";

            var cycles = new Dictionary<string, string>
            {
                ["earthCycle"] = "🌍 Земля",
                ["cetusCycle"] = "🏰 Цетус",
                ["vallisCycle"] = "❄️ Долина Сфер",
                ["cambionCycle"] = "🔥 Камбион"
            };

            return "Текущие циклы:\n\n" + string.Join("\n\n", state.WorldCycles.Select(cycle =>
                $"{cycles.GetValueOrDefault(cycle.Key, cycle.Key)}\n" +
                $"Состояние: {cycle.Value.State}\n" +
                $"⏰ Осталось: {FormatTimeSpan(cycle.Value.TimeLeft - DateTime.UtcNow)}"));
        }

        private string FormatTimeSpan(TimeSpan? span)
        {
            if (!span.HasValue || span.Value < TimeSpan.Zero)
                return "время истекло";

            return $"{(int)span.Value.TotalHours:D2}:{span.Value.Minutes:D2}:{span.Value.Seconds:D2}";
        }

        private async Task<string> FormatSettings(long userId, UserSettingsService userService)
        {
            var settings = await userService.GetUserSettingsAsync(userId);
            if (settings == null)
                return "Настройки не найдены. Используйте /start для начала работы.";

            return "⚙️ Ваши настройки:\n\n" +
                   $"Уведомления: {(settings.NotificationsEnabled ? "✅" : "❌")}\n\n" +
                   "Фильтры наград:\n" +
                   (settings.RewardFilters.Any()
                       ? string.Join("\n", settings.RewardFilters.Select(f => $"• {f}"))
                       : "Нет активных фильтров");
        }

        private string GetHelpText()
        {
            return "❓ Помощь\n\n" +
                   "Используйте кнопки меню для получения информации:\n\n" +
                   "🚨 Алерты - текущие алерты\n" +
                   "⚔️ Вторжения - активные вторжения\n" +
                   "🌟 Найтвейв - задания Найтвейв\n" +
                   "👑 Баро - информация о Баро Ки'Тиир\n" +
                   "🎭 Архонт - Охота на Архонта\n" +
                   "🌍 Циклы - циклы дня и ночи\n" +
                   "⚙️ Настройки - управление настройками\n\n" +
                   "Дополнительные команды:\n" +
                   "/addfilter [награда] - добавить фильтр награды\n" +
                   "/removefilter [награда] - удалить фильтр награды\n" +
                   "/filters - показать фильтры\n" +
                   "/stop - отключить уведомления";
        }

        private async Task HandleStopCommand(Message message, UserSettingsService userService, CancellationToken cancellationToken)
        {
            if (message.From == null || message.Chat == null) return;

            var settings = await userService.GetUserSettingsAsync(message.From.Id);
            if (settings != null)
            {
                settings.NotificationsEnabled = false;
                await userService.SaveUserSettingsAsync(settings);

                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Уведомления отключены. Используйте /start чтобы включить их снова.",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleAddFilterCommand(Message message, UserSettingsService userService, CancellationToken cancellationToken)
        {
            if (message.From == null || message.Chat == null || message.Text == null) return;

            var args = message.Text.Split(' ', 2);
            if (args.Length < 2)
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Пожалуйста, укажите фильтр. Например: /addfilter Нитаин",
                    cancellationToken: cancellationToken);
                return;
            }

            var filter = args[1].Trim();
            await userService.AddRewardFilterAsync(message.From.Id, filter);

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: $"Фильтр '{filter}' добавлен.",
                cancellationToken: cancellationToken);
        }

        private async Task HandleRemoveFilterCommand(Message message, UserSettingsService userService, CancellationToken cancellationToken)
        {
            if (message.From == null || message.Chat == null || message.Text == null) return;

            var args = message.Text.Split(' ', 2);
            if (args.Length < 2)
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Пожалуйста, укажите фильтр для удаления. Например: /removefilter Нитаин",
                    cancellationToken: cancellationToken);
                return;
            }

            var filter = args[1].Trim();
            await userService.RemoveRewardFilterAsync(message.From.Id, filter);

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: $"Фильтр '{filter}' удален.",
                cancellationToken: cancellationToken);
        }

        private async Task HandleFiltersCommand(Message message, UserSettingsService userService, CancellationToken cancellationToken)
        {
            if (message.From == null || message.Chat == null) return;

            var settings = await userService.GetUserSettingsAsync(message.From.Id);
            var response = settings?.RewardFilters.Count > 0
                ? $"Ваши фильтры:\n{string.Join("\n", settings.RewardFilters)}"
                : "У вас нет активных фильтров.";

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: response,
                cancellationToken: cancellationToken);
        }

        private async Task HandleHelpCommand(Message message, CancellationToken cancellationToken)
        {
            if (message.Chat == null) return;

            var helpText =
                "Доступные команды:\n\n" +
                "/start - Включить уведомления\n" +
                "/stop - Отключить уведомления\n" +
                "/addfilter [награда] - Добавить фильтр награды\n" +
                "/removefilter [награда] - Удалить фильтр награды\n" +
                "/filters - Показать ваши фильтры\n" +
                "/help - Показать это сообщение";

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: helpText,
                cancellationToken: cancellationToken);
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            _logger.LogError(exception, "Error handling polling: {ErrorMessage}", errorMessage);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
} 