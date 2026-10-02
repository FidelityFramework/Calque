[<RequireQualifiedAccess>]
module internal Calque.Core.CodeFormatterImpl

open Calque.Syntax.Diagnostics
open Calque.Syntax.Syntax
open Calque.Syntax.Text
open MultipleDefineCombinations

let getSourceText (source: string) : ISourceText = source.TrimEnd() |> SourceText.ofString

// Every started conditional branch remains owned until completion. A carried
// stop never hides a sibling fault, even when the stopped branch appears first.
let private parallelJoined isStopped workflows = async {
  let! outcomes = workflows |> Seq.map Async.Catch |> Async.Parallel
  let errors = outcomes |> Array.choose (function Choice2Of2 error -> Some error | _ -> None)
  match errors |> Array.tryFind (isStopped >> not) with
  | Some error -> return raise error
  | None when errors.Length > 0 -> return raise errors[0]
  | None -> return outcomes |> Array.choose (function Choice1Of2 value -> Some value | _ -> None)
}

let parseWithCheckpoint checkpoint isStopped (isSignature: bool) (source: ISourceText) = async {
  // Parsing must remain inside the cold workflow, including the first tree.
  checkpoint ()
  let baseUntypedTree, baseDiagnostics =
    Calque.Syntax.Parse.parseFileWithCheckpoint checkpoint isSignature source []

  let hashDirectives =
    match baseUntypedTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(trivia = { ConditionalDirectives = directives }))
    | ParsedInput.SigFile(ParsedSigFileInput(trivia = { ConditionalDirectives = directives })) -> directives

  match hashDirectives with
  | [] ->
    let errors = baseDiagnostics |> List.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)
    if not errors.IsEmpty then raise (ParseException baseDiagnostics)
    return [| baseUntypedTree, DefineCombination.Empty |]
  | hashDirectives ->
    checkpoint ()
    let defineCombinations = Defines.getDefineCombination hashDirectives
    checkpoint ()
    let! results =
      defineCombinations
      |> List.map (fun defineCombination -> async {
        checkpoint ()
        let untypedTree, diagnostics =
          if defineCombination.Value.IsEmpty then baseUntypedTree, baseDiagnostics
          else Calque.Syntax.Parse.parseFileWithCheckpoint checkpoint isSignature source defineCombination.Value
        let errors = diagnostics |> List.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)
        if errors.IsEmpty then return Ok(untypedTree, defineCombination)
        else
          let defineNames =
            if defineCombination.Value.IsEmpty then "no defines"
            else defineCombination.Value |> String.concat ", "
          return Error defineNames
      })
      |> parallelJoined isStopped
    let failures = results |> Array.choose (function Error name -> Some name | _ -> None) |> Array.toList
    if not failures.IsEmpty then raise (DefineParseException failures)
    checkpoint ()
    return results |> Array.choose (function Ok result -> Some result | _ -> None)
}

let parse isSignature source = parseWithCheckpoint ignore (fun _ -> false) isSignature source

let formatASTWithCheckpoint
  (checkpoint: FormattingPhase -> unit)
  (inspectOak: SyntaxOak.Oak -> unit)
  (ast: ParsedInput)
  (sourceText: ISourceText option)
  (config: FormatConfig)
  (cursor: pos option) =
  let check phase () = checkpoint phase
  checkpoint FormattingPhase.Oak
  let oak = ASTTransformer.mkOakWithCheckpoint (check FormattingPhase.Oak) sourceText ast
  checkpoint FormattingPhase.Trivia
  let oak =
    match sourceText with
    | None -> oak
    | Some source -> Trivia.enrichTreeWithCheckpoint (check FormattingPhase.Trivia) config source ast oak
  let oak =
    match cursor with
    | None -> oak
    | Some cursor -> Trivia.insertCursor oak cursor
  checkpoint FormattingPhase.Dialect
  inspectOak oak
  checkpoint FormattingPhase.Print
  let context = { Context.Context.Create config with Checkpoint = check FormattingPhase.Print }
  let result = context |> CodePrinter.genFile oak |> Context.dump false
  checkpoint FormattingPhase.Print
  result

let formatASTWith inspectOak ast sourceText config cursor =
  formatASTWithCheckpoint ignore inspectOak ast sourceText config cursor

let formatAST ast sourceText config cursor = formatASTWith ignore ast sourceText config cursor

let formatDocumentWithCheckpoint
  (checkpoint: FormattingPhase -> unit)
  (isStopped: exn -> bool)
  (inspectOak: SyntaxOak.Oak -> unit)
  (config: FormatConfig)
  (isSignature: bool)
  (source: ISourceText)
  (cursor: pos option) = async {
  let! asts = parseWithCheckpoint (fun () -> checkpoint FormattingPhase.Parse) isStopped isSignature source
  let! results =
    asts
    |> Array.map (fun (ast, defines) -> async {
      let result = formatASTWithCheckpoint checkpoint inspectOak ast (Some source) config cursor
      return defines, result
    })
    |> parallelJoined isStopped
  checkpoint FormattingPhase.Merge
  let merged =
    match Array.toList results with
    | [] -> failwith "not possible"
    | [ _, result ] -> result
    | all -> mergeMultipleFormatResultsWithCheckpoint (fun () -> checkpoint FormattingPhase.Merge) config all
  checkpoint FormattingPhase.Merge
  return merged
}

let formatDocumentWith inspectOak config isSignature source cursor =
  formatDocumentWithCheckpoint ignore (fun _ -> false) inspectOak config isSignature source cursor

let formatDocument config isSignature source cursor =
  formatDocumentWith ignore config isSignature source cursor
