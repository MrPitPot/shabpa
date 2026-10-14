// Lets records and init-only properties compile against .NET Framework 4.8.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
