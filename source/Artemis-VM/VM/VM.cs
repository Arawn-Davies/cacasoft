using System;
using Artemis_IL.StandardLib;
using Artemis_IL.Conversions;

namespace Artemis_IL
{
    public partial class VM
    {
		/// <summary>
		/// Result of the last test instruction (TEQ/TNE/TLT/TMT).
		/// Used by conditional jump/call instructions.
		/// </summary>
		private bool LastLogic;
		/// <summary>
		/// Current instructions parameters
		/// </summary>
		private int[] parameters;
		/// <summary>
		/// enum of the Address Modes
		/// </summary>
		private enum AddressMode
		{
            RegReg,
            ValReg,
            RegVal,
            ValVal
        }

		/// <summary>
		/// Address mode for current operation
		/// </summary>
		private AddressMode opMode;

		#region registers
		/// <summary>
		/// Program Counter. Full-width (not byte) so code can address the entire RAM
		/// segment (see <see cref="Globals.DefaultRamSize"/>) rather than wrapping at 256 bytes.
		/// </summary>
		public int PC;
		/// <summary>
		/// Stack Pointer
		/// </summary>
		public byte SP;
		/// <summary>
		/// Instruction Pointer. Full-width for the same reason as <see cref="PC"/>.
		/// </summary>
		public int IP;
		/// <summary>
		/// Stack segment
		/// </summary>
		public byte SS;
		/// <summary>
		/// General purpose register
		/// Lower byte of the A register
		/// </summary>
		public byte AL;
		/// <summary>
		/// General purpose register
		/// Higher byte of the A register
		/// </summary>
		public byte AH;
		/// <summary>
		/// General purpose register
		/// Lower byte of the B register
		/// </summary>
		public byte BL;
		/// <summary>
		/// General purpose register
		/// Higher byte of the B register
		/// </summary>
		public byte BH;
		/// <summary>
		/// General purpose register
		/// Lower byte of the C regiser
		/// </summary>
		public byte CL;
		/// <summary>
		/// General purpose register
		/// Higher byte of the C register
		/// </summary>
		public byte CH;
        /// <summary>
		/// 32-bit general purpose register
		/// </summary>
		public Int32 X;
        /// <summary>
        /// 32-bit general purpose register
        /// </summary>
        public Int32 Y;
        #endregion

        public bool Running = false;

        /// <summary>
        /// 256-byte stack memory. SP indexes into this array; grows downward from 0xFF.
        /// </summary>
        public byte[] _stackMemory = new byte[256];
	
		/// <summary>
		/// Loads the application as a byte array into the virtual machine's memory
		/// </summary>
		/// <param name="application"></param>
		private void LoadApplication(byte[] application)
		{  
            int i = 0;
            while (i < application.Length)
            {
                ram.memory[i] = application[i];
                i++;
            }
            //Sets the RAM limit, right above the end of the executable:
            ram.RAMLimit = (i + 1);

        }
		/// <summary>
		/// This virtual machine's RAM
		/// </summary>
		public RandomAccessMemory ram;
		/// <summary>
		/// Constructor for a new instance of a virtual machine, specifiying the executable to run and the VM's amount of RAM.
		/// </summary>
		/// <param name="executable">The executing binary's size must be greater than the ramsize</param>
		/// <param name="ramsize">The size of virtual memory in bytes must be larger than the size of the executable binary</param>
		public VM(byte[] executable, int ramsize)
		{
            ram = new RandomAccessMemory(ramsize);
            // Defines the parameters integer array as an array of length 5
            parameters = new int[5];
			// Sets the instruction pointer to 0
			IP = 0;
			// Sets the program counter to 1 (one ahead of IP)
			PC = 1;
			// Stack Pointer starts at 0xFF (top of 256-byte stack segment)
			SP = 0xFF;
			// Sets the parent virtual machine for the standard library to this instance
			KernelInterrupts.ParentVM = this;
            SoftwareInterrupts.ParentVM = this;
			//Loads the executable into the Virtual Machine's memory through LoadApplication()
			LoadApplication(executable);
		}
		/// <summary>
		/// Address mode initially set to 0, for Register:Register
		/// </summary>
		private int AdMode = 0;
        /// <summary>
        /// Gets the current address mode from the specified byte
        /// </summary>
        /// <param name="b">Address mode byte</param>
        public void GetAddressMode(byte b)
        {
            AdMode = GetLastTwo(b);
            if (AdMode == 0)
            {
                opMode = AddressMode.RegReg;
            }
            else if (AdMode == 1)
            {
                opMode = AddressMode.ValReg;
            }
            else if (AdMode == 2)
            {
                opMode = AddressMode.RegVal;
            }
            else if (AdMode == 3)
            {
                opMode = AddressMode.ValVal;
            }
            else
            {
                throw new Exception("[CRITICAL ERROR] Invalid address mode at " + IP + " (" + AdMode + ").");
            }
        }
        /// <summary>
        /// Executes the binary loaded into the Virtual Machine's memory
        /// </summary>
        public void Execute()
		{
            Running = true;
            while (Running && ram.memory[IP] != 0x00)
            {
                byte firstByte = ram.memory[IP];
                byte opcode = (byte)GetFirstSix(firstByte);
                if (opcode == 0x00)
                {
                    Running = false;
                    break;
                }
                GetAddressMode(firstByte);
                ParseOpcode(opcode);
                IP = PC;
                PC++;
            }
            Running = false;
		}

        // Useful for stepping in monitor or debugging modes
        public void Tick()
        {
            if (ram.memory[IP] != 0x00)
            {
                byte firstByte = ram.memory[IP];
                byte opcode = (byte)GetFirstSix(firstByte);
                if (opcode == 0x00)
                {
                    Running = false;
                    return;
                }
                GetAddressMode(firstByte);
                ParseOpcode(opcode);
                IP = PC;
                PC++;
            }
        }

        public void ExecuteAtAddress(byte addr)
        {
            IP = addr;
            Execute();
        }

        public void Halt()
        {
            Running = false;
        }

        /// <summary>
        /// Retrieves the last two bits from a single byte (bits 1-0, the address mode field).
        /// Per spec §2: bits 41-40 of the 48-bit instruction = the lower 2 bits of byte 0.
        /// </summary>
        /// <param name="b">First byte of the instruction</param>
        /// <returns>Address mode value (0-3)</returns>
        public int GetLastTwo(byte b)
        {
            return b & 0x03;
        }
        /// <summary>
        /// Retrieves the integer value of the upper six bits in a byte (the opcode field).
        /// Per spec §2: bits 47-42 of the 48-bit instruction = the upper 6 bits of byte 0.
        /// </summary>
        /// <param name="b">First byte of the instruction</param>
        /// <returns>Opcode value (0-63)</returns>
        public int GetFirstSix(byte b)
        {
            return (b >> 2) & 0x3F;
        }

        /// <summary>
		/// Stores content into a 16-bit split register (A, B, or C), setting both low and high halves.
        /// </summary>
        /// <param name="Register">Register name ('A', 'B', or 'C')</param>
        /// <param name="Content">16-bit value to store</param>
        public void SetSplit(char Register, int Content)
        {
            byte lower = (byte)(Content & 0xFF);
            byte higher = (byte)((Content >> 8) & 0xFF);
            if (Register == 'A')
            {
                AL = lower;
                AH = higher;
            }
            else if (Register == 'B')
            {
                BL = lower;
                BH = higher;
            }
            else if (Register == 'C')
            {
                CL = lower;
                CH = higher;
            }
        }

        /// <summary>
        /// Retrieves the data stored in each register half (AL/AH, BL/BH, CL/CH), returning the integer value
        /// If the specified register isn't A/B/C, throw a new exception.
        /// </summary>
        /// <param name="Register"></param>
        /// <returns>Two halves of the specified register combined into one integer</returns>
        public int GetSplit(char Register)
        {
            if (Register == 'A')
            {
                return BitOps.CombineBytes(AL, AH);
            }
            else if (Register == 'B')
            {
                return BitOps.CombineBytes(BL, BH);
            }
            else if (Register == 'C')
            {
                return BitOps.CombineBytes(CL, CH);
            }
            throw new Exception("There was an internal error and the VM has had to close.");
        }
    }
}
