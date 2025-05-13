# EVE Online Discord Bot

A Discord bot that uses the Janice API to provide EVE Online item appraisals.

## Prerequisites

- .NET 8.0 SDK
- A Discord bot token (create one at https://discord.com/developers/applications)
- A Janice API key (get one at https://janice.e-351.com/)

## Setup

1. Update the `appsettings.json` file with your credentials:
```json
{
  "Discord": {
    "Token": "your_discord_bot_token_here"
  },
  "Janice": {
    "ApiKey": "your_janice_api_key_here",
    "BaseUrl": "https://janice.e-351.com/api/v5"
  }
}
```

2. Build and run the bot:
##### On Windows
```bash
dotnet build
dotnet run
```
##### On Linux
```bash
dotnet publish -r linux-x64
cd /bin/Release/net8.0/linux-x64
dotnet EVEOnlineDiscordBot.dll
```

## Usage

The bot responds to the following commands:

- `!appraise <item name>` - Get an appraisal for the specified item using Janice API
  Example: `!appraise PLEX`

## Features

- Item appraisal using Janice API
- Clean and simple Discord interface
- Real-time price updates
- Formatted embed messages with price information 
