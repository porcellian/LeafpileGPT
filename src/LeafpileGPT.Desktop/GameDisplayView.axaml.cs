using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace LeafpileGPT.Desktop;

public partial class GameDisplayView : UserControl
{
    private readonly ScrollViewer _transcriptScrollViewer;

    public GameDisplayView()
    {
        AvaloniaXamlLoader.Load(this);
        _transcriptScrollViewer = this.FindControl<ScrollViewer>("TranscriptScrollViewer")!;
        Model = new GameDisplayModel();
        DataContext = Model;
        Model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GameDisplayModel.Transcript))
            {
                Dispatcher.UIThread.Post(_transcriptScrollViewer.ScrollToEnd);
            }
        };
    }

    public GameDisplayModel Model { get; }

    public void FocusPrompt() => this.FindControl<TextBox>("PromptTextBox")?.Focus();

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Model.SubmitCommand();
            e.Handled = true;
        }
    }
}
