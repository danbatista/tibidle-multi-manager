using System.Windows;
using System.Windows.Controls;

namespace TibidleMultiManager;

public static class BrowserLayout
{
    public static void Apply(Grid host, IReadOnlyList<Grid> panes, bool gridView, int selected)
    {
        if (host.RowDefinitions.Count == 0)
        {
            for (var i = 0; i < 2; i++)
            {
                host.RowDefinitions.Add(new RowDefinition());
                host.ColumnDefinitions.Add(new ColumnDefinition());
            }
        }
        for (var i = 0; i < panes.Count; i++)
        {
            var pane = panes[i];
            Grid.SetRow(pane, gridView ? i / 2 : 0);
            Grid.SetColumn(pane, gridView ? i % 2 : 0);
            Grid.SetRowSpan(pane, gridView ? 1 : 2);
            Grid.SetColumnSpan(pane, gridView ? 1 : 2);
            pane.Margin = new Thickness(gridView ? 3 : 0);
            pane.Visibility = gridView || i == selected ? Visibility.Visible : Visibility.Hidden;
        }
    }
}
