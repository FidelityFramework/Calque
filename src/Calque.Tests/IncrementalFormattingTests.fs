module Calque.Tests.IncrementalFormattingTests

open System
open System.Threading
open System.Threading.Tasks
open NUnit.Framework
open Calque.Core
open Calque.Incremental
open Fidelity.FSharp.Incremental.Hosting

let private settings: Settings = { Epoch = 1UL; CommandCapacity = 64; MaxDemands = 8 }
let private document () = { Uri = "file:///workspace/Program.clef"; Incarnation = Guid.NewGuid() }
let private snapshot document revision source: SourceSnapshot = {
  Document = document; Revision = revision; ConfigurationRevision = 1UL
  IsSignature = false; Source = source
  Config = { FormatConfig.Default with IndentSize = 2; EndOfLine = EndOfLineStyle.LF }
}
let private require = function Ok value -> value | Error error -> failwithf "%A" error
let private expectError (expected: FormatterError) (answer: Result<'T, FormatterError>) =
  match answer with
  | Error actual -> Assert.That(actual, Is.EqualTo<FormatterError> expected)
  | Ok _ -> Assert.Fail(sprintf "expected %A, operation succeeded" expected)
let private run workflow = Async.RunSynchronously(workflow, timeout = 5000)
let private signal () = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
let private waitSignal (signal: TaskCompletionSource<unit>) =
  Assert.That(signal.Task.Wait(TimeSpan.FromSeconds 5.), Is.True, "controlled evaluator did not reach its gate")
let private formatted source = FormatOutcome.Formatted { Code = source; Cursor = None }
let private close handle = DocumentFormatter.beginClose handle |> DocumentFormatter.awaitClose |> run |> require
let private release request handle =
  DocumentFormatter.release request handle |> require |> DocumentFormatter.observeControl |> run |> require

[<Test; CancelAfter(10000)>]
let ``cold construction and explicit start do no formatting until demand`` () =
  let identity = document ()
  let mutable calls = 0
  let evaluate (snapshot: SourceSnapshot) _ = async {
    Interlocked.Increment(&calls) |> ignore
    return formatted snapshot.Source
  }
  let handle = DocumentFormatter.createWith settings identity evaluate |> require
  try
    let source = snapshot identity 1UL "let value=42\n"
    DocumentFormatter.request source handle |> expectError FormatterError.NotStarted
    Assert.That(calls, Is.Zero)
    DocumentFormatter.start handle |> require
    Assert.That(calls, Is.Zero)
    let request = DocumentFormatter.request source handle |> require
    let workflow = DocumentFormatter.observe request handle
    let first = workflow |> run |> require
    let second = workflow |> run |> require
    Assert.That(first, Is.EqualTo second)
    Assert.That(first.Snapshot, Is.EqualTo source)
    Assert.That(calls, Is.EqualTo 1)
  finally close handle

[<Test; CancelAfter(10000)>]
let ``one observer cancellation and one demand release preserve another consumer`` () =
  let identity = document ()
  let entered, finish = signal (), signal ()
  let mutable calls = 0
  let evaluate (snapshot: SourceSnapshot) _ = async {
    Interlocked.Increment(&calls) |> ignore
    entered.TrySetResult() |> ignore
    do! Async.AwaitTask finish.Task
    return formatted snapshot.Source
  }
  let handle = DocumentFormatter.createWith settings identity evaluate |> require
  try
    DocumentFormatter.start handle |> require
    let source = snapshot identity 1UL "let value=42\n"
    let first = DocumentFormatter.request source handle |> require
    let second = DocumentFormatter.request source handle |> require
    use cancelled = new CancellationTokenSource()
    let observer = Async.StartAsTask(DocumentFormatter.observe first handle, cancellationToken = cancelled.Token)
    waitSignal entered
    cancelled.Cancel()
    Assert.Catch<OperationCanceledException>(Action(fun () -> observer.GetAwaiter().GetResult() |> ignore)) |> ignore
    release first handle
    finish.TrySetResult() |> ignore
    let preview = DocumentFormatter.observe second handle |> run |> require
    Assert.That(preview.Snapshot, Is.EqualTo source)
    Assert.That(calls, Is.EqualTo 1)
    DocumentFormatter.observe first handle |> run |> expectError FormatterError.Released
  finally
    finish.TrySetResult() |> ignore
    close handle

[<Test; CancelAfter(10000)>]
let ``same labels cannot replace source configuration or document incarnation`` () =
  let identity = document ()
  let handle = DocumentFormatter.create settings identity |> require
  try
    DocumentFormatter.start handle |> require
    let original = snapshot identity 4UL "let value=42\n"
    let request = DocumentFormatter.request original handle |> require
    DocumentFormatter.observe request handle |> run |> require |> ignore
    let conflictingSource = { original with Source = "let value=43\n" }
    let conflictingConfig = { original with Config = { original.Config with IndentSize = 4 } }
    let foreign = { original with Document = { identity with Incarnation = Guid.NewGuid() } }
    DocumentFormatter.request conflictingSource handle |> expectError FormatterError.ConflictingSnapshot
    DocumentFormatter.request conflictingConfig handle |> expectError FormatterError.ConflictingSnapshot
    DocumentFormatter.request foreign handle |> expectError FormatterError.ForeignDocument
    DocumentFormatter.request { original with Revision = 3UL } handle |> expectError FormatterError.RevisionNotIncreasing
    Assert.That(DocumentFormatter.isCurrent original handle, Is.True)
  finally close handle

[<Test; CancelAfter(10000)>]
let ``new source and configuration revisions revoke old preview observations`` () =
  let identity = document ()
  let handle = DocumentFormatter.create settings identity |> require
  try
    DocumentFormatter.start handle |> require
    let original = snapshot identity 1UL "let compute value=\n    let adjusted=value+1\n    adjusted*2\n"
    let first = DocumentFormatter.request original handle |> require
    let oldPreview = DocumentFormatter.observe first handle |> run |> require
    let changed = { original with ConfigurationRevision = 2UL; Config = { original.Config with IndentSize = 3 } }
    let second = DocumentFormatter.request changed handle |> require
    Assert.That(DocumentFormatter.isCurrent oldPreview.Snapshot handle, Is.False)
    DocumentFormatter.observe first handle |> run |> expectError FormatterError.Superseded
    let preview = DocumentFormatter.observe second handle |> run |> require
    match preview.Outcome with
    | FormatOutcome.Formatted result -> Assert.That(result.Code, Does.Contain "\n   let adjusted")
    | other -> Assert.Fail(sprintf "%A" other)
    let thirdSource = { changed with Revision = 2UL; Source = "let compute value=value+2\n" }
    let third = DocumentFormatter.request thirdSource handle |> require
    let newest = DocumentFormatter.observe third handle |> run |> require
    Assert.That(newest.Snapshot, Is.EqualTo thirdSource)
    Assert.That(DocumentFormatter.isCurrent preview.Snapshot handle, Is.False)
  finally close handle

[<TestCase("let declaration = <@ %law @>\n", "CALQUE_FORMAT")>]
[<TestCase("let broken=\n", "CALQUE_PARSE")>]
[<CancelAfter(10000)>]
let ``dialect and incomplete source refusals retain the exact input revision`` (source: string) (expectedCode: string) =
  let identity = document ()
  let handle = DocumentFormatter.create settings identity |> require
  try
    DocumentFormatter.start handle |> require
    let input = snapshot identity 9UL source
    let request = DocumentFormatter.request input handle |> require
    let preview = DocumentFormatter.observe request handle |> run |> require
    Assert.That(preview.Snapshot, Is.EqualTo input)
    match preview.Outcome with
    | FormatOutcome.Refused diagnostic ->
      Assert.That(diagnostic.Code, Is.EqualTo expectedCode)
      Assert.That(diagnostic.Message, Is.Not.Empty)
    | other -> Assert.Fail(sprintf "refused source was published as %A" other)
  finally close handle

[<Test; CancelAfter(10000)>]
let ``current cancellation immediately fences late completion and close joins cleanup`` () =
  let identity = document ()
  let entered, cancellationSeen, cleanup = signal (), signal (), signal ()
  let evaluate (source: SourceSnapshot) cancellation = async {
    entered.TrySetResult() |> ignore
    do! WorkCancellation.wait cancellation
    cancellationSeen.TrySetResult() |> ignore
    // A stop request has arrived, but this evaluator still owns its cleanup.
    do! Async.AwaitTask cleanup.Task
    return formatted source.Source
  }
  let handle = DocumentFormatter.createWith settings identity evaluate |> require
  try
    DocumentFormatter.start handle |> require
    let source = snapshot identity 1UL "let value=42\n"
    let request = DocumentFormatter.request source handle |> require
    waitSignal entered
    let cancelled = DocumentFormatter.cancelCurrent handle |> require
    Assert.That(DocumentFormatter.isCurrent source handle, Is.False)
    DocumentFormatter.observe request handle |> run |> expectError FormatterError.Superseded
    cancelled |> DocumentFormatter.observeControl |> run |> require
    waitSignal cancellationSeen
    let closing = DocumentFormatter.beginClose handle
    let join = Async.StartAsTask(DocumentFormatter.awaitClose closing)
    DocumentFormatter.request source handle |> expectError FormatterError.Closed
    Assert.That(join.Wait(TimeSpan.FromMilliseconds 200.), Is.False, "close cannot certify cleanup while the evaluator is held")
    cleanup.TrySetResult() |> ignore
    Assert.That(join.Wait(TimeSpan.FromSeconds 5.), Is.True)
    join.GetAwaiter().GetResult() |> require
    DocumentFormatter.awaitClose closing |> run |> require
  finally
    cleanup.TrySetResult() |> ignore
    close handle

[<Test; CancelAfter(10000)>]
let ``superseded producer must drain before the newest document attempt starts`` () =
  let identity = document ()
  let entered, finish = signal (), signal ()
  let mutable calls = 0
  let evaluate (source: SourceSnapshot) _ = async {
    let call = Interlocked.Increment(&calls)
    if call = 1 then
      entered.TrySetResult() |> ignore
      do! Async.AwaitTask finish.Task
    return formatted source.Source
  }
  let handle = DocumentFormatter.createWith settings identity evaluate |> require
  try
    DocumentFormatter.start handle |> require
    let firstSource = snapshot identity 1UL "let value=42\n"
    let first = DocumentFormatter.request firstSource handle |> require
    waitSignal entered
    let latestSource = snapshot identity 2UL "let value=43\n"
    let latest = DocumentFormatter.request latestSource handle |> require
    DocumentFormatter.observe first handle |> run |> expectError FormatterError.Superseded
    Assert.That(calls, Is.EqualTo 1)
    finish.TrySetResult() |> ignore
    let preview = DocumentFormatter.observe latest handle |> run |> require
    Assert.That(preview.Snapshot, Is.EqualTo latestSource)
    Assert.That(calls, Is.EqualTo 2)
    Assert.That(DocumentFormatter.isCurrent firstSource handle, Is.False)
  finally
    finish.TrySetResult() |> ignore
    close handle

[<Test; CancelAfter(10000)>]
let ``outstanding demand capacity is bounded and release allows another consumer`` () =
  let identity = document ()
  let handle = DocumentFormatter.create { settings with MaxDemands = 2 } identity |> require
  try
    DocumentFormatter.start handle |> require
    let source = snapshot identity 1UL "let value=42\n"
    let first = DocumentFormatter.request source handle |> require
    let second = DocumentFormatter.request source handle |> require
    DocumentFormatter.observe second handle |> run |> require |> ignore
    DocumentFormatter.request source handle |> expectError FormatterError.DemandCapacity
    release first handle
    let third = DocumentFormatter.request source handle |> require
    DocumentFormatter.observe third handle |> run |> require |> ignore
  finally close handle

[<Test; CancelAfter(10000)>]
let ``failure after cancellation remains observable after joined close`` () =
  let identity = document ()
  let entered, cancellationSeen, cleanup = signal (), signal (), signal ()
  let evaluate _ cancellation = async {
    entered.TrySetResult() |> ignore
    do! WorkCancellation.wait cancellation
    cancellationSeen.TrySetResult() |> ignore
    do! Async.AwaitTask cleanup.Task
    return raise (InvalidOperationException "formatter-cleanup-failure-after-withdrawal")
  }
  let handle = DocumentFormatter.createWith settings identity evaluate |> require
  try
    DocumentFormatter.start handle |> require
    DocumentFormatter.request (snapshot identity 1UL "let value=42\n") handle |> require |> ignore
    waitSignal entered
    DocumentFormatter.cancelCurrent handle |> require |> DocumentFormatter.observeControl |> run |> require
    waitSignal cancellationSeen
    let closing = DocumentFormatter.beginClose handle
    use cancelled = new CancellationTokenSource()
    let detached = Async.StartAsTask(DocumentFormatter.awaitClose closing, cancellationToken = cancelled.Token)
    cancelled.Cancel()
    Assert.Catch<OperationCanceledException>(Action(fun () -> detached.GetAwaiter().GetResult() |> ignore)) |> ignore
    cleanup.TrySetResult() |> ignore
    DocumentFormatter.awaitClose closing |> run |> require
    let diagnostics = DocumentFormatter.drainDiagnostics handle
    Assert.That(diagnostics, Has.Length.EqualTo 1)
    Assert.That(diagnostics.Head.Failure.Message, Is.EqualTo "formatter-cleanup-failure-after-withdrawal")
    Assert.That(DocumentFormatter.drainDiagnostics handle, Is.Empty)
  finally
    cleanup.TrySetResult() |> ignore
    close handle

[<Test; CancelAfter(10000)>]
let ``conditional printer failure cannot finish while a sibling branch remains owned`` () =
  let identity = document ()
  let entered, failed, formatterFinished = signal (), signal (), signal ()
  use sibling = new ManualResetEventSlim(false)
  let mutable branches = 0
  let inspect _ =
    if Interlocked.Increment(&branches) = 1 then
      entered.TrySetResult() |> ignore
      if not (sibling.Wait(TimeSpan.FromSeconds 5.)) then failwith "conditional sibling gate timed out"
    else
      try raise (Calque.Core.FormatException "controlled-conditional-printer-failure")
      finally failed.TrySetResult() |> ignore
  let evaluate (source: SourceSnapshot) _ = async {
    try
      let! result =
        CodeFormatterImpl.formatDocumentWith inspect source.Config source.IsSignature
          (CodeFormatterImpl.getSourceText source.Source) None
      return FormatOutcome.Formatted result
    with :? Calque.Core.FormatException as error ->
      formatterFinished.TrySetResult() |> ignore
      return FormatOutcome.Refused { Code = "CALQUE_FORMAT"; Message = error.Message }
  }
  let handle = DocumentFormatter.createWith settings identity evaluate |> require
  try
    DocumentFormatter.start handle |> require
    let source = snapshot identity 1UL "#if FIRST\nlet first=1\n#else\nlet second=2\n#endif\n"
    let request = DocumentFormatter.request source handle |> require
    waitSignal entered
    waitSignal failed
    // Both printing branches entered. The failure cannot escape the operation
    // while the controlled sibling still runs; this is not a latency budget.
    Assert.That(formatterFinished.Task.Wait(TimeSpan.FromMilliseconds 200.), Is.False)
    let closing = DocumentFormatter.beginClose handle
    let joined = Async.StartAsTask(DocumentFormatter.awaitClose closing)
    Assert.That(joined.Wait(TimeSpan.FromMilliseconds 200.), Is.False)
    sibling.Set()
    Assert.That(joined.Wait(TimeSpan.FromSeconds 5.), Is.True)
    joined.GetAwaiter().GetResult() |> require
    waitSignal formatterFinished
    Assert.That(branches, Is.EqualTo 2)
    DocumentFormatter.observe request handle |> run |> expectError FormatterError.Closed
  finally
    sibling.Set()
    close handle

[<Test; CancelAfter(10000)>]
let ``close seals a cold formatter without starting any evaluation`` () =
  let identity = document ()
  let mutable calls = 0
  let evaluate (source: SourceSnapshot) _ = async {
    Interlocked.Increment(&calls) |> ignore
    return formatted source.Source
  }
  let handle = DocumentFormatter.createWith settings identity evaluate |> require
  close handle
  DocumentFormatter.start handle |> expectError FormatterError.Closed
  Assert.That(calls, Is.Zero)

let private substantialSource =
  [ for index in 1 .. 40 -> $"// value {index}\nlet value{index}=({index}+1)*2\n" ]
  |> String.concat "\n"

let private conditionalSource =
  "#if FIRST\n" + substantialSource + "#else\nlet fallback=0\n#endif\n"

let private phaseFromName = function
  | "Parse" -> FormattingPhase.Parse
  | "Oak" -> FormattingPhase.Oak
  | "Trivia" -> FormattingPhase.Trivia
  | "Dialect" -> FormattingPhase.Dialect
  | "Print" -> FormattingPhase.Print
  | "Merge" -> FormattingPhase.Merge
  | name -> failwithf "unknown checkpoint phase %s" name

[<Test; CancelAfter(10000)>]
let ``format and parse workflows stay cold through source preparation and initial parse`` () =
  let stopped = FormattingStoppedException()
  let mutable checks = 0
  let checkpoint _ =
    checks <- checks + 1
    raise stopped
  let isStopped error = obj.ReferenceEquals(error, stopped)
  let formatting = CodeFormatter.FormatDocumentWithCheckpointAsync(checkpoint, isStopped, false, substantialSource, FormatConfig.Default)
  let parsing = CodeFormatterImpl.parseWithCheckpoint (fun () -> checkpoint FormattingPhase.Parse) isStopped false
                  (CodeFormatterImpl.getSourceText substantialSource)
  Assert.That(checks, Is.Zero)
  let formatFailure = Assert.Catch(Action(fun () -> formatting |> run |> ignore))
  Assert.That(formatFailure, Is.SameAs stopped)
  Assert.That(checks, Is.EqualTo 1)
  let parseFailure = Assert.Catch(Action(fun () -> parsing |> run |> ignore))
  Assert.That(parseFailure, Is.SameAs stopped)
  Assert.That(checks, Is.EqualTo 2)

[<TestCase("Parse"); TestCase("Oak"); TestCase("Trivia"); TestCase("Dialect"); TestCase("Print"); TestCase("Merge")>]
[<CancelAfter(10000)>]
let ``real formatting stops inside the selected phase without completing it`` phaseName =
  let target = phaseFromName phaseName
  let source = if target = FormattingPhase.Merge then conditionalSource else substantialSource
  let stopped = FormattingStoppedException()
  let mutable checks = 0
  let checkpoint phase =
    if phase = target then
      checks <- checks + 1
      if checks = 12 then raise stopped
  let result =
    Assert.Catch(Action(fun () ->
      CodeFormatter.FormatDocumentWithCheckpointAsync(checkpoint, (fun error -> obj.ReferenceEquals(error, stopped)),
        false, source, FormatConfig.Default)
      |> run |> ignore))
  Assert.That(result, Is.SameAs stopped, "the stop must not become a parse or source diagnostic")
  Assert.That(checks, Is.EqualTo 12, "the real parser/walker/printer/merge must not run the remainder")

[<TestCase("Parse"); TestCase("Print"); TestCase("Merge")>]
[<CancelAfter(10000)>]
let ``releasing the last real pipeline demand stops work before a same revision replacement`` phaseName =
  let identity = document ()
  let target = phaseFromName phaseName
  let sourceText = if target = FormattingPhase.Merge then conditionalSource else substantialSource
  let entered = signal ()
  use resume = new ManualResetEventSlim(false)
  let mutable preparations = 0
  let mutable withdrawnChecks = 0
  let mutable replacementChecks = 0
  let inspect _ phase =
    if phase = FormattingPhase.SourcePreparation then Interlocked.Increment(&preparations) |> ignore
    if Volatile.Read(&preparations) <= 2 then
      if phase = target && Interlocked.Increment(&withdrawnChecks) = 12 then
        entered.TrySetResult() |> ignore
        if not (resume.Wait(TimeSpan.FromSeconds 5.)) then failwith "last-demand checkpoint gate timed out"
    else
      Interlocked.Increment(&replacementChecks) |> ignore
  let handle = DocumentFormatter.createWithCheckpoint settings identity inspect |> require
  try
    DocumentFormatter.start handle |> require
    let source = snapshot identity 1UL sourceText
    let first = DocumentFormatter.request source handle |> require
    waitSignal entered
    release first handle
    DocumentFormatter.observe first handle |> run |> expectError FormatterError.Released
    // No revision change or explicit cancellation can account for this stop.
    // A new demand for the exact input must wait for the withdrawn attempt.
    let replacement = DocumentFormatter.request source handle |> require
    Assert.That(preparations, Is.EqualTo 2, "the held attempt must drain before a replacement prepares source")
    Assert.That(replacementChecks, Is.Zero)
    resume.Set()
    let preview = DocumentFormatter.observe replacement handle |> run |> require
    Assert.That(preview.Snapshot, Is.EqualTo source)
    match preview.Outcome with
    | FormatOutcome.Formatted _ -> ()
    | other -> Assert.Fail(sprintf "same-revision demand was not formatted: %A" other)
    Assert.That(withdrawnChecks, Is.EqualTo 12, "the last release must stop the real pipeline at its held checkpoint")
    Assert.That(preparations, Is.EqualTo 4, "exactly one replacement performs its before/after preparation checks")
    Assert.That(replacementChecks, Is.GreaterThan 0)
    Assert.That(DocumentFormatter.drainDiagnostics handle, Is.Empty)
  finally
    resume.Set()
    close handle

[<TestCase("Parse"); TestCase("Print")>]
[<CancelAfter(10000)>]
let ``withdrawal interrupts the real pipeline and replacement starts only after its drain`` phaseName =
  let identity = document ()
  let target = phaseFromName phaseName
  let entered = signal ()
  use resume = new ManualResetEventSlim(false)
  let mutable oldChecks = 0
  let mutable replacementChecks = 0
  let inspect (source: SourceSnapshot) phase =
    if source.Revision = 1UL && phase = target then
      if Interlocked.Increment(&oldChecks) = 12 then
        entered.TrySetResult() |> ignore
        if not (resume.Wait(TimeSpan.FromSeconds 5.)) then failwith "pipeline checkpoint gate timed out"
    elif source.Revision = 2UL then
      Interlocked.Increment(&replacementChecks) |> ignore
  let handle = DocumentFormatter.createWithCheckpoint settings identity inspect |> require
  try
    DocumentFormatter.start handle |> require
    let original = snapshot identity 1UL substantialSource
    let first = DocumentFormatter.request original handle |> require
    waitSignal entered
    DocumentFormatter.cancelCurrent handle |> require |> DocumentFormatter.observeControl |> run |> require
    let replacement = snapshot identity 2UL "let replacement=42\n"
    let next = DocumentFormatter.request replacement handle |> require
    Assert.That(replacementChecks, Is.Zero, "the old evaluator still owns its blocked checkpoint")
    DocumentFormatter.observe first handle |> run |> expectError FormatterError.Superseded
    resume.Set()
    let preview = DocumentFormatter.observe next handle |> run |> require
    Assert.That(preview.Snapshot, Is.EqualTo replacement)
    Assert.That(oldChecks, Is.EqualTo 12, "withdrawn parsing/printing must stop at the held checkpoint")
    Assert.That(replacementChecks, Is.GreaterThan 0)
    Assert.That(DocumentFormatter.drainDiagnostics handle, Is.Empty)
  finally
    resume.Set()
    close handle

[<Test; CancelAfter(10000)>]
let ``releasing one real parser demand preserves its peer and shared computation`` () =
  let identity = document ()
  let entered = signal ()
  use resume = new ManualResetEventSlim(false)
  let mutable checks = 0
  let mutable preparations = 0
  let inspect _ phase =
    if phase = FormattingPhase.SourcePreparation then Interlocked.Increment(&preparations) |> ignore
    elif phase = FormattingPhase.Parse && Interlocked.Increment(&checks) = 12 then
      entered.TrySetResult() |> ignore
      if not (resume.Wait(TimeSpan.FromSeconds 5.)) then failwith "shared parser checkpoint timed out"
  let handle = DocumentFormatter.createWithCheckpoint settings identity inspect |> require
  try
    DocumentFormatter.start handle |> require
    let source = snapshot identity 1UL substantialSource
    let first = DocumentFormatter.request source handle |> require
    waitSignal entered
    let peer = DocumentFormatter.request source handle |> require
    release first handle
    resume.Set()
    let preview = DocumentFormatter.observe peer handle |> run |> require
    Assert.That(preview.Snapshot, Is.EqualTo source)
    match preview.Outcome with
    | FormatOutcome.Formatted _ -> ()
    | other -> Assert.Fail(sprintf "shared parser was not retained: %A" other)
    Assert.That(checks, Is.GreaterThan 12)
    Assert.That(preparations, Is.EqualTo 2, "one pipeline's before/after source preparation checks")
    Assert.That(DocumentFormatter.drainDiagnostics handle, Is.Empty)
  finally
    resume.Set()
    close handle

[<TestCase(false); TestCase(true)>]
[<CancelAfter(10000)>]
let ``a conditional stop joins its sibling and cannot hide an independent fault`` foreignStop =
  let stopped = FormattingStoppedException()
  let failure: exn =
    if foreignStop then FormattingStoppedException()
    else InvalidOperationException "independent-conditional-fault"
  let entered, stoppedBranch = signal (), signal ()
  use releaseSibling = new ManualResetEventSlim(false)
  let rec containsSecond (node: SyntaxOak.Node) =
    match node with
    | :? SyntaxOak.SingleTextNode as token when token.Text = "second" -> true
    | _ -> node.Children |> Array.exists containsSecond
  let inspect (oak: SyntaxOak.Oak) =
    if containsSecond oak then
      stoppedBranch.TrySetResult() |> ignore
      raise stopped
    else
      entered.TrySetResult() |> ignore
      if not (releaseSibling.Wait(TimeSpan.FromSeconds 5.)) then failwith "conditional cleanup gate timed out"
      raise failure
  let source = CodeFormatterImpl.getSourceText "#if FIRST\nlet first=1\n#else\nlet second=2\n#endif\n"
  let workflow =
    CodeFormatterImpl.formatDocumentWithCheckpoint ignore (fun error -> obj.ReferenceEquals(error, stopped))
      inspect FormatConfig.Default false source None
  let running = Async.StartAsTask workflow
  try
    waitSignal entered
    waitSignal stoppedBranch
    Assert.That(running.IsCompleted, Is.False, "a stopped branch does not abandon the owned sibling")
    releaseSibling.Set()
    let actual = Assert.Catch(Action(fun () -> running.GetAwaiter().GetResult() |> ignore))
    Assert.That(actual, Is.SameAs failure)
  finally
    releaseSibling.Set()
    try running.GetAwaiter().GetResult() |> ignore with _ -> ()

[<Test; CancelAfter(10000)>]
let ``a foreign stop from the real pipeline remains a host diagnostic`` () =
  let identity = document ()
  let foreign = FormattingStoppedException()
  let mutable checks = 0
  let inspect _ phase =
    if phase = FormattingPhase.Parse && Interlocked.Increment(&checks) = 12 then raise foreign
  let handle = DocumentFormatter.createWithCheckpoint settings identity inspect |> require
  try
    DocumentFormatter.start handle |> require
    let request = DocumentFormatter.request (snapshot identity 1UL substantialSource) handle |> require
    match DocumentFormatter.observe request handle |> run with
    | Error (FormatterError.HostFailure failure) -> Assert.That(failure.Message, Is.EqualTo foreign.Message)
    | other -> Assert.Fail(sprintf "foreign stop was swallowed: %A" other)
    Assert.That(checks, Is.EqualTo 12, "the foreign failure must cross the parser's token/exception wrapper")
    Assert.That(DocumentFormatter.drainDiagnostics handle, Has.Length.EqualTo 1)
  finally close handle
