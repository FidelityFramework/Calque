module Calque.Syntax.Parse

open Calque.Syntax.Diagnostics
open Calque.Syntax.Syntax
open Calque.Syntax.Text

type FSharpParserDiagnostic =
    {
        Severity: FSharpDiagnosticSeverity
        SubCategory: string
        Range: range option
        ErrorNumber: int option
        Message: string
    }

val parseFile:
    isSignature: bool -> sourceText: ISourceText -> defines: string list -> ParsedInput * FSharpParserDiagnostic list

/// Check before every parser token. Callback failures remain control/host failures,
/// never source diagnostics.
val internal parseFileWithCheckpoint:
    checkpoint: (unit -> unit) -> isSignature: bool -> sourceText: ISourceText ->
        defines: string list -> ParsedInput * FSharpParserDiagnostic list
