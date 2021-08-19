(*
    Use Trace.moe and Anilist to get show from image
*)

module Gaburoon.Trace

open Gaburoon.Logger
open FSharp.Data

[<Literal>]
let private SampleTraceMoeResponse =
    """{"frameCount":745506,"error":"","result":[{"anilist":99939,"filename":"Nekopara - OVA (BD 1280x720 x264 AAC).mp4","episode":null,"from":97.75,"to":98.92,"similarity":0.9440424588727485,"video":"https://media.trace.moe/video/99939/Nekopara%20-%20OVA%20(BD%201280x720%20x264%20AAC).mp4?t=98.33500000000001&token=xxxxxxxxxxxxxx","image":"https://media.trace.moe/image/99939/Nekopara%20-%20OVA%20(BD%201280x720%20x264%20AAC).mp4?t=98.33500000000001&token=xxxxxxxxxxxxxx"}]}"""

[<Literal>]
let private SampleAnilistResponse =
    """{"data":{"Media":{"title":{"romaji":" ","english":" "}}}}"""

[<Literal>]
let private AnilistUrl = "https://graphql.anilist.co"

let inline private traceMoeQuery imageUrl =
    $"""https://api.trace.moe/search?url={imageUrl |> string}"""

let inline private anilistQuery showId =
    $"query {{ Media(id:{showId |> string}) {{ title {{ romaji english }} }} }}"

type private TraceMoeResponse = JsonProvider<SampleTraceMoeResponse>
type private AnilistResponse = JsonProvider<SampleAnilistResponse>

type ShowTitle = { English: string; Romaji: string }

let getShowName imageUrl =
    try
        logMsg $"Getting show from {imageUrl}"

        (imageUrl |> traceMoeQuery |> TraceMoeResponse.Load)
            .Result.[0]
            .Anilist
        |> anilistQuery
        |> fun query -> Http.RequestString(AnilistUrl, query = [ "query", query ], httpMethod = "POST")
        |> AnilistResponse.Parse
        |> fun result -> result.Data.Media.Title
        |> fun title ->
            { English = title.English |> string
              Romaji = title.Romaji |> string }
    with
    | e ->
        logError e
        { English = ""; Romaji = "" }
