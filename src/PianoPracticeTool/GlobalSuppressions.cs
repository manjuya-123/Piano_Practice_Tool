using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Immutable record intentionally used as an event payload.",
    Scope = "type",
    Target = "~T:PianoPracticeTool.Views.ScoreEditorSaveRequestedEventArgs")]

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Immutable record intentionally used as an event payload.",
    Scope = "type",
    Target = "~T:PianoPracticeTool.Views.ScoreEditorPreviewRequestedEventArgs")]

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Immutable record intentionally used as an event payload.",
    Scope = "type",
    Target = "~T:PianoPracticeTool.Views.EditorSongRequestedEventArgs")]

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Immutable record intentionally used as an event payload.",
    Scope = "type",
    Target = "~T:PianoPracticeTool.Services.Editor.EditorPreviewPositionEventArgs")]
