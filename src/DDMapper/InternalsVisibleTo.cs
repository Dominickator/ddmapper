// Exposes internals to the test assembly only, so tests can observe plan caching
// without changing the public API surface.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DDMapper.Tests")]
