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
