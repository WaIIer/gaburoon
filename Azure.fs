module Gaburoon.Azure

open Gaburoon.Model
open Gaburoon.Setup

open System
open Azure.Security
open Azure.Identity
open Azure.Security.KeyVault.Secrets
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open System.IO

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
