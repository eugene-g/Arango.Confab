module Arango.Confab

open FSharp.Data
open FSharp.Data.JsonExtensions

let loggerStatePath = "/_api/replication/logger-state"
let loggerFollowPath = "/_api/replication/logger-follow"

type URL = string

type Change =
  | InsertUpdate
  | Delete

let codeToChange (code : int) =
  match code with
  | 2300 -> Some InsertUpdate
  | 2302 -> Some Delete
  | _ -> None

type Message = {
    change: Change;
    message: JsonValue
}

type Subscriber (fn) =
  member this.ProcessMessage (message:Message) =
    fn message

type Bus () =
  let mutable subscribers = []
  member this.Subscribe (change:Change, subscriber:Subscriber) =
    subscribers <- (change, subscriber) :: subscribers
  // TODO unsubscribe
  member this.Unsubscribe (change:Change, subscriber:Subscriber) =
    subscribers <- []
  member this.Publish (message:Message) =
    subscribers
    |> List.toArray
    |> Array.iter (fun subscription ->
      let change, subscriber = subscription
      if message.change = change then subscriber.ProcessMessage(message)
      ()
    )

type Publisher (url: URL, bus: Bus) =
  let mutable lastLogTick = None
  let mutable backoff = 1
  member this.FetchLastTickLog = async {
      let! response = Http.AsyncRequest(url + loggerStatePath)
      if response.StatusCode <> 200
      then
        backoff <- if backoff < 1000 then backoff * 10 else backoff
        do! Async.Sleep backoff
        do! this.FetchLastTickLog
      else
        lastLogTick <- this.ParseLastLogTick response.Body
        do! this.FetchLog
      () }
  member this.FetchLog = async {
      if lastLogTick.IsSome then
          let! response = Http.AsyncRequest(url + loggerFollowPath, ["from", lastLogTick.Value])
          if response.StatusCode <> 200
          then
            backoff <- if backoff < 1000 then backoff * 10 else backoff
            do! Async.Sleep backoff
            do! this.FetchLog
          else
            backoff <- 1
            this.ParseLog response.Body
            do! Async.Sleep backoff
            do! this.FetchLastTickLog
      () }
  member this.ParseLastLogTick (body:HttpResponseBody) =
    match body with
    | Text text ->
      Some ((JsonValue.Parse text)?state?lastLogTick.AsString())
    | _ ->
      None

  member this.ParseLog (body:HttpResponseBody) =
    match body with
    | Binary bytes ->
      (System.Text.Encoding.Default.GetString bytes).Split [|'\n'|]
      |> Array.iter (fun entry ->
        if entry = "" then () else
          let parsedEntry = JsonValue.Parse entry
          if parsedEntry.TryGetProperty("data").IsSome then
            let logType = parsedEntry.TryGetProperty("type")
            if logType.IsSome then
              let change = codeToChange (logType.Value.AsInteger())
              if change.IsSome then
                this.Publish({ change = change.Value; message = parsedEntry?data })
        ()
      )
    | _ ->
      System.Console.WriteLine("Unexpected return value type.")

  member this.Start =
    this.FetchLastTickLog |> Async.Start
  member this.Publish(message:Message) =
    bus.Publish(message)