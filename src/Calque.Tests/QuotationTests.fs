module Calque.Tests.QuotationTests

open System
open NUnit.Framework
open Calque
open Calque.Core
open Calque.Syntax.Syntax

// Clef expressions.md:2827-2853 specifies typed/raw quotations and rejects
// splices. Numeric Selection section 4 uses native dimensioned quoted laws.
// These fixtures exercise source preservation, not quotation evaluation.
type private TypeShape =
  | TypeName of string list
  | TypeApplication of TypeShape * TypeShape list

type private MeasureShape =
  | MeasureName of string list
  | Product of MeasureShape * MeasureShape
  | Quotient of MeasureShape option * MeasureShape
  | Power of MeasureShape * int * int
  | Measures of MeasureShape list

type private LiteralShape =
  | Integer of int
  | RealBits of int64
  | Text of string
  | Measured of LiteralShape * MeasureShape

type private PatternShape =
  | Parameter of string
  | AnnotatedParameter of PatternShape * TypeShape

type private ExpressionShape =
  | Literal of LiteralShape
  | Symbol of string list
  | Apply of ExpressionShape * ExpressionShape
  | Lambda of PatternShape list * ExpressionShape
  | Tuple of ExpressionShape list
  | Quote of isRaw: bool * body: ExpressionShape

let rec private typeShape = function
  | SynType.LongIdent(SynLongIdent(identifiers, _, _)) ->
    TypeName (identifiers |> List.map _.idText)
  | SynType.App(typeName = name; typeArgs = arguments) ->
    TypeApplication(typeShape name, List.map typeShape arguments)
  | SynType.Paren(innerType = inner) -> typeShape inner
  | other -> failwithf "Unexpected quotation fixture type: %A" other

let rec private exponentShape = function
  | SynRationalConst.Integer(value, _) -> value, 1
  | SynRationalConst.Rational(numerator, _, _, denominator, _, _) -> numerator, denominator
  | SynRationalConst.Negate(value, _) ->
    let numerator, denominator = exponentShape value
    -numerator, denominator
  | SynRationalConst.Paren(value, _) -> exponentShape value

let rec private measureShape = function
  | SynMeasure.Named(identifiers, _) -> MeasureName (identifiers |> List.map _.idText)
  | SynMeasure.Product(left, _, right, _) -> Product(measureShape left, measureShape right)
  | SynMeasure.Divide(left, _, right, _) -> Quotient(Option.map measureShape left, measureShape right)
  | SynMeasure.Power(measure, _, exponent, _) ->
    let numerator, denominator = exponentShape exponent
    Power(measureShape measure, numerator, denominator)
  | SynMeasure.Seq([ measure ], _) -> measureShape measure
  | SynMeasure.Seq(measures, _) -> Measures(List.map measureShape measures)
  | SynMeasure.Paren(measure, _) -> measureShape measure
  | other -> failwithf "Unexpected quotation fixture measure: %A" other

let rec private literalShape = function
  | SynConst.Int32 value -> Integer value
  | SynConst.Double value -> RealBits (BitConverter.DoubleToInt64Bits value)
  | SynConst.String(value, _, _) -> Text value
  | SynConst.Measure(value, _, measure, _) -> Measured(literalShape value, measureShape measure)
  | other -> failwithf "Unexpected quotation fixture literal: %A" other

let rec private patternShape = function
  | SynPat.Named(ident = SynIdent(identifier, _)) -> Parameter identifier.idText
  | SynPat.Typed(pat = pattern; targetType = annotation) ->
    AnnotatedParameter(patternShape pattern, typeShape annotation)
  | SynPat.Paren(pat = inner) -> patternShape inner
  | other -> failwithf "Unexpected quotation fixture pattern: %A" other

let rec private expressionShape = function
  | SynExpr.Quote(isRaw = isRaw; quotedExpr = body) -> Quote(isRaw, expressionShape body)
  | SynExpr.Const(constant = constant) -> Literal (literalShape constant)
  | SynExpr.Ident identifier -> Symbol [ identifier.idText ]
  | SynExpr.LongIdent(longDotId = SynLongIdent(identifiers, _, _)) ->
    Symbol (identifiers |> List.map _.idText)
  | SynExpr.App(funcExpr = functionValue; argExpr = argument) ->
    Apply(expressionShape functionValue, expressionShape argument)
  | SynExpr.Lambda(parsedData = Some(parameters, body)) ->
    Lambda(List.map patternShape parameters, expressionShape body)
  | SynExpr.Tuple(exprs = expressions) -> Tuple (List.map expressionShape expressions)
  | SynExpr.Paren(expr = inner) -> expressionShape inner
  | other -> failwithf "Unexpected quotation fixture expression: %A" other

// Match syntax constructors directly, omitting only ranges, trivia and parentheses.
// A new unexpected form fails the fixture instead of silently disappearing.
let private quotationShapes source =
  let parsed = CodeFormatter.ParseAsync(false, source) |> Async.RunSynchronously
  Assert.That(parsed, Has.Length.EqualTo 1)
  let tree, _ = parsed[0]
  match tree with
  | ParsedInput.ImplFile(ParsedImplFileInput(contents = [ SynModuleOrNamespace(decls = declarations) ])) ->
    declarations
    |> List.collect (function
      | SynModuleDecl.Let(bindings = bindings) ->
        bindings |> List.map (fun (SynBinding(expr = expression)) -> expressionShape expression)
      | other -> failwithf "Unexpected quotation fixture declaration: %A" other)
  | other -> failwithf "Unexpected quotation fixture file: %A" other

let private preserved source =
  let before = quotationShapes source
  match Formatting.format source with
  | Error reason -> failwith reason
  | Ok code ->
    Assert.That(quotationShapes code, Is.EqualTo<ExpressionShape list>(before))
    match Formatting.format code with
    | Ok second -> Assert.That(second, Is.EqualTo code)
    | Error reason -> failwith reason
    code, before

[<TestCase("<@", "@>", false)>]
[<TestCase("<@@", "@@>", true)>]
let ``typed and raw quotations retain their quote kind and operator precedence`` (opening: string) (closing: string) (isRaw: bool) =
  // expressions.md also gives <@ 1 + 1 @> as its elementary quotation example.
  let source = sprintf "let law = %s 1+2*3 %s\n" opening closing
  let code, before = preserved source
  match before with
  | [ Quote(raw, Apply(Apply(Symbol addition, Literal(Integer 1)), Apply(Apply(Symbol multiplication, Literal(Integer 2)), Literal(Integer 3)))) ] ->
    Assert.That(raw, Is.EqualTo isRaw)
    Assert.That(addition, Is.Not.EqualTo multiplication)
  | other -> Assert.Fail(sprintf "fixture lost quotation or multiplication precedence: %A" other)
  Assert.That(code, Does.Contain opening)
  Assert.That(code, Does.Contain closing)

[<Test>]
let ``nested quotations preserve boundaries and the native quoted lambda body`` () =
  // The inner lambda is the second strongly typed example in expressions.md.
  let _, before = preserved "let declaration = <@@ <@ (fun x->x+1) @> @@>\n"
  match before with
  | [ Quote(true, Quote(false, Lambda([ Parameter "x" ], Apply(Apply(Symbol _, Symbol [ "x" ]), Literal(Integer 1))))) ] -> ()
  | other -> Assert.Fail(sprintf "fixture lost nested quotation or lambda structure: %A" other)

[<Test>]
let ``a native dimensioned quoted law preserves parameter measures and arithmetic`` () =
  let code, before = preserved "let forceLaw = <@ fun (mass:float<kg>)->mass*9.81e0<m/s^2> @>\n"
  match before with
  | [ Quote(false, Lambda([ AnnotatedParameter(Parameter "mass", annotation) ], Apply(Apply(Symbol _, Symbol [ "mass" ]), Literal(Measured(RealBits _, dimension))))) ] ->
    Assert.That(annotation, Is.EqualTo (TypeApplication(TypeName [ "float" ], [ TypeName [ "kg" ] ])))
    Assert.That(dimension, Is.EqualTo (Quotient(Some(MeasureName [ "m" ]), Power(MeasureName [ "s" ], 2, 1))))
  | other -> Assert.Fail(sprintf "fixture lost dimensional law structure: %A" other)
  Assert.That(code, Does.Contain "9.81e0")

[<Test>]
let ``quoted infix modulus and native clamp calls remain ordinary arithmetic`` () =
  // Width Inference section 7 expresses intended loss with % and clamp.
  let code, before = preserved "let bounds = <@ fun counter->(counter%256,clamp 0 255 counter) @>\n"
  match before with
  | [ Quote(false, Lambda([ Parameter "counter" ], Tuple [
      Apply(Apply(Symbol _, Symbol [ "counter" ]), Literal(Integer 256));
      Apply(Apply(Apply(Symbol [ "clamp" ], Literal(Integer 0)), Literal(Integer 255)), Symbol [ "counter" ])
    ])) ] -> ()
  | other -> Assert.Fail(sprintf "fixture lost quoted arithmetic or native call structure: %A" other)
  Assert.That(code, Does.Contain "counter % 256")

[<Test>]
let ``quotation comments and literal spelling survive in source order`` () =
  let tripleQuotes = String.replicate 3 "\""
  let source = String.concat "\n" [
    "let declaration ="
    "  <@"
    "    ("
    "      0x2A, // first quoted value"
    "      (* null :> obj stay comment text *)"
    "      9.81e0<m/s^2>,"
    "      @\"null :> obj <@ @>\","
    sprintf "      %s%s%s" tripleQuotes "% and %% stay literal text" tripleQuotes
    "    ) // last quoted value"
    "  @>"
    ""
  ]
  let code, before = preserved source
  match before with
  | [ Quote(false, Tuple [ Literal(Integer 42); Literal(Measured _); Literal(Text "null :> obj <@ @>"); Literal(Text "% and %% stay literal text") ]) ] -> ()
  | other -> Assert.Fail(sprintf "fixture lost quoted tuple or literal values: %A" other)
  let mutable next = 0
  for marker in [
    "0x2A"
    "// first quoted value"
    "(* null :> obj stay comment text *)"
    "9.81e0"
    "@\"null :> obj <@ @>\""
    "\"\"\"% and %% stay literal text\"\"\""
    "// last quoted value"
  ] do
    let position = code.IndexOf(marker, next, StringComparison.Ordinal)
    Assert.That(position, Is.GreaterThanOrEqualTo next, sprintf "lost or reordered %s" marker)
    next <- position + marker.Length

[<TestCase("let declaration = <@ null @>\n")>]
[<TestCase("let declaration = <@@ 42 :> obj @@>\n")>]
let ``quotations still refuse null and CLR widening in their expression bodies`` source =
  match Formatting.format source with
  | Error reason -> Assert.That(reason, Does.Contain "not permitted in Clef source")
  | Ok code -> Assert.Fail(sprintf "forbidden quoted source was rewritten: %s" code)

[<TestCase("let declaration = <@ %law @>\n")>]
[<TestCase("let declaration = <@@ %%law @@>\n")>]
let ``typed and raw quotation splices are refused as non Clef source`` source =
  match Formatting.format source with
  | Error reason -> Assert.That(reason, Does.Contain "quotation splice")
  | Ok code -> Assert.Fail(sprintf "quotation splice was rewritten: %s" code)
