module Gaburoon.GoogleDrive

open Google.Apis.Drive.v3
open Google.Apis.Services
open Google.Apis.Auth.OAuth2

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
