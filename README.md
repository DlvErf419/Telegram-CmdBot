# Telegram-CmdBot
# Telegram CMD Bot

Console-based Telegram bot in C# (.NET) using Telegram.Bot 16.0.0. Send messages instantly from the terminal, or create multiple daily number schedules that post at a specific hour and minute with a chosen increment. Each schedule runs in the background and persists across restarts via a JSON state file (state.txt).

Features:
• Instant send (type a message and deliver immediately)
• Add, list, and remove multiple daily number schedules
• Per-schedule: initial number, HH\:MM time, increment step
• Duplicate-send protection within the same minute
• Iran time zone by default (Asia/Tehran / Iran Standard Time)
• State persisted to state.txt so jobs resume on restart

Setup:

1. Create a bot with BotFather, obtain the bot token, and add the bot to your target channel (as admin if posting to a channel).
2. Put your bot token and channel ID in the app config file.
3. Run the app; use the menu to send text (option 1) or manage schedules (option 2).

Notes:
• Do not commit real tokens to GitHub; use placeholders.
• Delete state.txt to reset all schedules and counters. | #Telegram | #ErfDelv | #Telegram_Bot
،
