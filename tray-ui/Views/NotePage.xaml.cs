using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using tray_ui.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace tray_ui.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class NotePage : Page
{
    private Note? noteModel;

    public NotePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Note note)
        {
            noteModel = note;
        }
        else
        {
            noteModel = new Note();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (noteModel is not null)
        {
            await noteModel.SaveAsync();

            Frame.GoBack();
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (noteModel is not null)
        {
            await noteModel.DeleteAsync();
        }

        if (Frame.CanGoBack == true)
        {
            Frame.GoBack();
        }
    }
}