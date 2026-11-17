using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Interpretation = BrainSimulator.Modules.ModuleInterpret.Interpretation;

namespace BrainSimulator.Modules;

public partial class ModuleInterpretDlg : ModuleBaseDlg
{
    private Interpretation result;
    private string interpretationText = "";

    public ModuleInterpretDlg() => InitializeComponent();

    private void InputChanged(object sender, TextChangedEventArgs e)
    {
        result = null;
        if (ExecuteButton is not null) ExecuteButton.IsEnabled = false;
    }

    private void InterpretClick(object sender, RoutedEventArgs e)
    {
        InputChanged(sender, null);
        try
        {
            result = ((ModuleInterpret)ParentModule).Interpret(EnglishInput.Text);
            interpretationText = $"Intent   {result.Intent?.ToString() ?? "Unresolved"}\n" +
                string.Join("\n", result.Relationships.Select(link => link.ToString())) +
                (result.Diagnostic is null ? "" : $"\n{result.Diagnostic}") +
                "\n\nTrace\n" + string.Join("\n", result.Trace);
            Output.Text = interpretationText;
            ExecuteButton.IsEnabled = result.Intent is not null && result.Diagnostic is null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            Output.Text = error.Message;
        }
    }

    private void ExecuteClick(object sender, RoutedEventArgs e)
    {
        if (result is null) return;
        try
        {
            var links = ((ModuleInterpret)ParentModule).Execute(result);
            Output.Text = interpretationText + "\n\nExecution\n" +
                (links.Count == 0 ? "No matching Relationships." : string.Join("\n", links.Select(link => link.ToString())));
            Output.ScrollToEnd();
        }
        catch (ArgumentException error)
        {
            Output.Text = error.Message + " Interpret the input again.";
            ExecuteButton.IsEnabled = false;
        }
    }

    private void ExamplesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ((ModuleInterpret)ParentModule).LoadExamples();
            Output.Text = "Example facts loaded. Interpret an input to try them.";
            InputChanged(sender, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException)
        {
            Output.Text = error.Message;
        }
    }
}
