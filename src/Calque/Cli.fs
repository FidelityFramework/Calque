namespace Calque

open System
open System.IO
open System.Text

module Cli =
  let private usage =
    """Calque — Clef source formatter

Usage: calque [--check] FILE.clef...
       calque [--check] --stdin

Formats explicit UTF-8 .clef files with two-space indentation.
Symbolic-link inputs are refused.
--stdin (or -) reads source from stdin and writes formatted source to stdout.
--check reports files requiring formatting and never writes source.
-- separates options from file paths; --help prints this help.

Exit codes: 0 success/unchanged, 1 formatting required, 2 refusal or error.
The retained parser supports a guarded syntax subset; unsupported Clef syntax
is refused without translating the source.
"""

  type private Input =
    | Files of string list
    | StandardInput

  type private Invocation =
    | Help
    | Format of check: bool * input: Input

  type private PreparedFile = {
    Path: string
    OriginalBytes: byte array
    Original: string
    Formatted: string
    HasBom: bool
  }

  let private parse (arguments: string array) =
    let rec collect check stdin paths positional remaining =
      match remaining with
      | [] ->
        match stdin, List.rev paths with
        | true, [] -> Ok (Format(check, StandardInput))
        | true, _ -> Error "stdin cannot be combined with file paths."
        | false, [] -> Error "provide explicit .clef file paths or --stdin."
        | false, paths -> Ok (Format(check, Files(List.distinct paths)))
      | "--help" :: rest when not positional ->
        if rest.IsEmpty && paths.IsEmpty && not stdin && not check then Ok Help
        else Error "--help must be used on its own."
      | "--check" :: rest when not positional -> collect true stdin paths false rest
      | ("--stdin" | "-") :: rest when not positional -> collect check true paths false rest
      | "--" :: rest when not positional -> collect check stdin paths true rest
      | option :: _ when not positional && option.StartsWith("-", StringComparison.Ordinal) ->
        Error (sprintf "unknown option '%s'." option)
      | path :: rest ->
        if not (String.Equals(Path.GetExtension path, ".clef", StringComparison.OrdinalIgnoreCase)) then
          Error (sprintf "'%s' is not a .clef file." path)
        else collect check stdin (path :: paths) positional rest
    collect false false [] false (List.ofArray arguments)

  let private utf8 = UTF8Encoding(false, true)

  let private isSymbolicLink path =
    FileInfo(path).LinkTarget |> Option.ofObj |> Option.isSome

  let private prepareFile path =
    try
      if isSymbolicLink path then
        raise (IOException "symbolic-link inputs are refused.")
      let bytes = File.ReadAllBytes path
      let hasBom = bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy
      let offset = if hasBom then 3 else 0
      let original = utf8.GetString(bytes, offset, bytes.Length - offset)
      Formatting.format original
      |> Result.map (fun formatted -> {
        Path = path
        OriginalBytes = bytes
        Original = original
        Formatted = formatted
        HasBom = hasBom
      })
      |> Result.mapError (fun reason -> sprintf "%s: %s" path reason)
    with error -> Error (sprintf "%s: %s" path error.Message)

  let private prepareFiles paths =
    let rec loop prepared = function
      | [] -> Ok (List.rev prepared)
      | path :: remaining ->
        match prepareFile path with
        | Ok file -> loop (file :: prepared) remaining
        | Error reason -> Error reason
    loop [] paths

  let private replaceFile beforeReplace file =
    let path = Path.GetFullPath file.Path
    let temporary = Path.Combine(Path.GetDirectoryName path, "." + Path.GetFileName path + ".calque-" + Guid.NewGuid().ToString("N") + ".tmp")
    try
      let encoding = UTF8Encoding(file.HasBom, true)
      let bytes = Array.append (encoding.GetPreamble()) (encoding.GetBytes file.Formatted)
      do
        use staged = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
        staged.Write(bytes, 0, bytes.Length)
        staged.Flush(true)
      if not (OperatingSystem.IsWindows()) then
        File.SetUnixFileMode(temporary, File.GetUnixFileMode path)
      beforeReplace file.Path
      if isSymbolicLink path then
        raise (IOException(sprintf "%s became a symbolic link; source was not replaced." file.Path))
      if File.ReadAllBytes path <> file.OriginalBytes then
        raise (IOException(sprintf "%s changed since preparation; source was not replaced." file.Path))
      File.Move(temporary, path, true)
    finally
      if File.Exists temporary then File.Delete temporary

  /// The executable and tests use this same entry path. Every file is parsed
  /// and formatted successfully before the first source file is written. The
  /// callback exposes the replacement boundary for deterministic I/O tests.
  let runWithBeforeReplace beforeReplace (arguments: string array) (input: TextReader) (output: TextWriter) (errors: TextWriter) =
    let refuse reason =
      errors.WriteLine("Calque: " + reason)
      2
    try
      match parse arguments with
      | Error reason -> refuse reason
      | Ok Help ->
        output.Write usage
        0
      | Ok (Format(check, StandardInput)) ->
        let source = input.ReadToEnd()
        match Formatting.format source with
        | Error reason -> refuse reason
        | Ok formatted when check -> if formatted = source then 0 else 1
        | Ok formatted ->
          output.Write formatted
          0
      | Ok (Format(check, Files paths)) ->
        match prepareFiles paths with
        | Error reason -> refuse reason
        | Ok files ->
          let changed = files |> List.filter (fun file -> file.Original <> file.Formatted)
          if check then
            for file in changed do output.WriteLine file.Path
            if changed.IsEmpty then 0 else 1
          else
            for file in changed do
              replaceFile beforeReplace file
            0
    with error -> refuse error.Message

  let run arguments input output errors =
    runWithBeforeReplace ignore arguments input output errors
