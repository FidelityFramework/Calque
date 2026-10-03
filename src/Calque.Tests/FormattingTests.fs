module Calque.Tests.FormattingTests

open System
open NUnit.Framework
open Calque
open Calque.Core
open Calque.Syntax.Syntax

let private formatted source =
  match Formatting.format source with
  | Ok code -> code
  | Error reason -> failwith reason

[<Test>]
let ``formatting is idempotent on supported source with nested bindings`` () =
  let source = "let calculate value=\n    let adjusted=value+1\n    adjusted*2\n"
  let first = formatted source
  Assert.That(first, Is.Not.EqualTo source)
  Assert.That(formatted first, Is.EqualTo first)
  Assert.That(first, Does.Contain "\n  let adjusted = value + 1\n")
  Assert.That(first, Does.Not.Contain "\n    let adjusted")

[<Test>]
let ``ordered duplicate comments and literal spelling survive formatting`` () =
  let source = """// repeated
module Comments
// repeated
let first=1 // first tail
(* between bindings *)
// repeated
let second="// repeated inside a string"
// final tail
"""
  let code = formatted source
  let markers = [
    "// repeated"
    "// repeated"
    "// first tail"
    "(* between bindings *)"
    "// repeated"
    "\"// repeated inside a string\""
    "// final tail"
  ]
  let mutable next = 0
  for marker in markers do
    let position = code.IndexOf(marker, next, StringComparison.Ordinal)
    Assert.That(position, Is.GreaterThanOrEqualTo next, sprintf "lost or reordered %s" marker)
    next <- position + marker.Length
  let count (token: string) (text: string) =
    text.Split([| token |], StringSplitOptions.None).Length - 1
  Assert.That(count "// repeated" code, Is.EqualTo (count "// repeated" source))
  Assert.That(formatted code, Is.EqualTo code)

[<Test>]
let ``invalid source is refused instead of emitting a recovered tree`` () =
  match Formatting.format "let broken =\n" with
  | Error reason -> Assert.That(reason, Is.Not.Empty)
  | Ok code -> Assert.Fail(sprintf "invalid source was rewritten: %s" code)

[<TestCase("let value = eager 42\n")>]
[<TestCase("let line = __LINE__\n")>]
[<TestCase("#line 100 \"file.clef\"\nlet value = 42\n")>]
[<TestCase("#100 \"file.clef\"\nlet value = 42\n")>]
[<TestCase("# 100 \"file.clef\"\nlet value = 42\n")>]
[<TestCase("let large = 9223372036854775808\n")>]
[<TestCase("#if FEATURE\nlet value = eager 42\n#else\nlet value = 42\n#endif\n")>]
let ``dialect and source location sensitive syntax is refused`` source =
  match Formatting.format source with
  | Error reason -> Assert.That(reason, Is.Not.Empty)
  | Ok code -> Assert.Fail(sprintf "unsupported source was rewritten: %s" code)

[<TestCase("let value = 42 :> obj\n")>]
[<TestCase("let value = null\n")>]
[<TestCase("let values: obj[] = [||]\n")>]
[<TestCase("let choose value = match value with null -> 0 | _ -> 1\n")>]
[<TestCase("let choose value = match value with :? string -> 0 | _ -> 1\n")>]
[<TestCase("let identity (value: #System.IDisposable) = value\n")>]
[<TestCase("let f<'T when 'T : delegate<unit, unit>> () = ()\n")>]
[<TestCase("type Holder(value: int) =\n  member _.Value = value\n")>]
[<TestCase("let value = { new System.IDisposable with member _.Dispose() = () }\n")>]
let ``CLR casts null and object oriented syntax are refused`` source =
  match Formatting.format source with
  | Error reason -> Assert.That(reason, Does.Contain "not permitted in Clef source")
  | Ok code -> Assert.Fail(sprintf "forbidden source was rewritten: %s" code)

[<TestCase("let run () = task { return 42 }\n")>]
[<TestCase("let idle () = task { }\n")>]
[<TestCase("let run next = task {\n  let! value = next ()\n  return value + 1\n}\n")>]
[<TestCase("let declaration = <@ task { return 42 } @>\n")>]
let ``dotnet task computation expressions are refused`` source =
  match Formatting.format source with
  | Error reason -> Assert.That(reason, Does.Contain "'task computation expression' is not permitted in Clef source")
  | Ok code -> Assert.Fail(sprintf "task source was rewritten: %s" code)

[<TestCase("let task = 42\nlet next = task + 1\n")>]
[<TestCase("type Job = { task: int }\nlet job = { task = 1 }\n")>]
let ``a binding or field named task remains ordinary source`` source =
  match Formatting.format source with
  | Ok code -> Assert.That(code, Is.EqualTo source)
  | Error reason -> Assert.Fail(sprintf "ordinary task name was refused: %s" reason)

[<Test>]
let ``inconclusive conditional coverage refuses valid source instead of dropping a branch`` () =
  let source = """module ConditionalCoverage
#if (A1 || B1) && (!A1 || !B1) && (A2 || B2) && (!A2 || !B2) && (A3 || B3) && (!A3 || !B3) && (A4 || B4) && (!A4 || !B4) && (A5 || B5) && (!A5 || !B5)
let hidden = 17
#endif
let ordinary = 42
"""
  match Formatting.format source with
  | Error reason -> Assert.That(reason, Does.Contain "branch coverage is inconclusive")
  | Ok code -> Assert.Fail(sprintf "conditional source was silently omitted or accepted: %s" code)

[<Test>]
let ``mutable bindings loops and record updates remain supported imperative source`` () =
  let source = """type Counter = { Total: int; Label: string }
let accumulate limit =
  let mutable sum=0
  for value in 1..limit do
    sum <- sum+value
  let mutable remaining=limit
  while remaining>0 do
    remaining <- remaining-1
  let original={Total=sum;Label="count"}
  {original with Total=original.Total+remaining}
"""
  let code = formatted source
  Assert.That(code, Does.Contain "let mutable sum = 0")
  Assert.That(code, Does.Contain "for value in")
  Assert.That(code, Does.Contain "sum <- sum + value")
  Assert.That(code, Does.Contain "while remaining > 0 do")
  Assert.That(code, Does.Contain "original with")
  Assert.That(formatted code, Is.EqualTo code)

[<Test>]
let ``CLR syntax words in trivia literals and quoted names leave functional calls available`` () =
  let source = "// :> obj null class member\n(* outer (* null :> obj *) member *)\nlet ``null``=42\nlet ``obj``=``null``\nlet text=\":> obj null class member\"\nlet printed=Console.writeln text\n"
  let code = formatted source
  Assert.That(code, Does.Contain "// :> obj null class member")
  Assert.That(code, Does.Contain "(* outer (* null :> obj *) member *)")
  Assert.That(code, Does.Contain "\":> obj null class member\"")
  Assert.That(code, Does.Contain "``null``")
  Assert.That(code, Does.Contain "``obj``")
  Assert.That(code, Does.Contain "Console.writeln text")
  Assert.That(formatted code, Is.EqualTo code)

[<Test>]
let ``dialect words in comments strings and quoted identifiers remain ordinary source`` () =
  let source = """// eager __LINE__ #line
let ``eager`` value=value
(* eager __LINE__ #line *)
let text="eager __LINE__ #line"
let result=``eager`` text
"""
  let code = formatted source
  Assert.That(code, Does.Contain "// eager __LINE__ #line")
  Assert.That(code, Does.Contain "(* eager __LINE__ #line *)")
  Assert.That(code, Does.Contain "\"eager __LINE__ #line\"")
  Assert.That(code, Does.Contain "``eager``")
  Assert.That(formatted code, Is.EqualTo code)

type private Arithmetic =
  | Number of int
  | Symbol of string
  | Apply of Arithmetic * Arithmetic

let private arithmeticShape source =
  let rec expression = function
    | SynExpr.Const(constant = SynConst.Int32 value) -> Number value
    | SynExpr.Ident identifier -> Symbol identifier.idText
    | SynExpr.LongIdent(longDotId = SynLongIdent(identifiers, _, _)) ->
      Symbol (identifiers |> List.map _.idText |> String.concat ".")
    | SynExpr.App(funcExpr = functionValue; argExpr = argument) ->
      Apply(expression functionValue, expression argument)
    | SynExpr.Paren(expr = inner) -> expression inner
    | other -> failwithf "Unexpected arithmetic fixture expression: %A" other
  let parsed = CodeFormatter.ParseAsync(false, source) |> Async.RunSynchronously
  Assert.That(parsed, Has.Length.EqualTo 1)
  let tree, _ = parsed[0]
  match tree with
  | ParsedInput.ImplFile(ParsedImplFileInput(contents = [ SynModuleOrNamespace(decls = [ SynModuleDecl.Let(bindings = [ SynBinding(expr = value) ]) ]) ])) ->
    expression value
  | other -> failwithf "Unexpected arithmetic fixture file: %A" other

[<Test>]
let ``formatting preserves the parsed operator tree and precedence`` () =
  let source = "let calculation=1+2*3\n"
  let before = arithmeticShape source
  match before with
  | Apply(Apply(Symbol addition, Number 1), Apply(Apply(Symbol multiplication, Number 2), Number 3)) ->
    Assert.That(addition, Is.Not.EqualTo multiplication)
  | other -> Assert.Fail(sprintf "fixture lost multiplication precedence: %A" other)
  Assert.That(arithmeticShape (formatted source), Is.EqualTo before)
