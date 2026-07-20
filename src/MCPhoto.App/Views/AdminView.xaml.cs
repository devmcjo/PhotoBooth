using System.Windows.Controls;
using MCPhoto.App.ViewModels;

namespace MCPhoto.App.Views;

public partial class AdminView : UserControl
{
    public AdminView() => InitializeComponent();

    private void OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AdminViewModel vm && sender is PasswordBox pb)
            vm.Password = pb.Password;
    }
}
