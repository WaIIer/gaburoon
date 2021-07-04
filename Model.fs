module Gaburoon.Model

open Discord.WebSocket
open System.Collections
open Google.Apis.Drive.v3
open System.Collections.Generic



type ContentType =
    | NSFW
    | SFW

type Folder =
    { Name: string
      ContentType: ContentType
      mutable GoogleId: string option }

type GaburoonConfiguration =
    { Folders: IDictionary<string, Folder>
      KeyVaultName: string
      StorageAccount: string
      BlobContainer: string
      DiscordGuild: string
      TextChannel: string
      ComputerVisionResource: string
      DBPath: string
      ConnectionString: string }

type GaburoonModel =
    { DiscordClient: DiscordSocketClient
      TextChannel: SocketTextChannel
      Secrets: IDictionary<string, string>
      GoogleDriveService: DriveService
      Configuration: GaburoonConfiguration
      ValidFolders: Dictionary<string, string * Folder>
      InvalidFolders: HashSet<string>
      ConnectionString: string }

type DownloadFile =
    { Path: string
      GoogleFile: Google.Apis.Drive.v3.Data.File }
