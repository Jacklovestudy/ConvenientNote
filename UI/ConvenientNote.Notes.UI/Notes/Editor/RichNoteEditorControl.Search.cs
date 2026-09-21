using System.Windows;
using System.Windows.Controls;

namespace ConvenientNote.Views;

public partial class RichNoteEditorControl
{
    private void OpenSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        DocumentSearchBox.Focus();
        FindInDocument(DocumentSearchBox.Text, 0);
        DocumentSearchBox.SelectAll();
    }

    private void CloseSearch()
    {
        SearchBar.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private void OpenSearchButton_Click(object sender, RoutedEventArgs e) => OpenSearch();
    private void CloseSearchButton_Click(object sender, RoutedEventArgs e) => CloseSearch();
    private void FindPreviousButton_Click(object sender, RoutedEventArgs e) => FindInDocument(DocumentSearchBox.Text, -1);

    private void DocumentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchStatusText is null || Editor is null) return;
        _lastFindIndex = -1;
        FindInDocument(DocumentSearchBox.Text);
    }

    private void SetSearchStatus(int current, int total, bool notFound = false)
    {
        SearchStatusText.Text = notFound ? "0 / 0 · 未找到" : $"{current} / {total}";
        FindPreviousButton.IsEnabled = total > 0;
        FindNextButton.IsEnabled = total > 0;
    }
}
