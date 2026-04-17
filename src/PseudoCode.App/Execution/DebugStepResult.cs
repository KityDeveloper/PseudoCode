using PseudoCode.App.Services;

namespace PseudoCode.App;

internal sealed record DebugStepResult(ExecutionResult Execution, int? CurrentLine, bool IsFinished);
