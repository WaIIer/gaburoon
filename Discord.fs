module Gaburoon.Discord

open Gaburoon.Setup

open Discord.WebSocket
open Discord

open Gaburoon.Logger
open Gaburoon.Model
open System.Threading.Tasks
open System

let private discordLogger (msg: LogMessage) =
    let sev = msg.Severity

    (match msg.Severity with
     | LogSeverity.Critical -> logCrit
     | LogSeverity.Debug -> logDebug
     | LogSeverity.Error -> logError
     | _ -> logInfo)
        msg.Message

    Task.CompletedTask

let getDiscordClient discordToken (config: GaburoonConfiguration) =
    let discordClient = new DiscordSocketClient()
    discordClient.add_Log (Func<LogMessage, Task>(discordLogger))
    // TODO: implement
    discordClient.add_MessageReceived (Func<SocketMessage, Task>(fun x -> Task.CompletedTask))

    discordClient.LoginAsync(TokenType.Bot, discordToken, true)
    |> Async.AwaitTask
    |> Async.RunSynchronously

    discordClient.StartAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

    System.Threading.Thread.Sleep 2000

    let guild =
        try
            discordClient.Guilds
            |> Seq.find (fun guild -> guild.Name = config.DiscordGuild)
        with
        | _ ->
            let foundGuilds =
                discordClient.Guilds
                |> Seq.map (fun guild -> guild.Name)
                |> String.concat ", "

            logError $"Unable to find {config.DiscordGuild}"
            logInfo "Found: {foundGuilds}"
            failwith $"Unable to find {config.DiscordGuild} in {foundGuilds}"

    let channel =
        try
            guild.TextChannels
            |> Seq.find (fun tc -> tc.Name = config.TextChannel)
        with
        | _ ->
            let foundTextChannels =
                guild.TextChannels
                |> Seq.map (fun tc -> tc.Name)
                |> String.concat ", "

            logError $"Unable to find {config.TextChannel}"
            logInfo $"Found {foundTextChannels}"
            failwith $"Unable to find {config.TextChannel} in {foundTextChannels}"

    logInfo $"Bot started, posting to {guild.Name} #{channel.Name}"

    discordClient, channel
