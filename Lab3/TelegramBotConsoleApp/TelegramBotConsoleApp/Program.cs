using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using TelegramBotConsoleApp;
using TelegramBotConsoleApp.Data;
using TelegramBotConsoleApp.Handlers;
using Prometheus;

namespace TelegramBotConsoleApp
{

    public class Program
    {
        public async static Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, _) => cts.Cancel();


            //http://localhost:9184/metrics
            using var server = new KestrelMetricServer(port: 9184);
            server.Start();
            
           
            var host = CreateHostBuilder(args).Build();
            host.RunAsync(cts.Token);

            var telegramHandler = host.Services.GetRequiredService<ITelegramUpdateHandler>();

            var conf = host.Services.GetRequiredService<IConfiguration>();
            var token = conf.GetValue<string>("TelegramToken");
            
            var botClient = new TelegramBotClient(token);

            Console.WriteLine("Bot was started " + botClient.GetMeAsync().Result.FirstName);


            //await botClient.DeleteMyCommandsAsync();
            //var commands = await botClient.GetMyCommandsAsync();

            /*
            await botClient.SetMyCommandsAsync(new List<BotCommand>{ 
                new BotCommand { Command = "/start", Description = "Display bot's description." },
                new BotCommand { Command = "/help", Description = "Display bot's commands." },
                new BotCommand { Command = "/stats", Description = "Display count of notes, which you wrote to bot." },
                new BotCommand { Command = "/notes", Description = "Display notes, which you wrote to bot (by date)." }
                //new BotCommand { Command = "/statsbyday", Description = "Display count of notes, which you wrote to bot (by date)." }
            }, cancellationToken: cts.Token);
            */


            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { }, // receive all update types
            };
            botClient.StartReceiving(telegramHandler.HandleUpdateAsync, telegramHandler.HandleErrorAsync, receiverOptions, cancellationToken: cts.Token);


            Console.WriteLine("Bot started. Press ^C to stop");
            await Task.Delay(-1, cancellationToken: cts.Token);
            Console.WriteLine("Bot stopped");

            //await botClient.CloseAsync(cts.Token);
            
            Console.ReadLine();
        }


        public static void LoadConfiguration(HostBuilderContext host, IConfigurationBuilder builder)
        {
            builder
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();
        }

        private static void ConfigureServices(HostBuilderContext host, IServiceCollection services)
        {
            //services.AddLogging(logBuilder => logBuilder.AddConsole());
            var logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.DurableHttpUsingFileSizeRolledBuffers(requestUri: "http://localhost:24224/myapp.test")
                .WriteTo.Console()
                .CreateLogger();
            //.ForContext<Program>();

            services.AddLogging(x => x.AddSerilog(logger));

            services
                .AddDbContext<TelegramContext>(options =>
                {
                    options.UseSqlServer(
                        host.Configuration.GetConnectionString("DbConnection")
                        );
                } , ServiceLifetime.Scoped);
            
            services.AddScoped<ITelegramUpdateHandler, TelegramUpdateHandler>();
            services.AddScoped<Program>();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(LoadConfiguration)
                .ConfigureServices(ConfigureServices);
    }
}