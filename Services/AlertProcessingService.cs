using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using WarframeAlertBot.Models;

namespace WarframeAlertBot.Services
{
    public class AlertProcessingService : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AlertProcessingService> _logger;
        private Timer? _timer;
        private GameState? _lastState;

        public AlertProcessingService(
            IServiceProvider serviceProvider,
            ILogger<AlertProcessingService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
            return Task.CompletedTask;
        }

        private async void DoWork(object? state)
        {
            using var scope = _serviceProvider.CreateScope();
            try
            {
                var warframeService = scope.ServiceProvider.GetRequiredService<WarframeApiService>();
                var userService = scope.ServiceProvider.GetRequiredService<UserSettingsService>();
                var botClient = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();

                var currentState = await warframeService.GetGameStateAsync();
                var users = await userService.GetAllUsersAsync();

                if (_lastState != null)
                {
                    // Проверяем новые события
                    var newEvents = currentState.Events
                        .Where(e => e.Active && !_lastState.Events.Any(le => le.Id == e.Id))
                        .ToList();

                    // Проверяем новые вторжения
                    var newInvasions = currentState.Invasions
                        .Where(i => !i.Completed && !_lastState.Invasions.Any(li => li.Id == i.Id))
                        .ToList();

                    // Проверяем новые задания Найтвейв
                    var newChallenges = currentState.NightwaveChallenges
                        .Where(c => !_lastState.NightwaveChallenges.Any(lc => lc.Id == c.Id))
                        .ToList();

                    // Проверяем изменения в статусе Баро
                    var baroChanged = IsVoidTraderChanged(currentState.VoidTrader, _lastState.VoidTrader);

                    // Проверяем новую Охоту на Архонта
                    var archonChanged = IsArchonHuntChanged(currentState.ArchonHunt, _lastState.ArchonHunt);

                    foreach (var user in users.Where(u => u.NotificationsEnabled))
                    {
                        try
                        {
                            // Отправляем уведомления о новых событиях
                            foreach (var evt in newEvents.Where(e => ShouldNotifyUser(e.Reward, user)))
                            {
                                await SendNotification(botClient, user.ChatId, FormatEventNotification(evt));
                            }

                            // Отправляем уведомления о новых вторжениях
                            foreach (var invasion in newInvasions.Where(i => 
                                ShouldNotifyUser(i.AttackerReward, user) || 
                                ShouldNotifyUser(i.DefenderReward, user)))
                            {
                                await SendNotification(botClient, user.ChatId, FormatInvasionNotification(invasion));
                            }

                            // Отправляем уведомления о новых заданиях Найтвейв
                            if (newChallenges.Any())
                            {
                                await SendNotification(botClient, user.ChatId, FormatNightwaveNotification(newChallenges));
                            }

                            // Отправляем уведомление о Баро
                            if (baroChanged)
                            {
                                await SendNotification(botClient, user.ChatId, FormatVoidTraderNotification(currentState.VoidTrader));
                            }

                            // Отправляем уведомление о новой Охоте на Архонта
                            if (archonChanged)
                            {
                                await SendNotification(botClient, user.ChatId, FormatArchonHuntNotification(currentState.ArchonHunt));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error sending notifications to user {UserId}", user.TelegramUserId);
                        }
                    }
                }

                _lastState = currentState;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing game state");
            }
        }

        private bool ShouldNotifyUser(string reward, UserSettings user)
        {
            if (user.RewardFilters.Count == 0)
                return true;

            return user.RewardFilters.Any(filter => 
                reward.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        private async Task SendNotification(ITelegramBotClient botClient, string chatId, string message)
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: message,
                cancellationToken: CancellationToken.None
            );
        }

        private string FormatEventNotification(Event evt)
        {
            return $"🚨 Новое событие!\n\n" +
                   $"📍 {evt.Node}\n" +
                   $"👾 {evt.Faction}\n" +
                   $"💎 Награда: {evt.Reward}\n" +
                   $"⏰ До: {evt.Expiry:HH:mm}";
        }

        private string FormatInvasionNotification(Invasion invasion)
        {
            return $"⚔️ Новое вторжение!\n\n" +
                   $"📍 {invasion.Node}\n" +
                   $"🔵 {invasion.AttackingFaction} ({invasion.AttackerReward})\n" +
                   $"🔴 {invasion.DefendingFaction} ({invasion.DefenderReward})\n" +
                   $"📊 Прогресс: {invasion.Completion:F1}%";
        }

        private string FormatNightwaveNotification(List<NightwaveChallenge> challenges)
        {
            return "🌟 Новые задания Найтвейв!\n\n" +
                   string.Join("\n\n", challenges.Select(c =>
                       $"• {c.Title} ({c.Standing:N0})\n" +
                       $"  {c.Description}\n" +
                       $"  ⏰ До: {c.Expiry:g}"));
        }

        private string FormatVoidTraderNotification(VoidTrader? trader)
        {
            if (trader == null)
                return string.Empty;

            var message = $"👑 Баро Ки'Тиир\n\n" +
                         $"📍 Локация: {trader.Location}\n";

            if (trader.Active && trader.Inventory.Any())
            {
                message += "\nТовары:\n" + string.Join("\n", trader.Inventory.Select(item =>
                    $"• {item.Name} ({item.DucatPrice} дукат, {item.CreditPrice:N0} кр)"));
            }
            else
            {
                var arrival = trader.Arrival?.ToLocalTime();
                message += arrival.HasValue
                    ? $"\n⏰ Прибытие: {arrival:g}"
                    : "\nВремя прибытия неизвестно";
            }

            return message;
        }

        private string FormatArchonHuntNotification(ArchonHunt? hunt)
        {
            if (hunt == null)
                return string.Empty;

            return $"🎭 Новая Охота на Архонта!\n\n" +
                   $"👑 Босс: {hunt.Boss}\n\n" +
                   "Миссии:\n" + string.Join("\n", hunt.Missions.Select((m, i) => $"{i + 1}. {m}")) +
                   $"\n\n⏰ До: {hunt.Expiry:g}";
        }

        private bool IsVoidTraderChanged(VoidTrader? current, VoidTrader? last)
        {
            if (current == null || last == null)
                return false;

            return current.Active != last.Active ||
                   current.Location != last.Location ||
                   current.Arrival != last.Arrival ||
                   current.Departure != last.Departure;
        }

        private bool IsArchonHuntChanged(ArchonHunt? current, ArchonHunt? last)
        {
            if (current == null || last == null)
                return false;

            return current.Boss != last.Boss ||
                   current.Expiry != last.Expiry ||
                   !current.Missions.SequenceEqual(last.Missions);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
} 