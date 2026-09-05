using System.Windows;
using AutoHitCounter.ViewModels;

namespace AutoHitCounter.Views.Controls;


public partial class SplitEntityEditorWindow : Window
{
    public SplitEntityEditorWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(SplitEntityEditorViewModel vm)
    {
        DataContext = vm;
        vm.RequestClose = Close;
    }
}
