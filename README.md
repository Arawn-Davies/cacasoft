# Artemis-IL

A free and open source register-based virtual machine and intermediate language targeting .NET Standard.

Artemis-IL (AIL) provides a stable, portable compilation target with a small orthogonal instruction set, a two-pass assembler, a decompiler, and an integrated IDE (AIL Studio). The same AIL bytecode runs identically on any compliant VM implementation.

For the full language and VM specification, standard library reference, and instruction set, see the **[GitHub Wiki](../../wiki)**.

---

## Repository layout

| Path | Contents |
|------|----------|
| `/source/Artemis-VM` | Core VM library (netstandard2.0) — registers, RAM, instruction execution |
| `/source/AIL-Runtime` | Command-line runtime host (net10.0) |
| `/source/AIL-Studio` | WinForms IDE with assembler, decompiler, and step debugger (net10.0-windows) |
| `/source/AIL-Tests` | xUnit test suite |
| `/wiki` | Mirror of the GitHub Wiki pages |
| `/LICENSES` | License texts for this project and its FOSS dependencies |
| `/examples` | Sample `.ail` assembly programs |

---

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
# Build all projects
dotnet build "Artemis IL.sln"

# Run the tests
dotnet test source/AIL-Tests/AIL-Tests.csproj
```

> **Note:** AIL-Studio targets `net10.0-windows` and will only build on Windows. The VM library, runtime, and tests build cross-platform.

---

## Running

```sh
# Run the built-in "Hello, World!" demo
dotnet run --project source/AIL-Runtime

# Compile and run a .ail source file directly
dotnet run --project source/AIL-Runtime -- path/to/program.ail

# Or run pre-compiled bytecode
dotnet run --project source/AIL-Runtime -- path/to/program.ila
```

---

## Examples

The `/examples` folder contains ready-to-assemble `.ail` programs:

| File | Description |
|------|-------------|
| `hello_world_db.ail` | Prints "Hello, World" using the `DB` pseudo-instruction and the write-string interrupt |
| `calculator.ail` | Demonstrates arithmetic instructions and integer output |

Open any `.ail` file in AIL Studio to assemble, run, and step-debug it interactively.

---

## License

This project is released under the [Clear BSD License](LICENSE). License texts for all FOSS dependencies (xUnit, .NET SDK) are collected in the [`LICENSES/`](LICENSES/) directory.
