/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */
//
// Copyright (c) FutureAI. All rights reserved.
// Contains confidential and  proprietary information and programs which may not be distributed without a separate license
//

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BrainSimulator.Modules
{
    public partial class ModuleStressTestDlg : ModuleBaseDlg
    {
        public ModuleStressTestDlg()
        {
            InitializeComponent();
        }

        public override bool Draw(bool checkDrawTimer)
        {
            return base.Draw(checkDrawTimer);
        }

        private void TheCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Draw(false);
        }

        private async void BtnBenchmark_Click(object sender, RoutedEventArgs e)
        {
            await RunOffTheUiThread("Benchmarking",
                count => ModuleStressTest.RunBenchmark(count, ReportProgress));
        }

        private async void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            await RunOffTheUiThread("Adding",
                count => ModuleStressTest.AddManyTestItems(count, ReportProgress));
        }

        private async void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            await RunOffTheUiThread("Removing",
                _ => ModuleStressTest.ClearTestItems(ReportProgress), requiresCount: false);
        }

        /// <summary>
        /// Runs one long operation without blocking the window, so the progress
        /// it reports can actually be seen while it is running.
        /// </summary>
        private async Task RunOffTheUiThread(
            string description,
            Func<int, string> work,
            bool requiresCount = true)
        {
            int count = 0;
            if (requiresCount && !int.TryParse(textInput.Text, out count))
            {
                txtOutput.Text = "Enter a whole number of Thoughts.";
                return;
            }

            SetButtonsEnabled(false);
            StatusLabel.Content = description + "...";
            txtOutput.Text = "";
            try
            {
                string result = await Task.Run(() => work(count));
                txtOutput.Text = result;
                StatusLabel.Content = "Done.";
            }
            catch (Exception exception)
            {
                txtOutput.Text = "Error: " + exception.Message;
                StatusLabel.Content = "Failed.";
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void ReportProgress(string message)
        {
            Dispatcher.BeginInvoke(new Action(() => StatusLabel.Content = message));
        }

        private void SetButtonsEnabled(bool enabled)
        {
            btnBenchmark.IsEnabled = enabled;
            btnAdd.IsEnabled = enabled;
            btnClear.IsEnabled = enabled;
        }
    }
}
