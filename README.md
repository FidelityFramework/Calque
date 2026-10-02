# Calque

Calque formats Clef source. It is a focused fork of
[Fantomas](https://github.com/fsprojects/fantomas), with a library, incremental
formatting API and CLI hosted on .NET 10.

## Build and use

Use the SDK pinned in `global.json`:

```sh
dotnet build Calque.slnx
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --help
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --check path/to/Program.clef
dotnet src/Calque/bin/Debug/net10.0/Calque.dll path/to/Program.clef
dotnet src/Calque/bin/Debug/net10.0/Calque.dll --stdin < path/to/Program.clef
dotnet vstest src/Calque.Tests/bin/Debug/net10.0/Calque.Tests.dll
```

The CLI formats UTF-8 `.clef` files with two-space indentation. `--check` leaves
files unchanged. Exit codes: `0` for success, `1` for changes found by `--check`,
and `2` for diagnostics or I/O errors. Unsupported syntax is refused before writes.

## Documentation

- [Design, syntax support and integration](docs/design.md)
- [Provenance and bootstrap history](docs/history.md)
- [Contributing and validation](CONTRIBUTING.md)
- [Apache-2.0 license](LICENSE.md) and [upstream changelog](UPSTREAM_CHANGELOG.md)
