namespace PseudoCode.App.Services;

public sealed record ExecutionResult(
    bool Success,
    IReadOnlyList<string> Output,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyDictionary<string, object?> Variables);
