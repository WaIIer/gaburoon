module Gaburoon.Gaburoon

open System
open Gaburoon.Model
open Gaburoon.GoogleDrive
open Gaburoon.Logger


open Google.Apis.Drive.v3.Data
open System.IO
open Gaburoon.Azure
open Gaburoon.Discord

let private changeSpaces = "drive"
let private changeRequestFields = "*"

let private changeIsNotTrashed (change: Change) =
    logInfo $"Checing if change is trashed: {change.File.Trashed}"

    (not change.File.Trashed.HasValue)
    || not change.File.Trashed.Value

let private isAllowedExtension (file: Google.Apis.Drive.v3.Data.File) =
    logInfo $"Checking extension of {file.Name}"

    let allowedFileExtensions =
        [| ".jpg"
           ".jpeg"
           ".gif"
           ".png"
           "webp" |]

    allowedFileExtensions
    |> Array.contains (Path.GetExtension file.Name)

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
    |> Seq.map (downloadFile model)
    |> Seq.map (getContentType model)
    |> Seq.map (postToDiscord model)
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
