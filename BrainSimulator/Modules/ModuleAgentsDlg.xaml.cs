/*
 * Brain Simulator Thought
 * Copyright (c) 2026 Charles Simon
 * Licensed under the MIT License.
 */

using System.Windows;
using System.Windows.Controls;

namespace BrainSimulator.Modules;

public partial class ModuleAgentsDlg : ModuleBaseDlg
{
    private bool _buttonsBuilt;

    public ModuleAgentsDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        if (ParentModule is not ModuleAgents module) return false;

        if (!_buttonsBuilt)
        {
            AgentButtons.Children.Clear();
            foreach (IManualAgent agent in module.Agents)
            {
                var button = new Button
                {
                    Content = agent.AgentName,
                    Tag = agent.AgentName,
                    MinWidth = 105,
                    Height = 32,
                    Margin = new Thickness(3),
                    Padding = new Thickness(8, 2, 8, 2),
                };
                button.Click += Agent_Click;
                AgentButtons.Children.Add(button);
            }
            _buttonsBuilt = true;
        }

        StatusText.Text = module.Status;
        StatusText.ScrollToEnd();
        return true;
    }

    private void Agent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string agentName &&
            ParentModule is ModuleAgents module)
            module.RunAgent(agentName);
    }
}
