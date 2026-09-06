using System.Windows;
using AutoHitCounter.ViewModels;

namespace AutoHitCounter.Views.Controls;

public partial class PhaseSubSplitEditorWindow : Window
{
    public PhaseSubSplitEditorWindow()
    {
        InitializeComponent();
    }

    public void SetViewModel(PhaseSubSplitEditorViewModel vm)
    {
        DataContext = vm;
        vm.RequestClose = Close;
    }
}
