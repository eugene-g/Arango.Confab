# Arango.Confab

```fsharp
open Arango.Confab

let subscriber = Subscriber (fun msg ->
  printfn "%A, %A" msg.change msg.message
)

let bus = Bus ()

let publisher = Publisher ("http://127.0.0.1:8529/_db/test", bus)

bus.Subscribe(InsertUpdate, subscriber)

bus.Subscribe(Delete, subscriber)

publisher.Start
```