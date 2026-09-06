import Foundation

/// Built-in example sources — ported verbatim from Caca.VM.Studio/MainForm.cs
/// (ExampleHelloWorld / ExampleCalculator), so the same programs are available
/// from File > Load Example in both IDEs.
enum Example: String, CaseIterable, Identifiable {
    case helloWorld = "Hello World"
    case calculator = "Calculator"

    var id: String { rawValue }

    var source: String {
        switch self {
        case .helloWorld: return Example.helloWorldSource
        case .calculator: return Example.calculatorSource
        }
    }

    private static let helloWorldSource = """
    ; hello_world_db.cil
    ; Demonstrates the DB pseudo-instruction (like the x86 `db` directive).
    ; The string "Hello, World" is defined inline in the source; the program
    ; then prints it using KEI 0x01 in write-string mode.

            JMP     main            ; skip over data section

    ; ── Data section ──────────────────────────────────────────────────────────────
    hello:
            DB      "Hello, World", 0x0A, 0x00   ; 14 bytes: text + newline + null

    ; ── Code section ──────────────────────────────────────────────────────────────
    main:
            MOV     AL, 0x02        ; KEI 0x01 mode: write string from memory
            MOV     X, hello        ; X = address of string data
            MOV     BL, 13          ; BL = length of string (13 bytes)
            KEI     0x01            ; write string to stdout
            KEI     0x02            ; halt

    """

    private static let calculatorSource = """
    ; calculator.cil
    ; Simple integer calculator demonstrating CIL arithmetic.
    ; Each block prints   A op B = result
    ; using KEI 0x01 (write-char / write-integer) interrupts.
    ;
    ; KEI 0x01 modes used:
    ;   AL = 0x01  write a single character (value in AH)
    ;   AL = 0x05  write register B as a decimal integer

    ; ── 3 + 4 = 7 ────────────────────────────────────────────────────────────────
            MOV     AL, 0x01
            MOV     AH, '3'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '+'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '4'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '='
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     BL, 3
            ADD     BL, 4           ; BL = 7
            MOV     AL, 0x05
            KEI     0x01            ; print "7"
            MOV     AL, 0x01
            MOV     AH, 0x0A        ; newline
            KEI     0x01

    ; ── 10 - 3 = 7 ───────────────────────────────────────────────────────────────
            MOV     AH, '1'
            KEI     0x01
            MOV     AH, '0'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '-'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '3'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '='
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     BL, 10
            SUB     BL, 3           ; BL = 7
            MOV     AL, 0x05
            KEI     0x01            ; print "7"
            MOV     AL, 0x01
            MOV     AH, 0x0A
            KEI     0x01

    ; ── 6 * 7 = 42 ───────────────────────────────────────────────────────────────
            MOV     AH, '6'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '*'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '7'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '='
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     BL, 6
            MUL     BL, 7           ; BL = 42
            MOV     AL, 0x05
            KEI     0x01            ; print "42"
            MOV     AL, 0x01
            MOV     AH, 0x0A
            KEI     0x01

    ; ── 20 / 4 = 5 ───────────────────────────────────────────────────────────────
            MOV     AH, '2'
            KEI     0x01
            MOV     AH, '0'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '/'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '4'
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     AH, '='
            KEI     0x01
            MOV     AH, ' '
            KEI     0x01
            MOV     BL, 20
            DIV     BL, 4           ; BL = 5
            MOV     AL, 0x05
            KEI     0x01            ; print "5"
            MOV     AL, 0x01
            MOV     AH, 0x0A
            KEI     0x01

            KEI     0x02            ; halt

    """
}

let defaultSource = """
// Caca Studio — Caca Intermediate Language
// Example: print 'Hi!' then halt

MOV AL, 0x01   // write-char mode
MOV AH, 'H'
KEI 0x01
MOV AH, 'i'
KEI 0x01
MOV AH, '!'
KEI 0x01
MOV AH, '\\n'
KEI 0x01
KEI 0x02       // halt

"""
