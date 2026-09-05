using System;

namespace Artemis_IL
{
    public partial class VM
    {
        /// <summary>
        /// Parses and executes a single instruction identified by its opcode.
        /// Every instruction occupies exactly 6 bytes:
        ///   byte 0 : [opcode (bits 7-2)] [addrmode (bits 1-0)]
        ///   byte 1 : parameter 1 (8-bit register byte or value)
        ///   bytes 2-5 : parameter 2 (32-bit little-endian value or register byte in low byte)
        /// PC must be one ahead of IP on entry; PC is advanced by 5 for fixed-length
        /// instructions, or set directly for control-flow instructions.
        /// </summary>
        /// <param name="opcode">The 6-bit opcode extracted from byte 0</param>
        /// <returns>Always 0 (reserved for future use)</returns>
        public byte ParseOpcode(byte opcode)
        {
            // ── Register & Memory Operations ─────────────────────────────────────

            // MOV — Copy value into register (0x01)
            // Modes: RegReg, RegVal
            if (opcode == 0x01)
            {
                byte dest = ram.memory[IP + 1];
                if (dest < 0xF0)
                    throw new Exception($"[CRITICAL ERROR] MOV at {IP}: parameter 1 (0x{dest:X2}) is not a register.");

                if (opMode == AddressMode.RegVal)
                    SetRegister(dest, Get32BitParameter(IP + 2));
                else if (opMode == AddressMode.RegReg)
                    SetRegister(dest, GetRegister(ram.memory[IP + 2]));
                else
                    throw new Exception($"[CRITICAL ERROR] MOV at {IP}: invalid addressing mode {AdMode}.");

                PC += 5;
            }

            // SWP — Swap two registers (0x02)
            // Mode: RegReg
            else if (opcode == 0x02)
            {
                byte r1 = ram.memory[IP + 1];
                byte r2 = ram.memory[IP + 2];
                int tmp = GetRegister(r1);
                SetRegister(r1, GetRegister(r2));
                SetRegister(r2, tmp);
                PC += 5;
            }

            // ADD — dest = dest + src (0x04)
            // Modes: RegReg, RegVal
            else if (opcode == 0x04)
            {
                byte dest = ram.memory[IP + 1];
                if (opMode == AddressMode.RegVal)
                    SetRegister(dest, GetRegister(dest) + Get32BitParameter(IP + 2));
                else if (opMode == AddressMode.RegReg)
                    SetRegister(dest, GetRegister(dest) + GetRegister(ram.memory[IP + 2]));
                else
                    throw new Exception($"[CRITICAL ERROR] ADD at {IP}: invalid addressing mode {AdMode}.");
                PC += 5;
            }

            // SUB — dest = dest - src (0x05)
            // Modes: RegReg, RegVal
            else if (opcode == 0x05)
            {
                byte dest = ram.memory[IP + 1];
                if (opMode == AddressMode.RegVal)
                    SetRegister(dest, GetRegister(dest) - Get32BitParameter(IP + 2));
                else if (opMode == AddressMode.RegReg)
                    SetRegister(dest, GetRegister(dest) - GetRegister(ram.memory[IP + 2]));
                else
                    throw new Exception($"[CRITICAL ERROR] SUB at {IP}: invalid addressing mode {AdMode}.");
                PC += 5;
            }

            // SHL — Shift left (0x06)
            // param1 = src register, param2 = positions (value)
            else if (opcode == 0x06)
            {
                byte src = ram.memory[IP + 1];
                int positions = Get32BitParameter(IP + 2) & 31;
                SetRegister(src, GetRegister(src) << positions);
                PC += 5;
            }

            // SHR — Shift right (0x07)
            // param1 = src register, param2 = positions (value)
            else if (opcode == 0x07)
            {
                byte src = ram.memory[IP + 1];
                int positions = Get32BitParameter(IP + 2) & 31;
                SetRegister(src, (int)((uint)GetRegister(src) >> positions));
                PC += 5;
            }

            // INC — reg++ (0x08)
            else if (opcode == 0x08)
            {
                byte reg = ram.memory[IP + 1];
                SetRegister(reg, GetRegister(reg) + 1);
                PC += 5;
            }

            // DEC — reg-- (0x09)
            else if (opcode == 0x09)
            {
                byte reg = ram.memory[IP + 1];
                SetRegister(reg, GetRegister(reg) - 1);
                PC += 5;
            }

            // AND — srcA = srcA & srcB (0x0A)
            // Modes: RegReg, RegVal
            else if (opcode == 0x0A)
            {
                byte dest = ram.memory[IP + 1];
                if (opMode == AddressMode.RegReg)
                    SetRegister(dest, GetRegister(dest) & GetRegister(ram.memory[IP + 2]));
                else
                    SetRegister(dest, GetRegister(dest) & Get32BitParameter(IP + 2));
                PC += 5;
            }

            // BOR — srcA = srcA | srcB (0x0B)
            // Modes: RegReg, RegVal
            else if (opcode == 0x0B)
            {
                byte dest = ram.memory[IP + 1];
                if (opMode == AddressMode.RegReg)
                    SetRegister(dest, GetRegister(dest) | GetRegister(ram.memory[IP + 2]));
                else
                    SetRegister(dest, GetRegister(dest) | Get32BitParameter(IP + 2));
                PC += 5;
            }

            // XOR — srcA = srcA ^ srcB (0x0C)
            // Modes: RegReg, RegVal
            else if (opcode == 0x0C)
            {
                byte dest = ram.memory[IP + 1];
                if (opMode == AddressMode.RegReg)
                    SetRegister(dest, GetRegister(dest) ^ GetRegister(ram.memory[IP + 2]));
                else
                    SetRegister(dest, GetRegister(dest) ^ Get32BitParameter(IP + 2));
                PC += 5;
            }

            // NOT — src = ~src (0x0D)
            else if (opcode == 0x0D)
            {
                byte src = ram.memory[IP + 1];
                SetRegister(src, ~GetRegister(src));
                PC += 5;
            }

            // ROL — Rotate left (0x0E)
            // param1 = src register, param2 = positions (value)
            else if (opcode == 0x0E)
            {
                byte src = ram.memory[IP + 1];
                int n = Get32BitParameter(IP + 2) & 31;
                uint val = (uint)GetRegister(src);
                SetRegister(src, (int)((val << n) | (val >> (32 - n))));
                PC += 5;
            }

            // ROR — Rotate right (0x0F)
            // param1 = src register, param2 = positions (value)
            else if (opcode == 0x0F)
            {
                byte src = ram.memory[IP + 1];
                int n = Get32BitParameter(IP + 2) & 31;
                uint val = (uint)GetRegister(src);
                SetRegister(src, (int)((val >> n) | (val << (32 - n))));
                PC += 5;
            }

            // ── Flow Control ──────────────────────────────────────────────────────

            // JMP — Unconditional jump (0x10)
            // RegReg/RegVal: target from register (param1); ValReg/ValVal: target from param2
            else if (opcode == 0x10)
            {
                if (opMode == AddressMode.RegReg || opMode == AddressMode.RegVal)
                    PC = GetRegister(ram.memory[IP + 1]);
                else
                    PC = Get32BitParameter(IP + 2);
                // No PC += 5 — PC is already the jump target; Execute() will do IP=PC, PC++
            }

            // CLL — Call subroutine (0x11)
            // Pushes return address onto call stack, then jumps
            else if (opcode == 0x11)
            {
                // Return address = address immediately after this 6-byte instruction
                CallStack.Call(PC + 5);
                if (opMode == AddressMode.RegReg || opMode == AddressMode.RegVal)
                    PC = GetRegister(ram.memory[IP + 1]);
                else
                    PC = Get32BitParameter(IP + 2);
            }

            // RET — Return from subroutine (0x12)
            else if (opcode == 0x12)
            {
                PC = CallStack.Return();
                // No PC += 5 — PC is the saved return address
            }

            // JMT — Jump if true (0x13)
            else if (opcode == 0x13)
            {
                if (LastLogic)
                {
                    if (opMode == AddressMode.RegReg || opMode == AddressMode.RegVal)
                        PC = GetRegister(ram.memory[IP + 1]);
                    else
                        PC = Get32BitParameter(IP + 2);
                }
                else
                    PC += 5;
            }

            // JMF — Jump if false (0x14)
            else if (opcode == 0x14)
            {
                if (!LastLogic)
                {
                    if (opMode == AddressMode.RegReg || opMode == AddressMode.RegVal)
                        PC = GetRegister(ram.memory[IP + 1]);
                    else
                        PC = Get32BitParameter(IP + 2);
                }
                else
                    PC += 5;
            }

            // CLT — Call if true (0x17)
            else if (opcode == 0x17)
            {
                if (LastLogic)
                {
                    CallStack.Call(PC + 5);
                    if (opMode == AddressMode.RegReg || opMode == AddressMode.RegVal)
                        PC = GetRegister(ram.memory[IP + 1]);
                    else
                        PC = Get32BitParameter(IP + 2);
                }
                else
                    PC += 5;
            }

            // CLF — Call if false (0x18)
            else if (opcode == 0x18)
            {
                if (!LastLogic)
                {
                    CallStack.Call(PC + 5);
                    if (opMode == AddressMode.RegReg || opMode == AddressMode.RegVal)
                        PC = GetRegister(ram.memory[IP + 1]);
                    else
                        PC = Get32BitParameter(IP + 2);
                }
                else
                    PC += 5;
            }

            // ── Test Instructions ─────────────────────────────────────────────────

            // TEQ — Test equal; sets LastLogic if reg1 == reg2 (0x1A)
            else if (opcode == 0x1A)
            {
                LastLogic = GetRegister(ram.memory[IP + 1]) == GetRegister(ram.memory[IP + 2]);
                PC += 5;
            }

            // TNE — Test not equal (0x1B)
            else if (opcode == 0x1B)
            {
                LastLogic = GetRegister(ram.memory[IP + 1]) != GetRegister(ram.memory[IP + 2]);
                PC += 5;
            }

            // TLT — Test less than (0x1C)
            else if (opcode == 0x1C)
            {
                LastLogic = GetRegister(ram.memory[IP + 1]) < GetRegister(ram.memory[IP + 2]);
                PC += 5;
            }

            // TMT — Test more than (0x1D)
            else if (opcode == 0x1D)
            {
                LastLogic = GetRegister(ram.memory[IP + 1]) > GetRegister(ram.memory[IP + 2]);
                PC += 5;
            }

            // ── Stack Manipulation ────────────────────────────────────────────────

            // PSH — Push onto stack (0x20)
            // Decrements SP, then writes through ram.SetByte — the stack lives in the
            // shared address space (Spec-Architecture.md §4), growing down from the
            // top of RAM, not in a separate isolated buffer. SetByte's existing
            // RAMLimit guard (see RAM.cs) is what stops a runaway push from
            // corrupting the loaded program's code; there is no separate check here.
            else if (opcode == 0x20)
            {
                byte val;
                if (opMode == AddressMode.RegReg || opMode == AddressMode.RegVal)
                    val = (byte)GetRegister(ram.memory[IP + 1]);
                else
                    val = ram.memory[IP + 1];
                SP--;
                ram.SetByte(SP, val);
                PC += 5;
            }

            // POP — Pop from stack (0x21)
            // Reads through ram.GetByte, then increments SP. Throws on an empty stack
            // (SP already at the top of RAM) rather than reading past the end of the
            // allocated memory.
            else if (opcode == 0x21)
            {
                if (SP >= ram.memory.Length - 1)
                    throw new Exception("POP was executed with an empty stack.");
                byte val = ram.GetByte(SP);
                SP++;
                SetRegister(ram.memory[IP + 1], val);
                PC += 5;
            }

            // ── I/O ───────────────────────────────────────────────────────────────

            // INB — Receive byte from port (0x24)
            // param1 = port, param2[0] = dest register
            else if (opcode == 0x24)
            {
                // I/O port infrastructure reserved for future implementation
                PC += 5;
            }

            // INW — Receive word from port (0x25)
            else if (opcode == 0x25)
            {
                PC += 5;
            }

            // IND — Receive double word from port (0x26)
            else if (opcode == 0x26)
            {
                PC += 5;
            }

            // OUB — Send byte to port (0x27)
            // param1 = port, param2 = src register or value
            else if (opcode == 0x27)
            {
                PC += 5;
            }

            // OUW — Send word to port (0x28)
            else if (opcode == 0x28)
            {
                PC += 5;
            }

            // OUD — Send double word to port (0x29)
            else if (opcode == 0x29)
            {
                PC += 5;
            }

            // ── Interrupts ────────────────────────────────────────────────────────

            // SWI — Software interrupt (0x2A)
            else if (opcode == 0x2A)
            {
                parameters[1] = ram.memory[IP + 1];
                StandardLib.SoftwareInterrupts.HandleInterrupt(parameters[1]);
                PC += 5;
            }

            // KEI — Kernel interrupt (0x2B)
            else if (opcode == 0x2B)
            {
                parameters[1] = ram.memory[IP + 1];
                StandardLib.KernelInterrupts.HandleInterrupt(parameters[1]);
                PC += 5;
            }

            // ── Arithmetic (continued) ────────────────────────────────────────────

            // MUL — dest = dest * src (0x30)
            // Modes: RegReg, RegVal
            else if (opcode == 0x30)
            {
                byte dest = ram.memory[IP + 1];
                if (opMode == AddressMode.RegReg)
                    SetRegister(dest, GetRegister(dest) * GetRegister(ram.memory[IP + 2]));
                else
                    SetRegister(dest, GetRegister(dest) * Get32BitParameter(IP + 2));
                PC += 5;
            }

            // DIV — dest = dest / src (0x31), integer division
            // Modes: RegReg, RegVal
            else if (opcode == 0x31)
            {
                byte dest = ram.memory[IP + 1];
                int divisor = (opMode == AddressMode.RegReg)
                    ? GetRegister(ram.memory[IP + 2])
                    : Get32BitParameter(IP + 2);
                if (divisor == 0)
                    throw new Exception($"[CRITICAL ERROR] DIV at {IP}: division by zero.");
                SetRegister(dest, GetRegister(dest) / divisor);
                PC += 5;
            }

            // ── Register & Memory Operations (continued) ──────────────────────────

            // MOM — Move to memory; write src to dest address (0x3A)
            // Modes: RegVal/RegReg (src=register), ValVal/ValReg (src=value/literal);
            // RegReg/ValReg additionally mean dest is register-indirect — the address
            // is the register's runtime value (param2's byte), not a baked-in literal.
            // These two axes (src kind, dest kind) are independent, so all four
            // AddressMode combinations are meaningful here.
            else if (opcode == 0x3A)
            {
                bool destIsRegister = (opMode == AddressMode.RegReg || opMode == AddressMode.ValReg);
                int destAddr = destIsRegister
                    ? GetRegister(ram.memory[IP + 2])
                    : Get32BitParameter(IP + 2);
                int srcVal;
                if (opMode == AddressMode.RegVal || opMode == AddressMode.RegReg)
                    srcVal = GetRegister(ram.memory[IP + 1]);
                else
                    srcVal = ram.memory[IP + 1];
                ram.SetByte(destAddr, (byte)srcVal);
                PC += 5;
            }

            // MOE — Move from memory; read src address into dest register (0x3B)
            // dest (param1) is always a register. Mode RegVal: src is a literal
            // address (existing behaviour). Mode RegReg: src is register-indirect —
            // the address is the register named in param2's runtime value.
            else if (opcode == 0x3B)
            {
                bool srcIsRegister = (opMode == AddressMode.RegReg || opMode == AddressMode.ValReg);
                int srcAddr = srcIsRegister
                    ? GetRegister(ram.memory[IP + 2])
                    : Get32BitParameter(IP + 2);
                byte destReg = ram.memory[IP + 1];
                SetRegister(destReg, ram.GetByte(srcAddr));
                PC += 5;
            }

            else
            {
                throw new Exception($"[ERROR] Unsupported instruction at {IP}: opcode 0x{opcode:X2}.");
            }

            return 0;
        }
    }
}
