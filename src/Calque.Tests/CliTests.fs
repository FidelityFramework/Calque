module Calque.Tests.CliTests

open System
open System.IO
open System.Text
open NUnit.Framework
open Calque

let private invoke arguments source =
  use input = new StringReader(source)
  use output = new StringWriter()
  use errors = new StringWriter()
  let code = Cli.run arguments input output errors
  code, output.ToString(), errors.ToString()

let private invokeBeforeReplace beforeReplace arguments =
  use input = new StringReader("")
  use output = new StringWriter()
  use errors = new StringWriter()
  let code = Cli.runWithBeforeReplace beforeReplace arguments input output errors
  code, output.ToString(), errors.ToString()

let private withDirectory action =
  let path = Path.Combine(Path.GetTempPath(), "calque-tests-" + Guid.NewGuid().ToString("N"))
  Directory.CreateDirectory path |> ignore
  try action path
  finally Directory.Delete(path, true)

let private write directory name (source: string) =
  let path = Path.Combine(directory, name)
  File.WriteAllText(path, source, UTF8Encoding(false))
  path

[<Test>]
let ``check reports formatting changes without writing bytes or timestamps`` () =
  withDirectory (fun directory ->
    let path = write directory "sample.clef" "let answer=42\n"
    let original = File.ReadAllBytes path
    let time = DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
    File.SetLastWriteTimeUtc(path, time)
    let code, output, errors = invoke [| "--check"; path |] ""
    Assert.That(code, Is.EqualTo 1)
    Assert.That(output, Does.Contain path)
    Assert.That(errors, Is.Empty)
    Assert.That(File.ReadAllBytes path, Is.EqualTo<byte array>(original))
    Assert.That(File.GetLastWriteTimeUtc path, Is.EqualTo time))

[<Test>]
let ``format then check accepts the same file without touching it again`` () =
  withDirectory (fun directory ->
    let path = write directory "sample.clef" "let calculate value=\n    let adjusted=value+1\n    adjusted*2\n"
    let code, _, errors = invoke [| path |] ""
    Assert.That(code, Is.Zero)
    Assert.That(errors, Is.Empty)
    let formatted = File.ReadAllText path
    Assert.That(formatted, Does.Contain "\n  let adjusted = value + 1\n")
    let time = File.GetLastWriteTimeUtc path
    let checkCode, output, errors = invoke [| "--check"; path |] ""
    Assert.That(checkCode, Is.Zero)
    Assert.That(output, Is.Empty)
    Assert.That(errors, Is.Empty)
    Assert.That(File.ReadAllText path, Is.EqualTo formatted)
    Assert.That(File.GetLastWriteTimeUtc path, Is.EqualTo time))

[<Test>]
let ``all input files must parse before any source file is written`` () =
  withDirectory (fun directory ->
    let first = write directory "valid.clef" "let answer=42\n"
    let second = write directory "invalid.clef" "let broken =\n"
    let originalFirst, originalSecond = File.ReadAllBytes first, File.ReadAllBytes second
    let code, output, errors = invoke [| first; second |] ""
    Assert.That(code, Is.EqualTo 2)
    Assert.That(output, Is.Empty)
    Assert.That(errors, Does.Contain second)
    Assert.That(File.ReadAllBytes first, Is.EqualTo<byte array>(originalFirst))
    Assert.That(File.ReadAllBytes second, Is.EqualTo<byte array>(originalSecond)))

[<TestCase("let value = eager 42\n")>]
[<TestCase("let line = __LINE__\n")>]
[<TestCase("#line 100 \"file.clef\"\nlet value = 42\n")>]
[<TestCase("#100 \"file.clef\"\nlet value = 42\n")>]
[<TestCase("# 100 \"file.clef\"\nlet value = 42\n")>]
[<TestCase("let large = 9223372036854775808\n")>]
[<TestCase("#if FEATURE\nlet value = eager 42\n#else\nlet value = 42\n#endif\n")>]
[<TestCase("let value = 42 :> obj\n")>]
[<TestCase("let value = null\n")>]
[<TestCase("let values: obj[] = [||]\n")>]
[<TestCase("let choose value = match value with null -> 0 | _ -> 1\n")>]
[<TestCase("let choose value = match value with :? string -> 0 | _ -> 1\n")>]
[<TestCase("let identity (value: #System.IDisposable) = value\n")>]
[<TestCase("let f<'T when 'T : delegate<unit, unit>> () = ()\n")>]
[<TestCase("type Holder(value: int) =\n  member _.Value = value\n")>]
[<TestCase("let value = { new System.IDisposable with member _.Dispose() = () }\n")>]
let ``unsupported dialect source refuses the whole file batch before writes`` source =
  withDirectory (fun directory ->
    let first = write directory "valid.clef" "let answer=42\n"
    let second = write directory "unsupported.clef" source
    let originalFirst, originalSecond = File.ReadAllBytes first, File.ReadAllBytes second
    let code, output, errors = invoke [| first; second |] ""
    Assert.That(code, Is.EqualTo 2)
    Assert.That(output, Is.Empty)
    Assert.That(errors, Does.Contain second)
    Assert.That(File.ReadAllBytes first, Is.EqualTo<byte array>(originalFirst))
    Assert.That(File.ReadAllBytes second, Is.EqualTo<byte array>(originalSecond)))

[<Test>]
let ``conditional branch refusal prevents every file write in the batch`` () =
  withDirectory (fun directory ->
    let first = write directory "first.clef" "let answer=42\n"
    let source = """module ConditionalCoverage
#if (A1 || B1) && (!A1 || !B1) && (A2 || B2) && (!A2 || !B2) && (A3 || B3) && (!A3 || !B3) && (A4 || B4) && (!A4 || !B4) && (A5 || B5) && (!A5 || !B5)
let hidden = 17
#endif
let ordinary = 42
"""
    let conditional = write directory "conditional.clef" source
    let originalFirst, originalConditional = File.ReadAllBytes first, File.ReadAllBytes conditional
    let code, output, errors = invoke [| first; conditional |] ""
    Assert.That(code, Is.EqualTo 2)
    Assert.That(output, Is.Empty)
    Assert.That(errors, Does.Contain conditional)
    Assert.That(errors, Does.Contain "branch coverage is inconclusive")
    Assert.That(File.ReadAllBytes first, Is.EqualTo<byte array>(originalFirst))
    Assert.That(File.ReadAllBytes conditional, Is.EqualTo<byte array>(originalConditional)))

[<Test>]
let ``imperative source formats through the CLI and remains idempotent`` () =
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
  let code, output, errors = invoke [| "--stdin" |] source
  Assert.That(code, Is.Zero)
  Assert.That(errors, Is.Empty)
  Assert.That(output, Does.Contain "let mutable sum = 0")
  Assert.That(output, Does.Contain "for value in")
  Assert.That(output, Does.Contain "while remaining > 0 do")
  Assert.That(output, Does.Contain "original with")
  let checkCode, replacement, errors = invoke [| "--check"; "--stdin" |] output
  Assert.That(checkCode, Is.Zero)
  Assert.That(replacement, Is.Empty)
  Assert.That(errors, Is.Empty)

[<Test>]
let ``literal CLR words and qualified functional calls format through the CLI`` () =
  let source = "// :> obj null class member\nlet ``null``=42\nlet ``obj``=``null``\nlet text=\":> obj null class member\"\nlet printed=Console.writeln text\n"
  let code, output, errors = invoke [| "--stdin" |] source
  Assert.That(code, Is.Zero)
  Assert.That(errors, Is.Empty)
  Assert.That(output, Does.Contain "// :> obj null class member")
  Assert.That(output, Does.Contain "\":> obj null class member\"")
  Assert.That(output, Does.Contain "``obj``")
  Assert.That(output, Does.Contain "``null``")
  Assert.That(output, Does.Contain "Console.writeln text")
  let checkCode, _, errors = invoke [| "--check"; "--stdin" |] output
  Assert.That(checkCode, Is.Zero)
  Assert.That(errors, Is.Empty)

[<Test>]
let ``stdin formats source and check emits no replacement text`` () =
  let source = "// retained\nlet answer=42\n"
  let code, output, errors = invoke [| "--stdin" |] source
  Assert.That(code, Is.Zero)
  Assert.That(output, Does.Contain "// retained")
  Assert.That(output, Does.Contain "let answer = 42")
  Assert.That(errors, Is.Empty)
  let dirty, replacement, errors = invoke [| "--check"; "-" |] source
  Assert.That(dirty, Is.EqualTo 1)
  Assert.That(replacement, Is.Empty)
  Assert.That(errors, Is.Empty)
  let clean, replacement, errors = invoke [| "--check"; "--stdin" |] output
  Assert.That(clean, Is.Zero)
  Assert.That(replacement, Is.Empty)
  Assert.That(errors, Is.Empty)

[<Test>]
let ``invalid stdin fails without emitting a replacement`` () =
  let code, output, errors = invoke [| "--stdin" |] "let broken =\n"
  Assert.That(code, Is.EqualTo 2)
  Assert.That(output, Is.Empty)
  Assert.That(errors, Is.Not.Empty)

[<Test>]
let ``UTF8 source preserves Unicode comments and an existing BOM`` () =
  withDirectory (fun directory ->
    let path = Path.Combine(directory, "unicode.clef")
    File.WriteAllText(path, "// café — λ\nlet answer=42\n", UTF8Encoding(true))
    let code, _, errors = invoke [| path |] ""
    Assert.That(code, Is.Zero)
    Assert.That(errors, Is.Empty)
    let bytes = File.ReadAllBytes path
    Assert.That(bytes[0..2], Is.EqualTo<byte array>([| 0xEFuy; 0xBBuy; 0xBFuy |]))
    Assert.That(File.ReadAllText path, Does.Contain "// café — λ"))

[<Test>]
let ``an edit during formatting is preserved and its staged replacement is removed`` () =
  withDirectory (fun directory ->
    let path = write directory "sample.clef" "let answer=42\n"
    let edited = UTF8Encoding(false).GetBytes "// newer edit\nlet answer = 43\n"
    let code, output, errors =
      invokeBeforeReplace (fun original -> File.WriteAllBytes(original, edited)) [| path |]
    Assert.That(code, Is.EqualTo 2)
    Assert.That(output, Is.Empty)
    Assert.That(errors, Does.Contain "changed since preparation")
    Assert.That(File.ReadAllBytes path, Is.EqualTo<byte array>(edited))
    Assert.That(Directory.GetFiles directory, Is.EquivalentTo [| path |]))

[<Test>]
let ``a failure before replacement leaves original bytes and no staged file`` () =
  withDirectory (fun directory ->
    let path = write directory "sample.clef" "let answer=42\n"
    let original = File.ReadAllBytes path
    let code, _, errors =
      invokeBeforeReplace (fun _ -> raise (IOException "injected write boundary failure")) [| path |]
    Assert.That(code, Is.EqualTo 2)
    Assert.That(errors, Does.Contain "injected write boundary failure")
    Assert.That(File.ReadAllBytes path, Is.EqualTo<byte array>(original))
    Assert.That(Directory.GetFiles directory, Is.EquivalentTo [| path |]))

[<Test>]
let ``a symbolic link input refuses the batch without replacing the link or any source`` () =
  if not (OperatingSystem.IsWindows()) then
    withDirectory (fun directory ->
      let first = write directory "first.clef" "let answer=42\n"
      let target = write directory "target.clef" "let target=43\n"
      let link = Path.Combine(directory, "link.clef")
      File.CreateSymbolicLink(link, target) |> ignore
      let originalFirst, originalTarget = File.ReadAllBytes first, File.ReadAllBytes target
      let code, output, errors = invoke [| first; link |] ""
      Assert.That(code, Is.EqualTo 2)
      Assert.That(output, Is.Empty)
      Assert.That(errors, Does.Contain "symbolic-link inputs are refused")
      Assert.That(File.ReadAllBytes first, Is.EqualTo<byte array>(originalFirst))
      Assert.That(File.ReadAllBytes target, Is.EqualTo<byte array>(originalTarget))
      Assert.That(FileInfo(link).LinkTarget, Is.EqualTo target)
      Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty))

[<Test>]
let ``a symbolic link introduced before replacement is preserved and refused`` () =
  if not (OperatingSystem.IsWindows()) then
    withDirectory (fun directory ->
      let path = write directory "sample.clef" "let answer=42\n"
      let target = write directory "target.clef" "let answer=42\n"
      let originalTarget = File.ReadAllBytes target
      let introduceLink original =
        File.Delete original
        File.CreateSymbolicLink(original, target) |> ignore
      let code, _, errors = invokeBeforeReplace introduceLink [| path |]
      Assert.That(code, Is.EqualTo 2)
      Assert.That(errors, Does.Contain "became a symbolic link")
      Assert.That(FileInfo(path).LinkTarget, Is.EqualTo target)
      Assert.That(File.ReadAllBytes target, Is.EqualTo<byte array>(originalTarget))
      Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty))

[<Test>]
let ``formatting preserves Unix permission bits`` () =
  if not (OperatingSystem.IsWindows()) then
    withDirectory (fun directory ->
      let path = write directory "sample.clef" "let answer=42\n"
      let mode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute ||| UnixFileMode.GroupRead
      File.SetUnixFileMode(path, mode)
      let code, _, errors = invoke [| path |] ""
      Assert.That(code, Is.Zero)
      Assert.That(errors, Is.Empty)
      Assert.That(File.GetUnixFileMode path, Is.EqualTo mode))

[<TestCase("--unknown")>]
[<TestCase("sample.fs")>]
let ``unsupported arguments return an error without source output`` argument =
  let code, output, errors = invoke [| argument |] ""
  Assert.That(code, Is.EqualTo 2)
  Assert.That(output, Is.Empty)
  Assert.That(errors, Is.Not.Empty)

[<Test>]
let ``stdin and explicit files cannot be mixed`` () =
  let code, output, errors = invoke [| "--stdin"; "sample.clef" |] "let answer=42\n"
  Assert.That(code, Is.EqualTo 2)
  Assert.That(output, Is.Empty)
  Assert.That(errors, Does.Contain "cannot be combined")

[<Test>]
let ``help describes Calque and the syntax boundary`` () =
  let code, output, errors = invoke [| "--help" |] ""
  Assert.That(code, Is.Zero)
  Assert.That(output, Does.Contain "Calque")
  Assert.That(output, Does.Contain "unsupported Clef syntax")
  Assert.That(errors, Is.Empty)
