module Gaburoon.GoogleDrive

open System
open Google.Apis.Drive.v3
open Google.Apis.Services
open Google.Apis.Auth.OAuth2

open Gaburoon.Model
open Gaburoon.Logger
open System.Collections.Generic
open System.IO

let private authenticate () =
    let scope =
        [| DriveService.Scope.DriveReadonly
           DriveService.Scope.DriveMetadataReadonly |]

    // TODO: unhard-code this
    let credential =
        GoogleCredential.FromFile("service-account.json")

    credential.CreateScoped scope


let getGoogleDriveService () =
    new DriveService(
        BaseClientService.Initializer(HttpClientInitializer = authenticate (), ApplicationName = "Test App")
    )

let private folderQuery name =
    $"name = '{name}'  and mimeType = 'application/vnd.google-apps.folder'"

let private pageSize (i: int32) = Nullable(i)

// TODO: Get all sub folders
let getValidFolders (config: GaburoonConfiguration) (googleDriveService: DriveService) =
    let validFolders = Dictionary<string, string * Folder>()

    config.Folders
    |> Seq.iter
        (fun kvp ->
            let folder = kvp.Value
            let request = googleDriveService.Files.List()
            request.Q <- folderQuery folder.Name
            request.PageSize <- pageSize 1

            let result = request.Execute().Files

            match (result |> Seq.tryHead) with
            | Some googleFile -> validFolders.[googleFile.Id] <- (googleFile.Id, folder)
            | _ -> logCrit $"Unable to find folder: {folder.Name} in Google Drive")

    validFolders

let downloadFile model (file: Data.File) =
    logInfo $"Downloading: {file.Name}"
    let service = model.GoogleDriveService

    use stream =
        new FileStream(file.Name, FileMode.Create, FileAccess.Write)

    (service.Files.Get file.Id).Download stream
    { Path = file.Name; GoogleFile = file }
