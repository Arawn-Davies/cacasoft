using System;
using System.Collections.Generic;
using System.Text;

namespace Caca.VM
{
    public class RandomAccessMemory
    {
		/// <summary>
		/// Fills the specified location in memory with the specified byte content
		/// </summary>
		/// <param name="location"></param>
		/// <param name="content"></param>
		public void SetSection(int location, byte[] content)
		{
            for (int i = 0; i < content.Length; i++)
            {
                memory[(i + location)] = content[i];
            }
        }

		/// <summary>
		/// Returns a section of memory at the specified location of the specified length
		/// </summary>
		/// <param name="location"></param>
		/// <param name="length"></param>
		/// <returns>Section of memory</returns>
		public byte[] GetSection(int location, int length)
		{
            byte[] ret = new byte[length];
            for (int i = 0; i < length; i++)
            {
                ret[i] = memory[(location + i)];
            }
            return ret;
        }
		/// <summary>
		/// Sets a specified location in memory to a specified byte.
		/// Writes to any address at or above RAMLimit (data area, outside the loaded code).
		/// </summary>
		/// <param name="location"></param>
		/// <param name="content"></param>
		public void SetByte(int location, byte content)
		{
            if (location >= RAMLimit)
            {
                memory[location] = content;
            }
            else
            {
                throw new Exception("The application attempted to overwrite its own code and was terminated.");
            }
        }

		/// <summary>
		/// Returns the byte at the specified memory location.
		/// </summary>
		/// <param name="location"></param>
		/// <returns>Byte at the given address</returns>
		public byte GetByte(int location)
		{
            return memory[location];
        }
		/// <summary>
		/// Memory as a byte array
		/// </summary>
		public byte[] memory;
		/// <summary>
		/// Constructor for the RAM
		/// </summary>
		/// <param name="amount"></param>
		public RandomAccessMemory(int amount)
		{
			memory = new byte[amount];
		}
		/// <summary>
		/// Position of where the executed binary ends + 1
		/// </summary>
		public int RAMLimit;

    }
}
