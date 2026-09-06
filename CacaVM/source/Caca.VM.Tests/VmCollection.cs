using Xunit;

namespace Caca.VM.Tests
{
    /// <summary>
    /// Serialises all tests that touch the process-wide
    /// <see cref="Caca.VM.Globals"/> singleton (console, DebugMode, ParentVM).
    /// Without this, concurrent test classes could interfere with each other's
    /// captured output.
    /// </summary>
    [CollectionDefinition("VM")]
    public sealed class VmCollection : ICollectionFixture<object> { }
}
