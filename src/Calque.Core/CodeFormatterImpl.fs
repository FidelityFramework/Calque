[<RequireQualifiedAccess>]
module internal Calque.Core.CodeFormatterImpl

open Calque.Syntax.Diagnostics
open Calque.Syntax.Syntax
open Calque.Syntax.Text
open MultipleDefineCombinations

let getSourceText (source: string) : ISourceText = source.TrimEnd() |> SourceText.ofString

// An owned format operation must finish every branch before reporting a refusal.
// Capturing each failure prevents Async.Parallel from returning on the first
// exception while another synchronous parser/printer still owns work. Hosted
// callers use explicit stop requests with ambient CancellationToken.None.
let private parallelJoined workflows =
  async {
    let! outcomes = workflows |> Seq.map Async.Catch |> Async.Parallel

    return
      outcomes
      |> Array.map (function
        | Choice1Of2 value -> value
        | Choice2Of2 error -> raise error)
  }

let parse (isSignature: bool) (source: ISourceText) : Async<(ParsedInput * DefineCombination) array> =
    // First get the syntax tree without any defines
    let baseUntypedTree, baseDiagnostics =
        Calque.Syntax.Parse.parseFile isSignature source []

    let hashDirectives =
        match baseUntypedTree with
        | ParsedInput.ImplFile(ParsedImplFileInput(trivia = { ConditionalDirectives = directives }))
        | ParsedInput.SigFile(ParsedSigFileInput(trivia = { ConditionalDirectives = directives })) -> directives

    match hashDirectives with
    | [] ->
        async {
            let errors =
                baseDiagnostics
                |> List.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)

            if not errors.IsEmpty then
                raise (ParseException baseDiagnostics)

            return [| (baseUntypedTree, DefineCombination.Empty) |]
        }
    | hashDirectives ->
        let defineCombinations = Defines.getDefineCombination hashDirectives

        async {
            let! results =
                defineCombinations
                |> List.map (fun defineCombination ->
                    async {
                        // The combination without defines was already parsed to find the directives.
                        let untypedTree, diagnostics =
                            if defineCombination.Value.IsEmpty then
                                baseUntypedTree, baseDiagnostics
                            else
                                Calque.Syntax.Parse.parseFile isSignature source defineCombination.Value

                        let errors =
                            diagnostics
                            |> List.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)

                        if errors.IsEmpty then
                            return Ok(untypedTree, defineCombination)
                        else

                        let defineNames =
                            if defineCombination.Value.IsEmpty then
                                "no defines"
                            else
                                defineCombination.Value |> String.concat ", "

                        return Error defineNames
                    }
                )
                |> parallelJoined

            let failures =
                results
                |> Array.choose (
                    function
                    | Error name -> Some name
                    | _ -> None
                )
                |> Array.toList

            if not failures.IsEmpty then
                raise (DefineParseException(failures))

            return
                results
                |> Array.choose (
                    function
                    | Ok result -> Some result
                    | _ -> None
                )
        }

let formatASTWith
    (inspectOak: SyntaxOak.Oak -> unit)
    (ast: ParsedInput)
    (sourceText: ISourceText option)
    (config: FormatConfig)
    (cursor: pos option)
    : FormatResult
    =
    let context = Context.Context.Create config

    let oak =
        match sourceText with
        | None -> ASTTransformer.mkOak None ast
        | Some sourceText ->

        ASTTransformer.mkOak (Some sourceText) ast
        |> Trivia.enrichTree config sourceText ast

    let oak =
        match cursor with
        | None -> oak
        | Some cursor -> Trivia.insertCursor oak cursor

    inspectOak oak
    context |> CodePrinter.genFile oak |> Context.dump false

let formatAST
    (ast: ParsedInput)
    (sourceText: ISourceText option)
    (config: FormatConfig)
    (cursor: pos option)
    : FormatResult
    =
    formatASTWith ignore ast sourceText config cursor

let formatDocumentWith
    (inspectOak: SyntaxOak.Oak -> unit)
    (config: FormatConfig)
    (isSignature: bool)
    (source: ISourceText)
    (cursor: pos option)
    : Async<FormatResult>
    =
    async {
        let! asts = parse isSignature source

        let! results =
            asts
            |> Array.map (fun (ast', defineCombination) ->
                async {
                    let formattedCode: FormatResult =
                        formatASTWith inspectOak ast' (Some source) config cursor

                    return (defineCombination, formattedCode)
                }
            )
            |> parallelJoined
            |> Async.map Array.toList

        let merged =
            match results with
            | [] -> failwith "not possible"
            | [ (_, x) ] -> x
            | all -> mergeMultipleFormatResults config all

        return merged
    }

let formatDocument
    (config: FormatConfig)
    (isSignature: bool)
    (source: ISourceText)
    (cursor: pos option)
    : Async<FormatResult>
    =
    formatDocumentWith ignore config isSignature source cursor
