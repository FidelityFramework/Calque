namespace Calque.Incremental

open System
open Calque.Core
open Fidelity.FSharp.Incremental
open Fidelity.FSharp.Incremental.Hosting

type DocumentIdentity = { Uri: string; Incarnation: Guid }

/// The complete immutable input identity. Source and configuration labels cannot
/// be reused for different source bytes or formatting policy.
[<NoComparison>]
type SourceSnapshot = {
  Document: DocumentIdentity
  Revision: uint64
  ConfigurationRevision: uint64
  IsSignature: bool
  Source: string
  Config: FormatConfig
}

type Settings = { Epoch: uint64; CommandCapacity: int; MaxDemands: int }
type Diagnostic = { Code: string; Message: string }

[<RequireQualifiedAccess; NoComparison>]
type FormatOutcome =
  | Formatted of FormatResult
  | Refused of Diagnostic

[<NoComparison>]
type Preview = { Snapshot: SourceSnapshot; Outcome: FormatOutcome }

[<RequireQualifiedAccess>]
type FormatterError =
  | InvalidSettings of string
  | InvalidSnapshot of string
  | ForeignDocument
  | ConflictingSnapshot
  | RevisionNotIncreasing
  | DemandCapacity
  | NotStarted
  | Closed
  | Superseded
  | Released
  | Cancelled
  | Foundation of MailboxError
  | HostFailure of Failure

/// One explicitly started .NET host per document incarnation. No operation writes
/// source, grants edit permission, or provides compiler/execution authority.
[<RequireQualifiedAccess>]
module DocumentFormatter =
  [<NoEquality; NoComparison>]
  type Handle
  [<NoEquality; NoComparison>]
  type Request
  [<NoEquality; NoComparison>]
  type ControlOperation
  [<NoEquality; NoComparison>]
  type CloseOperation

  /// Cold construction: neither a coordinator nor formatter work starts here.
  val create: Settings -> DocumentIdentity -> Result<Handle, FormatterError>
  val start: Handle -> Result<unit, FormatterError>

  /// Admit demand and retain the exact ordered shared-foundation operations.
  /// A new snapshot immediately fences older observations. Inspect observe for
  /// final command acceptance; enqueueing alone does not establish a result.
  val request: SourceSnapshot -> Handle -> Result<Request, FormatterError>
  /// Cold and repeatable. Cancelling an observer does not release shared demand.
  val observe: Request -> Handle -> Async<Result<Preview, FormatterError>>
  val release: Request -> Handle -> Result<ControlOperation, FormatterError>
  /// Withdraw presentation immediately; physical cancellation/drain remain owned.
  val cancelCurrent: Handle -> Result<ControlOperation, FormatterError>
  val observeControl: ControlOperation -> Async<Result<unit, FormatterError>>

  /// Seal admission immediately. awaitClose joins the actual formatter workflow.
  val beginClose: Handle -> CloseOperation
  val awaitClose: CloseOperation -> Async<Result<unit, FormatterError>>
  /// A point-in-time presentation check; callers must order actual buffer writes
  /// against their own current editor revision.
  val isCurrent: SourceSnapshot -> Handle -> bool
  /// The owner must drain and retain these typed failures, including failures
  /// from superseded work. Joined close does not consume their evidence.
  val drainDiagnostics: Handle -> HostDiagnostic list

  val internal createWith:
    Settings -> DocumentIdentity ->
      (SourceSnapshot -> WorkCancellation -> Async<FormatOutcome>) -> Result<Handle, FormatterError>
