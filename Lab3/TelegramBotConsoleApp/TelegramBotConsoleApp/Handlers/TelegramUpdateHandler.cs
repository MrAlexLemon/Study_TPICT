using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot;
using System.Text.Json;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;
using TelegramBotConsoleApp.Data;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Globalization;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.InlineQueryResults;
using System.Collections.Generic;
using Azure;
using System.Runtime;
using Serilog.Context;
using Serilog;
using System.Diagnostics.Metrics;
using Prometheus;

namespace TelegramBotConsoleApp.Handlers
{
    public class TelegramUpdateHandler : ITelegramUpdateHandler
    {
        private readonly ILogger<TelegramUpdateHandler> _logger;

        private readonly TelegramContext _context;
        private readonly Dictionary<Tuple<long, long>, DateTime> _settings;
        private readonly Dictionary<Tuple<long, long>, int> _pageSettings;

        private readonly Counter s_incomingMessages;
        private readonly Counter s_outcomingMessages;


        public TelegramUpdateHandler(ILogger<TelegramUpdateHandler> logger, TelegramContext context)
        {
            this._logger = logger;
            this._context = context;
            this._settings = new Dictionary<Tuple<long, long>, DateTime>();
            this._pageSettings = new Dictionary<Tuple<long, long>, int>();

            this.s_incomingMessages = Metrics.CreateCounter("incoming_messages", "The number of incoming messages");
            this.s_outcomingMessages = Metrics.CreateCounter("outcoming_messages", "The number of outcoming messages");
        }
        
        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Telegram.Bot.Types.Update update, CancellationToken cancellationToken)
        {
            this._logger.LogInformation("Message Handling was started.");
            s_incomingMessages.Inc(1);
            //this._logger.LogInformation("Created {@User} on {Created}", exampleUser, DateTime.Now);

            var handler = update.Type switch
            {
                UpdateType.Message => HandleMessageRecievedAsync(botClient, update.Message!, cancellationToken),
                UpdateType.CallbackQuery => HandleCallbackQueryAsync(botClient, update.CallbackQuery!, cancellationToken),
                _ => Task.CompletedTask,
            };

            try
            {
                await handler;
            }
            catch (Exception exception)
            {
                await HandleErrorAsync(botClient, exception, cancellationToken);
            }

            this._logger.LogInformation("Message Handling was ended.");
        }

        private async Task HandleMessageRecievedAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            if (message.Type != MessageType.Text)
                return;

            this._logger.LogInformation("{@User}", new {UserId = message.From.Id, UserName = message.From.Username, Message = message.Text, MessageDate = message.Date });

            if (!_settings.ContainsKey(new Tuple<long, long>(message.From.Id, message.Chat.Id)))
                _settings.Add(new Tuple<long, long>(message.From.Id, message.Chat.Id), DateTime.UtcNow);
            if(!_pageSettings.ContainsKey(new Tuple<long, long>(message.From.Id, message.Chat.Id)))
                _pageSettings.Add(new Tuple<long, long>(message.From.Id, message.Chat.Id), 1);


            var task = message.Text!.ToLower().Trim().Split(' ')[0] switch
            {
                "/start" => HandleStartAsync(botClient, message, cancellationToken),
                "/help" => HandleHelpAsync(botClient, message, cancellationToken),
                "/stats" => HandleStatsAsync(botClient, message, cancellationToken),
                "/notes" => SendInlineCalendarAsync(botClient, message.From!.Id, message.Chat.Id, DateTime.UtcNow, null, cancellationToken),
                _ => HandleNoteAsync(botClient, message, cancellationToken),
            };
            await task;
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery query, CancellationToken cancellationToken)
        {
            if (query.Data is null || query.Data.ToLower().Trim() == "empty" || query.Data.ToLower().Trim() == "")
                return;

            this._logger.LogInformation("{@User}", new { UserId = query.From.Id, UserName = query.From.Username, Message = query.Data, MessageDate = query.Message.Date });

            if (!_settings.ContainsKey(new Tuple<long, long>(query.From.Id, query.Message.Chat.Id)))
                _settings.Add(new Tuple<long, long>(query.From.Id, query.Message.Chat.Id), DateTime.UtcNow);
            if (!_pageSettings.ContainsKey(new Tuple<long, long>(query.From.Id, query.Message.Chat.Id)))
                _pageSettings.Add(new Tuple<long, long>(query.From.Id, query.Message.Chat.Id), 1);

            var task = query.Data.ToLower().Trim() switch
            {
                "next" => SendInlineCalendarAsync(botClient, query.From.Id, query.Message!.Chat.Id, _settings[new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)].AddMonths(1), query.Message.MessageId, cancellationToken),
                "previous" => SendInlineCalendarAsync(botClient, query.From.Id, query.Message!.Chat.Id, _settings[new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)].AddMonths(-1), query.Message.MessageId, cancellationToken),
                "previousnote" => GetNotesByDateCalendarAsync(botClient, _pageSettings[new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)] - 1, query, query.Message.MessageId, cancellationToken),//Task.CompletedTask,
                "nextnote" => GetNotesByDateCalendarAsync(botClient, _pageSettings[new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)] + 1, query, query.Message.MessageId, cancellationToken),//Task.CompletedTask,
                _ => GetNotesByDateCalendarAsync(botClient, 1, query, null, cancellationToken)//Task.CompletedTask,
            };

            await task;
        }

        private async Task HandleStartAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            this._logger.LogInformation("Start Command Handling was started.");

            var text = $"Hello, I'm a {botClient.GetMeAsync().Result.FirstName} bot. You may write some notes and then handle them.";
            await botClient.SendTextMessageAsync(message.Chat.Id, text, ParseMode.Html, cancellationToken: cancellationToken);

            s_outcomingMessages.Inc(1);
            this._logger.LogInformation("Start Command Handling was ended.");
        }

        private async Task HandleHelpAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            this._logger.LogInformation("Help Command Handling was started.");

            var text = $"Some commands: \n";
            var botCommands = await botClient.GetMyCommandsAsync();

            foreach (var command in botCommands)
            {
                text += $"{command.Command} : {command.Description} \n";
            }

            await botClient.SendTextMessageAsync(message.Chat.Id, text, ParseMode.Markdown, cancellationToken: cancellationToken);

            s_outcomingMessages.Inc(1);
            this._logger.LogInformation("Help Command Handling was ended.");
        }

        private async Task HandleNoteAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            this._logger.LogInformation("Note Command Handling was started.");

            var lastMessageDate = await _context.MessageInfos.Where(x => x.ChatId == message.Chat.Id && x.UserId == message.From!.Id)
                .OrderByDescending(x => x.MessageDate).Take(1).Select(x => x.MessageDate).FirstOrDefaultAsync(cancellationToken);

            if (lastMessageDate.AddMinutes(5) >= message.Date)
            {
                await HandleErrorAsync(botClient, new Exception("Stop spamming!"), cancellationToken);
                await botClient.SendTextMessageAsync(message.Chat.Id, "Stop spamming!", ParseMode.Html, replyToMessageId: message.MessageId, cancellationToken: cancellationToken);
                s_outcomingMessages.Inc(1);
                return;
            }


            if (message.Text is null || message.Text.Length >= 4000)
            {
                await HandleErrorAsync(botClient, new Exception("Invalid message size!"), cancellationToken);
                await botClient.SendTextMessageAsync(message.Chat.Id, "Invalid message size!", ParseMode.Html, replyToMessageId: message.MessageId, cancellationToken: cancellationToken);
                s_outcomingMessages.Inc(1);
                return;
            }

            await this._context.MessageInfos.AddAsync(new MessageInfo() 
            { 
                ChatId = message.Chat.Id, 
                UserId = message.From!.Id,
                MessageDate = message.Date,
                MessageId = message.MessageId,
                Message = message.Text
            }, 
            cancellationToken);
            await this._context.SaveChangesAsync(cancellationToken);

            var text = $"Note was written! \n" +
                $"You wrote: {message.Text}";
            await botClient.SendTextMessageAsync(message.Chat.Id, text, ParseMode.Html, replyToMessageId: message.MessageId, cancellationToken: cancellationToken);

            s_outcomingMessages.Inc(1);
            this._logger.LogInformation("Note Command Handling was ended.");
        }

        private async Task HandleStatsAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            this._logger.LogInformation("Stats Command Handling was started.");

            var countMessagesHandler = await this._context.MessageInfos
                .Where(x =>
                x.ChatId == message.Chat.Id &&
                x.UserId == message.From!.Id)
                .CountAsync(cancellationToken);

            var countMessagesByMonthHandler =
            await this._context.MessageInfos.GroupBy(x => new { x.ChatId, x.UserId, x.MessageDate.Year, x.MessageDate.Month })
            .Select(x =>
            new { Period = $"{x.Key.Year}.{x.Key.Month}", count = x.Count() })
            .ToListAsync();

            var text = $"Your stats: \n" +
                $"You wrote: {countMessagesHandler} notes. \n";

            if (countMessagesHandler == 0)
            {
                text += $"Statistics by period (by month): {countMessagesHandler} \n";
            }
            else
            {
                text += $"Statistics by period (by month): \n";
            }

            foreach (var period in countMessagesByMonthHandler.OrderByDescending(x=>x.Period))
            {
                text += $"{period.Period} : {period.count} \n";
            }

            await botClient.SendTextMessageAsync(message.Chat.Id, text, ParseMode.Html, cancellationToken: cancellationToken);

            s_outcomingMessages.Inc(1);
            this._logger.LogInformation("Stats Command Handling was started.");
        }

        private async Task GetNotesByDateCalendarAsync(ITelegramBotClient botClient, int  page , CallbackQuery query, int? messageId, CancellationToken cancellationToken)
        {
            this._logger.LogInformation("GetNotesByDateCalendarAsync Handling was started.");

            if(messageId is null)
                await botClient.DeleteMessageAsync(query.Message!.Chat.Id, query.Message.MessageId, cancellationToken);

            var inputDate = query.Data.ToLower().Trim() switch
            {
                "previousnote" => _settings[new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)],
                "nextnote" => _settings[new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)],
                _ => DateTime.ParseExact(query.Data!, "yyyy-MM-dd", CultureInfo.InvariantCulture).Date
            };


            if (_settings.ContainsKey(new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)))
                _settings[new Tuple<long, long>(query.From.Id, query.Message!.Chat.Id)] = inputDate;


            var countMessagesHandler = await this._context.MessageInfos
               .Where(x =>
               x.ChatId == query.Message.Chat.Id &&
               x.UserId == query.From!.Id &&
               x.MessageDate.Date == inputDate.Date)
               .CountAsync(cancellationToken);

            if (page <= 0 || page > countMessagesHandler)
            {
                if (messageId is null)
                {
                    await botClient.SendTextMessageAsync(
                            chatId: query.Message.Chat.Id,
                            text: $"0 notes at this date: {inputDate.Date.ToString("yyyy-MM-dd")}.",
                            cancellationToken: cancellationToken);
                    s_outcomingMessages.Inc(1);
                }
                return;
            }

            var text = $"You wrote: {countMessagesHandler} notes ({inputDate.Year}.{inputDate.Month}.{inputDate.Day}).\n";

            if(countMessagesHandler == 0)
            {
                await botClient.SendTextMessageAsync(
                    chatId: query.Message.Chat.Id,
                    text: text,
                    cancellationToken: cancellationToken);
                s_outcomingMessages.Inc(1);
                return;
            }

            var note = await this._context.MessageInfos.Where(x =>
               x.ChatId == query.Message.Chat.Id &&
               x.UserId == query.From!.Id &&
               x.MessageDate.Date == inputDate.Date).OrderBy(x=>x.MessageDate).Skip(page-1).Take(1).FirstOrDefaultAsync(cancellationToken);

            text += $"Note Date: {note.MessageDate}\n";
            text += $"Note`s content:\n {note.Message}";

            var tempKey = new Tuple<long, long>(query.Message.Chat.Id, query.From.Id);

            if (!_pageSettings.ContainsKey(tempKey))
                _pageSettings.Add(tempKey, page);
            else
                _pageSettings[tempKey] = page;

            
            InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(
                new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(text: "<", callbackData: "PreviousNote"),
                        InlineKeyboardButton.WithCallbackData(text: $"{page}/{countMessagesHandler}", callbackData: "empty"),
                        InlineKeyboardButton.WithCallbackData(text: ">", callbackData: "NextNote"),
                    }

                });


            if (messageId is null)
            {
                await botClient.SendTextMessageAsync(
                        chatId: query.Message.Chat.Id,
                        text: text,
                        replyMarkup: inlineKeyboard,
                        cancellationToken: cancellationToken);
            }
            else
            {
                await botClient.EditMessageTextAsync(query.Message.Chat.Id, messageId.Value, text, replyMarkup: inlineKeyboard);
            }

            s_outcomingMessages.Inc(1);
            this._logger.LogInformation("GetNotesByDateCalendarAsync Handling was ended.");
        }

        public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(exception.Message));
            _logger.LogError(JsonSerializer.Serialize(exception.Message));
            return Task.CompletedTask;
        }

        private async Task SendInlineCalendarAsync(ITelegramBotClient botClient, long UserId, long ChatId, DateTime inputDate, int? messageId, CancellationToken cancellationToken)
        {
            this._logger.LogInformation("SendInlineCalendarAsync was started.");

            var tempKey = new Tuple<long, long>(ChatId, UserId);

            if (!_settings.ContainsKey(tempKey))
                _settings.Add(tempKey, inputDate);
            else
                _settings[tempKey] = inputDate;

            InlineKeyboardMarkup calendarKeyboardresult =  GetInlineCalendar(inputDate);

            if (messageId is not null)
                await botClient.EditMessageReplyMarkupAsync(ChatId, messageId.Value, calendarKeyboardresult, cancellationToken);
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: ChatId,
                    text: "Please, choose date.",
                    replyMarkup: calendarKeyboardresult,
                    cancellationToken: cancellationToken);
            }

            s_outcomingMessages.Inc(1);
            this._logger.LogInformation("SendInlineCalendarAsync was ended.");
        }


        private InlineKeyboardMarkup GetInlineCalendar(DateTime inputDate)
        {
            var currentDate = inputDate;//DateTime.UtcNow;
            var year = currentDate.Year;
            var month = currentDate.Month;

            var date = new DateTime(year, month, 1);
            int[,] calendar = new int[6, 7];


            var header = new[] { InlineKeyboardButton.WithCallbackData(text: CultureInfo.GetCultureInfo("en").DateTimeFormat.GetMonthName(month) + " " + year, callbackData: "empty") };
            var daysOfWeek = new[] {
                InlineKeyboardButton.WithCallbackData(text: "Mo", callbackData: "empty"),
                InlineKeyboardButton.WithCallbackData(text: "Tu", callbackData: "empty"),
                InlineKeyboardButton.WithCallbackData(text: "We", callbackData: "empty"),
                InlineKeyboardButton.WithCallbackData(text: "Th", callbackData: "empty"),
                InlineKeyboardButton.WithCallbackData(text: "Fr", callbackData: "empty"),
                InlineKeyboardButton.WithCallbackData(text: "Sa", callbackData: "empty"),
                InlineKeyboardButton.WithCallbackData(text: "Su", callbackData: "empty")
            };


            List<InlineKeyboardButton[]> calendarKeyboard = new List<InlineKeyboardButton[]>();
            List<InlineKeyboardButton> calendarContent = new List<InlineKeyboardButton>();

            calendarKeyboard.Add(header);
            calendarKeyboard.Add(daysOfWeek);


            int days = DateTime.DaysInMonth(year, month);
            int currentDay = 1;
            var dayOfWeek = (int)date.DayOfWeek;
            for (int i = 0; i < calendar.GetLength(0); i++)
            {
                for (int j = 0; j < calendar.GetLength(1) && currentDay - dayOfWeek + 1 <= days; j++)
                {
                    if (i == 0 && month > j)
                    {
                        calendar[i, j] = 0;
                    }
                    else
                    {
                        calendar[i, j] = currentDay - dayOfWeek + 1;
                        currentDay++;
                    }
                }
            }


            for (int i = 0; i < calendar.GetLength(0); i++)
            {
                for (int j = 0; j < calendar.GetLength(1); j++)
                {
                    if (calendar[i, j] > 0)
                    {
                        calendarContent.Add(InlineKeyboardButton.WithCallbackData(text: calendar[i, j].ToString(), callbackData: new DateTime(year, month, calendar[i, j]).ToString("yyyy-MM-dd")));
                    }
                    else
                    {
                        calendarContent.Add(InlineKeyboardButton.WithCallbackData(text: " ", callbackData: "empty"));
                    }
                }
                calendarKeyboard.Add(calendarContent.ToArray());
                calendarContent = new List<InlineKeyboardButton>();
            }

            calendarKeyboard.Add(new[] {
                InlineKeyboardButton.WithCallbackData(text: "<", callbackData: "Previous"),
                InlineKeyboardButton.WithCallbackData(text: " ", callbackData: "empty"),
                InlineKeyboardButton.WithCallbackData(text: ">", callbackData: "Next")
                });


            InlineKeyboardMarkup calendarKeyboardresult = new InlineKeyboardMarkup(calendarKeyboard);

            return calendarKeyboardresult;
        }
    }
}
