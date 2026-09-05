using System;
using System.Collections.Generic;
using System.Text;

namespace Caca.VM.Compiler
{
    /// <summary>
    /// Two-pass assembler for CacaVM.
    ///
    /// <b>Syntax (per line):</b>
    /// <code>
    ///   ; single-line comment
    ///   // alternative single-line comment
    ///   label:
    ///   MNEMONIC [param1 [, param2]]
    ///   DB value1 [, value2 ...]
    /// </code>
    ///
    /// <b>Operand forms:</b>
    /// <list type="bullet">
    ///   <item><description>Register name (case-insensitive): PC IP SP SS A AL AH B BL BH C CL CH X Y</description></item>
    ///   <item><description>Decimal integer: 42</description></item>
    ///   <item><description>Hex integer: 0xFF</description></item>
    ///   <item><description>Char literal: 'H'  '\n'  ' '</description></item>
    ///   <item><description>Label reference: jumps and data address loads</description></item>
    ///   <item><description>String literal (DB only): "Hello, World"</description></item>
    /// </list>
    ///
    /// <b>Instruction encoding (spec §2):</b>
    /// Every assembled instruction is exactly 6 bytes:
    /// <code>
    ///   byte 0   : (opcode &lt;&lt; 2) | address_mode
    ///   byte 1   : param1  (8-bit register byte or value)
    ///   bytes 2-5: param2  (32-bit little-endian value or register byte in low byte)
    /// </code>
    ///
    /// <b>DB pseudo-instruction:</b>
    /// Emits raw bytes into the output stream without producing a full 6-byte instruction.
    /// Labels that follow a DB block are assigned the correct byte offset so that
    /// instructions such as <c>MOV X, label</c> resolve to the right address.
    /// </summary>
    public class Compiler
    {
        private readonly string _source;

        /// <summary>Raw bytecode produced after <see cref="Compile"/>.</summary>
        public byte[] ByteCode { get; private set; } = Array.Empty<byte>();

        /// <param name="source">CIL assembly source text to compile.</param>
        public Compiler(string source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Assembles the source and returns the raw bytecode.
        /// The result is also stored in <see cref="ByteCode"/>.
        /// Throws <see cref="BuildException"/> on any syntax or semantic error.
        /// </summary>
        public byte[] Compile()
        {
            if (string.IsNullOrWhiteSpace(_source))
                throw new BuildException("Source cannot be empty.");

            // Normalise line endings so only '\n' needs to be handled.
            string[] lines = _source.Replace("\r", "").Split('\n');

            // ── Pass 1: discover labels and compute byte offsets ──────────────
            // Regular instructions are always 6 bytes.  DB pseudo-instructions
            // emit variable-length raw data, so we count actual byte offsets
            // rather than instruction-count × 6.
            var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int byteOffset = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string stripped = StripComment(lines[i]).Trim();
                if (stripped.Length == 0)
                    continue;

                if (stripped.EndsWith(':'))
                {
                    // Label definition — record current byte offset.
                    string name = stripped[..^1].Trim();
                    if (name.Length == 0)
                        throw new BuildException("Empty label name.", i + 1);
                    if (labels.ContainsKey(name))
                        throw new BuildException($"Duplicate label '{name}'.", i + 1);
                    labels[name] = byteOffset;
                }
                else
                {
                    string[] tokens = Tokenise(stripped);
                    if (tokens.Length == 0)
                        continue;

                    if (tokens[0].Equals("DB", StringComparison.OrdinalIgnoreCase))
                        byteOffset += CountDbBytes(tokens, i + 1); // raw bytes only
                    else
                        byteOffset += 6;                           // one full instruction
                }
            }

            // ── Pass 2: emit bytecode ─────────────────────────────────────────
            // All labels are now resolved, so forward references work correctly.
            var bytes = new List<byte>(byteOffset);
            for (int i = 0; i < lines.Length; i++)
            {
                string stripped = StripComment(lines[i]).Trim();
                if (stripped.Length == 0 || stripped.EndsWith(':'))
                    continue; // blank lines and label-only lines produce no bytes

                string[] tokens = Tokenise(stripped);
                if (tokens.Length == 0)
                    continue;

                if (tokens[0].Equals("DB", StringComparison.OrdinalIgnoreCase))
                    bytes.AddRange(AssembleDb(tokens, i + 1));    // raw byte data
                else
                    bytes.AddRange(AssembleLine(tokens, labels, i + 1)); // 6-byte instruction
            }

            ByteCode = bytes.ToArray();
            return ByteCode;
        }

        // ── Line assembly ─────────────────────────────────────────────────────

        /// <summary>
        /// Assembles one non-DB source line into a 6-byte CacaVM instruction.
        /// </summary>
        /// <param name="tokens">Pre-tokenised operands; tokens[0] is the mnemonic.</param>
        /// <param name="labels">Label → byte-offset table from pass 1.</param>
        /// <param name="lineNum">1-based source line number for error messages.</param>
        private static byte[] AssembleLine(string[] tokens, Dictionary<string, int> labels, int lineNum)
        {
            // Resolve the mnemonic to its opcode byte.
            string mnemonic = tokens[0].ToUpperInvariant();
            byte opcode = Instruction.GetOpcode(mnemonic);
            if (opcode == 0 && !Instruction.IsMnemonic(mnemonic))
                throw new BuildException($"Unknown mnemonic '{tokens[0]}'.", lineNum);

            // Parse each operand, recording whether it is a register or a value.
            bool reg1 = false, reg2 = false;
            byte p1b = 0;  // param1 as an 8-bit byte (register ID or low byte of value)
            int  p1i = 0;  // param1 as a full 32-bit integer (used to propagate to param2 for single-operand jumps)
            int  p2i = 0;  // param2 as a 32-bit integer

            if (tokens.Length >= 2)
                (reg1, p1b, p1i) = ParseParam(tokens[1], labels, lineNum, isParam1: true);

            if (tokens.Length >= 3)
            {
                (reg2, _, p2i) = ParseParam(tokens[2], labels, lineNum, isParam1: false);
            }
            else if (!reg1)
            {
                // Single non-register operand (e.g. JMP 0x60, KEI 0x01, JMT label):
                // the VM reads the jump/interrupt target from param2 (bytes 2-5) for
                // value-mode instructions, so we must propagate the full integer value
                // through.  Without this, single-operand jumps always encode target 0.
                p2i = p1i;
            }

            // Determine the 2-bit addressing mode from the operand types.
            AddressMode mode = (reg1, reg2) switch
            {
                (true,  true)  => AddressMode.RegisterRegister, // both operands are registers
                (false, true)  => AddressMode.ValueRegister,    // immediate → register
                (true,  false) => AddressMode.RegisterValue,    // register  → value/address
                _              => AddressMode.ValueValue,       // immediate → immediate/address
            };

            return Instruction.Encode(opcode, mode, p1b, p2i);
        }

        /// <summary>
        /// Parses a single operand token and classifies it as a register or a value.
        /// </summary>
        /// <param name="token">The raw token string, e.g. "AL", "0x01", "'H'", "label".</param>
        /// <param name="labels">Resolved label table.</param>
        /// <param name="lineNum">Source line number for error reporting.</param>
        /// <param name="isParam1">
        ///   True when parsing the first operand.  Affects how a numeric literal is
        ///   packed: for param1 the value is stored as an 8-bit byte (instruction
        ///   encoding uses only 1 byte for param1); for param2 the full 32-bit value
        ///   is used.
        /// </param>
        /// <returns>
        ///   A tuple of (isRegister, byteValue, intValue) where:
        ///   <list type="bullet">
        ///     <item><description><c>isRegister</c> — true when the token names a register.</description></item>
        ///     <item><description><c>byteValue</c>  — the register ID byte (0xF0–0xFE) or the low 8 bits of a literal.</description></item>
        ///     <item><description><c>intValue</c>   — the full 32-bit value; used as param2 or a label address.</description></item>
        ///   </list>
        /// </returns>
        private static (bool isReg, byte byteVal, int intVal) ParseParam(
            string token, Dictionary<string, int> labels, int lineNum, bool isParam1)
        {
            // Register name? (e.g. AL, BL, X)
            if (Registers.IsRegister(token))
            {
                byte rb = Registers.GetByte(token);
                return (true, rb, rb);
            }

            // Single-character literal? (e.g. 'H', '\n', ' ')
            if (CharValue.Check(token))
            {
                byte cv = CharValue.Parse(token);
                return (false, cv, cv);
            }

            // Numeric literal — hex (0xFF) or decimal (255)?
            if (TryParseInteger(token, out int numVal))
            {
                // param1 is only 8 bits wide in the instruction format; param2 is 32 bits.
                byte bv = isParam1 ? (byte)(numVal & 0xFF) : (byte)0;
                return (false, bv, numVal);
            }

            // Label reference? (e.g. JMP main, MOV X, hello)
            if (labels.TryGetValue(token, out int addr))
                return (false, 0, addr);

            string paramNum = isParam1 ? "1" : "2";
            throw new BuildException($"Invalid value for parameter {paramNum}: '{token}'.", lineNum);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Strips everything from the first unquoted comment marker (<c>;</c> or <c>//</c>)
        /// to the end of the line, preserving content inside char literals and string literals.
        /// </summary>
        private static string StripComment(string line)
        {
            bool inChar   = false; // currently inside a '...' char literal
            bool inString = false; // currently inside a "..." string literal
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inString)
                {
                    // Inside a string literal: a backslash escapes the next character,
                    // so skip it to avoid treating \" as the closing quote.
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                }
                else if (inChar)
                {
                    // Inside a char literal: same escape-skip rule as string literals.
                    if (c == '\\') { i++; continue; }
                    if (c == '\'') inChar = false;
                }
                else
                {
                    // Outside any literal: check for literal-open or comment-start.
                    if (c == '"')  { inString = true;  continue; }
                    if (c == '\'') { inChar   = true;  continue; }
                    if (c == ';') return line[..i];
                    if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
                }
            }
            return line;
        }

        /// <summary>
        /// Splits a source line into tokens.  Commas and whitespace are treated as
        /// separators when they appear outside of a char or string literal.
        /// Char literals (<c>'X'</c>, <c>'\n'</c>, <c>' '</c>) and string literals
        /// (<c>"Hello"</c>) are kept intact as single tokens.
        /// </summary>
        private static string[] Tokenise(string line)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];

                if (c == '\'')
                {
                    // Char literal — accumulate everything up to and including the closing '.
                    sb.Append(c);
                    i++;
                    while (i < line.Length)
                    {
                        char ch = line[i];
                        sb.Append(ch);
                        i++;
                        if (ch == '\\' && i < line.Length)
                        {
                            // Backslash-escape: include the next character verbatim so
                            // '\'' and '\n' etc. don't confuse the closing-quote check.
                            sb.Append(line[i]);
                            i++;
                        }
                        else if (ch == '\'')
                        {
                            break; // closing single-quote found
                        }
                    }
                }
                else if (c == '"')
                {
                    // String literal — accumulate everything up to and including the closing ".
                    sb.Append(c);
                    i++;
                    while (i < line.Length)
                    {
                        char ch = line[i];
                        sb.Append(ch);
                        i++;
                        if (ch == '\\' && i < line.Length)
                        {
                            sb.Append(line[i]);
                            i++;
                        }
                        else if (ch == '"')
                        {
                            break; // closing double-quote found
                        }
                    }
                }
                else if (c == ' ' || c == '\t' || c == ',')
                {
                    // Separator — flush the accumulated token (if any) then skip the separator.
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }

            // Flush any remaining characters as the last token.
            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens.ToArray();
        }

        /// <summary>
        /// Tries to parse <paramref name="token"/> as a decimal or hexadecimal integer.
        /// Hex values must be prefixed with <c>0x</c> or <c>0X</c>.
        /// </summary>
        private static bool TryParseInteger(string token, out int value)
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(token[2..], System.Globalization.NumberStyles.HexNumber,
                    null, out value))
                    return true;
            }
            else if (int.TryParse(token, out value))
            {
                return true;
            }
            value = 0;
            return false;
        }

        // ── DB pseudo-instruction ─────────────────────────────────────────────

        /// <summary>
        /// Counts the number of raw bytes that a <c>DB</c> directive will emit.
        /// Called in pass 1 so that labels following a DB block are assigned the
        /// correct byte offset.
        /// </summary>
        /// <param name="tokens">Tokenised DB line; tokens[0] == "DB".</param>
        /// <param name="lineNum">Source line number for error messages.</param>
        private static int CountDbBytes(string[] tokens, int lineNum)
        {
            int count = 0;
            // Each token after "DB" is an independent byte-value specifier.
            for (int i = 1; i < tokens.Length; i++)
                count += DbTokenByteCount(tokens[i], lineNum);
            return count;
        }

        /// <summary>
        /// Emits the raw bytes for a <c>DB</c> directive as a <see cref="List{T}"/> of bytes.
        /// <para>
        /// Each token after "DB" may be one of:
        /// <list type="bullet">
        ///   <item><description><c>"Hello"</c>  — string literal: emits one byte per character (with escape processing)</description></item>
        ///   <item><description><c>'H'</c>       — char literal: emits a single byte</description></item>
        ///   <item><description><c>0x48</c>      — hex byte literal</description></item>
        ///   <item><description><c>72</c>        — decimal byte literal</description></item>
        /// </list>
        /// </para>
        /// Returns a plain <see cref="List{T}"/> — a concrete, universally portable
        /// collection type that does not rely on compiler-generated iterator state machines.
        /// </summary>
        /// <param name="tokens">Tokenised DB line; tokens[0] == "DB".</param>
        /// <param name="lineNum">Source line number for error messages.</param>
        private static List<byte> AssembleDb(string[] tokens, int lineNum)
        {
            var result = new List<byte>();

            for (int i = 1; i < tokens.Length; i++)
            {
                string tok = tokens[i];

                if (tok.StartsWith('"') && tok.EndsWith('"') && tok.Length >= 2)
                {
                    // String literal — emit one byte per character with escape handling.
                    string content = tok[1..^1]; // strip surrounding quotes
                    for (int j = 0; j < content.Length; j++)
                    {
                        if (content[j] == '\\' && j + 1 < content.Length)
                        {
                            // Backslash-escape sequence (e.g. \n, \t, \\)
                            result.Add(EscapeToChar(content[j + 1]));
                            j++; // skip the escape-sequence character
                        }
                        else
                        {
                            result.Add((byte)content[j]);
                        }
                    }
                }
                else if (CharValue.Check(tok))
                {
                    // Single-character literal such as 'H' or '\n'
                    result.Add(CharValue.Parse(tok));
                }
                else if (TryParseInteger(tok, out int numVal))
                {
                    // Hex or decimal byte value
                    result.Add((byte)(numVal & 0xFF));
                }
                else
                {
                    throw new BuildException($"DB: invalid byte value '{tok}'.", lineNum);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the number of raw bytes emitted by a single DB token.
        /// Used in pass 1 for byte-offset tracking.
        /// </summary>
        private static int DbTokenByteCount(string tok, int lineNum)
        {
            if (tok.StartsWith('"') && tok.EndsWith('"') && tok.Length >= 2)
            {
                // String literal: count characters, treating each escape sequence as 1 byte.
                string content = tok[1..^1];
                int count = 0;
                for (int j = 0; j < content.Length; j++)
                {
                    if (content[j] == '\\' && j + 1 < content.Length)
                        j++; // skip the escape-sequence character; it counts as one byte
                    count++;
                }
                return count;
            }

            // Char literal or numeric literal — always exactly 1 byte.
            if (CharValue.Check(tok) || TryParseInteger(tok, out _))
                return 1;

            throw new BuildException($"DB: invalid byte value '{tok}'.", lineNum);
        }

        /// <summary>
        /// Converts the character following a backslash in a string/char literal into
        /// the corresponding byte value (e.g. <c>'n'</c> → <c>0x0A</c>).
        /// Unrecognised escape characters are passed through as-is.
        /// </summary>
        private static byte EscapeToChar(char escape) => escape switch
        {
            'n'  => (byte)'\n',  // 0x0A  newline
            'r'  => (byte)'\r',  // 0x0D  carriage return
            't'  => (byte)'\t',  // 0x09  horizontal tab
            'a'  => (byte)'\a',  // 0x07  bell
            'b'  => (byte)'\b',  // 0x08  backspace
            'v'  => (byte)'\v',  // 0x0B  vertical tab
            '0'  => 0,           // 0x00  null terminator
            '\'' => (byte)'\'',  // 0x27  single quote
            '"'  => (byte)'"',   // 0x22  double quote
            '\\' => (byte)'\\',  // 0x5C  literal backslash
            _    => (byte)escape, // pass unrecognised escapes through unchanged
        };
    }
}
