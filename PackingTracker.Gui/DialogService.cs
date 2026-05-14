using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PackingTracker.Gui;

public sealed record PackingItemEditResult(string Name, int Quantity, string Category);

// ViewModels depend on this interface instead of directly creating Avalonia windows.
public interface IDialogService
{
    Task<PackingItemEditResult?> EditItemAsync(PackingItem item, IEnumerable<string> categories);
    Task<bool> ConfirmAsync(string title, string message);
}

public sealed class DialogService : IDialogService
{
    private readonly Window _owner;

    public DialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task<PackingItemEditResult?> EditItemAsync(PackingItem item, IEnumerable<string> categories)
    {
        TextBox nameBox = CreateTextBox(item.Name);
        TextBox quantityBox = CreateTextBox(item.Quantity.ToString());
        ComboBox categoryBox = CreateCategoryBox(categories, item.Category);

        TextBlock validationText = new TextBlock
        {
            Text = "",
            Foreground = Brush("#FDA4AF"),
            FontSize = 13,
            IsVisible = false
        };

        Button cancelButton = CreateButton("Cancel", false);
        Button saveButton = CreateButton("Save Changes", true);

        Window dialog = CreateDialog(470, 390);
        dialog.Content = CreateEditItemContent(nameBox, quantityBox, categoryBox, validationText, cancelButton, saveButton);

        cancelButton.Click += (_, _) =>
        {
            dialog.Close(null);
        };

        saveButton.Click += (_, _) =>
        {
            string updatedName = nameBox.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(updatedName))
            {
                ShowValidation(validationText, "Item name cannot be empty.");
                return;
            }

            if (!int.TryParse(quantityBox.Text, out int updatedQuantity) || updatedQuantity <= 0)
            {
                ShowValidation(validationText, "Quantity must be a positive number.");
                return;
            }

            string updatedCategory = GetSelectedCategory(categoryBox, item.Category);
            dialog.Close(new PackingItemEditResult(updatedName, updatedQuantity, updatedCategory));
        };

        return await dialog.ShowDialog<PackingItemEditResult?>(_owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        Window dialog = CreateDialog(460, 230);
        dialog.Content = CreateConfirmationContent(dialog, title, message);

        return await dialog.ShowDialog<bool>(_owner);
    }

    private static Window CreateDialog(double width, double height)
    {
        return new Window
        {
            Width = width,
            Height = height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent
        };
    }

    private static Control CreateEditItemContent(
        TextBox nameBox,
        TextBox quantityBox,
        ComboBox categoryBox,
        TextBlock validationText,
        Button cancelButton,
        Button saveButton)
    {
        StackPanel fields = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                CreateField("Item name", nameBox),
                CreateField("Quantity", quantityBox),
                CreateField("Category", categoryBox),
                validationText
            }
        };

        Border footer = CreateFooter(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Children =
            {
                cancelButton,
                saveButton
            }
        });

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

        Grid.SetRow(fields, 1);
        Grid.SetRow(footer, 2);

        return CreateDialogShell(new Grid
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
        });
    }

    private static Control CreateConfirmationContent(Window dialog, string title, string message)
    {
        Button cancelButton = CreateButton("Cancel", false);
        Button deleteButton = CreateButton("Delete", true);
        deleteButton.Background = Brush("#E11D48");
        deleteButton.BorderBrush = Brush("#FDA4AF");
        deleteButton.Foreground = Brushes.White;

        cancelButton.Click += (_, _) =>
        {
            dialog.Close(false);
        };

        deleteButton.Click += (_, _) =>
        {
            dialog.Close(true);
        };

        Grid heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14,
            Children =
            {
                CreateWarningIcon(),
                CreateConfirmationText(title, message)
            }
        };

        Border footer = CreateFooter(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 10,
            Children =
            {
                cancelButton,
                deleteButton
            }
        });

        Grid.SetRow(footer, 1);

        return CreateDialogShell(new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 22,
            Children =
            {
                heading,
                footer
            }
        });
    }

    private static Border CreateDialogShell(Control content)
    {
        return new Border
        {
            Padding = new Thickness(8),
            Child = new Border
            {
                Background = Brush("#0F172A"),
                BorderBrush = Brush("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(22),
                Child = content
            }
        };
    }

    private static Border CreateFooter(Control content)
    {
        return new Border
        {
            Padding = new Thickness(0, 16, 0, 0),
            BorderBrush = Brush("#1E293B"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = content
        };
    }

    private static TextBox CreateTextBox(string text)
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

    private static ComboBox CreateCategoryBox(IEnumerable<string> categories, string selectedCategory)
    {
        List<string> categoryList = categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .ToList();
        string matchingCategory = categoryList.FirstOrDefault(category =>
            string.Equals(category, selectedCategory, StringComparison.OrdinalIgnoreCase)) ?? selectedCategory;

        if (!string.IsNullOrWhiteSpace(matchingCategory) &&
            !categoryList.Any(category => string.Equals(category, matchingCategory, StringComparison.OrdinalIgnoreCase)))
        {
            categoryList.Add(matchingCategory);
        }

        return new ComboBox
        {
            ItemsSource = categoryList,
            SelectedItem = matchingCategory,
            Background = Brush("#111827"),
            BorderBrush = Brush("#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Foreground = Brush("#F8FAFC"),
            Padding = new Thickness(12, 9)
        };
    }

    private static Control CreateField(string label, Control input)
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

    private static Button CreateButton(string text, bool isPrimary)
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

    private static Border CreateWarningIcon()
    {
        return new Border
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
    }

    private static StackPanel CreateConfirmationText(string title, string message)
    {
        StackPanel textContent = new StackPanel
        {
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Foreground = Brush("#F8FAFC"),
                    FontSize = 20,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = message,
                    Foreground = Brush("#CBD5E1"),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    LineHeight = 20
                }
            }
        };

        Grid.SetColumn(textContent, 1);
        return textContent;
    }

    private static string GetSelectedCategory(ComboBox categoryBox, string fallbackCategory)
    {
        return categoryBox.SelectedItem is string selectedCategory &&
            !string.IsNullOrWhiteSpace(selectedCategory)
            ? selectedCategory
            : fallbackCategory;
    }

    private static void ShowValidation(TextBlock validationText, string message)
    {
        validationText.Text = message;
        validationText.IsVisible = true;
    }

    private static SolidColorBrush Brush(string color)
    {
        return new SolidColorBrush(Color.Parse(color));
    }
}
