using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace BoomBx.Views
{
    public partial class InputDialog : Window
    {
        public static readonly StyledProperty<string> PromptProperty =
            AvaloniaProperty.Register<InputDialog, string>(nameof(Prompt));
        
        public static readonly StyledProperty<string> InputTextProperty =
            AvaloniaProperty.Register<InputDialog, string>(nameof(InputText));

        public string Prompt
        {
            get => GetValue(PromptProperty);
            set => SetValue(PromptProperty, value);
        }

        public string InputText
        {
            get => GetValue(InputTextProperty);
            set => SetValue(InputTextProperty, value);
        }

        public InputDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close(InputText);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}