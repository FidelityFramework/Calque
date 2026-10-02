namespace Calque.Core

open Calque.Syntax.Syntax
open SyntaxOak

[<AbstractClass; Sealed>]
type CodeFormatter private () =
  static member FormatDocumentAsync(isSignature: bool, source: string, config: FormatConfig) : Async<FormatResult> =
    source
    |> CodeFormatterImpl.getSourceText
    |> fun sourceText ->
      CodeFormatterImpl.formatDocumentWith SourceDialect.ensureSupported config isSignature sourceText None

  static member ParseAsync(isSignature: bool, source: string) : Async<(ParsedInput * string list) array> =
    async {
      let sourceText = CodeFormatterImpl.getSourceText source
      let! trees = CodeFormatterImpl.parse isSignature sourceText

      for ast, _ in trees do
        ASTTransformer.mkOak (Some sourceText) ast
        |> Trivia.enrichTree FormatConfig.Default sourceText ast
        |> SourceDialect.ensureSupported

      return trees |> Array.map (fun (ast, defines) -> ast, defines.Value)
    }

  static member FormatOakAsync(oak: Oak, config: FormatConfig) : Async<string> =
    async {
      SourceDialect.ensureSupported oak
      return Context.Context.Create config |> CodePrinter.genFile oak |> Context.dump false |> _.Code
    }
