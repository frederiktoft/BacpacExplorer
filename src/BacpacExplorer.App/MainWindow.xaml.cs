using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BacpacExplorer.Core.Bacpac;
using BacpacExplorer.Core.Database;
using BacpacExplorer.Core.Models;
using BacpacExplorer.Core.Querying;
using BacpacExplorer.App.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BacpacExplorer.App;

/// <summary>
/// Coordinates the WPF Explorer and Query workspaces while delegating package, database, and SQL work to Core services.
/// It also owns the lifetime of the current package's temporary LocalDB database.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IBacpacReader _reader = new BacpacReader();
    private readonly CancellationTokenSource _operationCancellation = new();
    private readonly ILogger<MainWindow> _logger;
    private BacpacDatabase? _database;
    private DatabaseTable? _selectedTable;
    private ITemporaryDatabase? _temporaryDatabase;
    private IDataBrowser? _dataBrowser;
    private ISqlQueryService? _queryService;
    private CancellationTokenSource? _queryCancellation;
    private DataPage? _currentPage;
    private bool _busy;

    public MainWindow()
    {
        _logger = App.LoggerFactory.CreateLogger<MainWindow>();
        InitializeComponent();
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = AppText.Get("OpenDialogTitle"),
            Filter = AppText.Get("OpenDialogFilter"),
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true);
            StatusText.Text = AppText.Get("ReadingPackage");
            var database = await _reader.ReadAsync(dialog.FileName, _operationCancellation.Token);
            await DisposeTemporaryDatabaseAsync();

            _database = database;
            _selectedTable = null;
            PrepareDataButton.Content = AppText.Get("LoadDatabaseData");
            FileText.Text = database.FilePath;
            RebuildTree();
            ResetDataView();
            ResetQueryView();

            StatusText.Text = AppText.Format("LoadedPackage", database.FileName, database.Schemas.Count, database.TableCount);
            DatabaseSummaryText.Text = AppText.Format("DatabaseSummary", database.DatabaseName, FormatBytes(database.FileSize),
                database.Schemas.Count, database.TableCount, database.PackageEntries.Count);
            SelectionTitle.Text = AppText.Get("SelectTable");
            SelectionSubtitle.Text = AppText.Get("TableSpecificInformation");
            ColumnsGrid.ItemsSource = null;
            DetailsText.Text = AppText.Get("SelectTableColumns");
            _logger.LogInformation(
                "Opened BACPAC {FileName} with {SchemaCount} schemas and {TableCount} tables",
                database.FileName,
                database.Schemas.Count,
                database.TableCount);
        }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
        {
        }
        catch (BacpacReadException exception)
        {
            ShowError(exception, AppText.Get("FailedReadPackage"), exception.Message);
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception, AppText.Get("OpeningBacpacOperation"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildTree();

    private void RebuildTree()
    {
        DatabaseTree.Items.Clear();
        if (_database is null)
            return;

        var filter = FilterBox.Text.Trim();
        foreach (var schema in _database.Schemas)
        {
            var matchingTables = schema.Tables
                .Where(table => string.IsNullOrWhiteSpace(filter)
                    || table.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || table.Schema.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || table.Columns.Any(column => column.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            if (matchingTables.Length == 0)
                continue;

            var schemaNode = new TreeViewItem { Header = AppText.Format("SchemaTableCount", schema.Name, matchingTables.Length), IsExpanded = true };
            foreach (var table in matchingTables)
                schemaNode.Items.Add(new TreeViewItem { Header = table.Name, Tag = table });

            DatabaseTree.Items.Add(schemaNode);
        }
    }

    private async void DatabaseTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: DatabaseTable table })
            return;

        _selectedTable = table;
        QuerySelectedTableButton.IsEnabled = !_busy;
        SelectionTitle.Text = table.FullName;
        SelectionSubtitle.Text = AppText.Format("ColumnCount", table.Columns.Count);
        ColumnsGrid.ItemsSource = table.Columns;
        DetailsText.Text = _temporaryDatabase?.IsReady == true
            ? AppText.Get("DataReadyForTable")
            : AppText.Get("LoadDataInstruction");
        ResetDataView();

        if (!_busy && _temporaryDatabase?.IsReady == true && DetailsTabs.SelectedItem == DataTab)
            await NavigateAsync(offset: 0);
    }

    private async void PrepareDataButton_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null)
            return;

        try
        {
            SetBusy(true);
            await EnsureDatabaseDataReadyAsync(_operationCancellation.Token);
            if (_selectedTable is not null)
            {
                DetailsTabs.SelectedItem = DataTab;
                await LoadPageAsync(offset: 0);
            }
            else
            {
                StatusText.Text = AppText.Get("DataReadySelectTable");
            }
        }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
        {
        }
        catch (TemporaryDatabaseException exception)
        {
            ShowError(exception, AppText.Get("CouldNotPrepareTableData"), exception.Message);
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception, AppText.Get("LoadingTableDataOperation"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task EnsureDatabaseDataReadyAsync(CancellationToken cancellationToken)
    {
        if (_database is null)
            throw new InvalidOperationException(AppText.Get("OpenBeforeData"));

        _temporaryDatabase ??= new LocalDbTemporaryDatabase(
            App.LoggerFactory.CreateLogger<LocalDbTemporaryDatabase>());
        if (!_temporaryDatabase.IsReady)
        {
            _logger.LogInformation("Preparing LocalDB data for BACPAC {FileName}", _database.FileName);
            var progress = new Progress<string>(message => StatusText.Text = message);
            await _temporaryDatabase.PrepareAsync(_database.FilePath, progress, cancellationToken);
            _logger.LogInformation("LocalDB data is ready for BACPAC {FileName}", _database.FileName);
        }

        _dataBrowser ??= new SqlDataBrowser(_temporaryDatabase.ConnectionString);
        _queryService ??= new ReadOnlySqlQueryService(
            _temporaryDatabase.ConnectionString,
            _temporaryDatabase.ReadOnlyUserName);
        PrepareDataButton.Content = AppText.Get("DatabaseDataReady");
        DetailsText.Text = AppText.Get("ImportedDataInstruction");
    }

    private async void ExecuteQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null || _queryCancellation is not null)
            return;

        _queryCancellation = CancellationTokenSource.CreateLinkedTokenSource(_operationCancellation.Token);
        try
        {
            SetBusy(true);
            QueryMessagesText.Text = AppText.Get("PreparingDatabaseData");
            await EnsureDatabaseDataReadyAsync(_queryCancellation.Token);

            QueryMessagesText.Text = AppText.Get("ExecutingQuery");
            StatusText.Text = AppText.Get("ExecutingReadOnlyQuery");
            var result = await _queryService!.ExecuteAsync(QueryEditor.Text, _queryCancellation.Token);
            QueryResultsGrid.ItemsSource = CreateDataTable(result.Columns, result.Rows).DefaultView;

            var summary = AppText.Format("QuerySummary", result.Rows.Count, result.Elapsed.TotalMilliseconds);
            if (result.WasTruncated)
                summary += AppText.Get("QueryTruncated");
            QueryMessagesText.Text = result.Messages.Count == 0
                ? summary
                : $"{summary}{Environment.NewLine}{string.Join(Environment.NewLine, result.Messages)}";
            StatusText.Text = summary;
            _logger.LogInformation(
                "Read-only query completed in {ElapsedMilliseconds:0.0} ms with {RowCount} rows. Truncated: {WasTruncated}",
                result.Elapsed.TotalMilliseconds,
                result.Rows.Count,
                result.WasTruncated);
        }
        catch (OperationCanceledException)
        {
            QueryMessagesText.Text = AppText.Get("QueryCancelledSentence");
            StatusText.Text = AppText.Get("QueryCancelled");
        }
        catch (QueryExecutionException exception)
        {
            _logger.LogError(exception, "Read-only query execution failed");
            QueryMessagesText.Text = exception.Message;
            StatusText.Text = AppText.Get("QueryFailed");
        }
        catch (TemporaryDatabaseException exception)
        {
            ShowError(exception, AppText.Get("CouldNotPrepareDatabaseData"), exception.Message);
            QueryMessagesText.Text = exception.Message;
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception, AppText.Get("ExecutingQueryOperation"));
            QueryMessagesText.Text = exception.Message;
        }
        finally
        {
            _queryCancellation?.Dispose();
            _queryCancellation = null;
            SetBusy(false);
        }
    }

    private void CancelQueryButton_Click(object sender, RoutedEventArgs e) => _queryCancellation?.Cancel();

    private void QuerySelectedTableButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTable is null)
            return;

        QueryEditor.Text = AppText.Format("GeneratedTableQuery", _selectedTable.FullName);
        WorkspaceTabs.SelectedIndex = 1;
        QueryEditor.Focus();
    }

    private async void DetailsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || DetailsTabs.SelectedItem != DataTab || _selectedTable is null
            || _temporaryDatabase?.IsReady != true || _dataBrowser is null || _currentPage is not null)
            return;

        await NavigateAsync(offset: 0);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage is null)
            return;

        await NavigateAsync(Math.Max(0, _currentPage.Offset - _currentPage.PageSize));
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage is null)
            return;

        await NavigateAsync(_currentPage.Offset + _currentPage.PageSize);
    }

    private void RowsGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        ShowSelectedCell(
            RowsGrid,
            SelectedCellLabel,
            SelectedCellText,
            AppText.Get("SelectedCellValue"),
            AppText.Get("SelectCellPrompt"));
    }

    private void QueryResultsGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        ShowSelectedCell(
            QueryResultsGrid,
            QueryCellLabel,
            QueryCellText,
            AppText.Get("SelectedResultValue"),
            AppText.Get("SelectResultCellPrompt"));
    }

    private static void ShowSelectedCell(
        DataGrid grid,
        TextBlock label,
        TextBox valueText,
        string defaultLabel,
        string prompt)
    {
        if (grid.SelectedCells.FirstOrDefault() is not { IsValid: true } cell
            || cell.Item is not DataRowView row
            || string.IsNullOrWhiteSpace(cell.Column.SortMemberPath))
        {
            label.Text = defaultLabel;
            valueText.Text = prompt;
            return;
        }

        var columnName = cell.Column.SortMemberPath;
        var value = row[columnName];
        label.Text = AppText.Format("SelectedCellLabel", columnName);
        valueText.Text = value is DBNull
            ? AppText.Get("NullValue")
            : Convert.ToString(value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        valueText.ScrollToHome();
    }

    private async Task NavigateAsync(int offset)
    {
        try
        {
            SetBusy(true);
            await LoadPageAsync(offset);
        }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception, AppText.Get("LoadingDataPageOperation"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadPageAsync(int offset)
    {
        if (_dataBrowser is null || _selectedTable is null)
            return;

        StatusText.Text = AppText.Format("ReadingTable", _selectedTable.FullName);
        var pageSize = offset == 0 ? GetSelectedPageSize() : _currentPage?.PageSize ?? GetSelectedPageSize();
        _currentPage = await _dataBrowser.ReadPageAsync(
            _selectedTable, offset, pageSize, _operationCancellation.Token);

        RowsGrid.ItemsSource = CreateDataTable(_currentPage).DefaultView;
        var firstRow = _currentPage.Rows.Count == 0 ? 0 : _currentPage.Offset + 1;
        var lastRow = _currentPage.Offset + _currentPage.Rows.Count;
        PageText.Text = _currentPage.Rows.Count == 0
            ? AppText.Get("NoRows")
            : AppText.Format("RowsRange", firstRow, lastRow);
        StatusText.Text = AppText.Format("LoadedRows", _currentPage.Rows.Count, _selectedTable.FullName);
    }

    private static DataTable CreateDataTable(DataPage page) => CreateDataTable(page.Columns, page.Rows);

    private static DataTable CreateDataTable(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var table = new DataTable { Locale = System.Globalization.CultureInfo.InvariantCulture };
        foreach (var column in columns)
            table.Columns.Add(column, typeof(object));

        foreach (var row in rows)
            table.Rows.Add(row.Select(value => value ?? DBNull.Value).ToArray());

        return table;
    }

    private int GetSelectedPageSize() =>
        PageSizeBox.SelectedItem is ComboBoxItem item
        && int.TryParse(item.Content?.ToString(), out var pageSize)
            ? pageSize
            : 100;

    private void ResetDataView()
    {
        _currentPage = null;
        RowsGrid.SelectedCells.Clear();
        RowsGrid.ItemsSource = null;
        SelectedCellLabel.Text = AppText.Get("SelectedCellValue");
        SelectedCellText.Text = AppText.Get("SelectCellPrompt");
        PageText.Text = _temporaryDatabase?.IsReady == true
            ? AppText.Get("SelectTableRows")
            : AppText.Get("LoadDataFromToolbar");
        PreviousButton.IsEnabled = false;
        NextButton.IsEnabled = false;
    }

    private void ResetQueryView()
    {
        QueryResultsGrid.SelectedCells.Clear();
        QueryResultsGrid.ItemsSource = null;
        QueryCellLabel.Text = AppText.Get("SelectedResultValue");
        QueryCellText.Text = AppText.Get("SelectResultCellPrompt");
        QueryMessagesText.Text = AppText.Get("QueryInitialMessage");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        OpenButton.IsEnabled = !busy;
        PrepareDataButton.IsEnabled = !busy && _database is not null && _temporaryDatabase?.IsReady != true;
        DatabaseTree.IsEnabled = !busy;
        FilterBox.IsEnabled = !busy;
        PageSizeBox.IsEnabled = !busy;
        PreviousButton.IsEnabled = !busy && _currentPage?.HasPreviousPage == true;
        NextButton.IsEnabled = !busy && _currentPage?.HasNextPage == true;
        QueryEditor.IsEnabled = !busy;
        ExecuteQueryButton.IsEnabled = !busy && _database is not null;
        CancelQueryButton.IsEnabled = busy && _queryCancellation is not null;
        QuerySelectedTableButton.IsEnabled = !busy && _selectedTable is not null;
    }

    private async Task DisposeTemporaryDatabaseAsync()
    {
        var database = _temporaryDatabase;
        _temporaryDatabase = null;
        _dataBrowser = null;
        _queryService = null;
        if (database is not null)
            await database.DisposeAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _operationCancellation.Cancel();
        _queryCancellation?.Cancel();
        try
        {
            DisposeTemporaryDatabaseAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Temporary database cleanup failed during application shutdown");
        }
        finally
        {
            _operationCancellation.Dispose();
        }
    }

    private void ShowError(Exception exception, string status, string message)
    {
        _logger.LogError(exception, "Operation failed with status {Status}", status);
        StatusText.Text = status;
        MessageBox.Show(this, message, AppText.Get("DialogCaption"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowUnexpectedError(Exception exception, string operation)
    {
        ShowError(exception, AppText.Get("UnexpectedErrorStatus"), AppText.Format("UnexpectedErrorMessage", operation));
    }

    private static string FormatBytes(long bytes)
    {
        var units = AppText.Get("ByteUnits").Split('|');
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
