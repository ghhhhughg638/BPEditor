using UAssetAPI;
using UAssetAPI.UnrealTypes;
using System.Text.Encodings.Web;
using System.Text.Json;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Collections.Concurrent;
using System.Reflection;
using UAssetAPI.FieldTypes;
using System.Text.Json.Serialization.Metadata;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Timer = System.Threading.Timer;

namespace BPEditor
{
    public partial class Form1 : Form
    {
        private const int MaxBlueprintNodes = 4000;
        private const int MaxExpressionDepth = 64;
        public string PakKey = "";
        public string WorkDir = "";
        public EngineVersion engineVersion = EngineVersion.UNKNOWN;
        public string UsmapPath = "";
        public bool DontReCom = false;
        public EngineVersion exportEngineVersion = EngineVersion.UNKNOWN;
        public PakVersion exportPakVersion = PakVersion.V11;

        private Dictionary<string, NodeDefinition> nodeDefinitions = new Dictionary<string, NodeDefinition>();
        private Dictionary<int, string> bytecodeObjectNameMap = new Dictionary<int, string>();
        private readonly Dictionary<string, string> nodeDefinitionNameBySignature = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> nextNodeDefinitionSuffixByBaseName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> knownFunctionParametersByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> functionDefinitionAssetPathByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BlueprintFunctionTemplate> functionTemplatesByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DataPropertyTemplate> dataTemplatesByName = new(StringComparer.Ordinal);
        private readonly BindingSource dataLibraryBindingSource = new();
        private readonly List<DataPropertyTemplate> sortedDataTemplateCache = new();
        private bool dataLibraryCacheDirty = true;
        private readonly Dictionary<string, BlueprintAssetContext> sourceAssetContextByRelativePath = new(StringComparer.Ordinal);
        private BlueprintAssetContext? currentImportAssetContext;
        private NodeDefinition? nodeLibraryContextNode;

        // 当前显示的节点定义列表（用于过滤）
        private List<NodeDefinition> displayedDefinitions = new List<NodeDefinition>();
        private string currentPakMountPointRaw = string.Empty;
        private string currentPakMountPointDisplay = string.Empty;
        private readonly string bpeiniPath;

        private string currentPakFile = "";
        private PakReader? currentPakReader = null;
        private FileStream? currentPakFileStream = null;

        [DllImport("kernel32.dll")]
        static extern bool IsDebuggerPresent();
        private readonly Timer timer;
        private DateTime? tplast = null;
        private bool BeDebugged = false;

        public bool IsDebug =>
#if DEBUG
true;
#else
false;
#endif
        public bool IsUserDebug = false;
        public string Version = "v1.7";

        private sealed record FunctionSignature(string Name, List<string> Parameters, List<string> ReturnValues, string SourceAssetRelativePath);
        private sealed record ExternalCallSignature(string Name, int ParameterCount, bool IncludeTargetPin, string? RepresentativeTemplateJson, string? RepresentativeExpressionType, List<NodeReferenceDescriptor> ReferenceDescriptors, string? SourceAssetRelativePath);
        private sealed class OpenAssetTabInfo
        {
            public required string Path { get; init; }
            public required bool IsWorkload { get; set; }
            public BlueprintCanvas? BlueprintCanvas { get; set; }
            public NonBlueprintExportEditorState? NonBlueprintExportState { get; set; }
            public BlueprintAssetContext AssetContext { get; set; } = BlueprintAssetContext.CreateFallback(string.Empty);
        }

        private sealed class LoadedAssetEditorData
        {
            public string AssetJson { get; init; } = string.Empty;
            public BlueprintData? BlueprintData { get; init; }
            public BlueprintAssetContext AssetContext { get; init; } = BlueprintAssetContext.CreateFallback(string.Empty);
            public NonBlueprintExportEditorState NonBlueprintExportState { get; init; } = NonBlueprintExportEditorState.FromAssetJson(string.Empty);
            public Dictionary<string, DataPropertyTemplate> DataTemplates { get; init; } = new(StringComparer.Ordinal);
            public Exception? BlueprintParseException { get; init; }
        }

        private sealed class PakReadSession : IDisposable
        {
            public FileStream Stream { get; }
            public PakReader Reader { get; }

            public PakReadSession(FileStream stream, PakReader reader)
            {
                Stream = stream;
                Reader = reader;
            }

            public void Dispose()
            {
                Stream.Dispose();
            }
        }

        public Form1()
        {
            timer = new(TimerCallback, null, 0, 500);

            InitializeComponent();

            // 配置节点库为自定义绘制
            nodeLibrary.DrawMode = DrawMode.OwnerDrawFixed;
            nodeLibrary.ItemHeight = 48;  // 增加行高以容纳更多信息
            nodeLibrary.DrawItem += NodeLibrary_DrawItem;

            tabControl1.TabPageClosing += TabControl1_TabPageClosing;
            InitializeDataLibrary();

            InitializeBuiltinDefinitions();
            UpdateNodeLibrary("");  // 初始显示全部
            UpdateDataLibrary("");
            SetupDragDrop();

            FormClosing += Form1_FormClosing;

            if (IsDebug) blueprintDatajsonToolStripMenuItem.Visible = true;

            tabPage3.Parent = null;
            tabPage4.Parent = null;

            string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            bpeiniPath = Path.Combine(localAppDataPath, "BPEditor", "appsettings.bpeini");
            if (File.Exists(bpeiniPath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(bpeiniPath);
                    PakKey = lines[0];
                    WorkDir = lines[1];
                    engineVersion = (EngineVersion)int.Parse(lines[2]);
                    UsmapPath = lines[3];
                    DontReCom = bool.Parse(lines[4]);
                    exportEngineVersion = (EngineVersion)int.Parse(lines[5]);
                    exportPakVersion = (PakVersion)int.Parse(lines[6]);
                }
                catch (Exception ex)
                {
                    /*
                    _ = MessageBox.Show("错误：无法读取配置文件\n" + ex, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    PakKey = "";
                    WorkDir = "";
                    engineVersion = EngineVersion.UNKNOWN;
                    UsmapPath = "";
                    DontReCom = false;
                    */
                }
                TryClearWorkDir().GetAwaiter().OnCompleted(() =>
                {
                    try
                    {
                        label1.Text = "正常";
                        label1.Refresh();
                    }
                    catch { }
                });
            }
        }

        private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bpeiniPath));
                string[] lines = [
                    PakKey,
                    WorkDir,
                    ((int)engineVersion).ToString(),
                    UsmapPath,
                    DontReCom.ToString(),
                    ((int)exportEngineVersion).ToString(),
                    ((int)exportPakVersion).ToString(),
                ];
                File.WriteAllLines(bpeiniPath, lines);
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show("错误：无法保存配置文件\n" + ex, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            await TryClearWorkDir();
        }

        private void TimerCallback(object? state)
        {
            if (tplast != null)
            {
                if (DateTime.Now - tplast >= TimeSpan.FromMilliseconds(1000))
                {
                    //_ = MessageBox.Show("触发断点了！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                    BeDebugged = true;
                }
            }
            tplast = DateTime.Now;
        }

        private async Task TryClearWorkDir()
        {
            try
            {
                label1.Text = "清理工作区......";
                label1.Refresh();
            }
            catch { }
            try
            {
                Enabled = false;
            }
            catch { }
            await Task.Run(() =>
            {
                if (!string.IsNullOrWhiteSpace(WorkDir))
                {
                    try
                    {
                        string workloadPath = Path.Combine(WorkDir, "Workload");
                        Directory.Delete(workloadPath, true);
                        Directory.CreateDirectory(workloadPath);
                    }
                    catch { }
                    if (!DontReCom)
                    {
                        try
                        {
                            string sourcePath = Path.Combine(WorkDir, "Source");
                            Directory.Delete(sourcePath, true);
                            Directory.CreateDirectory(sourcePath);
                        }
                        catch { }
                    }
                }
            });
            try
            {
                Enabled = true;
            }
            catch { }
        }

        private void TabControl1_TabPageClosing(object? sender, TabPageClosingEventArgs e)
        {
            if (tabControl1.TabCount == 1)
            {
                当前文件ToolStripMenuItem.Enabled = false;
                tabPage3.Parent = null;
                tabPage4.Parent = null;
            }
            if (e.TabPage.Text.StartsWith("[工作区] ") && !File.Exists(Path.Combine(WorkDir, "Workload", e.TabPage.ToolTipText + ".json")))
            {
                TreeNode? node = FindTreeNodeByPath(treeView2, e.TabPage.ToolTipText);
                if (node != null)
                {
                    RemoveAloneNode(node);
                    if (treeView2.Nodes.Count == 0) 整个工作区pakToolStripMenuItem.Enabled = false;
                }
            }
        }

        private void InitializeBuiltinDefinitions()
        {
            nodeDefinitions.Clear();
            nodeDefinitionNameBySignature.Clear();
            nextNodeDefinitionSuffixByBaseName.Clear();
            knownFunctionParametersByName.Clear();
            functionDefinitionAssetPathByName.Clear();
            functionTemplatesByName.Clear();
            dataTemplatesByName.Clear();
            MarkDataLibraryTemplatesChanged();
            sourceAssetContextByRelativePath.Clear();

            EnsureNodeDefinition(
                "Get Variable",
                [],
                [new Pin("Value", PinDirection.Output, PinType.Data)],
                CreateGenericLocalVariableExpression().ToJsonString(),
                "LocalVariable");
            EnsureNodeDefinition(
                "Set Variable",
                [new Pin("In", PinDirection.Input, PinType.Exec), new Pin("Value", PinDirection.Input, PinType.Data)],
                [new Pin("Out", PinDirection.Output, PinType.Exec), new Pin("Value", PinDirection.Output, PinType.Data)],
                CreateGenericSetVariableExpression().ToJsonString(),
                "Let");
            EnsureNodeDefinition("Self", [], [new Pin("Value", PinDirection.Output, PinType.Data)], new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Self, UAssetAPI"
            }.ToJsonString(), "Self");
            EnsureNodeDefinition("Int Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            EnsureNodeDefinition("Float Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            EnsureNodeDefinition("Double Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            EnsureNodeDefinition("Byte Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            EnsureNodeDefinition("String Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            EnsureNodeDefinition("Text Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)], CreateGenericTextConstExpression().ToJsonString(), "TextConst");
            EnsureNodeDefinition("Name Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            EnsureNodeDefinition("Bool Const", [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            EnsureNodeDefinition("No Object", [], [new Pin("Value", PinDirection.Output, PinType.Data)], new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_NoObject, UAssetAPI"
            }.ToJsonString(), "NoObject");
            EnsureNodeDefinition("Return", [new Pin("In", PinDirection.Input, PinType.Exec), new Pin("Return", PinDirection.Input, PinType.Data)], [new Pin("Out", PinDirection.Output, PinType.Exec)]);
            EnsureNodeDefinition("Jump", [new Pin("In", PinDirection.Input, PinType.Exec)], [new Pin("Out", PinDirection.Output, PinType.Exec), new Pin("To", PinDirection.Output, PinType.Exec)]);
            EnsureNodeDefinition(
                "Jump If Not",
                [new Pin("In", PinDirection.Input, PinType.Exec), new Pin("Condition", PinDirection.Input, PinType.Data)],
                [new Pin("Out", PinDirection.Output, PinType.Exec), new Pin("To", PinDirection.Output, PinType.Exec)]);
            EnsureNodeDefinition(
                "Computed Jump",
                [new Pin("In", PinDirection.Input, PinType.Exec), new Pin("Offset", PinDirection.Input, PinType.Data)],
                [new Pin("Out", PinDirection.Output, PinType.Exec)]);
            EnsureNodeDefinition("Push Flow", [new Pin("In", PinDirection.Input, PinType.Exec)], [new Pin("Out", PinDirection.Output, PinType.Exec), new Pin("To", PinDirection.Output, PinType.Exec)]);
            EnsureNodeDefinition("Pop Flow", [new Pin("In", PinDirection.Input, PinType.Exec)], [new Pin("Out", PinDirection.Output, PinType.Exec)]);
            EnsureNodeDefinition("Pop Flow If Not", [new Pin("In", PinDirection.Input, PinType.Exec), new Pin("Condition", PinDirection.Input, PinType.Data)], [new Pin("Out", PinDirection.Output, PinType.Exec)]);
            EnsureNodeDefinition("Context", [new Pin("Target", PinDirection.Input, PinType.Data)], [new Pin("Result", PinDirection.Output, PinType.Data)]);
            EnsureVariableNodeDefinitions();
        }

        private void EnsureVariableNodeDefinitions()
        {
            if (nodeDefinitions.TryGetValue("Get Variable", out NodeDefinition? getVariable))
            {
                bool hasValueOutput = getVariable.OutputPins.Any(pin =>
                    pin.Direction == PinDirection.Output &&
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, "Value", StringComparison.Ordinal));
                if (!hasValueOutput)
                    getVariable.OutputPins.Add(new Pin("Value", PinDirection.Output, PinType.Data));
            }

            if (nodeDefinitions.TryGetValue("Set Variable", out NodeDefinition? setVariable))
            {
                bool hasExecIn = setVariable.InputPins.Any(pin =>
                    pin.Direction == PinDirection.Input &&
                    pin.Type == PinType.Exec &&
                    string.Equals(pin.Name, "In", StringComparison.Ordinal));
                if (!hasExecIn)
                    setVariable.InputPins.Insert(0, new Pin("In", PinDirection.Input, PinType.Exec));

                bool hasValueInput = setVariable.InputPins.Any(pin =>
                    pin.Direction == PinDirection.Input &&
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, "Value", StringComparison.Ordinal));
                if (!hasValueInput)
                    setVariable.InputPins.Add(new Pin("Value", PinDirection.Input, PinType.Data));

                bool hasExecOut = setVariable.OutputPins.Any(pin =>
                    pin.Direction == PinDirection.Output &&
                    pin.Type == PinType.Exec &&
                    string.Equals(pin.Name, "Out", StringComparison.Ordinal));
                if (!hasExecOut)
                    setVariable.OutputPins.Insert(0, new Pin("Out", PinDirection.Output, PinType.Exec));

                bool hasValueOutput = setVariable.OutputPins.Any(pin =>
                    pin.Direction == PinDirection.Output &&
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, "Value", StringComparison.Ordinal));
                if (!hasValueOutput)
                    setVariable.OutputPins.Add(new Pin("Value", PinDirection.Output, PinType.Data));
            }
        }

        private void EnsureCallNodeDefinitionsHaveTargetPins()
        {
            foreach (NodeDefinition definition in nodeDefinitions.Values)
            {
                string baseDefinitionName = GetBaseDefinitionName(definition.Name);
                if (!baseDefinitionName.StartsWith("Call", StringComparison.Ordinal))
                    continue;

                bool hasTargetInput = definition.InputPins.Any(pin =>
                    pin.Direction == PinDirection.Input &&
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, "Target", StringComparison.Ordinal));
                if (hasTargetInput)
                    continue;

                int insertIndex = definition.InputPins.FindIndex(pin =>
                    pin.Direction == PinDirection.Input &&
                    pin.Type == PinType.Data);
                if (insertIndex < 0)
                    insertIndex = definition.InputPins.Count;

                definition.InputPins.Insert(insertIndex, new Pin("Target", PinDirection.Input, PinType.Data));
            }
        }

        /// <summary>
        /// 根据过滤条件更新节点库显示
        /// </summary>
        /// <param name="filter">过滤关键字（不区分大小写）</param>
        private void UpdateNodeLibrary(string filter)
        {
            nodeLibrary.BeginUpdate();

            displayedDefinitions.Clear();
            nodeLibrary.Items.Clear();

            IEnumerable<NodeDefinition> source = nodeDefinitions.Values
                .Where(def => !IsDeprecatedGenericCallOrEventDefinition(def.Name));
            if (!string.IsNullOrWhiteSpace(filter))
            {
                source = source.Where(def => def.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            displayedDefinitions.AddRange(source);
            foreach (var def in displayedDefinitions)
            {
                nodeLibrary.Items.Add(def);
            }

            nodeLibrary.EndUpdate();
        }

        private void InitializeDataLibrary()
        {
            dataLibrary.SuspendLayout();
            dataLibrary.AutoGenerateColumns = false;
            dataLibrary.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataLibrary.MultiSelect = false;
            dataLibrary.ReadOnly = true;
            dataLibrary.AllowUserToAddRows = false;
            dataLibrary.AllowUserToDeleteRows = false;
            dataLibrary.AllowUserToResizeRows = false;
            dataLibrary.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataLibrary.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            dataLibrary.RowTemplate.Height = 24;
            dataLibrary.Columns.Clear();
            dataLibrary.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "属性名称",
                DataPropertyName = nameof(DataPropertyTemplate.Name),
                FillWeight = 64,
                SortMode = DataGridViewColumnSortMode.Automatic
            });
            dataLibrary.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "PropertyType",
                HeaderText = "数据类型",
                DataPropertyName = nameof(DataPropertyTemplate.PropertyType),
                FillWeight = 42,
                SortMode = DataGridViewColumnSortMode.Automatic
            });
            /*
            dataLibrary.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "EnumOptions",
                HeaderText = "Enum选项",
                FillWeight = 55
            });
            */
            /*
            dataLibrary.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "OccurrenceCount",
                HeaderText = "出现次数",
                DataPropertyName = nameof(DataPropertyTemplate.OccurrenceCount),
                FillWeight = 28,
                SortMode = DataGridViewColumnSortMode.Automatic
            });
            */

            dataLibrary.DataSource = dataLibraryBindingSource;
            dataLibrary.CellFormatting += DataLibrary_CellFormatting;
            textBox2.KeyDown += textBox2_KeyDown;
            button2.Click += button2_Click;
            dataLibrary.ResumeLayout();
        }

        private void MarkDataLibraryTemplatesChanged()
        {
            dataLibraryCacheDirty = true;
        }

        private IReadOnlyList<DataPropertyTemplate> GetSortedDataTemplateCache()
        {
            if (!dataLibraryCacheDirty)
                return sortedDataTemplateCache;

            sortedDataTemplateCache.Clear();
            sortedDataTemplateCache.AddRange(dataTemplatesByName.Values);
            sortedDataTemplateCache.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            dataLibraryCacheDirty = false;
            return sortedDataTemplateCache;
        }

        private void UpdateDataLibrary(string filter)
        {
            string normalizedFilter = filter.Trim();
            IReadOnlyList<DataPropertyTemplate> source = GetSortedDataTemplateCache();
            List<DataPropertyTemplate> displayedTemplates;
            if (string.IsNullOrWhiteSpace(normalizedFilter))
            {
                displayedTemplates = source.ToList();
            }
            else
            {
                displayedTemplates = source
                    .Where(template => IsDataTemplateMatch(template, normalizedFilter))
                    .ToList();
            }

            dataLibrary.SuspendLayout();
            try
            {
                dataLibraryBindingSource.DataSource = displayedTemplates;
            }
            finally
            {
                dataLibrary.ResumeLayout();
            }
        }

        private static bool IsDataTemplateMatch(DataPropertyTemplate template, string filter)
        {
            return template.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                template.PropertyType.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (template.EnumType?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                template.EnumOptions.Any(option => option.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DataLibrary_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 ||
                e.ColumnIndex < 0 ||
                dataLibrary.Rows[e.RowIndex].DataBoundItem is not DataPropertyTemplate template)
            {
                return;
            }

            if (string.Equals(dataLibrary.Columns[e.ColumnIndex].Name, "EnumOptions", StringComparison.Ordinal))
            {
                e.Value = template.EnumOptions.Count == 0
                    ? string.Empty
                    : string.Join(", ", template.EnumOptions);
                e.FormattingApplied = true;
            }
        }

        private void textBox2_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                UpdateDataLibrary(textBox2.Text.Trim());
                e.SuppressKeyPress = true;
            }
        }

        private void button2_Click(object? sender, EventArgs e)
        {
            UpdateDataLibrary(textBox2.Text.Trim());
        }

        /// <summary>
        /// 自定义绘制节点库每一项，显示节点名称、输入/输出引脚统计
        /// </summary>
        private void NodeLibrary_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= nodeLibrary.Items.Count) return;

            NodeDefinition def = nodeLibrary.Items[e.Index] as NodeDefinition;
            if (def == null) return;

            e.DrawBackground();

            // 计算区域
            Rectangle rect = e.Bounds;
            Rectangle nameRect = new Rectangle(rect.X + 5, rect.Y + 2, rect.Width - 10, 20);
            Rectangle statsRect = new Rectangle(rect.X + 5, rect.Y + 22, rect.Width - 10, 20);

            // 绘制节点名称（粗体）
            using (Font nameFont = new Font(e.Font, FontStyle.Bold))
            using (Brush nameBrush = new SolidBrush(e.ForeColor))
            {
                e.Graphics.DrawString(def.Name, nameFont, nameBrush, nameRect);
            }

            // 统计引脚信息
            /*
            int inputCount = def.InputPins.Count;
            int outputCount = def.OutputPins.Count;
            int execInputs = def.InputPins.Count(p => p.Type == PinType.Exec);
            int dataInputs = inputCount - execInputs;
            int execOutputs = def.OutputPins.Count(p => p.Type == PinType.Exec);
            int dataOutputs = outputCount - execOutputs;
            */

            //string stats = $"输入: {inputCount} (执行:{execInputs} 数据:{dataInputs})  |  输出: {outputCount} (执行:{execOutputs} 数据:{dataOutputs})";
            bool HasOutput = false;
            foreach (Pin p in def.OutputPins)
            {
                if (p.Type == PinType.Data)
                {
                    HasOutput = true;
                    break;
                }
            }
            string stats = "";
            if (HasOutput)
            {
                stats += "DATA ";
            }
            else
            {
                stats += "VOID ";
            }
            stats += $"{def.Name.Replace(' ', '_')}(";
            foreach (Pin p in def.InputPins)
            {
                if (p.Type == PinType.Data) stats += "DATA " + p.Name.Replace(' ', '_') + ", ";
            }
            if (stats.EndsWith(", ")) stats = stats.Remove(stats.Length - 2);
            stats += ")";

            using (Font statsFont = new Font(e.Font.FontFamily, 8))
            using (Brush statsBrush = new SolidBrush(Color.Gray))
            {
                e.Graphics.DrawString(stats, statsFont, statsBrush, statsRect);
            }

            // 绘制焦点框
            e.DrawFocusRectangle();
        }

        private void SetupDragDrop()
        {
            nodeLibrary.MouseDown += NodeLibrary_MouseDown;
        }

        private void NodeLibrary_MouseDown(object? sender, MouseEventArgs e)
        {
            int idx = nodeLibrary.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= nodeLibrary.Items.Count)
                return;

            nodeLibrary.SelectedIndex = idx;
            NodeDefinition? def = nodeLibrary.Items[idx] as NodeDefinition;
            if (def == null)
                return;

            if (e.Button == MouseButtons.Left)
            {
                DoDragDrop(def, DragDropEffects.Copy);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                nodeLibraryContextNode = def;
                string? functionName = TryGetEventFunctionName(def.Name);
                if (string.IsNullOrWhiteSpace(functionName))
                    return;

                bool canJump = !string.IsNullOrWhiteSpace(functionName) &&
                    functionDefinitionAssetPathByName.ContainsKey(functionName);

                ContextMenuStrip menu = new();
                ToolStripMenuItem jumpMenuItem = new("跳转到定义")
                {
                    Enabled = canJump
                };
                jumpMenuItem.Click += NodeLibraryJumpToDefinitionMenuItem_Click;
                menu.Items.Add(jumpMenuItem);
                menu.Show(nodeLibrary, e.Location);
            }
        }

        private async void NodeLibraryJumpToDefinitionMenuItem_Click(object? sender, EventArgs e)
        {
            NodeDefinition? def = nodeLibraryContextNode;
            if (def == null)
                return;

            string? functionName = TryGetEventFunctionName(def.Name);
            if (string.IsNullOrWhiteSpace(functionName))
                return;

            if (!functionDefinitionAssetPathByName.TryGetValue(functionName, out string? assetRelativePath) ||
                string.IsNullOrWhiteSpace(assetRelativePath))
            {
                _ = MessageBox.Show($"未找到函数 {functionName} 的定义文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string treePath = BuildTreePathFromRelativeAssetPath(assetRelativePath);
            TreeNode? node = FindTreeNodeByPath(treeView1, treePath);
            if (node == null)
            {
                _ = MessageBox.Show($"在资源树中找不到文件：{assetRelativePath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ExpandAndSelectTreeNode(node);
            treeView1.Focus();
            await OpenAssetByTreePathAsync(GetNodePath(node), false);
        }

        private static string? TryGetEventFunctionName(string definitionName)
        {
            const string prefix = "Event ";
            if (!definitionName.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            string functionName = definitionName[prefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(functionName) ? null : functionName;
        }

        private string BuildTreePathFromRelativeAssetPath(string relativeAssetPath)
        {
            string normalized = NormalizePath(relativeAssetPath);
            if (string.IsNullOrWhiteSpace(currentPakMountPointDisplay))
                return normalized;

            return $"{NormalizePath(currentPakMountPointDisplay)}/{normalized}";
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/').Trim('/');
        }

        private static bool IsDeprecatedGenericCallOrEventDefinition(string definitionName)
        {
            string baseDefinitionName = GetBaseDefinitionName(definitionName);
            return string.Equals(baseDefinitionName, "Call", StringComparison.Ordinal) ||
                string.Equals(baseDefinitionName, "Event", StringComparison.Ordinal);
        }

        private static TreeNode? FindTreeNodeByPath(TreeView treeView, string treePath)
        {
            string[] parts = treePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return null;

            TreeNodeCollection currentCollection = treeView.Nodes;
            TreeNode? currentNode = null;
            foreach (string part in parts)
            {
                TreeNode? next = null;
                foreach (TreeNode candidate in currentCollection)
                {
                    if (string.Equals(candidate.Text, part, StringComparison.OrdinalIgnoreCase))
                    {
                        next = candidate;
                        break;
                    }
                }

                if (next == null)
                    return null;

                currentNode = next;
                currentCollection = next.Nodes;
            }

            return currentNode;
        }

        private static void ExpandAndSelectTreeNode(TreeNode node)
        {
            TreeNode? current = node.Parent;
            while (current != null)
            {
                current.Expand();
                current = current.Parent;
            }

            TreeView? tree = node.TreeView;
            if (tree != null)
            {
                tree.SelectedNode = node;
                node.EnsureVisible();
            }
        }

        private static byte[]? HexStringToBytes(string hexString)
        {
            try
            {
                if (hexString.StartsWith("0x")) hexString = hexString[2..];
                byte[] byteArray = Enumerable.Range(0, hexString.Length / 2)
                    .Select(i => Convert.ToByte(hexString.Substring(i * 2, 2), 16))
                    .ToArray();
                return byteArray;
            }
            catch
            {
                return null;
            }
        }

        private async void 打开ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (WorkDir == "" || WorkDir == null)
            {
                _ = MessageBox.Show("请先在 文件-设置 中选择一个文件夹作为工作目录", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            /*
            OpenFileDialog dialog = new()
            {
                Filter = "包文件（*.pak）|*.pak|资源文件（*.uasset;*.json）|*.uasset;*.json|所有文件（*.*）|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = "打开包或资源文件"
            };
            */
            OpenFileDialog dialog = new()
            {
                Filter = "包文件（*.pak）|*.pak|所有文件（*.*）|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = "打开包文件"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (dialog.FileName.EndsWith(".pak"))
                {
                    await ImportPakAndRefreshDefinitionsAsync(dialog.FileName);
                    await TryClearWorkDir();
                }
                else
                {

                }
            }
        }

        private async Task ImportPakAndRefreshDefinitionsAsync(string pakFilePath)
        {
            Enabled = false;
            try
            {
                IProgress<string> progress = new Progress<string>(message =>
                {
                    label1.Text = message;
                    label1.Update();
                });

                PakOpenResult result = await Task.Run(() => ReadPakIndex(pakFilePath, progress));

                tabControl1.SelectedIndex = -1;
                tabControl1.TabPages.Clear();
                treeView1.Nodes.Clear();
                treeView2.Nodes.Clear();
                当前文件ToolStripMenuItem.Enabled = false;
                InitializeBuiltinDefinitions();
                UpdateNodeLibrary(textBox1.Text.Trim());
                UpdateDataLibrary(textBox2.Text.Trim());

                currentPakMountPointRaw = result.MountPointRaw;
                currentPakMountPointDisplay = result.MountPointDisplay;
                functionDefinitionAssetPathByName.Clear();

                progress?.Report("构建树......");
                await YieldUiForProgressAsync();

                treeView1.BeginUpdate();
                try
                {
                    string[] files = result.Files.Where(file => file.EndsWith(".uasset")).ToArray();
                    if (!string.IsNullOrWhiteSpace(result.MountPointDisplay))
                    {
                        treeView1.Nodes.Add(result.MountPointDisplay);
                        TreeViewHelper.BuildTreeFromPaths(files, treeView1.Nodes[0]);
                    }
                    else
                    {
                        TreeViewHelper.BuildTreeFromPaths(files, treeView1);
                    }
                }
                finally
                {
                    treeView1.EndUpdate();
                }

                currentPakFile = pakFilePath;
                扫描节点定义ToolStripMenuItem.Enabled = true;
                节点定义表jsonToolStripMenuItem1.Enabled = true;

                progress?.Report($"成功载入 {Path.GetFileName(pakFilePath)} ，Pak版本：{result.PakVersion}，文件 {result.Files.Length} 个");
            }
            catch (Exception ex)
            {
                _ = MessageBox.Show("试图导入 Pak 文件时出现错误：\n" + ex, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Enabled = true;
            }
        }

        private async Task RegisterScannedDefinitionsAsync(PakImportResult result, IProgress<string>? progress)
        {
            progress?.Report("导入函数定义......");
            await YieldUiForProgressAsync();

            int functionTotal = result.Functions.Count;
            int functionCurrent = 0;
            foreach (FunctionSignature signature in result.Functions.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                RegisterFunctionDefinitions(signature.Name, signature.Parameters, signature.ReturnValues);
                functionDefinitionAssetPathByName[signature.Name] = signature.SourceAssetRelativePath;
                if (functionTemplatesByName.TryGetValue(signature.Name, out BlueprintFunctionTemplate? existingTemplate))
                    existingTemplate.SourceAssetRelativePath = signature.SourceAssetRelativePath;

                functionCurrent++;
                if (functionCurrent % 200 == 0 || functionCurrent == functionTotal)
                {
                    progress?.Report($"导入函数定义（{functionCurrent}/{functionTotal}）......");
                    await YieldUiForProgressAsync();
                }
            }

            int externalTotal = result.ExternalCalls.Count;
            int externalCurrent = 0;
            foreach (ExternalCallSignature externalCall in result.ExternalCalls.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                RegisterExternalCallDefinition(externalCall);

                externalCurrent++;
                if (externalCurrent % 200 == 0 || externalCurrent == externalTotal)
                {
                    progress?.Report($"导入外部Call定义（{externalCurrent}/{externalTotal}）......");
                    await YieldUiForProgressAsync();
                }
            }

            int dataTotal = result.DataTemplates.Count;
            foreach (DataPropertyTemplate dataTemplate in result.DataTemplates.Values)
            {
                MergeDataTemplate(dataTemplatesByName, dataTemplate);
            }
            MarkDataLibraryTemplatesChanged();
            UpdateDataLibrary(textBox2.Text.Trim());

            progress?.Report("扫描完成，函数定义 " + functionTotal + " 个，外部Call定义 " + externalTotal + " 个，Data模板 " + dataTotal + " 个");
        }

        private static async Task YieldUiForProgressAsync()
        {
            await Task.Yield();
        }

        private void 设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings s = new()
            {
                Owner = this
            };
            s.ShowDialog();
        }

        private sealed record PakOpenResult(
            string[] Files,
            string MountPointRaw,
            string MountPointDisplay,
            string PakVersion);

        private sealed record PakImportResult(
            string[] Files,
            string MountPointRaw,
            string MountPointDisplay,
            string PakVersion,
            Dictionary<string, FunctionSignature> Functions,
            Dictionary<string, ExternalCallSignature> ExternalCalls,
            Dictionary<string, DataPropertyTemplate> DataTemplates);
        private sealed class NodeDefinitionCachePackage
        {
            public Dictionary<string, NodeDefinition> NodeDefinitions { get; set; } = new(StringComparer.Ordinal);
            public Dictionary<string, List<string>> KnownFunctionParametersByName { get; set; } = new(StringComparer.Ordinal);
            public Dictionary<string, string> FunctionDefinitionAssetPathByName { get; set; } = new(StringComparer.Ordinal);
            public Dictionary<string, BlueprintFunctionTemplate> FunctionTemplatesByName { get; set; } = new(StringComparer.Ordinal);
            public Dictionary<string, DataPropertyTemplate> DataTemplatesByName { get; set; } = new(StringComparer.Ordinal);
        }

        private PakReader CreatePakReader(FileStream stream)
        {
            PakBuilder builder = new();
            if (!string.IsNullOrWhiteSpace(PakKey))
            {
                byte[]? key = HexStringToBytes(PakKey);
                if (key != null)
                    builder = builder.Key(key);
            }

            return builder.Reader(stream);
        }

        private PakReadSession CreatePakReadSession(string pakFilePath)
        {
            FileStream stream = File.Open(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new PakReadSession(stream, CreatePakReader(stream));
        }

        private PakOpenResult ReadPakIndex(string pakFilePath, IProgress<string>? progress)
        {
            progress?.Report("载入Pak......");

            FileStream stream = File.Open(pakFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            PakReader reader = CreatePakReader(stream);
            string[] files = reader.Files();
            string mountPointRaw = reader.GetMountPoint() ?? string.Empty;
            string mountPointDisplay = mountPointRaw.StartsWith("../../../", StringComparison.Ordinal)
                ? mountPointRaw[9..]
                : mountPointRaw;
            mountPointDisplay = mountPointDisplay.Trim('/');

            if (currentPakFileStream != null) currentPakFileStream.Dispose();

            currentPakFileStream = stream;
            currentPakReader = reader;

            return new PakOpenResult(files, mountPointRaw, mountPointDisplay, reader.GetVersion().ToString());
        }

        // 仅封装“解压Pak + 扫描并导入函数定义”流程，不在打开Pak时自动调用。
        private async Task ExtractCurrentPakAndImportFunctionDefinitionsAsync(IProgress<string>? progress)
        {
            PakImportResult? result = await Task.Run(() => ExtractCurrentPak(progress));
            if (result != null)
            {
                await RegisterScannedDefinitionsAsync(result, progress);
                UpdateNodeLibrary(textBox1.Text.Trim());
            }
        }

        private PakImportResult? ExtractCurrentPak(IProgress<string>? progress)
        {
            if (currentPakFile == "" || currentPakFile == null ||
                currentPakReader == null ||
                currentPakFileStream == null) return null;

            string pakFilePath = currentPakFile;
            PakReader reader = currentPakReader;

            string[] files = reader.Files();
            string mountPointRaw = reader.GetMountPoint() ?? string.Empty;
            string mountPointDisplay = mountPointRaw.StartsWith("../../../", StringComparison.Ordinal)
                ? mountPointRaw[9..]
                : mountPointRaw;
            mountPointDisplay = mountPointDisplay.Trim('/');

            if (!DontReCom)
            {
                progress?.Report("删除旧文件......");
                if (Directory.Exists(Path.Combine(WorkDir, "Source")))
                    Directory.Delete(Path.Combine(WorkDir, "Source"), true);
                Directory.CreateDirectory(Path.Combine(WorkDir, "Source"));
            }

            progress?.Report("解压Pak......");
            int extracted = 0;
            ParallelOptions extractOptions = new()
            {
                MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount)
            };
            Parallel.ForEach(
                files,
                extractOptions,
                () => CreatePakReadSession(pakFilePath),
                (file, _, _, session) =>
                {
                    string filepath = Path.Combine(WorkDir, "Source", file);

                    if (!(DontReCom && File.Exists(filepath)))
                    {
                        byte[] bytes = session.Reader.Get(session.Stream, file);
                        string? dir = Path.GetDirectoryName(filepath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllBytes(filepath, bytes);
                    }

                    int current = Interlocked.Increment(ref extracted);
                    if (current % 100 == 0 || current == files.Length)
                        progress?.Report($"解压Pak（{current}/{files.Length}）......");

                    return session;
                },
                session => session.Dispose());

            List<string> uassetPaths = files
                .Where(file => file.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.Combine(WorkDir, "Source", file))
                .Where(File.Exists)
                .ToList();

            (Dictionary<string, FunctionSignature> scannedFunctions, Dictionary<string, ExternalCallSignature> scannedExternalCalls, Dictionary<string, DataPropertyTemplate> scannedDataTemplates) =
                ScanFunctionDefinitionsFromAssets(uassetPaths, progress);
            return new PakImportResult(files, mountPointRaw, mountPointDisplay, reader.GetVersion().ToString(), scannedFunctions, scannedExternalCalls, scannedDataTemplates);
        }

        private (Dictionary<string, FunctionSignature> Functions, Dictionary<string, ExternalCallSignature> ExternalCalls, Dictionary<string, DataPropertyTemplate> DataTemplates) ScanFunctionDefinitionsFromAssets(
            IReadOnlyList<string> uassetPaths,
            IProgress<string>? progress)
        {
            progress?.Report($"扫描函数定义......");
            ConcurrentDictionary<string, FunctionSignature> signatures = new(StringComparer.Ordinal);
            ConcurrentDictionary<string, ExternalCallSignature> externalCalls = new(StringComparer.Ordinal);
            ConcurrentDictionary<string, DataPropertyTemplate> dataTemplates = new(StringComparer.Ordinal);
            if (uassetPaths.Count == 0)
            {
                return (
                    signatures.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                    externalCalls.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                    dataTemplates.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
            }

            int processed = 0;
            ParallelOptions options = new()
            {
                MaxDegreeOfParallelism = Math.Max(4, (Environment.ProcessorCount * 3) / 2)
            };
            ThreadLocal<UAssetAPI.Unversioned.Usmap?> mappingsByThread = new(() =>
            {
                if (string.IsNullOrWhiteSpace(UsmapPath)) return null;
                return new UAssetAPI.Unversioned.Usmap(UsmapPath);
            });

            Parallel.ForEach(uassetPaths, options, path =>
            {
                try
                {
                    UAsset uAsset = new(
                        path,
                        engineVersion,
                        mappingsByThread.Value!);
                    foreach (FunctionExport functionExport in uAsset.Exports.OfType<FunctionExport>())
                    {
                        string functionName = functionExport.ObjectName?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(functionName))
                            continue;

                        (List<string> parameters, List<string> returnValues) = GetFunctionSignatureParts(functionExport);
                        string relativeAssetPath = NormalizePath(Path.GetRelativePath(Path.Combine(WorkDir, "Source"), path));
                        FunctionSignature signature = new(functionName, parameters, returnValues, relativeAssetPath);
                        signatures.AddOrUpdate(
                            functionName,
                            signature,
                            (_, existing) => SelectBetterSignature(existing, signature));
                    }

                    string json = uAsset.SerializeJson();
                    JsonObject? root = JsonNode.Parse(json) as JsonObject;
                    if (root != null)
                    {
                        JsonArray? exports = root["Exports"] as JsonArray;
                        if (exports != null)
                        {
                            string relativeAssetPath = NormalizePath(Path.GetRelativePath(Path.Combine(WorkDir, "Source"), path));
                            Dictionary<int, BlueprintObjectSymbol> symbolMap = BuildBlueprintObjectSymbols(root);
                            foreach (JsonObject exportObj in exports.OfType<JsonObject>())
                            {
                                CollectDataTemplatesFromExport(exportObj, relativeAssetPath, dataTemplates);

                                string exportType = exportObj["$type"]?.GetValue<string>() ?? string.Empty;
                                JsonArray? scriptBytecode = exportObj["ScriptBytecode"] as JsonArray;
                                if (!exportType.Contains("FunctionExport", StringComparison.Ordinal) ||
                                scriptBytecode == null ||
                                scriptBytecode.Count == 0)
                                {
                                    continue;
                                }

                                foreach (JsonObject expressionObj in scriptBytecode.OfType<JsonObject>())
                                {
                                    if (!IsExpressionObject(expressionObj) || IsTerminalExpression(expressionObj))
                                        continue;

                                    CollectExternalCallSignaturesFromExpression(
                                        expressionObj,
                                        symbolMap,
                                        relativeAssetPath,
                                        externalCalls);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 个别文件解析失败时跳过，继续扫描其余文件。
                }

                int current = Interlocked.Increment(ref processed);
                if (current % 200 == 0 || current == uassetPaths.Count)
                    progress?.Report($"扫描函数定义（{current}/{uassetPaths.Count}）......");
            });

            mappingsByThread.Dispose();
            foreach (string functionName in signatures.Keys)
            {
                externalCalls.TryRemove(functionName, out _);
            }

            return (
                signatures.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                externalCalls.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                dataTemplates.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
        }

        private static FunctionSignature SelectBetterSignature(FunctionSignature left, FunctionSignature right)
        {
            int leftScore = left.Parameters.Count * 10 + left.ReturnValues.Count;
            int rightScore = right.Parameters.Count * 10 + right.ReturnValues.Count;
            if (rightScore > leftScore) return right;
            if (leftScore > rightScore) return left;

            return string.Compare(right.SourceAssetRelativePath, left.SourceAssetRelativePath, StringComparison.OrdinalIgnoreCase) < 0
                ? right
                : left;
        }

        private static void CollectDataTemplatesFromExport(
            JsonObject exportObj,
            string? sourceAssetRelativePath,
            ConcurrentDictionary<string, DataPropertyTemplate> dataTemplates)
        {
            if (exportObj["Data"] is not JsonArray dataArray || dataArray.Count == 0)
                return;

            foreach (JsonObject dataObj in dataArray.OfType<JsonObject>())
            {
                DataPropertyTemplate? template = CreateDataTemplate(dataObj, sourceAssetRelativePath);
                if (template == null)
                    continue;

                dataTemplates.AddOrUpdate(
                    template.Name,
                    template,
                    (_, existing) =>
                    {
                        MergeDataTemplateInto(existing, template);
                        return existing;
                    });
            }
        }

        private static Dictionary<string, DataPropertyTemplate> CollectDataTemplatesFromAssetRoot(
            JsonObject? root,
            string? sourceAssetRelativePath)
        {
            Dictionary<string, DataPropertyTemplate> result = new(StringComparer.Ordinal);
            if (root?["Exports"] is not JsonArray exports)
                return result;

            foreach (JsonObject exportObj in exports.OfType<JsonObject>())
            {
                if (exportObj["Data"] is not JsonArray dataArray || dataArray.Count == 0)
                    continue;

                foreach (JsonObject dataObj in dataArray.OfType<JsonObject>())
                {
                    DataPropertyTemplate? template = CreateDataTemplate(dataObj, sourceAssetRelativePath);
                    if (template == null)
                        continue;

                    MergeDataTemplate(result, template);
                }
            }

            return result;
        }

        private static DataPropertyTemplate? CreateDataTemplate(JsonObject dataObj, string? sourceAssetRelativePath)
        {
            string name = dataObj["Name"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string rawType = dataObj["$type"]?.GetValue<string>() ?? string.Empty;
            string propertyType = SimplifyPropertyDataType(rawType);
            DataPropertyTemplate template = new()
            {
                Name = name,
                PropertyType = propertyType,
                TemplateJson = dataObj.ToJsonString(),
                OccurrenceCount = 1,
                SourceAssetRelativePath = sourceAssetRelativePath
            };

            if (string.Equals(propertyType, "EnumPropertyData", StringComparison.Ordinal))
            {
                template.EnumType = dataObj["EnumType"]?.GetValue<string>();
                string enumValue = dataObj["Value"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(enumValue))
                    template.EnumOptions.Add(enumValue);
            }
            else if (string.Equals(propertyType, "BoolPropertyData", StringComparison.Ordinal))
            {
                string boolValue = dataObj["Value"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(boolValue))
                    template.EnumOptions.Add(boolValue);
            }

            return template;
        }

        private static string SimplifyPropertyDataType(string rawType)
        {
            if (string.IsNullOrWhiteSpace(rawType))
                return string.Empty;

            string typeName = rawType.Split(',')[0].Trim();
            int lastDot = typeName.LastIndexOf('.');
            return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
        }

        private static void MergeDataTemplate(IDictionary<string, DataPropertyTemplate> target, DataPropertyTemplate incoming)
        {
            if (string.IsNullOrWhiteSpace(incoming.Name))
                return;

            DataPropertyTemplate incomingClone = BlueprintModelCloner.Clone(incoming);
            if (!target.TryGetValue(incomingClone.Name, out DataPropertyTemplate? existing))
            {
                target[incomingClone.Name] = incomingClone;
                return;
            }

            MergeDataTemplateInto(existing, incomingClone);
        }

        private static void MergeDataTemplateInto(DataPropertyTemplate existing, DataPropertyTemplate incoming)
        {
            if (string.IsNullOrWhiteSpace(existing.TemplateJson) && !string.IsNullOrWhiteSpace(incoming.TemplateJson))
                existing.TemplateJson = incoming.TemplateJson;
            if (string.IsNullOrWhiteSpace(existing.PropertyType) && !string.IsNullOrWhiteSpace(incoming.PropertyType))
                existing.PropertyType = incoming.PropertyType;
            if (string.IsNullOrWhiteSpace(existing.EnumType) && !string.IsNullOrWhiteSpace(incoming.EnumType))
                existing.EnumType = incoming.EnumType;
            if (string.IsNullOrWhiteSpace(existing.SourceAssetRelativePath) && !string.IsNullOrWhiteSpace(incoming.SourceAssetRelativePath))
                existing.SourceAssetRelativePath = incoming.SourceAssetRelativePath;

            existing.OccurrenceCount += Math.Max(0, incoming.OccurrenceCount);

            HashSet<string> enumOptions = new(existing.EnumOptions, StringComparer.Ordinal);
            foreach (string option in incoming.EnumOptions)
            {
                if (!string.IsNullOrWhiteSpace(option) && enumOptions.Add(option))
                    existing.EnumOptions.Add(option);
            }
            existing.EnumOptions.Sort(StringComparer.Ordinal);
        }

        private void CollectExternalCallSignaturesFromExpression(
            JsonObject expressionObj,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceAssetRelativePath,
            ConcurrentDictionary<string, ExternalCallSignature> externalCalls)
        {
            CollectExternalCallSignaturesFromExpressionCore(
                expressionObj,
                symbolMap,
                sourceAssetRelativePath,
                externalCalls,
                new HashSet<int>(),
                0);
        }

        private void CollectExternalCallSignaturesFromExpressionCore(
            JsonObject expressionObj,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceAssetRelativePath,
            ConcurrentDictionary<string, ExternalCallSignature> externalCalls,
            HashSet<int> recursionStack,
            int depth)
        {
            if (depth > MaxExpressionDepth)
                return;

            int expressionRefId = RuntimeHelpers.GetHashCode(expressionObj);
            if (!recursionStack.Add(expressionRefId))
                return;

            try
            {
                JsonObject callExpression = expressionObj;
                string callExpressionType = SimplifyExpressionType(callExpression["$type"]?.GetValue<string>() ?? string.Empty);
                bool includeTargetPin = false;

                if (TryGetMergedContextCallData(expressionObj, out JsonObject contextCallExpression, out string contextCallType))
                {
                    callExpression = contextCallExpression;
                    callExpressionType = contextCallType;
                }

                if (IsCallLikeExpression(callExpression) &&
                    TryResolveCallFunctionNameForScan(callExpression, callExpressionType, symbolMap, out string functionName))
                {
                    includeTargetPin = true;
                    int parameterCount = CountExpressionParameters(callExpression["Parameters"] as JsonArray);
                    List<NodeReferenceDescriptor> referenceDescriptors = CollectReferenceDescriptors(
                        callExpression,
                        symbolMap,
                        functionName,
                        sourceAssetRelativePath);

                    externalCalls.AddOrUpdate(
                        functionName,
                        _ => new ExternalCallSignature(
                            functionName,
                            parameterCount,
                            includeTargetPin,
                            callExpression.ToJsonString(),
                            callExpressionType,
                            referenceDescriptors,
                            sourceAssetRelativePath),
                        (_, existing) => new ExternalCallSignature(
                            functionName,
                            Math.Max(existing.ParameterCount, parameterCount),
                            existing.IncludeTargetPin || includeTargetPin,
                            existing.RepresentativeTemplateJson ?? callExpression.ToJsonString(),
                            existing.RepresentativeExpressionType ?? callExpressionType,
                            existing.ReferenceDescriptors.Count > 0 ? existing.ReferenceDescriptors : referenceDescriptors,
                            existing.SourceAssetRelativePath ?? sourceAssetRelativePath));
                }

                foreach (JsonObject child in EnumerateExpressionChildren(expressionObj))
                {
                    CollectExternalCallSignaturesFromExpressionCore(
                        child,
                        symbolMap,
                        sourceAssetRelativePath,
                        externalCalls,
                        recursionStack,
                        depth + 1);
                }
            }
            finally
            {
                recursionStack.Remove(expressionRefId);
            }
        }

        private static int CountExpressionParameters(JsonArray? parametersArray)
        {
            if (parametersArray == null) return 0;
            int count = 0;
            foreach (JsonObject parameter in parametersArray.OfType<JsonObject>())
            {
                if (!IsExpressionObject(parameter) || IsTerminalExpression(parameter))
                    continue;
                count++;
            }
            return count;
        }

        private static IEnumerable<JsonObject> EnumerateExpressionChildren(JsonObject expressionObj)
        {
            foreach ((string key, JsonNode? value) in expressionObj)
            {
                if (key == "$type" || value == null)
                    continue;

                if (value is JsonObject childObject && IsExpressionObject(childObject) && !IsTerminalExpression(childObject))
                {
                    yield return childObject;
                    continue;
                }

                if (value is JsonArray arrayValue)
                {
                    foreach (JsonObject childExpr in arrayValue.OfType<JsonObject>())
                    {
                        if (!IsExpressionObject(childExpr) || IsTerminalExpression(childExpr))
                            continue;
                        yield return childExpr;
                    }
                }
            }
        }

        private static bool TryResolveCallFunctionNameForScan(
            JsonObject expressionObj,
            string expressionType,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            out string functionName)
        {
            functionName = string.Empty;

            if (expressionType is "FinalFunction" or "LocalFinalFunction" or "CallMath")
            {
                if (expressionObj["StackNode"] is JsonValue value && value.TryGetValue<int>(out int objectIndex))
                {
                    if (!symbolMap.TryGetValue(objectIndex, out BlueprintObjectSymbol? resolvedSymbol) ||
                        string.IsNullOrWhiteSpace(resolvedSymbol.ObjectName))
                    {
                        return false;
                    }

                    functionName = resolvedSymbol.ObjectName;
                }
                else
                {
                    return false;
                }
            }
            else if (expressionType == "LocalVirtualFunction")
            {
                functionName = expressionObj["VirtualFunctionName"]?.GetValue<string>() ?? string.Empty;
            }
            else
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(functionName) || functionName.StartsWith("#", StringComparison.Ordinal))
                return false;

            return true;
        }

        /// <summary>
        /// 获取TreeView节点的完整路径，格式如 "父节点\子节点\当前节点"
        /// </summary>
        /// <param name="node">目标树节点</param>
        /// <returns>路径字符串，若节点为null则返回空字符串</returns>
        public static string GetNodePath(TreeNode node)
        {
            if (node == null)
                return string.Empty;

            var pathParts = new List<string>();
            TreeNode current = node;

            // 从当前节点向上遍历到根节点，收集每个节点的文本
            while (current != null)
            {
                string t = current.Text.EndsWith('/') ? current.Text.Substring(0, current.Text.Length - 2) : current.Text;
                pathParts.Add(t);
                current = current.Parent;
            }

            // 反转列表，使路径从根节点到当前节点
            pathParts.Reverse();
            return string.Join("/", pathParts);
        }

        private async void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Nodes.Count == 0)
            {
                await OpenAssetByTreePathAsync(GetNodePath(e.Node), false);
                当前文件ToolStripMenuItem.Enabled = true;
            }
        }

        private async Task OpenAssetByTreePathAsync(string treePath, bool IsWorkload)
        {
            string pakPath = treePath;
            if (!string.IsNullOrWhiteSpace(currentPakMountPointDisplay))
            {
                string prefix = NormalizePath(currentPakMountPointDisplay) + "/";
                string normalizedTreePath = NormalizePath(pakPath);
                if (normalizedTreePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    pakPath = normalizedTreePath[prefix.Length..];
                else
                    pakPath = normalizedTreePath;
            }
            else
            {
                pakPath = NormalizePath(pakPath);
            }

            if (pakPath.StartsWith('/')) pakPath = pakPath[1..];
            label1.Text = $"读取 {pakPath} ......";

            try
            {
                Enabled = false;
                string assetPath = Path.Combine(WorkDir, IsWorkload ? "Workload" : "Source", pakPath.Replace('/', Path.DirectorySeparatorChar) + (IsWorkload ? ".json" : ""));

                LoadedAssetEditorData result = await Task.Run(() =>
                {
                    UAsset uAsset = LoadUAssetForEditor(assetPath, pakPath);
                    string assetJson = uAsset.SerializeJson();
                    JsonObject? root = JsonNode.Parse(assetJson) as JsonObject;
                    BlueprintAssetContext assetContext = BuildBlueprintAssetContext(uAsset, assetJson, root);
                    NonBlueprintExportEditorState nonBlueprintState = NonBlueprintExportEditorState.FromAssetJson(assetJson);
                    Dictionary<string, DataPropertyTemplate> localDataTemplates = CollectDataTemplatesFromAssetRoot(root, assetContext.SourceAssetRelativePath);

                    try
                    {
                        (BlueprintData blueprintData, BlueprintAssetContext parsedContext) = ConvertUAssetToBlueprintData(uAsset, assetJson, root, assetContext);
                        return new LoadedAssetEditorData
                        {
                            AssetJson = assetJson,
                            BlueprintData = blueprintData,
                            AssetContext = parsedContext,
                            NonBlueprintExportState = nonBlueprintState,
                            DataTemplates = localDataTemplates
                        };
                    }
                    catch (Exception ex)
                    {
                        return new LoadedAssetEditorData
                        {
                            AssetJson = assetJson,
                            AssetContext = assetContext,
                            NonBlueprintExportState = nonBlueprintState,
                            DataTemplates = localDataTemplates,
                            BlueprintParseException = ex
                        };
                    }
                });

                foreach (DataPropertyTemplate dataTemplate in result.DataTemplates.Values)
                    MergeDataTemplate(dataTemplatesByName, dataTemplate);
                MarkDataLibraryTemplatesChanged();
                UpdateDataLibrary(textBox2.Text.Trim());
                UpdateNodeLibrary(textBox1.Text.Trim());
                (TabPage filePage, BlueprintCanvas? canvas, bool existed) = GetOrCreateTabPageForPath(treePath, result, IsWorkload);
                if (!existed && canvas != null && result.BlueprintData != null)
                    canvas.ImportData(result.BlueprintData, nodeDefinitions);
                else
                {
                    try
                    {
                        tabControl1.SelectedTab = filePage;
                    }
                    catch { }
                }

                if (result.BlueprintParseException != null)
                {
                    label1.Text = "蓝图解析失败，已加载非蓝图导出";
                    MessageBox.Show($"解析蓝图失败，非蓝图导出仍可编辑：{result.BlueprintParseException}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    label1.Text = "读取完成";
                }
                tabControl1_SelectedIndexChanged(tabControl1, new EventArgs());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取文件失败：{ex}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                label1.Text = "读取失败";
            }
            finally
            {
                Enabled = true;
            }
        }

        // 辅助方法：根据文件路径获取或创建文件标签页。
        // 文件标签页内部使用 TabControl 承载各类 Export 编辑页。
        private (TabPage FilePage, BlueprintCanvas? Canvas, bool Existed) GetOrCreateTabPageForPath(string path, LoadedAssetEditorData editorData, bool IsWorkload)
        {
            foreach (TabPage page in tabControl1.TabPages)
            {
                if (page.Tag is OpenAssetTabInfo info &&
                    string.Equals(info.Path, path, StringComparison.Ordinal) &&
                    info.IsWorkload == IsWorkload)
                {
                    if (info.NonBlueprintExportState == null && editorData.NonBlueprintExportState.HasExports)
                        AddNonBlueprintExportPage(page, editorData.NonBlueprintExportState);
                    return (page, info.BlueprintCanvas, true);
                }
            }

            TabPage newPage = new() { Text = IsWorkload ? "[工作区] " + Path.GetFileName(path) : Path.GetFileName(path), ToolTipText = path };
            TabControl exportTabControl = new()
            {
                Dock = DockStyle.Fill,
                Name = "assetExportTabControl"
            };
            exportTabControl.SelectedIndexChanged += ExportTabControl_SelectedIndexChanged;
            BlueprintCanvas? canvas = null;
            if (editorData.BlueprintData != null && editorData.BlueprintParseException == null)
            {
                TabPage blueprintPage = new()
                {
                    Text = "蓝图",
                    Name = "blueprintTabPage"
                };
                canvas = new(editorData.AssetContext) { Dock = DockStyle.Fill };
                canvas.OffsetCalculator = blueprintData => CalculateStatementOffsetsForCanvas(blueprintData, canvas.AssetContext);
                canvas.StatusMessage += C_StatusMessage;
                canvas.BlueprintEdited += Canvas_BlueprintEdited;
                blueprintPage.Controls.Add(canvas);
                exportTabControl.TabPages.Add(blueprintPage);
            }
            newPage.Controls.Add(exportTabControl);
            newPage.Tag = new OpenAssetTabInfo
            {
                Path = path,
                IsWorkload = IsWorkload,
                BlueprintCanvas = canvas,
                NonBlueprintExportState = editorData.NonBlueprintExportState,
                AssetContext = editorData.AssetContext
            };
            AddNonBlueprintExportPage(newPage, editorData.NonBlueprintExportState);
            tabControl1.TabPages.Add(newPage);
            tabControl1.SelectedTab = newPage;
            return (newPage, canvas, false);
        }

        private void ExportTabControl_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender is not null)
            {
                int selIndex = ((TabControl)sender).SelectedIndex;
                if (selIndex == 0)
                {
                    tabPage3.Parent = tabControl3;
                    tabPage4.Parent = null;
                }
                else if (selIndex == 1)
                {
                    tabPage3.Parent = null;
                    tabPage4.Parent = tabControl3;
                }
                else
                {
                    tabPage3.Parent = null;
                    tabPage4.Parent = null;
                }
            }
        }

        private UAsset LoadUAssetForEditor(string assetPath, string pakPath)
        {
            if (assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return UAsset.DeserializeJson(File.ReadAllText(assetPath));

            if (!File.Exists(assetPath))
            {
                if (currentPakReader == null || currentPakFileStream == null)
                    throw new FileNotFoundException($"文件 {pakPath} 未找到！");

                byte[] bytes = currentPakReader.Get(currentPakFileStream, pakPath);
                string? dir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(assetPath, bytes);

                bytes = currentPakReader.Get(currentPakFileStream, pakPath[..^5] + "exp");
                File.WriteAllBytes(assetPath[..^5] + "exp", bytes);
            }

            return new UAsset(assetPath, engineVersion, new(UsmapPath));
        }

        private void AddNonBlueprintExportPage(TabPage filePage, NonBlueprintExportEditorState state)
        {
            TabControl? exportTabControl = filePage.Controls.OfType<TabControl>().FirstOrDefault();
            if (exportTabControl == null ||
                exportTabControl.TabPages.Cast<TabPage>().Any(page => string.Equals(page.Name, "nonBlueprintExportsTabPage", StringComparison.Ordinal)))
            {
                return;
            }

            state.Edited += NonBlueprintExportState_Edited;
            TabPage nonBlueprintPage = new()
            {
                Text = "非蓝图导出",
                Name = "nonBlueprintExportsTabPage"
            };
            nonBlueprintPage.Controls.Add(new NonBlueprintExportsControl(
                state,
                GetSelectedDataTemplate,
                ResolveDataTemplateByName));
            exportTabControl.TabPages.Add(nonBlueprintPage);
        }

        private DataPropertyTemplate? GetSelectedDataTemplate()
        {
            if (dataLibrary.CurrentRow?.DataBoundItem is DataPropertyTemplate template)
                return BlueprintModelCloner.Clone(template);

            if (dataLibrary.SelectedRows.Count > 0 &&
                dataLibrary.SelectedRows[0].DataBoundItem is DataPropertyTemplate selectedTemplate)
            {
                return BlueprintModelCloner.Clone(selectedTemplate);
            }

            return null;
        }

        private DataPropertyTemplate? ResolveDataTemplateByName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                dataTemplatesByName.TryGetValue(name, out DataPropertyTemplate? template)
                    ? template
                    : null;
        }

        private void NonBlueprintExportState_Edited(object? sender, EventArgs e)
        {
            if (sender is not NonBlueprintExportEditorState state)
                return;

            foreach (TabPage page in tabControl1.TabPages)
            {
                if (page.Tag is OpenAssetTabInfo info && ReferenceEquals(info.NonBlueprintExportState, state))
                {
                    MarkFileTabDirty(page);
                    AddToWorkload(page.ToolTipText);
                    return;
                }
            }
        }

        private void Canvas_BlueprintEdited(object obj)
        {
            if (obj is not BlueprintCanvas canvas)
                return;

            TabPage? filePage = GetFileTabPageForCanvas(canvas);
            if (filePage == null)
                return;

            MarkFileTabDirty(filePage);
            AddToWorkload(filePage.ToolTipText);
        }

        private BlueprintCanvas? GetSelectedBlueprintCanvas()
        {
            return tabControl1.SelectedTab != null
                ? GetBlueprintCanvasFromFilePage(tabControl1.SelectedTab)
                : null;
        }

        private string BuildSelectedEditedAssetJson()
        {
            return tabControl1.SelectedTab != null
                ? BuildEditedAssetJsonFromFilePage(tabControl1.SelectedTab)
                : throw new InvalidOperationException("当前没有打开的文件。");
        }

        private string BuildEditedAssetJsonFromFilePage(TabPage filePage)
        {
            CommitPendingGridEdits(filePage);

            if (filePage.Tag is OpenAssetTabInfo info)
            {
                string json;
                if (info.BlueprintCanvas != null)
                {
                    json = ConvertBlueprintDataToUAsset(info.BlueprintCanvas.ExportData(), info.BlueprintCanvas.AssetContext);
                    info.AssetContext = info.BlueprintCanvas.AssetContext;
                }
                else
                {
                    json = !string.IsNullOrWhiteSpace(info.AssetContext.OriginalAssetJson)
                        ? info.AssetContext.OriginalAssetJson
                        : info.NonBlueprintExportState?.OriginalAssetJson ?? string.Empty;
                }

                if (info.NonBlueprintExportState != null)
                    json = info.NonBlueprintExportState.ApplyToAssetJson(json, dataTemplatesByName);

                if (!string.IsNullOrWhiteSpace(json))
                    info.AssetContext.OriginalAssetJson = json;

                return json;
            }

            BlueprintCanvas? canvas = GetBlueprintCanvasFromFilePage(filePage);
            if (canvas != null)
                return ConvertBlueprintDataToUAsset(canvas.ExportData(), canvas.AssetContext);

            throw new InvalidOperationException("标签页没有可导出的编辑内容。");
        }

        private static void CommitPendingGridEdits(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is DataGridView grid)
                {
                    grid.EndEdit();
                    if (grid.DataSource is BindingSource bindingSource)
                        bindingSource.EndEdit();
                }

                CommitPendingGridEdits(child);
            }
        }

        private static BlueprintCanvas? GetBlueprintCanvasFromFilePage(TabPage filePage)
        {
            if (filePage.Tag is OpenAssetTabInfo info)
                return info.BlueprintCanvas;

            TabControl? exportTabControl = filePage.Controls.OfType<TabControl>().FirstOrDefault();
            TabPage? blueprintPage = exportTabControl?.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(page => string.Equals(page.Name, "blueprintTabPage", StringComparison.Ordinal) ||
                    string.Equals(page.Text, "蓝图", StringComparison.Ordinal));
            return blueprintPage?.Controls.OfType<BlueprintCanvas>().FirstOrDefault();
        }

        private TabPage? GetFileTabPageForCanvas(BlueprintCanvas canvas)
        {
            foreach (TabPage page in tabControl1.TabPages)
            {
                if (page.Tag is OpenAssetTabInfo info && ReferenceEquals(info.BlueprintCanvas, canvas))
                    return page;
            }

            Control? current = canvas.Parent;
            while (current != null)
            {
                if (current is TabPage tabPage && tabControl1.TabPages.Contains(tabPage))
                    return tabPage;
                current = current.Parent;
            }

            return null;
        }

        private static void MarkFileTabDirty(TabPage filePage)
        {
            string cleanText = filePage.Text.EndsWith(" *", StringComparison.Ordinal)
                ? filePage.Text[..^2]
                : filePage.Text;
            if (!cleanText.StartsWith("[工作区] ", StringComparison.Ordinal))
                cleanText = "[工作区] " + cleanText;
            filePage.Text = cleanText + " *";

            if (filePage.Tag is OpenAssetTabInfo info)
                info.IsWorkload = true;
        }

        private static void MarkFileTabClean(TabPage filePage)
        {
            if (filePage.Text.EndsWith(" *", StringComparison.Ordinal))
                filePage.Text = filePage.Text[..^2];
        }

        private void AddToWorkload(string file)
        {
            try
            {
                TreeViewHelper.AddPathToNodeCollection(treeView2.Nodes, file);
                treeView2.ExpandAll();
                整个工作区pakToolStripMenuItem.Enabled = true;
            }
            catch { }
        }

        private void C_StatusMessage(string obj)
        {
            label1.Text = obj;
        }

        // 搜索按钮点击事件：根据 textBox1 内容过滤节点库
        private void button1_Click(object sender, EventArgs e)
        {
            string filter = textBox1.Text.Trim();
            UpdateNodeLibrary(filter);

            // 可选：如果过滤后没有结果，可给出提示
            if (nodeLibrary.Items.Count == 0)
            {
                label1.Text = $"未找到包含 \"{filter}\" 的节点定义";
            }
            else
            {
                label1.Text = $"显示 {nodeLibrary.Items.Count} 个节点定义 (筛选: \"{filter}\")";
            }
        }

        private (BlueprintData, BlueprintAssetContext) ConvertUAssetToBlueprintData(UAsset uAsset)
        {
            string json = uAsset.SerializeJson();
            JsonObject? root = JsonNode.Parse(json) as JsonObject;
            BlueprintAssetContext assetContext = BuildBlueprintAssetContext(uAsset, json, root);
            return ConvertUAssetToBlueprintData(uAsset, json, root, assetContext);
        }

        private (BlueprintData, BlueprintAssetContext) ConvertUAssetToBlueprintData(
            UAsset uAsset,
            string json,
            JsonObject? root,
            BlueprintAssetContext assetContext)
        {
            BlueprintData blueprintData = new();
            JsonArray? exports = root?["Exports"] as JsonArray;
            if (exports == null) return (blueprintData, assetContext);
            currentImportAssetContext = assetContext;
            try
            {
                bytecodeObjectNameMap = BuildBytecodeObjectNameMap(root);
                AnnotateTopLevelStatementIndices(uAsset, exports);

                int functionBaseY = 60;
                foreach (JsonObject exportObj in exports.OfType<JsonObject>())
                {
                    string exportType = exportObj["$type"]?.GetValue<string>() ?? string.Empty;
                    JsonArray? scriptBytecode = exportObj["ScriptBytecode"] as JsonArray;
                    if (!exportType.Contains("FunctionExport") || scriptBytecode == null || scriptBytecode.Count == 0)
                        continue;

                    string functionName = exportObj["ObjectName"]?.GetValue<string>() ?? "UnnamedFunction";
                    (List<string> parameterNames, List<string> returnValues) = GetFunctionSignatureParts(exportObj);
                    RegisterFunctionDefinitions(functionName, parameterNames, returnValues);
                    int localNodeIndex = 0;

                    string entryDefinitionName = EnsureNodeDefinition(
                        $"Event {functionName}",
                        [],
                        new[] { new Pin("Exec", PinDirection.Output, PinType.Exec) }
                            .Concat(parameterNames.Select(name => new Pin(name, PinDirection.Output, PinType.Data)))
                            .ToList());

                    SerializableNode entryNode = new()
                    {
                        Id = Guid.NewGuid(),
                        DefinitionName = entryDefinitionName,
                        Location = new Point(60, functionBaseY)
                    };
                    entryNode.MetaData["NodeKind"] = "FunctionEntry";
                    entryNode.MetaData["FunctionName"] = functionName;
                    entryNode.MetaData["ParameterNamesJson"] = JsonSerializer.Serialize(parameterNames);
                    blueprintData.Nodes.Add(entryNode);

                    Guid previousExecNodeId = entryNode.Id;
                    string previousExecPinName = "Exec";
                    List<(SerializableNode Node, JsonObject Expression)> functionTopLevelNodes = [];

                    int topLevelOrder = 0;
                    foreach (JsonObject expressionObj in scriptBytecode.OfType<JsonObject>())
                    {
                        if (IsTerminalExpression(expressionObj)) continue;

                        BuiltExpression built = BuildExpressionGraph(
                            expressionObj,
                            blueprintData,
                            functionName,
                            functionBaseY,
                            ref localNodeIndex,
                            0,
                            topLevelOrder,
                            new HashSet<int>());

                        topLevelOrder++;

                        SerializableNode? builtNode = blueprintData.Nodes.LastOrDefault(node => node.Id == built.NodeId);
                        if (builtNode != null)
                            functionTopLevelNodes.Add((builtNode, expressionObj));

                        if (built.HasExecInput)
                        {
                            blueprintData.Connections.Add(new ConnectionData
                            {
                                FromNodeId = previousExecNodeId,
                                FromPinName = previousExecPinName,
                                ToNodeId = built.NodeId,
                                ToPinName = "In"
                            });
                        }

                        if (built.HasExecOutput)
                        {
                            previousExecNodeId = built.NodeId;
                            previousExecPinName = "Out";
                        }
                    }

                    RestoreOffsetDrivenConnectionsForImportedFunction(functionTopLevelNodes, blueprintData);
                    functionBaseY += Math.Max(260, (localNodeIndex + 2) * 110);
                }
            }
            finally
            {
                currentImportAssetContext = null;
            }

            return (blueprintData, assetContext);
        }

        private string ConvertBlueprintDataToUAsset(BlueprintData blueprintData, string originalJson, bool includeStatementIndex = false)
        {
            return ConvertBlueprintDataToUAsset(
                blueprintData,
                BlueprintAssetContext.CreateFallback(originalJson),
                includeStatementIndex);
        }

        private void RestoreOffsetDrivenConnectionsForImportedFunction(
            IReadOnlyList<(SerializableNode Node, JsonObject Expression)> topLevelNodes,
            BlueprintData blueprintData)
        {
            if (topLevelNodes.Count == 0)
                return;

            Dictionary<int, SerializableNode> topLevelNodeByStatementIndex = new();
            foreach ((SerializableNode node, _) in topLevelNodes)
            {
                if (!node.MetaData.TryGetValue("StatementIndex", out string? statementIndexText) ||
                    !int.TryParse(statementIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int statementIndex))
                {
                    continue;
                }

                topLevelNodeByStatementIndex[statementIndex] = node;
            }

            foreach ((SerializableNode node, JsonObject expressionObj) in topLevelNodes)
            {
                string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
                switch (expressionType)
                {
                    case "Jump":
                    case "JumpIfNot":
                        if (TryGetIntJsonValue(expressionObj["CodeOffset"], out int codeOffset) &&
                            topLevelNodeByStatementIndex.TryGetValue(codeOffset, out SerializableNode? targetNode))
                        {
                            AddImportedConnectionIfMissing(blueprintData, node.Id, "To", targetNode.Id, "In");
                        }
                        else if (TryGetIntJsonValue(expressionObj["CodeOffset"], out codeOffset))
                        {
                            node.MetaData["OriginalCodeOffset"] = codeOffset.ToString(CultureInfo.InvariantCulture);
                        }
                        break;
                    case "PushExecutionFlow":
                        if (TryGetIntJsonValue(expressionObj["PushingAddress"], out int pushingAddress) &&
                            topLevelNodeByStatementIndex.TryGetValue(pushingAddress, out SerializableNode? pushTargetNode))
                        {
                            AddImportedConnectionIfMissing(blueprintData, node.Id, "To", pushTargetNode.Id, "In");
                        }
                        else if (TryGetIntJsonValue(expressionObj["PushingAddress"], out pushingAddress))
                        {
                            node.MetaData["OriginalPushingAddress"] = pushingAddress.ToString(CultureInfo.InvariantCulture);
                        }
                        break;
                }
            }
        }

        private static void AddImportedConnectionIfMissing(
            BlueprintData blueprintData,
            Guid fromNodeId,
            string fromPinName,
            Guid toNodeId,
            string toPinName)
        {
            if (blueprintData.Connections.Any(connection =>
                    connection.FromNodeId == fromNodeId &&
                    string.Equals(connection.FromPinName, fromPinName, StringComparison.Ordinal) &&
                    connection.ToNodeId == toNodeId &&
                    string.Equals(connection.ToPinName, toPinName, StringComparison.Ordinal)))
            {
                return;
            }

            blueprintData.Connections.Add(new ConnectionData
            {
                FromNodeId = fromNodeId,
                FromPinName = fromPinName,
                ToNodeId = toNodeId,
                ToPinName = toPinName
            });
        }

        private static bool TryGetIntJsonValue(JsonNode? node, out int value)
        {
            value = 0;
            return node is JsonValue jsonValue &&
                jsonValue.TryGetValue<int>(out value);
        }

        private static void PruneInvalidBlueprintData(BlueprintData blueprintData)
        {
            HashSet<Guid> nodeIds = new(blueprintData.Nodes.Select(n => n.Id));
            Dictionary<string, ConnectionData> bestByExactKey = new(StringComparer.Ordinal);
            Dictionary<string, ConnectionData> bestByIncomingKey = new(StringComparer.Ordinal);

            foreach (ConnectionData conn in blueprintData.Connections)
            {
                if (!nodeIds.Contains(conn.FromNodeId) || !nodeIds.Contains(conn.ToNodeId))
                    continue;
                if (string.IsNullOrWhiteSpace(conn.FromPinName) || string.IsNullOrWhiteSpace(conn.ToPinName))
                    continue;

                string key = $"{conn.FromNodeId}|{conn.FromPinName}|{conn.ToNodeId}|{conn.ToPinName}";
                if (!TryKeepHigherSequence(bestByExactKey, key, conn))
                    continue;

                if (!AllowsMultipleIncomingConnections(conn.ToPinName))
                {
                    string incomingKey = $"{conn.ToNodeId}|{conn.ToPinName}";
                    _ = TryKeepHigherSequence(bestByIncomingKey, incomingKey, conn);
                }
            }

            HashSet<ConnectionData> validSet = new(bestByIncomingKey.Values.Concat(
                bestByExactKey.Values.Where(conn => AllowsMultipleIncomingConnections(conn.ToPinName))));
            List<ConnectionData> valid = blueprintData.Connections
                .Where(validSet.Contains)
                .OrderBy(conn => conn.Sequence)
                .ThenBy(conn => conn.ToNodeId)
                .ThenBy(conn => conn.ToPinName, StringComparer.Ordinal)
                .ThenBy(conn => conn.FromNodeId)
                .ThenBy(conn => conn.FromPinName, StringComparer.Ordinal)
                .ToList();

            if (valid.Count != blueprintData.Connections.Count)
                blueprintData.Connections = valid;
        }

        private static bool AllowsMultipleIncomingConnections(string toPinName)
        {
            return string.Equals(toPinName, "In", StringComparison.Ordinal);
        }

        private static bool TryKeepHigherSequence(Dictionary<string, ConnectionData> bestByKey, string key, ConnectionData candidate)
        {
            if (!bestByKey.TryGetValue(key, out ConnectionData? existing) || candidate.Sequence >= existing.Sequence)
            {
                bestByKey[key] = candidate;
                return true;
            }

            return false;
        }

        private static void RemoveFieldRecursively(JsonNode? node, string fieldName)
        {
            if (node is JsonObject obj)
            {
                obj.Remove(fieldName);
                foreach ((string _, JsonNode? child) in obj)
                {
                    RemoveFieldRecursively(child, fieldName);
                }
                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    RemoveFieldRecursively(child, fieldName);
                }
            }
        }

        private Dictionary<string, List<SerializableNode>> CollectReachableTopLevelNodesByFunction(
            BlueprintData blueprintData,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById)
        {
            Dictionary<Guid, List<Guid>> execAdjacency = BuildExecAdjacency(blueprintData, nodeById);
            Dictionary<string, List<SerializableNode>> result = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<Guid>> visitedByFunction = new(StringComparer.Ordinal);

            List<SerializableNode> functionEntries = blueprintData.Nodes
                .Where(node => TryResolveEntryFunctionName(node, out _))
                .OrderBy(node => node.Location.Y)
                .ThenBy(node => node.Location.X)
                .ToList();

            HashSet<string> activeFunctionNames = new(StringComparer.Ordinal);
            foreach (SerializableNode entry in functionEntries)
            {
                if (TryResolveEntryFunctionName(entry, out string activeFunctionName) &&
                    !string.IsNullOrWhiteSpace(activeFunctionName))
                {
                    activeFunctionNames.Add(activeFunctionName);
                }
            }

            foreach (SerializableNode entry in functionEntries)
            {
                _ = TryResolveEntryFunctionName(entry, out string functionName);
                if (!result.TryGetValue(functionName, out List<SerializableNode>? topLevelNodes))
                {
                    topLevelNodes = [];
                    result[functionName] = topLevelNodes;
                }

                if (!visitedByFunction.TryGetValue(functionName, out HashSet<Guid>? visited))
                {
                    visited = [];
                    visitedByFunction[functionName] = visited;
                }

                if (!execAdjacency.TryGetValue(entry.Id, out List<Guid>? firstNodes))
                    continue;

                foreach (Guid nextNodeId in firstNodes)
                {
                    TraverseExecReachableNodes(
                        nextNodeId,
                        functionName,
                        nodeById,
                        execAdjacency,
                        topLevelNodes,
                        visited);
                }
            }

            // Preserve imported top-level pure data expressions that do not participate in exec flow.
            // Exec-capable nodes are exported strictly according to the current exec graph so deleted
            // or re-routed links do not keep stale TopLevelOrder nodes alive.
            IEnumerable<SerializableNode> metadataTopLevelNodes = blueprintData.Nodes
                .Where(node =>
                    IsExpressionLikeNode(node) &&
                    !NodeHasExecPins(node) &&
                    node.MetaData.TryGetValue("FunctionName", out string? fn) &&
                    !string.IsNullOrWhiteSpace(fn) &&
                    activeFunctionNames.Contains(fn) &&
                    node.MetaData.ContainsKey("TopLevelOrder"))
                .OrderBy(node =>
                {
                    if (node.MetaData.TryGetValue("FunctionName", out string? fn) && !string.IsNullOrWhiteSpace(fn))
                        return fn;
                    return string.Empty;
                }, StringComparer.Ordinal)
                .ThenBy(node =>
                {
                    if (node.MetaData.TryGetValue("TopLevelOrder", out string? orderText) &&
                        int.TryParse(orderText, out int order))
                    {
                        return order;
                    }
                    return int.MaxValue;
                })
                .ThenBy(node => node.Location.Y)
                .ThenBy(node => node.Location.X);

            foreach (SerializableNode node in metadataTopLevelNodes)
            {
                string functionName = node.MetaData["FunctionName"];
                if (!result.TryGetValue(functionName, out List<SerializableNode>? topLevelNodes))
                {
                    topLevelNodes = [];
                    result[functionName] = topLevelNodes;
                }

                if (!visitedByFunction.TryGetValue(functionName, out HashSet<Guid>? visited))
                {
                    visited = [];
                    visitedByFunction[functionName] = visited;
                }

                if (visited.Add(node.Id))
                    topLevelNodes.Add(node);
            }

            return result;
        }

        private static bool TryResolveEntryFunctionName(SerializableNode node, out string functionName)
        {
            functionName = string.Empty;
            if (node.MetaData.TryGetValue("NodeKind", out string? kind) &&
                string.Equals(kind, "FunctionEntry", StringComparison.Ordinal) &&
                node.MetaData.TryGetValue("FunctionName", out string? metaFunctionName) &&
                !string.IsNullOrWhiteSpace(metaFunctionName))
            {
                functionName = metaFunctionName;
                return true;
            }

            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);
            if (baseDefinitionName.StartsWith("Event ", StringComparison.Ordinal))
            {
                string candidate = baseDefinitionName.Substring(6).Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    functionName = candidate;
                    return true;
                }
            }

            if (string.Equals(baseDefinitionName, "Event", StringComparison.Ordinal))
            {
                string candidate = ResolveNodeEditorText(node);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    functionName = candidate;
                    return true;
                }
            }

            return false;
        }

        private Dictionary<Guid, List<Guid>> BuildExecAdjacency(
            BlueprintData blueprintData,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById)
        {
            Dictionary<Guid, List<Guid>> adjacency = [];
            foreach (ConnectionData connection in blueprintData.Connections)
            {
                if (!nodeById.TryGetValue(connection.FromNodeId, out SerializableNode? fromNode) ||
                    !nodeById.TryGetValue(connection.ToNodeId, out SerializableNode? toNode))
                {
                    continue;
                }

                if (!IsSequenceExecConnection(connection, fromNode, toNode))
                    continue;

                if (!adjacency.TryGetValue(connection.FromNodeId, out List<Guid>? nextNodes))
                {
                    nextNodes = [];
                    adjacency[connection.FromNodeId] = nextNodes;
                }

                if (!nextNodes.Contains(connection.ToNodeId))
                    nextNodes.Add(connection.ToNodeId);
            }

            return adjacency;
        }

        private void TraverseExecReachableNodes(
            Guid nodeId,
            string functionName,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById,
            IReadOnlyDictionary<Guid, List<Guid>> execAdjacency,
            IList<SerializableNode> orderedNodes,
            ISet<Guid> visited)
        {
            if (!visited.Add(nodeId))
                return;
            if (!nodeById.TryGetValue(nodeId, out SerializableNode? node))
                return;
            if (!IsExpressionLikeNode(node))
                return;

            orderedNodes.Add(node);
            if (!execAdjacency.TryGetValue(nodeId, out List<Guid>? nextNodes))
                return;

            foreach (Guid nextNodeId in nextNodes)
            {
                TraverseExecReachableNodes(
                    nextNodeId,
                    functionName,
                    nodeById,
                    execAdjacency,
                    orderedNodes,
                    visited);
            }
        }

        private bool IsExpressionLikeNode(SerializableNode node)
        {
            if (node.MetaData.TryGetValue("NodeKind", out string? kind) &&
                string.Equals(kind, "Expression", StringComparison.Ordinal))
            {
                return true;
            }

            return nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? def) &&
                (!string.IsNullOrWhiteSpace(def.SourceExpressionTemplateJson) ||
                 !string.IsNullOrWhiteSpace(def.SourceExpressionType) ||
                 CanBuildFallbackExpressionFromDefinitionName(node.DefinitionName));
        }

        private bool IsSequenceExecConnection(ConnectionData connection, SerializableNode fromNode, SerializableNode toNode)
        {
            if (TryGetPinType(fromNode.DefinitionName, connection.FromPinName, PinDirection.Output, out PinType fromPinType) &&
                TryGetPinType(toNode.DefinitionName, connection.ToPinName, PinDirection.Input, out PinType toPinType))
            {
                return fromPinType == PinType.Exec &&
                    toPinType == PinType.Exec &&
                    IsSequenceExecOutputPin(connection.FromPinName) &&
                    IsSequenceExecInputPin(connection.ToPinName);
            }

            bool fromEntryExec =
                fromNode.MetaData.TryGetValue("NodeKind", out string? fromKind) &&
                fromKind == "FunctionEntry" &&
                string.Equals(connection.FromPinName, "Exec", StringComparison.Ordinal) &&
                string.Equals(connection.ToPinName, "In", StringComparison.Ordinal);
            if (fromEntryExec)
                return true;

            bool fromExecLikeOutput =
                IsSequenceExecInputPin(connection.ToPinName) &&
                IsSequenceExecOutputPin(connection.FromPinName);
            return fromExecLikeOutput;
        }

        private static bool IsSequenceExecOutputPin(string pinName)
        {
            return string.Equals(pinName, "Out", StringComparison.Ordinal) ||
                string.Equals(pinName, "Then", StringComparison.Ordinal) ||
                string.Equals(pinName, "True", StringComparison.Ordinal) ||
                string.Equals(pinName, "False", StringComparison.Ordinal) ||
                string.Equals(pinName, "Exec", StringComparison.Ordinal);
        }

        private static bool IsSequenceExecInputPin(string pinName)
        {
            return string.Equals(pinName, "In", StringComparison.Ordinal);
        }

        private bool TryGetPinType(string definitionName, string pinName, PinDirection direction, out PinType pinType)
        {
            pinType = default;
            if (!nodeDefinitions.TryGetValue(definitionName, out NodeDefinition? definition))
                return false;

            IEnumerable<Pin> pins = direction == PinDirection.Input
                ? definition.InputPins
                : definition.OutputPins;
            Pin? pin = pins.FirstOrDefault(item => string.Equals(item.Name, pinName, StringComparison.Ordinal));
            if (pin == null)
                return false;

            pinType = pin.Type;
            return true;
        }

        private bool NodeHasExecPins(SerializableNode node)
        {
            if (nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
            {
                return definition.InputPins.Any(pin => pin.Type == PinType.Exec) ||
                    definition.OutputPins.Any(pin => pin.Type == PinType.Exec);
            }

            if (node.MetaData.TryGetValue("DisplayExpressionType", out string? displayType) &&
                !string.IsNullOrWhiteSpace(displayType))
            {
                return ExpressionHasExecInput(displayType) || ExpressionHasExecOutput(displayType);
            }

            if (node.MetaData.TryGetValue("SourceExpressionType", out string? sourceType) &&
                !string.IsNullOrWhiteSpace(sourceType))
            {
                return ExpressionHasExecInput(sourceType) || ExpressionHasExecOutput(sourceType);
            }

            return false;
        }

        private Dictionary<Guid, int> CalculateStatementOffsetsForCanvas(BlueprintData blueprintData, BlueprintAssetContext assetContext)
        {
            string rebuiltJson = ConvertBlueprintDataToUAsset(blueprintData, assetContext, includeStatementIndex: true);
            JsonObject? root = JsonNode.Parse(rebuiltJson) as JsonObject;
            JsonArray? exports = root?["Exports"] as JsonArray;
            if (root == null || exports == null)
                return [];

            Dictionary<Guid, SerializableNode> nodeById = blueprintData.Nodes.ToDictionary(node => node.Id);
            Dictionary<(Guid NodeId, string PinName), Guid> incomingConnections = blueprintData.Connections
                .GroupBy(connection => (connection.ToNodeId, connection.ToPinName))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(connection => connection.Sequence)
                        .ThenByDescending(connection => connection.FromNodeId)
                        .First()
                        .FromNodeId);

            Dictionary<string, List<SerializableNode>> topLevelNodesByFunction =
                CollectReachableTopLevelNodesByFunction(blueprintData, nodeById);

            Dictionary<Guid, int> offsets = [];
            foreach (JsonObject exportObj in exports.OfType<JsonObject>())
            {
                string exportType = exportObj["$type"]?.GetValue<string>() ?? string.Empty;
                if (!exportType.Contains("FunctionExport", StringComparison.Ordinal))
                    continue;

                string functionName = exportObj["ObjectName"]?.GetValue<string>() ?? string.Empty;
                if (!topLevelNodesByFunction.TryGetValue(functionName, out List<SerializableNode>? topLevelNodes))
                    continue;

                JsonArray? scriptBytecode = exportObj["ScriptBytecode"] as JsonArray;
                if (scriptBytecode == null)
                    continue;

                int count = Math.Min(topLevelNodes.Count, scriptBytecode.Count);
                for (int i = 0; i < count; i++)
                {
                    if (scriptBytecode[i] is not JsonObject expressionObj || IsTerminalExpression(expressionObj))
                        continue;

                    MapStatementOffsetsRecursive(
                        topLevelNodes[i],
                        expressionObj,
                        nodeById,
                        incomingConnections,
                        offsets,
                        new HashSet<Guid>());
                }
            }

            return offsets;
        }

        private void MapStatementOffsetsRecursive(
            SerializableNode node,
            JsonObject expressionObj,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById,
            IReadOnlyDictionary<(Guid NodeId, string PinName), Guid> incomingConnections,
            IDictionary<Guid, int> offsets,
            ISet<Guid> recursionStack)
        {
            if (!recursionStack.Add(node.Id))
                return;

            try
            {
                if (expressionObj["StatementIndex"] is JsonValue indexValue &&
                    indexValue.TryGetValue<int>(out int statementIndex))
                {
                    offsets[node.Id] = statementIndex;
                }

                foreach ((JsonObject childExpression, string pinName) in CollectDisplayChildExpressions(expressionObj))
                {
                    if (!incomingConnections.TryGetValue((node.Id, pinName), out Guid childNodeId))
                        continue;
                    if (!nodeById.TryGetValue(childNodeId, out SerializableNode? childNode))
                        continue;

                    MapStatementOffsetsRecursive(
                        childNode,
                        childExpression,
                        nodeById,
                        incomingConnections,
                        offsets,
                        recursionStack);
                }
            }
            finally
            {
                recursionStack.Remove(node.Id);
            }
        }

        private static void RecomputeStatementIndicesWithUAssetApi(JsonObject root, JsonArray exports, bool throwOnFailure = true)
        {
            string rebuiltJson = root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });

            try
            {
                UAsset rebuiltAsset = UAsset.DeserializeJson(rebuiltJson);
                AnnotateTopLevelStatementIndices(rebuiltAsset, exports);
            }
            catch (Exception ex)
            {
                if (throwOnFailure)
                    throw new InvalidOperationException("无法使用 UAssetAPI 重新计算导出蓝图的 StatementIndex。", ex);
            }
        }

        private JsonObject RebuildExpressionNode(
            SerializableNode node,
            string functionName,
            UAsset asset,
            BlueprintAssetContext assetContext,
            IReadOnlyDictionary<string, FunctionExport> localFunctionExports,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById,
            IReadOnlyDictionary<(Guid NodeId, string PinName), Guid> incomingConnections,
            IDictionary<Guid, JsonObject> rebuiltExpressionByNodeId,
            ISet<Guid> rebuiltNodeIds,
            HashSet<Guid> recursionStack)
        {
            if (!recursionStack.Add(node.Id))
                throw new InvalidOperationException($"检测到循环引用，无法重建表达式节点 {node.DefinitionName} ({node.Id})。");

            try
            {
                string? sourceExpressionJson = null;
                bool sourceFromDefinition = false;
                if (!TryResolveNodeSourceExpression(node, out sourceExpressionJson, out sourceFromDefinition))
                {
                    throw new InvalidOperationException($"节点 {node.DefinitionName} ({node.Id}) 缺少 SourceExpressionJson，无法导回 UAsset JSON。");
                }

                JsonObject expressionObj = (JsonNode.Parse(sourceExpressionJson) as JsonObject)?.DeepClone() as JsonObject
                    ?? throw new InvalidOperationException($"节点 {node.DefinitionName} ({node.Id}) 的 SourceExpressionJson 无法解析。");

                StripStatementIndices(expressionObj);
                ApplyExpressionEdits(node, expressionObj);
                NormalizeSetVariableAssignmentOpcode(node, functionName, assetContext, expressionObj);
                expressionObj = NormalizeCallExpressionForTargetConnection(node, expressionObj, incomingConnections);
                expressionObj = EnsureCallExpressionMatchesNodeFunction(node, expressionObj, incomingConnections);
                ApplyResolvedCallMetadataForNode(
                    expressionObj,
                    node,
                    asset,
                    assetContext,
                    BuildCurrentAssetSymbols(asset),
                    localFunctionExports);

                List<ExpressionChildSlot> childSlots = CollectChildExpressionSlots(expressionObj).ToList();
                foreach (ExpressionChildSlot slot in childSlots)
                {
                    if (!ShouldManageUserChildSlot(node, expressionObj, slot.PinName))
                        continue;

                    if (TryResolveIncomingConnectionForSlot(node, expressionObj, incomingConnections, slot.PinName, out Guid childNodeId) &&
                        nodeById.TryGetValue(childNodeId, out SerializableNode? childNode))
                    {
                        JsonObject rebuiltChild = RebuildExpressionNode(
                            childNode,
                            functionName,
                            asset,
                            assetContext,
                            localFunctionExports,
                            nodeById,
                            incomingConnections,
                            rebuiltExpressionByNodeId,
                            rebuiltNodeIds,
                            recursionStack);
                        slot.ReplaceWith(rebuiltChild);
                    }
                    else
                    {
                        slot.ReplaceWith(CreateNothingExpression());
                    }
                }

                EnsureValidContextPointersRecursive(expressionObj);

                rebuiltExpressionByNodeId[node.Id] = expressionObj;
                rebuiltNodeIds.Add(node.Id);
                return expressionObj;
            }
            finally
            {
                recursionStack.Remove(node.Id);
            }
        }

        private bool TryResolveIncomingConnectionForSlot(
            SerializableNode node,
            JsonObject expressionObj,
            IReadOnlyDictionary<(Guid NodeId, string PinName), Guid> incomingConnections,
            string slotPinName,
            out Guid childNodeId)
        {
            if (incomingConnections.TryGetValue((node.Id, slotPinName), out childNodeId))
                return true;

            if (TryMapSlotPinToDeclaredInputPin(node, slotPinName, out string declaredPinName) &&
                incomingConnections.TryGetValue((node.Id, declaredPinName), out childNodeId))
            {
                return true;
            }

            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (!TryResolveCallFunctionName(expressionObj, expressionType, out string functionName))
                return false;

            if (TryMapArgumentPinToParameterPin(functionName, slotPinName, out string parameterPinName) &&
                incomingConnections.TryGetValue((node.Id, parameterPinName), out childNodeId))
            {
                return true;
            }

            if (TryMapParameterPinToArgumentPin(functionName, slotPinName, out string argumentPinName) &&
                incomingConnections.TryGetValue((node.Id, argumentPinName), out childNodeId))
            {
                return true;
            }

            return false;
        }

        private bool ShouldManageUserChildSlot(
            SerializableNode node,
            JsonObject expressionObj,
            string slotPinName)
        {
            if (TryMapSlotPinToDeclaredInputPin(node, slotPinName, out _))
                return true;

            if (NodeDeclaresManagedInputPin(node, expressionObj, slotPinName))
                return true;

            if (!nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
                return false;

            if (definition.InputPins.Any(pin =>
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, slotPinName, StringComparison.Ordinal)))
            {
                return true;
            }

            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (!TryResolveCallFunctionName(expressionObj, expressionType, out string functionName))
                return false;

            if (TryMapArgumentPinToParameterPin(functionName, slotPinName, out string parameterPinName))
            {
                return definition.InputPins.Any(pin =>
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, parameterPinName, StringComparison.Ordinal));
            }

            if (TryMapParameterPinToArgumentPin(functionName, slotPinName, out string argumentPinName))
            {
                return definition.InputPins.Any(pin =>
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, argumentPinName, StringComparison.Ordinal));
            }

            return false;
        }

        private bool TryMapSlotPinToDeclaredInputPin(
            SerializableNode node,
            string slotPinName,
            out string declaredPinName)
        {
            declaredPinName = string.Empty;
            if (string.IsNullOrWhiteSpace(slotPinName))
                return false;

            if (NodeDeclaresManagedInputPin(node, slotPinName))
            {
                declaredPinName = slotPinName;
                return true;
            }

            if (!slotPinName.StartsWith("Arg", StringComparison.Ordinal) ||
                !int.TryParse(slotPinName[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int argumentIndex) ||
                argumentIndex <= 0)
            {
                return false;
            }

            List<string> orderedInputPins = GetOrderedUserDataInputPins(node);
            if (argumentIndex > orderedInputPins.Count)
                return false;

            declaredPinName = orderedInputPins[argumentIndex - 1];
            return !string.IsNullOrWhiteSpace(declaredPinName);
        }

        private List<string> GetOrderedUserDataInputPins(SerializableNode node)
        {
            IEnumerable<string> orderedPins = Enumerable.Empty<string>();

            if (nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
            {
                orderedPins = definition.InputPins
                    .Where(pin => pin.Type == PinType.Data &&
                        !string.Equals(pin.Name, "Target", StringComparison.Ordinal))
                    .Select(pin => pin.Name);
            }
            else if (TryGetInputPinNamesFromMetadata(node, out HashSet<string>? metadataPins) && metadataPins != null)
            {
                orderedPins = metadataPins.Where(pinName =>
                    !string.Equals(pinName, "Target", StringComparison.Ordinal));
            }
            else if (node.PinSchemas.Count > 0)
            {
                orderedPins = node.PinSchemas
                    .Where(schema =>
                        !string.IsNullOrWhiteSpace(schema.PinName) &&
                        !schema.IsTargetObject)
                    .Select(schema => schema.PinName);
            }

            List<string> result = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string pinName in orderedPins)
            {
                if (string.IsNullOrWhiteSpace(pinName) ||
                    string.Equals(pinName, "In", StringComparison.Ordinal) ||
                    string.Equals(pinName, "Return", StringComparison.Ordinal))
                {
                    continue;
                }

                if (seen.Add(pinName))
                    result.Add(pinName);
            }

            return result;
        }

        private bool NodeDeclaresManagedInputPin(
            SerializableNode node,
            JsonObject expressionObj,
            string slotPinName)
        {
            if (NodeDeclaresManagedInputPin(node, slotPinName))
                return true;

            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (!TryResolveCallFunctionName(expressionObj, expressionType, out string functionName))
                return false;

            if (TryMapArgumentPinToParameterPin(functionName, slotPinName, out string parameterPinName) &&
                NodeDeclaresManagedInputPin(node, parameterPinName))
            {
                return true;
            }

            if (TryMapParameterPinToArgumentPin(functionName, slotPinName, out string argumentPinName) &&
                NodeDeclaresManagedInputPin(node, argumentPinName))
            {
                return true;
            }

            return false;
        }

        private bool NodeDeclaresManagedInputPin(SerializableNode node, string pinName)
        {
            if (string.IsNullOrWhiteSpace(pinName))
                return false;

            if (node.PinSchemas.Any(schema =>
                    !string.IsNullOrWhiteSpace(schema.PinName) &&
                    string.Equals(schema.PinName, pinName, StringComparison.Ordinal)))
            {
                return true;
            }

            if (TryGetInputPinNamesFromMetadata(node, out HashSet<string>? metadataPins) &&
                metadataPins.Contains(pinName))
            {
                return true;
            }

            return nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition) &&
                definition.InputPins.Any(pin =>
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, pinName, StringComparison.Ordinal));
        }

        private static bool TryGetInputPinNamesFromMetadata(SerializableNode node, out HashSet<string>? inputPinNames)
        {
            inputPinNames = null;
            if (!node.MetaData.TryGetValue("InputPinMapJson", out string? inputPinMapJson) ||
                string.IsNullOrWhiteSpace(inputPinMapJson))
            {
                return false;
            }

            try
            {
                Dictionary<string, string>? pinMap = JsonSerializer.Deserialize<Dictionary<string, string>>(inputPinMapJson);
                if (pinMap == null || pinMap.Count == 0)
                    return false;

                inputPinNames = new HashSet<string>(
                    pinMap.Keys.Where(name => !string.IsNullOrWhiteSpace(name)),
                    StringComparer.Ordinal);
                return inputPinNames.Count > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private void NormalizeSpecificCallNodesForExport(BlueprintData blueprintData)
        {
            foreach (SerializableNode node in blueprintData.Nodes)
            {
                NormalizeSpecificCallNodeForExport(node);
            }
        }

        private void NormalizeSpecificCallNodeForExport(SerializableNode node)
        {
            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);
            if (!baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
                return;

            string functionName = ExtractCallFunctionName(baseDefinitionName);
            if (string.IsNullOrWhiteSpace(functionName) ||
                string.Equals(functionName, "Function", StringComparison.Ordinal))
            {
                return;
            }

            foreach (string key in new[] { "CallFunctionName", "FunctionName", "TargetFunctionName", "EditorText" })
            {
                node.PinValues.Remove(key);
            }

            node.MetaData["CallFunctionName"] = functionName;
            node.MetaData.Remove("SourceExpressionJson");
            node.MetaData.Remove("SourceExpressionType");
            node.MetaData.Remove("TemplateDerived");

            node.ReferenceDescriptors = node.ReferenceDescriptors
                .Where(descriptor =>
                    !IsStackNodeDescriptor(descriptor) ||
                    string.Equals(descriptor.ObjectName, functionName, StringComparison.Ordinal))
                .Select(BlueprintModelCloner.Clone)
                .ToList();

            if (nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
            {
                node.PinSchemas = BuildGenericCallPinSchemas(definition)
                    .Select(BlueprintModelCloner.Clone)
                    .ToList();
            }
        }

        private bool TryMapArgumentPinToParameterPin(string functionName, string slotPinName, out string parameterPinName)
        {
            parameterPinName = string.Empty;
            if (!slotPinName.StartsWith("Arg", StringComparison.Ordinal) ||
                !int.TryParse(slotPinName[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                index <= 0)
            {
                return false;
            }

            if (!knownFunctionParametersByName.TryGetValue(functionName, out List<string>? parameterNames) ||
                index > parameterNames.Count)
            {
                return false;
            }

            string candidate = parameterNames[index - 1];
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            parameterPinName = candidate;
            return true;
        }

        private bool TryMapParameterPinToArgumentPin(string functionName, string slotPinName, out string argumentPinName)
        {
            argumentPinName = string.Empty;
            if (!knownFunctionParametersByName.TryGetValue(functionName, out List<string>? parameterNames) ||
                parameterNames.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < parameterNames.Count; i++)
            {
                if (!string.Equals(parameterNames[i], slotPinName, StringComparison.Ordinal))
                    continue;

                argumentPinName = $"Arg{i + 1}";
                return true;
            }

            return false;
        }

        private JsonObject NormalizeCallExpressionForTargetConnection(
            SerializableNode node,
            JsonObject expressionObj,
            IReadOnlyDictionary<(Guid NodeId, string PinName), Guid> incomingConnections)
        {
            bool hasTargetConnection = incomingConnections.ContainsKey((node.Id, "Target"));
            if (TryGetMergedContextCallData(expressionObj, out JsonObject contextCallExpression, out _))
            {
                if (!hasTargetConnection)
                    return (JsonObject)contextCallExpression.DeepClone();
                return expressionObj;
            }

            if (!hasTargetConnection || !IsCallLikeExpression(expressionObj))
                return expressionObj;

            return CreateGenericContextCallExpression((JsonObject)expressionObj.DeepClone());
        }

        private JsonObject EnsureCallExpressionMatchesNodeFunction(
            SerializableNode node,
            JsonObject expressionObj,
            IReadOnlyDictionary<(Guid NodeId, string PinName), Guid> incomingConnections)
        {
            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);
            bool isCallNode =
                baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal) ||
                string.Equals(baseDefinitionName, "Call", StringComparison.Ordinal);
            if (!isCallNode)
                return expressionObj;

            string expectedFunctionName = ResolveCallFunctionNameFromNode(node);
            if (string.IsNullOrWhiteSpace(expectedFunctionName) ||
                string.Equals(expectedFunctionName, "Function", StringComparison.Ordinal))
            {
                return expressionObj;
            }

            bool needsContextWrapper = incomingConnections.ContainsKey((node.Id, "Target"));
            JsonObject effectiveExpression = expressionObj;
            string effectiveExpressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            bool hasContextWrapper = TryGetMergedContextCallData(expressionObj, out JsonObject contextCallExpression, out string contextCallType);
            if (hasContextWrapper)
            {
                effectiveExpression = contextCallExpression;
                effectiveExpressionType = contextCallType;
            }

            bool matchesExpectedFunction =
                TryResolveCallFunctionName(effectiveExpression, effectiveExpressionType, out string actualFunctionName) &&
                string.Equals(actualFunctionName, expectedFunctionName, StringComparison.Ordinal);

            bool hasExpectedWrapperShape = needsContextWrapper == hasContextWrapper;
            if (matchesExpectedFunction && hasExpectedWrapperShape)
                return expressionObj;

            string fallbackExpressionType = ResolveFallbackCallExpressionType(node, expectedFunctionName);
            int parameterCount = GetOrderedUserDataInputPins(node).Count;
            JsonObject regeneratedCallExpression = CreateGenericCallExpression(expectedFunctionName, parameterCount, fallbackExpressionType);
            return needsContextWrapper
                ? CreateGenericContextCallExpression(regeneratedCallExpression)
                : regeneratedCallExpression;
        }

        private bool TryResolveNodeSourceExpression(SerializableNode node, out string? sourceExpressionJson, out bool sourceFromDefinition)
        {
            sourceFromDefinition = false;
            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);
            string resolvedCallFunctionName = string.Empty;
            bool isSpecificCallNode = baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal);
            bool isGenericCallNode = string.Equals(baseDefinitionName, "Call", StringComparison.Ordinal);
            if (baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
            {
                resolvedCallFunctionName = ExtractCallFunctionName(baseDefinitionName);
                if (TryEnsureFunctionTemplateAvailable(resolvedCallFunctionName))
                    ApplyFunctionTemplateMetadataToDefinition(node.DefinitionName, resolvedCallFunctionName);
            }
            else if (isGenericCallNode)
            {
                resolvedCallFunctionName = ResolveCallFunctionNameFromNode(node);
                if (!string.IsNullOrWhiteSpace(resolvedCallFunctionName) && TryEnsureFunctionTemplateAvailable(resolvedCallFunctionName))
                    ApplyFunctionTemplateMetadataToDefinition(node.DefinitionName, resolvedCallFunctionName);
            }

            if (node.MetaData.TryGetValue("SourceExpressionJson", out sourceExpressionJson) &&
                !string.IsNullOrWhiteSpace(sourceExpressionJson))
            {
                if (!ShouldDiscardNodeSourceExpression(node, resolvedCallFunctionName, sourceExpressionJson))
                {
                    return true;
                }
            }

            if (nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition) &&
                !string.IsNullOrWhiteSpace(definition.SourceExpressionTemplateJson))
            {
                if (!ShouldDiscardNodeSourceExpression(node, resolvedCallFunctionName, definition.SourceExpressionTemplateJson))
                {
                    sourceExpressionJson = definition.SourceExpressionTemplateJson;
                    sourceFromDefinition = true;
                    return true;
                }
            }

            if (nodeDefinitions.TryGetValue(node.DefinitionName, out definition) &&
                !string.IsNullOrWhiteSpace(definition.RepresentativeCallTemplateJson))
            {
                if (!ShouldDiscardNodeSourceExpression(node, resolvedCallFunctionName, definition.RepresentativeCallTemplateJson))
                {
                    sourceExpressionJson = definition.RepresentativeCallTemplateJson;
                    sourceFromDefinition = true;
                    return true;
                }
            }

            if (TryCreateFallbackSourceExpressionJson(node, out sourceExpressionJson))
            {
                sourceFromDefinition = false;
                return true;
            }

            sourceExpressionJson = null;
            return false;
        }

        private static bool IsTemplateDerivedNode(SerializableNode node)
        {
            return node.MetaData.TryGetValue("TemplateDerived", out string? rawValue) &&
                bool.TryParse(rawValue, out bool isTemplateDerived) &&
                isTemplateDerived;
        }

        private bool ShouldDiscardNodeSourceExpression(SerializableNode node, string resolvedCallFunctionName, string sourceExpressionJson)
        {
            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);
            bool isCallNode =
                baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal) ||
                string.Equals(baseDefinitionName, "Call", StringComparison.Ordinal);
            if (!isCallNode)
                return false;

            if (string.IsNullOrWhiteSpace(resolvedCallFunctionName) ||
                string.Equals(resolvedCallFunctionName, "Function", StringComparison.Ordinal))
            {
                resolvedCallFunctionName = ResolveCallFunctionNameFromNode(node);
            }

            if (string.IsNullOrWhiteSpace(resolvedCallFunctionName) ||
                string.Equals(resolvedCallFunctionName, "Function", StringComparison.Ordinal))
            {
                return false;
            }

            JsonObject? sourceExpressionObj = JsonNode.Parse(sourceExpressionJson) as JsonObject;
            if (sourceExpressionObj == null ||
                !TryResolveExpressionCallFunctionName(sourceExpressionObj, out string sourceFunctionName) ||
                string.IsNullOrWhiteSpace(sourceFunctionName))
            {
                return false;
            }

            return !string.Equals(sourceFunctionName, resolvedCallFunctionName, StringComparison.Ordinal);
        }

        private bool TryResolveExpressionCallFunctionName(JsonObject expressionObj, out string functionName)
        {
            functionName = string.Empty;
            JsonObject effectiveExpression = expressionObj;
            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (TryGetMergedContextCallData(expressionObj, out JsonObject contextCallExpression, out string contextCallType))
            {
                effectiveExpression = contextCallExpression;
                expressionType = contextCallType;
            }

            return TryResolveCallFunctionName(effectiveExpression, expressionType, out functionName);
        }

        private bool TryCreateFallbackSourceExpressionJson(SerializableNode node, out string? sourceExpressionJson)
        {
            sourceExpressionJson = null;
            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);

            JsonObject? expressionObj = baseDefinitionName switch
            {
                "Return" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Return, UAssetAPI",
                    ["ReturnExpression"] = CreateIntZeroExpression()
                },
                "Jump" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Jump, UAssetAPI",
                    ["CodeOffset"] = 0
                },
                "Jump If Not" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_JumpIfNot, UAssetAPI",
                    ["CodeOffset"] = 0,
                    ["BooleanExpression"] = CreateFalseExpression()
                },
                "Computed Jump" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_ComputedJump, UAssetAPI",
                    ["CodeOffsetExpression"] = CreateIntZeroExpression()
                },
                "Push Flow" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_PushExecutionFlow, UAssetAPI",
                    ["PushingAddress"] = 0
                },
                "Pop Flow" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_PopExecutionFlow, UAssetAPI"
                },
                "Pop Flow If Not" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_PopExecutionFlowIfNot, UAssetAPI",
                    ["BooleanExpression"] = CreateFalseExpression()
                },
                "Self" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Self, UAssetAPI"
                },
                "Int Const" => CreateIntZeroExpression(),
                "Float Const" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_FloatConst, UAssetAPI",
                    ["Value"] = 0
                },
                "Double Const" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_DoubleConst, UAssetAPI",
                    ["Value"] = 0
                },
                "Byte Const" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_ByteConst, UAssetAPI",
                    ["Value"] = 0
                },
                "String Const" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_StringConst, UAssetAPI",
                    ["Value"] = string.Empty
                },
                "Text Const" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_TextConst, UAssetAPI",
                    ["Value"] = new JsonObject
                    {
                        ["TextLiteralType"] = "LiteralString",
                        ["LiteralString"] = new JsonObject
                        {
                            ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_StringConst, UAssetAPI",
                            ["Value"] = string.Empty
                        }
                    }
                },
                "Name Const" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_NameConst, UAssetAPI",
                    ["Value"] = "None"
                },
                "Bool Const" => CreateFalseExpression(),
                "No Object" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_NoObject, UAssetAPI"
                },
                _ => null
            };

            if (expressionObj == null &&
                (string.Equals(baseDefinitionName, "Call", StringComparison.Ordinal) ||
                 baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal)))
            {
                string functionName = ResolveCallFunctionNameFromNode(node);
                int parameterCount = 0;
                if (nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
                {
                    parameterCount = definition.InputPins.Count(pin =>
                        pin.Direction == PinDirection.Input &&
                        pin.Type == PinType.Data &&
                        !string.Equals(pin.Name, "Target", StringComparison.Ordinal));
                }

                string fallbackExpressionType = ResolveFallbackCallExpressionType(node, functionName);
                expressionObj = CreateGenericCallExpression(functionName, parameterCount, fallbackExpressionType);
            }

            if (expressionObj == null)
                return false;

            sourceExpressionJson = expressionObj.ToJsonString();
            return true;
        }

        private static bool CanBuildFallbackExpressionFromDefinitionName(string definitionName)
        {
            string baseDefinitionName = GetBaseDefinitionName(definitionName);
            if (baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
                return true;

            return baseDefinitionName is
                "Return" or
                "Jump" or
                "Jump If Not" or
                "Computed Jump" or
                "Push Flow" or
                "Pop Flow" or
                "Pop Flow If Not" or
                "Self" or
                "Int Const" or
                "Float Const" or
                "Double Const" or
                "Byte Const" or
                "String Const" or
                "Text Const" or
                "Name Const" or
                "Bool Const" or
                "No Object";
        }

        private static string GetBaseDefinitionName(string definitionName)
        {
            definitionName = NormalizeLegacyDefinitionName(definitionName);
            int hashSuffix = definitionName.LastIndexOf(" #", StringComparison.Ordinal);
            if (hashSuffix > 0 &&
                int.TryParse(definitionName[(hashSuffix + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return definitionName[..hashSuffix].TrimEnd();
            }

            return definitionName;
        }

        private static string NormalizeLegacyDefinitionName(string definitionName)
        {
            if (string.IsNullOrWhiteSpace(definitionName))
                return string.Empty;

            int hashSuffix = definitionName.LastIndexOf(" #", StringComparison.Ordinal);
            string baseName = definitionName;
            string suffix = string.Empty;
            if (hashSuffix > 0 &&
                int.TryParse(definitionName[(hashSuffix + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                baseName = definitionName[..hashSuffix].TrimEnd();
                suffix = definitionName[hashSuffix..];
            }

            if (string.Equals(baseName, "Branch", StringComparison.Ordinal))
                return "Jump If Not" + suffix;

            return definitionName;
        }

        private static string ExtractCallFunctionName(string definitionName)
        {
            if (!definitionName.StartsWith("Call ", StringComparison.Ordinal))
                return "Function";

            string functionName = definitionName.Substring(5).Trim();
            return string.IsNullOrWhiteSpace(functionName) ? "Function" : functionName;
        }

        private static string ResolveNodeEditorText(SerializableNode node)
        {
            if (!node.PinValues.TryGetValue("EditorText", out object? rawValue) || rawValue == null)
                return string.Empty;

            if (rawValue is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.String)
                    return element.GetString() ?? string.Empty;
                return element.ToString();
            }

            return Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string ResolveCallFunctionNameFromNode(SerializableNode node)
        {
            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);
            if (baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
                return ExtractCallFunctionName(baseDefinitionName);

            string explicitFunctionName = ResolveCallFunctionNameFromPinValues(node);
            if (!string.IsNullOrWhiteSpace(explicitFunctionName))
                return explicitFunctionName;

            if (node.MetaData.TryGetValue("CallFunctionName", out string? metadataFunctionName) &&
                !string.IsNullOrWhiteSpace(metadataFunctionName))
            {
                return metadataFunctionName.Trim();
            }

            if (string.Equals(baseDefinitionName, "Call", StringComparison.Ordinal))
            {
                string editorText = ResolveNodeEditorText(node).Trim();
                if (!string.IsNullOrWhiteSpace(editorText))
                    return editorText;
            }

            return "Function";
        }

        private static string ResolveCallFunctionNameFromPinValues(SerializableNode node)
        {
            foreach (string key in new[] { "CallFunctionName", "FunctionName", "TargetFunctionName" })
            {
                if (!node.PinValues.TryGetValue(key, out object? rawValue) || rawValue == null)
                    continue;

                string value = ConvertPinValueToString(rawValue).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static string ConvertPinValueToString(object rawValue)
        {
            if (rawValue is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.String)
                    return element.GetString() ?? string.Empty;
                return element.ToString();
            }

            return Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static JsonObject CreateIntZeroExpression()
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_IntZero, UAssetAPI"
            };
        }

        private static JsonObject CreateFalseExpression()
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_False, UAssetAPI"
            };
        }

        private static JsonObject CreateNothingExpression()
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Nothing, UAssetAPI"
            };
        }

        private static JsonObject CreateGenericLocalVariableExpression(string propertyName = "Variable", int resolvedOwner = 0)
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_LocalVariable, UAssetAPI",
                ["Variable"] = CreatePropertyPointerExpression(propertyName, resolvedOwner)
            };
        }

        private static JsonObject CreateGenericSetVariableExpression(string propertyName = "Variable", int resolvedOwner = 0)
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Let, UAssetAPI",
                ["Value"] = CreatePropertyPointerExpression(propertyName, resolvedOwner),
                ["Variable"] = CreateGenericLocalVariableExpression(propertyName, resolvedOwner),
                ["Expression"] = CreateIntZeroExpression()
            };
        }

        private static JsonObject CreatePropertyPointerExpression(string propertyName, int resolvedOwner)
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.KismetPropertyPointer, UAssetAPI",
                ["New"] = new JsonObject
                {
                    ["$type"] = "UAssetAPI.UnrealTypes.FFieldPath, UAssetAPI",
                    ["Path"] = new JsonArray(propertyName),
                    ["ResolvedOwner"] = resolvedOwner
                }
            };
        }

        private static JsonObject CreateGenericLocalVirtualFunctionExpression(string functionName, int parameterCount)
        {
            JsonArray parameters = [];
            for (int i = 0; i < parameterCount; i++)
            {
                parameters.Add(CreateIntZeroExpression());
            }

            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_LocalVirtualFunction, UAssetAPI",
                ["VirtualFunctionName"] = functionName,
                ["Parameters"] = parameters
            };
        }

        private static JsonObject CreateGenericCallExpression(string functionName, int parameterCount, string expressionType)
        {
            JsonArray parameters = [];
            for (int i = 0; i < parameterCount; i++)
            {
                parameters.Add(CreateIntZeroExpression());
            }

            return expressionType switch
            {
                "CallMath" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_CallMath, UAssetAPI",
                    ["StackNode"] = 0,
                    ["Parameters"] = parameters
                },
                "LocalFinalFunction" => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_LocalFinalFunction, UAssetAPI",
                    ["StackNode"] = 0,
                    ["Parameters"] = parameters
                },
                "LocalVirtualFunction" => CreateGenericLocalVirtualFunctionExpression(functionName, parameterCount),
                _ => new JsonObject
                {
                    ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_FinalFunction, UAssetAPI",
                    ["StackNode"] = 0,
                    ["Parameters"] = parameters
                }
            };
        }

        private static JsonObject CreateEmptyPropertyPointerExpression()
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.KismetPropertyPointer, UAssetAPI",
                ["New"] = new JsonObject
                {
                    ["$type"] = "UAssetAPI.UnrealTypes.FFieldPath, UAssetAPI",
                    ["Path"] = new JsonArray(),
                    ["ResolvedOwner"] = 0
                }
            };
        }

        private string ResolveFallbackCallExpressionType(SerializableNode node, string functionName)
        {
            if (nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
            {
                foreach (string? candidate in new[]
                {
                    definition.RepresentativeCallExpressionType,
                    definition.SourceExpressionType
                })
                {
                    if (IsSupportedFallbackCallExpressionType(candidate))
                        return candidate!;
                }

                if (TryResolveCallExpressionTypeFromTemplate(definition.RepresentativeCallTemplateJson, out string representativeType))
                    return representativeType;

                if (TryResolveCallExpressionTypeFromTemplate(definition.SourceExpressionTemplateJson, out string sourceType))
                    return sourceType;
            }

            return LooksLikeMathLibraryFunctionName(functionName) ? "CallMath" : "FinalFunction";
        }

        private static bool TryResolveCallExpressionTypeFromTemplate(string? templateJson, out string expressionType)
        {
            expressionType = string.Empty;
            if (string.IsNullOrWhiteSpace(templateJson))
                return false;

            try
            {
                if (JsonNode.Parse(templateJson) is not JsonObject templateExpression)
                    return false;

                string candidate = SimplifyExpressionType(templateExpression["$type"]?.GetValue<string>() ?? string.Empty);
                if (!IsSupportedFallbackCallExpressionType(candidate))
                    return false;

                expressionType = candidate;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsSupportedFallbackCallExpressionType(string? expressionType)
        {
            return expressionType is "FinalFunction" or "LocalFinalFunction" or "CallMath" or "LocalVirtualFunction";
        }

        private static bool LooksLikeMathLibraryFunctionName(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return false;

            return functionName.Contains("_Int", StringComparison.Ordinal) ||
                functionName.Contains("_Float", StringComparison.Ordinal) ||
                functionName.Contains("_Double", StringComparison.Ordinal) ||
                functionName.Contains("_Byte", StringComparison.Ordinal) ||
                functionName.Contains("_Object", StringComparison.Ordinal) ||
                functionName.Contains("_Vector", StringComparison.Ordinal) ||
                functionName.Contains("Equal", StringComparison.Ordinal) ||
                functionName.Contains("NotEqual", StringComparison.Ordinal) ||
                functionName.Contains("Less", StringComparison.Ordinal) ||
                functionName.Contains("Greater", StringComparison.Ordinal) ||
                functionName.Contains("Add_", StringComparison.Ordinal) ||
                functionName.Contains("Subtract_", StringComparison.Ordinal) ||
                functionName.Contains("Multiply_", StringComparison.Ordinal) ||
                functionName.Contains("Divide_", StringComparison.Ordinal);
        }

        private static JsonObject CreateGenericContextCallExpression(JsonObject callExpression)
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Context, UAssetAPI",
                ["ObjectExpression"] = CreateNothingExpression(),
                ["Offset"] = 0,
                ["PropertyType"] = 0,
                ["RValuePointer"] = CreateEmptyPropertyPointerExpression(),
                ["ContextExpression"] = callExpression
            };
        }

        private static void EnsureValidContextPointersRecursive(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                string expressionType = SimplifyExpressionType(obj["$type"]?.GetValue<string>() ?? string.Empty);
                if (IsContextWrapperExpressionType(expressionType))
                {
                    if (obj["ObjectExpression"] is not JsonObject objectExpression || !IsExpressionObject(objectExpression))
                        obj["ObjectExpression"] = CreateNothingExpression();

                    obj["Offset"] ??= 0;
                    obj["PropertyType"] ??= 0;

                    if (obj["RValuePointer"] is not JsonObject pointerObject)
                    {
                        obj["RValuePointer"] = CreateEmptyPropertyPointerExpression();
                    }
                    else
                    {
                        pointerObject["$type"] ??= "UAssetAPI.Kismet.Bytecode.KismetPropertyPointer, UAssetAPI";
                        if (pointerObject["New"] is not JsonObject fieldPathObject)
                        {
                            pointerObject["New"] = CreateEmptyPropertyPointerExpression()["New"]?.DeepClone();
                        }
                        else
                        {
                            fieldPathObject["$type"] ??= "UAssetAPI.UnrealTypes.FFieldPath, UAssetAPI";
                            if (fieldPathObject["Path"] is not JsonArray)
                                fieldPathObject["Path"] = new JsonArray();
                            fieldPathObject["ResolvedOwner"] ??= 0;
                        }
                    }
                }

                foreach ((_, JsonNode? child) in obj)
                {
                    EnsureValidContextPointersRecursive(child);
                }

                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    EnsureValidContextPointersRecursive(child);
                }
            }
        }

        private static JsonObject CreateGenericTextConstExpression()
        {
            return new JsonObject
            {
                ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_TextConst, UAssetAPI",
                ["Value"] = new JsonObject
                {
                    ["TextLiteralType"] = "LiteralString",
                    ["LiteralString"] = new JsonObject
                    {
                        ["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_StringConst, UAssetAPI",
                        ["Value"] = string.Empty
                    }
                }
            };
        }

        private static void StripStatementIndices(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                obj.Remove("StatementIndex");
                foreach ((string _, JsonNode? child) in obj)
                {
                    StripStatementIndices(child);
                }
                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    StripStatementIndices(child);
                }
            }
        }

        private void ApplyExpressionEdits(SerializableNode node, JsonObject expressionObj)
        {
            string expressionType = node.MetaData.TryGetValue("SourceExpressionType", out string? typeName)
                ? typeName
                : SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            object? constEditedValue = node.PinValues.TryGetValue("__RawConstValueText", out object? rawConstValue)
                ? rawConstValue
                : node.PinValues.TryGetValue("Value", out object? editedValue)
                    ? editedValue
                    : null;

            if (constEditedValue != null)
            {
                switch (expressionType)
                {
                    case "IntConst":
                        if (TryConvertInt(constEditedValue, out int intValue))
                            expressionObj["Value"] = intValue;
                        break;
                    case "IntConstByte":
                        if (TryConvertInt(constEditedValue, out int intConstByteValue))
                        {
                            if (intConstByteValue is >= byte.MinValue and <= byte.MaxValue)
                            {
                                expressionObj["Value"] = intConstByteValue;
                            }
                            else
                            {
                                // Promote EX_IntConstByte to EX_IntConst when value exceeds byte range.
                                expressionObj["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_IntConst, UAssetAPI";
                                expressionObj["Value"] = intConstByteValue;
                            }
                        }
                        break;
                    case "IntZero":
                    case "IntOne":
                        if (TryConvertInt(constEditedValue, out int intLiteralValue))
                        {
                            if (intLiteralValue == 0)
                            {
                                expressionObj["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_IntZero, UAssetAPI";
                                expressionObj.Remove("Value");
                            }
                            else if (intLiteralValue == 1)
                            {
                                expressionObj["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_IntOne, UAssetAPI";
                                expressionObj.Remove("Value");
                            }
                            else
                            {
                                expressionObj["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_IntConst, UAssetAPI";
                                expressionObj["Value"] = intLiteralValue;
                            }
                        }
                        break;
                    case "ByteConst":
                        if (TryConvertInt(constEditedValue, out int byteValue))
                            expressionObj["Value"] = Math.Clamp(byteValue, byte.MinValue, byte.MaxValue);
                        break;
                    case "FloatConst":
                        if (TryConvertFloat(constEditedValue, out float floatValue))
                            expressionObj["Value"] = floatValue;
                        break;
                    case "DoubleConst":
                        if (TryConvertDouble(constEditedValue, out double doubleValue))
                            expressionObj["Value"] = doubleValue;
                        break;
                    case "StringConst":
                    case "NameConst":
                        expressionObj["Value"] = Convert.ToString(constEditedValue, CultureInfo.InvariantCulture);
                        break;
                    case "TextConst":
                        ApplyTextConstEdit(expressionObj, Convert.ToString(constEditedValue, CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                    case "True":
                    case "False":
                        if (TryConvertBool(constEditedValue, out bool boolValue))
                        {
                            expressionObj["$type"] = boolValue
                                ? "UAssetAPI.Kismet.Bytecode.Expressions.EX_True, UAssetAPI"
                                : "UAssetAPI.Kismet.Bytecode.Expressions.EX_False, UAssetAPI";
                        }
                        break;
                }
            }

            if (node.PinValues.TryGetValue("EditorText", out object? editedTextObj))
            {
                string editedText = Convert.ToString(editedTextObj, CultureInfo.InvariantCulture) ?? string.Empty;
                switch (expressionType)
                {
                    case "TextConst":
                        ApplyTextConstEdit(expressionObj, editedText);
                        break;
                    case "LocalVirtualFunction":
                        if (!string.IsNullOrWhiteSpace(editedText))
                            expressionObj["VirtualFunctionName"] = editedText;
                        break;
                    case "LocalVariable":
                    case "LocalOutVariable":
                    case "InstanceVariable":
                    case "DefaultVariable":
                        SetFieldPathName(expressionObj["Variable"], editedText);
                        break;
                    case "Let":
                    case "LetBool":
                    case "LetObj":
                    case "LetWeakObjPtr":
                    case "LetDelegate":
                    case "LetMulticastDelegate":
                    case "LetValueOnPersistentFrame":
                        ApplyAssignmentTargetName(expressionObj, editedText);
                        break;
                }
            }
        }

        private static void ApplyAssignmentTargetName(JsonObject expressionObj, string editedText)
        {
            if (string.IsNullOrWhiteSpace(editedText)) return;

            if (SetFieldPathName(expressionObj["DestinationProperty"], editedText)) return;
            if (SetFieldPathName(expressionObj["Variable"]?["Variable"], editedText)) return;
            if (SetFieldPathName(expressionObj["VariableExpression"]?["Variable"], editedText)) return;
            _ = SetFieldPathName(expressionObj["Value"], editedText);
        }

        private static bool SetFieldPathName(JsonNode? node, string editedText)
        {
            if (string.IsNullOrWhiteSpace(editedText)) return false;

            JsonArray? pathArray = node?["New"]?["Path"] as JsonArray;
            if (pathArray == null) return false;

            if (pathArray.Count == 0)
                pathArray.Add(editedText);
            else
                pathArray[pathArray.Count - 1] = editedText;

            return true;
        }

        private void NormalizeSetVariableAssignmentOpcode(
            SerializableNode node,
            string functionName,
            BlueprintAssetContext assetContext,
            JsonObject expressionObj)
        {
            string baseDefinitionName = GetBaseDefinitionName(node.DefinitionName);
            if (!string.Equals(baseDefinitionName, "Set Variable", StringComparison.Ordinal))
                return;

            if (!TryResolveVariableLoadedPropertyTemplate(assetContext, functionName, node, out VariableLoadedPropertyResolution? resolution) ||
                resolution == null ||
                !TryResolveSerializedTypeFromPropertyTemplate(resolution.Template.PropertyTemplateJson, out string serializedType))
            {
                return;
            }

            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (string.Equals(serializedType, "BoolProperty", StringComparison.Ordinal))
            {
                if (expressionType == "Let")
                    ConvertAssignmentExpressionToLetBool(expressionObj, resolution.Template.PropertyName);
                return;
            }

            if (expressionType is "LetBool" or "LetObj" or "LetWeakObjPtr" or "LetDelegate" or "LetMulticastDelegate")
            {
                ConvertAssignmentExpressionToLet(expressionObj, resolution.Template.PropertyName);
                return;
            }

            if (expressionType == "Let" && expressionObj["Value"] == null)
            {
                expressionObj["Value"] = TryCloneAssignmentVariablePointer(expressionObj, out JsonNode? pointer)
                    ? pointer
                    : CreatePropertyPointerExpression(resolution.Template.PropertyName, 0);
            }
        }

        private static void ConvertAssignmentExpressionToLet(JsonObject expressionObj, string propertyName)
        {
            JsonNode variableExpression = expressionObj["VariableExpression"]?.DeepClone() ??
                expressionObj["Variable"]?.DeepClone() ??
                CreateGenericLocalVariableExpression(propertyName);
            JsonNode assignmentExpression = expressionObj["AssignmentExpression"]?.DeepClone() ??
                expressionObj["Expression"]?.DeepClone() ??
                CreateNothingExpression();
            JsonNode valuePointer = TryCloneVariableExpressionPointer(variableExpression, out JsonNode? clonedPointer)
                ? clonedPointer
                : expressionObj["Value"]?.DeepClone() ?? CreatePropertyPointerExpression(propertyName, 0);

            expressionObj["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_Let, UAssetAPI";
            expressionObj["Value"] = valuePointer;
            expressionObj["Variable"] = variableExpression;
            expressionObj["Expression"] = assignmentExpression;
            expressionObj.Remove("VariableExpression");
            expressionObj.Remove("AssignmentExpression");
        }

        private static void ConvertAssignmentExpressionToLetBool(JsonObject expressionObj, string propertyName)
        {
            JsonNode variableExpression = expressionObj["VariableExpression"]?.DeepClone() ??
                expressionObj["Variable"]?.DeepClone() ??
                CreateGenericLocalVariableExpression(propertyName);
            JsonNode assignmentExpression = expressionObj["AssignmentExpression"]?.DeepClone() ??
                expressionObj["Expression"]?.DeepClone() ??
                CreateFalseExpression();

            expressionObj["$type"] = "UAssetAPI.Kismet.Bytecode.Expressions.EX_LetBool, UAssetAPI";
            expressionObj["VariableExpression"] = variableExpression;
            expressionObj["AssignmentExpression"] = assignmentExpression;
            expressionObj.Remove("Value");
            expressionObj.Remove("Variable");
            expressionObj.Remove("Expression");
        }

        private static bool TryCloneAssignmentVariablePointer(JsonObject expressionObj, out JsonNode? pointer)
        {
            pointer = null;
            JsonNode? variableExpression = expressionObj["Variable"] ?? expressionObj["VariableExpression"];
            if (variableExpression == null)
                return false;

            return TryCloneVariableExpressionPointer(variableExpression, out pointer);
        }

        private static bool TryCloneVariableExpressionPointer(JsonNode variableExpression, out JsonNode? pointer)
        {
            pointer = null;
            if (variableExpression is not JsonObject variableObj)
                return false;

            JsonNode? candidate = variableObj["Variable"];
            if (candidate == null)
                return false;

            pointer = candidate.DeepClone();
            return true;
        }

        private static bool TryResolveSerializedTypeFromPropertyTemplate(string propertyTemplateJson, out string serializedType)
        {
            serializedType = string.Empty;
            if (string.IsNullOrWhiteSpace(propertyTemplateJson))
                return false;

            try
            {
                if (JsonNode.Parse(propertyTemplateJson) is not JsonObject propertyObj)
                    return false;

                serializedType = propertyObj["SerializedType"]?.GetValue<string>() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(serializedType);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryConvertInt(object? value, out int result)
        {
            result = default;
            return value switch
            {
                int i => (result = i) == i,
                JsonElement { ValueKind: JsonValueKind.Number } element => element.TryGetInt32(out result),
                _ => int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            };
        }

        private static bool TryConvertFloat(object? value, out float result)
        {
            result = default;
            return value switch
            {
                float f => (result = f) == f,
                double d => (result = (float)d) == (float)d,
                JsonElement { ValueKind: JsonValueKind.Number } element => element.TryGetSingle(out result),
                _ => float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result)
            };
        }

        private static bool TryConvertDouble(object? value, out double result)
        {
            result = default;
            return value switch
            {
                double d => (result = d) == d,
                float f => (result = f) == f,
                JsonElement { ValueKind: JsonValueKind.Number } element => element.TryGetDouble(out result),
                _ => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result)
            };
        }

        private static bool TryConvertBool(object? value, out bool result)
        {
            result = default;
            return value switch
            {
                bool b => (result = b) == b,
                JsonElement { ValueKind: JsonValueKind.True } => (result = true),
                JsonElement { ValueKind: JsonValueKind.False } => !(result = false),
                _ => bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result)
            };
        }

        private IEnumerable<ExpressionChildSlot> CollectChildExpressionSlots(JsonObject expressionObj)
        {
            if (TryGetMergedContextCallData(expressionObj, out JsonObject contextCallExpression, out _))
            {
                if (expressionObj["ObjectExpression"] is JsonObject objectExpression &&
                    IsExpressionObject(objectExpression))
                {
                    yield return new ExpressionChildSlot("Target", replacement => expressionObj["ObjectExpression"] = replacement);
                }

                foreach (ExpressionChildSlot slot in CollectChildExpressionSlotsCore(contextCallExpression))
                {
                    yield return slot;
                }

                yield break;
            }

            foreach (ExpressionChildSlot slot in CollectChildExpressionSlotsCore(expressionObj))
            {
                yield return slot;
            }
        }

        private IEnumerable<ExpressionChildSlot> CollectChildExpressionSlotsCore(JsonObject expressionObj)
        {
            foreach ((string key, JsonNode? value) in expressionObj)
            {
                if (key == "$type" || value == null) continue;

                if (value is JsonObject childObject && IsExpressionObject(childObject))
                {
                    yield return new ExpressionChildSlot(NormalizePinName(key), replacement => expressionObj[key] = replacement);
                    continue;
                }

                if (value is not JsonArray arrayValue) continue;

                for (int i = 0; i < arrayValue.Count; i++)
                {
                    if (arrayValue[i] is not JsonObject childExpr || !IsExpressionObject(childExpr))
                        continue;

                    string pinName = key == "Parameters"
                        ? $"Arg{i + 1}"
                        : $"{NormalizePinName(key)}[{i}]";
                    int arrayIndex = i;
                    yield return new ExpressionChildSlot(pinName, replacement => arrayValue[arrayIndex] = replacement);
                }
            }
        }

        private List<(JsonObject Child, string PinName)> CollectDisplayChildExpressions(JsonObject expressionObj)
        {
            if (TryGetMergedContextCallData(expressionObj, out JsonObject contextCallExpression, out _))
            {
                List<(JsonObject Child, string PinName)> mergedChildren = [];
                if (expressionObj["ObjectExpression"] is JsonObject objectExpression &&
                    IsExpressionObject(objectExpression) &&
                    !IsTerminalExpression(objectExpression))
                {
                    mergedChildren.Add((objectExpression, "Target"));
                }

                mergedChildren.AddRange(CollectChildExpressions(contextCallExpression));
                return mergedChildren;
            }

            return CollectChildExpressions(expressionObj);
        }

        private static int ParseMetaInt(SerializableNode node, string key)
        {
            return node.MetaData.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : int.MaxValue;
        }

        private static void AnnotateTopLevelStatementIndices(UAsset uAsset, JsonArray exports)
        {
            KismetSerializer.asset = uAsset;

            for (int i = 0; i < exports.Count && i < uAsset.Exports.Count; i++)
            {
                if (exports[i] is not JsonObject exportObj ||
                    uAsset.Exports[i] is not FunctionExport functionExport)
                {
                    continue;
                }

                JsonArray? rawScript = exportObj["ScriptBytecode"] as JsonArray;
                if (rawScript == null || functionExport.ScriptBytecode == null || functionExport.ScriptBytecode.Length == 0)
                    continue;

                JsonArray? exactScript;
                try
                {
                    exactScript = JsonNode.Parse(KismetSerializer.SerializeScript(functionExport.ScriptBytecode).ToString()) as JsonArray;
                }
                catch
                {
                    // Some malformed/legacy function exports may fail statement-index re-serialization.
                    // Skip them so one bad function doesn't break the whole export/save flow.
                    continue;
                }
                if (exactScript == null) continue;

                int count = Math.Min(rawScript.Count, exactScript.Count);
                for (int j = 0; j < count; j++)
                {
                    if (rawScript[j] is not JsonObject rawExpr || exactScript[j] is not JsonObject exactExpr)
                        continue;

                    JsonNode? exactIndex = exactExpr["StatementIndex"];
                    if (exactIndex != null)
                        rawExpr["StatementIndex"] = exactIndex.GetValue<int>();
                }
            }
        }

        private readonly record struct BuiltExpression(Guid NodeId, bool HasExecInput, bool HasExecOutput, string? DataOutputPinName);
        private readonly record struct ExpressionChildSlot(string PinName, Action<JsonObject> ReplaceWith);
        private readonly record struct VisualExpressionSpec(
            JsonObject DisplayExpression,
            JsonObject? TargetExpression,
            List<(JsonObject Child, string PinName)> ChildExpressions,
            List<string> ChildPinNames,
            string DisplayName,
            string ExpressionType,
            bool HasExecInput,
            bool HasExecOutput,
            string? DataOutputPinName,
            string? EditableNodeText,
            object? EditableConstantValue,
            bool IsMergedContextCall,
            bool ForceIncludeTargetInput);

        private BuiltExpression BuildExpressionGraph(
            JsonObject expressionObj,
            BlueprintData blueprintData,
            string functionName,
            int functionBaseY,
            ref int localNodeIndex,
            int depth,
            int topLevelOrder,
            HashSet<int> recursionStack)
        {
            if (depth > MaxExpressionDepth)
            {
                return CreatePlaceholderExpressionNode(
                    "Depth Limit",
                    blueprintData,
                    functionName,
                    functionBaseY,
                    ref localNodeIndex,
                    depth);
            }

            if (blueprintData.Nodes.Count >= MaxBlueprintNodes)
            {
                return CreatePlaceholderExpressionNode(
                    "Node Limit",
                    blueprintData,
                    functionName,
                    functionBaseY,
                    ref localNodeIndex,
                    depth);
            }

            int expressionRefId = RuntimeHelpers.GetHashCode(expressionObj);
            if (!recursionStack.Add(expressionRefId))
            {
                return CreatePlaceholderExpressionNode(
                    "Recursive Expression",
                    blueprintData,
                    functionName,
                    functionBaseY,
                    ref localNodeIndex,
                    depth);
            }

            try
            {
                VisualExpressionSpec visualSpec = GetVisualExpressionSpec(expressionObj);
                JsonObject nodeExpression = visualSpec.DisplayExpression;
                string displayName = visualSpec.DisplayName;

                List<Pin> inputPins = [];
                List<Pin> outputPins = [];

                bool hasExecInput = visualSpec.HasExecInput;
                bool hasExecOutput = visualSpec.HasExecOutput;
                string? dataOutputPin = visualSpec.DataOutputPinName;

                if (hasExecInput) inputPins.Add(new Pin("In", PinDirection.Input, PinType.Exec));
                if (hasExecOutput) outputPins.Add(new Pin("Out", PinDirection.Output, PinType.Exec));
                if (!string.IsNullOrWhiteSpace(dataOutputPin))
                    outputPins.Add(new Pin(dataOutputPin!, PinDirection.Output, PinType.Data));

                List<(JsonObject Child, string PinName)> childExpressions = visualSpec.ChildExpressions;
                List<string> childPinNames = visualSpec.ChildPinNames;
                if (TryResolveCallFunctionName(nodeExpression, visualSpec.ExpressionType, out string callFunctionName))
                {
                    childExpressions = ApplyKnownCallParameterNames(callFunctionName, childExpressions);
                    childPinNames = ApplyKnownCallParameterNames(callFunctionName, childPinNames);
                }
                childExpressions = childExpressions
                    .Where(child => !ShouldHideChildPin(visualSpec.ExpressionType, child.PinName, visualSpec.EditableNodeText))
                    .ToList();
                childPinNames = childPinNames
                    .Where(pinName => !ShouldHideChildPin(visualSpec.ExpressionType, pinName, visualSpec.EditableNodeText))
                    .ToList();

                if (visualSpec.ForceIncludeTargetInput && !inputPins.Any(p => p.Name == "Target"))
                {
                    inputPins.Add(new Pin("Target", PinDirection.Input, PinType.Data));
                }

                // ReturnExpression is commonly EX_Nothing. We don't materialize terminal children as nodes,
                // but Return must still expose a data input pin to match canonical node definition.
                if (visualSpec.ExpressionType == "Return" &&
                    !childExpressions.Any(child => string.Equals(child.PinName, "Return", StringComparison.Ordinal)))
                {
                    inputPins.Add(new Pin("Return", PinDirection.Input, PinType.Data));
                }

                foreach (string pinName in childPinNames)
                {
                    if (!inputPins.Any(p => p.Name == pinName))
                        inputPins.Add(new Pin(pinName, PinDirection.Input, PinType.Data));
                }

                EnsureImportedExecPins(displayName, inputPins, outputPins);

                string definitionName = EnsureNodeDefinition(
                    displayName,
                    inputPins,
                    outputPins,
                    expressionObj.ToJsonString(),
                    SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty));
                if (IsGenericVariableDefinitionName(displayName))
                    definitionName = GetBaseDefinitionName(definitionName);
                SerializableNode currentNode = new()
                {
                    Id = Guid.NewGuid(),
                    DefinitionName = definitionName,
                    Location = new Point(Math.Max(60, 980 - depth * 240), functionBaseY + localNodeIndex * 100)
                };
                currentNode.MetaData["NodeKind"] = "Expression";
                currentNode.MetaData["FunctionName"] = functionName;
                currentNode.MetaData["SourceExpressionJson"] = expressionObj.ToJsonString();
                currentNode.MetaData["SourceExpressionType"] = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
                currentNode.MetaData["DisplayExpressionType"] = visualSpec.ExpressionType;
                currentNode.MetaData["DisplayMode"] = visualSpec.IsMergedContextCall ? "MergedContextCall" : "Direct";
                currentNode.MetaData["InputPinMapJson"] = JsonSerializer.Serialize(childExpressions.ToDictionary(x => x.PinName, x => x.Child["$type"]?.GetValue<string>() ?? string.Empty));
                if (TryResolveCallFunctionName(nodeExpression, visualSpec.ExpressionType, out string resolvedCallFunctionName) &&
                    !string.IsNullOrWhiteSpace(resolvedCallFunctionName))
                {
                    currentNode.MetaData["CallFunctionName"] = resolvedCallFunctionName;
                }
                if (expressionObj["StatementIndex"] != null)
                {
                    string statementIndex = expressionObj["StatementIndex"]!.ToJsonString();
                    currentNode.MetaData["OriginalStatementIndex"] = statementIndex;
                    currentNode.MetaData["StatementIndex"] = statementIndex;
                }
                if (depth == 0)
                    currentNode.MetaData["TopLevelOrder"] = topLevelOrder.ToString();

                string? editableNodeText = visualSpec.EditableNodeText;
                if (!string.IsNullOrWhiteSpace(editableNodeText))
                    currentNode.PinValues["EditorText"] = editableNodeText;
                object? editableConstantValue = visualSpec.EditableConstantValue;
                if (editableConstantValue != null)
                    currentNode.PinValues["Value"] = editableConstantValue;
                PopulateNodeExportMetadata(currentNode, definitionName, functionName, expressionObj);
                localNodeIndex++;
                blueprintData.Nodes.Add(currentNode);

                foreach ((JsonObject childExpression, string pinName) in childExpressions)
                {
                    BuiltExpression childNode = BuildExpressionGraph(
                        childExpression,
                        blueprintData,
                        functionName,
                        functionBaseY,
                        ref localNodeIndex,
                        depth + 1,
                        topLevelOrder,
                        recursionStack);

                    if (!string.IsNullOrWhiteSpace(childNode.DataOutputPinName))
                    {
                        blueprintData.Connections.Add(new ConnectionData
                        {
                            FromNodeId = childNode.NodeId,
                            FromPinName = childNode.DataOutputPinName!,
                            ToNodeId = currentNode.Id,
                            ToPinName = pinName
                        });
                    }
                }

                return new BuiltExpression(currentNode.Id, hasExecInput, hasExecOutput, dataOutputPin);
            }
            finally
            {
                recursionStack.Remove(expressionRefId);
            }
        }

        private static void EnsureImportedExecPins(string displayName, List<Pin> inputPins, List<Pin> outputPins)
        {
            string normalizedDisplayName = NormalizeLegacyDefinitionName(displayName);
            switch (normalizedDisplayName)
            {
                case "Return":
                    EnsurePinPresent(inputPins, "In", PinDirection.Input, PinType.Exec);
                    EnsurePinPresent(inputPins, "Return", PinDirection.Input, PinType.Data);
                    EnsurePinPresent(outputPins, "Out", PinDirection.Output, PinType.Exec);
                    break;
                case "Jump":
                    EnsurePinPresent(inputPins, "In", PinDirection.Input, PinType.Exec);
                    EnsurePinPresent(outputPins, "Out", PinDirection.Output, PinType.Exec);
                    EnsurePinPresent(outputPins, "To", PinDirection.Output, PinType.Exec);
                    break;
                case "Jump If Not":
                    EnsurePinPresent(inputPins, "In", PinDirection.Input, PinType.Exec);
                    EnsurePinPresent(inputPins, "Condition", PinDirection.Input, PinType.Data);
                    EnsurePinPresent(outputPins, "Out", PinDirection.Output, PinType.Exec);
                    EnsurePinPresent(outputPins, "To", PinDirection.Output, PinType.Exec);
                    break;
                case "Push Flow":
                    EnsurePinPresent(inputPins, "In", PinDirection.Input, PinType.Exec);
                    EnsurePinPresent(outputPins, "Out", PinDirection.Output, PinType.Exec);
                    EnsurePinPresent(outputPins, "To", PinDirection.Output, PinType.Exec);
                    break;
            }
        }

        private static void EnsurePinPresent(List<Pin> pins, string pinName, PinDirection direction, PinType pinType)
        {
            if (pins.Any(pin =>
                    string.Equals(pin.Name, pinName, StringComparison.Ordinal) &&
                    pin.Direction == direction &&
                    pin.Type == pinType))
            {
                return;
            }

            pins.Add(new Pin(pinName, direction, pinType));
        }

        private BuiltExpression CreatePlaceholderExpressionNode(
            string title,
            BlueprintData blueprintData,
            string functionName,
            int functionBaseY,
            ref int localNodeIndex,
            int depth)
        {
            string definitionName = EnsureNodeDefinition(title, [], [new Pin("Value", PinDirection.Output, PinType.Data)]);
            SerializableNode placeholderNode = new()
            {
                Id = Guid.NewGuid(),
                DefinitionName = definitionName,
                Location = new Point(Math.Max(60, 980 - depth * 240), functionBaseY + localNodeIndex * 100)
            };
            placeholderNode.MetaData["NodeKind"] = "Placeholder";
            placeholderNode.MetaData["FunctionName"] = functionName;
            placeholderNode.MetaData["PlaceholderTitle"] = title;
            localNodeIndex++;
            blueprintData.Nodes.Add(placeholderNode);
            return new BuiltExpression(placeholderNode.Id, false, false, "Value");
        }

        private VisualExpressionSpec GetVisualExpressionSpec(JsonObject expressionObj)
        {
            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);

            if (TryGetMergedContextCallData(expressionObj, out JsonObject contextCallExpression, out string contextCallType))
            {
                List<(JsonObject Child, string PinName)> mergedChildren = [];
                List<string> mergedPinNames = [];
                if (expressionObj["ObjectExpression"] is JsonObject objectExpression &&
                    IsExpressionObject(objectExpression))
                {
                    mergedPinNames.Add("Target");
                    if (!IsTerminalExpression(objectExpression))
                        mergedChildren.Add((objectExpression, "Target"));
                }

                mergedChildren.AddRange(CollectChildExpressions(contextCallExpression));
                mergedPinNames.AddRange(CollectChildExpressionPinNames(contextCallExpression));

                return new VisualExpressionSpec(
                    contextCallExpression,
                    expressionObj,
                    mergedChildren,
                    mergedPinNames,
                    GetExpressionDisplayName(contextCallExpression, contextCallType),
                    contextCallType,
                    ExpressionHasExecInput(contextCallType),
                    ExpressionHasExecOutput(contextCallType),
                    GetDataOutputPinName(contextCallExpression, contextCallType),
                    ResolveEditableNodeText(contextCallExpression, contextCallType),
                    ResolveEditableConstantValue(contextCallExpression, contextCallType),
                    true,
                    true);
            }

            return new VisualExpressionSpec(
                expressionObj,
                null,
                CollectChildExpressions(expressionObj),
                CollectChildExpressionPinNames(expressionObj),
                GetExpressionDisplayName(expressionObj, expressionType),
                expressionType,
                ExpressionHasExecInput(expressionType),
                ExpressionHasExecOutput(expressionType),
                GetDataOutputPinName(expressionObj, expressionType),
                ResolveEditableNodeText(expressionObj, expressionType),
                ResolveEditableConstantValue(expressionObj, expressionType),
                false,
                IsCallLikeExpression(expressionObj));
        }

        private static bool IsContextWrapperExpressionType(string expressionType)
        {
            return expressionType is "Context" or "ClassContext" or "InterfaceContext";
        }

        private static bool TryGetMergedContextCallData(
            JsonObject expressionObj,
            out JsonObject contextCallExpression,
            out string contextCallType)
        {
            contextCallExpression = null!;
            contextCallType = string.Empty;

            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (!IsContextWrapperExpressionType(expressionType))
                return false;

            if (expressionObj["ContextExpression"] is not JsonObject callExpression ||
                !IsExpressionObject(callExpression) ||
                IsTerminalExpression(callExpression) ||
                !IsCallLikeExpression(callExpression))
            {
                return false;
            }

            contextCallExpression = callExpression;
            contextCallType = SimplifyExpressionType(callExpression["$type"]?.GetValue<string>() ?? string.Empty);
            return true;
        }

        private List<string> GetFunctionParameterNames(JsonObject exportObj)
        {
            List<string> names = [];
            JsonArray? loadedProperties = exportObj["LoadedProperties"] as JsonArray;
            if (loadedProperties == null) return names;

            foreach (JsonObject propertyObj in loadedProperties.OfType<JsonObject>())
            {
                string flags = propertyObj["PropertyFlags"]?.GetValue<string>() ?? string.Empty;
                if (!flags.Contains("CPF_Parm")) continue;

                string? name = propertyObj["Name"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            return names;
        }

        private (List<string> Parameters, List<string> ReturnValues) GetFunctionSignatureParts(JsonObject exportObj)
        {
            List<string> parameters = [];
            List<string> returnValues = [];
            JsonArray? loadedProperties = exportObj["LoadedProperties"] as JsonArray;
            if (loadedProperties == null) return (parameters, returnValues);

            foreach (JsonObject propertyObj in loadedProperties.OfType<JsonObject>())
            {
                string flags = propertyObj["PropertyFlags"]?.GetValue<string>() ?? string.Empty;
                if (!flags.Contains("CPF_Parm", StringComparison.Ordinal))
                    continue;

                string? name = propertyObj["Name"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (flags.Contains("CPF_ReturnParm", StringComparison.Ordinal))
                    returnValues.Add(name);
                else
                    parameters.Add(name);
            }

            return (parameters, returnValues);
        }

        private static (List<string> Parameters, List<string> ReturnValues) GetFunctionSignatureParts(FunctionExport export)
        {
            List<string> parameters = [];
            List<string> returnValues = [];
            FProperty[] loadedProperties = export.LoadedProperties ?? [];
            foreach (FProperty property in loadedProperties)
            {
                EPropertyFlags flags = property.PropertyFlags;
                if (!flags.HasFlag(EPropertyFlags.CPF_Parm))
                    continue;

                string name = property.Name?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (flags.HasFlag(EPropertyFlags.CPF_ReturnParm))
                    returnValues.Add(name);
                else
                    parameters.Add(name);
            }

            return (parameters, returnValues);
        }

        private List<(JsonObject Child, string PinName)> CollectChildExpressions(JsonObject expressionObj)
        {
            List<(JsonObject Child, string PinName)> children = [];
            foreach ((string key, JsonNode? value) in expressionObj)
            {
                if (key == "$type" || value == null) continue;

                if (value is JsonObject childObject && IsExpressionObject(childObject))
                {
                    if (IsTerminalExpression(childObject)) continue;
                    children.Add((childObject, NormalizePinName(key)));
                    continue;
                }

                if (value is JsonArray arrayValue)
                {
                    for (int i = 0; i < arrayValue.Count; i++)
                    {
                        if (arrayValue[i] is not JsonObject childExpr)
                            continue;
                        if (!IsExpressionObject(childExpr)) continue;
                        if (IsTerminalExpression(childExpr)) continue;
                        string pinName = key == "Parameters"
                            ? $"Arg{i + 1}"
                            : $"{NormalizePinName(key)}[{i}]";
                        children.Add((childExpr, pinName));
                    }
                }
            }

            return children;
        }

        private List<string> CollectChildExpressionPinNames(JsonObject expressionObj)
        {
            List<string> pinNames = [];
            foreach ((string key, JsonNode? value) in expressionObj)
            {
                if (key == "$type" || value == null) continue;

                if (value is JsonObject childObject && IsExpressionObject(childObject))
                {
                    if (!IsEndOfScriptExpression(childObject))
                        pinNames.Add(NormalizePinName(key));
                    continue;
                }

                if (value is not JsonArray arrayValue)
                    continue;

                for (int i = 0; i < arrayValue.Count; i++)
                {
                    if (arrayValue[i] is not JsonObject childExpr || !IsExpressionObject(childExpr))
                        continue;
                    if (IsEndOfScriptExpression(childExpr))
                        continue;

                    string pinName = key == "Parameters"
                        ? $"Arg{i + 1}"
                        : $"{NormalizePinName(key)}[{i}]";
                    pinNames.Add(pinName);
                }
            }

            return pinNames;
        }

        private static bool IsExpressionObject(JsonObject obj)
        {
            string type = obj["$type"]?.GetValue<string>() ?? string.Empty;
            return type.Contains("UAssetAPI.Kismet.Bytecode.Expressions.");
        }

        private static bool IsCallLikeExpression(JsonObject expressionObj)
        {
            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            return expressionType is "FinalFunction" or "LocalFinalFunction" or "CallMath" or "LocalVirtualFunction";
        }

        private static bool IsTerminalExpression(JsonObject expressionObj)
        {
            string type = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            return type is "EndOfScript" or "Nothing";
        }

        private static bool IsEndOfScriptExpression(JsonObject expressionObj)
        {
            string type = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            return type == "EndOfScript";
        }

        private static string SimplifyExpressionType(string fullType)
        {
            string typeName = fullType.Split(',')[0];
            int lastDot = typeName.LastIndexOf('.');
            if (lastDot >= 0) typeName = typeName[(lastDot + 1)..];
            if (typeName.StartsWith("EX_")) typeName = typeName[3..];
            return typeName;
        }

        private string GetExpressionDisplayName(JsonObject expressionObj, string expressionType)
        {
            return expressionType switch
            {
                "FinalFunction" or "LocalFinalFunction" or "CallMath"
                    => $"Call {ResolveStackNodeName(expressionObj["StackNode"])}",
                "LocalVirtualFunction"
                    => $"Call {expressionObj["VirtualFunctionName"]?.GetValue<string>() ?? "VirtualFunction"}",
                "LocalVariable" or "LocalOutVariable" or "InstanceVariable" or "DefaultVariable"
                    => "Get Variable",
                "Self" => "Self",
                "Let" or "LetBool" or "LetObj" or "LetWeakObjPtr" or "LetDelegate" or "LetMulticastDelegate" or "LetValueOnPersistentFrame"
                    => "Set Variable",
                "ObjectConst" => $"Object {ResolveObjectIndexName(expressionObj["Value"])}",
                "NoObject" => "No Object",
                "IntConst" or "IntConstByte" or "IntZero" or "IntOne" => "Int Const",
                "FloatConst" => "Float Const",
                "DoubleConst" => "Double Const",
                "ByteConst" => "Byte Const",
                "StringConst" => "String Const",
                "TextConst" => "Text Const",
                "NameConst" => "Name Const",
                "True" or "False" => "Bool Const",
                "Return" => "Return",
                "Jump" => "Jump",
                "JumpIfNot" => "Jump If Not",
                "ComputedJump" => "Computed Jump",
                "PushExecutionFlow" => "Push Flow",
                "PopExecutionFlowIfNot" => "Pop Flow If Not",
                "PopExecutionFlow" => "Pop Flow",
                "Context" or "ClassContext" or "InterfaceContext" => $"Context {ResolveContextChildName(expressionObj)}",
                _ => expressionType
            };
        }

        private bool TryResolveCallFunctionName(JsonObject expressionObj, string expressionType, out string functionName)
        {
            functionName = string.Empty;
            if (expressionType is "FinalFunction" or "LocalFinalFunction" or "CallMath")
            {
                string resolved = ResolveStackNodeName(expressionObj["StackNode"]);
                if (string.IsNullOrWhiteSpace(resolved) || resolved.StartsWith("#", StringComparison.Ordinal))
                    return false;
                functionName = resolved;
                return true;
            }

            if (expressionType == "LocalVirtualFunction")
            {
                string? virtualName = expressionObj["VirtualFunctionName"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(virtualName))
                    return false;
                functionName = virtualName;
                return true;
            }

            return false;
        }

        private List<(JsonObject Child, string PinName)> ApplyKnownCallParameterNames(
            string functionName,
            List<(JsonObject Child, string PinName)> childExpressions)
        {
            if (!knownFunctionParametersByName.TryGetValue(functionName, out List<string>? parameterNames) ||
                parameterNames.Count == 0)
            {
                return childExpressions;
            }

            List<(JsonObject Child, string PinName)> renamed = new(childExpressions.Count);
            HashSet<string> usedPins = new(StringComparer.Ordinal);
            foreach ((JsonObject child, string pinName) in childExpressions)
            {
                string targetPinName = pinName;
                if (pinName.StartsWith("Arg", StringComparison.Ordinal) &&
                    int.TryParse(pinName[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int argIndex) &&
                    argIndex > 0 &&
                    argIndex <= parameterNames.Count)
                {
                    string candidate = parameterNames[argIndex - 1];
                    if (!string.IsNullOrWhiteSpace(candidate))
                        targetPinName = candidate;
                }

                if (!usedPins.Add(targetPinName))
                {
                    int suffix = 2;
                    string unique = $"{targetPinName}_{suffix}";
                    while (!usedPins.Add(unique))
                    {
                        suffix++;
                        unique = $"{targetPinName}_{suffix}";
                    }
                    targetPinName = unique;
                }

                renamed.Add((child, targetPinName));
            }

            return renamed;
        }

        private List<string> ApplyKnownCallParameterNames(string functionName, List<string> pinNames)
        {
            if (!knownFunctionParametersByName.TryGetValue(functionName, out List<string>? parameterNames) ||
                parameterNames.Count == 0)
            {
                return pinNames;
            }

            List<string> renamed = new(pinNames.Count);
            HashSet<string> usedPins = new(StringComparer.Ordinal);
            foreach (string pinName in pinNames)
            {
                string targetPinName = pinName;
                if (pinName.StartsWith("Arg", StringComparison.Ordinal) &&
                    int.TryParse(pinName[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int argIndex) &&
                    argIndex > 0 &&
                    argIndex <= parameterNames.Count)
                {
                    string candidate = parameterNames[argIndex - 1];
                    if (!string.IsNullOrWhiteSpace(candidate))
                        targetPinName = candidate;
                }

                if (!usedPins.Add(targetPinName))
                {
                    int suffix = 2;
                    string unique = $"{targetPinName}_{suffix}";
                    while (!usedPins.Add(unique))
                    {
                        suffix++;
                        unique = $"{targetPinName}_{suffix}";
                    }
                    targetPinName = unique;
                }

                renamed.Add(targetPinName);
            }

            return renamed;
        }

        private void RegisterFunctionDefinitions(string functionName, List<string> parameters, List<string> returnValues)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return;

            knownFunctionParametersByName[functionName] = [.. parameters];

            string eventDefinitionName = EnsureNodeDefinition(
                $"Event {functionName}",
                [],
                new[] { new Pin("Exec", PinDirection.Output, PinType.Exec) }
                    .Concat(parameters.Select(name => new Pin(name, PinDirection.Output, PinType.Data)))
                    .ToList());
            if (TryEnsureFunctionTemplateAvailable(functionName))
                ApplyFunctionTemplateMetadataToDefinition(eventDefinitionName, functionName);

            List<Pin> callInputs = [new Pin("In", PinDirection.Input, PinType.Exec), new Pin("Target", PinDirection.Input, PinType.Data)];
            callInputs.AddRange(parameters.Select(name => new Pin(name, PinDirection.Input, PinType.Data)));

            List<Pin> callOutputs = [new Pin("Out", PinDirection.Output, PinType.Exec)];
            if (returnValues.Count > 0)
                callOutputs.AddRange(returnValues.Select(name => new Pin(name, PinDirection.Output, PinType.Data)));
            else
                callOutputs.Add(new Pin("Result", PinDirection.Output, PinType.Data));

            string callDefinitionName = EnsureNodeDefinition($"Call {functionName}", callInputs, callOutputs);
            ApplyFunctionTemplateMetadataToDefinition(callDefinitionName, functionName);
        }

        private void RegisterExternalCallDefinition(ExternalCallSignature signature)
        {
            if (string.IsNullOrWhiteSpace(signature.Name))
                return;

            List<string> parameterNames = BuildExternalCallParameterNames(signature.Name, signature.ParameterCount);
            knownFunctionParametersByName[signature.Name] = [.. parameterNames];

            List<Pin> callInputs = [new Pin("In", PinDirection.Input, PinType.Exec), new Pin("Target", PinDirection.Input, PinType.Data)];
            callInputs.AddRange(parameterNames.Select(name => new Pin(name, PinDirection.Input, PinType.Data)));

            List<Pin> callOutputs =
            [
                new Pin("Out", PinDirection.Output, PinType.Exec),
                new Pin("Result", PinDirection.Output, PinType.Data)
            ];

            string callDefinitionName = EnsureNodeDefinition(
                $"Call {signature.Name}",
                callInputs,
                callOutputs,
                signature.RepresentativeTemplateJson,
                signature.RepresentativeExpressionType);
            ApplyExternalCallTemplateMetadataToDefinition(callDefinitionName, signature);
        }

        private List<string> BuildExternalCallParameterNames(string functionName, int parameterCount)
        {
            int targetCount = Math.Max(0, parameterCount);
            if (knownFunctionParametersByName.TryGetValue(functionName, out List<string>? existingNames) &&
                existingNames != null &&
                existingNames.Count > 0)
            {
                List<string> merged = [];
                int desiredCount = Math.Max(targetCount, existingNames.Count);
                for (int i = 0; i < desiredCount; i++)
                {
                    string candidate = i < existingNames.Count ? existingNames[i] : string.Empty;
                    merged.Add(string.IsNullOrWhiteSpace(candidate) ? $"Arg{i + 1}" : candidate);
                }
                return merged;
            }

            return Enumerable
                .Range(1, targetCount)
                .Select(i => $"Arg{i}")
                .ToList();
        }

        private Dictionary<int, string> BuildBytecodeObjectNameMap(JsonNode? root)
        {
            Dictionary<int, string> map = [];

            JsonArray? exports = root?["Exports"] as JsonArray;
            if (exports != null)
            {
                for (int i = 0; i < exports.Count; i++)
                {
                    JsonObject? exportObj = exports[i] as JsonObject;
                    string? objectName = exportObj?["ObjectName"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(objectName))
                        map[i + 1] = objectName;
                }
            }

            JsonArray? imports = root?["Imports"] as JsonArray;
            if (imports != null)
            {
                for (int i = 0; i < imports.Count; i++)
                {
                    JsonObject? importObj = imports[i] as JsonObject;
                    string? objectName = importObj?["ObjectName"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(objectName))
                        map[-(i + 1)] = objectName;
                }
            }

            return map;
        }

        private string EnsureNodeDefinition(
            string baseName,
            List<Pin> inputPins,
            List<Pin> outputPins,
            string? sourceExpressionTemplateJson = null,
            string? sourceExpressionType = null)
        {
            string signatureKey = BuildNodeDefinitionSignature(baseName, inputPins, outputPins);
            if (nodeDefinitionNameBySignature.TryGetValue(signatureKey, out string? cachedName) &&
                nodeDefinitions.TryGetValue(cachedName, out NodeDefinition? cachedDef) &&
                PinsMatch(cachedDef.InputPins, inputPins) &&
                PinsMatch(cachedDef.OutputPins, outputPins))
            {
                BackfillNodeDefinitionTemplate(cachedDef, sourceExpressionTemplateJson, sourceExpressionType);
                return cachedName;
            }

            if (nodeDefinitions.TryGetValue(baseName, out NodeDefinition? existing) &&
                PinsMatch(existing.InputPins, inputPins) &&
                PinsMatch(existing.OutputPins, outputPins))
            {
                BackfillNodeDefinitionTemplate(existing, sourceExpressionTemplateJson, sourceExpressionType);
                nodeDefinitionNameBySignature[signatureKey] = baseName;
                return baseName;
            }

            if (!nodeDefinitions.ContainsKey(baseName))
            {
                nodeDefinitions[baseName] = new NodeDefinition
                {
                    Name = baseName,
                    InputPins = inputPins.Select(ClonePin).ToList(),
                    OutputPins = outputPins.Select(ClonePin).ToList(),
                    SourceExpressionTemplateJson = sourceExpressionTemplateJson,
                    SourceExpressionType = sourceExpressionType
                };
                nodeDefinitionNameBySignature[signatureKey] = baseName;
                nextNodeDefinitionSuffixByBaseName[baseName] = 2;
                return baseName;
            }

            int suffix = 2;
            if (nextNodeDefinitionSuffixByBaseName.TryGetValue(baseName, out int cachedSuffix))
                suffix = Math.Max(2, cachedSuffix);

            string finalName = $"{baseName} #{suffix}";
            while (nodeDefinitions.TryGetValue(finalName, out NodeDefinition? conflict) &&
                (!PinsMatch(conflict.InputPins, inputPins) || !PinsMatch(conflict.OutputPins, outputPins)))
            {
                suffix++;
                finalName = $"{baseName} #{suffix}";
            }

            if (!nodeDefinitions.ContainsKey(finalName))
            {
                nodeDefinitions[finalName] = new NodeDefinition
                {
                    Name = finalName,
                    InputPins = inputPins.Select(ClonePin).ToList(),
                    OutputPins = outputPins.Select(ClonePin).ToList(),
                    SourceExpressionTemplateJson = sourceExpressionTemplateJson,
                    SourceExpressionType = sourceExpressionType
                };
            }
            else
            {
                BackfillNodeDefinitionTemplate(nodeDefinitions[finalName], sourceExpressionTemplateJson, sourceExpressionType);
            }

            nodeDefinitionNameBySignature[signatureKey] = finalName;
            nextNodeDefinitionSuffixByBaseName[baseName] = suffix + 1;
            return finalName;
        }

        private static void BackfillNodeDefinitionTemplate(NodeDefinition definition, string? sourceExpressionTemplateJson, string? sourceExpressionType)
        {
            bool shouldRefreshCallTemplate =
                !string.IsNullOrWhiteSpace(sourceExpressionTemplateJson) &&
                GetBaseDefinitionName(definition.Name).StartsWith("Call ", StringComparison.Ordinal);

            if ((string.IsNullOrWhiteSpace(definition.SourceExpressionTemplateJson) || shouldRefreshCallTemplate) &&
                !string.IsNullOrWhiteSpace(sourceExpressionTemplateJson))
            {
                definition.SourceExpressionTemplateJson = sourceExpressionTemplateJson;
            }

            if ((string.IsNullOrWhiteSpace(definition.SourceExpressionType) || shouldRefreshCallTemplate) &&
                !string.IsNullOrWhiteSpace(sourceExpressionType))
            {
                definition.SourceExpressionType = sourceExpressionType;
            }
        }

        private static string BuildNodeDefinitionSignature(string baseName, List<Pin> inputPins, List<Pin> outputPins)
        {
            string inputSig = string.Join("|", inputPins.Select(p => $"{p.Name}:{p.Direction}:{p.Type}"));
            string outputSig = string.Join("|", outputPins.Select(p => $"{p.Name}:{p.Direction}:{p.Type}"));
            return $"{baseName}||I:{inputSig}||O:{outputSig}";
        }

        private static bool PinsMatch(List<Pin> left, List<Pin> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Name != right[i].Name ||
                    left[i].Direction != right[i].Direction ||
                    left[i].Type != right[i].Type)
                {
                    return false;
                }
            }

            return true;
        }

        private static Pin ClonePin(Pin pin) => new(pin.Name, pin.Direction, pin.Type);

        private static bool ExpressionHasExecInput(string expressionType) =>
            !IsPureDataExpressionType(expressionType) &&
            expressionType is not "LocalVariable" and not "LocalOutVariable" and not "InstanceVariable" and not "DefaultVariable" and not "Self" and
            not "ObjectConst" and not "NoObject" and not "IntConst" and not "IntConstByte" and not "IntZero" and not "IntOne" and
            not "FloatConst" and not "DoubleConst" and not "ByteConst" and not "StringConst" and not "TextConst" and
            not "NameConst" and not "True" and not "False" and not "Nothing" and not "EndOfScript";

        private static bool ExpressionHasExecOutput(string expressionType) =>
            !IsPureDataExpressionType(expressionType) &&
            expressionType is not "LocalVariable" and not "LocalOutVariable" and not "InstanceVariable" and not "DefaultVariable" and not "Self" and
            not "ObjectConst" and not "NoObject" and not "IntConst" and not "IntConstByte" and not "IntZero" and not "IntOne" and
            not "FloatConst" and not "DoubleConst" and not "ByteConst" and not "StringConst" and not "TextConst" and
            not "NameConst" and not "True" and not "False" and not "Nothing" and not "EndOfScript";

        private static string? GetDataOutputPinName(JsonObject expressionObj, string expressionType)
        {
            return expressionType switch
            {
                _ when IsPureDataExpressionType(expressionType) => "Value",
                "LocalVariable" or "LocalOutVariable" or "InstanceVariable" or "DefaultVariable" or "Self" => "Value",
                "ObjectConst" or "NoObject" => "Value",
                "IntConst" or "IntConstByte" or "IntZero" or "IntOne" => "Value",
                "FloatConst" or "DoubleConst" or "ByteConst" or "StringConst" or "TextConst" or "NameConst" or "True" or "False" => "Value",
                "FinalFunction" or "LocalFinalFunction" or "CallMath" or "LocalVirtualFunction" => "Result",
                "Context" or "ClassContext" or "InterfaceContext" => "Result",
                "Let" or "LetBool" or "LetObj" or "LetWeakObjPtr" or "LetDelegate" or "LetMulticastDelegate" or "LetValueOnPersistentFrame" => "Value",
                _ when (expressionObj["Value"] != null) => "Value",
                _ => null
            };
        }

        private static bool IsPureDataExpressionType(string expressionType) =>
            expressionType is "SwitchValue" or "FKismetSwitchCase";

        private string ResolveStackNodeName(JsonNode? stackNode)
        {
            if (stackNode == null) return "Function";
            if (stackNode is JsonValue value && value.TryGetValue<int>(out int intValue))
                return ResolveIndexedObjectName(intValue);
            return stackNode.ToJsonString();
        }

        private static string ResolveFieldPathName(JsonNode? variableNode)
        {
            JsonArray? pathArray = variableNode?["New"]?["Path"] as JsonArray;
            if (pathArray != null && pathArray.Count > 0)
            {
                string joined = string.Join(".", pathArray.Select(node => node?.GetValue<string>() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(joined)) return joined;
            }

            return "Variable";
        }

        private static string ResolveAssignmentTargetName(JsonObject expressionObj)
        {
            string fieldTarget = ResolveFieldPathName(expressionObj["DestinationProperty"]);
            if (fieldTarget != "Variable") return fieldTarget;

            fieldTarget = ResolveVariableExpressionName(expressionObj["Variable"]);
            if (fieldTarget != "Variable") return fieldTarget;

            fieldTarget = ResolveVariableExpressionName(expressionObj["VariableExpression"]);
            if (fieldTarget != "Variable") return fieldTarget;

            if (expressionObj["Value"]?["New"]?["Path"] is JsonArray pathArray && pathArray.Count > 0)
            {
                string joined = string.Join(".", pathArray.Select(node => node?.GetValue<string>() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(joined)) return joined;
            }

            return "Variable";
        }

        private static string ResolveVariableExpressionName(JsonNode? variableExpressionNode)
        {
            if (variableExpressionNode is not JsonObject variableExpressionObj)
                return "Variable";

            string variableName = ResolveFieldPathName(variableExpressionObj["Variable"]);
            if (variableName != "Variable") return variableName;

            variableName = ResolveFieldPathName(variableExpressionObj);
            return variableName;
        }

        private static string ResolveConstDisplayValue(JsonObject expressionObj, string expressionType)
        {
            if (expressionType == "IntZero") return "0";
            if (expressionType == "IntOne") return "1";
            if (expressionType == "True") return "True";
            if (expressionType == "False") return "False";

            JsonNode? value = expressionObj["Value"];
            if (value == null) return expressionType;
            return value.ToJsonString().Trim('"');
        }

        private string ResolveContextChildName(JsonObject expressionObj)
        {
            JsonObject? contextExpression = expressionObj["ContextExpression"] as JsonObject;
            if (contextExpression == null) return "Context";

            string contextType = SimplifyExpressionType(contextExpression["$type"]?.GetValue<string>() ?? string.Empty);
            return contextType switch
            {
                "FinalFunction" or "LocalFinalFunction" or "CallMath" => ResolveStackNodeName(contextExpression["StackNode"]),
                "LocalVirtualFunction" => contextExpression["VirtualFunctionName"]?.GetValue<string>() ?? "VirtualFunction",
                _ => contextType
            };
        }

        private string ResolveObjectIndexName(JsonNode? valueNode)
        {
            if (valueNode == null) return "Object";
            if (valueNode is JsonValue value && value.TryGetValue<int>(out int intValue))
                return ResolveIndexedObjectName(intValue);
            return valueNode.ToJsonString();
        }

        private string ResolveIndexedObjectName(int index)
        {
            if (bytecodeObjectNameMap.TryGetValue(index, out string? resolvedName) && !string.IsNullOrWhiteSpace(resolvedName))
                return resolvedName;
            return $"#{index}";
        }

        private static object? ResolveEditableConstantValue(JsonObject expressionObj, string expressionType)
        {
            return expressionType switch
            {
                "IntConst" or "IntConstByte" => expressionObj["Value"]?.GetValue<int>(),
                "IntZero" => 0,
                "IntOne" => 1,
                "FloatConst" => expressionObj["Value"]?.GetValue<float>(),
                "DoubleConst" => expressionObj["Value"]?.GetValue<double>(),
                "ByteConst" => expressionObj["Value"]?.ToJsonString().Trim('"'),
                "StringConst" or "NameConst" => expressionObj["Value"]?.GetValue<string>(),
                "TextConst" => ResolveEditableTextConstValue(expressionObj),
                "True" => true,
                "False" => false,
                _ => null
            };
        }

        private static string? ResolveEditableTextConstValue(JsonObject expressionObj)
        {
            JsonObject? scriptText = expressionObj["Value"] as JsonObject;
            if (scriptText == null) return null;

            string? localizedSource = scriptText["LocalizedSource"]?["Value"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(localizedSource)) return localizedSource;

            if (scriptText["LiteralString"] is JsonObject literalExpression)
            {
                string? nestedLiteral = literalExpression["Value"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(nestedLiteral)) return nestedLiteral;
            }
            else
            {
                string? literal = scriptText["LiteralString"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(literal)) return literal;
            }

            string? invariant = scriptText["InvariantLiteralString"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(invariant)) return invariant;

            return null;
        }

        private static void ApplyTextConstEdit(JsonObject expressionObj, string editedText)
        {
            if (editedText == null) return;
            if (expressionObj["Value"] is not JsonObject scriptText) return;

            if (scriptText["LocalizedSource"] is JsonObject localizedSource)
            {
                localizedSource["Value"] = editedText;
                return;
            }

            if (scriptText["LiteralString"] is JsonObject literalExpression)
            {
                literalExpression["Value"] = editedText;
                return;
            }

            if (scriptText["LiteralString"] != null)
            {
                scriptText["LiteralString"] = editedText;
                return;
            }

            scriptText["InvariantLiteralString"] = editedText;
        }

        private static string NormalizePinName(string key)
        {
            return key switch
            {
                "ObjectExpression" => "Target",
                "ContextExpression" => "Context",
                "AssignmentExpression" => "Value",
                "VariableExpression" => "Variable",
                "Expression" => "Value",
                "BooleanExpression" => "Condition",
                "CodeOffsetExpression" => "Offset",
                "ReturnExpression" => "Return",
                _ => key
            };
        }

        private static string? ResolveEditableNodeText(JsonObject expressionObj, string expressionType)
        {
            return expressionType switch
            {
                "LocalVirtualFunction"
                    => expressionObj["VirtualFunctionName"]?.GetValue<string>(),
                "LocalVariable" or "LocalOutVariable" or "InstanceVariable" or "DefaultVariable"
                    => ResolveFieldPathName(expressionObj["Variable"]),
                "Let" or "LetBool" or "LetObj" or "LetWeakObjPtr" or "LetDelegate" or "LetMulticastDelegate" or "LetValueOnPersistentFrame"
                    => ResolveAssignmentTargetName(expressionObj),
                "TextConst" => ResolveEditableTextConstValue(expressionObj),
                _ => null
            };
        }

        private static bool ShouldHideChildPin(string expressionType, string pinName, string? editableNodeText)
        {
            if (string.IsNullOrWhiteSpace(editableNodeText)) return false;
            return expressionType switch
            {
                "Let" or "LetBool" or "LetObj" or "LetWeakObjPtr" or "LetDelegate" or "LetMulticastDelegate" or "LetValueOnPersistentFrame"
                    => pinName == "Variable",
                _ => false
            };
        }

        private static bool IsGenericVariableDefinitionName(string definitionName)
        {
            string baseDefinitionName = GetBaseDefinitionName(definitionName);
            return string.Equals(baseDefinitionName, "Get Variable", StringComparison.Ordinal) ||
                string.Equals(baseDefinitionName, "Set Variable", StringComparison.Ordinal);
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                button1_Click(null, null);
            }
        }

        private void uAssetJsonToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "UAsset Json（*.json）|*.json|所有文件（*.*）|*.*",
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string json = BuildSelectedEditedAssetJson();
                    File.WriteAllText(dialog.FileName, json);
                    label1.Text = "导出成功";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    label1.Text = "导出失败";
                }
            }
        }

        private void uAssetFileuassetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "UAsset File（*.uasset）|*.uasset|所有文件（*.*）|*.*",
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string json = BuildSelectedEditedAssetJson();
                    UAsset asset = UAsset.DeserializeJson(json);
                    asset.Mappings = new(UsmapPath);
                    asset.SetEngineVersion(exportEngineVersion);
                    EnsureTypedAssetNameMapCoverage(asset);
                    asset.Write(dialog.FileName);
                    label1.Text = "导出成功";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    label1.Text = "导出失败";
                }
            }
        }

        private void ImportNodeDefToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Filter = "定义文件（*.json）|*.json|所有文件（*.*）|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = "打开定义文件"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    Dictionary<string, NodeDefinition>? importedDefinitions = null;
                    Dictionary<string, List<string>> importedKnownParameters = new(StringComparer.Ordinal);
                    Dictionary<string, string> importedFunctionPaths = new(StringComparer.Ordinal);
                    Dictionary<string, BlueprintFunctionTemplate> importedFunctionTemplates = new(StringComparer.Ordinal);
                    Dictionary<string, DataPropertyTemplate> importedDataTemplates = new(StringComparer.Ordinal);

                    NodeDefinitionCachePackage? package = JsonSerializer.Deserialize<NodeDefinitionCachePackage>(json);
                    if (package?.NodeDefinitions != null && package.NodeDefinitions.Count > 0)
                    {
                        importedDefinitions = package.NodeDefinitions;
                        if (package.KnownFunctionParametersByName != null)
                            importedKnownParameters = package.KnownFunctionParametersByName;
                        if (package.FunctionDefinitionAssetPathByName != null)
                            importedFunctionPaths = package.FunctionDefinitionAssetPathByName;
                        if (package.FunctionTemplatesByName != null)
                            importedFunctionTemplates = package.FunctionTemplatesByName;
                        if (package.DataTemplatesByName != null)
                            importedDataTemplates = package.DataTemplatesByName;
                    }
                    else
                    {
                        importedDefinitions = JsonSerializer.Deserialize<Dictionary<string, NodeDefinition>>(json);
                    }

                    if (importedDefinitions == null || importedDefinitions.Count == 0)
                        throw new InvalidDataException("定义文件为空，或格式无法识别。");

                    foreach (string deprecatedName in importedDefinitions.Keys
                        .Where(IsDeprecatedGenericCallOrEventDefinition)
                        .ToList())
                    {
                        importedDefinitions.Remove(deprecatedName);
                    }

                    nodeDefinitions = importedDefinitions;
                    RebuildNodeDefinitionRuntimeCaches(importedKnownParameters, importedFunctionPaths, importedFunctionTemplates);
                    dataTemplatesByName.Clear();
                    foreach (DataPropertyTemplate dataTemplate in importedDataTemplates.Values)
                    {
                        MergeDataTemplate(dataTemplatesByName, dataTemplate);
                    }
                    MarkDataLibraryTemplatesChanged();
                    UpdateNodeLibrary(textBox1.Text.Trim());
                    UpdateDataLibrary(textBox2.Text.Trim());

                    nodeLibrary.Refresh();
                    dataLibrary.Refresh();

                    label1.Text = $"导入成功（{nodeDefinitions.Count} 个定义，{dataTemplatesByName.Count} 个Data模板）";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入定义文件失败：{ex}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    label1.Text = "导入定义文件失败";
                }
            }
        }

        private void ExportNodeDefToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "定义文件（*.json）|*.json|所有文件（*.*）|*.*",
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    NodeDefinitionCachePackage package = new()
                    {
                        NodeDefinitions = nodeDefinitions.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                        KnownFunctionParametersByName = knownFunctionParametersByName.ToDictionary(
                            kv => kv.Key,
                            kv => new List<string>(kv.Value),
                            StringComparer.Ordinal),
                        FunctionDefinitionAssetPathByName = functionDefinitionAssetPathByName.ToDictionary(
                            kv => kv.Key,
                            kv => kv.Value,
                            StringComparer.Ordinal),
                        FunctionTemplatesByName = functionTemplatesByName.ToDictionary(
                            kv => kv.Key,
                            kv => BlueprintModelCloner.Clone(kv.Value),
                            StringComparer.Ordinal),
                        DataTemplatesByName = dataTemplatesByName.ToDictionary(
                            kv => kv.Key,
                            kv => BlueprintModelCloner.Clone(kv.Value),
                            StringComparer.Ordinal)
                    };
                    string json = JsonSerializer.Serialize(package, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(dialog.FileName, json);
                    label1.Text = "导出成功";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出定义文件失败：{ex}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    label1.Text = "导出定义文件失败";
                }
            }
        }

        private static JsonArray EnsureImportsArray(JsonObject root)
        {
            if (root["Imports"] is JsonArray imports)
                return imports;

            JsonArray created = [];
            root["Imports"] = created;
            return created;
        }

        private static void EnsureDependsMapLength(JsonObject root, int exportCount)
        {
            JsonArray depends = root["DependsMap"] as JsonArray ?? [];
            while (depends.Count < exportCount)
            {
                depends.Add(new JsonArray());
            }
            while (depends.Count > exportCount)
            {
                depends.RemoveAt(depends.Count - 1);
            }
            root["DependsMap"] = depends;
        }

        private void AutoRepairImportExportReferences(
            JsonObject root,
            JsonArray exports,
            JsonArray imports,
            BlueprintData blueprintData,
            IReadOnlyDictionary<Guid, JsonObject> rebuiltExpressionByNodeId)
        {
            EnsureDependsMapLength(root, exports.Count);
            HashSet<string> additionalNames = CollectBlueprintFunctionNamesForTables(blueprintData);
            EnsureNameMapContains(root, additionalNames);

            Dictionary<string, int> exportIndexByName = new(StringComparer.Ordinal);
            for (int i = 0; i < exports.Count; i++)
            {
                if (exports[i] is not JsonObject exportObj)
                    continue;
                string? objectName = exportObj["ObjectName"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(objectName) || exportIndexByName.ContainsKey(objectName))
                    continue;
                exportIndexByName[objectName] = i + 1;
            }

            Dictionary<string, int> functionImportIndexByName = new(StringComparer.Ordinal);
            Dictionary<string, int> classImportIndexByName = new(StringComparer.Ordinal);
            int firstFunctionOuterIndex = 0;
            for (int i = 0; i < imports.Count; i++)
            {
                if (imports[i] is not JsonObject importObj)
                    continue;

                string objectName = importObj["ObjectName"]?.GetValue<string>() ?? string.Empty;
                string className = importObj["ClassName"]?.GetValue<string>() ?? string.Empty;
                int index = -(i + 1);

                if (string.Equals(className, "Function", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(objectName))
                {
                    functionImportIndexByName.TryAdd(objectName, index);
                    if (firstFunctionOuterIndex == 0 &&
                        importObj["OuterIndex"] is JsonValue outerValue &&
                        outerValue.TryGetValue<int>(out int parsedOuter))
                    {
                        firstFunctionOuterIndex = parsedOuter;
                    }
                }
                else if (string.Equals(className, "Class", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(objectName))
                {
                    classImportIndexByName.TryAdd(objectName, index);
                }
            }

            foreach (string functionName in additionalNames)
            {
                _ = EnsureFunctionObjectIndex(
                    functionName,
                    exports,
                    imports,
                    exportIndexByName,
                    functionImportIndexByName,
                    classImportIndexByName,
                    firstFunctionOuterIndex);
            }

            foreach (SerializableNode node in blueprintData.Nodes)
            {
                if (!rebuiltExpressionByNodeId.TryGetValue(node.Id, out JsonObject? expressionObj))
                    continue;

                string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
                if (expressionType is not "FinalFunction" and not "LocalFinalFunction" and not "CallMath")
                    continue;

                if (!TryResolveCallFunctionNameForRepair(node, expressionObj, exportIndexByName, functionImportIndexByName, out string functionName))
                    continue;

                int currentObjectIndex = 0;
                string currentFunctionName = string.Empty;
                bool hasCurrentObjectIndex = false;
                if (expressionObj["StackNode"] is JsonValue stackNodeValue &&
                    stackNodeValue.TryGetValue<int>(out int objectIndex))
                {
                    currentObjectIndex = objectIndex;
                    hasCurrentObjectIndex = true;
                    currentFunctionName = ResolveFunctionNameByObjectIndex(objectIndex, exportIndexByName, functionImportIndexByName);
                }

                int repairedIndex = EnsureFunctionObjectIndex(
                    functionName,
                    exports,
                    imports,
                    exportIndexByName,
                    functionImportIndexByName,
                    classImportIndexByName,
                    firstFunctionOuterIndex);

                bool needsRepair =
                    !hasCurrentObjectIndex ||
                    !IsValidObjectIndex(currentObjectIndex, exports.Count, imports.Count) ||
                    repairedIndex != currentObjectIndex ||
                    (!string.IsNullOrWhiteSpace(currentFunctionName) &&
                     !string.Equals(currentFunctionName, functionName, StringComparison.Ordinal));

                if (!needsRepair)
                    continue;

                expressionObj["StackNode"] = repairedIndex;
            }
        }

        private static bool IsValidObjectIndex(int index, int exportCount, int importCount)
        {
            if (index > 0) return index <= exportCount;
            if (index < 0) return -index <= importCount;
            return false;
        }

        private static bool TryResolveCallFunctionNameForRepair(
            SerializableNode node,
            JsonObject expressionObj,
            IReadOnlyDictionary<string, int> exportIndexByName,
            IReadOnlyDictionary<string, int> functionImportIndexByName,
            out string functionName)
        {
            functionName = ResolveCallFunctionNameFromNode(node);
            if (!string.IsNullOrWhiteSpace(functionName) && !string.Equals(functionName, "Function", StringComparison.Ordinal))
                return true;

            if (expressionObj["StackNode"] is JsonValue stackNodeValue &&
                stackNodeValue.TryGetValue<int>(out int objectIndex))
            {
                functionName = ResolveFunctionNameByObjectIndex(objectIndex, exportIndexByName, functionImportIndexByName);
                if (!string.IsNullOrWhiteSpace(functionName))
                    return true;
            }

            return false;
        }

        private static string ResolveFunctionNameByObjectIndex(
            int objectIndex,
            IReadOnlyDictionary<string, int> exportIndexByName,
            IReadOnlyDictionary<string, int> functionImportIndexByName)
        {
            if (objectIndex > 0)
            {
                foreach ((string name, int idx) in exportIndexByName)
                {
                    if (idx == objectIndex)
                        return name;
                }
            }
            else if (objectIndex < 0)
            {
                foreach ((string name, int idx) in functionImportIndexByName)
                {
                    if (idx == objectIndex)
                        return name;
                }
            }

            return string.Empty;
        }

        private int EnsureFunctionObjectIndex(
            string functionName,
            JsonArray exports,
            JsonArray imports,
            IDictionary<string, int> exportIndexByName,
            IDictionary<string, int> functionImportIndexByName,
            IDictionary<string, int> classImportIndexByName,
            int firstFunctionOuterIndex)
        {
            if (exportIndexByName.TryGetValue(functionName, out int exportIndex))
                return exportIndex;

            if (functionImportIndexByName.TryGetValue(functionName, out int importIndex))
                return importIndex;

            int outerIndex = ResolveFunctionOuterIndex(functionName, classImportIndexByName, firstFunctionOuterIndex);
            JsonObject newImport = new()
            {
                ["$type"] = "UAssetAPI.Import, UAssetAPI",
                ["ObjectName"] = functionName,
                ["OuterIndex"] = outerIndex,
                ["ClassPackage"] = "/Script/CoreUObject",
                ["ClassName"] = "Function",
                ["PackageName"] = null,
                ["bImportOptional"] = false
            };

            imports.Add(newImport);
            int newIndex = -imports.Count;
            functionImportIndexByName[functionName] = newIndex;
            return newIndex;
        }

        private static int ResolveFunctionOuterIndex(
            string functionName,
            IDictionary<string, int> classImportIndexByName,
            int fallbackOuterIndex)
        {
            if (functionName.StartsWith("Array_", StringComparison.Ordinal) &&
                classImportIndexByName.TryGetValue("KismetArrayLibrary", out int arrayLibIndex))
            {
                return arrayLibIndex;
            }

            if ((functionName.Contains("_Int", StringComparison.Ordinal) ||
                 functionName.Contains("_Float", StringComparison.Ordinal) ||
                 functionName.Contains("Equal", StringComparison.Ordinal) ||
                 functionName.Contains("Less", StringComparison.Ordinal) ||
                 functionName.Contains("Greater", StringComparison.Ordinal)) &&
                classImportIndexByName.TryGetValue("KismetMathLibrary", out int mathLibIndex))
            {
                return mathLibIndex;
            }

            string? ownerClassName = null;
            int underscore = functionName.IndexOf('_');
            if (underscore > 0)
            {
                ownerClassName = functionName[..underscore];
            }

            if (!string.IsNullOrWhiteSpace(ownerClassName) &&
                classImportIndexByName.TryGetValue(ownerClassName, out int ownerClassIndex))
            {
                return ownerClassIndex;
            }

            if (fallbackOuterIndex != 0)
                return fallbackOuterIndex;

            return 0;
        }

        private static void EnsureNameMapContains(JsonObject root, IEnumerable<string> names)
        {
            if (root["NameMap"] is not JsonArray nameMap)
                return;

            HashSet<string> existing = new(nameMap.OfType<JsonValue>()
                .Select(v => v.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))!,
                StringComparer.Ordinal);

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || !existing.Add(name))
                    continue;
                nameMap.Add(name);
            }
        }

        private static void EnsureNameMapCoverageFromUAssetJson(JsonObject root, JsonArray exports, JsonArray imports)
        {
            HashSet<string> names = new(StringComparer.Ordinal);

            foreach (JsonObject exportObj in exports.OfType<JsonObject>())
            {
                CollectPotentialFNames(exportObj, null, names);
                if (exportObj["ScriptBytecode"] is JsonArray scriptArray)
                {
                    foreach (JsonNode? expressionNode in scriptArray)
                    {
                        CollectPotentialFNames(expressionNode, "ScriptBytecode", names);
                    }
                }
            }

            foreach (JsonObject importObj in imports.OfType<JsonObject>())
            {
                CollectPotentialFNames(importObj, null, names);
            }

            EnsureNameMapContains(root, names);
        }

        private static void CollectPotentialFNames(JsonNode? node, string? parentKey, ISet<string> names)
        {
            if (node is JsonObject obj)
            {
                foreach ((string key, JsonNode? child) in obj)
                {
                    if (child is JsonValue childValue &&
                        childValue.TryGetValue<string>(out string? strVal) &&
                        ShouldTreatAsFNameKey(key) &&
                        IsLikelyFNameValue(strVal))
                    {
                        names.Add(strVal);
                    }

                    if (string.Equals(key, "Path", StringComparison.Ordinal) && child is JsonArray pathArray)
                    {
                        foreach (JsonValue pathValue in pathArray.OfType<JsonValue>())
                        {
                            if (!pathValue.TryGetValue<string>(out string? pathName))
                                continue;
                            if (IsLikelyFNameValue(pathName))
                                names.Add(pathName);
                        }
                    }

                    CollectPotentialFNames(child, key, names);
                }
                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    CollectPotentialFNames(child, parentKey, names);
                }
            }
        }

        private static bool ShouldTreatAsFNameKey(string key)
        {
            return key is
                "Name" or
                "ObjectName" or
                "ClassName" or
                "ClassPackage" or
                "PackageName" or
                "VirtualFunctionName" or
                "FunctionName" or
                "StructType" or
                "EnumType" or
                "InnerType" or
                "PropertyName" or
                "PropertyClass" or
                "ValueName" or
                "FieldName" or
                "EnumName";
        }

        private static bool IsLikelyFNameValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (value.Length > 256)
                return false;
            if (value.Contains(',', StringComparison.Ordinal))
                return false;
            if (value.Contains("UAssetAPI.", StringComparison.Ordinal))
                return false;
            return true;
        }

        private static HashSet<string> CollectBlueprintFunctionNamesForTables(BlueprintData blueprintData)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (SerializableNode node in blueprintData.Nodes)
            {
                if (TryResolveEntryFunctionName(node, out string entryFunction))
                {
                    names.Add(entryFunction);
                    continue;
                }

                string callName = ResolveCallFunctionNameFromNode(node);
                if (!string.IsNullOrWhiteSpace(callName) && !string.Equals(callName, "Function", StringComparison.Ordinal))
                    names.Add(callName);
            }
            return names;
        }

        private static void EnsureTypedAssetNameMapCoverage(UAsset asset)
        {
            if (asset == null)
                return;

            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            CollectTypedAssetNameCandidates(asset, asset, visited);
            RebindAllAssetFNames(asset);
            NormalizeInvalidEnumFallbackFNames(asset);
        }

        private static void CollectTypedAssetNameCandidates(object? value, UAsset asset, ISet<object> visited)
        {
            if (value == null)
                return;

            Type type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || value is decimal || value is Guid)
                return;

            if (value is FName fname)
            {
                string? nameText = TryGetFNameText(fname);
                if (!string.IsNullOrWhiteSpace(nameText))
                    _ = asset.AddNameReference(FString.FromString(nameText));
                return;
            }

            if (value is FString fstring)
                return;

            if (!type.IsValueType && !visited.Add(value))
                return;

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    CollectTypedAssetNameCandidates(entry.Key, asset, visited);
                    CollectTypedAssetNameCandidates(entry.Value, asset, visited);
                }
                return;
            }

            if (value is System.Collections.IEnumerable enumerable && value is not JsonNode)
            {
                foreach (object? item in enumerable)
                {
                    CollectTypedAssetNameCandidates(item, asset, visited);
                }
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                object? childValue;
                try
                {
                    childValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }

                CollectTypedAssetNameCandidates(childValue, asset, visited);
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object? childValue;
                try
                {
                    childValue = field.GetValue(value);
                }
                catch
                {
                    continue;
                }

                CollectTypedAssetNameCandidates(childValue, asset, visited);
            }
        }

        private static void RebindAllAssetFNames(UAsset asset)
        {
            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            RebindAllAssetFNamesRecursive(asset, asset, visited);
        }

        private static void RebindAllAssetFNamesRecursive(object? value, UAsset asset, ISet<object> visited)
        {
            if (value == null)
                return;

            Type type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || value is decimal || value is Guid)
                return;

            if (value is FName fname)
            {
                RebindAssetFName(fname, asset);
                return;
            }

            if (value is FString)
                return;

            if (!type.IsValueType && !visited.Add(value))
                return;

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    RebindAllAssetFNamesRecursive(entry.Key, asset, visited);
                    RebindAllAssetFNamesRecursive(entry.Value, asset, visited);
                }
                return;
            }

            if (value is System.Collections.IEnumerable enumerable && value is not JsonNode)
            {
                foreach (object? item in enumerable)
                {
                    RebindAllAssetFNamesRecursive(item, asset, visited);
                }
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                object? childValue;
                try
                {
                    childValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }

                RebindAllAssetFNamesRecursive(childValue, asset, visited);
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object? childValue;
                try
                {
                    childValue = field.GetValue(value);
                }
                catch
                {
                    continue;
                }

                RebindAllAssetFNamesRecursive(childValue, asset, visited);
            }
        }

        private static void RebindAssetFName(FName? fname, UAsset asset)
        {
            if (fname == null)
                return;

            string? nameText = TryGetFNameText(fname);
            fname.Asset = asset;

            if (string.IsNullOrWhiteSpace(nameText))
                return;

            var rebound = FName.FromString(asset, nameText);
            if (rebound == null)
                return;

            fname.Value = rebound.Value;
            fname.Number = rebound.Number;
        }

        private static string? TryGetFNameText(FName? fname)
        {
            if (fname == null)
                return null;

            try
            {
                return fname.ToString();
            }
            catch
            {
                try
                {
                    FieldInfo? dummyField = typeof(FName).GetField("DummyValue", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (dummyField?.GetValue(fname) is FString dummyValue)
                        return dummyValue.Value;
                }
                catch
                {
                }

                return null;
            }
        }

        private static void NormalizeInvalidEnumFallbackFNames(UAsset asset)
        {
            if (asset == null || !asset.HasUnversionedProperties)
                return;

            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            NormalizeInvalidEnumFallbackFNamesRecursive(asset, asset, visited);
        }

        private static void NormalizeInvalidEnumFallbackFNamesRecursive(object? value, UAsset asset, ISet<object> visited)
        {
            if (value == null)
                return;

            Type type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || value is decimal || value is Guid)
                return;

            if (value is FName || value is FString)
                return;

            if (!type.IsValueType && !visited.Add(value))
                return;

            if (value is UAssetAPI.PropertyTypes.Objects.EnumPropertyData enumProperty)
            {
                NormalizeInvalidEnumFallbackFName(enumProperty, asset);
            }

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    NormalizeInvalidEnumFallbackFNamesRecursive(entry.Key, asset, visited);
                    NormalizeInvalidEnumFallbackFNamesRecursive(entry.Value, asset, visited);
                }
                return;
            }

            if (value is System.Collections.IEnumerable enumerable && value is not JsonNode)
            {
                foreach (object? item in enumerable)
                {
                    NormalizeInvalidEnumFallbackFNamesRecursive(item, asset, visited);
                }
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                object? childValue;
                try
                {
                    childValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }

                NormalizeInvalidEnumFallbackFNamesRecursive(childValue, asset, visited);
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                object? childValue;
                try
                {
                    childValue = field.GetValue(value);
                }
                catch
                {
                    continue;
                }

                NormalizeInvalidEnumFallbackFNamesRecursive(childValue, asset, visited);
            }
        }

        private static void NormalizeInvalidEnumFallbackFName(UAssetAPI.PropertyTypes.Objects.EnumPropertyData enumProperty, UAsset asset)
        {
            FName? value = enumProperty.Value;
            if (value == null || value.Number <= 0)
                return;

            string? valueName = value.Value?.Value;
            const string parsedFallbackBase = "UASSETAPI_INVALID_ENUM_IDX";
            if (!string.Equals(valueName, parsedFallbackBase, StringComparison.Ordinal))
                return;

            string literalFallback = UAssetAPI.PropertyTypes.Objects.EnumPropertyData.InvalidEnumIndexFallbackPrefix +
                (value.Number - 1).ToString(CultureInfo.InvariantCulture);
            enumProperty.Value = FName.DefineDummy(asset, literalFallback);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private void EnsureFunctionExportsForBlueprintFunctions(
            JsonObject root,
            JsonArray exports,
            IEnumerable<string> functionNames)
        {
            List<JsonObject> functionExports = exports
                .OfType<JsonObject>()
                .Where(obj => (obj["$type"]?.GetValue<string>() ?? string.Empty).Contains("FunctionExport", StringComparison.Ordinal))
                .ToList();
            if (functionExports.Count == 0)
                return;

            JsonObject template = (JsonObject)functionExports[0].DeepClone();
            HashSet<string> existing = new(functionExports
                .Select(obj => obj["ObjectName"]?.GetValue<string>() ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.Ordinal);

            JsonObject? classExport = exports
                .OfType<JsonObject>()
                .FirstOrDefault(obj => (obj["$type"]?.GetValue<string>() ?? string.Empty).Contains("ClassExport", StringComparison.Ordinal));

            foreach (string functionName in functionNames)
            {
                if (string.IsNullOrWhiteSpace(functionName) || !existing.Add(functionName))
                    continue;

                JsonObject newExport = (JsonObject)template.DeepClone();
                newExport["ObjectName"] = functionName;
                newExport["ScriptBytecode"] = new JsonArray();
                newExport["ScriptBytecodeSize"] = 0;
                newExport["ScriptBytecodeRaw"] = null;
                if (newExport["LoadedProperties"] is JsonArray)
                    newExport["LoadedProperties"] = new JsonArray();
                if (newExport["Data"] is JsonArray)
                    newExport["Data"] = new JsonArray();
                exports.Add(newExport);

                int exportIndex = exports.Count;
                if (classExport != null)
                    AddFunctionToClassExportMaps(classExport, functionName, exportIndex);
            }

            EnsureNameMapContains(root, functionNames);
        }

        private static void AddFunctionToClassExportMaps(JsonObject classExport, string functionName, int exportIndex)
        {
            if (classExport["FuncMap"] is JsonArray funcMap)
            {
                bool exists = funcMap.OfType<JsonArray>()
                    .Any(pair => pair.Count >= 2 &&
                                 string.Equals(pair[0]?.GetValue<string>() ?? string.Empty, functionName, StringComparison.Ordinal));
                if (!exists)
                {
                    funcMap.Add(new JsonArray
                    {
                        functionName,
                        exportIndex
                    });
                }
            }

            if (classExport["Children"] is JsonArray children)
            {
                bool exists = children.OfType<JsonValue>()
                    .Any(v => v.TryGetValue<int>(out int value) && value == exportIndex);
                if (!exists)
                    children.Add(exportIndex);
            }
        }

        private void RebuildNodeDefinitionRuntimeCaches(
            IReadOnlyDictionary<string, List<string>>? importedKnownParameters,
            IReadOnlyDictionary<string, string>? importedFunctionPaths,
            IReadOnlyDictionary<string, BlueprintFunctionTemplate>? importedFunctionTemplates = null)
        {
            NormalizeLegacyNodeDefinitions();
            nodeDefinitionNameBySignature.Clear();
            nextNodeDefinitionSuffixByBaseName.Clear();
            knownFunctionParametersByName.Clear();
            functionDefinitionAssetPathByName.Clear();
            functionTemplatesByName.Clear();
            sourceAssetContextByRelativePath.Clear();

            EnsureCallNodeDefinitionsHaveTargetPins();

            foreach ((string key, NodeDefinition definition) in nodeDefinitions.ToList())
            {
                if (definition == null)
                    continue;

                if (string.IsNullOrWhiteSpace(definition.Name))
                    definition.Name = key;

                definition.InputPins ??= [];
                definition.OutputPins ??= [];
                SanitizeNodeDefinitionForRuntimeUse(definition);

                string signatureKey = BuildNodeDefinitionSignature(definition.Name, definition.InputPins, definition.OutputPins);
                if (!nodeDefinitionNameBySignature.ContainsKey(signatureKey))
                    nodeDefinitionNameBySignature[signatureKey] = definition.Name;

                string baseName = definition.Name;
                int suffix = 1;
                int separator = definition.Name.LastIndexOf(" #", StringComparison.Ordinal);
                if (separator > 0 && int.TryParse(definition.Name[(separator + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSuffix) && parsedSuffix >= 2)
                {
                    baseName = definition.Name[..separator];
                    suffix = parsedSuffix;
                }

                int next = suffix + 1;
                if (nextNodeDefinitionSuffixByBaseName.TryGetValue(baseName, out int existingNext))
                    next = Math.Max(next, existingNext);
                nextNodeDefinitionSuffixByBaseName[baseName] = next;
            }

            EnsureVariableNodeDefinitions();

            if (importedKnownParameters != null)
            {
                foreach ((string functionName, List<string> parameters) in importedKnownParameters)
                {
                    if (string.IsNullOrWhiteSpace(functionName) || parameters == null)
                        continue;
                    knownFunctionParametersByName[functionName] = [.. parameters];
                }
            }

            // 向后兼容：旧格式没有参数缓存时，从 Call 定义中反推参数名。
            foreach ((string definitionName, NodeDefinition definition) in nodeDefinitions)
            {
                if (!definitionName.StartsWith("Call ", StringComparison.Ordinal))
                    continue;

                string functionName = definitionName[5..].Trim();
                if (string.IsNullOrWhiteSpace(functionName) || knownFunctionParametersByName.ContainsKey(functionName))
                    continue;

                List<string> parameterNames = definition.InputPins
                    .Where(pin =>
                        pin.Type == PinType.Data &&
                        !string.Equals(pin.Name, "Target", StringComparison.Ordinal))
                    .Select(pin => pin.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                if (parameterNames.Count > 0)
                    knownFunctionParametersByName[functionName] = parameterNames;
            }

            if (importedFunctionPaths != null)
            {
                foreach ((string functionName, string path) in importedFunctionPaths)
                {
                    if (string.IsNullOrWhiteSpace(functionName) || string.IsNullOrWhiteSpace(path))
                        continue;
                    functionDefinitionAssetPathByName[functionName] = path;
                }
            }

            if (importedFunctionTemplates != null)
            {
                foreach ((string functionName, BlueprintFunctionTemplate template) in importedFunctionTemplates)
                {
                    if (string.IsNullOrWhiteSpace(functionName))
                        continue;
                    functionTemplatesByName[functionName] = BlueprintModelCloner.Clone(template);
                }
            }

            foreach ((string definitionName, NodeDefinition definition) in nodeDefinitions)
            {
                string baseDefinitionName = GetBaseDefinitionName(definitionName);
                if (baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
                {
                    ApplyFunctionTemplateMetadataToDefinition(definitionName, ExtractCallFunctionName(baseDefinitionName));
                }
            }
        }

        private void SanitizeNodeDefinitionForRuntimeUse(NodeDefinition definition)
        {
            string baseDefinitionName = GetBaseDefinitionName(definition.Name);

            switch (baseDefinitionName)
            {
                case "Return":
                    EnsureExecPin(definition.InputPins, "In", PinDirection.Input);
                    EnsureDataPin(definition.InputPins, "Return", PinDirection.Input);
                    EnsureExecPin(definition.OutputPins, "Out", PinDirection.Output);
                    break;
                case "Jump":
                    EnsureExecPin(definition.InputPins, "In", PinDirection.Input);
                    EnsureExecPin(definition.OutputPins, "Out", PinDirection.Output);
                    EnsureExecPin(definition.OutputPins, "To", PinDirection.Output);
                    break;
                case "Jump If Not":
                    EnsureExecPin(definition.InputPins, "In", PinDirection.Input);
                    EnsureDataPin(definition.InputPins, "Condition", PinDirection.Input);
                    EnsureExecPin(definition.OutputPins, "Out", PinDirection.Output);
                    EnsureExecPin(definition.OutputPins, "To", PinDirection.Output);
                    break;
                case "Push Flow":
                    EnsureExecPin(definition.InputPins, "In", PinDirection.Input);
                    EnsureExecPin(definition.OutputPins, "Out", PinDirection.Output);
                    EnsureExecPin(definition.OutputPins, "To", PinDirection.Output);
                    break;
            }

            if (IsGenericVariableDefinitionName(baseDefinitionName))
            {
                definition.ReferenceDescriptors.Clear();
                definition.PinSchemas.Clear();
                definition.PropertyTemplateJsonByName.Clear();
                definition.RepresentativeCallTemplateJson = null;
                definition.RepresentativeCallExpressionType = null;
                definition.RepresentativeCallReferenceDescriptors.Clear();
                return;
            }

            if (!baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
                return;

            string functionName = ExtractCallFunctionName(baseDefinitionName);
            definition.PinSchemas = BuildGenericCallPinSchemas(definition);
            definition.PropertyTemplateJsonByName.Clear();
            definition.ReferenceDescriptors = definition.ReferenceDescriptors
                .Where(descriptor =>
                    !IsStackNodeDescriptor(descriptor) ||
                    string.Equals(descriptor.ObjectName, functionName, StringComparison.Ordinal))
                .Select(BlueprintModelCloner.Clone)
                .ToList();
            definition.RepresentativeCallReferenceDescriptors = definition.RepresentativeCallReferenceDescriptors
                .Where(descriptor =>
                    !IsStackNodeDescriptor(descriptor) ||
                    string.Equals(descriptor.ObjectName, functionName, StringComparison.Ordinal))
                .Select(BlueprintModelCloner.Clone)
                .ToList();

            if (DefinitionCallTemplateTargetsDifferentFunction(definition.SourceExpressionTemplateJson, functionName))
            {
                definition.SourceExpressionTemplateJson = null;
                definition.SourceExpressionType = null;
            }

            if (DefinitionCallTemplateTargetsDifferentFunction(definition.RepresentativeCallTemplateJson, functionName))
            {
                definition.RepresentativeCallTemplateJson = null;
                definition.RepresentativeCallExpressionType = null;
                definition.RepresentativeCallReferenceDescriptors.Clear();
            }
        }

        private void NormalizeLegacyNodeDefinitions()
        {
            Dictionary<string, NodeDefinition> normalizedDefinitions = new(StringComparer.Ordinal);
            foreach ((string key, NodeDefinition definition) in nodeDefinitions)
            {
                if (definition == null)
                    continue;

                string normalizedName = NormalizeLegacyDefinitionName(string.IsNullOrWhiteSpace(definition.Name) ? key : definition.Name);
                definition.Name = normalizedName;

                if (!normalizedDefinitions.TryGetValue(normalizedName, out NodeDefinition? existingDefinition))
                {
                    normalizedDefinitions[normalizedName] = definition;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existingDefinition.SourceExpressionTemplateJson) &&
                    !string.IsNullOrWhiteSpace(definition.SourceExpressionTemplateJson))
                {
                    existingDefinition.SourceExpressionTemplateJson = definition.SourceExpressionTemplateJson;
                    existingDefinition.SourceExpressionType = definition.SourceExpressionType;
                }

                if (existingDefinition.ReferenceDescriptors.Count == 0 && definition.ReferenceDescriptors.Count > 0)
                    existingDefinition.ReferenceDescriptors = definition.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList();
                if (existingDefinition.PinSchemas.Count == 0 && definition.PinSchemas.Count > 0)
                    existingDefinition.PinSchemas = definition.PinSchemas.Select(BlueprintModelCloner.Clone).ToList();
                if (existingDefinition.PropertyTemplateJsonByName.Count == 0 && definition.PropertyTemplateJsonByName.Count > 0)
                    existingDefinition.PropertyTemplateJsonByName = definition.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            }

            nodeDefinitions = normalizedDefinitions;
        }

        private static void EnsureExecPin(List<Pin> pins, string pinName, PinDirection direction)
        {
            if (pins.Any(pin => pin.Type == PinType.Exec && string.Equals(pin.Name, pinName, StringComparison.Ordinal)))
                return;

            pins.Add(new Pin(pinName, direction, PinType.Exec));
        }

        private static void EnsureDataPin(List<Pin> pins, string pinName, PinDirection direction)
        {
            if (pins.Any(pin => pin.Type == PinType.Data && string.Equals(pin.Name, pinName, StringComparison.Ordinal)))
                return;

            pins.Add(new Pin(pinName, direction, PinType.Data));
        }

        private static List<PinExportSchema> BuildGenericCallPinSchemas(NodeDefinition definition)
        {
            List<PinExportSchema> schemas = [];

            foreach (Pin pin in definition.InputPins)
            {
                if (pin.Type != PinType.Data)
                    continue;

                if (string.Equals(pin.Name, "Target", StringComparison.Ordinal))
                {
                    schemas.Add(new PinExportSchema
                    {
                        PinName = "Target",
                        SemanticKind = "TargetObject",
                        ExpressionPath = "/ObjectExpression",
                        IsTargetObject = true
                    });
                    continue;
                }

                schemas.Add(new PinExportSchema
                {
                    PinName = pin.Name,
                    SemanticKind = "Parameter",
                    IsParameter = true
                });
            }

            if (definition.OutputPins.Any(pin =>
                    pin.Type == PinType.Data &&
                    string.Equals(pin.Name, "Result", StringComparison.Ordinal)))
            {
                schemas.Add(new PinExportSchema
                {
                    PinName = "Result",
                    SemanticKind = "ReturnValue",
                    IsReturnValue = true
                });
            }

            return schemas;
        }

        private async void 扫描节点定义ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Enabled = false;
            IProgress<string> progress = new Progress<string>(message =>
            {
                label1.Text = message;
                label1.Update();
            });
            await ExtractCurrentPakAndImportFunctionDefinitionsAsync(progress);
            Enabled = true;
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                SaveWorkload();
            }
        }

        private void SaveWorkload()
        {
            if (tabControl1.TabCount != 0)
            {
                foreach (TabPage p in tabControl1.TabPages)
                {
                    if (p.Text.EndsWith(" *"))
                    {
                        try
                        {
                            AddToWorkload(p.ToolTipText);
                            string filepath = Path.Combine(WorkDir, "Workload", p.ToolTipText + ".json");
                            Directory.CreateDirectory(Path.GetDirectoryName(filepath));
                            File.WriteAllText(filepath, BuildEditedAssetJsonFromFilePage(p));
                            MarkFileTabClean(p);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"文件 {p.ToolTipText} 保存失败：{ex}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            label1.Text = "工作区已保存";
        }

        private void 保存ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            SaveWorkload();
        }

        public static string[] GetTreeViewLastNodes(TreeNodeCollection node)
        {
            List<string> l = [];
            foreach (TreeNode n in node)
            {
                if (n.Nodes.Count == 0)
                {
                    l.Add(GetNodePath(n));
                    continue;
                }
                string[] ns = GetTreeViewLastNodes(n.Nodes);
                l.AddRange(ns);
            }
            return [.. l];
        }

        private void 整个工作区pakToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "包文件（*.pak）|*.pak|所有文件（*.*）|*.*",
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                SaveWorkload();
                string[] ns = GetTreeViewLastNodes(treeView2.Nodes);
                using FileStream fs = File.Create(dialog.FileName);
                using PakWriter writer = new PakBuilder().Writer(fs, exportPakVersion);
                foreach (string n in ns)
                {
                    string json = File.ReadAllText(Path.Combine(WorkDir, "Workload", n + ".json"));
                    UAsset asset = UAsset.DeserializeJson(json);
                    asset.Mappings = new(UsmapPath);
                    asset.SetEngineVersion(exportEngineVersion);
                    EnsureTypedAssetNameMapCoverage(asset);
                    asset.Write(out MemoryStream uassetStream, out MemoryStream uexpStream);
                    writer.WriteFile(n, uassetStream.ToArray());
                    writer.WriteFile(n[..^5] + "exp", uexpStream.ToArray());
                }
                writer.WriteIndex();
            }
        }

        private void treeView2_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Point ClickPoint = new Point(e.X, e.Y);
                TreeNode CurrentNode = treeView2.GetNodeAt(ClickPoint);
                if (CurrentNode != null)//判断你点的是不是一个节点
                {
                    treeView2.SelectedNode = CurrentNode;//选中这个节点
                }
                TreeNode n = treeView2.SelectedNode;
                if (n == null) return;
                ContextMenuStrip menu = new();
                ToolStripMenuItem removeMenuItem = new("从工作区中移除");
                removeMenuItem.Click += treeview2_RemoveMenuItem_Click;
                menu.Items.Add(removeMenuItem);
                menu.Show(treeView2, e.Location);
            }
        }

        private void treeview2_RemoveMenuItem_Click(object? sender, EventArgs e)
        {
            TreeNode p = treeView2.SelectedNode.Parent;
            treeView2.SelectedNode.Remove();
            if (p != null) RemoveAloneNode(p);
            if (treeView2.Nodes.Count == 0) 整个工作区pakToolStripMenuItem.Enabled = false;
        }

        private static void RemoveAloneNode(TreeNode node)
        {
            if (node == null) return;
            TreeNode parent = node.Parent;
            if (node.Nodes.Count == 0)
            {
                node.Remove();
                RemoveAloneNode(parent);
            }
        }

        private void treeView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Point ClickPoint = new(e.X, e.Y);
                TreeNode CurrentNode = treeView1.GetNodeAt(ClickPoint);
                if (CurrentNode != null)//判断你点的是不是一个节点
                {
                    treeView1.SelectedNode = CurrentNode;//选中这个节点
                }
                TreeNode n = treeView1.SelectedNode;
                if (n == null) return;
                if (n.Nodes.Count == 0)
                {
                    ContextMenuStrip menu = new();
                    ToolStripMenuItem addMenuItem = new("添加到工作区");
                    addMenuItem.Click += treeview1_AddMenuItem_Click;
                    menu.Items.Add(addMenuItem);
                    menu.Show(treeView1, e.Location);
                }
            }
        }

        private void treeview1_AddMenuItem_Click(object? sender, EventArgs e)
        {
            AddNodeToWorkload(treeView1.SelectedNode);
        }

        private void AddNodeToWorkload(TreeNode node)
        {
            if (node.Nodes.Count == 0)
            {
                string path = GetNodePath(node);
                string pakPath = path[currentPakMountPointDisplay.Length..];
                if (pakPath.StartsWith('/')) pakPath = pakPath[1..];
                if (currentPakReader == null || currentPakFileStream == null)
                {
                    _ = MessageBox.Show($"错误：添加文件 {pakPath} 失败。\n找不到源文件。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string filepath = Path.Combine(WorkDir, "Source", pakPath);
                if (!(DontReCom && File.Exists(filepath)))
                {
                    string? filepathdir = Path.GetDirectoryName(filepath);
                    if (filepathdir != null) Directory.CreateDirectory(filepathdir);
                    byte[] bytes = currentPakReader.Get(currentPakFileStream, pakPath);
                    File.WriteAllBytes(filepath, bytes);
                    bytes = currentPakReader.Get(currentPakFileStream, Path.ChangeExtension(pakPath, "uexp"));
                    File.WriteAllBytes(Path.ChangeExtension(filepath, "uexp"), bytes);
                }
                string workloadfilepath = Path.Combine(WorkDir, "Workload", path + ".json");
                string? workloadfilepathdir = Path.GetDirectoryName(workloadfilepath);
                if (workloadfilepathdir != null) Directory.CreateDirectory(workloadfilepathdir);
                File.WriteAllText(workloadfilepath, new UAsset(filepath, engineVersion, new(UsmapPath)).SerializeJson());
                AddToWorkload(path);
                整个工作区pakToolStripMenuItem.Enabled = true;
            }
            else
            {
                foreach (TreeNode n in node.Nodes) AddNodeToWorkload(n);
            }
        }

        private async void treeView2_NodeMouseDoubleClickAsync(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node.Nodes.Count == 0)
            {
                await OpenAssetByTreePathAsync(GetNodePath(e.Node), true);
                当前文件ToolStripMenuItem.Enabled = true;
            }
        }

        private void blueprintDatajsonToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "Blueprint Data（*.json）|*.json|所有文件（*.*）|*.*",
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    BlueprintCanvas c = GetSelectedBlueprintCanvas() ?? throw new InvalidOperationException("当前标签页没有蓝图画布。");
                    string json = JsonSerializer.Serialize(c.ExportData());
                    File.WriteAllText(dialog.FileName, json);
                    label1.Text = "导出成功";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    label1.Text = "导出失败";
                }
            }
        }

        private void 使用方法ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _ = MessageBox.Show(
                "1.打开一个 Pak 文件\n" +
                "2.加载（或扫描）定义文件\n" +
                "3.打开你要编辑的文件\n" +
                "4.编辑\n" +
                "  - Ctrl+C 复制\n" +
                "  - Ctrl+V 粘贴\n" +
                "  - 左键拖动 框选\n" +
                "  - 右键拖动 平移画布\n" +
                "5.导出 Pak 文件",
                "使用方法",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void 关于ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBoxIcon icon = MessageBoxIcon.Information;
            if (Debugger.IsAttached) icon = MessageBoxIcon.Warning;
            else if (IsDebuggerPresent() || BeDebugged) icon = MessageBoxIcon.Error;
            _ = MessageBox.Show(
                "BPEditor " + Version + " " + (IsDebug ? "Debug" : (IsUserDebug ? "UserDebug" : "Release")) + "\n" +
                "By BiliBili @ OceanCreation, uid: 1123458702\n\n" +
                "! ONLY CCB TEAM CAN DO !",
                "关于",
                MessageBoxButtons.OK, icon);
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            object? o = tabControl1.SelectedTab?.Controls[0];
            if (o is not null and TabControl)
            {
                int selIndex = ((TabControl)o).SelectedIndex;
                if (selIndex == 0)
                {
                    tabPage3.Parent = tabControl3;
                    tabPage4.Parent = null;
                }
                else if (selIndex == 1)
                {
                    tabPage3.Parent = null;
                    tabPage4.Parent = tabControl3;
                }
                else
                {
                    tabPage3.Parent = null;
                    tabPage4.Parent = null;
                }
            }
        }
    }
}
