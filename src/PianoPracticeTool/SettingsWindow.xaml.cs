using System.Windows;
using PianoPracticeTool.Services.Settings;
using PianoPracticeTool.Views;

namespace PianoPracticeTool;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(
        AppSettings settings,
        IReadOnlyList<string> midiDeviceNames,
        IReadOnlyList<string> midiOutputDeviceNames,
        string midiStatus)
    {
        InitializeComponent();
        SettingsContent.SetState(
            settings,
            midiDeviceNames,
            midiOutputDeviceNames,
            midiStatus);
    }

    public SettingsView View => SettingsContent;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();
}
