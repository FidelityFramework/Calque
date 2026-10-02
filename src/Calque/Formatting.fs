namespace Calque

open Calque.Core

module Formatting =
  let private config =
    { FormatConfig.Default with
        IndentSize = 2
        EndOfLine = EndOfLineStyle.LF }

  /// Supply the original source so the retained formatter can place trivia.
  /// Unsupported dialect syntax is refused by the core before formatting.
  let format (source: string) : Result<string, string> =
    try
      let result =
        CodeFormatter.FormatDocumentAsync(false, source, config)
        |> Async.RunSynchronously
      Ok result.Code
    with error -> Error error.Message
