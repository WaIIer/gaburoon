module Gaburoon.Discord

open Discord.WebSocket
open Discord

open Gaburoon.Logger
open Gaburoon.Model
open System.Threading.Tasks
open System
open Google.Apis.Drive.v3.Data
open Gaburoon.DataBase
open System.Text.RegularExpressions
open Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models

let private deleteMessage (message: SocketMessage) (dbEntry: DbEntry) =
    try
        logMsg $"Trying to delete message with id {dbEntry.DiscordMessageId.Value}"

        if dbEntry.IsRemoved then
            logMsg $"Message already deleted"
        else
            message.Channel.DeleteMessageAsync(dbEntry.DiscordMessageId.Value |> uint64)
            |> Async.AwaitTask
            |> Async.RunSynchronously

            logMsg $"Message deleted successfully"

            dbMarkRemoved dbEntry.DiscordMessageId.Value
    with
    | e -> logError $"Failed to delete message with id {dbEntry.DiscordMessageId.Value}: {e |> string}"

    Task.CompletedTask

/// Execute command
/// called from onMessage
let private handleCommand (cmd: String) (imageId: int64) (message: SocketMessage) : Task =
    logInfo $"Processing command: !{cmd} {imageId}"
    let cmd = cmd.ToUpper()

    try
        let dbEntry = getMessageInfoFromImageId imageId

        logInfo $"Got Discord Message ID: {dbEntry.DiscordMessageId.Value}"

        match cmd with
        | "DELETE" -> deleteMessage message dbEntry
        | "HIDE" -> Task.CompletedTask
        | "SPOILER" -> Task.CompletedTask
        | _ ->
            printfn $"Unknown command {cmd}"
            Task.CompletedTask
    with
    | e ->
        printfn $"Failed to get message ID from: {imageId}"
        printfn $"{e}"
        Task.CompletedTask



/// Run this function whenever a message is posted in Gaburoon's text channel
/// Look for a command (![command] [post id])
/// Process command if it matches syntax
let onMessage (message: SocketMessage) =
    let content = message.Content
    logMsg content

    if content.Length = 0 then
        Task.CompletedTask
    else
        let r = @"!(\w+) ([0-9]+)"

        let matches =
            Regex.Match(content, r).Groups
            |> Seq.map (fun group -> group.Value)
            |> Array.ofSeq
        // If valid command
        if (matches |> Array.length) = 3 then
            handleCommand matches.[1] (matches.[2] |> Int64.Parse) message
        else
            Task.CompletedTask

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
    discordClient.add_MessageReceived (Func<SocketMessage, Task>(onMessage))
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

let private sendImage (textChannel: SocketTextChannel) path message contentType =
    let restUserMessage =
        textChannel.SendFileAsync(path, message, false, isSpoiler = (contentType = NSFW))
        |> Async.AwaitTask
        |> Async.RunSynchronously

    System.IO.File.Delete path |> ignore

    restUserMessage

let postToDiscord model (downloadFile: DownloadFile, adultInfo: AdultInfo, rowId: int64) =
    logInfo $"posting {contentType adultInfo} image: {downloadFile.Path}"

    try
        (sendImage model.TextChannel downloadFile.Path $"{rowId}: {downloadFile.Path}" (contentType adultInfo))
            .Id
        |> int64

    with
    | e ->
        logError $"Failed to post {downloadFile.Path}: {e |> string}"
        raise e
