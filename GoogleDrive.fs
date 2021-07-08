module Gaburoon.GoogleDrive

open System
open Google.Apis.Drive.v3
open Google.Apis.Services
open Google.Apis.Auth.OAuth2

open Gaburoon.Model
open Gaburoon.Logger
open System.Collections.Generic
open System.IO
open Google.Apis.Drive.v3.Data

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

/// Validate that the change to the file in Google Drive is not
/// the file getting 'trashed'/deleted
let changeIsNotTrashed (change: Change) =
    logInfo $"Checing if change is trashed: {change.File.Trashed}"

    (not change.File.Trashed.HasValue)
    || not change.File.Trashed.Value

/// Verify that the file uploaded to the drive is
/// a valid image type
let isAllowedExtension (file: File) =
    logInfo $"Checking extension of {file.Name}"

    let allowedFileExtensions =
        [| ".jpg"
           ".jpeg"
           ".gif"
           ".png"
           "webp" |]

    allowedFileExtensions
    |> Array.contains (Path.GetExtension file.Name)

let downloadFile model (file: Data.File) =
    logInfo $"Downloading: {file.Name}"
    let service = model.GoogleDriveService

    use stream =
        new FileStream(file.Name, FileMode.Create, FileAccess.Write)

    (service.Files.Get file.Id).Download stream
    { Path = file.Name; GoogleFile = file }

let private checkFolderTree model startingId =
    let directoryTree = ResizeArray<string>()
    directoryTree.Add(startingId)

    let rec recursiveCheck currentId =
        logMsg $"Recursive Check: {currentId}"

        let query =
            model.GoogleDriveService.Files.Get(currentId)

        query.Fields <- "name, parents"

        let queryResult = query.Execute()

        if
            queryResult.Parents.Count = 0
            || model.InvalidFolders.Contains(queryResult.Parents |> Seq.head)
        then
            logMsg $"No parents found for {queryResult.Name}, {startingId} is invalid"

            directoryTree
            |> Seq.iter (model.InvalidFolders.Add >> ignore)

            false
        else if model.ValidFolders.ContainsKey(queryResult.Parents |> Seq.head) then
            logMsg $"{startingId} is valid"

            let _, file =
                model.ValidFolders.[queryResult.Parents |> Seq.head]

            let contentType = file.ContentType

            directoryTree
            |> Seq.iter
                (fun fileId ->
                    model.ValidFolders.[fileId] <-
                        (fileId,
                         { Name = ""
                           ContentType = contentType
                           GoogleId = Some fileId }))

            true
        else
            directoryTree.Add currentId |> ignore
            recursiveCheck (queryResult.Parents |> Seq.head)

    recursiveCheck startingId


// Check if file is in a valid folder or invalid folder
// If the folder is new, traverse up its tree to dtermine if it in a tracked folder
let validateFile (model: GaburoonModel) (file: Data.File) =
    logInfo $"Validating parent of {file.Name}"

    match (file.Parents |> Seq.tryHead) with
    | None -> false
    | Some parent ->
        if model.ValidFolders.ContainsKey parent then
            true
        else if model.InvalidFolders.Contains parent then
            false
        else
            logInfo $"Traversing folder tree for {file.Name}"
            checkFolderTree model parent
