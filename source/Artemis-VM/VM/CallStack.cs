using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Artemis_IL
{
    /// <summary>
    /// Class containing methods for managing the call stack
    /// </summary>
    public static class CallStack
    {
        /// <summary>
        /// Maximum nested call depth. Matches the size of <see cref="mem_locations"/>.
        /// </summary>
        private const int MaxDepth = 255;
        /// <summary>
        /// Stack index — the number of return addresses currently pushed (i.e. one
        /// past the top of the stack, the slot the next Call() will write to).
        /// </summary>
        private static int stc_index = 0;
        /// <summary>
        /// int array containing the memory locations
        /// </summary>
        private static int[] mem_locations = new int[MaxDepth];
        /// <summary>
        /// Pushes the location of a bytecode onto the call stack
        /// </summary>
        /// <param name="location"></param>
        public static void Call(int location)
        {
            if (stc_index >= MaxDepth)
                throw new Exception($"The application exceeded the maximum call stack depth ({MaxDepth} nested calls) and was terminated.");
            // Adds the location to the top of the stack (stc_index)
            mem_locations[stc_index] = location;
            // Increments the stack index by 1
            stc_index += 1;
        }
        /// <summary>
        /// Pops and returns the last location in the call stack
        /// </summary>
        /// <returns>The location in the call stack</returns>
        public static int Return()
        {
            if (stc_index <= 0)
                throw new Exception("RET was executed with an empty call stack (no matching CLL/CLT/CLF).");
            // Decrement first: stc_index is a count of pushed entries, so the top of
            // the stack is at stc_index - 1, not stc_index (that slot is one past the
            // last write Call() made).
            stc_index -= 1;
            // Gets the location from the top of the stack
            int r = mem_locations[stc_index];
            // Overwrites the last location
            mem_locations[stc_index] = 0;
            // Returns the earlier retrieved location
            return r;
        }

        /// <summary>
        /// Clears the call stack. CallStack's state is static (shared process-wide,
        /// not owned by any one VM instance), so a new VM must call this or it would
        /// silently inherit whatever a previous VM instance (or test) left behind.
        /// </summary>
        public static void Reset()
        {
            stc_index = 0;
            Array.Clear(mem_locations, 0, mem_locations.Length);
        }
    }
}
