module Gaburoon.Logger

open System
open System.IO
open Model

type private LogLevel =
    | Debug
    | Info
    | Error
    | Critical
    | Msg

let mutable private logFile = ""

let private log lvl msg =
    let logTime = DateTime.UtcNow |> string

    let levelText =
        match lvl with
        | Debug -> "Debug"
        | Info -> "Info"
        | Error -> "Crror"
        | Critical -> "Crit"
        | Msg -> "Msg"

    let msg = $"[{logTime}][{levelText}] {msg}"

    try
        File.AppendAllText(logFile, msg + Environment.NewLine)
    with
    | e -> printfn $"Failed to write to {logFile}: {e}"
#if DEBUG
    printfn $"{msg}"
#endif

let logDebug msg = msg |> (log Debug)
let logInfo msg = msg |> (log Info)
let logError msg = msg |> (log Error)
let logCrit msg = msg |> (log Critical)
let logMsg msg = msg |> (log Msg)

/// Create log directory if it does not already exist
/// Set logFile to $pwd/logs/guild.text-channel.log
/// Fail on any exceptions here
let initializeLogger (config: GaburoonConfiguration) =
    let logDir =
        (Directory.GetCurrentDirectory(), "logs")
        |> Path.Join

    if not (Directory.Exists logDir) then
        Directory.CreateDirectory logDir |> ignore

    logFile <-
        (logDir, $"{config.DiscordGuild}.{config.TextChannel}.log")
        |> Path.Join
