namespace Arcanum.Scripting;

/// <summary>How much of an original function a method implements (the registry status of its row).</summary>
internal enum OriginalFunctionScope
{
    /// <summary>The whole function, within the method's documented domain.</summary>
    Full,
    /// <summary>Part of the function; the registry row says which part is left out and why.</summary>
    Partial,
}

/// <summary>
/// Marks a method of <c>Arcanum.Scripting</c> that implements a function of the retail 1.0.7.4 executable (ARC-DEV-008,
/// docs/verification/README.md). This assembly cannot reference <c>Arcanum.Simulation</c>, so it declares its own attribute
/// of the same name and shape; the registry test recognises it by name.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class OriginalFunctionAttribute(uint address, OriginalFunctionScope scope) : Attribute
{
    public uint Address { get; } = address;
    public OriginalFunctionScope Scope { get; } = scope;
}
