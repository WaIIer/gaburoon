open System
open System.Collections.Generic

open Gaburoon.Model
open Gaburoon.Setup
open Gaburoon.Azure
open Gaburoon.DataBase
open Gaburoon.GoogleDrive
open Gaburoon.Discord

let deafultConfigFile = "GaburoonConfig.json"

let initializeGaburron (config: GaburoonConfiguration) =
    let secrets = getSecrets config

    getServiceAccountJson config secrets.["gaburoonsa-connection-sring1"]

    let dbConnectionString =
        try
            initializeDatabase config
        with
        | e -> failwith $"Failed to create initialize database: {e}"

    let googleDriveService = getGoogleDriveService ()

    let (discordClient, textChannel) =
        getDiscordClient secrets.["DISCORD-TOKEN"] config

    { GoogleDriveService = googleDriveService
      DiscordClient = discordClient
      TextChannel = textChannel
      Configuration = config
      ValidFolders = Dictionary<string, string * Folder>()
      InvalidFolders = HashSet<string>()
      Secrets = secrets
      ConnectionString = config.ConnectionString }

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

    0
