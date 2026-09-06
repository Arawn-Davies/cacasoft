using System;

namespace Caca.VM.Compiler
{
    /// <summary>
    /// Exception raised when source code cannot be assembled.
    /// </summary>
    public class BuildException : Exception
    {
        /// <summary>1-based source line number where the error occurred (0 = unknown).</summary>
        public int SrcLineNumber { get; }

        public BuildException() : base() { }

        public BuildException(string message) : base(message) { }

        public BuildException(string message, int lineNumber)
            : base(message)
        {
            SrcLineNumber = lineNumber;
        }

        public BuildException(string message, Exception inner, int lineNumber)
            : base(message, inner)
        {
            SrcLineNumber = lineNumber;
        }
    }
}
