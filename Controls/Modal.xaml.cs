using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EarningsTracker.Controls
{
    /// <summary>
    /// Interaction logic for Modal.xaml
    /// </summary>
    public partial class Modal : UserControl
    {

        public event EventHandler? CloseRequested;
        private IInputElement? _returnFocus;
        public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }
        public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
            nameof(IsBusy), typeof(bool), typeof(Modal), new PropertyMetadata(false));

        public Modal()
        {
            InitializeComponent();
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible)
                    FocusContent();
                else
                    RestoreFocus();
            };
            KeyDown += Modal_KeyDown;
        }

        public void Open(string title, object content)
        {
            // Capture before the background is disabled by the visibility binding.
            if (Visibility != Visibility.Visible)
                _returnFocus = Keyboard.FocusedElement;
            Title = title;
            ModalContent = content;
            SetHeaderVisible(true);
            Visibility = Visibility.Visible;
            FocusContent();
        }

        private void FocusContent()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!IsVisible || ModalContent == null)
                    return;
                if (!ModalContentPresenter.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)) &&
                    !MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
                    Focus();
            }));
        }

        private void RestoreFocus()
        {
            var target = _returnFocus;
            _returnFocus = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!IsVisible && target is UIElement { IsVisible: true, IsEnabled: true, Focusable: true })
                    Keyboard.Focus(target);
            }));
        }

        private void Modal_KeyDown(object sender, KeyEventArgs e)
        {
            // Dropdowns consume Escape first. Completed session imports keep their Done workflow.
            if (e.Key == Key.Escape && !e.Handled && !IsBusy && ModalHeader.Visibility == Visibility.Visible)
            {
                Close();
                e.Handled = true;
            }
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public object? ModalContent
        {
            get => GetValue(ModalContentProperty);
            set => SetValue(ModalContentProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(Modal),
                new PropertyMetadata("")
            );

        public static readonly DependencyProperty ModalContentProperty =
            DependencyProperty.Register(
                nameof(ModalContent),
                typeof(object),
                typeof(Modal),
                new PropertyMetadata(null, ModalContentChanged)
            );

        private static void ModalContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            // Collapsing the modal does not detach its content or release an importer's context.
            if (!ReferenceEquals(e.OldValue, e.NewValue) && e.OldValue is IDisposable content)
                content.Dispose();
            if (e.NewValue != null && sender is Modal modal && modal.IsVisible)
                modal.FocusContent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void Close()
        {
            if (IsBusy) return; // A commit cannot be cancelled by detaching its dialog.
            ModalContent = null;
            Visibility = Visibility.Collapsed;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetHeaderVisible(bool visible)
        {
            ModalHeader.Visibility = visible
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (IsVisible)
                FocusContent();
        }
    }
}
