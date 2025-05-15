using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Discord.Interactions;

namespace EveOnlineBot
{
    public static class ButtonUtils
    {
        private static readonly Dictionary<string, string> _itemCache = new();
        private static int _cacheCounter = 0;

        private static string GetCacheKey(string items)
        {
            var key = $"items_{_cacheCounter++}";
            _itemCache[key] = items;
            return key;
        }

        private static string GetItemsFromCache(string key)
        {
            return _itemCache.TryGetValue(key, out var items) ? items : string.Empty;
        }

        public static ComponentBuilder CreateMarketSelectMenu(string items, int selectedMarketId, Dictionary<int, string> markets)
        {
            var builder = new ComponentBuilder();
            
            // Add buttons for major trade hubs
            var tradeHubs = new Dictionary<int, string>
            {
                { 2, "Jita 4-4" },
                { 115, "Amarr" },
                { 117, "Dodixie" },
                { 116, "Rens" },
                { 118, "Hek" },
                { 6, "NPC" },
                { 114, "MJ-5F9" },
                { 3, "R1O-GN" }
            };

            var availableHubs = tradeHubs.Where(hub => markets.ContainsKey(hub.Key)).ToList();
            
            // Create select menu for markets
            var selectMenu = new SelectMenuBuilder()
                .WithCustomId($"market_select|{GetCacheKey(items)}")
                .WithPlaceholder("Select a market...")
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (var hub in availableHubs)
            {
                selectMenu.AddOption(hub.Value, hub.Key.ToString(), isDefault: hub.Key == selectedMarketId);
            }

            var row = new ActionRowBuilder().WithSelectMenu(selectMenu);
            builder.AddRow(row);

            return builder;
        }

        public static string GetItemsFromButtonId(string customId)
        {
            var parts = customId.Split('|');
            if (parts.Length != 2 || (parts[0] != "appraise" && parts[0] != "market_select"))
            {
                return string.Empty;
            }

            return GetItemsFromCache(parts[1]);
        }

        public static ComponentBuilder CreateValueCopyButtons(decimal sellValue, decimal buyValue, decimal splitValue, decimal buyValue90)
        {
            var builder = new ComponentBuilder();

            // Create a single row for all copy buttons
            var copyButtonsRow = new ActionRowBuilder();
            
            copyButtonsRow.AddComponent(new ButtonBuilder()
                .WithLabel("💾 Sell")
                .WithCustomId($"copy|sell|{sellValue}")
                .WithStyle(ButtonStyle.Success)
                .Build());

            copyButtonsRow.AddComponent(new ButtonBuilder()
                .WithLabel("💾 Buy")
                .WithCustomId($"copy|buy|{buyValue}")
                .WithStyle(ButtonStyle.Success)
                .Build());

            copyButtonsRow.AddComponent(new ButtonBuilder()
                .WithLabel("💾 Split")
                .WithCustomId($"copy|split|{splitValue}")
                .WithStyle(ButtonStyle.Success)
                .Build());

            copyButtonsRow.AddComponent(new ButtonBuilder()
                .WithLabel("💾 Buy90")
                .WithCustomId($"copy|buy90|{buyValue90}")
                .WithStyle(ButtonStyle.Success)
                .Build());

            builder.AddRow(copyButtonsRow);
            return builder;
        }
    }

    class Program
    {
        private readonly DiscordSocketClient _client;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _services;
        private Dictionary<int, string> _markets;

        public Program()
        {
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };
            _client = new DiscordSocketClient(config);
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("X-ApiKey", _configuration["Janice:ApiKey"]);
            _markets = new Dictionary<int, string>();

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_httpClient)
                .AddSingleton(_configuration)
                .AddSingleton(_markets)
                .AddSingleton<InteractionService>()
                .BuildServiceProvider();

            _interactions = _services.GetRequiredService<InteractionService>();
        }

        public static async Task Main(string[] args)
        {
            var program = new Program();
            await program.MainAsync();
        }

        public async Task MainAsync()
        {
            _client.Log += Log;
            _client.MessageReceived += MessageReceived;
            _client.ButtonExecuted += ButtonExecuted;
            _client.InteractionCreated += HandleInteraction;
            _client.Ready += async () =>
            {
                Console.WriteLine("Bot is ready! Registering slash commands...");
                await RegisterCommandsToAllGuilds();
            };
            _client.JoinedGuild += async (guild) =>
            {
                Console.WriteLine($"Joined new guild: {guild.Name} ({guild.Id})");
                await RegisterCommandsToGuild(guild.Id);
            };

            // Register interaction modules
            await _interactions.AddModulesAsync(System.Reflection.Assembly.GetEntryAssembly(), _services);
            Console.WriteLine("Interaction modules registered.");

            // Fetch available markets
            await FetchMarkets();

            await _client.LoginAsync(TokenType.Bot, _configuration["Discord:Token"]);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private async Task RegisterCommandsToAllGuilds()
        {
            try
            {
                Console.WriteLine("Registering commands to all guilds...");
                var guilds = _client.Guilds;
                Console.WriteLine($"Found {guilds.Count} guilds.");

                foreach (var guild in guilds)
                {
                    await RegisterCommandsToGuild(guild.Id);
                }

                Console.WriteLine("Finished registering commands to all guilds.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering commands to all guilds: {ex}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task RegisterCommandsToGuild(ulong guildId)
        {
            try
            {
                var guild = _client.GetGuild(guildId);
                if (guild == null)
                {
                    Console.WriteLine($"Warning: Could not find guild with ID {guildId}");
                    return;
                }

                Console.WriteLine($"Registering commands to guild: {guild.Name} ({guild.Id})");
                await _interactions.RegisterCommandsToGuildAsync(guild.Id);
                Console.WriteLine($"Successfully registered commands to guild: {guild.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering commands to guild {guildId}: {ex}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task RegisterSlashCommands()
        {
            try
            {
                Console.WriteLine("Starting slash command registration...");
                await RegisterCommandsToAllGuilds();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering slash commands: {ex}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                if (interaction is SocketMessageComponent component)
                {
                    if (component.Data.CustomId.StartsWith("market_select|"))
                    {
                        var parts = component.Data.CustomId.Split('|');
                        var items = ButtonUtils.GetItemsFromButtonId(component.Data.CustomId);
                        if (string.IsNullOrEmpty(items))
                        {
                            await component.RespondAsync("Error: Could not retrieve items from cache.", ephemeral: true);
                            return;
                        }

                        var marketId = int.Parse(component.Data.Values.First());
                        Console.WriteLine($"Processing appraisal for market {marketId} with items: {items}");

                        // Get the new appraisal
                        var fullAppraisal = await GetAppraisal(items, 1d, marketId);
                        var ninetyPercentAppraisal = await GetAppraisal(items, 0.9d, marketId);

                        if (!fullAppraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                        {
                            await component.RespondAsync("No valid items found in the appraisal.", ephemeral: true);
                            return;
                        }

                        var totalSellValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                        var totalBuyValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                        var totalSplitValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                        var totalBuyValue90Percent = ninetyPercentAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                        var totalVolume = fullAppraisal.GetProperty("totalVolume").GetDecimal();
                        var totalPackagedVolume = fullAppraisal.GetProperty("totalPackagedVolume").GetDecimal();
                        var marketName = fullAppraisal.GetProperty("market").GetProperty("name").GetString();
                        var fullAppraisalCode = fullAppraisal.GetProperty("code").GetString();
                        var ninetyPercentAppraisalCode = ninetyPercentAppraisal.GetProperty("code").GetString();

                        var embed = new EmbedBuilder()
                            .WithTitle("Total Appraisal")
                            .WithColor(Color.Blue)
                            .WithCurrentTimestamp()
                            .WithFooter($"Market: {marketName}");

                        embed.AddField("Total Values", 
                            $"Sell Value: {totalSellValue:N2} ISK\n" +
                            $"Buy Value: {totalBuyValue:N2} ISK\n" +
                            $"Split Value: {totalSplitValue:N2} ISK\n" +
                            $"Buy Value @90%: {totalBuyValue90Percent:N2} ISK", false);

                        embed.AddField("Volume Information", 
                            $"Total Volume: {totalVolume:N2} m³\n" +
                            $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                        embed.AddField("Full Appraisal Code", fullAppraisalCode, false);
                        embed.AddField("90% Appraisal Code", ninetyPercentAppraisalCode, false);

                        // Create the components with both market select menu and copy buttons
                        var marketSelect = ButtonUtils.CreateMarketSelectMenu(items, marketId, _markets);
                        var copyButtons = ButtonUtils.CreateValueCopyButtons(
                            totalSellValue,
                            totalBuyValue,
                            totalSplitValue,
                            totalBuyValue90Percent
                        );

                        // Combine both component sets
                        foreach (var row in copyButtons.ActionRows)
                        {
                            marketSelect.AddRow(row);
                        }

                        await component.UpdateAsync(x => 
                        {
                            x.Embed = embed.Build();
                            x.Components = marketSelect.Build();
                        });
                        return;
                    }
                }

                var context = new SocketInteractionContext(_client, interaction);
                var result = await _interactions.ExecuteCommandAsync(context, _services);
                
                if (!result.IsSuccess)
                {
                    Console.WriteLine($"Error executing command: {result.ErrorReason}");
                    if (interaction.Type == InteractionType.ApplicationCommand)
                    {
                        await interaction.RespondAsync($"Error executing command: {result.ErrorReason}", ephemeral: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling interaction: {ex}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    await interaction.RespondAsync("There was an error executing this command.", ephemeral: true);
                }
            }
        }

        private Task Log(LogMessage msg)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{msg.Severity}] {msg.Source}: {msg.Message}");
            return Task.CompletedTask;
        }

        private async Task FetchMarkets()
        {
            try
            {
                var url = $"{_configuration["Janice:BaseUrl"]}/markets";
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to fetch markets: {responseContent}");
                }

                var markets = JsonSerializer.Deserialize<JsonElement>(responseContent);
                foreach (var market in markets.EnumerateArray())
                {
                    var id = market.GetProperty("id").GetInt32();
                    var name = market.GetProperty("name").GetString();
                    _markets[id] = name;
                }

                Console.WriteLine("Available markets:");
                foreach (var market in _markets)
                {
                    Console.WriteLine($"ID: {market.Key}, Name: {market.Value}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching markets: {ex}");
            }
        }

        private ComponentBuilder CreateMarketButtons(string items, int selectedMarketId = 2)
        {
            return ButtonUtils.CreateMarketSelectMenu(items, selectedMarketId, _markets);
        }

        private async Task MessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot) return;

            if (Regex.IsMatch(message.Content, @"^!appraise\b"))
            {
                // Appraisal command
                var content = Regex.Replace(message.Content, @"^!appraise\b", "").Trim();
                if (string.IsNullOrEmpty(content))
                {
                    await message.Channel.SendMessageAsync("Please provide items to appraise.");
                    return;
                }

                try
                {
                    // Split the input into lines and process each line
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var requestBody = string.Join("\n", lines);
                    
                    var fullAppraisal = await GetAppraisal(requestBody, 1d, 2);
                    var ninetyPercentAppraisal = await GetAppraisal(requestBody, 0.9d, 2);

                    if (!fullAppraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                    {
                        await message.Channel.SendMessageAsync("No valid items found in the appraisal.");
                        return;
                    }

                    var totalSellValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                    var totalBuyValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalSplitValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                    var totalBuyValue90Percent = ninetyPercentAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalVolume = fullAppraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = fullAppraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = fullAppraisal.GetProperty("market").GetProperty("name").GetString();
                    var fullAppraisalCode = fullAppraisal.GetProperty("code").GetString();
                    var ninetyPercentAppraisalCode = ninetyPercentAppraisal.GetProperty("code").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("Total Appraisal")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Sell Value: {totalSellValue:N2} ISK\n" +
                        $"Buy Value: {totalBuyValue:N2} ISK\n" +
                        $"Split Value: {totalSplitValue:N2} ISK\n" +
                        $"Buy Value @90%: {totalBuyValue90Percent:N2} ISK", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Full Appraisal Code", fullAppraisalCode, false);
                    embed.AddField("90% Appraisal Code", ninetyPercentAppraisalCode, false);

                    // Create the components with both market buttons and copy buttons
                    var marketButtons = CreateMarketButtons(requestBody, 2);
                    var copyButtons = ButtonUtils.CreateValueCopyButtons(
                        totalSellValue,
                        totalBuyValue,
                        totalSplitValue,
                        totalBuyValue90Percent
                    );

                    // Combine both button sets
                    foreach (var row in copyButtons.ActionRows)
                    {
                        marketButtons.AddRow(row);
                    }

                    await message.Channel.SendMessageAsync(embed: embed.Build(), components: marketButtons.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error getting appraisal: {ex.Message}");
                }
            }

            // Recall command
            else if (Regex.IsMatch(message.Content, @"^!recall\b"))
            {
                var code = Regex.Replace(message.Content, @"^!recall\b", "").Trim();
                if (string.IsNullOrEmpty(code))
                {
                    await message.Channel.SendMessageAsync("Please provide an appraisal code to recall.");
                    return;
                }

                try
                {
                    var url = $"{_configuration["Janice:BaseUrl"]}/appraisal/{code}";
                    var response = await _httpClient.GetAsync(url);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"API request failed with status {response.StatusCode}: {responseContent}");
                    }

                    var appraisal = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var totalSellValue = appraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                    var totalBuyValue = appraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalSplitValue = appraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                    var totalVolume = appraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = appraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = appraisal.GetProperty("market").GetProperty("name").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("Recalled Appraisal")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Sell Value: {totalSellValue:N2} ISK\n" +
                        $"Buy Value: {totalBuyValue:N2} ISK\n" +
                        $"Split Value: {totalSplitValue:N2} ISK\n", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Appraisal Code", code, false);

                    await message.Channel.SendMessageAsync(embed: embed.Build());
                }                
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error recalling appraisal: {ex.Message}");
                }
            }

            // NPC market
            else if (Regex.IsMatch(message.Content, @"^!npcbuy90\b", RegexOptions.IgnoreCase))
            {
                var content = Regex.Replace(message.Content, @"^!npcbuy90\b", "", RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrEmpty(content))
                {
                    await message.Channel.SendMessageAsync("Please provide items to appraise.");
                    return;
                }

                try
                {
                    // Split the input into lines and process each line
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var requestBody = string.Join("\n", lines);
                    

                    var appraisal = await GetAppraisal(requestBody, 0.9d, 6);
                    if (!appraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                    {
                        await message.Channel.SendMessageAsync("No valid items found in the appraisal.");
                        return;
                    }

                    var totalBuyValue = appraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalVolume = appraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = appraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = appraisal.GetProperty("market").GetProperty("name").GetString();
                    var appraisalCode = appraisal.GetProperty("code").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("NPC Buy @90%")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Buy Value: {totalBuyValue:N2} ISK\n", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Appraisal Code", appraisalCode, false);

                    await message.Channel.SendMessageAsync(embed: embed.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error getting appraisal: {ex.Message}");
                }
            }

            // NPC market
            else if (Regex.IsMatch(message.Content, @"^!npcbuy"))
            {
                var content = Regex.Replace(message.Content, @"^!npcbuy", "").Trim();
                if (string.IsNullOrEmpty(content))
                {
                    await message.Channel.SendMessageAsync("Please provide items to appraise.");
                    return;
                }

                try
                {
                    // Split the input into lines and process each line
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var requestBody = string.Join("\n", lines);

                    var appraisal = await GetAppraisal(requestBody, 1d, 6);
                    if (!appraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                    {
                        await message.Channel.SendMessageAsync("No valid items found in the appraisal.");
                        return;
                    }

                    var totalBuyValue = appraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalVolume = appraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = appraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = appraisal.GetProperty("market").GetProperty("name").GetString();
                    var appraisalCode = appraisal.GetProperty("code").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("Total Appraisal")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Buy Value: {totalBuyValue:N2} ISK\n", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Appraisal Code", appraisalCode, false);

                    await message.Channel.SendMessageAsync(embed: embed.Build());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in MessageReceived: {ex}");
                    await message.Channel.SendMessageAsync($"Error getting appraisal: {ex.Message}");
                }
            }

            // Help command
            else if (Regex.IsMatch(message.Content, @"^!help\b"))
            {
                var embed = new EmbedBuilder()
                    .WithTitle("EVE Online Bot Commands")
                    .WithColor(Color.Blue)
                    .WithDescription("Here are all the available commands and how to use them:")
                    .WithCurrentTimestamp();

                embed.AddField("!appraise", 
                    "Get an appraisal for items at full market value.\n" +
                    "Usage: `!appraise <items>`\n" +
                    "Example: `!appraise PLEX` or `!appraise` followed by a list of items", false);

                embed.AddField("!recall", 
                    "Recall a previous appraisal using its code.\n" +
                    "Usage: `!recall <appraisal_code>`\n" +
                    "Example: `!recall ABC123`", false);

                embed.AddField("!npcbuy", 
                    "Get an appraisal for items at NPC buy prices (100% value).\n" +
                    "Usage: `!npcbuy <items>`\n" +
                    "Example: `!npcbuy PLEX` or `!npcbuy` followed by a list of items", false);

                embed.AddField("!npcbuy90", 
                    "Get an appraisal for items at NPC buy prices (90% value).\n" +
                    "Usage: `!npcbuy90 <items>`\n" +
                    "Example: `!npcbuy90 PLEX` or `!npcbuy90` followed by a list of items", false);

                embed.AddField("!help", 
                    "Display this help message with all available commands.\n" +
                    "Usage: `!help`", false);

                await message.Channel.SendMessageAsync(embed: embed.Build());
            }
        }

        private async Task ButtonExecuted(SocketMessageComponent component)
        {
            try
            {
                // Parse the custom ID to get the type of button
                var parts = component.Data.CustomId.Split('|');
                
                if (parts[0] == "copy")
                {
                    // Handle copy button
                    var valueType = parts[1];
                    var value = parts[2];
                    var formattedValue = decimal.Parse(value).ToString("N2");
                    
                    // Create a message with the value in a code block for easy copying
                    var response = new EmbedBuilder()
                        .WithTitle($"📋 {valueType} Value")
                        .WithDescription($"Click the value below to copy:\n```{formattedValue}```")
                        .WithColor(Color.Green)
                        .WithCurrentTimestamp()
                        .Build();

                    await component.RespondAsync(embed: response, ephemeral: true);
                    return;
                }
                else if (parts[0] == "appraise")
                {
                    // Existing appraisal button handling
                    var items = ButtonUtils.GetItemsFromButtonId(component.Data.CustomId);
                    if (string.IsNullOrEmpty(items))
                    {
                        await component.RespondAsync("Error: Could not retrieve items from cache.", ephemeral: true);
                        return;
                    }

                    var market = int.Parse(parts[2]);

                    Console.WriteLine($"Processing appraisal for market {market} with items: {items}");

                    // Get the new appraisal
                    var fullAppraisal = await GetAppraisal(items, 1d, market);
                    var ninetyPercentAppraisal = await GetAppraisal(items, 0.9d, market);

                    if (!fullAppraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                    {
                        await component.RespondAsync("No valid items found in the appraisal.", ephemeral: true);
                        return;
                    }

                    var totalSellValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                    var totalBuyValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalSplitValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                    var totalBuyValue90Percent = ninetyPercentAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                    var totalVolume = fullAppraisal.GetProperty("totalVolume").GetDecimal();
                    var totalPackagedVolume = fullAppraisal.GetProperty("totalPackagedVolume").GetDecimal();
                    var marketName = fullAppraisal.GetProperty("market").GetProperty("name").GetString();
                    var fullAppraisalCode = fullAppraisal.GetProperty("code").GetString();
                    var ninetyPercentAppraisalCode = ninetyPercentAppraisal.GetProperty("code").GetString();

                    var embed = new EmbedBuilder()
                        .WithTitle("Total Appraisal")
                        .WithColor(Color.Blue)
                        .WithCurrentTimestamp()
                        .WithFooter($"Market: {marketName}");

                    embed.AddField("Total Values", 
                        $"Sell Value: {totalSellValue:N2} ISK\n" +
                        $"Buy Value: {totalBuyValue:N2} ISK\n" +
                        $"Split Value: {totalSplitValue:N2} ISK\n" +
                        $"Buy Value @90%: {totalBuyValue90Percent:N2} ISK", false);

                    embed.AddField("Volume Information", 
                        $"Total Volume: {totalVolume:N2} m³\n" +
                        $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                    embed.AddField("Full Appraisal Code", fullAppraisalCode, false);
                    embed.AddField("90% Appraisal Code", ninetyPercentAppraisalCode, false);

                    // Create the components with both market buttons and copy buttons
                    var marketButtons = CreateMarketButtons(items, market);
                    var copyButtons = ButtonUtils.CreateValueCopyButtons(
                        totalSellValue,
                        totalBuyValue,
                        totalSplitValue,
                        totalBuyValue90Percent
                    );

                    // Combine both button sets
                    foreach (var row in copyButtons.ActionRows)
                    {
                        marketButtons.AddRow(row);
                    }

                    await component.UpdateAsync(x => 
                    {
                        x.Embed = embed.Build();
                        x.Components = marketButtons.Build();
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ButtonExecuted: {ex}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await component.RespondAsync($"Error updating appraisal: {ex.Message}", ephemeral: true);
            }
        }

        private async Task<JsonElement> GetAppraisal(string items, double percentage, int market)
        {
            /// <summary>
            /// Makes a request to the Janice API to get an appraisal for the given items.
            /// </summary>
            /// <param name="items">The items to appraise, formatted as a string.</param>
            /// <param name="percentage">The percentage of the appraisal to return (100 for full, 90 for 90%).</param>
            /// <returns>A JsonElement containing the appraisal data.</returns>
            /// <exception cref="Exception">Thrown if the API request fails or returns an error.</exception>
            /// <exception cref="JsonException">Thrown if the response cannot be deserialized into a JsonElement.</exception>
            /// <exception cref="ArgumentNullException">Thrown if the items string is null or empty.</exception>
            /// <exception cref="HttpRequestException">Thrown if there is an issue with the HTTP request.</exception>
            try
            {
                var url = $"{_configuration["Janice:BaseUrl"]}/appraisal?market={market}&persist=true&compactize=true&pricePercentage={percentage}";

                Console.WriteLine($"Making request to: {url}");
                Console.WriteLine($"Using API Key: {_configuration["Janice:ApiKey"]}");
                Console.WriteLine($"Market ID: {market}");

                var content = new StringContent(
                    items,
                    System.Text.Encoding.UTF8,
                    "text/plain"
                );

                // Add headers to match curl example
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"Response Status: {response.StatusCode}");
                Console.WriteLine($"Response Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
                Console.WriteLine($"Response Content: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API request failed with status {response.StatusCode}: {responseContent}");
                }

                if (string.IsNullOrEmpty(responseContent))
                {
                    throw new Exception("API returned empty response");
                }

                if (responseContent.StartsWith("<"))
                {
                    throw new Exception("API returned HTML instead of JSON. Please check your API key and endpoint configuration.");
                }

                return JsonSerializer.Deserialize<JsonElement>(responseContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAppraisal: {ex}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private Embed CreateAppraisalEmbed(JsonElement appraisal, string itemName)
        {
            // Ensure the title doesn't exceed Discord's 256 character limit
            var title = $"Appraisal for {itemName}";
            if (title.Length > 256)
            {
                title = title.Substring(0, 253) + "...";
            }

            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();

            try
            {
                if (appraisal.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var item = items[0];
                    if (item.TryGetProperty("effectivePrices", out var prices))
                    {
                        // Add immediate prices
                        builder.AddField("Buy Price", $"{prices.GetProperty("buyPrice").GetDecimal():N2} ISK", true);
                        builder.AddField("Sell Price", $"{prices.GetProperty("sellPrice").GetDecimal():N2} ISK", true);
                        builder.AddField("Split Price", $"{prices.GetProperty("splitPrice").GetDecimal():N2} ISK", true);

                        // Add 5-day median prices
                        builder.AddField("5-Day Median Buy", $"{prices.GetProperty("buyPrice5DayMedian").GetDecimal():N2} ISK", true);
                        builder.AddField("5-Day Median Sell", $"{prices.GetProperty("sellPrice5DayMedian").GetDecimal():N2} ISK", true);
                        builder.AddField("5-Day Median Split", $"{prices.GetProperty("splitPrice5DayMedian").GetDecimal():N2} ISK", true);

                        // Add market information
                        if (appraisal.TryGetProperty("market", out var market))
                        {
                            builder.AddField("Market", market.GetProperty("name").GetString(), true);
                        }

                        // Add volume information
                        if (item.TryGetProperty("buyOrderCount", out var buyOrders) && 
                            item.TryGetProperty("sellOrderCount", out var sellOrders))
                        {
                            builder.AddField("Buy Orders", buyOrders.GetInt32().ToString("N0"), true);
                            builder.AddField("Sell Orders", sellOrders.GetInt32().ToString("N0"), true);
                        }

                        // Add timestamp
                        if (appraisal.TryGetProperty("datasetTime", out var datasetTime))
                        {
                            builder.WithFooter($"Data from {DateTime.Parse(datasetTime.GetString()).ToLocalTime():g}");
                        }
                    }
                    else
                    {
                        builder.AddField("Error", "No price data available", false);
                    }
                }
                else
                {
                    builder.AddField("Error", "Item not found", false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateAppraisalEmbed: {ex}");
                builder.AddField("Error", "Failed to parse price data", false);
            }

            return builder.Build();
        }
    }

    public class AppraisalModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly Dictionary<int, string> _markets;

        public AppraisalModule(HttpClient httpClient, IConfiguration configuration, Dictionary<int, string> markets)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _markets = markets;
        }

        [SlashCommand("appraise", "Get an appraisal for items at the specified market")]
        public async Task AppraiseCommand(
            [Discord.Interactions.Summary("items", "The items to appraise")] string items,
            [Discord.Interactions.Summary("market_name", "The market to appraise at")] string selectedMarket = "Jita 4-4")
        {
            await DeferAsync();

            try
            {
                // Find market ID from name
                var marketId = _markets.FirstOrDefault(x => x.Value.Equals(selectedMarket, StringComparison.OrdinalIgnoreCase)).Key;
                if (marketId == 0)
                {
                    marketId = 2; // Default to Jita 4-4 if market not found
                }

                var fullAppraisal = await GetAppraisal(items, 1d, marketId);
                var ninetyPercentAppraisal = await GetAppraisal(items, 0.9d, marketId);

                if (!fullAppraisal.TryGetProperty("items", out var itemsArray) || itemsArray.GetArrayLength() == 0)
                {
                    await FollowupAsync("No valid items found in the appraisal.");
                    return;
                }

                var totalSellValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                var totalBuyValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                var totalSplitValue = fullAppraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                var totalBuyValue90Percent = ninetyPercentAppraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                var totalVolume = fullAppraisal.GetProperty("totalVolume").GetDecimal();
                var totalPackagedVolume = fullAppraisal.GetProperty("totalPackagedVolume").GetDecimal();
                var marketName = fullAppraisal.GetProperty("market").GetProperty("name").GetString();
                var fullAppraisalCode = fullAppraisal.GetProperty("code").GetString();
                var ninetyPercentAppraisalCode = ninetyPercentAppraisal.GetProperty("code").GetString();

                var embed = new EmbedBuilder()
                    .WithTitle("Total Appraisal")
                    .WithColor(Color.Blue)
                    .WithCurrentTimestamp()
                    .WithFooter($"Market: {marketName}");

                embed.AddField("Total Values", 
                    $"Sell Value: {totalSellValue:N2} ISK\n" +
                    $"Buy Value: {totalBuyValue:N2} ISK\n" +
                    $"Split Value: {totalSplitValue:N2} ISK\n" +
                    $"Buy Value @90%: {totalBuyValue90Percent:N2} ISK", false);

                embed.AddField("Volume Information", 
                    $"Total Volume: {totalVolume:N2} m³\n" +
                    $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                embed.AddField("Full Appraisal Code", fullAppraisalCode, false);
                embed.AddField("90% Appraisal Code", ninetyPercentAppraisalCode, false);

                // Create the component with buttons, highlighting the selected market
                var componentBuilder = ButtonUtils.CreateMarketSelectMenu(items, marketId, _markets);

                await FollowupAsync(embed: embed.Build(), components: componentBuilder.Build());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AppraiseCommand: {ex}");
                await FollowupAsync($"Error getting appraisal: {ex.Message}");
            }
        }

        [SlashCommand("recall", "Recall a previous appraisal using its code")]
        public async Task RecallCommand(
            [Discord.Interactions.Summary("code", "The appraisal code to recall")] string code)
        {
            await DeferAsync();

            try
            {
                var url = $"{_configuration["Janice:BaseUrl"]}/appraisal/{code}";
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API request failed with status {response.StatusCode}: {responseContent}");
                }

                var appraisal = JsonSerializer.Deserialize<JsonElement>(responseContent);
                
                var totalSellValue = appraisal.GetProperty("effectivePrices").GetProperty("totalSellPrice").GetDecimal();
                var totalBuyValue = appraisal.GetProperty("effectivePrices").GetProperty("totalBuyPrice").GetDecimal();
                var totalSplitValue = appraisal.GetProperty("effectivePrices").GetProperty("totalSplitPrice").GetDecimal();
                var totalVolume = appraisal.GetProperty("totalVolume").GetDecimal();
                var totalPackagedVolume = appraisal.GetProperty("totalPackagedVolume").GetDecimal();
                var marketName = appraisal.GetProperty("market").GetProperty("name").GetString();

                var embed = new EmbedBuilder()
                    .WithTitle("Recalled Appraisal")
                    .WithColor(Color.Blue)
                    .WithCurrentTimestamp()
                    .WithFooter($"Market: {marketName}");

                embed.AddField("Total Values", 
                    $"Sell Value: {totalSellValue:N2} ISK\n" +
                    $"Buy Value: {totalBuyValue:N2} ISK\n" +
                    $"Split Value: {totalSplitValue:N2} ISK\n", false);

                embed.AddField("Volume Information", 
                    $"Total Volume: {totalVolume:N2} m³\n" +
                    $"Total Packaged Volume: {totalPackagedVolume:N2} m³", false);

                embed.AddField("Appraisal Code", code, false);

                await FollowupAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RecallCommand: {ex}");
                await FollowupAsync($"Error recalling appraisal: {ex.Message}");
            }
        }

        private async Task<JsonElement> GetAppraisal(string items, double percentage, int market)
        {
            /// <summary>
            /// Makes a request to the Janice API to get an appraisal for the given items.
            /// </summary>
            /// <param name="items">The items to appraise, formatted as a string.</param>
            /// <param name="percentage">The percentage of the appraisal to return (100 for full, 90 for 90%).</param>
            /// <returns>A JsonElement containing the appraisal data.</returns>
            /// <exception cref="Exception">Thrown if the API request fails or returns an error.</exception>
            /// <exception cref="JsonException">Thrown if the response cannot be deserialized into a JsonElement.</exception>
            /// <exception cref="ArgumentNullException">Thrown if the items string is null or empty.</exception>
            /// <exception cref="HttpRequestException">Thrown if there is an issue with the HTTP request.</exception>
            try
            {
                var url = $"{_configuration["Janice:BaseUrl"]}/appraisal?market={market}&persist=true&compactize=true&pricePercentage={percentage}";

                Console.WriteLine($"Making request to: {url}");
                Console.WriteLine($"Using API Key: {_configuration["Janice:ApiKey"]}");
                Console.WriteLine($"Market ID: {market}");

                var content = new StringContent(
                    items,
                    System.Text.Encoding.UTF8,
                    "text/plain"
                );

                // Add headers to match curl example
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"Response Status: {response.StatusCode}");
                Console.WriteLine($"Response Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
                Console.WriteLine($"Response Content: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API request failed with status {response.StatusCode}: {responseContent}");
                }

                if (string.IsNullOrEmpty(responseContent))
                {
                    throw new Exception("API returned empty response");
                }

                if (responseContent.StartsWith("<"))
                {
                    throw new Exception("API returned HTML instead of JSON. Please check your API key and endpoint configuration.");
                }

                return JsonSerializer.Deserialize<JsonElement>(responseContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAppraisal: {ex}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }
    }
} 
