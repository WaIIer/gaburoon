open System
open System.Collections.Generic
open Newtonsoft.Json

open Gaburoon.Model
open Gaburoon.Setup
open Gaburoon.Azure
open Gaburoon.DataBase
open Gaburoon.GoogleDrive
open Gaburoon.Discord
open Gaburoon.Logger
open Gaburoon.Gaburoon

let deafultConfigFile = "GaburoonConfig.json"

let initializeGaburron (config: GaburoonConfiguration) =
    let secrets = getSecrets config

    (JsonConvert.SerializeObject(secrets, Formatting.Indented))
    |> logInfo

    getServiceAccountJson config secrets.["gaburoonsa-connection-string1"]

    let dbConnectionString =
        try
            initializeDatabase config
        with
        | e -> failwith $"Failed to create initialize database: {e}"

    let googleDriveService = getGoogleDriveService ()

    let (discordClient, textChannel) =
        getDiscordClient secrets.["DISCORD-TOKEN"] config

    let model =
        { GoogleDriveService = googleDriveService
          DiscordClient = discordClient
          TextChannel = textChannel
          Configuration = config
          ValidFolders = getValidFolders config googleDriveService
          InvalidFolders = HashSet<string>()
          Secrets = secrets
          ConnectionString = config.ConnectionString }


    (JsonConvert.SerializeObject(model.ValidFolders, Formatting.Indented))
    |> logInfo


    model


[<EntryPoint>]
let main argv =
    let config =
        if (argv |> Array.length) > 1
           && (argv.[1].Contains(".json")) then
            argv.[1]
        else
            deafultConfigFile
        |> parseJsonConfig

    let model = initializeGaburron config


    runGaburoon model

    0
