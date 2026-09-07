# CIL Standard Library

This page documents the built-in kernel interrupts provided by the CIL runtime. Invoke them with the `KEI` instruction (opcode `0x2B`).

See [CIL-Specification §6.7](CIL-Specification#67-interrupts) for the `KEI` instruction encoding.

---

## KEI 0x01 — stdio

Behaviour is selected by the value in `AL`:

| AL     | Operation        | Description                                                                                                           |
|--------|------------------|-----------------------------------------------------------------------------------------------------------------------|
| `0x01` | Write character  | Writes the character in `AH` to standard output.                                                                     |
| `0x02` | Write string     | Writes a string to standard output. `X` holds the start address in RAM; `B` holds the length in bytes.               |
| `0x03` | Read character   | Reads one character from standard input and stores it in `AH`.                                                       |
| `0x04` | Read line        | Reads a line from standard input. Bytes are stored starting at the address in `X`; `B` is set to the length read.    |
| `0x05` | Write integer    | Writes the 16-bit **unsigned** value of `B` to standard output as decimal ASCII. See the limitation noted below.     |
| `0x06` | Write signed integer | Writes the signed 32-bit value of `X` to standard output as decimal ASCII, sign included.                        |

### Examples

**Write a single character (`'A'`):**
```
MOV AL, 0x01
MOV AH, 0x41    ; ASCII 'A'
KEI 0x01
```

**Write a string at address `0x0300`, length 5:**
```
MOV AL, 0x02
MOV X,  0x0300
MOV B,  0x0005
KEI 0x01
```

**Read a character into AH:**
```
MOV AL, 0x03
KEI 0x01
; AH now contains the character read
```

**Read a line into memory at `0x0300`:**
```
MOV AL, 0x04
MOV X,  0x0300
KEI 0x01
; B now contains the number of bytes read
```

**Write register `BL`/`BH` as a decimal integer:**
```
MOV AL, 0x05
MOV B,  42
KEI 0x01
; prints "42"
```

`AL = 0x05` reads `B` (the 16-bit composite of `BL`/`BH`, 0–65535) as an
**unsigned** value — there is no sign bit to interpret. It cannot print a
negative number or a value outside 0–65535; it exists for the common case of
a small non-negative count or index, where `X`/`Y` would be overkill. `AL =
0x06` is the full-range equivalent: a real signed 32-bit value from `X`,
negative numbers included, via `int.ToString()` on the host, which already
gets every edge case right (`int.MinValue` included — negating it would
overflow, and `ToString()` never negates, it formats the two's complement
value directly).

**Write `X` as a signed decimal integer:**
```
MOV X, -17
MOV AL, 0x06
KEI 0x01
; prints "-17"
```

Before `0x06` existed, a caller needing full-range signed output had to write
its own conversion by hand — see how [cacalang](Cacalang)'s CacaVM backend's
`__print_int` still does, predating this mode. New code should use `0x06`
instead.

---

## KEI 0x02 — Halt

Stops execution immediately. Equivalent to a program exit.

```
KEI 0x02
```

---

## Undefined interrupts

Any interrupt number not listed above causes the VM to print a diagnostic message and halt. Do not rely on this behaviour — it is subject to change.

---

## SWI 0x01 — String utilities

Software interrupt for basic string operations. Invoke with `SWI 0x01`; `AL` selects the operation.

| AL     | Operation   | Inputs                              | Outputs                  | Description                                              |
|--------|-------------|-------------------------------------|--------------------------|----------------------------------------------------------|
| `0x01` | String length | `X` = address of null-terminated string | `B` = length in bytes | Counts bytes until a `0x00` terminator; result in `B`. |
| `0x02` | String copy | `X` = source address, `Y` = destination address, `B` = byte count | — | Copies `B` bytes from `X` to `Y`. |
| `0x03` | String compare | `X`, `Y` = addresses of two null-terminated strings | `B` = 1 if equal, 0 if not | Byte-for-byte equality only, not lexicographic ordering — see the note below. |
| `0x04` | Parse integer (atoi) | `X` = address of a null-terminated decimal string | `Y` = the parsed signed 32-bit value | Reads an optional leading `+`/`-`, then digits, stopping at the first non-digit or the terminator. No digits at all is defined as `Y = 0`, not an error. |

### Examples

**Get length of a null-terminated string at `0x0300`:**
```
MOV AL, 0x01
MOV X,  0x0300
SWI 0x01
; B now contains the string length
```

**Copy 5 bytes from `0x0300` to `0x0400`:**
```
MOV AL, 0x02
MOV X,  0x0300
MOV Y,  0x0400
MOV B,  5
SWI 0x01
```

**Compare null-terminated strings at `0x0300` and `0x0400`:**
```
MOV AL, 0x03
MOV X,  0x0300
MOV Y,  0x0400
SWI 0x01
; B = 1 if the strings are equal, 0 if not
```

`AL = 0x03` compares for equality only. Ordering (`strcmp`'s classic
negative/zero/positive three-way result) would need a signed result, and `B`
is combined unsigned by every routine here (see `BitOps.CombineBytes`) — there
is no sign bit to carry a "less than zero" result through it correctly. A
caller that genuinely needs ordering, not just equality, has to compare bytes
itself via `MOE`.

**Parse a decimal integer from the null-terminated string at `0x0300`:**
```
MOV AL, 0x04
MOV X,  0x0300
SWI 0x01
; Y now holds the parsed signed integer
```

---

## Undefined SWI numbers

Any SWI number not listed above causes the VM to print a diagnostic message and halt.

