using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace PackingTracker.Gui;

// One category card in the grouped packing list.
public sealed class CategoryGroupViewModel : ViewModelBase
{
    private readonly Action<CategoryGroupViewModel>? _toggled;
    private bool _isExpanded;

    public CategoryGroupViewModel(
        string category,
        List<PackingItemViewModel> items,
        bool isExpanded,
        Action<CategoryGroupViewModel>? toggled = null)
    {
        Category = category;
        Items = items;
        AccentBrush = CreateAccentBrush(category);
        _isExpanded = isExpanded;
        _toggled = toggled;
        ToggleCommand = new RelayCommand(ToggleExpanded);
    }

    public string Category { get; }
    public List<PackingItemViewModel> Items { get; }
    public IBrush AccentBrush { get; }
    public ICommand ToggleCommand { get; }
    public int TotalQuantity => Items.Sum(item => item.Quantity);
    public int PackedQuantity => Items.Where(item => item.IsPacked).Sum(item => item.Quantity);
    public int ProgressPercent => TotalQuantity == 0 ? 0 : PackedQuantity * 100 / TotalQuantity;
    public string CountLabel => TotalQuantity == 1 ? "1 item" : $"{TotalQuantity} items";
    public string ProgressLabel => $"{PackedQuantity}/{TotalQuantity}";
    public CornerRadius HeaderCornerRadius => IsExpanded ? new CornerRadius(10, 10, 0, 0) : new CornerRadius(10);
    public string ToggleGlyph => IsExpanded ? "-" : "+";
    public string ToggleToolTip => IsExpanded ? "Collapse category" : "Expand category";

    public bool IsExpanded
    {
        get
        {
            return _isExpanded;
        }
        private set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(HeaderCornerRadius));
            OnPropertyChanged(nameof(ToggleGlyph));
            OnPropertyChanged(nameof(ToggleToolTip));
        }
    }

    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        // Let the main ViewModel remember this category's state while filters refresh the list.
        _toggled?.Invoke(this);
    }

    public void SetExpanded(bool isExpanded)
    {
        IsExpanded = isExpanded;
    }

    private static IBrush CreateAccentBrush(string category)
    {
        return category switch
        {
            "Clothing" => new SolidColorBrush(Color.Parse("#06B6D4")),
            "Shoes" => new SolidColorBrush(Color.Parse("#2563EB")),
            "Toiletries" => new SolidColorBrush(Color.Parse("#14B8A6")),
            "Documents" => new SolidColorBrush(Color.Parse("#F59E0B")),
            "Medications" => new SolidColorBrush(Color.Parse("#10B981")),
            "Accessories" => new SolidColorBrush(Color.Parse("#F97316")),
            "Electronics" => new SolidColorBrush(Color.Parse("#8B5CF6")),
            "Travel Gear" => new SolidColorBrush(Color.Parse("#0EA5E9")),
            "Food & Drinks" => new SolidColorBrush(Color.Parse("#84CC16")),
            _ => new SolidColorBrush(Color.Parse("#E11D48"))
        };
    }

}
