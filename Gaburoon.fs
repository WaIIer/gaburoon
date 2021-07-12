module Gaburoon.Gaburoon

open Gaburoon.Model
open Gaburoon.GoogleDrive
open Gaburoon.Logger
open Gaburoon.DataBase



open Gaburoon.Azure
open Gaburoon.Discord
open Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models


let private changeSpaces = "drive"
let private changeRequestFields = "*"

let getContentType model (downloadFile: DownloadFile) =
    logInfo $"Getting content type of {downloadFile.Path}"

    let adultInfo =
        try
            classifyImage model downloadFile
        with
        | _ ->
            logMsg $"Setting image to NSFW due to failure to retrieve adultInfo"
            let ai = AdultInfo()
            ai.IsAdultContent <- true
            ai


    if not adultInfo.IsAdultContent then
        let (_, parent) =
            model.ValidFolders.[downloadFile.GoogleFile.Parents |> Seq.head]

        if parent.ContentType = NSFW then
            logInfo $"Setting {downloadFile.Path} to NSFW because it is in an NSFW folder"
            adultInfo.IsAdultContent <- true


    downloadFile, adultInfo



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
        (fun file -> downloadFile model file)
        >> getContentType model
        >> (fun (downloadFile, adultInfo) -> downloadFile, adultInfo, insertRow downloadFile adultInfo)
        >> (fun (downloadFile, adultInfo, rowId) ->
            try
                (postToDiscord model (downloadFile, adultInfo, rowId), (uploadImageInformation downloadFile adultInfo))
                |> (updateRowInfo rowId)
            with
            | e -> logDebug $"Failed to post to discord {e}")
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
