namespace Arcanum.Scripting.Dialog;

internal sealed class DialogOperationParseException : Exception
{
    internal DialogOperationParseException(DialogFailureKind kind, DialogOperationSource source)
    {
        Kind = kind;
        OperationSource = source;
    }

    internal DialogFailureKind Kind { get; }
    internal DialogOperationSource OperationSource { get; }
}
