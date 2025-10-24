using System;
using System.Windows;

namespace ShoutingIguana;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Subscribe to Closed event to dispose resources
        Closed += MainWindow_Closed;
    }
    
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        // Dispose DataContext if it implements IDisposable
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

