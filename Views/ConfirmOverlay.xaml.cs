using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Proofly.Views
{
    /// <summary>
    /// A confirmation shown inside the window, dimming everything behind it.
    /// Clicking outside the card or pressing Escape cancels.
    /// </summary>
    public partial class ConfirmOverlay : UserControl
    {
        private Action _onFirst;
        private Action _onSecond;
        private Action _onCancel;
        private Action<string> _onText;

        public ConfirmOverlay()
        {
            InitializeComponent();
            Visibility = Visibility.Collapsed;
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        public void Open(
            string title,
            string message,
            string firstLabel,
            bool firstDestructive,
            Action onFirst,
            string secondLabel = null,
            Action onSecond = null,
            Action onCancel = null)
        {
            TitleText.Text = title;
            MessageText.Text = message;
            InputBox.Visibility = Visibility.Collapsed;
            _onText = null;

            FirstButton.Content = firstLabel;
            FirstButton.Style = (Style)FindResource(firstDestructive ? "DangerButton" : "PrimaryButton");

            SecondButton.Visibility = secondLabel == null ? Visibility.Collapsed : Visibility.Visible;
            SecondButton.Content = secondLabel ?? "";
            SecondButton.Style = (Style)FindResource("DangerButton");

            _onFirst = onFirst;
            _onSecond = onSecond;
            _onCancel = onCancel;

            Visibility = Visibility.Visible;
            FirstButton.Focus();
        }

        /// <summary>Asks for a short piece of text, such as a session name.</summary>
        public void OpenPrompt(string title, string message, string initialText, string confirmLabel, Action<string> onConfirm)
        {
            Open(title, message, confirmLabel, false, null);
            _onText = onConfirm;
            InputBox.Text = initialText ?? "";
            InputBox.Visibility = Visibility.Visible;
            InputBox.Focus();
            InputBox.SelectAll();
        }

        private void Finish(Action action)
        {
            Visibility = Visibility.Collapsed;
            _onFirst = null;
            _onSecond = null;
            _onCancel = null;
            _onText = null;
            if (action != null) action();
        }

        private void Confirm()
        {
            Action<string> onText = _onText;
            if (onText == null)
            {
                Finish(_onFirst);
                return;
            }
            string text = InputBox.Text.Trim();
            // An empty name is not accepted, the dialog simply stays open.
            if (text.Length == 0) return;
            Finish(null);
            onText(text);
        }

        private void First_Click(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void Second_Click(object sender, RoutedEventArgs e)
        {
            Finish(_onSecond);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Finish(_onCancel);
        }

        private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Finish(_onCancel);
        }

        private void Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Keeps a click on the card from reaching the backdrop behind it.
            e.Handled = true;
        }

        private void Confirm_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(_onCancel);
            }
            else if (e.Key == Key.Enter && _onText != null)
            {
                e.Handled = true;
                Confirm();
            }
        }
    }
}
