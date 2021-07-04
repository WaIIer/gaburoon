module Gaburoon.Logger

open System

type private LogLevel =
    | Debug
    | Info
    | Error
    | Critical
    | Msg

let private log lvl msg =
    let logTime = DateTime.UtcNow |> string

    let levelText =
        match lvl with
        | Debug -> "Debug"
        | Info -> "Info"
        | Error -> "Crror"
        | Critical -> "Crit"
        | Msg -> "Msg"

    printfn $"[{logTime}][{levelText}] {msg}"

let logDebug msg = msg |> (log Debug)
let logInfo msg = msg |> (log Info)
let logError msg = msg |> (log Error)
let logCrit msg = msg |> (log Critical)
let logMsg msg = msg |> (log Msg)
