namespace Arcanum.Simulation;

/// <summary>How much of an original function a method implements (the registry status of its row).</summary>
public enum OriginalFunctionScope
{
    /// <summary>The whole function, within the method's documented domain.</summary>
    Full,
    /// <summary>Part of the function; the registry row says which part is left out and why.</summary>
    Partial,
}

/// <summary>
/// Marks a method that implements a function of the retail 1.0.7.4 executable at <see cref="Address"/> (ARC-DEV-008). Every
/// marked method has a row in <c>docs/verification/functions.csv</c> with the same address and scope, and every implemented
/// row has a marked method; a root test checks both. An assembly that cannot reference this one declares its own
/// <c>OriginalFunctionAttribute(uint address, OriginalFunctionScope scope)</c> of the same shape: the test recognises the
/// attribute by name.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class OriginalFunctionAttribute(uint address, OriginalFunctionScope scope) : Attribute
{
    /// <summary>The function's address in the executable.</summary>
    public uint Address { get; } = address;

    public OriginalFunctionScope Scope { get; } = scope;
}
