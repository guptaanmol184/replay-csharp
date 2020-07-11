﻿using Replay.Logging;
using Replay.Services;
using Replay.Services.Logging;
using Replay.UI;
using System;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Replay.ViewModel.Services
{
    /// <summary>
    /// Handles viewmodel manipulation based on input events.
    /// This partial class is the main entry point for input events, and
    /// routes to the other partial classes for specific functionality.
    /// </summary>
    public partial class ViewModelService
    {
        private readonly IReplServices services;

        public ViewModelService(IReplServices services, Dispatcher dispatcher, WindowViewModel windowvm)
        {
            this.services = services;
            this.services.UserConfigurationLoaded += ConfigureWindow(dispatcher, windowvm);
            this.services.PipeMessageReceived += AppendLine(dispatcher, windowvm);
        }

        private EventHandler<string> AppendLine(Dispatcher dispatcher, WindowViewModel windowvm) =>
            (object sender, string line) => dispatcher.Invoke(async () =>
        {
            var linevm = (
                from line in new[] { windowvm.Entries[windowvm.FocusIndex], windowvm.Entries.Last() }
                where line.Document.TextLength == 0
                select line
            )
            .FirstOrDefault();

            if(linevm is null)
            {
                linevm = new LineViewModel();
                windowvm.Entries.Add(linevm);
                windowvm.FocusIndex = windowvm.Entries.Count - 1;
            }
            dispatcher.Invoke(() => { }, DispatcherPriority.Background); // wait for databinding to finish so linevm.Document is populated.
            linevm.Document.Text = line;
            await ReadEvalPrintLoop(windowvm, linevm, LineOperation.Evaluate);
        });

        /// <summary>
        /// Callback for when user settings are loaded
        /// </summary>
        private EventHandler<UserConfiguration> ConfigureWindow(Dispatcher dispatcher, WindowViewModel windowvm) =>
            (object sender, UserConfiguration config) => dispatcher.Invoke(() =>
        {
            windowvm.Background = new SolidColorBrush(config.BackgroundColor);
            windowvm.Foreground = new SolidColorBrush(config.ForegroundColor);
        });

        public async Task HandleKeyDown(WindowViewModel windowvm, LineViewModel linevm, KeyEventArgs e)
        {
            if (windowvm.Intellisense.IsOpen) return;

            int previousHistoryPointer = ResetHistoryCyclePointer(windowvm);

            if(KeyboardShortcuts.MapToCommand(e) is ReplCommand command)
            {
                e.Handled = true; // tricky! we're in an async void event handler. WPF will see this value
                                  // first, as the event handler will complete before our task is does.
                e.Handled = await HandleCommand(windowvm, linevm, command, previousHistoryPointer);
            }
        }

        public async Task HandleKeyUp(WindowViewModel windowvm, LineViewModel linevm, KeyEventArgs e)
        {
            if (windowvm.Intellisense.IsOpen) return;

            if (Keyboard.Modifiers == ModifierKeys.None
                && e.Key == Key.OemPeriod // complete member accesses
                && !IsCompletingDigit()) // but don't complete decimal points in numbers
            {
                await CompleteCode(windowvm, linevm);
            }

            bool IsCompletingDigit()
            {
                string text = linevm.Document.Text;
                return text.Length >= 2 && Char.IsDigit(text[text.Length - 2]);
            }
        }

        public async Task HandleSmartPaste(LineViewModel linevm, string pastedText)
        {
            var unboundVariables = await services.GetUnboundVariables(linevm.Id, pastedText);
            if(!unboundVariables.Any())
            {
                linevm.Document.Text = pastedText.Trim();
            }
            else
            {
                var declarations = unboundVariables.Select(name => $"var {name} = ;").ToArray();

                linevm.Document.Text = string.Join(Environment.NewLine, declarations)
                    + Environment.NewLine
                    + pastedText.Trim();
                linevm.CaretOffset = declarations.First().Length - 1;
            }
        }

        private async Task<bool> HandleCommand(WindowViewModel windowvm, LineViewModel linevm, ReplCommand cmd, int previousHistoryPointer)
        {
            switch (cmd)
            {
                case ReplCommand.EvaluateCurrentLine:
                    await ReadEvalPrintLoop(windowvm, linevm, LineOperation.Evaluate);
                    return true;
                case ReplCommand.ReevaluateCurrentLine:
                    await ReadEvalPrintLoop(windowvm, linevm, LineOperation.Reevaluate);
                    return true;
                case ReplCommand.CancelLine when !linevm.IsTextSelected(): // if text is selected, assume the user wants to copy
                    await ReadEvalPrintLoop(windowvm, linevm, LineOperation.NoEvaluate);
                    return true;
                case ReplCommand.CancelLine:
                    return false;
                case ReplCommand.CyclePreviousLine:
                    CycleThroughHistory(windowvm, linevm, previousHistoryPointer, -1);
                    return true;
                case ReplCommand.CycleNextLine:
                    CycleThroughHistory(windowvm, linevm, previousHistoryPointer, +1);
                    return true;
                case ReplCommand.OpenIntellisense:
                    await CompleteCode(windowvm, linevm);
                    return true;
                case ReplCommand.GoToFirstLine:
                    windowvm.FocusIndex = windowvm.MinimumFocusIndex;
                    return true;
                case ReplCommand.GoToLastLine:
                    windowvm.FocusIndex = windowvm.Entries.Count - 1;
                    return true;
                case ReplCommand.ClearScreen:
                    ClearScreen(windowvm);
                    return true;
                case ReplCommand.SaveSession:
                    await new SaveDialog(services).SaveAsync(windowvm.Entries);
                    return true;

                case ReplCommand.LineDown when linevm.IsCaretOnFinalLine():
                    windowvm.FocusIndex++;
                    return true;
                case ReplCommand.LineUp when linevm.IsCaretOnFirstLine():
                    windowvm.FocusIndex--;
                    return true;
                case ReplCommand.SmartPaste:
                    await HandleSmartPaste(linevm, Clipboard.GetText());
                    return true;
                case ReplCommand.LineUp:
                case ReplCommand.LineDown:
                    // don't intercept keyboard for these commands.
                    return false;
                default:
                    throw new ArgumentOutOfRangeException("Unknown command " + cmd);
            }
        }
    }
}
