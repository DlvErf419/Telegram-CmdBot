using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramCmdBot
{
    class Program
    {
        // ===== Global config =====
        private static TimeZoneInfo _tz;
        private static ITelegramBotClient _bot = null!;
        private static string _channelId = "";
        private static string _stateFilePath = "state.txt"; // JSON content

        // Job manager
        private static ScheduleManager _manager = null!;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // Time zone (Iran)
            try { _tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
            catch { _tz = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); }

            // Load settings
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var token = config["Telegram:BotToken"];
            _channelId = config["Telegram:ChannelId"] ?? "";
            _stateFilePath = config["State:FilePath"] ?? "state.txt";

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_channelId))
            {
                Console.WriteLine("❌ BotToken or ChannelId is missing. Set them in appsettings.json.");
                return;
            }

            _bot = new TelegramBotClient(token);
            _manager = new ScheduleManager(_bot, _channelId, _stateFilePath, _tz);
            _manager.LoadFromDisk();  // load saved jobs
            _manager.StartAll();      // start runners

            // Main menu
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=== Telegram CMD Bot ===");
                Console.WriteLine("1) Send text immediately");
                Console.WriteLine("2) Manage scheduled numbers");
                Console.WriteLine("3) Exit");
                Console.Write("Your choice: ");
                var choice = Console.ReadLine();

                if (choice == "1")
                {
                    await ImmediateSendAsync();
                }
                else if (choice == "2")
                {
                    await ManageSchedulesAsync();
                }
                else if (choice == "3")
                {
                    Console.WriteLine("Exiting...");
                    _manager.StopAll();
                    break;
                }
                else
                {
                    Console.WriteLine("Invalid option.");
                }
            }
        }

        // ===== Part 1: Send now =====
        private static async Task ImmediateSendAsync()
        {
            Console.Write("Enter message text (empty = back): ");
            var text = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("Cancelled.");
                return;
            }

            try
            {
                await _bot.SendTextMessageAsync(
                    chatId: _channelId,
                    text: text,
                    parseMode: ParseMode.Markdown
                );
                Console.WriteLine("✅ Message sent.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Send error: " + ex.Message);
            }
        }

        // ===== Part 2: Schedules CRUD =====
        private static async Task ManageSchedulesAsync()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=== Manage Scheduled Numbers ===");
                Console.WriteLine("1) Add new schedule");
                Console.WriteLine("2) List schedules");
                Console.WriteLine("3) Remove a schedule");
                Console.WriteLine("4) Back to main menu");
                Console.Write("Your choice: ");
                var choice = Console.ReadLine();

                if (choice == "1")
                {
                    var job = CreateJobInteractively();
                    if (job != null)
                    {
                        _manager.AddJob(job);
                        Console.WriteLine("✅ Schedule added and started in background.");
                    }
                }
                else if (choice == "2")
                {
                    var jobs = _manager.ListJobs();
                    if (jobs.Count == 0)
                    {
                        Console.WriteLine("(no schedules)");
                    }
                    else
                    {
                        Console.WriteLine("-- Schedules --");
                        foreach (var j in jobs)
                        {
                            Console.WriteLine($"[{j.Id}] at {j.Hour:D2}:{j.Minute:D2} | current={j.CurrentNumber} | step=+{j.Step}");
                        }
                    }
                }
                else if (choice == "3")
                {
                    Console.Write("Enter schedule Id to remove: ");
                    var idStr = Console.ReadLine();
                    if (int.TryParse(idStr, out var id))
                    {
                        if (_manager.RemoveJob(id))
                            Console.WriteLine("✅ Removed.");
                        else
                            Console.WriteLine("⚠️ Not found.");
                    }
                    else
                    {
                        Console.WriteLine("Invalid Id.");
                    }
                }
                else if (choice == "4")
                {
                    return;
                }
                else
                {
                    Console.WriteLine("Invalid option.");
                }

                await Task.Delay(100);
            }
        }

        private static ScheduledJob? CreateJobInteractively()
        {
            int currentNumber = ReadInt("Enter initial number: ");
            var hour = ReadInt("Hour of day to send (0-23): ", 0, 23);
            var minute = ReadInt("Minute to send (0-59): ", 0, 59);
            var step = ReadInt("Increment step (added after each send): ");
            return new ScheduledJob
            {
                Hour = hour,
                Minute = minute,
                Step = step,
                CurrentNumber = currentNumber
            };
        }

        private static int ReadInt(string prompt, int? min = null, int? max = null)
        {
            while (true)
            {
                Console.Write(prompt);
                var s = Console.ReadLine();

                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    if (min.HasValue && n < min.Value) { Console.WriteLine($"Must be >= {min.Value}."); continue; }
                    if (max.HasValue && n > max.Value) { Console.WriteLine($"Must be <= {max.Value}."); continue; }
                    return n;
                }

                Console.WriteLine("Invalid input. Enter an integer.");
            }
        }
    }

    // ===================== Models & Manager =====================

    public class ScheduledJob
    {
        public int Id { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
        public int Step { get; set; }
        public int CurrentNumber { get; set; }

        [JsonIgnore] public CancellationTokenSource? Cts { get; set; }
        [JsonIgnore] public Task? Runner { get; set; }
        [JsonIgnore] public DateTime? LastSentKey { get; set; }
    }

    public class ScheduleState
    {
        public List<ScheduledJob> Jobs { get; set; } = new();
        public int NextId { get; set; } = 1;
    }

    public class ScheduleManager
    {
        private readonly ITelegramBotClient _bot;
        private readonly string _defaultChannelId;
        private readonly string _statePath;
        private readonly TimeZoneInfo _tz;

        private readonly object _lock = new();
        private ScheduleState _state = new();

        private readonly ConcurrentDictionary<int, ScheduledJob> _running = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        public ScheduleManager(ITelegramBotClient bot, string defaultChannelId, string statePath, TimeZoneInfo tz)
        {
            _bot = bot;
            _defaultChannelId = defaultChannelId;
            _statePath = statePath;
            _tz = tz;
        }

        public void LoadFromDisk()
        {
            try
            {
                if (File.Exists(_statePath))
                {
                    var json = File.ReadAllText(_statePath, Encoding.UTF8);
                    var loaded = JsonSerializer.Deserialize<ScheduleState>(json, _jsonOptions);
                    if (loaded != null) _state = loaded;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("⚠️ Failed to load state.txt: " + ex.Message);
            }
        }

        public void SaveToDisk()
        {
            try
            {
                lock (_lock)
                {
                    var json = JsonSerializer.Serialize(_state, _jsonOptions);
                    File.WriteAllText(_statePath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("⚠️ Failed to write state.txt: " + ex.Message);
            }
        }

        public void StartAll()
        {
            foreach (var job in _state.Jobs)
            {
                StartJob(job);
            }
        }

        public void StopAll()
        {
            foreach (var kv in _running)
            {
                kv.Value.Cts?.Cancel();
            }
        }

        public List<ScheduledJob> ListJobs()
        {
            lock (_lock)
            {
                return new List<ScheduledJob>(_state.Jobs);
            }
        }

        public void AddJob(ScheduledJob job)
        {
            lock (_lock)
            {
                job.Id = _state.NextId++;
                _state.Jobs.Add(job);
                SaveToDisk();
            }
            StartJob(job);
        }

        public bool RemoveJob(int id)
        {
            ScheduledJob? job;
            lock (_lock)
            {
                job = _state.Jobs.Find(j => j.Id == id);
                if (job == null) return false;

                _state.Jobs.Remove(job);
                SaveToDisk();
            }

            if (_running.TryRemove(id, out var running))
            {
                running.Cts?.Cancel();
            }

            return true;
        }

        private void StartJob(ScheduledJob job)
        {
            if (_running.TryGetValue(job.Id, out var existing))
            {
                existing.Cts?.Cancel();
            }

            var cts = new CancellationTokenSource();
            job.Cts = cts;
            job.Runner = Task.Run(() => RunnerLoop(job, cts.Token), cts.Token);
            _running[job.Id] = job;
        }

        private async Task RunnerLoop(ScheduledJob job, CancellationToken ct)
        {
            Console.WriteLine($"▶️  Running job {job.Id} at {job.Hour:D2}:{job.Minute:D2} (current={job.CurrentNumber}, step=+{job.Step})");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);

                    if (nowLocal.Hour == job.Hour && nowLocal.Minute == job.Minute)
                    {
                        var minuteKey = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, nowLocal.Hour, nowLocal.Minute, 0);
                        if (job.LastSentKey != minuteKey)
                        {
                            try
                            {
                                await _bot.SendTextMessageAsync(
                                    chatId: _defaultChannelId,
                                    text: job.CurrentNumber.ToString(CultureInfo.InvariantCulture),
                                    parseMode: ParseMode.Markdown,
                                    cancellationToken: ct
                                );

                                Console.WriteLine($"✅ Job {job.Id} sent: {job.CurrentNumber} @ {nowLocal:yyyy-MM-dd HH:mm}");

                                job.CurrentNumber += job.Step;
                                job.LastSentKey = minuteKey;
                                SaveToDisk();
                            }
                            catch (TaskCanceledException) { }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Job {job.Id} send error: {ex.Message}");
                            }

                            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { }
                        }
                    }

                    try { await Task.Delay(TimeSpan.FromSeconds(1), ct); } catch { }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Job {job.Id} loop error: {ex.Message}");
                    try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { }
                }
            }

            Console.WriteLine($"⏹ Job {job.Id} stopped.");
        }
    }
}
