# cacalang

A small, C-like language with a compiler that both interprets programs and
compiles them to real .NET assemblies. Programs can call into .NET — the
standard library, or your own C# assemblies — through `extern` functions.
Builds and runs on Windows, macOS and Linux.

```
func isPrime(n: int): bool do
    if n < 2 then return false; end;

    var factor = 2;
    while factor * factor <= n do
        if n % factor == 0 then return false; end;
        factor = factor + 1;
    end;

    return true;
end

for n = 2 to 30 do
    if isPrime(n) then print n + " is prime"; end;
end;
```

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). From `cacalang/`:

```sh
dotnet build
dotnet test
```

Run a program, or compile it to an executable and run that:

```sh
dotnet run --project src/Caca.Cli -- run samples/helloworld.caca

dotnet run --project src/Caca.Cli -- build samples/helloworld.caca
./helloworld.exe
```

## Documentation

| | |
|---|---|
| [Language](Cacalang-Language) | A tour of the language, with examples |
| [Grammar](Cacalang-Grammar) | The formal specification and the semantics of each construct |
| [Command line](Cacalang-CLI) | `run`, `build`, `check`, the REPL, and debugging a compiled program |
| [Editor support](Cacalang-Editor) | The language server, and the Visual Studio Code extension |
| [Diagnostics](Cacalang-Diagnostics) | Every error the compiler reports, and what causes it |
| [Architecture](Cacalang-Architecture) | How the compiler works, stage by stage |
| [History](Cacalang-History) | Where the project came from, and what changed |
| [Contributing](Cacalang-Contributing) | Building, and how to add a language feature |

## Credits

cacalang began as the Good for Nothing compiler, written by Joel Pobar and
published in MSDN Magazine in February 2008 as
["Create a Language Compiler for the .NET Framework"](https://learn.microsoft.com/en-us/archive/msdn-magazine/2008/february/create-a-language-compiler-for-the-net-framework-using-csharp).
Original code copyright (c) Microsoft Corporation; the original terms were
published at a page that no longer exists. See [History](Cacalang-History).
