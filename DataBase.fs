module Gaburoon.DataBase

open Gaburoon.Model
open Gaburoon.Logger
open Gaburoon.Setup

open System
open System.IO
open Microsoft.Data.Sqlite

/// Execute query on database where the result does not matter
let private nonQueryCommand connectionString commandText =
    use connection = new SqliteConnection(connectionString)
    let command = connection.CreateCommand()
    command.CommandText <- commandText

    try
        connection.Open()
        command.ExecuteNonQuery() |> ignore
        connection.Close()
    with
    | e ->
        logError $"SQLite command failed: {e |> string}"
        raise e

// Execute a query on the database where the result does matter
// Return a string result
let private executeScalar connectionString commandText =
    use connection = new SqliteConnection(connectionString)

    try
        connection.Open()
        let command = connection.CreateCommand()
        command.CommandText <- commandText
        let result = command.ExecuteScalar() |> string
        Some(result)
    with
    | e ->
        logError $"Failed to execute query:{System.Environment.NewLine}{commandText}"
        None

/// Get Discord message ID associated with the input imageId
let getMessageIdFromImgageId model imageId =
    logInfo $"Getting Discord message ID for {imageId}"

    let getDiscordMessageIdCommand =
        @$"
            SELECT DiscordMessageId from post_table
            where PostId = '{imageId}'
        "

    match executeScalar model.ConnectionString getDiscordMessageIdCommand with
    | Some (result) -> Some(result |> UInt64.Parse)
    | None -> None

/// Check if the database has already been created
/// Create if if that is not the case
let initializeDatabase (config: GaburoonConfiguration) =
    if not (Directory.Exists config.DBPath) then
        try
            logInfo $"Creating {config.DBPath}"

            Directory.CreateDirectory(Path.GetDirectoryName config.DBPath)
            |> ignore

            logInfo $"Successfully created {config.DBPath}"
        with
        | e -> failwith $"Failed to create {config.DBPath}: {e |> string}"

        try
            @"
                CREATE TABLE IF NOT EXISTS post_table (
                    PostId INTEGER PRIMARY KEY,
                    TIMESTAMP TEXT NOT NULL,
                    DiscordMessageId TEXT,
                    GoogleImageURL TEXT NOT NULL,
                    GoogleImageId TEXT NOT NULL,
                    FileOwners TEXT NOT NULL,
                    ImageName TEXT NOT NULL,
                    IsRemoved INTEGER NOT NULL,
                    IsAdult INTEGER NOT NULL,
                    AdultScore REAL NOT NULL,
                    IsSpoilerRequested INTEGER NOT NULL,
                    UpdatedTimeStamp TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS image_table (
                    ImageId INTEGER PRIMARY KEY,
                    TIMESTAMP TEXT NOT NULL,
                    GoogleImageURL TEXT NOT NULL,
                    AdultScore REAL NOT NULL,
                    IsAdult INTEGER NOT NULL,
                    RacyScore REAL NOT NULL,
                    IsRacy INTEGER NOT NULL,
                    GoryScore REAL NOT NULL,
                    IsGory INTEGER NOT NULL
                );
            "
            |> (nonQueryCommand config.ConnectionString)
        with
        | e -> raise e
