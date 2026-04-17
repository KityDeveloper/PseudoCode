using Avalonia.Platform.Storage;
using PseudoCode.App.Services;

namespace PseudoCode.App;

internal sealed record HelpTopic(string Title, string Description, string Example, bool IsExpanded = false);

internal sealed record CommandInfo(string Text, string InsertText, string Description, bool IsTemplate = false);

internal enum CloseDocumentChoice
{
    Cancel,
    Save,
    Discard
}

internal sealed class OpenDocument
{
    public OpenDocument(string displayName, string text, PseudoLanguageDefinition language)
    {
        DisplayName = displayName;
        Text = text;
        Interpreter = new AdvancedPseudoInterpreter(language);
    }

    public IStorageFile? File { get; set; }
    public string DisplayName { get; set; }
    public string Location { get; set; } = "Sin guardar";
    public string Text { get; set; }
    public string OutputText { get; set; } = string.Empty;
    public string DiagnosticsText { get; set; } = "Sin diagnosticos.";
    public string VariablesText { get; set; } = string.Empty;
    public HashSet<int> DiagnosticLines { get; set; } = [];
    public bool HasUnsavedChanges { get; set; }
    public ExecutionResult? LastExecutionResult { get; set; }
    public AdvancedPseudoInterpreter Interpreter { get; private set; }
    public bool IsDebugging { get; set; }
    public int? DebugLine { get; set; }

    public void ApplyLanguage(PseudoLanguageDefinition language)
    {
        Interpreter = new AdvancedPseudoInterpreter(language);
        LastExecutionResult = null;
        OutputText = string.Empty;
        DiagnosticsText = "Sin diagnosticos.";
        VariablesText = string.Empty;
        DiagnosticLines.Clear();
        IsDebugging = false;
        DebugLine = null;
    }
}
