# Standard Library

This page documents the built-in kernel interrupts provided by the CIL runtime. Invoke them with the `KEI` instruction (opcode `0x2B`).

See [[Specification#67-interrupts]] for the `KEI` instruction encoding.

---

## KEI 0x01 — stdio

Behaviour is selected by the value in `AL`:

| AL     | Operation        | Description                                                                                                           |
|--------|------------------|-----------------------------------------------------------------------------------------------------------------------|
| `0x01` | Write character  | Writes the character in `AH` to standard output.                                                                     |
| `0x02` | Write string     | Writes a string to standard output. `X` holds the start address in RAM; `B` holds the length in bytes.               |
| `0x03` | Read character   | Reads one character from standard input and stores it in `AH`.                                                       |
| `0x04` | Read line        | Reads a line from standard input. Bytes are stored starting at the address in `X`; `B` is set to the length read.    |

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

---

## Undefined SWI numbers

Any SWI number not listed above causes the VM to print a diagnostic message and halt.

