module Gaburoon.Discord

open Discord.WebSocket
open Discord

open Gaburoon.Logger
open Gaburoon.Model
open Gaburoon.Trace
open System.Threading.Tasks
open System
open Gaburoon.DataBase
open System.Text.RegularExpressions
open Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models
open System.Net
open System.IO
open FSharp.Collections

let mutable private selfDestructingMessages = ResizeArray<uint64 * DateTime>()

let removeSelfDestructMessages (model: GaburoonModel) =
    if (not (selfDestructingMessages |> Seq.isEmpty))
       && ((selfDestructingMessages |> Seq.head |> snd) < DateTime.UtcNow) then
        model.TextChannel.DeleteMessageAsync(selfDestructingMessages |> Seq.head |> fst)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore

        selfDestructingMessages.RemoveAt 0

/// Send Image to Discord
let private sendImage (textChannel: ISocketMessageChannel) path message contentType =
    let restUserMessage =
        textChannel.SendFileAsync(path, message, false, isSpoiler = (contentType = NSFW))
        |> Async.AwaitTask
        |> Async.RunSynchronously

    System.IO.File.Delete path |> ignore

    restUserMessage

/// Asynchronously delete command message
let private deletCommandMessage (commandMessage: SocketMessage) =
    try
        commandMessage.DeleteAsync()
        |> Async.AwaitTask
        |> Async.RunSynchronously

        logMsg "Command message deleted"
    with
    | e -> logDebug $"Failed to delete command message: {e}"

/// Delete discord message and update database to reflect that the post has been deleted
let private deleteMessage (commandMessage: SocketMessage) (dbEntry: DbEntry) =
    logMsg $"Trying to delete message with id {dbEntry.DiscordMessageId.Value}"

    commandMessage.Channel.DeleteMessageAsync(dbEntry.DiscordMessageId.Value |> uint64)
    |> Async.AwaitTask
    |> Async.RunSynchronously

    logMsg $"Message deleted successfully"

    dbMarkRemoved dbEntry.DiscordMessageId.Value

/// Delete the original post and post a new version that is spoilered
let private hideImage (commandMessage: SocketMessage) (dbEntry: DbEntry) =
    let messageId =
        dbEntry.DiscordMessageId.Value |> UInt64.Parse

    let originalMessage =
        commandMessage.Channel.GetMessageAsync(messageId, CacheMode.AllowDownload, RequestOptions.Default)
        |> Async.AwaitTask
        |> Async.RunSynchronously

    originalMessage.DeleteAsync() |> ignore

    let attachment = originalMessage.Attachments |> Seq.head

    let webClient = new WebClient()
    webClient.DownloadFile(Uri(attachment.Url), attachment.Filename)

    /// Send as NSFW to force spoiler
    let updatedMessage =
        sendImage commandMessage.Channel attachment.Filename originalMessage.Content NSFW

    File.Delete attachment.Filename

    dbHide updatedMessage.Id originalMessage.Id

let private showImageInfo (commandMessage: SocketMessage) (dbEntry: DbEntry) =
    try
        let imageInfo = getImageInfo dbEntry.GoogleImageId

        let messageText =
            [ $"Id: {dbEntry.ImageId.Value} Name: {dbEntry.ImageName}"
              $"Uploader: {dbEntry.FileOwners}"
              $"""[Google Drive Link]("{dbEntry.GoogleImageUrl}")""" ]
            |> String.concat "\n"

        let embed = EmbedBuilder()
        embed.Title <- $"{Path.GetFileNameWithoutExtension dbEntry.ImageName} Info:"

        let uploaderField = EmbedFieldBuilder()

        let embed =
            embed.WithDescription "Uploader: {dbEntry.FileOwners}\nA second description"

        commandMessage.Channel.SendMessageAsync("", false, embed.Build())
        |> ignore

    with
    | e -> logDebug $"{e}"

let private getTitle (commandMessage: SocketMessage) (dbEntry: DbEntry) =
    try
        let imageMessage =
            commandMessage.Channel.GetMessageAsync(id = (dbEntry.DiscordMessageId.Value |> UInt64.Parse))
            |> Async.AwaitTask
            |> Async.RunSynchronously

        let imageUrl =
            (imageMessage.Attachments |> Seq.head).Url

        let showName = getShowName imageUrl

        let messageBody =
            $"{showName.English} ({showName.Romaji})"

        (commandMessage.Channel.SendMessageAsync(messageBody)
         |> Async.AwaitTask
         |> Async.RunSynchronously)
            .Id
        |> fun id -> selfDestructingMessages.Add(id, DateTime.UtcNow.AddSeconds(30.0))
    with
    | e -> logDebug $"{e}"

/// Execute command
/// called from onMessage
let private handleCommand (cmd: String) (imageId: int64) (commandMessage: SocketMessage) : Task =
    deletCommandMessage commandMessage

    logInfo $"Processing command: !{cmd} {imageId}"
    let cmd = cmd.ToUpper()

    async {
        try
            let dbEntry = getMessageInfoFromImageId imageId

            if dbEntry.IsRemoved then
                logMsg $"Message already deleted, not running command"
            else
                logInfo $"Got Discord Message ID: {dbEntry.DiscordMessageId.Value}"

                match cmd with
                | "DELETE" -> deleteMessage commandMessage dbEntry
                | "HIDE"
                | "SPOILER" -> hideImage commandMessage dbEntry
                | "TITLE"
                | "SHOW"
                | "NAME" -> getTitle commandMessage dbEntry
                | "INFO" -> showImageInfo commandMessage dbEntry
                | _ -> printfn $"Unknown command {cmd}"
        with
        | e ->
            printfn $"Failed to get message ID from: {imageId}"
            printfn $"{e}"
    }
    |> Async.Start

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
        let r = @"!(\w+) ([0-9]+)$"

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

let postToDiscord model (downloadFile: DownloadFile, adultInfo: AdultInfo, rowId: int64) =
    logInfo $"posting {contentType adultInfo} image: {downloadFile.Path}"

    try
        (sendImage
            model.TextChannel
            downloadFile.Path
            $"{rowId}: {Path.GetFileNameWithoutExtension downloadFile.Path}"
            (contentType adultInfo))
            .Id
        |> int64

    with
    | e ->
        logError $"Failed to post {downloadFile.Path}: {e |> string}"
        raise e
