module Gaburoon.DataBase

open Gaburoon.Model
open Gaburoon.Logger
open Gaburoon.Util

open System
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models

let mutable private _connectionString = ""

type private DbEntry =
    { PostId: int64
      TIMESTAMP: DateTime
      DiscordMessageId: string option
      GoogleImageUrl: string
      GoogleImageId: string
      FileOwners: string
      ImageName: string
      IsRemoved: int64
      IsAdult: int64
      AdultScore: float
      IsSpoilerRequested: int64
      UpdatedTimeStamp: DateTime }

/// Execute query on database where the result does not matter
let private nonQueryCommand commandText =
    use connection = new SqliteConnection(_connectionString)
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
let private executeScalar commandText =
    use connection = new SqliteConnection(_connectionString)

    try
        connection.Open()
        let command = connection.CreateCommand()
        command.CommandText <- commandText
        let result = command.ExecuteScalar() |> string
        result
    with
    | e ->
        logError $"Failed to execute query:{System.Environment.NewLine}{commandText}"
        raise e

/// Get Discord message ID associated with the input imageId
let getMessageIdFromImgageId model imageId =
    logInfo $"Getting Discord message ID for {imageId}"

    let getDiscordMessageIdCommand =
        @$"
            SELECT DiscordMessageId from post_table
            where PostId = '{imageId}'
        "

    try
        executeScalar getDiscordMessageIdCommand
        |> UInt64.Parse
    with
    | e -> raise e

/// Check if the database has already been created
/// Create if if that is not the case
let initializeDatabase (config: GaburoonConfiguration) =
    // Set _connectionString to be used by all other functions
    _connectionString <- config.ConnectionString

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
                    GoogleImageId TEXT,
                    GoogleImageURL TEXT NOT NULL,
                    AdultScore REAL NOT NULL,
                    IsAdult INTEGER NOT NULL,
                    RacyScore REAL NOT NULL,
                    IsRacy INTEGER NOT NULL,
                    GoryScore REAL NOT NULL,
                    IsGory INTEGER NOT NULL
                );
            "
            |> nonQueryCommand
        with
        | e -> raise e

/// Add row to database, called when a new image is uploaded
/// Return -1 if there is an error adding the row to the database
let insertRow (downloadFile: DownloadFile) (adultInfo: AdultInfo) =

    let googleFile = downloadFile.GoogleFile
    let googleImageUrl = googleFile.WebViewLink
    let googleImageId = googleFile.Id
    let imageName = googleFile.OriginalFilename

    let fileOwners =
        googleFile.Owners
        |> Seq.map (fun owner -> owner.DisplayName)
        |> String.concat "; "

    let timeStamp = DateTime.UtcNow |> string

    let isAdult = adultInfo.IsAdultContent |> btoi

    // This command inserts a row in the table
    // and returns the rowId of the newly created row
    let insertCommand =
        $@"
        INSERT INTO post_table
            (
            TIMESTAMP, GoogleImageUrl,
            GoogleImageId, FileOwners, ImageName, IsRemoved,
            IsAdult, AdultScore, IsSpoilerRequested, UpdatedTimeStamp
            )
        VALUES
            (
                '{timeStamp}', '{googleImageUrl}',
                '{googleImageId |> string}', '{fileOwners}', '{imageName}', 0,
                {isAdult}, {adultInfo.AdultScore}, 0, '{timeStamp}'
            );
            SELECT last_insert_rowid();;
        "

    try
        let lastRowId = executeScalar insertCommand
        logInfo $"Inserted row {lastRowId}"
        lastRowId |> Int64.Parse
    with
    | e ->
        logError $"Failed to insert row in database: {e}"
        -1L

/// Create row in image_table which stores more information on the image
let uploadImageInformation (downloadFile: DownloadFile) (adultInfo: AdultInfo) =
    let insertCommand =
        @$"
        INSERT INTO image_table (
            TIMESTAMP, GoogleImageId, GoogleImageURL, AdultScore, IsAdult, RacyScore,
            IsRacy, GoryScore, IsGory
        )
        VALUES (
            '{DateTime.UtcNow |> string}', '{downloadFile.GoogleFile.Id}', '{downloadFile.GoogleFile.WebViewLink}', {adultInfo.AdultScore}, {adultInfo.IsAdultContent |> btoi},
            {adultInfo.RacyScore}, {adultInfo.IsRacyContent |> btoi}, {adultInfo.GoreScore}, {adultInfo.IsGoryContent |> btoi}
        )
        "

    try
        let lastRowId = executeScalar insertCommand
        logInfo $"Inserted row {lastRowId}"
        lastRowId |> Int64.Parse
    with
    | e ->
        logError $"Failed to insert row in database: {e}"
        -1L
