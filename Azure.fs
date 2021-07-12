module Gaburoon.Azure

open Gaburoon.Model
open Gaburoon.Setup

open System
open System.IO
open Azure.Identity
open Azure.Security.KeyVault.Secrets
open Azure.Storage.Blobs
open Microsoft.Azure.CognitiveServices.Vision.ComputerVision


open System.IO
open Gaburoon.Logger
open Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models

// TODO: un-hard code this
let private secretKeys =
    [| "DISCORD-TOKEN"
       "gaburoon-cv-key1"
       "gaburoon-cv-key2"
       "gaburoonsa-connection-string1"
       "gaburoonsa-key1" |]

let private keyVaultUri keyVaultName =
    $"https://{keyVaultName}.vault.azure.net/"

let getSecrets (config: GaburoonConfiguration) =
    let keyVaultUri = keyVaultUri config.KeyVaultName

    let secretClient =
        try
            SecretClient(Uri keyVaultUri, DefaultAzureCredential())
        with
        | e -> failwith $"Unable to create secret client: {e |> string}"

    // TODO: Add error checking
    secretKeys
    |> Array.map (secretClient.GetSecretAsync >> Async.AwaitTask)
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Array.map (fun result -> result.Value.Value)
    |> Array.zip secretKeys
    |> dict

let private serviceAccountFile = "service-account.json"

// Get service account json file from Azure blob storage account
let getServiceAccountJson (config: GaburoonConfiguration) connectionString =
    if File.Exists serviceAccountFile then
        File.Delete serviceAccountFile

    let blobServiceClient = BlobServiceClient(connectionString)

    let containerClient =
        blobServiceClient.GetBlobContainerClient config.BlobContainer

    let blobClient =
        containerClient.GetBlobClient serviceAccountFile

    try
        blobClient.DownloadTo serviceAccountFile
    with
    | e -> failwith "Failed to download service account file: {e |> string}"
    |> ignore

    if File.ReadAllText serviceAccountFile
       |> String.IsNullOrEmpty then
        failwith "Unable to download service account file"

let private cvClientEndpoint cvResource =
    $"https://{cvResource}.cognitiveservices.azure.com/"

let classifyImage model (downloadFile: DownloadFile) =
    logInfo $"Classifying {downloadFile.Path}"

    try
        let image = downloadFile.Path
        let key1 = model.Secrets.["gaburoon-cv-key1"]

        let imageStream =
            new FileStream(image, FileMode.Open, FileAccess.Read)

        let cvClient =
            new ComputerVisionClient(ApiKeyServiceClientCredentials(key1))

        cvClient.Endpoint <- cvClientEndpoint model.Configuration.ComputerVisionResource

        (cvClient.AnalyzeImageInStreamWithHttpMessagesAsync(imageStream, [| Nullable(VisualFeatureTypes.Adult) |])
         |> Async.AwaitTask
         |> Async.RunSynchronously)
            .Body
            .Adult
    with
    | e ->
        logError $"Error getting info from azure: {e |> string}"
        raise e
