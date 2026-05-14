using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PackingTracker.Gui;

// One row in the packing list. It wraps item data plus the commands used by that row's buttons.
public sealed class PackingItemViewModel : ViewModelBase
{
    private string _name = "";
    private int _quantity;
    private string _category = "";
    private bool _isPacked;

    public PackingItemViewModel(
        PackingItem item,
        Func<PackingItemViewModel, Task> editItemAsync,
        Func<PackingItemViewModel, Task> deleteItemAsync)
    {
        _name = item.Name;
        _quantity = item.Quantity;
        _category = item.Category;
        _isPacked = item.IsPacked;

        EditCommand = new AsyncRelayCommand(() => editItemAsync(this));
        DeleteCommand = new AsyncRelayCommand(() => deleteItemAsync(this));
    }

    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int Quantity
    {
        get => _quantity;
        set => SetProperty(ref _quantity, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public bool IsPacked
    {
        get => _isPacked;
        set
        {
            if (!SetProperty(ref _isPacked, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsUnpacked));
            OnPropertyChanged(nameof(PackedStatus));
        }
    }

    public bool IsUnpacked => !IsPacked;
    public string PackedStatus => IsPacked ? "Packed" : "Unpacked";

    public PackingItem ToPackingItem()
    {
        // Storage still works with the simple model type, not UI-facing ViewModels.
        return new PackingItem
        {
            Name = Name,
            Quantity = Quantity,
            Category = Category,
            IsPacked = IsPacked
        };
    }
}
