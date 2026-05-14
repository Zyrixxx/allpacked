using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PackingTracker.Gui;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const double ExpandedSidebarWidth = 320;
    private const double CollapsedSidebarWidth = 72;
    private const string DefaultProfileName = "Default";
    private const string MiscellaneousCategoryName = "Miscellaneous";
    private const string AllItemsFilterName = "All items";
    private const string PackedFilterName = "Packed";
    private const string UnpackedFilterName = "Unpacked";

    private static readonly string[] DefaultCategories =
    {
        "Clothing",
        "Shoes",
        "Toiletries",
        "Documents",
        "Medications",
        "Accessories",
        "Electronics",
        "Travel Gear",
        "Food & Drinks",
        MiscellaneousCategoryName
    };

    private readonly IDialogService _dialogService;
    private readonly ObservableCollection<PackingItemViewModel> _items = new();
    private readonly Dictionary<string, bool> _categoryExpandedStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _isBulkUpdatingItems;
    private bool _isInitialized;
    private int _filteredItemCount;
    private PackingItemViewModel? _lastDeletedItem;
    private int _lastDeletedIndex = -1;
    private string _lastDeletedProfileName = "";
    private string _newItemName = "";
    private string _quantityText = "1";
    private string? _selectedCategory;
    private string? _selectedSavedProfile;
    private string _profileName = DefaultProfileName;
    private string _profileNameInput = DefaultProfileName;
    private string _searchText = "";
    private string _selectedStatusFilter = AllItemsFilterName;
    private string _statusMessage = "Ready.";
    private bool _statusToastVisible;
    private double _statusToastOpacity;
    private bool _undoDeleteVisible;
    private bool _isSidebarCollapsed;
    private GridLength _sidebarWidth = new(ExpandedSidebarWidth);
    private Thickness _sidebarPadding = new(24);
    private CancellationTokenSource? _statusToastHideTokenSource;

    public MainWindowViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;

        // Buttons in MainWindow.axaml bind to these commands instead of using code-behind Click handlers.
        AddItemCommand = new RelayCommand(AddItem);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        CollapseAllCategoriesCommand = new RelayCommand(CollapseAllCategories);
        DeleteSelectedProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync);
        ExpandAllCategoriesCommand = new RelayCommand(ExpandAllCategories);
        LoadProfileCommand = new RelayCommand(LoadProfileFromInput);
        LoadSelectedProfileCommand = new RelayCommand(LoadSelectedProfile);
        PackAllVisibleCommand = new RelayCommand(() => SetVisibleItemsPacked(true));
        SaveCommand = new RelayCommand(SaveFromCommand);
        ToggleSidebarCommand = new RelayCommand(() => SetSidebarCollapsed(!_isSidebarCollapsed));
        UndoDeleteCommand = new RelayCommand(UndoDelete);
        UnpackAllVisibleCommand = new RelayCommand(() => SetVisibleItemsPacked(false));

        ResetCategories();
        LoadStartupProfile();
        SetSidebarCollapsed(true);
        _isInitialized = true;
    }

    public ObservableCollection<CategoryGroupViewModel> CategoryGroups { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> SavedProfiles { get; } = new();
    public IReadOnlyList<string> StatusFilters { get; } = new[] { AllItemsFilterName, UnpackedFilterName, PackedFilterName };

    public ICommand AddItemCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand CollapseAllCategoriesCommand { get; }
    public ICommand DeleteSelectedProfileCommand { get; }
    public ICommand ExpandAllCategoriesCommand { get; }
    public ICommand LoadProfileCommand { get; }
    public ICommand LoadSelectedProfileCommand { get; }
    public ICommand PackAllVisibleCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand UndoDeleteCommand { get; }
    public ICommand UnpackAllVisibleCommand { get; }

    public string NewItemName
    {
        get => _newItemName;
        set => SetProperty(ref _newItemName, value);
    }

    public string QuantityText
    {
        get => _quantityText;
        set => SetProperty(ref _quantityText, value);
    }

    public string? SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public string? SelectedSavedProfile
    {
        get => _selectedSavedProfile;
        set => SetProperty(ref _selectedSavedProfile, value);
    }

    public string ProfileName
    {
        get => _profileName;
        private set
        {
            if (!SetProperty(ref _profileName, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CollapsedProfileInitialText));
        }
    }

    public string ProfileNameInput
    {
        get => _profileNameInput;
        set => SetProperty(ref _profileNameInput, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value) || !_isInitialized)
            {
                return;
            }

            RefreshDashboard();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (!SetProperty(ref _selectedStatusFilter, value) || !_isInitialized)
            {
                return;
            }

            RefreshDashboard();
        }
    }

    public string ProfileItemCountText => TotalQuantity == 1 ? "1 item" : $"{TotalQuantity} items";
    public string ProfilePackedCountText => $"{PackedQuantity} packed";
    public string CollapsedProfileInitialText => GetProfileInitial(ProfileName);
    public string CollapsedItemCountText => TotalQuantity.ToString();
    public string CollapsedPackedCountText => PackedQuantity.ToString();
    public string PackedCountText => $"{PackedQuantity} / {TotalQuantity} packed";
    public string ProgressText => $"{ProgressPercent}%";
    public bool IsEverythingPacked => TotalQuantity > 0 && PackedQuantity == TotalQuantity;
    public bool HasVisibleItems => _filteredItemCount > 0;
    public bool HasNoVisibleItems => !HasVisibleItems;
    public string EmptyStateTitle => GetEmptyStateTitle();
    public string EmptyStateHint => GetEmptyStateHint();
    public bool IsExpandedSidebarVisible => !_isSidebarCollapsed;
    public bool IsCollapsedSidebarVisible => _isSidebarCollapsed;

    public GridLength SidebarWidth
    {
        get => _sidebarWidth;
        private set => SetProperty(ref _sidebarWidth, value);
    }

    public Thickness SidebarPadding
    {
        get => _sidebarPadding;
        private set => SetProperty(ref _sidebarPadding, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool StatusToastVisible
    {
        get => _statusToastVisible;
        private set => SetProperty(ref _statusToastVisible, value);
    }

    public double StatusToastOpacity
    {
        get => _statusToastOpacity;
        private set => SetProperty(ref _statusToastOpacity, value);
    }

    public bool UndoDeleteVisible
    {
        get => _undoDeleteVisible;
        private set => SetProperty(ref _undoDeleteVisible, value);
    }

    private int TotalQuantity => _items.Sum(item => item.Quantity);
    private int PackedQuantity => _items.Where(item => item.IsPacked).Sum(item => item.Quantity);
    private int ProgressPercent => TotalQuantity == 0 ? 0 : PackedQuantity * 100 / TotalQuantity;

    public void SaveCurrentProfile(bool refreshProfiles = false)
    {
        // Persistence stays in PackingStorage; the ViewModel converts UI rows back to plain models.
        PackingStorage.SaveItems(ProfileName, _items.Select(item => item.ToPackingItem()).ToList());

        if (refreshProfiles)
        {
            RefreshProfiles(ProfileName);
        }
    }

    private void LoadSelectedProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedSavedProfile))
        {
            ShowStatus("Select a saved profile first.");
            return;
        }

        LoadProfile(SelectedSavedProfile);
    }

    private void LoadProfileFromInput()
    {
        string requestedProfileName = GetProfileNameFromInput();

        if (ShouldReplaceStarterProfile(requestedProfileName))
        {
            ReplaceStarterProfile(requestedProfileName);
            return;
        }

        bool profileAlreadyExists = PackingStorage.ProfileExists(requestedProfileName);
        LoadProfile(requestedProfileName);

        if (!profileAlreadyExists)
        {
            SaveCurrentProfile(refreshProfiles: true);
            ShowStatus($"{ProfileName} profile created and saved.");
        }
    }

    private async Task DeleteSelectedProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSavedProfile))
        {
            ShowStatus("Select a saved profile to delete.");
            return;
        }

        string selectedProfile = SelectedSavedProfile;
        bool confirmed = await _dialogService.ConfirmAsync(
            "Delete profile?",
            $"Delete the {selectedProfile} profile and all saved items in it?");

        if (!confirmed)
        {
            ShowStatus("Profile deletion canceled.");
            return;
        }

        bool deleted = PackingStorage.DeleteProfile(selectedProfile);

        if (!deleted)
        {
            ShowStatus($"{selectedProfile} could not be deleted.");
            RefreshProfiles(ProfileName);
            return;
        }

        LoadProfile(GetNextProfileName());
        ShowStatus($"{selectedProfile} deleted.");
    }

    private string GetNextProfileName()
    {
        List<string> profiles = PackingStorage.GetProfiles();
        return profiles.Count > 0 ? profiles[0] : DefaultProfileName;
    }

    private void SaveFromCommand()
    {
        SaveCurrentProfile(refreshProfiles: true);
        ShowStatus($"Saved {_items.Count} item(s) for {ProfileName}.");
    }

    private void SetSidebarCollapsed(bool isCollapsed)
    {
        _isSidebarCollapsed = isCollapsed;
        SidebarWidth = new GridLength(isCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth);
        SidebarPadding = isCollapsed ? new Thickness(0, 18) : new Thickness(24);
        OnPropertyChanged(nameof(IsExpandedSidebarVisible));
        OnPropertyChanged(nameof(IsCollapsedSidebarVisible));
    }

    private void AddItem()
    {
        string name = NewItemName.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus("Enter an item name first.");
            return;
        }

        if (!int.TryParse(QuantityText, out int quantity) || quantity <= 0)
        {
            ShowStatus("Quantity must be a positive number.");
            return;
        }

        AddItemToList(CreateItemViewModel(new PackingItem
        {
            Name = name,
            Quantity = quantity,
            Category = GetSelectedCategory(),
            IsPacked = false
        }));

        HideUndoDelete();
        NewItemName = "";
        QuantityText = "1";
        ShowStatus($"{name} added.");
        RefreshDashboard();
        SaveCurrentProfile(refreshProfiles: true);
    }

    private string GetSelectedCategory()
    {
        return string.IsNullOrWhiteSpace(SelectedCategory) ? MiscellaneousCategoryName : SelectedCategory;
    }

    private void LoadProfile(string profileName)
    {
        ProfileName = PackingStorage.CleanProfileName(profileName);
        ProfileNameInput = ProfileName;
        HideUndoDelete();
        // Profiles should open with categories collapsed, independent from the previous profile's state.
        _categoryExpandedStates.Clear();
        ResetCategories();
        ClearItems();

        foreach (PackingItem item in PackingStorage.LoadItems(ProfileName))
        {
            item.Category = AddCategoryIfMissing(item.Category);
            AddItemToList(CreateItemViewModel(item));
        }

        ShowStatus($"Loaded {ProfileName}.");
        RefreshDashboard();
        RefreshProfiles(ProfileName);
        PackingStorage.SaveLastProfile(ProfileName);
    }

    private bool ShouldReplaceStarterProfile(string requestedProfileName)
    {
        return IsDefaultProfile(ProfileName) &&
            !IsDefaultProfile(requestedProfileName) &&
            !HasUserCreatedProfile();
    }

    private void ReplaceStarterProfile(string profileName)
    {
        PackingStorage.DeleteProfile(DefaultProfileName);

        ProfileName = PackingStorage.CleanProfileName(profileName);
        ProfileNameInput = ProfileName;
        HideUndoDelete();
        SaveCurrentProfile(refreshProfiles: true);
        RefreshDashboard();
        ShowStatus($"{ProfileName} profile created and saved.");
    }

    private static bool IsDefaultProfile(string profileName)
    {
        return string.Equals(
            profileName.Trim(),
            DefaultProfileName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUserCreatedProfile()
    {
        return PackingStorage.GetProfiles().Any(profile => !IsDefaultProfile(profile));
    }

    private void LoadStartupProfile()
    {
        List<string> profiles = PackingStorage.GetProfiles();
        string? lastProfile = PackingStorage.LoadLastProfile();

        if (!string.IsNullOrWhiteSpace(lastProfile) && profiles.Contains(lastProfile))
        {
            LoadProfile(lastProfile);
            return;
        }

        if (profiles.Count > 0)
        {
            LoadProfile(profiles[0]);
            return;
        }

        LoadProfile(DefaultProfileName);
    }

    private string GetProfileNameFromInput()
    {
        return PackingStorage.CleanProfileName(ProfileNameInput.Trim());
    }

    private void ResetCategories()
    {
        Categories.Clear();

        foreach (string category in DefaultCategories)
        {
            Categories.Add(category);
        }

        SelectedCategory = Categories.Count > 0 ? Categories[0] : null;
    }

    private string AddCategoryIfMissing(string categoryName)
    {
        string cleanedCategoryName = CleanCategoryName(categoryName);
        string? existingCategory = Categories.FirstOrDefault(category =>
            string.Equals(category, cleanedCategoryName, StringComparison.OrdinalIgnoreCase));

        if (existingCategory is not null)
        {
            return existingCategory;
        }

        Categories.Add(cleanedCategoryName);
        return cleanedCategoryName;
    }

    private static string CleanCategoryName(string categoryName)
    {
        string cleanedCategoryName = categoryName.Trim().Replace("|", "-");
        return string.IsNullOrWhiteSpace(cleanedCategoryName) ? MiscellaneousCategoryName : cleanedCategoryName;
    }

    private void RefreshDashboard()
    {
        // Rebuild derived UI state after item, filter, or packed-status changes.
        RefreshCategoryGroups();
        RaiseDashboardPropertiesChanged();
    }

    private void RaiseDashboardPropertiesChanged()
    {
        OnPropertyChanged(nameof(ProfileItemCountText));
        OnPropertyChanged(nameof(ProfilePackedCountText));
        OnPropertyChanged(nameof(CollapsedItemCountText));
        OnPropertyChanged(nameof(CollapsedPackedCountText));
        OnPropertyChanged(nameof(PackedCountText));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(IsEverythingPacked));
        OnPropertyChanged(nameof(HasVisibleItems));
        OnPropertyChanged(nameof(HasNoVisibleItems));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
    }

    private static string GetProfileInitial(string profileName)
    {
        string cleanedProfileName = profileName.Trim();
        return string.IsNullOrWhiteSpace(cleanedProfileName)
            ? "D"
            : char.ToUpperInvariant(cleanedProfileName[0]).ToString();
    }

    private PackingItemViewModel CreateItemViewModel(PackingItem item)
    {
        return new PackingItemViewModel(item, EditItemAsync, DeleteItemAsync);
    }

    private void AddItemToList(PackingItemViewModel item)
    {
        item.PropertyChanged += PackingItem_PropertyChanged;
        _items.Add(item);
    }

    private void ClearItems()
    {
        foreach (PackingItemViewModel item in _items)
        {
            item.PropertyChanged -= PackingItem_PropertyChanged;
        }

        _items.Clear();
    }

    private void PackingItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackingItemViewModel.IsPacked))
        {
            RefreshDashboard();

            if (!_isBulkUpdatingItems)
            {
                SaveCurrentProfile();
            }
        }
    }

    private async Task EditItemAsync(PackingItemViewModel item)
    {
        PackingItemEditResult? editResult = await _dialogService.EditItemAsync(item.ToPackingItem(), Categories);

        if (editResult is null)
        {
            ShowStatus("Edit canceled.");
            return;
        }

        item.Name = editResult.Name;
        item.Quantity = editResult.Quantity;
        item.Category = AddCategoryIfMissing(editResult.Category);

        HideUndoDelete();
        ShowStatus($"{item.Name} updated.");
        RefreshDashboard();
        SaveCurrentProfile();
    }

    private async Task DeleteItemAsync(PackingItemViewModel item)
    {
        bool confirmed = await _dialogService.ConfirmAsync(
            "Delete item?",
            $"Remove {item.Name} from this packing list?");

        if (!confirmed)
        {
            ShowStatus("Item deletion canceled.");
            return;
        }

        item.PropertyChanged -= PackingItem_PropertyChanged;
        _lastDeletedItem = item;
        _lastDeletedIndex = _items.IndexOf(item);
        _lastDeletedProfileName = ProfileName;
        _items.Remove(item);
        ShowUndoDelete($"{item.Name} removed.");
        RefreshDashboard();
        SaveCurrentProfile();
    }

    private void UndoDelete()
    {
        if (_lastDeletedItem is null || _lastDeletedProfileName != ProfileName)
        {
            HideUndoDelete();
            ShowStatus("There is nothing to undo.");
            return;
        }

        int insertIndex = _lastDeletedIndex;

        if (insertIndex < 0 || insertIndex > _items.Count)
        {
            insertIndex = _items.Count;
        }

        _lastDeletedItem.PropertyChanged += PackingItem_PropertyChanged;
        _items.Insert(insertIndex, _lastDeletedItem);
        AddCategoryIfMissing(_lastDeletedItem.Category);

        string restoredName = _lastDeletedItem.Name;
        HideUndoDelete();
        ShowStatus($"{restoredName} restored.");
        RefreshDashboard();
        SaveCurrentProfile();
    }

    private void RefreshProfiles(string selectedProfile)
    {
        List<string> profiles = PackingStorage.GetProfiles();
        SavedProfiles.Clear();

        foreach (string profile in profiles)
        {
            SavedProfiles.Add(profile);
        }

        SelectedSavedProfile = profiles.Contains(selectedProfile) ? selectedProfile : null;
    }

    private void RefreshCategoryGroups()
    {
        CategoryGroups.Clear();

        List<PackingItemViewModel> filteredItems = _items
            .Where(ItemMatchesFilters)
            .ToList();
        Dictionary<string, List<PackingItemViewModel>> itemsByCategory = GroupItemsByCategory(filteredItems);

        _filteredItemCount = filteredItems.Count;

        foreach (string category in GetVisibleCategories(itemsByCategory.Keys))
        {
            if (!itemsByCategory.TryGetValue(category, out List<PackingItemViewModel>? categoryItems))
            {
                continue;
            }

            bool isExpanded = _categoryExpandedStates.TryGetValue(category, out bool savedState) && savedState;
            CategoryGroups.Add(new CategoryGroupViewModel(category, categoryItems, isExpanded, StoreCategoryExpandedState));
        }
    }

    private static Dictionary<string, List<PackingItemViewModel>> GroupItemsByCategory(List<PackingItemViewModel> items)
    {
        var itemsByCategory = new Dictionary<string, List<PackingItemViewModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (PackingItemViewModel item in items)
        {
            if (!itemsByCategory.TryGetValue(item.Category, out List<PackingItemViewModel>? categoryItems))
            {
                categoryItems = new List<PackingItemViewModel>();
                itemsByCategory[item.Category] = categoryItems;
            }

            categoryItems.Add(item);
        }

        return itemsByCategory;
    }

    private List<string> GetVisibleCategories(IEnumerable<string> filteredCategories)
    {
        var categories = new List<string>(Categories);
        var seenCategories = new HashSet<string>(Categories, StringComparer.OrdinalIgnoreCase);

        foreach (string category in filteredCategories)
        {
            if (seenCategories.Add(category))
            {
                categories.Add(category);
            }
        }

        return categories;
    }

    private bool ItemMatchesFilters(PackingItemViewModel item)
    {
        return ItemMatchesSearch(item) && ItemMatchesStatusFilter(item);
    }

    private bool ItemMatchesSearch(PackingItemViewModel item)
    {
        string searchText = SearchText.Trim();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            item.Category.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool ItemMatchesStatusFilter(PackingItemViewModel item)
    {
        if (SelectedStatusFilter == PackedFilterName)
        {
            return item.IsPacked;
        }

        if (SelectedStatusFilter == UnpackedFilterName)
        {
            return !item.IsPacked;
        }

        return true;
    }

    private bool HasActiveFilters()
    {
        return !string.IsNullOrWhiteSpace(SearchText) || SelectedStatusFilter != AllItemsFilterName;
    }

    private void StoreCategoryExpandedState(CategoryGroupViewModel group)
    {
        _categoryExpandedStates[group.Category] = group.IsExpanded;
    }

    private void ExpandAllCategories()
    {
        SetAllCategoriesExpanded(true);
        ShowStatus("All categories expanded.");
    }

    private void CollapseAllCategories()
    {
        SetAllCategoriesExpanded(false);
        ShowStatus("All categories collapsed.");
    }

    private void SetVisibleItemsPacked(bool isPacked)
    {
        List<PackingItemViewModel> visibleItems = _items
            .Where(ItemMatchesFilters)
            .ToList();

        _isBulkUpdatingItems = true;

        try
        {
            foreach (PackingItemViewModel item in visibleItems)
            {
                item.IsPacked = isPacked;
            }
        }
        finally
        {
            _isBulkUpdatingItems = false;
        }

        RefreshDashboard();
        SaveCurrentProfile();

        string action = isPacked ? "packed" : "unpacked";
        string itemLabel = visibleItems.Count == 1 ? "item" : "items";
        ShowStatus($"{visibleItems.Count} {itemLabel} {action}.");
    }

    private void SetAllCategoriesExpanded(bool isExpanded)
    {
        foreach (CategoryGroupViewModel group in CategoryGroups)
        {
            _categoryExpandedStates[group.Category] = isExpanded;
            group.SetExpanded(isExpanded);
        }

        RefreshCategoryGroups();
    }

    private string GetEmptyStateTitle()
    {
        if (_items.Count == 0)
        {
            return "Start this packing list";
        }

        if (HasActiveFilters())
        {
            return "No matching items";
        }

        return "No visible items";
    }

    private string GetEmptyStateHint()
    {
        if (_items.Count == 0)
        {
            return "Use the item bar above to add the first thing you need to bring.";
        }

        if (HasActiveFilters())
        {
            return "Try a different search or clear the current filter.";
        }

        return "Add an item above to bring your packing list back.";
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        ShowStatusToast(keepVisible: false);
    }

    private void ShowUndoDelete(string message)
    {
        StatusMessage = message;
        UndoDeleteVisible = true;
        ShowStatusToast(keepVisible: true);
    }

    private void HideUndoDelete()
    {
        _lastDeletedItem = null;
        _lastDeletedIndex = -1;
        _lastDeletedProfileName = "";
        UndoDeleteVisible = false;
    }

    private async void ShowStatusToast(bool keepVisible)
    {
        _statusToastHideTokenSource?.Cancel();
        StatusToastVisible = true;
        StatusToastOpacity = 1;

        if (keepVisible)
        {
            return;
        }

        var hideTokenSource = new CancellationTokenSource();
        _statusToastHideTokenSource = hideTokenSource;

        try
        {
            await Task.Delay(2800, hideTokenSource.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (_statusToastHideTokenSource != hideTokenSource)
        {
            return;
        }

        StatusToastOpacity = 0;

        try
        {
            await Task.Delay(220, hideTokenSource.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (_statusToastHideTokenSource == hideTokenSource)
        {
            StatusToastVisible = false;
        }
    }

    private void ClearFilters()
    {
        SearchText = "";
        SelectedStatusFilter = AllItemsFilterName;
        RefreshDashboard();
        ShowStatus("Filters cleared.");
    }

}
