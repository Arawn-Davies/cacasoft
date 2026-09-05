namespace Caca.VM.Compiler
{
    /// <summary>
    /// Parses single-character literals written as 'X' or '\n' in source.
    /// </summary>
    internal static class CharValue
    {
        /// <summary>Returns true when <paramref name="token"/> looks like a char literal.</summary>
        public static bool Check(string token)
        {
            if (!token.StartsWith("'") || !token.EndsWith("'"))
                return false;
            // '\X' = 4 chars, 'X' = 3 chars
            return (token.Contains('\\') && token.Length == 4) || token.Length == 3;
        }

        /// <summary>Converts a char-literal token to its byte value.</summary>
        public static byte Parse(string token)
        {
            string inner = token.Trim('\'');
            if (inner.Length > 0 && inner[0] == '\\')
            {
                return inner[1] switch
                {
                    'n'  => (byte)'\n',
                    'r'  => (byte)'\r',
                    't'  => (byte)'\t',
                    'a'  => (byte)'\a',
                    'b'  => (byte)'\b',
                    'v'  => (byte)'\v',
                    '0'  => 0,
                    '\'' => (byte)'\'',
                    '"'  => (byte)'"',
                    '\\' => (byte)'\\',
                    _    => (byte)inner[1],
                };
            }
            return (byte)inner[0];
        }
    }
}
