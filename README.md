# Arango.Confab

Arango.Confab is a library that polls an ArangoDB instance and publishes changes to registered subscribers.

```fsharp
open Arango.Confab

let printMessage(msg : Message): unit =
  printfn "change: %A\ncname: %A\nmessage: %A" msg.change msg.cname msg.data

// Create subscribers
let insertSubscriber = {
  change = InsertUpdate;
  cname = Some "test";
  fn = printMessage
}
let deleteSubscriber = {
  change = Delete;
  cname = None;
  fn = printMessage
}

// Create a bus
let bus = [insertSubscriber; deleteSubscriber]

// Start the poller
start("http://127.0.0.1:8529/_db/test", bus)
```

Subscribers can listen for InsertUpdate or Delete events. They can listen on one collection (cname) or listen on all with None.

A bus is list a List<Subscriber>.

The URL passed to `start()` should include the database name.

Inside of the subscriber function you'll have access to the entire Message record:

```fsharp
type Message = {
  change: Change;
  cname: string;
  message: JsonValue
}
```

JsonValue can be further parsed with [FSharp.Data.JsonExtensions](https://fsharp.github.io/FSharp.Data/reference/fsharp-data-jsonextensions.html).