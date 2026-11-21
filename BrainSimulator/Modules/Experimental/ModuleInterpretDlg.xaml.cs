using System.Windows;

namespace BrainSimulator.Modules;

public partial class ModuleInterpretDlg : ModuleBaseDlg
{
    public ModuleInterpretDlg() => InitializeComponent();

    private void ShowSequenceClick(object sender, RoutedEventArgs e)
    {
        var module = (ModuleInterpret)ParentModule;
        Output.Text = module.Input(EnglishInput.Text, rememberUnknownWords: true);
    }
}
