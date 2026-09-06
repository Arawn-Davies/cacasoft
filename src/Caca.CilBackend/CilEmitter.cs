using System.Globalization;
using System.Text;
using Caca.Binding;
using Caca.Diagnostics;
using Caca.Syntax;

namespace Caca.CilBackend;

/// <summary>
/// Compiles a type-checked program to CIL assembly source, the language of the
/// CacaVM virtual machine (github.com/Arawn-Davies/CacaVM).
/// </summary>
/// <remarks>
/// The fourth backend, and the only one that lives outside <c>Caca.Compiler</c>:
/// it depends on the compiler's public AST and binding types, but the
/// compiler does not depend on it, or know it exists. <c>Caca.Compiler</c>
/// stays entirely CacaVM-ignorant; only <c>Caca.Cli</c> references both and
/// wires them together (see its <c>--target cacavm</c> handling). This is
/// deliberately unlike <c>CEmitter</c> and <c>IlEmitter</c>, which are
/// internal to the compiler assembly — the CacaVM target can be built,
/// tested, versioned and (if it ever needed to) dropped without touching the
/// core compiler at all.
///
/// It targets a real machine, not a text substitution: CIL
/// has five general-purpose registers (A/B/C are 16-bit; only X and Y are wide
/// enough for a 32-bit <c>int</c>), a single flat byte-addressable memory with
/// no indirect addressing wider than one byte at a time, and a hardware
/// push/pop stack that is also byte-at-a-time. Every non-trivial value this
/// emitter moves through memory or the stack is therefore packed and
/// unpacked four bytes at a time by hand (see <see cref="Push32"/>,
/// <see cref="Pop32"/>, <see cref="LoadSlot"/>, <see cref="StoreSlot"/>);
/// there is no frame-pointer register (X and Y are both needed free for
/// expression evaluation) — every local and parameter is instead addressed
/// relative to the CURRENT stack pointer, using an offset this emitter tracks
/// statically as it walks the tree (<see cref="_depth"/>), the same way a
/// stack-machine compiler tracks operand-stack depth. See
/// <c>docs/architecture.md</c> for why this shape was necessary.
///
/// v1 scope, deliberately: no <c>float</c> (CIL has no floating point unit or
/// opcodes at all — this is not an oversight to fix later so much as a
/// separate project), no <c>extern func</c> (no CLR under CIL), no
/// <c>read_string</c> (no string-input routine exists), and <c>string</c> may
/// only appear as the literal, direct operand of a <c>print</c> statement —
/// no string variables, parameters, return values, comparisons or
/// concatenation. Each is rejected with its own diagnostic, the same way
/// <see cref="CEmitter"/> rejects <c>extern func</c>. <c>read_int</c> IS
/// supported — see <see cref="EmitReadInt"/> — now that CacaVM's standard
/// library has an atoi (SWI 0x01, AL=0x04); it did not when this backend was
/// first written. Arrays do not exist in the source language at all, so they
/// are not a v1/later split; nothing here restricts them, there is simply
/// nothing to restrict.
///
/// It must agree with the other backends on every program the v1 subset
/// allows; the parity tests run its output through the actual CacaVM and
/// hold it to that.
/// </remarks>
public sealed class CilEmitter
{
    private readonly StringBuilder _text = new();
    private readonly IReadOnlyDictionary<string, FunctionSymbol> _functions;
    private readonly Dictionary<string, (string Label, int Length)> _stringLabels = new();
    private int _labelNumber;

    /// <summary>
    /// Bytes pushed onto the hardware stack, net, since the current function
    /// (or the top level) started executing. This is the only thing that
    /// makes stack-relative addressing of locals and parameters possible
    /// without a frame-pointer register: every <see cref="LoadSlot"/> and
    /// <see cref="StoreSlot"/> call bakes the CURRENT value of this counter
    /// into the offset it emits, so it must be exactly right at every point
    /// in the body — every construct that pushes something transiently
    /// (expression spilling, <c>Push32</c>) must pop it again before this
    /// counter is read for anything outside that construct.
    /// </summary>
    private int _depth;

    /// <summary>
    /// Total 4-byte slots reserved for the CURRENT function or top level —
    /// every named local plus, if it contains a <c>read_int</c>, the shared
    /// read buffer. Set once, right after the prologue reserves them, and
    /// read by both the auto-appended epilogue at the body's true end and by
    /// every early <c>return</c>'s epilogue: a single source of truth, rather
    /// than each recomputing it (and risking disagreeing) from <see cref="_slots"/>.
    /// </summary>
    private int _reservedSlotCount;

    /// <summary>
    /// Slot offsets for the current function's parameters and locals, keyed
    /// by name. A parameter's offset is zero or negative (it sits ABOVE the
    /// function's entry stack pointer, pushed by the caller); a local's is
    /// positive (reserved by this function's own prologue, below entry SP).
    /// A local's address at any point is <c>current SP + (_depth - offset)</c>;
    /// see <see cref="SlotAddressIntoX"/>.
    /// </summary>
    private readonly Dictionary<string, int> _slots = new();

    /// <summary>Where a <c>break</c> or <c>continue</c> jumps, innermost loop last.</summary>
    private readonly Stack<(string Break, string Continue)> _loops = new();

    private CilEmitter(IReadOnlyDictionary<string, FunctionSymbol> functions)
    {
        _functions = functions;
    }

    /// <summary>
    /// Renders <paramref name="unit"/> as CIL assembly source, or reports why
    /// it cannot be.
    /// </summary>
    /// <returns>The diagnostics; the program text is only valid when there are none.</returns>
    public static IReadOnlyList<Diagnostic> Emit(
        CompilationUnit unit,
        IReadOnlyDictionary<string, FunctionSymbol> functions,
        out string program)
    {
        var diagnostics = new DiagnosticBag();
        var emitter = new CilEmitter(functions);

        foreach (var function in functions.Values)
        {
            emitter.Validate(function, diagnostics);
        }

        emitter.Validate(unit.TopLevel, diagnostics);

        if (diagnostics.HasErrors)
        {
            program = string.Empty;
            return [.. diagnostics];
        }

        emitter.EmitProgram(unit);
        program = emitter._text.ToString();
        return [];
    }

    // ------------------------------------------------------------- validation

    /// <summary>
    /// Rejects everything the v1 subset does not cover, so the same program
    /// that fails here still runs on the interpreter, the IL backend and the
    /// C backend. Mirrors <see cref="CEmitter"/>'s extern rejection, widened
    /// to CIL's larger set of gaps.
    /// </summary>
    private void Validate(FunctionSymbol function, DiagnosticBag diagnostics)
    {
        if (function.IsExtern)
        {
            diagnostics.Report(
                DiagnosticCode.ExternNotAvailableInCacaVm,
                function.NameLocation,
                $"'{function.Name}' is bound to a .NET method, which CIL — CacaVM's assembly language, not " +
                ".NET's — has no CLR underneath to call");

            return;
        }

        if (function.ReturnType == CacaType.Float || function.Parameters.Any(p => p.Type == CacaType.Float))
        {
            diagnostics.Report(
                DiagnosticCode.FloatNotAvailableInCacaVm,
                function.NameLocation,
                $"'{function.Name}' uses 'float', which CIL has no instructions for");
        }

        if (function.ReturnType == CacaType.String || function.Parameters.Any(p => p.Type == CacaType.String))
        {
            diagnostics.Report(
                DiagnosticCode.StringNotLiteralInCacaVm,
                function.NameLocation,
                $"'{function.Name}' uses 'string' as a parameter or return type; on this target a string may " +
                "only appear as the literal, direct operand of 'print'");
        }

        Validate(function.Declaration.Body, diagnostics);
    }

    private void Validate(Statement statement, DiagnosticBag diagnostics)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    Validate(child, diagnostics);
                }

                break;

            case VariableDeclaration declaration:
                Validate(declaration.Initializer, diagnostics);
                break;

            case AssignmentStatement assignment:
                Validate(assignment.Value, diagnostics);
                break;

            case PrintStatement print:
                // The one place a string is allowed: printed directly, as a
                // literal. Everything else — including a literal float,
                // which EmitLiteral cannot handle either — goes through the
                // normal expression validation, which already rejects both.
                if (print.Expression is not LiteralExpression { LiteralType: CacaType.String })
                {
                    Validate(print.Expression, diagnostics);
                }

                break;

            case ReadStatement read when read.Type == CacaType.String:
                diagnostics.Report(
                    DiagnosticCode.ReadNotAvailableInCacaVm,
                    read.Location,
                    "this target has no string-input routine yet, so 'read_string' is not available " +
                    "(read_int is — CacaVM's atoi covers it)");

                break;

            case ForStatement loop:
                Validate(loop.From, diagnostics);
                Validate(loop.To, diagnostics);
                Validate(loop.Body, diagnostics);
                break;

            case IfStatement conditional:
                Validate(conditional.Condition, diagnostics);
                Validate(conditional.ThenBranch, diagnostics);

                if (conditional.ElseBranch is not null)
                {
                    Validate(conditional.ElseBranch, diagnostics);
                }

                break;

            case WhileStatement loop:
                Validate(loop.Condition, diagnostics);
                Validate(loop.Body, diagnostics);
                break;

            case ReturnStatement returned when returned.Value is not null:
                Validate(returned.Value, diagnostics);
                break;

            case CallStatement call:
                Validate(call.Call, diagnostics);
                break;
        }
    }

    /// <summary>A string may only ever be the direct operand of print — never stored, returned or passed.</summary>
    private static void ValidateNotBareString(Expression expression, DiagnosticBag diagnostics)
    {
        if (expression.EffectiveType == CacaType.String)
        {
            diagnostics.Report(
                DiagnosticCode.StringNotLiteralInCacaVm,
                expression.Location,
                "a string may only appear as the literal, direct operand of 'print' on this target");
        }
    }

    private void Validate(Expression expression, DiagnosticBag diagnostics)
    {
        if (expression.EffectiveType == CacaType.Float)
        {
            diagnostics.Report(
                DiagnosticCode.FloatNotAvailableInCacaVm,
                expression.Location,
                "'float' is used here, and CIL has no instructions for it");

            return;
        }

        ValidateNotBareString(expression, diagnostics);

        switch (expression)
        {
            case ParenthesizedExpression parenthesized:
                Validate(parenthesized.Expression, diagnostics);
                break;

            case UnaryExpression unary:
                Validate(unary.Operand, diagnostics);
                break;

            case BinaryExpression binary:
                Validate(binary.Left, diagnostics);
                Validate(binary.Right, diagnostics);
                break;

            case CallExpression call:
                foreach (var argument in call.Arguments)
                {
                    Validate(argument, diagnostics);
                }

                break;
        }
    }

    // ------------------------------------------------------------- structure

    private void EmitProgram(CompilationUnit unit)
    {
        _text.AppendLine("; Generated by the cacalang compiler. Assemble and run with the CacaVM CLI.");
        _text.AppendLine("JMP __cil_start");
        _text.AppendLine();

        EmitStringData(unit);
        EmitPrintIntRoutine();
        EmitPrintBoolRoutine();

        foreach (var function in _functions.Values)
        {
            Line($"func_{function.Name}:");
            EmitFunctionBody(function);
        }

        _text.AppendLine("__cil_start:");
        EmitBody(unit.TopLevel, isFunctionBody: false, locals: CollectLocals(unit.TopLevel), parameters: []);
        Line("KEI 0x02");
    }

    /// <summary>
    /// Walks every string literal print operand once, up front, and gives
    /// each distinct text its own label and byte length — <c>DB</c> data is
    /// part of the loaded program image and therefore read-only once running
    /// (the VM rejects any write at or below the end of the loaded program),
    /// which is exactly what a constant needs and is why this is the only
    /// place in the whole emitter that puts anything in memory directly
    /// rather than on the stack.
    /// </summary>
    private void EmitStringData(CompilationUnit unit)
    {
        var literals = new List<string>();
        CollectStringLiterals(unit.TopLevel, literals);

        foreach (var function in _functions.Values)
        {
            CollectStringLiterals(function.Declaration.Body, literals);
        }

        foreach (var text in literals.Distinct())
        {
            if (_stringLabels.ContainsKey(text))
            {
                continue;
            }

            var label = $"__str_{_stringLabels.Count}";
            _stringLabels[text] = (label, Encoding.UTF8.GetByteCount(text));
            Line($"{label}:");
            Line($"DB {DbStringLiteral(text)}");
        }

        // Fixed labels the runtime print helpers need, regardless of whether
        // the program itself uses any string literals at all.
        Line("__str_true:"); Line($"DB {DbStringLiteral("true")}");
        Line("__str_false:"); Line($"DB {DbStringLiteral("false")}");
        Line("__str_minvalue:"); Line($"DB {DbStringLiteral(MinValueText)}");
    }

    /// <summary>The one value <c>0 - x</c> cannot negate correctly, printed as a fixed literal instead.</summary>
    private static readonly string MinValueText = int.MinValue.ToString(CultureInfo.InvariantCulture);

    private static void CollectStringLiterals(Statement statement, List<string> literals)
    {
        if (statement is PrintStatement { Expression: LiteralExpression { LiteralType: CacaType.String } literal })
        {
            literals.Add(literal.StringValue);
        }

        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    CollectStringLiterals(child, literals);
                }

                break;

            case ForStatement loop:
                CollectStringLiterals(loop.Body, literals);
                break;

            case IfStatement conditional:
                CollectStringLiterals(conditional.ThenBranch, literals);

                if (conditional.ElseBranch is not null)
                {
                    CollectStringLiterals(conditional.ElseBranch, literals);
                }

                break;

            case WhileStatement loop:
                CollectStringLiterals(loop.Body, literals);
                break;
        }
    }

    /// <summary>A DB operand list: the assembler's own string-literal form, byte-exact via UTF-8.</summary>
    private static string DbStringLiteral(string value)
    {
        // The assembler's DB string literal writes each character as one byte;
        // going through UTF-8 first keeps this exact for non-ASCII text and
        // matches how the other backends encode a string's bytes.
        var escaped = new StringBuilder();

        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            escaped.Append(b switch
            {
                (byte)'"' => "\\\"",
                (byte)'\\' => "\\\\",
                _ => ((char)b).ToString(),
            });
        }

        return $"\"{escaped}\"";
    }

    /// <summary>Every local a function body declares, in the order <see cref="EmitFunctionBody"/> reserves slots.</summary>
    /// <remarks>Mirrors <see cref="CEmitter"/>'s CollectLocals, plus one synthetic slot per <c>for</c> loop's bound.</remarks>
    private static void CollectLocals(Statement statement, List<(string Name, int Width)> locals)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    CollectLocals(child, locals);
                }

                break;

            case VariableDeclaration declaration:
                locals.Add((declaration.Name, 1));
                break;

            case ForStatement loop:
                if (loop.DeclaresVariable)
                {
                    locals.Add((loop.Name, 1));
                }

                locals.Add((ForBoundSlot(loop), 1));
                CollectLocals(loop.Body, locals);
                break;

            case IfStatement conditional:
                CollectLocals(conditional.ThenBranch, locals);

                if (conditional.ElseBranch is not null)
                {
                    CollectLocals(conditional.ElseBranch, locals);
                }

                break;

            case WhileStatement loop:
                CollectLocals(loop.Body, locals);
                break;
        }
    }

    /// <summary>
    /// Every local this body needs slots for, plus — if it contains at least
    /// one <c>read_int</c> — one <see cref="ReadBufferWidth"/>-slot scratch
    /// buffer shared by all of them (reads happen one at a time, each fully
    /// consumed before the next, so there is nothing to gain from a separate
    /// buffer per call site).
    /// </summary>
    private static List<(string Name, int Width)> CollectLocals(Statement body)
    {
        var locals = new List<(string Name, int Width)>();
        CollectLocals(body, locals);

        if (ContainsReadInt(body))
        {
            locals.Add((ReadBufferSlot, ReadBufferWidth));
        }

        return locals;
    }

    private static bool ContainsReadInt(Statement statement) => statement switch
    {
        ReadStatement { Type: CacaType.Int } => true,
        BlockStatement block => block.Statements.Any(ContainsReadInt),
        ForStatement loop => ContainsReadInt(loop.Body),
        IfStatement conditional => ContainsReadInt(conditional.ThenBranch)
            || (conditional.ElseBranch is not null && ContainsReadInt(conditional.ElseBranch)),
        WhileStatement loop => ContainsReadInt(loop.Body),
        _ => false,
    };

    /// <summary>The hidden local a for-loop's upper bound is evaluated into once, up front.</summary>
    private static string ForBoundSlot(ForStatement loop) => $"<for@{loop.Location.Start}>bound";

    /// <summary>
    /// The scratch buffer <c>read_int</c> reads a line into before parsing it.
    /// 256 bytes (64 four-byte slots) is generous for any realistic integer
    /// input; <see cref="EmitReadInt"/> clamps the read length to the buffer's
    /// size before null-terminating it, so a pathological line longer than
    /// this is truncated, not a memory-safety problem — the standard
    /// library's own KEI 0x01/AL=0x04 has no bounds check of its own against
    /// a destination buffer's size, so this clamp is this emitter's
    /// responsibility, not something to assume happens downstream.
    /// </summary>
    private const string ReadBufferSlot = "<readbuf>";

    private const int ReadBufferWidth = 64;

    private void EmitFunctionBody(FunctionSymbol function)
    {
        var locals = CollectLocals(function.Declaration.Body);
        EmitBody(function.Declaration.Body, isFunctionBody: true, locals, function.Parameters.Select(p => p.Name).ToList());
    }

    /// <summary>
    /// Emits a function body, or the top level, as a prologue (reserve locals),
    /// the statements, and an epilogue (deallocate locals, then RET or halt).
    /// </summary>
    private void EmitBody(
        BlockStatement body, bool isFunctionBody, List<(string Name, int Width)> locals, IReadOnlyList<string> parameters)
    {
        _depth = 0;
        _slots.Clear();

        for (var i = 0; i < parameters.Count; i++)
        {
            // Parameter i (1-indexed) sits at entry_SP + 4*(i-1); see the
            // offset formula in SlotAddressIntoX. Expressed the same way as a
            // local's offset (current_SP + _depth - offset), a parameter's
            // offset is -4*(i-1).
            _slots[parameters[i]] = -4 * i;
        }

        foreach (var (local, width) in locals)
        {
            for (var i = 0; i < width; i++)
            {
                _depth += 4;
                Line("PSH 0"); Line("PSH 0"); Line("PSH 0"); Line("PSH 0");
            }

            // A multi-slot buffer's recorded address is that of its LAST
            // (lowest-address) 4-byte unit, i.e. byte 0 of the buffer — every
            // later unit sits at a higher address, exactly the ascending
            // byte-0..byte-N layout EmitReadInt needs.
            _slots[local] = _depth;
        }

        _reservedSlotCount = locals.Sum(l => l.Width);
        EmitStatements(body);

        if (isFunctionBody)
        {
            EmitEpilogue(_reservedSlotCount);
            Line("RET");
        }
    }

    /// <summary>Pops every reserved local, in one instruction per byte — the exact inverse of the prologue.</summary>
    private void EmitEpilogue(int slotCount)
    {
        for (var i = 0; i < slotCount; i++)
        {
            Line("POP X"); Line("POP X"); Line("POP X"); Line("POP X");
        }

        _depth -= 4 * slotCount;
    }

    // -------------------------------------------------------------- statements

    private void EmitStatements(BlockStatement block)
    {
        foreach (var statement in block.Statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                EmitStatements(block);
                break;

            case VariableDeclaration declaration:
                EmitExpression(declaration.Initializer);
                StoreSlot(declaration.Name);
                break;

            case AssignmentStatement assignment:
                EmitExpression(assignment.Value);
                StoreSlot(assignment.Name);
                break;

            case PrintStatement print:
                EmitPrint(print.Expression);
                break;

            case IfStatement conditional:
                EmitIf(conditional);
                break;

            case WhileStatement loop:
                EmitWhile(loop);
                break;

            case ForStatement loop:
                EmitFor(loop);
                break;

            case ReturnStatement returned:
                if (returned.Value is not null)
                {
                    EmitExpression(returned.Value);
                }

                // A return from inside a loop or a nested block still has to
                // unwind every local this function reserved before RET —
                // but RET diverts control away entirely, while _depth is
                // compile-time state shared with whatever this emitter
                // visits next (typically an enclosing if's other branch, or
                // the code after it). EmitEpilogue's decrement must not leak
                // into that: at runtime, only one branch of an if ever
                // executes, so the code textually after this return has to
                // see the same _depth it would have seen had this return not
                // been here at all — restore it once the RET is emitted.
                var depthBeforeReturn = _depth;
                EmitEpilogue(_reservedSlotCount);
                Line("RET");
                _depth = depthBeforeReturn;
                break;

            case CallStatement call:
                EmitExpression(call.Call);
                break;

            case ReadStatement read:
                EmitReadInt(read.Name);
                break;

            case BreakStatement:
                Line($"JMP {_loops.Peek().Break}");
                break;

            case ContinueStatement:
                Line($"JMP {_loops.Peek().Continue}");
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(statement), statement, $"unhandled statement {statement.GetType().Name}");
        }
    }

    private void EmitPrint(Expression expression)
    {
        if (expression is LiteralExpression { LiteralType: CacaType.String } literal)
        {
            var (label, length) = _stringLabels[literal.StringValue];
            Line("MOV AL, 0x02");
            Line($"MOV X, {label}");
            Line($"MOV B, {length}");
            Line("KEI 0x01");
            EmitNewline();
            return;
        }

        EmitExpression(expression);

        if (expression.EffectiveType == CacaType.Bool)
        {
            Line("CLL __print_bool");
        }
        else
        {
            Line("CLL __print_int");
        }
    }

    /// <summary>
    /// <c>read_int</c>: reads a line into the body's shared read buffer (see
    /// <see cref="ReadBufferSlot"/>), clamps the REPORTED length to the
    /// buffer's actual size before trusting it for anything, null-terminates
    /// at that (possibly clamped) point, then hands the buffer to the
    /// standard library's own atoi (SWI 0x01, AL=0x04) and stores the result.
    ///
    /// The clamp bounds where THIS code writes the terminator; it does not
    /// bound what KEI 0x01/AL=0x04 itself already wrote before this code ever
    /// ran. That interrupt takes no maximum-length parameter at all — it
    /// writes every byte of whatever line the console produced, however long,
    /// starting at X — so a line longer than the buffer has already
    /// overwritten whatever memory follows it by the time control reaches
    /// here. This is a real, open gap inherited from the standard library,
    /// not one this emitter can close from CIL: a genuine fix needs
    /// KEI 0x01/AL=0x04 itself to take a maximum length and stop there.
    /// </summary>
    private void EmitReadInt(string name)
    {
        SlotAddressIntoX(ReadBufferSlot);
        Line("MOV AL, 0x04");
        Line("KEI 0x01"); // reads a line into [X, X+B); B = bytes actually read — unbounded, see above

        Line("MOV Y, B");
        Line($"MOV X, {ReadBufferWidth * 4 - 1}");
        Line("TMT Y, X");

        var clampLabel = MakeLabel("readint_clamp");
        Line($"JMF {clampLabel}");
        Line($"MOV Y, {ReadBufferWidth * 4 - 1}");
        Line($"{clampLabel}:");

        SlotAddressIntoX(ReadBufferSlot); // recompute: clobbered by the clamp check above
        Line("ADD X, Y");
        Line("MOM 0, X"); // null-terminate, always within the buffer's bounds

        SlotAddressIntoX(ReadBufferSlot);
        Line("MOV AL, 0x04");
        Line("SWI 0x01"); // Y = parsed integer
        StoreSlot(name);
    }

    private void EmitIf(IfStatement conditional)
    {
        var elseLabel = MakeLabel("else");
        var endLabel = MakeLabel("endif");

        EmitExpression(conditional.Condition);
        Line("MOV X, 0");
        Line("TEQ Y, X");
        Line($"JMT {(conditional.ElseBranch is null ? endLabel : elseLabel)}");

        EmitStatement(conditional.ThenBranch);

        if (conditional.ElseBranch is null)
        {
            Line($"{endLabel}:");
            return;
        }

        Line($"JMP {endLabel}");
        Line($"{elseLabel}:");
        EmitStatement(conditional.ElseBranch);
        Line($"{endLabel}:");
    }

    private void EmitWhile(WhileStatement loop)
    {
        var startLabel = MakeLabel("while");
        var endLabel = MakeLabel("endwhile");

        Line($"{startLabel}:");
        EmitExpression(loop.Condition);
        Line("MOV X, 0");
        Line("TEQ Y, X");
        Line($"JMT {endLabel}");

        _loops.Push((endLabel, startLabel));
        EmitStatement(loop.Body);
        _loops.Pop();

        Line($"JMP {startLabel}");
        Line($"{endLabel}:");
    }

    /// <summary>
    /// Both bounds inclusive, the upper bound evaluated once, test before
    /// increment so the largest int is still reached — the exact semantics
    /// <see cref="CEmitter"/> documents and the parity tests hold every
    /// backend to.
    /// </summary>
    private void EmitFor(ForStatement loop)
    {
        var startLabel = MakeLabel("for");
        var tailLabel = MakeLabel("fornext");
        var endLabel = MakeLabel("endfor");
        var boundSlot = ForBoundSlot(loop);

        EmitExpression(loop.From);
        StoreSlot(loop.Name);
        EmitExpression(loop.To);
        StoreSlot(boundSlot);

        // Empty-range guard: skip the loop entirely if from > to. LoadSlot
        // uses X as its own scratch register internally (see
        // SlotAddressIntoX), so "MOV X,Y" followed directly by a second
        // LoadSlot call does NOT preserve the first value — X gets clobbered
        // by the second call's own address arithmetic before TMT ever reads
        // it. Save the first value on the hardware stack instead, same as
        // EmitBinary does for its two operands.
        LoadSlot(loop.Name);
        Push32("Y");
        LoadSlot(boundSlot);
        Pop32("X");
        Line("TMT X, Y");
        Line($"JMT {endLabel}");

        Line($"{startLabel}:");
        _loops.Push((endLabel, tailLabel));
        EmitStatement(loop.Body);
        _loops.Pop();

        Line($"{tailLabel}:");
        LoadSlot(loop.Name);
        Push32("Y");
        LoadSlot(boundSlot);
        Pop32("X");
        // Break when counter >= bound, i.e. NOT(counter < bound) — jump on
        // the flag being false, not true.
        Line("TLT X, Y");
        Line($"JMF {endLabel}");
        LoadSlot(loop.Name);
        Line("ADD Y, 1");
        StoreSlot(loop.Name);
        Line($"JMP {startLabel}");
        Line($"{endLabel}:");
    }

    // ------------------------------------------------------------- expressions

    /// <summary>Emits code that leaves <paramref name="expression"/>'s value in Y. Net stack depth unchanged.</summary>
    private void EmitExpression(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                EmitLiteral(literal);
                break;

            case VariableExpression variable:
                LoadSlot(variable.Name);
                break;

            case ParenthesizedExpression parenthesized:
                EmitExpression(parenthesized.Expression);
                break;

            case UnaryExpression unary:
                EmitUnary(unary);
                break;

            case BinaryExpression binary:
                EmitBinary(binary);
                break;

            case CallExpression call:
                EmitCall(call);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(expression), expression, $"unhandled expression {expression.GetType().Name}");
        }
    }

    private void EmitLiteral(LiteralExpression literal)
    {
        switch (literal.LiteralType)
        {
            case CacaType.Int:
                Line($"MOV Y, {literal.IntValue.ToString(CultureInfo.InvariantCulture)}");
                break;

            case CacaType.Bool:
                Line($"MOV Y, {(literal.BoolValue ? 1 : 0)}");
                break;

            default:
                throw new InvalidOperationException(
                    "a string literal must be handled by its statement, never reached as a bare expression");
        }
    }

    private void EmitUnary(UnaryExpression unary)
    {
        EmitExpression(unary.Operand);

        switch (unary.Operator)
        {
            case UnaryOperator.Identity:
                break;

            case UnaryOperator.Negate:
                // 0 - y wraps the same way CEmitter's caca_neg does for
                // int.MinValue: unchecked subtraction, not negation.
                Line("MOV X, 0");
                Line("SUB X, Y");
                Line("MOV Y, X");
                break;

            case UnaryOperator.Not:
                Line("MOV X, 0");
                Line("TEQ Y, X");
                MaterializeFlag(whenTrue: 1, whenFalse: 0);
                break;
        }
    }

    private void EmitBinary(BinaryExpression binary)
    {
        if (binary.Operator == BinaryOperator.LogicalAnd)
        {
            EmitShortCircuit(binary, stopValue: 0);
            return;
        }

        if (binary.Operator == BinaryOperator.LogicalOr)
        {
            EmitShortCircuit(binary, stopValue: 1);
            return;
        }

        EmitExpression(binary.Left);
        Push32("Y");
        EmitExpression(binary.Right);
        Pop32("X");

        // Now X = left, Y = right (see Push32/Pop32).
        switch (binary.Operator)
        {
            case BinaryOperator.Add:
                Line("ADD X, Y"); Line("MOV Y, X");
                break;

            case BinaryOperator.Subtract:
                Line("SUB X, Y"); Line("MOV Y, X");
                break;

            case BinaryOperator.Multiply:
                Line("MUL X, Y"); Line("MOV Y, X");
                break;

            case BinaryOperator.Divide:
                Line("DIV X, Y"); Line("MOV Y, X");
                break;

            case BinaryOperator.Modulo:
                EmitModulo();
                break;

            case BinaryOperator.Equal:
                Line("TEQ X, Y"); MaterializeFlag(1, 0);
                break;

            case BinaryOperator.NotEqual:
                Line("TNE X, Y"); MaterializeFlag(1, 0);
                break;

            case BinaryOperator.Less:
                Line("TLT X, Y"); MaterializeFlag(1, 0);
                break;

            case BinaryOperator.LessOrEqual:
                // a <= b  ==  !(a > b)
                Line("TMT X, Y"); MaterializeFlag(0, 1);
                break;

            case BinaryOperator.Greater:
                Line("TMT X, Y"); MaterializeFlag(1, 0);
                break;

            case BinaryOperator.GreaterOrEqual:
                // a >= b  ==  !(a < b)
                Line("TLT X, Y"); MaterializeFlag(0, 1);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "unhandled binary operator");
        }
    }

    /// <summary>
    /// Computes X = X % Y (X = left, Y = right) with no MOD opcode to lean on:
    /// remainder = left - (left / right) * right, truncating division, exactly
    /// matching C#'s <c>%</c> since both sides are plain C# int division
    /// underneath. The awkward part is that DIV/MUL destroy their destination
    /// register, and 'left' is needed again, fresh, after both — so it is
    /// captured non-destructively across four otherwise-unused 8-bit
    /// registers (AL/AH/BL/BH) first, and reconstructed from them twice: once
    /// to feed the division, once more for the final subtraction. 'right'
    /// (Y) needs no such care, since it is only ever DIV/MUL's source, never
    /// their destination, so it survives untouched throughout.
    /// </summary>
    private void EmitModulo()
    {
        Line("MOV AL, X"); Line("SHR X, 8");
        Line("MOV AH, X"); Line("SHR X, 8");
        Line("MOV BL, X"); Line("SHR X, 8");
        Line("MOV BH, X");

        Line("MOV X, BH"); Line("SHL X, 8"); Line("BOR X, BL");
        Line("SHL X, 8"); Line("BOR X, AH");
        Line("SHL X, 8"); Line("BOR X, AL");

        Line("DIV X, Y");
        Line("MUL X, Y");

        Line("MOV Y, BH"); Line("SHL Y, 8"); Line("BOR Y, BL");
        Line("SHL Y, 8"); Line("BOR Y, AH");
        Line("SHL Y, 8"); Line("BOR Y, AL");
        Line("SUB Y, X");
    }

    /// <summary>
    /// After a TEQ/TNE/TLT/TMT, turns the logic flag into a 0/1 value in Y.
    /// </summary>
    private void MaterializeFlag(int whenTrue, int whenFalse)
    {
        var setLabel = MakeLabel("flagtrue");
        var endLabel = MakeLabel("flagend");

        Line($"JMT {setLabel}");
        Line($"MOV Y, {whenFalse}");
        Line($"JMP {endLabel}");
        Line($"{setLabel}:");
        Line($"MOV Y, {whenTrue}");
        Line($"{endLabel}:");
    }

    private void EmitShortCircuit(BinaryExpression binary, int stopValue)
    {
        var stopLabel = MakeLabel(stopValue == 0 ? "andfalse" : "ortrue");
        var endLabel = MakeLabel("shortend");

        EmitExpression(binary.Left);
        Line("MOV X, " + stopValue);
        Line("TEQ Y, X");
        Line($"JMT {stopLabel}");
        EmitExpression(binary.Right);
        Line($"JMP {endLabel}");
        Line($"{stopLabel}:");
        Line($"MOV Y, {stopValue}");
        Line($"{endLabel}:");
    }

    private void EmitCall(CallExpression call)
    {
        // Pushed in reverse so parameter i (1-indexed) ends up at entry_SP +
        // 4*(i-1) — see the offset comment in EmitBody. PushArgument, not
        // Push32: the callee reads its parameters through LoadSlot, which
        // expects byte 0 (LSB) at the lowest address, and Push32's own byte
        // order (LSB pushed first, ending up at the HIGHEST of the four
        // addresses) is the opposite of that — fine for Push32's other,
        // self-contained uses where the same code pops it back, wrong the
        // moment a different piece of code (LoadSlot) does the reading.
        for (var i = call.Arguments.Count - 1; i >= 0; i--)
        {
            EmitExpression(call.Arguments[i]);
            PushArgument("Y");
        }

        Line($"CLL func_{call.Name}");

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            Line("POP X"); Line("POP X"); Line("POP X"); Line("POP X");
            _depth -= 4;
        }

        // The callee leaves its result in Y before RET; a void call's Y is
        // simply never read by the caller.
    }

    // ------------------------------------------------------- slots and stack

    /// <summary>
    /// Computes a slot's current address into X. A parameter's offset is
    /// zero or negative; a local's is positive — either way the address is
    /// <c>current SP + (_depth - offset)</c>, because <c>current SP ==
    /// entry_SP - _depth</c> and a slot's fixed address is <c>entry_SP -
    /// offset</c> (see the class remarks).
    /// </summary>
    private void SlotAddressIntoX(string name)
    {
        var offset = _slots[name];
        Line("MOV X, SP");

        var delta = _depth - offset;

        if (delta > 0)
        {
            Line($"ADD X, {delta}");
        }
        else if (delta < 0)
        {
            Line($"SUB X, {-delta}");
        }
    }

    /// <summary>Loads a 32-bit slot's value into Y. Clobbers X.</summary>
    private void LoadSlot(string name)
    {
        Line("MOV Y, 0");

        for (var k = 3; k >= 0; k--)
        {
            SlotAddressIntoX(name);

            if (k > 0)
            {
                Line($"ADD X, {k}");
            }

            Line("MOE X, X");
            Line("SHL Y, 8");
            Line("BOR Y, X");
        }
    }

    /// <summary>Stores Y (destroyed) into a 32-bit slot. Clobbers X and Y.</summary>
    private void StoreSlot(string name)
    {
        for (var k = 0; k < 4; k++)
        {
            SlotAddressIntoX(name);

            if (k > 0)
            {
                Line($"ADD X, {k}");
            }

            Line("MOM Y, X");

            if (k < 3)
            {
                Line("SHR Y, 8");
            }
        }
    }

    /// <summary>
    /// Pushes <paramref name="reg"/>'s four bytes for a call argument, byte 0
    /// (LSB) ending up at the LOWEST of the four addresses — the layout
    /// <see cref="LoadSlot"/> and <see cref="StoreSlot"/> expect, since the
    /// callee reads its parameters through them, not through <see cref="Pop32"/>.
    /// Captures the bytes into AL/AH/BL/BH first (otherwise-unused 8-bit
    /// registers) so they can be pushed in the opposite order from how they
    /// were extracted; <paramref name="reg"/> is destroyed. <see cref="_depth"/> grows by 4.
    /// </summary>
    private void PushArgument(string reg)
    {
        Line($"MOV AL, {reg}"); Line($"SHR {reg}, 8");
        Line($"MOV AH, {reg}"); Line($"SHR {reg}, 8");
        Line($"MOV BL, {reg}"); Line($"SHR {reg}, 8");
        Line($"MOV BH, {reg}");
        Line("PSH BH"); Line("PSH BL"); Line("PSH AH"); Line("PSH AL");
        _depth += 4;
    }

    /// <summary>Pushes <paramref name="reg"/>'s four bytes (LSB first). <paramref name="reg"/> is destroyed; <see cref="_depth"/> grows by 4.</summary>
    private void Push32(string reg)
    {
        Line($"PSH {reg}"); Line($"SHR {reg}, 8");
        Line($"PSH {reg}"); Line($"SHR {reg}, 8");
        Line($"PSH {reg}"); Line($"SHR {reg}, 8");
        Line($"PSH {reg}");
        _depth += 4;
    }

    /// <summary>
    /// Pops four bytes into <paramref name="reg"/> (MSB popped first, matching
    /// <see cref="Push32"/>'s push order). Reconstructs using BL — an 8-bit
    /// register, otherwise unused here — as the byte-sized scratch holder,
    /// specifically so whichever of X/Y is NOT <paramref name="reg"/> is left
    /// completely untouched (unlike popping through the other 32-bit
    /// register, which would have to clobber it to hold each freshly-popped
    /// byte before shifting it in). <see cref="_depth"/> shrinks by 4.
    /// </summary>
    private void Pop32(string reg)
    {
        Line($"POP {reg}");
        Line("POP BL"); Line($"SHL {reg}, 8"); Line($"BOR {reg}, BL");
        Line("POP BL"); Line($"SHL {reg}, 8"); Line($"BOR {reg}, BL");
        Line("POP BL"); Line($"SHL {reg}, 8"); Line($"BOR {reg}, BL");
        _depth -= 4;
    }

    // --------------------------------------------------------- runtime helpers

    /// <summary>
    /// Prints the signed 32-bit value in Y as decimal. Called like any
    /// function (CLL/RET) but takes its argument in Y directly rather than
    /// on the stack — it is a runtime primitive, not a user-callable one, so
    /// it needs no slot table of its own.
    /// </summary>
    private void EmitPrintIntRoutine()
    {
        Line("__print_int:");
        Line("MOV X, -2147483648");
        Line("TEQ Y, X");
        var minValueLabel = MakeLabel("print_int_minvalue");
        Line($"JMT {minValueLabel}");

        Line("MOV X, 0");
        Line("TLT Y, X");
        var positiveLabel = MakeLabel("print_int_positive");
        Line($"JMF {positiveLabel}");

        Line("MOV AL, 0x01");
        Line("MOV AH, '-'");
        Line("KEI 0x01");
        Line("MOV X, 0");
        Line("SUB X, Y");
        Line("MOV Y, X");

        Line($"{positiveLabel}:");
        Line("PSH 0xFF"); // sentinel: not a valid digit byte (digits are 0x30-0x39)

        var digitLoop = MakeLabel("print_int_digit");
        Line($"{digitLoop}:");
        Line("MOV X, Y"); Line("DIV X, 10");
        Push32("X"); // save the quotient; X is left garbage, restored via Pop32 below
        Line("MOV X, Y"); Line("DIV X, 10"); Line("MUL X, 10"); // recompute (Y/10)*10
        Line("SUB Y, X"); // Y = remainder (0-9)
        Line("ADD Y, '0'");
        // Restore the quotient (X) BEFORE pushing the digit (Y): the stack is
        // LIFO, and the digit must sit permanently on top of the sentinel and
        // any earlier digits for the print loop below to find later — it
        // cannot be pushed while the quotient's own save is still live on top
        // of the stack, or popping the quotient back would retrieve the
        // digit byte plus three bytes of the quotient instead.
        Pop32("X"); // X = quotient; Y (the digit) untouched
        Line("PSH Y");
        Line("MOV Y, X");
        Line("MOV X, 0");
        Line("TNE Y, X");
        Line($"JMT {digitLoop}");

        var printLoop = MakeLabel("print_int_print");
        var doneLabel = MakeLabel("print_int_done");
        Line($"{printLoop}:");
        Line("POP AH");
        Line("MOV X, 0xFF");
        Line("TEQ AH, X");
        Line($"JMT {doneLabel}");
        Line("MOV AL, 0x01");
        Line("KEI 0x01");
        Line($"JMP {printLoop}");

        Line($"{doneLabel}:");
        EmitNewline();
        Line("RET");

        Line($"{minValueLabel}:");
        Line("MOV AL, 0x02");
        Line("MOV X, __str_minvalue");
        Line($"MOV B, {MinValueText.Length}");
        Line("KEI 0x01");
        EmitNewline();
        Line("RET");
    }

    private void EmitPrintBoolRoutine()
    {
        Line("__print_bool:");
        Line("MOV X, 0");
        Line("TEQ Y, X");
        var falseLabel = MakeLabel("print_bool_false");
        Line($"JMT {falseLabel}");
        Line("MOV AL, 0x02");
        Line("MOV X, __str_true");
        Line("MOV B, 4");
        Line("KEI 0x01");
        EmitNewline();
        Line("RET");
        Line($"{falseLabel}:");
        Line("MOV AL, 0x02");
        Line("MOV X, __str_false");
        Line("MOV B, 5");
        Line("KEI 0x01");
        EmitNewline();
        Line("RET");
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Every <c>print</c> ends its output with a newline, matching every other backend.</summary>
    private void EmitNewline()
    {
        Line("MOV AL, 0x01");
        Line("MOV AH, 0x0A");
        Line("KEI 0x01");
    }

    private string MakeLabel(string hint) => $"__{hint}_{_labelNumber++}";

    private void Line(string text) => _text.AppendLine(text);
}
