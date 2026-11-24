using System.Windows.Input;

namespace BrainSimulator.Modules;

public partial class ModuleInflectDlg : ModuleBaseDlg
{
    public ModuleInflectDlg() => InitializeComponent();

    // Enter shows what the stored forms and the inflection rules make of the typed word.
    private void WordInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Output.Text = ((ModuleInflect)ParentModule).Report(WordInput.Text);
    }
}
