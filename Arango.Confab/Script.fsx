#if INTERACTIVE
#r "../packages/FSharp.Data/lib/net45/FSharp.Data.dll"
#else
#endif
#load "Arango.Confab.fs"
open Arango.Confab

let printMessage(msg : Message): unit =
  printfn "change: %A\ncname: %A\nmessage: %A" msg.change msg.cname msg.data

let insertSubscriber = {
  change = InsertUpdate;
  cname = Some "terminals";
  fn = printMessage
}

let deleteSubscriber = {
  change = Delete;
  cname = None;
  fn = printMessage
}

let bus = [insertSubscriber; deleteSubscriber]

start("http://127.0.0.1:8529/_db/test", bus)