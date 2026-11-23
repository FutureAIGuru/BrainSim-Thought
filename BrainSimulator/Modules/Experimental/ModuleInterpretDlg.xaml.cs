using System.Windows.Input;

namespace BrainSimulator.Modules;

public partial class ModuleInterpretDlg : ModuleBaseDlg
{
    public ModuleInterpretDlg() => InitializeComponent();

    // Enter interprets the sentence (relationships, [a->b->c], explain one that was not understood);
    // the reply goes to Answer and each stage's results to Details.
    private void EnglishInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var module = (ModuleInterpret)ParentModule;
        Output.Text = module.Input(EnglishInput.Text, rememberUnknownWords: true);
        Answer.Text = module.Answer;
    }

    // Undoes the most recent statement that changed the UKS.
    private void UndoClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var module = (ModuleInterpret)ParentModule;
        Answer.Text = module.UndoLastInput();
        Output.Text = "";
    }
}
