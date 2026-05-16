using System.ComponentModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BPEditor;

public sealed class NonBlueprintDataItem : INotifyPropertyChanged
{
    private string name = string.Empty;
    private string value = string.Empty;
    private string @namespace = string.Empty;
    private string cultureInvariantString = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        set => SetField(ref name, value ?? string.Empty, nameof(Name));
    }

    public string Value
    {
        get => value;
        set => SetField(ref this.value, value ?? string.Empty, nameof(Value));
    }

    public string Namespace
    {
        get => @namespace;
        set => SetField(ref @namespace, value ?? string.Empty, nameof(Namespace));
    }

    public string CultureInvariantString
    {
        get => cultureInvariantString;
        set => SetField(ref cultureInvariantString, value ?? string.Empty, nameof(CultureInvariantString));
    }

    public string TemplateJson { get; set; } = string.Empty;
    public string PropertyType { get; set; } = string.Empty;
    public List<string> EnumOptions { get; set; } = new();
    public int OriginalOrdinal { get; set; } = -1;

    private void SetField(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class NonBlueprintExportItem
{
    public int ExportIndex { get; init; }
    public string ObjectName { get; init; } = string.Empty;
    public string ExportType { get; init; } = string.Empty;
    public BindingList<NonBlueprintDataItem> DataItems { get; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(ObjectName)
        ? $"Export {ExportIndex}"
        : $"{ExportIndex}: {ObjectName}";
}

public sealed class NonBlueprintExportEditorState
{
    public string OriginalAssetJson { get; private set; } = string.Empty;
    public List<NonBlueprintExportItem> Exports { get; } = new();
    public bool HasExports => Exports.Count > 0;

    public event EventHandler? Edited;

    public static NonBlueprintExportEditorState FromAssetJson(string assetJson)
    {
        NonBlueprintExportEditorState state = new()
        {
            OriginalAssetJson = assetJson ?? string.Empty
        };

        JsonObject? root = ParseJsonObject(assetJson);
        if (root?["Exports"] is not JsonArray exports)
            return state;

        for (int i = 0; i < exports.Count; i++)
        {
            if (exports[i] is not JsonObject exportObj ||
                !IsEditableNonBlueprintExport(exportObj))
            {
                continue;
            }

            NonBlueprintExportItem exportItem = new()
            {
                ExportIndex = i,
                ObjectName = ReadString(exportObj["ObjectName"]),
                ExportType = SimplifyTypeName(ReadString(exportObj["$type"]))
            };

            if (exportObj["Data"] is JsonArray dataArray)
            {
                int ordinal = 0;
                foreach (JsonObject dataObj in dataArray.OfType<JsonObject>())
                {
                    NonBlueprintDataItem? dataItem = CreateDataItem(dataObj, ordinal++);
                    if (dataItem != null)
                        exportItem.DataItems.Add(dataItem);
                }
            }

            state.Exports.Add(exportItem);
            state.AttachExportItem(exportItem);
        }

        return state;
    }

    public string ApplyToAssetJson(string assetJson, IReadOnlyDictionary<string, DataPropertyTemplate> dataTemplatesByName)
    {
        JsonObject? root = ParseJsonObject(string.IsNullOrWhiteSpace(assetJson) ? OriginalAssetJson : assetJson);
        if (root?["Exports"] is not JsonArray exports)
            return assetJson;

        foreach (NonBlueprintExportItem exportItem in Exports)
        {
            if (exportItem.ExportIndex < 0 ||
                exportItem.ExportIndex >= exports.Count ||
                exports[exportItem.ExportIndex] is not JsonObject exportObj)
            {
                continue;
            }

            JsonArray dataArray = [];
            foreach (NonBlueprintDataItem dataItem in exportItem.DataItems)
            {
                JsonObject? dataObj = BuildDataObject(dataItem, dataTemplatesByName);
                if (dataObj != null)
                    dataArray.Add(dataObj);
            }
            exportObj["Data"] = dataArray;
        }

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public bool TryAddDataItem(int exportIndex, DataPropertyTemplate template)
    {
        NonBlueprintExportItem? exportItem = FindExport(exportIndex);
        if (exportItem == null || string.IsNullOrWhiteSpace(template.TemplateJson))
            return false;

        JsonObject? templateObj = ParseJsonObject(template.TemplateJson);
        if (templateObj == null)
            return false;

        NonBlueprintDataItem? dataItem = CreateDataItem(templateObj, exportItem.DataItems.Count);
        if (dataItem == null)
            return false;

        dataItem.EnumOptions = new List<string>(template.EnumOptions);
        exportItem.DataItems.Add(dataItem);
        NotifyEdited();
        return true;
    }

    public bool TryDeleteDataItem(int exportIndex, int dataIndex)
    {
        NonBlueprintExportItem? exportItem = FindExport(exportIndex);
        if (exportItem == null || dataIndex < 0 || dataIndex >= exportItem.DataItems.Count)
            return false;

        exportItem.DataItems.RemoveAt(dataIndex);
        NotifyEdited();
        return true;
    }

    public bool TryMoveDataItemUp(int exportIndex, int dataIndex)
    {
        return TryMoveDataItem(exportIndex, dataIndex, dataIndex - 1);
    }

    public bool TryMoveDataItemDown(int exportIndex, int dataIndex)
    {
        return TryMoveDataItem(exportIndex, dataIndex, dataIndex + 1);
    }

    internal void NotifyEdited()
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void AttachExportItem(NonBlueprintExportItem exportItem)
    {
        foreach (NonBlueprintDataItem dataItem in exportItem.DataItems)
            AttachDataItem(dataItem);

        exportItem.DataItems.ListChanged += DataItems_ListChanged;
    }

    private void DataItems_ListChanged(object? sender, ListChangedEventArgs e)
    {
        if (sender is BindingList<NonBlueprintDataItem> dataItems &&
            e.ListChangedType == ListChangedType.ItemAdded &&
            e.NewIndex >= 0 &&
            e.NewIndex < dataItems.Count)
        {
            AttachDataItem(dataItems[e.NewIndex]);
        }

        if (e.ListChangedType is ListChangedType.ItemAdded or
            ListChangedType.ItemDeleted or
            ListChangedType.ItemMoved or
            ListChangedType.ItemChanged or
            ListChangedType.Reset)
        {
            NotifyEdited();
        }
    }

    private void AttachDataItem(NonBlueprintDataItem dataItem)
    {
        dataItem.PropertyChanged -= DataItem_PropertyChanged;
        dataItem.PropertyChanged += DataItem_PropertyChanged;
    }

    private void DataItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyEdited();
    }

    private bool TryMoveDataItem(int exportIndex, int fromIndex, int toIndex)
    {
        NonBlueprintExportItem? exportItem = FindExport(exportIndex);
        if (exportItem == null ||
            fromIndex < 0 ||
            fromIndex >= exportItem.DataItems.Count ||
            toIndex < 0 ||
            toIndex >= exportItem.DataItems.Count ||
            fromIndex == toIndex)
        {
            return false;
        }

        NonBlueprintDataItem item = exportItem.DataItems[fromIndex];
        exportItem.DataItems.RemoveAt(fromIndex);
        exportItem.DataItems.Insert(toIndex, item);
        NotifyEdited();
        return true;
    }

    private NonBlueprintExportItem? FindExport(int exportIndex)
    {
        return Exports.FirstOrDefault(item => item.ExportIndex == exportIndex);
    }

    private static bool IsEditableNonBlueprintExport(JsonObject exportObj)
    {
        string exportType = ReadString(exportObj["$type"]);
        return !exportType.Contains("FunctionExport", StringComparison.Ordinal) &&
            !exportType.Contains("ClassExport", StringComparison.Ordinal);
    }

    private static NonBlueprintDataItem? CreateDataItem(JsonObject dataObj, int ordinal)
    {
        string name = ReadString(dataObj["Name"]);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new NonBlueprintDataItem
        {
            Name = name,
            Value = NormalizeEditableValue(ReadEditableString(dataObj["Value"]), SimplifyTypeName(ReadString(dataObj["$type"]))),
            Namespace = ReadEditableString(dataObj["Namespace"]),
            CultureInvariantString = ReadEditableString(dataObj["CultureInvariantString"]),
            TemplateJson = dataObj.ToJsonString(),
            PropertyType = SimplifyTypeName(ReadString(dataObj["$type"])),
            EnumOptions = ReadEnumOptions(dataObj),
            OriginalOrdinal = ordinal
        };
    }

    private static List<string> ReadEnumOptions(JsonObject dataObj)
    {
        string propertyType = SimplifyTypeName(ReadString(dataObj["$type"]));
        if (!string.Equals(propertyType, "EnumPropertyData", StringComparison.Ordinal))
            return [];

        string value = ReadEditableString(dataObj["Value"]);
        return string.IsNullOrWhiteSpace(value) ? [] : [value];
    }

    private static JsonObject? BuildDataObject(NonBlueprintDataItem dataItem, IReadOnlyDictionary<string, DataPropertyTemplate> dataTemplatesByName)
    {
        string templateJson = dataTemplatesByName.TryGetValue(dataItem.Name, out DataPropertyTemplate? template) &&
            !string.IsNullOrWhiteSpace(template.TemplateJson)
                ? template.TemplateJson
                : dataItem.TemplateJson;

        JsonObject? dataObj = ParseJsonObject(templateJson);
        if (dataObj == null)
            return null;

        dataObj["Name"] = dataItem.Name;
        SetEditableJsonValue(dataObj, "Value", dataItem.Value, forceWhenMissing: !string.IsNullOrWhiteSpace(dataItem.Value));
        SetEditableJsonValue(dataObj, "Namespace", dataItem.Namespace, forceWhenMissing: !string.IsNullOrWhiteSpace(dataItem.Namespace));
        SetEditableJsonValue(dataObj, "CultureInvariantString", dataItem.CultureInvariantString, forceWhenMissing: !string.IsNullOrWhiteSpace(dataItem.CultureInvariantString));
        return dataObj;
    }

    private static void SetEditableJsonValue(JsonObject obj, string propertyName, string editedValue, bool forceWhenMissing)
    {
        bool exists = obj.TryGetPropertyValue(propertyName, out JsonNode? exemplar);
        if (!exists && !forceWhenMissing)
            return;

        obj[propertyName] = ConvertEditedStringToJsonNode(editedValue, exemplar);
    }

    private static JsonNode? ConvertEditedStringToJsonNode(string text, JsonNode? exemplar)
    {
        text ??= string.Empty;
        if (exemplar is JsonValue value)
        {
            if (value.TryGetValue<string>(out _))
                return JsonValue.Create(text);
            if (value.TryGetValue<bool>(out _))
                return JsonValue.Create(bool.TryParse(text, out bool boolValue) && boolValue);
            if (value.TryGetValue<int>(out _))
                return JsonValue.Create(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue) ? intValue : 0);
            if (value.TryGetValue<long>(out _))
                return JsonValue.Create(long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue) ? longValue : 0);
            if (value.TryGetValue<double>(out _))
                return JsonValue.Create(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue) ? doubleValue : 0d);
        }

        string trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return JsonValue.Create(text);
            }
        }

        return JsonValue.Create(text);
    }

    private static JsonObject? ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadEditableString(JsonNode? node)
    {
        if (node == null)
            return string.Empty;

        if (node is JsonValue value && value.TryGetValue<string>(out string? text))
            return text ?? string.Empty;

        return node.ToJsonString();
    }

    private static string NormalizeEditableValue(string value, string propertyType)
    {
        if (string.Equals(propertyType, "BoolPropertyData", StringComparison.Ordinal) &&
            bool.TryParse(value, out bool boolValue))
        {
            return boolValue ? "true" : "false";
        }

        return value ?? string.Empty;
    }

    private static string ReadString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out string? text)
            ? text ?? string.Empty
            : string.Empty;
    }

    private static string SimplifyTypeName(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
            return string.Empty;

        string typeName = rawType.Split(',')[0].Trim();
        int lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
    }
}

public sealed class NonBlueprintExportsControl : UserControl
{
    private readonly NonBlueprintExportEditorState state;
    private readonly Func<DataPropertyTemplate?> selectedDataTemplateProvider;
    private readonly Func<string, DataPropertyTemplate?> dataTemplateResolver;
    private readonly Dictionary<DataGridViewCell, string> editOriginalValues = new();
    private readonly HashSet<DataGridViewCell> numericValidationReportedCells = new();

    public NonBlueprintExportsControl(
        NonBlueprintExportEditorState state,
        Func<DataPropertyTemplate?> selectedDataTemplateProvider,
        Func<string, DataPropertyTemplate?> dataTemplateResolver)
    {
        this.state = state;
        this.selectedDataTemplateProvider = selectedDataTemplateProvider;
        this.dataTemplateResolver = dataTemplateResolver;
        Dock = DockStyle.Fill;
        BuildControl();
    }

    private void BuildControl()
    {
        Controls.Clear();

        if (!state.HasExports)
        {
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "没有可编辑的非蓝图 Export Data。",
                TextAlign = ContentAlignment.MiddleCenter
            });
            return;
        }

        TabControl exportTabs = new()
        {
            Dock = DockStyle.Fill
        };

        foreach (NonBlueprintExportItem exportItem in state.Exports)
        {
            TabPage page = new()
            {
                Text = exportItem.DisplayName,
                ToolTipText = exportItem.ExportType
            };

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            ToolStrip toolStrip = new()
            {
                GripStyle = ToolStripGripStyle.Hidden
            };
            ToolStripButton addButton = new("增加") { ToolTipText = "从 Data 模板库追加到当前 Export Data 末尾" };
            ToolStripButton deleteButton = new("删除");
            ToolStripButton moveUpButton = new("上移");
            ToolStripButton moveDownButton = new("下移");
            toolStrip.Items.AddRange([addButton, deleteButton, moveUpButton, moveDownButton]);

            DataGridView grid = CreateDataGrid(exportItem);
            addButton.Click += (_, _) => AddSelectedTemplate(grid, exportItem.ExportIndex);
            deleteButton.Click += (_, _) => DeleteSelectedRow(grid, exportItem.ExportIndex);
            moveUpButton.Click += (_, _) => MoveSelectedRow(grid, exportItem.ExportIndex, moveUp: true);
            moveDownButton.Click += (_, _) => MoveSelectedRow(grid, exportItem.ExportIndex, moveUp: false);

            layout.Controls.Add(toolStrip, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            page.Controls.Add(layout);
            exportTabs.TabPages.Add(page);
        }

        Controls.Add(exportTabs);
    }

    private DataGridView CreateDataGrid(NonBlueprintExportItem exportItem)
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            ScrollBars = ScrollBars.Vertical,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            DataSource = exportItem.DataItems
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(NonBlueprintDataItem.Name),
            HeaderText = "Name",
            DataPropertyName = nameof(NonBlueprintDataItem.Name),
            ReadOnly = true,
            FillWeight = 22
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(NonBlueprintDataItem.Value),
            HeaderText = "Value",
            DataPropertyName = nameof(NonBlueprintDataItem.Value),
            FillWeight = 28
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(NonBlueprintDataItem.Namespace),
            HeaderText = "Namespace",
            DataPropertyName = nameof(NonBlueprintDataItem.Namespace),
            FillWeight = 25
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(NonBlueprintDataItem.CultureInvariantString),
            HeaderText = "CultureInvariantString",
            DataPropertyName = nameof(NonBlueprintDataItem.CultureInvariantString),
            FillWeight = 35
        });

        grid.DataBindingComplete += (_, _) => ConfigureValueCells(grid);
        grid.RowsAdded += (_, _) => ConfigureValueCells(grid);
        grid.CellBeginEdit += Grid_CellBeginEdit;
        grid.CellEndEdit += Grid_CellEndEdit;
        grid.EditingControlShowing += Grid_EditingControlShowing;
        grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
        grid.CellValidating += Grid_CellValidating;
        grid.CellParsing += Grid_CellParsing;
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.CellValueChanged += Grid_CellValueChanged;
        grid.UserDeletedRow += (_, _) => state.NotifyEdited();
        ConfigureValueCells(grid);
        return grid;
    }

    private void AddSelectedTemplate(DataGridView grid, int exportIndex)
    {
        DataPropertyTemplate? template = selectedDataTemplateProvider();
        if (template == null)
        {
            MessageBox.Show("请先在 Data 模板库中选择一个条目。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int oldCount = grid.Rows.Count;
        if (!state.TryAddDataItem(exportIndex, template))
        {
            MessageBox.Show("增加失败：选中的 Data 模板不可用。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SelectRow(grid, oldCount);
    }

    private void ConfigureValueCells(DataGridView grid)
    {
        if (!grid.Columns.Contains(nameof(NonBlueprintDataItem.Value)))
        {
            return;
        }

        int valueColumnIndex = grid.Columns[nameof(NonBlueprintDataItem.Value)].Index;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow ||
                row.Index < 0 ||
                row.Index >= grid.Rows.Count ||
                row.DataBoundItem is not NonBlueprintDataItem item)
            {
                continue;
            }

            DataGridViewCell currentCell = row.Cells[valueColumnIndex];
            if (grid.IsCurrentCellInEditMode &&
                grid.CurrentCell != null &&
                grid.CurrentCell.RowIndex == row.Index &&
                grid.CurrentCell.ColumnIndex == valueColumnIndex)
            {
                continue;
            }

            if (!ShouldUseValueComboBox(item))
            {
                if (currentCell is not DataGridViewTextBoxCell)
                    row.Cells[valueColumnIndex] = new DataGridViewTextBoxCell { Value = item.Value };
                continue;
            }

            if (currentCell is DataGridViewComboBoxCell existingComboCell)
            {
                UpdateComboBoxCellItems(existingComboCell, item);
                existingComboCell.Value = item.Value;
                continue;
            }

            DataGridViewComboBoxCell comboCell = new()
            {
                FlatStyle = FlatStyle.Flat,
                Value = item.Value
            };
            UpdateComboBoxCellItems(comboCell, item);
            row.Cells[valueColumnIndex] = comboCell;
        }
    }

    private void UpdateComboBoxCellItems(DataGridViewComboBoxCell comboCell, NonBlueprintDataItem item)
    {
        comboCell.Items.Clear();
        comboCell.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;

        foreach (string option in GetValueOptions(item))
        {
            if (!comboCell.Items.Contains(option))
                comboCell.Items.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(item.Value) && !comboCell.Items.Contains(item.Value))
            comboCell.Items.Add(item.Value);
    }

    private void Grid_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (sender is not DataGridView grid ||
            grid.CurrentCell == null ||
            !IsValueColumn(grid, grid.CurrentCell.ColumnIndex) ||
            GetDataItem(grid, grid.CurrentCell.RowIndex) is not NonBlueprintDataItem item ||
            e.Control is not DataGridViewComboBoxEditingControl comboBox)
        {
            return;
        }

        comboBox.DropDownStyle = IsEnumProperty(item)
            ? ComboBoxStyle.DropDown
            : ComboBoxStyle.DropDownList;
        comboBox.AutoCompleteMode = IsEnumProperty(item)
            ? AutoCompleteMode.SuggestAppend
            : AutoCompleteMode.None;
        comboBox.AutoCompleteSource = AutoCompleteSource.ListItems;
    }

    private void Grid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (sender is not DataGridView grid || !grid.IsCurrentCellDirty)
            return;

        NonBlueprintDataItem? item = grid.CurrentCell == null
            ? null
            : GetDataItem(grid, grid.CurrentCell.RowIndex);
        if (item != null && IsEnumProperty(item))
            return;

        if (grid.EditingControl is DataGridViewComboBoxEditingControl)
            grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void Grid_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (sender is not DataGridView grid ||
            !IsValueColumn(grid, e.ColumnIndex) ||
            GetDataItem(grid, e.RowIndex) is not NonBlueprintDataItem item ||
            !ShouldValidateNumericValue(item))
        {
            return;
        }

        DataGridViewCell cell = grid[e.ColumnIndex, e.RowIndex];
        editOriginalValues[cell] = item.Value;
        numericValidationReportedCells.Remove(cell);
    }

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is DataGridView grid &&
            e.RowIndex >= 0 &&
            e.ColumnIndex >= 0)
        {
            DataGridViewCell cell = grid[e.ColumnIndex, e.RowIndex];
            editOriginalValues.Remove(cell);
            numericValidationReportedCells.Remove(cell);
        }
    }

    private void Grid_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (sender is not DataGridView grid ||
            !IsValueColumn(grid, e.ColumnIndex) ||
            GetDataItem(grid, e.RowIndex) is not NonBlueprintDataItem item)
        {
            return;
        }

        if (ShouldValidateNumericValue(item) &&
            !TryValidateNumericValue(e.FormattedValue?.ToString() ?? string.Empty, item.PropertyType, out string errorMessage))
        {
            RestoreInvalidNumericValue(grid, e.RowIndex, e.ColumnIndex, item, errorMessage);
            return;
        }

        if (grid[e.ColumnIndex, e.RowIndex] is DataGridViewComboBoxCell comboCell &&
            IsEnumProperty(item))
        {
            string text = e.FormattedValue?.ToString() ?? string.Empty;
            if (!comboCell.Items.Contains(text))
                comboCell.Items.Add(text);
        }
    }

    private void Grid_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
    {
        if (sender is not DataGridView grid ||
            !IsValueColumn(grid, e.ColumnIndex) ||
            GetDataItem(grid, e.RowIndex) is not NonBlueprintDataItem item)
        {
            return;
        }

        if (ShouldValidateNumericValue(item))
        {
            string numericText = e.Value?.ToString() ?? string.Empty;
            if (!TryValidateNumericValue(numericText, item.PropertyType, out string errorMessage))
            {
                e.Value = GetOriginalEditValue(grid, e.RowIndex, e.ColumnIndex, item);
                e.ParsingApplied = true;
                RestoreInvalidNumericValue(grid, e.RowIndex, e.ColumnIndex, item, errorMessage);
                return;
            }

            e.Value = numericText;
            e.ParsingApplied = true;
            return;
        }

        if (!ShouldUseValueComboBox(item))
            return;

        string text = NormalizeEditableValue(e.Value?.ToString() ?? string.Empty, item.PropertyType);
        item.Value = text;
        if (IsEnumProperty(item) && !item.EnumOptions.Contains(text, StringComparer.Ordinal))
            item.EnumOptions.Add(text);

        if (grid[e.ColumnIndex, e.RowIndex] is DataGridViewComboBoxCell comboCell &&
            !comboCell.Items.Contains(text))
        {
            comboCell.Items.Add(text);
        }

        e.Value = text;
        e.ParsingApplied = true;
    }

    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (sender is DataGridView grid &&
            e.RowIndex >= 0 &&
            IsValueColumn(grid, e.ColumnIndex) &&
            GetDataItem(grid, e.RowIndex) is NonBlueprintDataItem item &&
            ShouldUseValueComboBox(item))
        {
            string text = NormalizeEditableValue(grid[e.ColumnIndex, e.RowIndex].Value?.ToString() ?? string.Empty, item.PropertyType);
            if (!string.Equals(item.Value, text, StringComparison.Ordinal))
                item.Value = text;

            if (grid[e.ColumnIndex, e.RowIndex] is DataGridViewComboBoxCell comboCell &&
                !comboCell.Items.Contains(text))
            {
                comboCell.Items.Add(text);
            }
        }

        state.NotifyEdited();
    }

    private void RestoreInvalidNumericValue(DataGridView grid, int rowIndex, int columnIndex, NonBlueprintDataItem item, string errorMessage)
    {
        string originalValue = GetOriginalEditValue(grid, rowIndex, columnIndex, item);
        item.Value = originalValue;
        grid[columnIndex, rowIndex].Value = originalValue;
        if (numericValidationReportedCells.Add(grid[columnIndex, rowIndex]))
            MessageBox.Show(errorMessage, "数据类型错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private string GetOriginalEditValue(DataGridView grid, int rowIndex, int columnIndex, NonBlueprintDataItem item)
    {
        return editOriginalValues.TryGetValue(grid[columnIndex, rowIndex], out string? originalValue)
            ? originalValue
            : item.Value;
    }

    private void DeleteSelectedRow(DataGridView grid, int exportIndex)
    {
        int rowIndex = GetSelectedRowIndex(grid);
        if (state.TryDeleteDataItem(exportIndex, rowIndex))
            SelectRow(grid, Math.Min(rowIndex, grid.Rows.Count - 1));
    }

    private void MoveSelectedRow(DataGridView grid, int exportIndex, bool moveUp)
    {
        int rowIndex = GetSelectedRowIndex(grid);
        bool moved = moveUp
            ? state.TryMoveDataItemUp(exportIndex, rowIndex)
            : state.TryMoveDataItemDown(exportIndex, rowIndex);
        if (moved)
            SelectRow(grid, moveUp ? rowIndex - 1 : rowIndex + 1);
    }

    private static bool IsValueColumn(DataGridView grid, int columnIndex)
    {
        return columnIndex >= 0 &&
            columnIndex < grid.Columns.Count &&
            string.Equals(grid.Columns[columnIndex].Name, nameof(NonBlueprintDataItem.Value), StringComparison.Ordinal);
    }

    private static NonBlueprintDataItem? GetDataItem(DataGridView grid, int rowIndex)
    {
        return rowIndex >= 0 && rowIndex < grid.Rows.Count
            ? grid.Rows[rowIndex].DataBoundItem as NonBlueprintDataItem
            : null;
    }

    private static bool ShouldUseValueComboBox(NonBlueprintDataItem item)
    {
        return IsBoolProperty(item) || IsEnumProperty(item);
    }

    private static bool IsBoolProperty(NonBlueprintDataItem item)
    {
        return string.Equals(item.PropertyType, "BoolPropertyData", StringComparison.Ordinal);
    }

    private static bool IsEnumProperty(NonBlueprintDataItem item)
    {
        return string.Equals(item.PropertyType, "EnumPropertyData", StringComparison.Ordinal);
    }

    private static bool ShouldValidateNumericValue(NonBlueprintDataItem item)
    {
        return string.Equals(item.PropertyType, "IntPropertyData", StringComparison.Ordinal) ||
            string.Equals(item.PropertyType, "FloatPropertyData", StringComparison.Ordinal) ||
            string.Equals(item.PropertyType, "DoublePropertyData", StringComparison.Ordinal);
    }

    private static bool TryValidateNumericValue(string value, string propertyType, out string errorMessage)
    {
        string text = value?.Trim() ?? string.Empty;
        if (string.Equals(propertyType, "IntPropertyData", StringComparison.Ordinal))
        {
            bool valid = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            errorMessage = valid ? string.Empty : $"Value 必须是 int 整数，当前输入：{value}";
            return valid;
        }

        if (string.Equals(propertyType, "FloatPropertyData", StringComparison.Ordinal))
        {
            bool valid = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue) &&
                !float.IsNaN(parsedValue) &&
                !float.IsInfinity(parsedValue);
            errorMessage = valid ? string.Empty : $"Value 必须是 float 数值，当前输入：{value}";
            return valid;
        }

        if (string.Equals(propertyType, "DoublePropertyData", StringComparison.Ordinal))
        {
            bool valid = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue) &&
                !double.IsNaN(parsedValue) &&
                !double.IsInfinity(parsedValue);
            errorMessage = valid ? string.Empty : $"Value 必须是 double 数值，当前输入：{value}";
            return valid;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static string NormalizeEditableValue(string value, string propertyType)
    {
        if (string.Equals(propertyType, "BoolPropertyData", StringComparison.Ordinal) &&
            bool.TryParse(value, out bool boolValue))
        {
            return boolValue ? "true" : "false";
        }

        return value ?? string.Empty;
    }

    private IEnumerable<string> GetValueOptions(NonBlueprintDataItem item)
    {
        if (IsBoolProperty(item))
            return ["true", "false"];

        DataPropertyTemplate? template = dataTemplateResolver(item.Name);
        return item.EnumOptions
            .Concat(template?.EnumOptions ?? [])
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(option => option, StringComparer.Ordinal)
            .ToList();
    }

    private static int GetSelectedRowIndex(DataGridView grid)
    {
        return grid.SelectedRows.Count > 0
            ? grid.SelectedRows[0].Index
            : grid.CurrentCell?.RowIndex ?? -1;
    }

    private static void SelectRow(DataGridView grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            return;

        grid.ClearSelection();
        grid.Rows[rowIndex].Selected = true;
        grid.CurrentCell = grid.Rows[rowIndex].Cells.Cast<DataGridViewCell>().FirstOrDefault(cell => cell.Visible);
    }
}
