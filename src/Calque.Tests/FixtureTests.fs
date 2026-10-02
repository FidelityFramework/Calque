module Calque.Tests.FixtureTests

open System
open System.IO
open NUnit.Framework
open Calque
open Calque.Core
open Calque.Syntax.Syntax

// Exact copies from clef-grammar/tests/fixtures/{lexical,measures,editing}.clef
// at f59fe12352ad2732ab5100481a6229f47ee9eef7. The editing fixture is invalid
// source used by the grammar's editor recovery tests; it must not be rewritten.
let private fixture name =
  File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))

let private format source =
  match Formatting.format source with
  | Ok code -> code
  | Error reason -> failwith reason

type private UnitShape =
  | Named of string list
  | Anonymous
  | Unity
  | Product of UnitShape * UnitShape
  | Quotient of UnitShape option * UnitShape
  | Power of UnitShape * int * int
  | Sequence of UnitShape list

type private LiteralShape =
  | Integer of int
  | RealBits of int64
  | Boolean of bool
  | Text of string
  | Measured of LiteralShape * UnitShape

let rec private exponentShape = function
  | SynRationalConst.Integer(value, _) -> value, 1
  | SynRationalConst.Rational(numerator, _, _, denominator, _, _) -> numerator, denominator
  | SynRationalConst.Negate(value, _) ->
    let numerator, denominator = exponentShape value
    -numerator, denominator
  | SynRationalConst.Paren(value, _) -> exponentShape value

let rec private unitShape = function
  | SynMeasure.Named(identifiers, _) -> Named (identifiers |> List.map _.idText)
  | SynMeasure.Anon _ -> Anonymous
  | SynMeasure.One _ -> Unity
  | SynMeasure.Product(left, _, right, _) -> Product(unitShape left, unitShape right)
  | SynMeasure.Divide(left, _, right, _) -> Quotient(Option.map unitShape left, unitShape right)
  | SynMeasure.Power(measure, _, exponent, _) ->
    let numerator, denominator = exponentShape exponent
    Power(unitShape measure, numerator, denominator)
  | SynMeasure.Seq([ measure ], _) -> unitShape measure
  | SynMeasure.Seq(measures, _) -> Sequence(List.map unitShape measures)
  | SynMeasure.Paren(measure, _) -> unitShape measure
  | other -> failwithf "Unexpected fixture measure: %A" other

let rec private literalShape = function
  | SynConst.Int32 value -> Integer value
  | SynConst.Double value -> RealBits (BitConverter.DoubleToInt64Bits value)
  | SynConst.Bool value -> Boolean value
  | SynConst.String(value, _, _) -> Text value
  | SynConst.Measure(value, _, measure, _) -> Measured(literalShape value, unitShape measure)
  | other -> failwithf "Unexpected fixture literal: %A" other

/// Compare literal values and dimensional syntax while excluding source ranges
/// and trivia. This visits the actual syntax constructors rather than CLR fields.
let private literalShapes source =
  let parsed = CodeFormatter.ParseAsync(false, source) |> Async.RunSynchronously
  Assert.That(parsed, Has.Length.EqualTo 1)
  let tree, _ = parsed[0]
  match tree with
  | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
    modules
    |> List.collect (fun (SynModuleOrNamespace(decls = declarations)) -> declarations)
    |> List.collect (function SynModuleDecl.Let(bindings = bindings) -> bindings | _ -> [])
    |> List.choose (fun (SynBinding(expr = expression)) ->
      match expression with
      | SynExpr.Const(constant = value) -> Some (literalShape value)
      | _ -> None)
  | other -> failwithf "Unexpected fixture file: %A" other

[<TestCase("lexical.clef")>]
[<TestCase("measures.clef")>]
let ``real Clef grammar fixtures format idempotently with exact literal and dimensional syntax`` name =
  let source = fixture name
  let before = literalShapes source
  Assert.That(before.Length, Is.GreaterThanOrEqualTo 7)
  let formatted = format source
  Assert.That(format formatted, Is.EqualTo formatted)
  Assert.That(literalShapes formatted, Is.EqualTo<LiteralShape list>(before))
  if name = "lexical.clef" then
    let escapedLiteral =
      source.Split '\n'
      |> Array.find (fun line -> line.StartsWith("let escaped = ", StringComparison.Ordinal))
      |> fun line -> line.Substring("let escaped = ".Length)
    for original in [
      "``distance travelled``"
      escapedLiteral
      "@\"a \"\"quote\"\" and // remain inside\""
      "// let fake = 9.81<m/s^2>"
      "(* nested comment *)"
      "\"\"\"a multiline string\nlet fake = 12.0<m>\n\"\"\""
    ] do
      Assert.That(formatted, Does.Contain original)
  else
    Assert.That(before |> List.exists (function Measured(_, Named [ "m" ]) -> true | _ -> false), Is.True)
    Assert.That(before |> List.exists (function Measured(_, Named [ "SI"; "m" ]) -> true | _ -> false), Is.True)
    Assert.That(before |> List.exists (function Measured(_, Quotient(Some(Named [ "m" ]), Power(Named [ "s" ], 2, 1))) -> true | _ -> false), Is.True)
    Assert.That(before |> List.exists (function Measured(_, Sequence [ Named [ "kg" ]; Power(Named [ "m" ], -1, 1); Power(Named [ "s" ], -2, 1) ]) -> true | _ -> false), Is.True)
    Assert.That(formatted, Does.Contain "-1.25e-3")

[<Test>]
let ``real incomplete editing source is refused by the formatter and CLI without writes`` () =
  let invalid = fixture "editing.clef"
  match Formatting.format invalid with
  | Error reason -> Assert.That(reason, Is.Not.Empty)
  | Ok code -> Assert.Fail(sprintf "editing recovery source was rewritten: %s" code)
  let directory = Path.Combine(Path.GetTempPath(), "calque-fixture-" + Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory directory |> ignore
  try
    let validPath = Path.Combine(directory, "lexical.clef")
    let invalidPath = Path.Combine(directory, "editing.clef")
    File.WriteAllText(validPath, fixture "lexical.clef")
    File.WriteAllText(invalidPath, invalid)
    let validBytes, invalidBytes = File.ReadAllBytes validPath, File.ReadAllBytes invalidPath
    use input = new StringReader("")
    use output = new StringWriter()
    use errors = new StringWriter()
    let exit = Cli.run [| validPath; invalidPath |] input output errors
    Assert.That(exit, Is.EqualTo 2)
    Assert.That(output.ToString(), Is.Empty)
    Assert.That(errors.ToString(), Does.Contain invalidPath)
    Assert.That(File.ReadAllBytes validPath, Is.EqualTo<byte array>(validBytes))
    Assert.That(File.ReadAllBytes invalidPath, Is.EqualTo<byte array>(invalidBytes))
  finally Directory.Delete(directory, true)
