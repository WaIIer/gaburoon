module Gaburoon.Setup

open Newtonsoft.Json
open System.IO

open Gaburoon.Model

type JsonConfiguration =
    { SFWFolders: string list
      NSFWFolders: string list
      KeyVault: string
      StorageAccount: string
      BlobContainer: string
      DiscordGuild: string
      TextChannel: string
      ComputerVisionResource: string }

let private parseFolderList contentType folders =
    folders
    |> List.map
        (fun folderName ->
            { Name = folderName
              ContentType = contentType
              GoogleId = None })

let private dbPath (guild: string) (textChannel: string) =
    Path.Combine [| Directory.GetCurrentDirectory()
                    "database"
                    $"""{guild.Replace(" ", "-")}.{textChannel.Replace(" ", "-")}""" |]

let private dbConnectionString dbPath = dbPath |> (sprintf "Data Source=%s")

let parseJsonConfig (configFile) : GaburoonConfiguration =
    let jsonConfig =
        try
            File.ReadAllText configFile
            |> JsonConvert.DeserializeObject<JsonConfiguration>
        with
        | e -> failwith (sprintf "Invalid config %s" (e |> string))

    let dbPath =
        dbPath jsonConfig.DiscordGuild jsonConfig.TextChannel

    { Folders =
          (jsonConfig.SFWFolders |> parseFolderList SFW)
          @ (jsonConfig.NSFWFolders |> parseFolderList NSFW)
      KeyVaultName = jsonConfig.KeyVault
      StorageAccount = jsonConfig.StorageAccount
      BlobContainer = jsonConfig.BlobContainer
      DiscordGuild = jsonConfig.DiscordGuild
      TextChannel = jsonConfig.TextChannel
      ComputerVisionResource = jsonConfig.ComputerVisionResource
      DBPath = dbPath
      ConnectionString = dbConnectionString dbPath }
