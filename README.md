# Warframe Alert Bot

Telegram бот для отслеживания оповещений в игре Warframe. Бот позволяет получать уведомления о новых алертах и фильтровать их по типу наград.

## Функциональность

- Отслеживание активных алертов
- Фильтрация алертов по наградам
- Отметка выполненных алертов
- Настраиваемые уведомления

## Требования

- .NET 8.0 SDK
- PostgreSQL
- Telegram Bot Token (получить у @BotFather)

## Установка

1. Клонируйте репозиторий:
```bash
git clone https://github.com/yourusername/warframe-alert-bot.git
cd warframe-alert-bot
```

2. Настройте базу данных PostgreSQL:
- Создайте новую базу данных
- Обновите строку подключения в `appsettings.json`

3. Настройте токен бота:
- Получите токен у @BotFather в Telegram
- Добавьте токен в `appsettings.json` в секцию "TelegramBot:Token"

4. Установите зависимости и запустите миграции:
```bash
dotnet restore
dotnet ef database update
```

5. Запустите приложение:
```bash
dotnet run
```

## Использование

1. Найдите бота в Telegram по имени
2. Отправьте команду `/start` для начала работы
3. Используйте следующие команды:
   - `/alerts` - просмотр текущих алертов
   - `/settings` - настройка фильтров
   - `/complete` - отметить алерт как выполненный

## Развертывание

Для развертывания в облаке:

1. Создайте аккаунт в VK Cloud или Yandex Cloud
2. Настройте переменные окружения:
   - `ConnectionStrings__DefaultConnection`
   - `TelegramBot__Token`
3. Разверните приложение через Docker или напрямую

## Лицензия

MIT 