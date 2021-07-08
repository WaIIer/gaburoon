module Gaburoon.Gaburoon

open Gaburoon.Model
open Gaburoon.GoogleDrive
open Gaburoon.Logger
open Gaburoon.DataBase


open System.Text
open Google.Apis.Drive.v3.Data
open System.IO
open Gaburoon.Azure
open Gaburoon.Discord
open System.Threading.Tasks
open Discord.WebSocket
open System.Text.RegularExpressions
open System

let private changeSpaces = "drive"
let private changeRequestFields = "*"

/// Execute command
/// called from onMessage
let private handleCommand model (cmd: String) (imageId: uint64) =
    logInfo $"Processing command: !{cmd} {imageId}"
    let cmd = cmd.ToUpper()

    try
        let messageId = getMessageIdFromImgageId model imageId

        logInfo $"Got Discord Message ID: {messageId}"

        match cmd with
        | "DELTE" -> ()
        | "HIDE"
        | "SPOILER" -> ()
        | _ -> printfn $"Unknown command {cmd}"
    with
    | e ->
        printfn "Failed to get message ID from: {imageId}"
        printfn $"{e}"

    Task.CompletedTask



/// Run this function whenever a message is posted in Gaburoon's text channel
/// Look for a command (![command] [post id])
/// Process command if it matches syntax
let private onMessage model (message: SocketMessage) =
    let content = message.Content

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
            handleCommand model matches.[1] (matches.[2] |> UInt64.Parse)
        else
            Task.CompletedTask

let getContentType model (downloadFile: DownloadFile) =
    logInfo $"Getting content type of {downloadFile.Path}"

    let (_, parent) =
        model.ValidFolders.[downloadFile.GoogleFile.Parents |> Seq.head]

    if parent.ContentType = NSFW then
        logInfo $"Setting {downloadFile.Path} to NSFW because it is in an NSFW folder"
        (downloadFile, NSFW)
    else
        downloadFile, (classifyImage model downloadFile)



let private lookForChanges model startToken =
    logInfo "Looking for changes"

    let service = model.GoogleDriveService

    let request = service.Changes.List(startToken)
    request.Spaces <- changeSpaces
    request.Fields <- changeRequestFields

    let execution = request.Execute()
    let changes = execution.Changes

    if changes.Count > 0 then
        logInfo $"found {changes.Count} change(s)"

    changes
    |> Seq.filter changeIsNotTrashed
    |> Seq.map (fun change -> change.File)
    |> Seq.filter isAllowedExtension
    |> Seq.filter (validateFile model)
    |> Seq.map (
        (downloadFile model)
        >> (getContentType model)
        >> (postToDiscord model)
    )
    |> Seq.iter ignore

    if changes.Count = 0 then
        startToken
    else
        logInfo $"Using new token {execution.NewStartPageToken}"
        execution.NewStartPageToken



let runGaburoon model =
    let mutable startToken =
        model
            .GoogleDriveService
            .Changes
            .GetStartPageToken()
            .Execute()
            .StartPageTokenValue

    while true do
        startToken <-
            try
                lookForChanges model startToken
            with
            | e ->
                logError $"{e |> string}"
                startToken

        System.Threading.Thread.Sleep(10 * 1000)
