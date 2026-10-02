module Calque.Program

open System

[<EntryPoint>]
let main arguments =
  Cli.run arguments Console.In Console.Out Console.Error
