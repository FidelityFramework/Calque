namespace Calque.Core

open Calque.Syntax.Syntax
open SyntaxOak

/// The source-aware syntax and printing boundary for Calque.
[<AbstractClass; Sealed>]
type CodeFormatter =
  /// Format every conditional syntax branch with the original source and its trivia.
  static member FormatDocumentAsync:
    isSignature: bool * source: string * config: FormatConfig -> Async<FormatResult>

  static member internal FormatDocumentWithCheckpointAsync:
    checkpoint: (FormattingPhase -> unit) * isStopped: (exn -> bool) * isSignature: bool * source: string * config: FormatConfig -> Async<FormatResult>

  /// Parse each conditional syntax branch without invoking any semantic compiler service.
  static member ParseAsync:
    isSignature: bool * source: string -> Async<(ParsedInput * string list) array>

  /// Print a caller-authored Oak tree with the same unsupported-source boundary.
  static member FormatOakAsync: oak: Oak * config: FormatConfig -> Async<string>
