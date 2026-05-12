using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PackingTracker.Gui;

public partial class MainWindow : Window
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

    private readonly ObservableCollection<PackingItem> _items = new();
    private readonly ObservableCollection<CategoryGroup> _categoryGroups = new();
    private readonly ObservableCollection<string> _categories = new();
    private readonly Dictionary<string, bool> _categoryExpandedStates = new();
    private bool _completionBannerVisible;
    private bool _isInitialized;
    private bool _isBulkUpdatingItems;
    private int _filteredItemCount;
    private PackingItem? _lastDeletedItem;
    private int _lastDeletedIndex = -1;
    private string _lastDeletedProfileName = "";
    private string _profileName = DefaultProfileName;
    private bool _isSidebarCollapsed;
    private CancellationTokenSource? _statusToastHideTokenSource;

    public MainWindow()
    {
        InitializeComponent();
        _isInitialized = true;
        CategoryGroupsList.ItemsSource = _categoryGroups;
        CategoryBox.ItemsSource = _categories;
        ResetCategories();
        StatusFilterBox.SelectedIndex = 0;
        LoadStartupProfile();
        SetSidebarCollapsed(true);
        Closed += (_, _) => SaveCurrentProfile();
    }

    private void LoadSelectedProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (ProfilesBox.SelectedItem is not string selectedProfile)
        {
            ShowStatus("Select a saved profile first.");
            return;
        }

        LoadProfile(selectedProfile);
    }

    private void LoadProfile_Click(object? sender, RoutedEventArgs e)
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
            ShowStatus($"{_profileName} profile created and saved.");
        }
    }

    private async void DeleteSelectedProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (ProfilesBox.SelectedItem is not string selectedProfile)
        {
            ShowStatus("Select a saved profile to delete.");
            return;
        }

        bool confirmed = await ConfirmAsync(
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
            RefreshProfiles(_profileName);
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

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        SaveCurrentProfile(refreshProfiles: true);
        ShowStatus($"Saved {_items.Count} item(s) for {_profileName}.");
    }

    private void ToggleSidebar_Click(object? sender, RoutedEventArgs e)
    {
        SetSidebarCollapsed(!_isSidebarCollapsed);
    }

    private void SetSidebarCollapsed(bool isCollapsed)
    {
        _isSidebarCollapsed = isCollapsed;
        BodyGrid.ColumnDefinitions[0].Width = new GridLength(
            isCollapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth);
        SidebarRail.Padding = isCollapsed ? new Thickness(0, 18) : new Thickness(24);
        ExpandedSidebar.IsVisible = !isCollapsed;
        CollapsedSidebar.IsVisible = isCollapsed;
    }

    private void AddItem_Click(object? sender, RoutedEventArgs e)
    {
        string name = GetEnteredItemName();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowStatus("Enter an item name first.");
            return;
        }

        if (!TryGetEnteredQuantity(out int quantity))
        {
            ShowStatus("Quantity must be a positive number.");
            return;
        }

        PackingItem item = CreateItem(name, quantity);
        AddItemToList(item);

        HideUndoDelete();
        ResetItemForm();
        ShowStatus($"{name} added.");
        RefreshDashboard();
        SaveCurrentProfile(refreshProfiles: true);
    }

    private string GetEnteredItemName()
    {
        return ItemNameBox.Text?.Trim() ?? "";
    }

    private bool TryGetEnteredQuantity(out int quantity)
    {
        return int.TryParse(QuantityBox.Text, out quantity) && quantity > 0;
    }

    private PackingItem CreateItem(string name, int quantity)
    {
        return new PackingItem
        {
            Name = name,
            Quantity = quantity,
            Category = GetSelectedCategory(),
            IsPacked = false
        };
    }

    private void ResetItemForm()
    {
        ItemNameBox.Text = "";
        QuantityBox.Text = "1";
    }

    private void LoadProfile(string profileName)
    {
        _profileName = PackingStorage.CleanProfileName(profileName);
        ProfileNameBox.Text = _profileName;
        HideUndoDelete();
        ResetCategories();
        ClearItems();

        foreach (PackingItem item in PackingStorage.LoadItems(_profileName))
        {
            item.Category = AddCategoryIfMissing(item.Category);
            AddItemToList(item);
        }

        ShowStatus($"Loaded {_profileName}.");
        RefreshDashboard();
        RefreshProfiles(_profileName);
        PackingStorage.SaveLastProfile(_profileName);
    }

    private bool ShouldReplaceStarterProfile(string requestedProfileName)
    {
        return IsDefaultProfile(_profileName) &&
            !IsDefaultProfile(requestedProfileName) &&
            !HasUserCreatedProfile();
    }

    private void ReplaceStarterProfile(string profileName)
    {
        PackingStorage.DeleteProfile(DefaultProfileName);

        _profileName = PackingStorage.CleanProfileName(profileName);
        ProfileNameBox.Text = _profileName;
        HideUndoDelete();
        SaveCurrentProfile(refreshProfiles: true);
        RefreshDashboard();
        ShowStatus($"{_profileName} profile created and saved.");
    }

    private static bool IsDefaultProfile(string profileName)
    {
        return string.Equals(
            profileName.Trim(),
            DefaultProfileName,
            System.StringComparison.OrdinalIgnoreCase);
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

    private void SaveCurrentProfile(bool refreshProfiles = false)
    {
        PackingStorage.SaveItems(_profileName, _items.ToList());

        if (_isInitialized)
        {
            SaveStateText.Text = "Saved";
        }

        if (refreshProfiles)
        {
            RefreshProfiles(_profileName);
        }
    }

    private string GetProfileNameFromInput()
    {
        string profileName = ProfileNameBox.Text?.Trim() ?? "";

        return PackingStorage.CleanProfileName(profileName);
    }

    private string GetSelectedCategory()
    {
        if (CategoryBox.SelectedItem is string category && !string.IsNullOrWhiteSpace(category))
        {
            return category;
        }

        return MiscellaneousCategoryName;
    }

    private void ResetCategories()
    {
        _categories.Clear();

        foreach (string category in DefaultCategories)
        {
            _categories.Add(category);
        }

        CategoryBox.SelectedIndex = 0;
    }

    private string AddCategoryIfMissing(string categoryName)
    {
        string cleanedCategoryName = CleanCategoryName(categoryName);

        string? existingCategory = _categories.FirstOrDefault(category =>
            string.Equals(category, cleanedCategoryName, System.StringComparison.OrdinalIgnoreCase));

        if (existingCategory is not null)
        {
            return existingCategory;
        }

        _categories.Add(cleanedCategoryName);
        return cleanedCategoryName;
    }

    private static string CleanCategoryName(string categoryName)
    {
        string cleanedCategoryName = categoryName.Trim().Replace("|", "-");
        return string.IsNullOrWhiteSpace(cleanedCategoryName) ? MiscellaneousCategoryName : cleanedCategoryName;
    }

    private void RefreshDashboard()
    {
        UpdateProfileSummary();
        RefreshCategoryGroups();
        UpdateListVisibility();
    }

    private void UpdateProfileSummary()
    {
        int packedCount = _items.Count(item => item.IsPacked);
        int totalCount = _items.Count;
        int progressPercent = totalCount == 0 ? 0 : packedCount * 100 / totalCount;

        ProfileNameText.Text = _profileName;
        ProfileItemCountText.Text = totalCount == 1 ? "1 item" : $"{totalCount} items";
        ProfilePackedCountText.Text = $"{packedCount} packed";
        CollapsedProfileInitialText.Text = GetProfileInitial(_profileName);
        CollapsedItemCountText.Text = totalCount.ToString();
        CollapsedPackedCountText.Text = packedCount.ToString();
        PackedCountText.Text = $"{packedCount} / {totalCount} packed";
        ProgressText.Text = $"{progressPercent}%";
        UpdateCompletionBanner(packedCount, totalCount);
    }

    private static string GetProfileInitial(string profileName)
    {
        string cleanedProfileName = profileName.Trim();
        return string.IsNullOrWhiteSpace(cleanedProfileName)
            ? "D"
            : char.ToUpperInvariant(cleanedProfileName[0]).ToString();
    }

    private void AddItemToList(PackingItem item)
    {
        item.PropertyChanged += PackingItem_PropertyChanged;
        _items.Add(item);
    }

    private void ClearItems()
    {
        foreach (PackingItem item in _items)
        {
            item.PropertyChanged -= PackingItem_PropertyChanged;
        }

        _items.Clear();
    }

    private void PackingItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackingItem.IsPacked))
        {
            RefreshDashboard();

            if (!_isBulkUpdatingItems)
            {
                SaveCurrentProfile();
            }
        }
    }

    private async void EditItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not PackingItem item)
        {
            ShowStatus("Could not find that item.");
            return;
        }

        bool edited = await EditItemAsync(item);

        if (!edited)
        {
            ShowStatus("Edit canceled.");
            return;
        }

        HideUndoDelete();
        ShowStatus($"{item.Name} updated.");
        RefreshDashboard();
        SaveCurrentProfile();
    }

    private async void DeleteItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not PackingItem item)
        {
            ShowStatus("Could not find that item.");
            return;
        }

        bool confirmed = await ConfirmAsync(
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
        _lastDeletedProfileName = _profileName;
        _items.Remove(item);
        ShowUndoDelete($"{item.Name} removed.");
        RefreshDashboard();
        SaveCurrentProfile();
    }

    private void UndoDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (_lastDeletedItem is null || _lastDeletedProfileName != _profileName)
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
        ProfilesBox.ItemsSource = profiles;
        ProfilesBox.SelectedItem = profiles.Contains(selectedProfile) ? selectedProfile : null;
    }

    private void RefreshCategoryGroups()
    {
        _categoryGroups.Clear();

        List<PackingItem> filteredItems = _items
            .Where(ItemMatchesFilters)
            .ToList();
        Dictionary<string, List<PackingItem>> itemsByCategory = GroupItemsByCategory(filteredItems);

        _filteredItemCount = filteredItems.Count;

        foreach (string category in GetVisibleCategories(itemsByCategory.Keys))
        {
            if (!itemsByCategory.TryGetValue(category, out List<PackingItem>? categoryItems))
            {
                continue;
            }

            bool isExpanded = !_categoryExpandedStates.TryGetValue(category, out bool savedState) || savedState;
            _categoryGroups.Add(new CategoryGroup(category, categoryItems, isExpanded));
        }
    }

    private static Dictionary<string, List<PackingItem>> GroupItemsByCategory(List<PackingItem> items)
    {
        var itemsByCategory = new Dictionary<string, List<PackingItem>>(System.StringComparer.OrdinalIgnoreCase);

        foreach (PackingItem item in items)
        {
            if (!itemsByCategory.TryGetValue(item.Category, out List<PackingItem>? categoryItems))
            {
                categoryItems = new List<PackingItem>();
                itemsByCategory[item.Category] = categoryItems;
            }

            categoryItems.Add(item);
        }

        return itemsByCategory;
    }

    private List<string> GetVisibleCategories(IEnumerable<string> filteredCategories)
    {
        var categories = new List<string>(_categories);
        var seenCategories = new HashSet<string>(_categories, System.StringComparer.OrdinalIgnoreCase);

        foreach (string category in filteredCategories)
        {
            if (seenCategories.Add(category))
            {
                categories.Add(category);
            }
        }

        return categories;
    }

    private bool ItemMatchesFilters(PackingItem item)
    {
        return ItemMatchesSearch(item) && ItemMatchesStatusFilter(item);
    }

    private bool ItemMatchesSearch(PackingItem item)
    {
        string searchText = SearchBox.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return item.Name.Contains(searchText, System.StringComparison.OrdinalIgnoreCase) ||
            item.Category.Contains(searchText, System.StringComparison.OrdinalIgnoreCase);
    }

    private bool ItemMatchesStatusFilter(PackingItem item)
    {
        string selectedFilter = GetSelectedStatusFilter();

        if (selectedFilter == PackedFilterName)
        {
            return item.IsPacked;
        }

        if (selectedFilter == UnpackedFilterName)
        {
            return !item.IsPacked;
        }

        return true;
    }

    private string GetSelectedStatusFilter()
    {
        if (StatusFilterBox.SelectedItem is ComboBoxItem item && item.Content is not null)
        {
            return item.Content.ToString() ?? AllItemsFilterName;
        }

        return AllItemsFilterName;
    }

    private bool HasActiveFilters()
    {
        return !string.IsNullOrWhiteSpace(SearchBox.Text) || GetSelectedStatusFilter() != AllItemsFilterName;
    }

    private void ToggleCategory_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not CategoryGroup group)
        {
            return;
        }

        group.ToggleExpanded();
        _categoryExpandedStates[group.Category] = group.IsExpanded;
        e.Handled = true;
    }

    private void ExpandAllCategories_Click(object? sender, RoutedEventArgs e)
    {
        SetAllCategoriesExpanded(true);
        ShowStatus("All categories expanded.");
    }

    private void CollapseAllCategories_Click(object? sender, RoutedEventArgs e)
    {
        SetAllCategoriesExpanded(false);
        ShowStatus("All categories collapsed.");
    }

    private void PackAllVisible_Click(object? sender, RoutedEventArgs e)
    {
        SetVisibleItemsPacked(true);
    }

    private void UnpackAllVisible_Click(object? sender, RoutedEventArgs e)
    {
        SetVisibleItemsPacked(false);
    }

    private void SetVisibleItemsPacked(bool isPacked)
    {
        List<PackingItem> visibleItems = _items
            .Where(ItemMatchesFilters)
            .ToList();

        _isBulkUpdatingItems = true;

        try
        {
            foreach (PackingItem item in visibleItems)
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
        foreach (CategoryGroup group in _categoryGroups)
        {
            _categoryExpandedStates[group.Category] = isExpanded;
        }

        RefreshCategoryGroups();
    }

    private void UpdateListVisibility()
    {
        bool hasVisibleItems = _filteredItemCount > 0;
        EmptyStatePanel.IsVisible = !hasVisibleItems;
        CategoryGroupsList.IsVisible = hasVisibleItems;

        if (_items.Count == 0)
        {
            EmptyStateTitle.Text = "Start this packing list";
            EmptyStateHint.Text = "Use the item bar above to add the first thing you need to bring.";
            return;
        }

        if (HasActiveFilters())
        {
            EmptyStateTitle.Text = "No matching items";
            EmptyStateHint.Text = "Try a different search or clear the current filter.";
            return;
        }

        EmptyStateTitle.Text = "No visible items";
        EmptyStateHint.Text = "Add an item above to bring your packing list back.";
    }

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        ShowStatusToast(keepVisible: false);
    }

    private void ShowUndoDelete(string message)
    {
        StatusText.Text = message;
        UndoDeleteButton.IsVisible = true;
        ShowStatusToast(keepVisible: true);
    }

    private void HideUndoDelete()
    {
        _lastDeletedItem = null;
        _lastDeletedIndex = -1;
        _lastDeletedProfileName = "";
        UndoDeleteButton.IsVisible = false;
    }

    private async void ShowStatusToast(bool keepVisible)
    {
        _statusToastHideTokenSource?.Cancel();
        StatusToast.IsVisible = true;
        StatusToast.Opacity = 1;

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

        StatusToast.Opacity = 0;

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
            StatusToast.IsVisible = false;
        }
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        RefreshDashboard();
    }

    private void StatusFilterBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        RefreshDashboard();
    }

    private void ClearFilters_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        StatusFilterBox.SelectedIndex = 0;
        RefreshDashboard();
        ShowStatus("Filters cleared.");
    }

    private async void UpdateCompletionBanner(int packedCount, int totalCount)
    {
        bool shouldShowBanner = totalCount > 0 && packedCount == totalCount;

        if (shouldShowBanner == _completionBannerVisible)
        {
            return;
        }

        _completionBannerVisible = shouldShowBanner;

        if (shouldShowBanner)
        {
            CompletionBanner.IsVisible = true;
            CompletionBanner.Opacity = 0;

            await Task.Delay(30);

            if (_completionBannerVisible)
            {
                CompletionBanner.Opacity = 1;
            }

            return;
        }

        CompletionBanner.Opacity = 0;

        await Task.Delay(260);

        if (!_completionBannerVisible)
        {
            CompletionBanner.IsVisible = false;
        }
    }

    private async Task<bool> EditItemAsync(PackingItem item)
    {
        TextBox nameBox = CreateDialogTextBox(item.Name);
        TextBox quantityBox = CreateDialogTextBox(item.Quantity.ToString());
        ComboBox categoryBox = CreateDialogCategoryBox(item.Category);

        TextBlock validationText = new TextBlock
        {
            Text = "",
            Foreground = Brush("#FDA4AF"),
            FontSize = 13,
            IsVisible = false
        };

        Button cancelButton = CreateDialogButton("Cancel", false);
        Button saveButton = CreateDialogButton("Save Changes", true);

        Window dialog = new Window
        {
            Width = 470,
            Height = 390,
            CanResize = false,
            ShowInTaskbar = false,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent
        };

        StackPanel fields = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                CreateDialogField("Item name", nameBox),
                CreateDialogField("Quantity", quantityBox),
                CreateDialogField("Category", categoryBox),
                validationText
            }
        };

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Children =
            {
                cancelButton,
                saveButton
            }
        };

        Border footer = new Border
        {
            Padding = new Thickness(0, 16, 0, 0),
            BorderBrush = Brush("#1E293B"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = buttons
        };
        Grid.SetRow(footer, 2);

        TextBlock titleText = new TextBlock
        {
            Text = "Edit item",
            Foreground = Brush("#F8FAFC"),
            FontSize = 20,
            FontWeight = FontWeight.Bold
        };

        TextBlock helperText = new TextBlock
        {
            Text = "Update the item details below.",
            Foreground = Brush("#94A3B8"),
            FontSize = 13
        };

        Border content = new Border
        {
            Background = Brush("#0F172A"),
            BorderBrush = Brush("#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 18,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            titleText,
                            helperText
                        }
                    },
                    fields,
                    footer
                }
            }
        };
        Grid.SetRow(fields, 1);

        dialog.Content = new Border
        {
            Padding = new Thickness(8),
            Child = content
        };

        cancelButton.Click += (_, _) =>
        {
            dialog.Close(false);
        };

        saveButton.Click += (_, _) =>
        {
            string updatedName = nameBox.Text?.Trim() ?? "";
            string updatedCategory = GetDialogSelectedCategory(categoryBox);

            if (string.IsNullOrWhiteSpace(updatedName))
            {
                ShowDialogValidation(validationText, "Item name cannot be empty.");
                return;
            }

            if (!int.TryParse(quantityBox.Text, out int updatedQuantity) || updatedQuantity <= 0)
            {
                ShowDialogValidation(validationText, "Quantity must be a positive number.");
                return;
            }

            item.Name = updatedName;
            item.Quantity = updatedQuantity;
            item.Category = updatedCategory;
            dialog.Close(true);
        };

        return await dialog.ShowDialog<bool>(this);
    }

    private TextBox CreateDialogTextBox(string text)
    {
        return new TextBox
        {
            Text = text,
            Background = Brush("#111827"),
            BorderBrush = Brush("#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Foreground = Brush("#F8FAFC"),
            Padding = new Thickness(12, 9)
        };
    }

    private ComboBox CreateDialogCategoryBox(string selectedCategory)
    {
        string category = AddCategoryIfMissing(selectedCategory);

        return new ComboBox
        {
            ItemsSource = _categories,
            SelectedItem = category,
            Background = Brush("#111827"),
            BorderBrush = Brush("#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Foreground = Brush("#F8FAFC"),
            Padding = new Thickness(12, 9)
        };
    }

    private static string GetDialogSelectedCategory(ComboBox categoryBox)
    {
        return categoryBox.SelectedItem is string selectedCategory &&
            !string.IsNullOrWhiteSpace(selectedCategory)
            ? selectedCategory
            : MiscellaneousCategoryName;
    }

    private Control CreateDialogField(string label, Control input)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = Brush("#CBD5E1"),
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                },
                input
            }
        };
    }

    private Button CreateDialogButton(string text, bool isPrimary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 112,
            Padding = new Thickness(14, 9),
            CornerRadius = new CornerRadius(8),
            Background = Brush(isPrimary ? "#06B6D4" : "#111827"),
            BorderBrush = Brush(isPrimary ? "#67E8F9" : "#334155"),
            Foreground = Brush(isPrimary ? "#082F49" : "#F8FAFC")
        };
    }

    private static void ShowDialogValidation(TextBlock validationText, string message)
    {
        validationText.Text = message;
        validationText.IsVisible = true;
    }

    private static SolidColorBrush Brush(string color)
    {
        return new SolidColorBrush(Color.Parse(color));
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        Window dialog = new Window
        {
            Width = 460,
            Height = 230,
            CanResize = false,
            ShowInTaskbar = false,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent
        };

        dialog.Content = CreateConfirmationContent(dialog, title, message);

        return await dialog.ShowDialog<bool>(this);
    }

    private Control CreateConfirmationContent(Window dialog, string title, string message)
    {
        Border iconContainer = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = Brush("#3B0A1F"),
            BorderBrush = Brush("#FB7185"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "!",
                Foreground = Brush("#FDA4AF"),
                FontSize = 24,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        TextBlock titleText = new TextBlock
        {
            Text = title,
            Foreground = Brush("#F8FAFC"),
            FontSize = 20,
            FontWeight = FontWeight.Bold
        };

        TextBlock messageText = new TextBlock
        {
            Text = message,
            Foreground = Brush("#CBD5E1"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 20
        };

        Button cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 104,
            Padding = new Thickness(14, 9),
            CornerRadius = new CornerRadius(8),
            Background = Brush("#111827"),
            BorderBrush = Brush("#334155"),
            Foreground = Brush("#F8FAFC")
        };

        Button deleteButton = new Button
        {
            Content = "Delete",
            MinWidth = 104,
            Padding = new Thickness(14, 9),
            CornerRadius = new CornerRadius(8),
            Background = Brush("#E11D48"),
            BorderBrush = Brush("#FDA4AF"),
            Foreground = Brushes.White
        };

        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Children =
            {
                cancelButton,
                deleteButton
            }
        };

        StackPanel textContent = new StackPanel
        {
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                titleText,
                messageText
            }
        };
        Grid.SetColumn(textContent, 1);

        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14,
            Children =
            {
                iconContainer,
                textContent
            }
        };

        Border footer = new Border
        {
            Padding = new Thickness(0, 16, 0, 0),
            BorderBrush = Brush("#1E293B"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = buttons
        };
        Grid.SetRow(footer, 1);

        Border content = new Border
        {
            Background = Brush("#0F172A"),
            BorderBrush = Brush("#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = 22,
                Children =
                {
                    heading,
                    footer
                }
            }
        };

        Border shell = new Border
        {
            Padding = new Thickness(8),
            Child = content
        };

        cancelButton.Click += (_, _) =>
        {
            dialog.Close(false);
        };

        deleteButton.Click += (_, _) =>
        {
            dialog.Close(true);
        };

        return shell;
    }
}

public class CategoryGroup : INotifyPropertyChanged
{
    private bool _isExpanded;

    public CategoryGroup(string category, List<PackingItem> items, bool isExpanded)
    {
        Category = category;
        Items = items;
        AccentBrush = CreateAccentBrush(category);
        _isExpanded = isExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Category { get; }
    public List<PackingItem> Items { get; }
    public IBrush AccentBrush { get; }
    public int PackedCount => Items.Count(item => item.IsPacked);
    public int ProgressPercent => Items.Count == 0 ? 0 : PackedCount * 100 / Items.Count;
    public string CountLabel => Items.Count == 1 ? "1 item" : $"{Items.Count} items";
    public string ProgressLabel => $"{PackedCount}/{Items.Count}";
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

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
