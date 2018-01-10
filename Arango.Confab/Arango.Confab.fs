module Arango.Confab

open FSharp.Data
open FSharp.Data.JsonExtensions

let loggerStatePath = "/_api/replication/logger-state"
let loggerFollowPath = "/_api/replication/logger-follow"

type URL = string

type Change =
  | InsertUpdate
  | Delete

type Message = {
  change: Change;
  cname: string;
  data: JsonValue
}

type Subscriber = {
  change: Change;
  cname: string option;
  fn: Message -> unit
}

type Bus = List<Subscriber>

// Change functions
let codeToChange (code : int) =
  match code with
  | 2300 -> Some InsertUpdate
  | 2302 -> Some Delete
  | _ -> None

// Bus functions
let addSubscriber (subscriber: Subscriber, bus: Bus) = subscriber :: bus

let transmit (msg: List<Message option>, bus: Bus): unit =
  msg
  |> List.toArray
  |> Array.iter (fun msg ->
    match msg with
    | Some msg ->
        bus
        |> Seq.iter (fun subscriber ->
          match subscriber with
          | { Subscriber.change = chg; Subscriber.cname = cname } when chg = msg.change && cname.IsNone -> subscriber.fn msg
          | { Subscriber.change = chg; Subscriber.cname = cname } when chg = msg.change && cname.Value = msg.cname -> subscriber.fn msg
          | _ -> ()
        )
        ()
    | None -> ()
  )

// Publisher functions
let parseLastLogTick(body: HttpResponseBody): string option =
  match body with
    | Text text ->
      Some ((JsonValue.Parse text)?state?lastLogTick.AsString())
    | _ ->
      None

let parseLog(body: HttpResponseBody): List<Message option> =
  match body with
  | Binary bytes ->
    (System.Text.Encoding.Default.GetString bytes).Split [|'\n'|]
    |> List.ofArray
    |> List.map (fun entry ->
      match entry with
      | "" -> None
      | _ ->
        let parsedEntry = JsonValue.Parse entry
        match parsedEntry.TryGetProperty("data") with
        | None -> None
        | Some data ->
          match parsedEntry.TryGetProperty("type") with
          | None -> None
          | Some logType ->
            match codeToChange (logType.AsInteger()) with
            | None -> None
            | Some change ->
              match parsedEntry.TryGetProperty("cname") with
              | None -> None
              | Some cname ->
                Some { change = change; cname = cname.AsString(); data = data }
    )
  | _ -> []

let rec fetchLastTickLog(backoff: int, url: URL, fetchLog: string option * int * URL * Bus -> Async<'a>, bus: Bus) = async {
  let! response = Http.AsyncRequest(url + loggerStatePath)
  if response.StatusCode <> 200
  then
    do! Async.Sleep backoff
    do! fetchLastTickLog (backoff + 1000, url, fetchLog, bus)
  else
    do! fetchLog ((parseLastLogTick response.Body), backoff, url, bus)
}

let rec fetchLog(lastLogTick: string option, backoff: int, url: URL, bus: Bus) = async {
  let! response = Http.AsyncRequest(url + loggerFollowPath, ["from", lastLogTick.Value])
  if response.StatusCode <> 200
  then
    do! Async.Sleep backoff
    do! fetchLog(lastLogTick, backoff + 1000, url, bus)
  else
    transmit ((parseLog response.Body), bus)
    do! Async.Sleep backoff
    do! fetchLastTickLog (1, url, fetchLog, bus)
}

let start (url: URL, bus: Bus) =
  fetchLastTickLog(0, url, fetchLog, bus) |> Async.Start