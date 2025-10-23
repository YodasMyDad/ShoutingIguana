using CommunityToolkit.Mvvm.ComponentModel;

namespace ShoutingIguana.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _welcomeMessage = "Hello World";

    public MainViewModel()
    {
        // Constructor ready for future DI injection of services
    }
}

