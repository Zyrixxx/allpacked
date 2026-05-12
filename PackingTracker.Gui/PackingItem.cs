using System.ComponentModel;

namespace PackingTracker.Gui;

public class PackingItem : INotifyPropertyChanged
{
    private bool isPacked;
    private string name = "";
    private int quantity;
    private string category = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get
        {
            return name;
        }
        set
        {
            if (name == value)
            {
                return;
            }

            name = value;
            OnPropertyChanged(nameof(Name));
        }
    }

    public int Quantity
    {
        get
        {
            return quantity;
        }
        set
        {
            if (quantity == value)
            {
                return;
            }

            quantity = value;
            OnPropertyChanged(nameof(Quantity));
        }
    }

    public string Category
    {
        get
        {
            return category;
        }
        set
        {
            if (category == value)
            {
                return;
            }

            category = value;
            OnPropertyChanged(nameof(Category));
        }
    }

    public bool IsPacked
    {
        get
        {
            return isPacked;
        }
        set
        {
            if (isPacked == value)
            {
                return;
            }

            isPacked = value;
            OnPropertyChanged(nameof(IsPacked));
            OnPropertyChanged(nameof(IsUnpacked));
            OnPropertyChanged(nameof(PackedStatus));
        }
    }

    public string PackedStatus => IsPacked ? "Packed" : "Unpacked";
    public bool IsUnpacked => !IsPacked;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
