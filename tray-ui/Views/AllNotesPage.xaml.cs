using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using tray_ui.Models;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace tray_ui.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class AllNotesPage : Page
{
    private AllNotes notesModel = new AllNotes();

    public AllNotesPage()
    {
        InitializeComponent();
    }

    private void NewNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(NotePage));
    }

    private void ItemsView_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        Frame.Navigate(typeof(NotePage), args.InvokedItem);
    }
}