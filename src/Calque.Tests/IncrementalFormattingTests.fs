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
