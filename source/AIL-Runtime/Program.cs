using System;
using Artemis_IL;
using System.IO;

namespace AIL_Runtime
{
    /// <summary>
    /// Entry point for the AIL-Runtime command-line host.
    /// Compiles a <c>.ail</c> source file, or loads pre-compiled bytecode directly, and
    /// runs it inside an <see cref="Artemis_IL.VM"/> instance sized by
    /// <see cref="Artemis_IL.Globals.DefaultRamSize"/>.
    /// When invoked without arguments the built-in "Hello, World!" demo is executed.
    /// </summary>
    class Program
    {
        // Instruction encoding helper: byte 0 = (opcode << 2) | addrmode
        // Opcodes: MOV=0x01, KEI=0x2B
        // Addressing modes: RegReg=0x00, RegVal=0x02
        // Registers: AL=0xF5, AH=0xF6
        //
        // "Hello, World!" printed one character at a time via KEI 0x01 (AL=0x01 write-char mode).
        //   MOV AL, 0x01       = 0x06 0xF5 0x01 0x00 0x00 0x00
        //   MOV AH, <char>     = 0x06 0xF6 <ch> 0x00 0x00 0x00
        //   KEI 0x01           = 0xAC 0x01 0x00 0x00 0x00 0x00
        //   KEI 0x02           = 0xAC 0x02 0x00 0x00 0x00 0x00  (halt)
        public static byte[] HelloWorld =
        {
            // MOV AL, 0x01  (write-char mode)
            0x06, 0xF5, 0x01, 0x00, 0x00, 0x00,
            // MOV AH, 'H'
            0x06, 0xF6, 0x48, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'e'
            0x06, 0xF6, 0x65, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'l'
            0x06, 0xF6, 0x6C, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'l'
            0x06, 0xF6, 0x6C, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'o'
            0x06, 0xF6, 0x6F, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, ','
            0x06, 0xF6, 0x2C, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, ' '
            0x06, 0xF6, 0x20, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'W'
            0x06, 0xF6, 0x57, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'o'
            0x06, 0xF6, 0x6F, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'r'
            0x06, 0xF6, 0x72, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'l'
            0x06, 0xF6, 0x6C, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, 'd'
            0x06, 0xF6, 0x64, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, '!'
            0x06, 0xF6, 0x21, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // MOV AH, '\n' (0x0A)
            0x06, 0xF6, 0x0A, 0x00, 0x00, 0x00,
            // KEI 0x01
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00,
            // KEI 0x02  (halt)
            0xAC, 0x02, 0x00, 0x00, 0x00, 0x00,
        };

        /// <summary>
        /// Application entry point.
        /// If <paramref name="args"/> is empty, runs the built-in <see cref="HelloWorld"/> demo.
        /// Otherwise treats <c>args[0]</c> as a file path: a <c>.ail</c> file is compiled from
        /// source (see <see cref="AIL_Studio.Compiler.Compiler"/>); anything else is read as
        /// raw pre-compiled bytecode, as before.
        /// Any VM exception is caught, printed in red, and waits for a keypress before exiting.
        /// </summary>
        static void Main(string[] args)
        {
            byte[] LoadedApplication = null;
            Artemis_IL.Globals.console = new AIL_Runtime.AR_Console();
            if (args.Length == 0)
            {
                Console.Title = "Artemis-VM Runtime - Hello World!";
                LoadedApplication = HelloWorld;
            }
            else if (args[0].EndsWith(".ail", StringComparison.OrdinalIgnoreCase))
            {
                string source = File.ReadAllText(args[0]);
                try
                {
                    LoadedApplication = new AIL_Studio.Compiler.Compiler(source).Compile();
                }
                catch (AIL_Studio.Compiler.BuildException ex)
                {
                    // file(line,col): error CODE: message — MSBuild-style diagnostic
                    // format, parseable by a $msCompile-style problem matcher.
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"{args[0]}({ex.SrcLineNumber},1): error AIL001: {ex.Message}");
                    Console.ResetColor();
                    Environment.Exit(1);
                    return;
                }
            }
            else
            {
                LoadedApplication = File.ReadAllBytes(args[0]);
            }
            try
            {
                Artemis_IL.Executable.Run(LoadedApplication);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message + "\nPress any key to terminate...");
                Console.ReadKey(true);
            }
        }
    }
}

