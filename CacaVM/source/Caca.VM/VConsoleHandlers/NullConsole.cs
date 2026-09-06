using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Caca.VM.Handlers
{
    /// <summary>
    /// A no-op <see cref="VConsole"/> used as the default when no real console is configured.
    /// All write operations silently discard their input; read operations throw
    /// <see cref="System.NotImplementedException"/> because there is no input source.
    /// </summary>
    public class NullConsole : VConsole
    {
        /// <inheritdoc/>
        public override byte Read()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override string ReadLine()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public override void WriteLine(string text)
        {
            // Do nothing
        }

        /// <inheritdoc/>
        public override void Write(char ch)
        {
            // Do nothing
        }

        /// <inheritdoc/>
        public override void Write(string text)
        {
            // Do nothing
        }
    }
}