// ========== 数据结构 ==========
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Packaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

public enum PinDirection { Input, Output }
public enum PinType { Exec, Data }

public class Pin
{
    public string Name { get; set; }
    public PinDirection Direction { get; set; }
    public PinType Type { get; set; }
    [JsonIgnore] public Rectangle Bounds { get; set; }   // 相对于节点控件的矩形
    [JsonIgnore] public Point Center { get; set; }       // 相对于节点控件的中心点
    public object Value { get; set; }

    public Pin() { }
    public Pin(string name, PinDirection dir, PinType type)
    {
        Name = name;
        Direction = dir;
        Type = type;
    }
}

public class NodeDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<Pin> InputPins { get; set; } = new List<Pin>();
    public List<Pin> OutputPins { get; set; } = new List<Pin>();
    public string? SourceExpressionTemplateJson { get; set; }
    public string? SourceExpressionType { get; set; }
    public List<NodeReferenceDescriptor> ReferenceDescriptors { get; set; } = new List<NodeReferenceDescriptor>();
    public List<PinExportSchema> PinSchemas { get; set; } = new List<PinExportSchema>();
    public Dictionary<string, string> PropertyTemplateJsonByName { get; set; } = new(StringComparer.Ordinal);
    public string? RepresentativeCallTemplateJson { get; set; }
    public string? RepresentativeCallExpressionType { get; set; }
    public List<NodeReferenceDescriptor> RepresentativeCallReferenceDescriptors { get; set; } = new List<NodeReferenceDescriptor>();
}

public class ConnectionData
{
    public Guid FromNodeId { get; set; }
    public string FromPinName { get; set; } = string.Empty;
    public Guid ToNodeId { get; set; }
    public string ToPinName { get; set; } = string.Empty;
    public long Sequence { get; set; }
}

public class BlueprintData
{
    public List<SerializableNode> Nodes { get; set; } = new List<SerializableNode>();
    public List<ConnectionData> Connections { get; set; } = new List<ConnectionData>();
}

public class SerializableNode
{
    public Guid Id { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public Point Location { get; set; }
    public Dictionary<string, object> PinValues { get; set; } = new Dictionary<string, object>();
    public Dictionary<string, string> MetaData { get; set; } = new Dictionary<string, string>();
    public List<NodeReferenceDescriptor> ReferenceDescriptors { get; set; } = new List<NodeReferenceDescriptor>();
    public List<PinExportSchema> PinSchemas { get; set; } = new List<PinExportSchema>();
    public Dictionary<string, string> PropertyTemplateJsonByName { get; set; } = new(StringComparer.Ordinal);
}

// ========== 动态节点控件 ==========
public class BlueprintNode : UserControl
{
    private const int NodeSurfacePadding = 120;
    private NodeDefinition definition;
    private Guid nodeId = Guid.NewGuid();
    private bool dragging = false;
    private Point dragStart;
    private Rectangle titleBarRect;
    private Rectangle closeButtonRect;
    private float zoom = 1.0f;
    private int baseWidth = 220;
    private int basePinHeight = 24;
    private int baseTitleHeight = 32;
    private int editorBaseHeight = 30;
    private int editorBaseWidth = 170;
    private int editorVerticalMargin = 8;
    private int footerBaseHeight = 18;
    private Control? valueEditor;
    private string? editorValueKey;
    private bool hasEditableValue;
    private bool isSelected;
    private bool suppressValueEditorEvents;
    internal BlueprintCanvas? OwnerCanvas { get; set; }

    public Guid NodeId => nodeId;
    public NodeDefinition Definition => definition;
    public List<Pin> InputPins => definition.InputPins;
    public List<Pin> OutputPins => definition.OutputPins;
    public float Zoom => zoom;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
        if (isSelected == value) return;
        isSelected = value;
            Invalidate();
            OwnerCanvas?.InvalidateCanvas();
        }
    }

    public event EventHandler NodeDeleted;
    public event EventHandler NodeDataChanged;
    public event Action<BlueprintNode, Pin> StartConnection;
    public event Action<BlueprintNode, Pin> EndConnection;

    // 在 BlueprintNode 类中添加
    public Dictionary<string, object> PinValues { get; set; } = new Dictionary<string, object>();
    public Dictionary<string, string> MetaData { get; set; } = new Dictionary<string, string>();

    public BlueprintNode(NodeDefinition def)
    {
        definition = CloneDefinition(def);
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.Selectable, true);
        BuildValueEditor();
        SetZoom(1.0f);

        this.MouseDown += OnMouseDown;
        this.MouseMove += OnMouseMove;
        this.MouseUp += OnMouseUp;
        this.Paint += OnPaint;
        this.Resize += (s, e) => UpdatePinBounds();
        this.ParentChanged += (s, e) => RefreshValueEditorChoices();
    }

    public void SetZoom(float newZoom)
    {
        zoom = Math.Max(0.5f, Math.Min(2.0f, newZoom));
        RecalcSize();
        UpdateValueEditorLayout();
        UpdatePinBounds();
        Invalidate();
    }

    private void RecalcSize()
    {
        int titleHeight = (int)(baseTitleHeight * zoom);
        int editorHeight = GetEditorHeight();
        int contentStartY = titleHeight + editorHeight;
        int pinCount = Math.Max(InputPins.Count, OutputPins.Count);
        int bodyHeight = Math.Max((int)(80 * zoom), contentStartY + pinCount * (int)(basePinHeight * zoom) + (int)(12 * zoom) + GetFooterHeight());
        int width = CalculateNodeWidth();
        this.Size = new Size(width, bodyHeight);
    }

    private void UpdatePinBounds()
    {
        int inputIndex = 0, outputIndex = 0;
        int pinHeight = (int)(basePinHeight * zoom);
        int startY = (int)(baseTitleHeight * zoom) + GetEditorHeight();

        // 左侧输入引脚
        foreach (var pin in InputPins)
        {
            int y = startY + inputIndex * pinHeight;
            pin.Bounds = new Rectangle(0, y, (int)(20 * zoom), pinHeight);
            pin.Center = new Point((int)(5 * zoom), y + pinHeight / 2);
            inputIndex++;
        }

        // 右侧输出引脚
        foreach (var pin in OutputPins)
        {
            int y = startY + outputIndex * pinHeight;
            pin.Bounds = new Rectangle(this.Width - (int)(20 * zoom), y, (int)(20 * zoom), pinHeight);
            pin.Center = new Point(this.Width - (int)(5 * zoom), y + pinHeight / 2);
            outputIndex++;
        }
    }

    private void OnPaint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 阴影
        using (var shadow = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            g.FillRectangle(shadow, 2, 2, Width, Height);

        // 主体背景
        using (var body = new LinearGradientBrush(ClientRectangle, Color.FromArgb(70, 70, 90), Color.FromArgb(50, 50, 70), 90))
            g.FillRectangle(body, 0, 0, Width, Height);

        // 边框
        using (var border = new Pen(Color.FromArgb(100, 100, 120)))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        if (isSelected)
        {
            using var selectedBorder = new Pen(Color.FromArgb(255, 240, 130), Math.Max(2f, 2f * zoom));
            g.DrawRectangle(selectedBorder, 1, 1, Width - 3, Height - 3);
        }

        // 标题栏
        int titleHeight = (int)(baseTitleHeight * zoom);
        titleBarRect = new Rectangle(0, 0, Width, titleHeight);
        (Color titleStart, Color titleEnd) = GetTitleColors();
        using (var titleBrush = new LinearGradientBrush(titleBarRect, titleStart, titleEnd, 90))
            g.FillRectangle(titleBrush, titleBarRect);

        float fontSize = 9 * zoom;
        using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.White))
            g.DrawString(definition.Name, font, brush, 6 * zoom, 8 * zoom);

        // 关闭按钮
        int btnSize = (int)(24 * zoom);
        int btnMargin = (int)(4 * zoom);
        closeButtonRect = new Rectangle(Width - btnSize - btnMargin, btnMargin, btnSize, btnSize);
        using (var btnBrush = new SolidBrush(Color.FromArgb(200, 60, 60)))
        using (var pen = new Pen(Color.White, 2 * zoom))
        {
            g.FillRectangle(btnBrush, closeButtonRect);
            int offset = (int)(6 * zoom);
            g.DrawLine(pen, closeButtonRect.X + offset, closeButtonRect.Y + offset,
                closeButtonRect.Right - offset, closeButtonRect.Bottom - offset);
            g.DrawLine(pen, closeButtonRect.Right - offset, closeButtonRect.Y + offset,
                closeButtonRect.X + offset, closeButtonRect.Bottom - offset);
        }

        DrawEditorValue(g);
        
        bool l = false;
        if (l == true)
        {
        // 绘制引脚
            foreach (var pin in InputPins)
                if (pin.Name == "In")DrawPin(g, pin, "入口In", true);
                else if (pin.Name == "Target")DrawPin(g, pin, "目标Target", true);
                else if (pin.Name == "callerCard")DrawPin(g, pin, "卡牌调用者callerCard", true);
                else if (pin.Name == "hasTarget")DrawPin(g, pin, "目标存在(Bool)hasTarget", true);
                else if (pin.Name == "card")DrawPin(g, pin, "对象card", true);
                else if (pin.Name == "Condition")DrawPin(g, pin, "条件(Bool)Condition", true);
                else if (pin.Name == "Value")DrawPin(g, pin, "值Value", true);
                else if (pin.Name == "Return")DrawPin(g, pin, "返回Return", true);

                else if (pin.Name == "Arg1")DrawPin(g, pin, "参数Arg1", true);
                else if (pin.Name == "Arg2")DrawPin(g, pin, "参数Arg2", true);
                else if (pin.Name == "Arg3")DrawPin(g, pin, "参数Arg3", true);
                else if (pin.Name == "Arg4")DrawPin(g, pin, "参数Arg4", true);
                else if (pin.Name == "Arg5")DrawPin(g, pin, "参数Arg5", true);
                else if (pin.Name == "Arg6")DrawPin(g, pin, "参数Arg6", true);
                else if (pin.Name == "Arg7")DrawPin(g, pin, "参数Arg7", true);
                else if (pin.Name == "Arg8")DrawPin(g, pin, "参数Arg8", true);
                else if (pin.Name == "Arg9")DrawPin(g, pin, "参数Arg9", true);
                else if (pin.Name == "Arg10")DrawPin(g, pin, "参数Arg10", true);


                else
                {
                    DrawPin(g, pin, pin.Name, true);
                }
                
            foreach (var pin in OutputPins)
                if (pin.Name == "Out")DrawPin(g, pin, "出口Out", false);
                else if (pin.Name == "To")DrawPin(g, pin, "跳转To", false);
                else if (pin.Name == "Result")DrawPin(g, pin, "结果Result", false);
                else if (pin.Name == "Value")DrawPin(g, pin, "值Value", false);
                else if (pin.Name == "Exec")DrawPin(g, pin, "执行Exec", false);
                else if (pin.Name == "canIt")DrawPin(g, pin, "canIt条件", false);
                else if (pin.Name == "reason")DrawPin(g, pin, "reason原因", false);

                else
                    {
                        DrawPin(g, pin, pin.Name, false);
                    }
        }
        else
        {
            foreach (var pin in InputPins)
                DrawPin(g, pin, pin.Name, true);
            foreach (var pin in OutputPins)
                DrawPin(g, pin, pin.Name, false);
        }
        DrawStatementOffset(g);
    }



    private void DrawEditorValue(Graphics g)
    {
        if (!TryGetEditablePinValue(out string key, out object? value))
            return;

        Rectangle editorRect = GetEditorBounds();
        if (editorRect.IsEmpty)
            return;

        using SolidBrush backBrush = new(Color.FromArgb(65, 65, 80));
        using Pen borderPen = new(Color.FromArgb(105, 105, 125));
        g.FillRectangle(backBrush, editorRect);
        g.DrawRectangle(borderPen, editorRect);

        string text = FormatEditableValue(PinValues.TryGetValue(key, out object? stored) ? stored : value);
        Rectangle textRect = Rectangle.Inflate(editorRect, (int)(-6 * zoom), 0);
        using Font font = new("Segoe UI", 8.5f * zoom);
        using SolidBrush textBrush = new(Color.White);
        using StringFormat format = new()
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        GraphicsState state = g.Save();
        try
        {
            g.SetClip(textRect);
            g.DrawString(text, font, textBrush, textRect, format);
        }
        finally
        {
            g.Restore(state);
        }
    }

    private void DrawPin(Graphics g, Pin pin, string name, bool isInput)
    {
        Color color = pin.Type == PinType.Exec ? Color.FromArgb(255, 200, 100) : Color.FromArgb(100, 200, 100);
        int radius = (int)(5 * zoom);
        using (var brush = new SolidBrush(color))
        using (var pen = new Pen(Color.Black, Math.Max(1, zoom)))
        {
            g.FillEllipse(brush, pin.Center.X - radius, pin.Center.Y - radius, radius * 2, radius * 2);
            g.DrawEllipse(pen, pin.Center.X - radius, pin.Center.Y - radius, radius * 2, radius * 2);
        }

        float fontSize = 8 * zoom;
        using (var font = new Font("Segoe UI", fontSize))
        using (var brush = new SolidBrush(Color.FromArgb(220, 220, 220)))
        {
            SizeF textSize = g.MeasureString(name, font);
            if (isInput)
                g.DrawString(name, font, brush, (int)(18 * zoom), pin.Bounds.Y + (int)(4 * zoom));
            else
                g.DrawString(name, font, brush, Width - (int)(18 * zoom) - textSize.Width, pin.Bounds.Y + (int)(4 * zoom));
        }
    }

    private int CalculateNodeWidth()
    {
        int minWidth = (int)(baseWidth * zoom);
        int titleWidth = MeasureTextWidth(definition.Name, 9 * zoom, FontStyle.Bold) + (int)(50 * zoom);
        int inputWidth = InputPins.Count == 0 ? 0 : InputPins.Max(p => MeasureTextWidth(p.Name, 8 * zoom, FontStyle.Regular));
        int outputWidth = OutputPins.Count == 0 ? 0 : OutputPins.Max(p => MeasureTextWidth(p.Name, 8 * zoom, FontStyle.Regular));
        int pinWidth = inputWidth + outputWidth + (int)(90 * zoom);
        int editorWidth = hasEditableValue ? (int)(Math.Max(editorBaseWidth, GetEditorPreferredWidth()) * zoom) + (int)(52 * zoom) : 0;
        return Math.Max(minWidth, Math.Max(titleWidth, Math.Max(pinWidth, editorWidth)));
    }

    private int MeasureTextWidth(string text, float fontSize, FontStyle style)
    {
        using Font font = new("Segoe UI", fontSize, style);
        Size textSize = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        return textSize.Width;
    }

    private void BuildValueEditor()
    {
        if (!TryGetEditablePinValue(out string editorKey, out _)) return;
        editorValueKey = editorKey;
        hasEditableValue = true;
        valueEditor = null;
    }

    private bool IsVariableSelectorEditor(string editorKey)
    {
        if (!string.Equals(editorKey, "EditorText", StringComparison.Ordinal))
            return false;

        string baseDefinitionName = GetBaseDefinitionNameForEditor(definition.Name);
        return string.Equals(baseDefinitionName, "Get Variable", StringComparison.Ordinal) ||
            baseDefinitionName.StartsWith("Get Variable ", StringComparison.Ordinal) ||
            string.Equals(baseDefinitionName, "Set Variable", StringComparison.Ordinal) ||
            baseDefinitionName.StartsWith("Set Variable ", StringComparison.Ordinal);
    }

    private static string GetBaseDefinitionNameForEditor(string definitionName)
    {
        definitionName = NormalizeLegacyDefinitionName(definitionName);
        if (string.IsNullOrWhiteSpace(definitionName))
            return string.Empty;

        int suffixSeparator = definitionName.LastIndexOf(" #", StringComparison.Ordinal);
        if (suffixSeparator > 0 &&
            int.TryParse(definitionName[(suffixSeparator + 2)..], out _))
        {
            return definitionName[..suffixSeparator];
        }

        return definitionName;
    }

    internal static string NormalizeLegacyDefinitionName(string definitionName)
    {
        if (string.IsNullOrWhiteSpace(definitionName))
            return string.Empty;

        int suffixSeparator = definitionName.LastIndexOf(" #", StringComparison.Ordinal);
        string baseName = definitionName;
        string suffix = string.Empty;
        if (suffixSeparator > 0 &&
            int.TryParse(definitionName[(suffixSeparator + 2)..], out _))
        {
            baseName = definitionName[..suffixSeparator];
            suffix = definitionName[suffixSeparator..];
        }

        if (string.Equals(baseName, "Branch", StringComparison.Ordinal))
            return "Jump If Not" + suffix;

        return definitionName;
    }

    public void RefreshValueEditorChoices()
    {
        if (valueEditor == null)
            return;

        if (valueEditor is not ComboBox combo || string.IsNullOrWhiteSpace(editorValueKey))
            return;

        if (IsVariableSelectorEditor(editorValueKey))
        {
            PopulateVariableComboChoices(combo);
            return;
        }

        if (editorValueKey == "Value")
        {
            suppressValueEditorEvents = true;
            try
            {
                bool boolValue = false;
                if (PinValues.TryGetValue("Value", out object? stored))
                {
                    if (stored is bool b)
                        boolValue = b;
                    else if (stored != null && bool.TryParse(Convert.ToString(stored), out bool parsed))
                        boolValue = parsed;
                }

                combo.SelectedIndex = boolValue ? 0 : 1;
            }
            finally
            {
                suppressValueEditorEvents = false;
            }
        }
    }

    private void PopulateVariableComboChoices(ComboBox combo)
    {
        string currentValue = editorValueKey != null && PinValues.TryGetValue(editorValueKey, out object? stored)
            ? FormatEditableValue(stored)
            : string.Empty;

        IReadOnlyList<string> availableVariables = OwnerCanvas != null
            ? OwnerCanvas.GetAvailableVariableNames(this)
            : Array.Empty<string>();

        suppressValueEditorEvents = true;
        try
        {
            combo.BeginUpdate();
            combo.Items.Clear();

            foreach (string variableName in availableVariables)
            {
                combo.Items.Add(variableName);
            }

            combo.Items.Add(BlueprintCanvas.AddVariableOptionText);
            int selectedIndex = !string.IsNullOrWhiteSpace(currentValue)
                ? combo.FindStringExact(currentValue)
                : -1;
            combo.SelectedIndex = selectedIndex;
            combo.Text = selectedIndex >= 0
                ? combo.Items[selectedIndex]?.ToString() ?? string.Empty
                : currentValue;
            UpdateVariableComboDropDownWidth(combo, availableVariables, currentValue);
        }
        finally
        {
            combo.EndUpdate();
            suppressValueEditorEvents = false;
        }
    }

    private void UpdateVariableComboDropDownWidth(
        ComboBox combo,
        IReadOnlyList<string> availableVariables,
        string currentValue)
    {
        int maxWidth = combo.Width;
        Font font = combo.Font ?? Font;

        foreach (string candidate in availableVariables)
            maxWidth = Math.Max(maxWidth, MeasureComboItemWidth(font, candidate));

        if (!string.IsNullOrWhiteSpace(currentValue))
            maxWidth = Math.Max(maxWidth, MeasureComboItemWidth(font, currentValue));

        maxWidth = Math.Max(maxWidth, MeasureComboItemWidth(font, BlueprintCanvas.AddVariableOptionText));
        int screenWidth = Screen.PrimaryScreen?.WorkingArea.Width ?? 1920;
        if (combo.IsHandleCreated)
            screenWidth = Screen.FromControl(combo).WorkingArea.Width;
        combo.DropDownWidth = Math.Min(Math.Max(combo.Width, maxWidth + SystemInformation.VerticalScrollBarWidth + 16), Math.Max(combo.Width, screenWidth - 32));
    }

    private static int MeasureComboItemWidth(Font font, string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return TextRenderer.MeasureText(
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
    }

    private void CommitVariableSelection(ComboBox combo)
    {
        if (suppressValueEditorEvents || string.IsNullOrWhiteSpace(editorValueKey))
            return;

        string selectedValue = combo.SelectedItem?.ToString() ?? combo.Text.Trim();

        if (string.Equals(selectedValue, BlueprintCanvas.AddVariableOptionText, StringComparison.Ordinal))
        {
            if (OwnerCanvas is BlueprintCanvas canvas &&
                canvas.TryCreateVariableForNode(this, out string createdVariableName) &&
                !string.IsNullOrWhiteSpace(createdVariableName))
            {
                PinValues[editorValueKey] = createdVariableName;
                RecalcSize();
                UpdateValueEditorLayout();
                UpdatePinBounds();
                Invalidate();
                NodeDataChanged?.Invoke(this, EventArgs.Empty);
                RefreshValueEditorChoices();
                return;
            }

            suppressValueEditorEvents = true;
            try
            {
                string previousValue = PinValues.TryGetValue(editorValueKey, out object? previous)
                    ? FormatEditableValue(previous)
                    : string.Empty;
                combo.SelectedIndex = string.IsNullOrWhiteSpace(previousValue) ? -1 : combo.FindStringExact(previousValue);
                combo.Text = previousValue;
            }
            finally
            {
                suppressValueEditorEvents = false;
            }
            return;
        }

        CommitVariableText(combo);
    }

    private void CommitVariableText(ComboBox combo)
    {
        if (suppressValueEditorEvents || string.IsNullOrWhiteSpace(editorValueKey))
            return;

        string selectedValue = combo.Text.Trim();
        if (string.Equals(selectedValue, BlueprintCanvas.AddVariableOptionText, StringComparison.Ordinal))
            return;

        string previousValue = PinValues.TryGetValue(editorValueKey, out object? previous)
            ? FormatEditableValue(previous)
            : string.Empty;
        if (string.Equals(previousValue, selectedValue, StringComparison.Ordinal))
            return;

        PinValues[editorValueKey] = selectedValue;
        RecalcSize();
        UpdateValueEditorLayout();
        UpdatePinBounds();
        Invalidate();
        NodeDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnVariableEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is ComboBox combo && e.KeyCode == Keys.Enter && !e.Control && !e.Alt && !e.Shift)
        {
            CommitVariableText(combo);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (OwnerCanvas is BlueprintCanvas canvas &&
            canvas.TryHandleEditorShortcut(e.KeyData))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private bool TryGetEditablePinValue(out string key, out object? value)
    {
        key = string.Empty;
        value = null;
        if (definition.Name.EndsWith(" Const", StringComparison.OrdinalIgnoreCase))
        {
            key = "Value";
            if (!PinValues.TryGetValue(key, out value))
            {
                value = definition.Name.Equals("Bool Const", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : string.Empty;
                PinValues[key] = value;
            }

            if (definition.Name.Equals("Bool Const", StringComparison.OrdinalIgnoreCase))
            {
                bool boolValue = false;
                if (value is bool b) boolValue = b;
                else if (value != null && bool.TryParse(Convert.ToString(value), out bool parsed)) boolValue = parsed;
                value = boolValue;
                PinValues[key] = boolValue;
            }

            return true;
        }

        string baseDefinitionName = GetBaseDefinitionNameForEditor(definition.Name);

        if ((baseDefinitionName == "Get Variable" ||
            baseDefinitionName.StartsWith("Get Variable ", StringComparison.Ordinal) ||
            baseDefinitionName == "Set Variable" ||
            baseDefinitionName.StartsWith("Set Variable ", StringComparison.Ordinal) ||
            baseDefinitionName == "Text Const"))
        {
            key = baseDefinitionName == "Text Const" ? "Value" : "EditorText";
            if (!PinValues.TryGetValue(key, out value))
            {
                value = string.Empty;
                PinValues[key] = value;
            }
            return true;
        }

        return false;
    }

    private int GetEditorHeight()
    {
        if (!hasEditableValue) return 0;
        return (int)((editorBaseHeight + editorVerticalMargin) * zoom);
    }

    private int GetFooterHeight()
    {
        return string.IsNullOrWhiteSpace(GetStatementOffsetText())
            ? 0
            : (int)(footerBaseHeight * zoom);
    }

    private int GetEditorPreferredWidth()
    {
        if (hasEditableValue && !string.IsNullOrWhiteSpace(editorValueKey))
        {
            object? stored = PinValues.TryGetValue(editorValueKey, out object? value) ? value : string.Empty;
            return Math.Max(editorBaseWidth, MeasureTextWidth(FormatEditableValue(stored) + "  ", 8.5f, FontStyle.Regular));
        }

        if (valueEditor is ComboBox comboBox)
        {
            int preferredWidth = 140;
            string selectedText = comboBox.SelectedItem?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(selectedText))
                preferredWidth = Math.Max(preferredWidth, MeasureTextWidth(selectedText + "  ", 8.5f, FontStyle.Regular));
            return preferredWidth;
        }
        if (valueEditor is TextBox textBox)
            return Math.Max(editorBaseWidth, MeasureTextWidth(textBox.Text + "  ", 8.5f, FontStyle.Regular));
        return editorBaseWidth;
    }

    private void UpdateValueEditorLayout()
    {
        if (valueEditor == null) return;
        int marginX = (int)(18 * zoom);
        int y = (int)(baseTitleHeight * zoom) + (int)(6 * zoom);
        int width = Math.Max((int)(editorBaseWidth * zoom), Width - marginX * 2);
        int height = (int)(editorBaseHeight * zoom);
        valueEditor.SetBounds(marginX, y, width, height);
        valueEditor.Font = new Font("Segoe UI", 8.5f * zoom);
    }

    public void ApplyPinValues(Dictionary<string, object>? pinValues)
    {
        PinValues = pinValues != null
            ? new Dictionary<string, object>(pinValues)
            : new Dictionary<string, object>();

        if (valueEditor == null)
        {
            BuildValueEditor();
            RecalcSize();
        }

        if (valueEditor == null)
        {
            RecalcSize();
            UpdatePinBounds();
            Invalidate();
            return;
        }

        if (valueEditor is ComboBox combo && IsVariableSelectorEditor(editorValueKey ?? string.Empty))
        {
            RefreshValueEditorChoices();
        }
        else if (valueEditor is ComboBox boolCombo && editorValueKey == "Value")
        {
            bool boolValue = false;
            object? value = PinValues.TryGetValue("Value", out object? stored) ? stored : false;
            if (value is bool b) boolValue = b;
            else if (value != null && bool.TryParse(Convert.ToString(value), out bool parsed)) boolValue = parsed;
            suppressValueEditorEvents = true;
            boolCombo.SelectedIndex = boolValue ? 0 : 1;
            suppressValueEditorEvents = false;
            PinValues["Value"] = boolValue;
        }
        else if (valueEditor is TextBox textBox)
        {
            object? value = editorValueKey != null && PinValues.TryGetValue(editorValueKey, out object? stored)
                ? stored
                : string.Empty;
            suppressValueEditorEvents = true;
            textBox.Text = FormatEditableValue(value);
            suppressValueEditorEvents = false;
        }

        UpdateValueEditorLayout();
        UpdatePinBounds();
        Invalidate();
    }

    internal void Render(Graphics g)
    {
        GraphicsState state = g.Save();
        try
        {
            g.TranslateTransform(Left, Top);
            using PaintEventArgs paintArgs = new(g, new Rectangle(Point.Empty, Size));
            OnPaint(this, paintArgs);
        }
        finally
        {
            g.Restore(state);
        }
    }

    internal Rectangle GetTitleBarBounds()
    {
        return new Rectangle(0, 0, Width, (int)(baseTitleHeight * zoom));
    }

    internal Rectangle GetCloseButtonBounds()
    {
        int btnSize = (int)(24 * zoom);
        int btnMargin = (int)(4 * zoom);
        return new Rectangle(Width - btnSize - btnMargin, btnMargin, btnSize, btnSize);
    }

    internal Rectangle GetEditorBounds()
    {
        if (!TryGetEditablePinValue(out _, out _))
            return Rectangle.Empty;

        int marginX = (int)(18 * zoom);
        int y = (int)(baseTitleHeight * zoom) + (int)(6 * zoom);
        int width = Math.Max((int)(editorBaseWidth * zoom), Width - marginX * 2);
        int height = (int)(editorBaseHeight * zoom);
        return new Rectangle(marginX, y, width, height);
    }

    internal bool TryGetOutputPinAt(Point nodePoint, out Pin? pin)
    {
        pin = OutputPins.FirstOrDefault(p => p.Bounds.Contains(nodePoint));
        return pin != null;
    }

    internal bool TryGetInputPinAt(Point nodePoint, out Pin? pin)
    {
        pin = InputPins.FirstOrDefault(p => p.Bounds.Contains(nodePoint));
        return pin != null;
    }

    internal Point GetPinWorldPosition(Pin pin)
    {
        return new Point(Location.X + pin.Center.X, Location.Y + pin.Center.Y);
    }

    internal bool TryGetEditableDescriptor(out string key, out object? value, out bool isVariableSelector, out bool isBoolean)
    {
        if (!TryGetEditablePinValue(out key, out value))
        {
            isVariableSelector = false;
            isBoolean = false;
            return false;
        }

        isVariableSelector = IsVariableSelectorEditor(key);
        isBoolean = key == "Value" && Definition.Name.Equals("Bool Const", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    internal string FormatEditorValue(object? value) => FormatEditableValue(value);

    internal bool CommitEditorValue(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        object newValue = value ?? string.Empty;
        if (PinValueEquals(PinValues.TryGetValue(key, out object? existingValue) ? existingValue : null, newValue))
            return false;

        PinValues[key] = newValue;
        RefreshLayoutMetrics();
        NodeDataChanged?.Invoke(this, EventArgs.Empty);
        OwnerCanvas?.InvalidateCanvas();
        return true;
    }

    private static bool PinValueEquals(object? left, object? right)
    {
        if (left is JsonElement leftElement)
            left = leftElement.ToString();
        if (right is JsonElement rightElement)
            right = rightElement.ToString();
        return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    public void FlushEditorValueToPinValues()
    {
        if (valueEditor == null || string.IsNullOrWhiteSpace(editorValueKey))
            return;

        if (valueEditor is TextBox textBox)
        {
            PinValues[editorValueKey] = textBox.Text;
            return;
        }

        if (valueEditor is ComboBox comboBox)
        {
            if (IsVariableSelectorEditor(editorValueKey))
            {
                string selectedValue = comboBox.Text.Trim();
                if (!string.Equals(selectedValue, BlueprintCanvas.AddVariableOptionText, StringComparison.Ordinal))
                    PinValues[editorValueKey] = selectedValue;
                return;
            }

            if (editorValueKey == "Value")
            {
                PinValues[editorValueKey] = comboBox.SelectedIndex == 0;
                return;
            }

            PinValues[editorValueKey] = comboBox.Text;
        }
    }

    public Dictionary<string, object> ExportPinValues()
    {
        FlushEditorValueToPinValues();
        Dictionary<string, object> exported = new(PinValues);

        if (hasEditableValue &&
            !string.IsNullOrWhiteSpace(editorValueKey) &&
            definition.Name.EndsWith(" Const", StringComparison.OrdinalIgnoreCase) &&
            PinValues.TryGetValue(editorValueKey, out object? rawValue))
        {
            exported["__RawConstValueText"] = FormatEditableValue(rawValue);
        }

        if (valueEditor is TextBox textBox && !string.IsNullOrWhiteSpace(editorValueKey))
        {
            exported[editorValueKey] = textBox.Text;
            if (definition.Name.EndsWith(" Const", StringComparison.OrdinalIgnoreCase))
                exported["__RawConstValueText"] = textBox.Text;
        }

        if (valueEditor is ComboBox comboBox && editorValueKey == "Value" && !IsVariableSelectorEditor(editorValueKey))
        {
            exported[editorValueKey] = comboBox.SelectedIndex == 0;
        }

        return exported;
    }

    public void ApplyMetaData(Dictionary<string, string>? metaData)
    {
        MetaData = metaData != null
            ? new Dictionary<string, string>(metaData)
            : new Dictionary<string, string>();
        RecalcSize();
        UpdateValueEditorLayout();
        UpdatePinBounds();
        Invalidate();
    }

    public void RefreshLayoutMetrics()
    {
        if (valueEditor == null)
            BuildValueEditor();

        RefreshValueEditorChoices();
        RecalcSize();
        UpdateValueEditorLayout();
        UpdatePinBounds();
        PerformLayout();
        Invalidate();
    }

    public void SetStatementOffset(int? offset)
    {
        if (offset.HasValue)
            MetaData["StatementIndex"] = offset.Value.ToString(CultureInfo.InvariantCulture);
        else
            MetaData.Remove("StatementIndex");

        RecalcSize();
        UpdateValueEditorLayout();
        UpdatePinBounds();
        Invalidate();
    }

    private static string FormatEditableValue(object? value)
    {
        if (value == null) return string.Empty;
        return value switch
        {
            float f => f.ToString("0.0######", CultureInfo.InvariantCulture),
            double d => d.ToString("0.0###############", CultureInfo.InvariantCulture),
            decimal m => m.ToString("0.0#########################", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private (Color Start, Color End) GetTitleColors()
    {
        if (definition.Name.EndsWith(" Const", StringComparison.OrdinalIgnoreCase))
            return (Color.FromArgb(185, 155, 55), Color.FromArgb(145, 120, 35));

        string baseDefinitionName = GetBaseDefinitionNameForEditor(definition.Name);
        if (baseDefinitionName == "Get Variable" ||
            baseDefinitionName.StartsWith("Get Variable ", StringComparison.Ordinal) ||
            baseDefinitionName == "Set Variable" ||
            baseDefinitionName.StartsWith("Set Variable ", StringComparison.Ordinal))
            return (Color.FromArgb(70, 150, 85), Color.FromArgb(45, 110, 60));

        return (Color.FromArgb(80, 120, 200), Color.FromArgb(60, 90, 160));
    }

    private string? GetStatementOffsetText()
    {
        if (!MetaData.TryGetValue("StatementIndex", out string? offsetText) || string.IsNullOrWhiteSpace(offsetText))
            return null;

        return $"@{offsetText}";
    }

    private void DrawStatementOffset(Graphics g)
    {
        string? offsetText = GetStatementOffsetText();
        if (string.IsNullOrWhiteSpace(offsetText)) return;

        using Font font = new("Segoe UI", 7.5f * zoom);
        SizeF textSize = g.MeasureString(offsetText, font);
        Point textLocation = new(
            Math.Max(4, Width - (int)Math.Ceiling(textSize.Width) - (int)(8 * zoom)),
            Math.Max(4, Height - (int)Math.Ceiling(textSize.Height) - (int)(6 * zoom)));

        using SolidBrush brush = new(Color.FromArgb(185, 185, 195));
        g.DrawString(offsetText, font, brush, textLocation);
    }

    private static NodeDefinition CloneDefinition(NodeDefinition def)
    {
        NodeDefinition clone = new NodeDefinition
        {
            Name = NormalizeLegacyDefinitionName(def.Name),
            InputPins = def.InputPins.Select(pin => new Pin(pin.Name, pin.Direction, pin.Type)).ToList(),
            OutputPins = def.OutputPins.Select(pin => new Pin(pin.Name, pin.Direction, pin.Type)).ToList(),
            SourceExpressionTemplateJson = def.SourceExpressionTemplateJson,
            SourceExpressionType = def.SourceExpressionType,
            ReferenceDescriptors = def.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList(),
            PinSchemas = def.PinSchemas.Select(BlueprintModelCloner.Clone).ToList(),
            PropertyTemplateJsonByName = def.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            RepresentativeCallTemplateJson = def.RepresentativeCallTemplateJson,
            RepresentativeCallExpressionType = def.RepresentativeCallExpressionType,
            RepresentativeCallReferenceDescriptors = def.RepresentativeCallReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList()
        };

        EnsureCompatibilityPins(clone);
        return clone;
    }

    private static void EnsureCompatibilityPins(NodeDefinition definition)
    {
        string baseDefinitionName = GetBaseDefinitionNameForEditor(definition.Name);
        switch (baseDefinitionName)
        {
            case "Return":
                EnsurePin(definition.InputPins, "In", PinDirection.Input, PinType.Exec);
                EnsurePin(definition.InputPins, "Return", PinDirection.Input, PinType.Data);
                EnsurePin(definition.OutputPins, "Out", PinDirection.Output, PinType.Exec);
                break;
            case "Jump":
                EnsurePin(definition.InputPins, "In", PinDirection.Input, PinType.Exec);
                EnsurePin(definition.OutputPins, "Out", PinDirection.Output, PinType.Exec);
                EnsurePin(definition.OutputPins, "To", PinDirection.Output, PinType.Exec);
                break;
            case "Jump If Not":
                EnsurePin(definition.InputPins, "In", PinDirection.Input, PinType.Exec);
                EnsurePin(definition.InputPins, "Condition", PinDirection.Input, PinType.Data);
                EnsurePin(definition.OutputPins, "Out", PinDirection.Output, PinType.Exec);
                EnsurePin(definition.OutputPins, "To", PinDirection.Output, PinType.Exec);
                break;
            case "Push Flow":
                EnsurePin(definition.InputPins, "In", PinDirection.Input, PinType.Exec);
                EnsurePin(definition.OutputPins, "Out", PinDirection.Output, PinType.Exec);
                EnsurePin(definition.OutputPins, "To", PinDirection.Output, PinType.Exec);
                break;
        }
    }

    private static void EnsurePin(List<Pin> pins, string pinName, PinDirection direction, PinType pinType)
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

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // 关闭按钮
            if (closeButtonRect.Contains(e.Location))
            {
                NodeDeleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            // 标题栏拖拽
            if (titleBarRect.Contains(e.Location))
            {
                if (OwnerCanvas is BlueprintCanvas canvas)
                    _ = canvas.BeginNodeDrag(this, e.Location);
                dragging = true;
                dragStart = e.Location;
                Capture = true;
                Cursor = Cursors.SizeAll;
                return;
            }

            // 检查是否点击输出引脚（开始连线）
            Pin clickedOutput = OutputPins.FirstOrDefault(p => p.Bounds.Contains(e.Location));
            if (clickedOutput != null)
            {
                StartConnection?.Invoke(this, clickedOutput);
            }
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (dragging && Parent != null)
        {
            if (OwnerCanvas is BlueprintCanvas canvas && canvas.IsNodeDragActive(this))
            {
                canvas.UpdateNodeDrag(this, e.Location);
                return;
            }

            int newX = Left + (e.X - dragStart.X);
            int newY = Top + (e.Y - dragStart.Y);
            newX = Math.Max(0, newX);
            newY = Math.Max(0, newY);
            Location = new Point(newX, newY);

            int requiredWidth = newX + Width + NodeSurfacePadding;
            int requiredHeight = newY + Height + NodeSurfacePadding;
            if (requiredWidth > Parent.Width || requiredHeight > Parent.Height)
            {
                Parent.Size = new Size(
                    Math.Max(Parent.Width, requiredWidth),
                    Math.Max(Parent.Height, requiredHeight));
            }

            Parent.Invalidate();
        }
    }

    private void OnMouseUp(object sender, MouseEventArgs e)
    {
        if (dragging)
        {
            if (OwnerCanvas is BlueprintCanvas canvas)
                canvas.EndNodeDrag(this, e.Location);
            dragging = false;
            Capture = false;
            Cursor = Cursors.Default;
        }

        // 检查是否点击输入引脚（结束连线）
        Pin clickedInput = InputPins.FirstOrDefault(p => p.Bounds.Contains(e.Location));
        if (clickedInput != null)
        {
            EndConnection?.Invoke(this, clickedInput);
        }
    }

    public Point GetPinScreenPosition(Pin pin)
    {
        return PointToScreen(pin.Center);
    }
}

// ========== 画布 ==========
public class BlueprintCanvas : Panel
{
    private const int BoxSelectEdgeScrollMargin = 28;
    private const int BoxSelectEdgeScrollSpeed = 24;
    private const int CanvasExpandStep = 640;
    private const int LayoutNodeSpacing = 24;
    private const int LayoutHorizontalSpacing = 48;
    private const int LayoutEventHorizontalSpacing = 700;
    private const int FreeCanvasMargin = 240;
    public const string AddVariableOptionText = "添加变量...";

    private sealed class CanvasVariableEntry
    {
        public BlueprintVariableDefinition Definition { get; set; } = new();
    }

    private sealed class ClipboardPayload
    {
        public List<SerializableNode> Nodes { get; set; } = new();
        public List<ConnectionData> Connections { get; set; } = new();
        public Dictionary<string, NodeDefinition> Definitions { get; set; } = new(StringComparer.Ordinal);
        public Point Origin { get; set; }
        public Dictionary<string, BlueprintFunctionTemplate> FunctionTemplates { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, LoadedPropertyTemplate> LoadedPropertyTemplates { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class UndoState
    {
        public BlueprintData Data { get; init; } = new();
        public BlueprintAssetContext AssetContext { get; init; } = BlueprintAssetContext.CreateFallback(string.Empty);
        public Dictionary<string, NodeDefinition> Definitions { get; init; } = new(StringComparer.Ordinal);
        public PointF ViewportOrigin { get; init; }
        public float Zoom { get; init; }
    }

    private sealed class DataAttachment
    {
        public required BlueprintNode Node { get; init; }
        public required int PinOrder { get; init; }
        public required int AnchorTop { get; init; }
    }

    private const string BlueprintClipboardPrefix = "BPEditor.BlueprintClipboard.v1:";
    private static string? inMemoryClipboardPayload;

    private sealed class CanvasSurface : Panel
    {
        public CanvasSurface()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(50, 50, 65);
        }
    }

    private sealed class MinimapOverlay : Control
    {
        public MinimapOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(50, 50, 65);
            TabStop = false;
        }
    }

    private sealed class BlueprintSpatialIndex
    {
        private const int CellSize = 512;
        private readonly Dictionary<Point, List<BlueprintNode>> cells = new();

        public void Rebuild(IEnumerable<BlueprintNode> sourceNodes)
        {
            cells.Clear();
            foreach (BlueprintNode node in sourceNodes)
            {
                foreach (Point cell in EnumerateCells(node.Bounds))
                {
                    if (!cells.TryGetValue(cell, out List<BlueprintNode>? bucket))
                    {
                        bucket = new List<BlueprintNode>();
                        cells[cell] = bucket;
                    }
                    bucket.Add(node);
                }
            }
        }

        public List<BlueprintNode> Query(Rectangle worldBounds)
        {
            HashSet<BlueprintNode> result = new();
            foreach (Point cell in EnumerateCells(worldBounds))
            {
                if (!cells.TryGetValue(cell, out List<BlueprintNode>? bucket))
                    continue;

                foreach (BlueprintNode node in bucket)
                {
                    if (node.Bounds.IntersectsWith(worldBounds))
                        result.Add(node);
                }
            }

            return result.ToList();
        }

        private static IEnumerable<Point> EnumerateCells(Rectangle bounds)
        {
            int left = FloorDiv(bounds.Left, CellSize);
            int right = FloorDiv(Math.Max(bounds.Left, bounds.Right - 1), CellSize);
            int top = FloorDiv(bounds.Top, CellSize);
            int bottom = FloorDiv(Math.Max(bounds.Top, bounds.Bottom - 1), CellSize);

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                    yield return new Point(x, y);
            }
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }
    }

    private List<BlueprintNode> nodes = new List<BlueprintNode>();
    private List<Connection> connections = new List<Connection>();
    private Connection? selectedConnection;
    private readonly HashSet<BlueprintNode> selectedNodes = new();
    private readonly HashSet<Connection> selectedConnections = new();
    private int suppressOffsetRefreshDepth = 0;
    private bool offsetRefreshRunning = false;
    private bool offsetRefreshPending = false;
    private readonly CanvasSurface surface;
    private readonly MinimapOverlay minimapOverlay;
    private readonly BlueprintSpatialIndex spatialIndex = new();
    private readonly System.Windows.Forms.Timer edgeScrollTimer;
    private bool spatialIndexDirty = true;

    private bool isConnecting = false;
    private BlueprintNode startNode;
    private Pin startPin;
    private Point tempLineEnd;
    private Point connectionStartCanvasPoint = Point.Empty;
    private bool connectionDragStarted = false;
    private PointF viewportOrigin = PointF.Empty;
    private Control? activeValueEditor;
    private BlueprintNode? activeEditorNode;
    private string? activeEditorKey;

    // 缩放相关
    private float zoom = 1.0f;
    private const float MIN_ZOOM = 0.5f;
    private const float MAX_ZOOM = 2.0f;

    // 画布平移相关
    private bool isPanningCanvas = false;      // 是否正在平移画布
    private Point panStartPointCanvas;         // 平移起始点（视口坐标系）
    private PointF panStartViewportOrigin;
    private bool isDraggingMinimap = false;
    private Point minimapDragOffset;
    private Rectangle minimapViewportRect = Rectangle.Empty;
    private bool isMinimapCollapsed = false;

    private const int MinimapWidth = 220;
    private const int MinimapHeight = 150;
    private const int MinimapMargin = 12;
    private const int MinimapCollapsedWidth = 20;
    private const int MinimapCollapsedHeight = 84;
    private const int MinimapToggleWidth = 18;
    private bool isBoxSelecting = false;
    private bool isDraggingNodes = false;
    private BlueprintNode? dragAnchorNode;
    private Point nodeDragStartCanvas = Point.Empty;
    private Dictionary<BlueprintNode, Point> nodeDragStartLocations = new();
    private Point selectionStartSurface = Point.Empty;
    private Point selectionCurrentSurface = Point.Empty;
    private Point lastPointerCanvasPoint = Point.Empty;
    private const int SelectionDragThreshold = 4;
    private int pasteSequence = 0;
    private long nextConnectionSequence = 1;
    private BlueprintAssetContext assetContext = BlueprintAssetContext.CreateFallback(string.Empty);
    private readonly Dictionary<string, CanvasVariableEntry> variableEntriesByScope = new(StringComparer.Ordinal);
    private readonly Stack<UndoState> undoStack = new();
    private readonly Stack<UndoState> redoStack = new();
    private bool isRestoringHistory = false;
    private bool suppressHistoryCapture = false;
    private bool hasPendingEditorUndoState = false;
    private UndoState? pendingEditorUndoState = null;
    private const int MaxUndoStates = 100;

    public BlueprintAssetContext AssetContext
    {
        get => assetContext;
        set
        {
            assetContext = BlueprintAssetContext.CloneOrFallback(value);
            RebuildVariableCatalog();
            RefreshVariableEditors();
        }
    }
    public Func<BlueprintData, Dictionary<Guid, int>>? OffsetCalculator { get; set; }

    public event Action<string> StatusMessage;

    private readonly Pen ExecPen = new(Color.FromArgb(255, 200, 100), 2);
    private readonly Pen DataPen = new(Color.FromArgb(100, 200, 100), 2);

    public event Action<object> BlueprintEdited;

    public float Zoom
    {
        get => zoom;
        set
        {
            float newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, value));
            if (Math.Abs(zoom - newZoom) > 0.01f)
            {
                zoom = newZoom;
                UpdateScrollArea();
                InvalidateCanvas();
                // 显示缩放状态
                StatusMessage?.Invoke($"缩放比例: {zoom * 100:F0}%");
            }
        }
    }

    public BlueprintCanvas(BlueprintAssetContext assetContext)
    {
        AllowDrop = true;
        TabStop = true;
        AutoScroll = false;
        BackColor = Color.FromArgb(50, 50, 65);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);

        surface = new CanvasSurface
        {
            Location = new Point(0, 0),
            Size = ClientSize,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        surface.Paint += Surface_Paint;
        surface.MouseMove += Canvas_MouseMove;
        surface.MouseDown += Canvas_MouseDown;
        surface.MouseUp += Canvas_MouseUp;
        surface.MouseWheel += Canvas_MouseWheel;
        surface.KeyDown += Canvas_KeyDown;
        surface.Resize += (s, e) => surface.Invalidate();
        Controls.Add(surface);

        AssetContext = assetContext ?? BlueprintAssetContext.CreateFallback(string.Empty);

        minimapOverlay = new MinimapOverlay();
        minimapOverlay.Paint += MinimapOverlay_Paint;
        minimapOverlay.MouseDown += MinimapOverlay_MouseDown;
        minimapOverlay.MouseMove += MinimapOverlay_MouseMove;
        minimapOverlay.MouseUp += MinimapOverlay_MouseUp;
        Controls.Add(minimapOverlay);
        minimapOverlay.BringToFront();

        edgeScrollTimer = new System.Windows.Forms.Timer { Interval = 16 };
        edgeScrollTimer.Tick += EdgeScrollTimer_Tick;

        DragEnter += (s, e) => { if (e.Data.GetDataPresent(typeof(NodeDefinition))) e.Effect = DragDropEffects.Copy; };
        DragDrop += (s, e) =>
        {
            if (e.Data.GetDataPresent(typeof(NodeDefinition)))
            {
                var def = (NodeDefinition)e.Data.GetData(typeof(NodeDefinition));
                var mousePos = PointToClient(new Point(e.X, e.Y));
                CreateNode(def, mousePos);
            }
        };

        MouseMove += Canvas_MouseMove;
        MouseDown += Canvas_MouseDown;
        MouseUp += Canvas_MouseUp;
        MouseWheel += Canvas_MouseWheel;
        KeyDown += Canvas_KeyDown;
        Resize += BlueprintCanvas_Resize;

        ExecPen.StartCap = LineCap.Round;
        ExecPen.EndCap = LineCap.ArrowAnchor;
        DataPen.StartCap = LineCap.Round;
        DataPen.EndCap = LineCap.ArrowAnchor;
    }

    internal void InvalidateCanvas()
    {
        surface.Invalidate();
        minimapOverlay.Invalidate();
    }

    private Point ScreenToWorld(Point screenPoint)
    {
        return new Point(
            (int)Math.Round(viewportOrigin.X + screenPoint.X / zoom),
            (int)Math.Round(viewportOrigin.Y + screenPoint.Y / zoom));
    }

    private Point WorldToScreen(Point worldPoint)
    {
        return new Point(
            (int)Math.Round((worldPoint.X - viewportOrigin.X) * zoom),
            (int)Math.Round((worldPoint.Y - viewportOrigin.Y) * zoom));
    }

    private Rectangle GetVisibleWorldRectangle(int margin = 160)
    {
        float width = ClientSize.Width / zoom;
        float height = ClientSize.Height / zoom;
        return Rectangle.Round(new RectangleF(
            viewportOrigin.X - margin,
            viewportOrigin.Y - margin,
            width + margin * 2,
            height + margin * 2));
    }

    private IEnumerable<BlueprintNode> EnumerateVisibleNodes(int margin = 160)
    {
        Rectangle visibleWorld = GetVisibleWorldRectangle(margin);
        if (spatialIndexDirty)
        {
            spatialIndex.Rebuild(nodes);
            spatialIndexDirty = false;
        }

        HashSet<BlueprintNode> visibleNodes = new(spatialIndex.Query(visibleWorld));
        foreach (BlueprintNode node in nodes)
        {
            if (visibleNodes.Contains(node))
                yield return node;
        }
    }

    private void MarkSpatialIndexDirty()
    {
        spatialIndexDirty = true;
    }

    private void CaptureUndoState()
    {
        if (isRestoringHistory || suppressHistoryCapture)
            return;

        undoStack.Push(CreateUndoState());
        TrimUndoStack();
        redoStack.Clear();
        hasPendingEditorUndoState = false;
        pendingEditorUndoState = null;
    }

    private UndoState CreateUndoState()
    {
        bool previousSuppressHistory = suppressHistoryCapture;
        suppressHistoryCapture = true;
        try
        {
            return new UndoState
            {
                Data = CloneBlueprintData(BuildBlueprintDataSnapshot()),
                AssetContext = BlueprintModelCloner.Clone(AssetContext),
                Definitions = nodes
                    .GroupBy(node => node.Definition.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => CloneNodeDefinition(group.First().Definition), StringComparer.Ordinal),
                ViewportOrigin = viewportOrigin,
                Zoom = zoom
            };
        }
        finally
        {
            suppressHistoryCapture = previousSuppressHistory;
        }
    }

    private void TrimUndoStack()
    {
        if (undoStack.Count <= MaxUndoStates)
            return;

        UndoState[] states = undoStack.ToArray();
        undoStack.Clear();
        for (int i = Math.Min(states.Length, MaxUndoStates) - 1; i >= 0; i--)
            undoStack.Push(states[i]);
    }

    private bool Undo()
    {
        CloseActiveValueEditor(commit: true);
        if (undoStack.Count == 0)
        {
            StatusMessage?.Invoke("撤销失败: 没有可撤销的操作");
            return false;
        }

        redoStack.Push(CreateUndoState());
        RestoreUndoState(undoStack.Pop(), "已撤销");
        return true;
    }

    private bool Redo()
    {
        CloseActiveValueEditor(commit: true);
        if (redoStack.Count == 0)
        {
            StatusMessage?.Invoke("重做失败: 没有可重做的操作");
            return false;
        }

        undoStack.Push(CreateUndoState());
        TrimUndoStack();
        RestoreUndoState(redoStack.Pop(), "已重做");
        return true;
    }

    private void RestoreUndoState(UndoState state, string statusMessage)
    {
        isRestoringHistory = true;
        bool previousSuppressHistory = suppressHistoryCapture;
        suppressHistoryCapture = true;
        try
        {
            AssetContext = BlueprintModelCloner.Clone(state.AssetContext);
            ImportData(CloneBlueprintData(state.Data), BuildDefinitionsForImportState(state), preserveImportedLayout: true);
            viewportOrigin = state.ViewportOrigin;
            zoom = state.Zoom;
            UpdateScrollArea();
            InvalidateCanvas();
            StatusMessage?.Invoke(statusMessage);
        }
        finally
        {
            suppressHistoryCapture = previousSuppressHistory;
            isRestoringHistory = false;
        }

        RefreshStatementOffsets();
        BlueprintEdited?.Invoke(this);
    }

    private Dictionary<string, NodeDefinition> BuildDefinitionsForImportState(UndoState state)
    {
        BlueprintData data = state.Data;
        Dictionary<string, NodeDefinition> definitions = state.Definitions.ToDictionary(
            kv => kv.Key,
            kv => CloneNodeDefinition(kv.Value),
            StringComparer.Ordinal);

        foreach (BlueprintNode node in nodes)
        {
            if (!definitions.ContainsKey(node.Definition.Name))
                definitions[node.Definition.Name] = CloneNodeDefinition(node.Definition);
        }

        foreach (SerializableNode node in data.Nodes)
        {
            if (!definitions.ContainsKey(node.DefinitionName))
                definitions[node.DefinitionName] = CreateFallbackDefinitionFromSerializableNode(node);
        }

        return definitions;
    }

    private static NodeDefinition CloneNodeDefinition(NodeDefinition definition)
    {
        return new NodeDefinition
        {
            Name = definition.Name,
            InputPins = definition.InputPins.Select(pin => new Pin(pin.Name, pin.Direction, pin.Type)).ToList(),
            OutputPins = definition.OutputPins.Select(pin => new Pin(pin.Name, pin.Direction, pin.Type)).ToList(),
            SourceExpressionTemplateJson = definition.SourceExpressionTemplateJson,
            SourceExpressionType = definition.SourceExpressionType,
            ReferenceDescriptors = definition.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList(),
            PinSchemas = definition.PinSchemas.Select(BlueprintModelCloner.Clone).ToList(),
            PropertyTemplateJsonByName = definition.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            RepresentativeCallTemplateJson = definition.RepresentativeCallTemplateJson,
            RepresentativeCallExpressionType = definition.RepresentativeCallExpressionType,
            RepresentativeCallReferenceDescriptors = definition.RepresentativeCallReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList()
        };
    }

    private static NodeDefinition CreateFallbackDefinitionFromSerializableNode(SerializableNode node)
    {
        return new NodeDefinition
        {
            Name = node.DefinitionName,
            InputPins = InferPinsFromSerializableNode(node, PinDirection.Input),
            OutputPins = InferPinsFromSerializableNode(node, PinDirection.Output),
            ReferenceDescriptors = node.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList(),
            PinSchemas = node.PinSchemas.Select(BlueprintModelCloner.Clone).ToList(),
            PropertyTemplateJsonByName = node.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
        };
    }

    private static List<Pin> InferPinsFromSerializableNode(SerializableNode node, PinDirection direction)
    {
        List<Pin> pins = [];
        bool isEvent = node.DefinitionName.StartsWith("Event ", StringComparison.Ordinal);
        bool isCall = node.DefinitionName.StartsWith("Call ", StringComparison.Ordinal);
        if (direction == PinDirection.Input)
        {
            if (!isEvent)
                pins.Add(new Pin("In", PinDirection.Input, PinType.Exec));
            foreach (string pinName in node.PinValues.Keys.Where(name => !string.Equals(name, "EditorText", StringComparison.Ordinal)))
                pins.Add(new Pin(pinName, PinDirection.Input, PinType.Data));
        }
        else
        {
            pins.Add(new Pin(isEvent ? "Exec" : "Out", PinDirection.Output, PinType.Exec));
            if (isCall)
                pins.Add(new Pin("Result", PinDirection.Output, PinType.Data));
        }

        return pins;
    }

    private static BlueprintData CloneBlueprintData(BlueprintData data)
    {
        return new BlueprintData
        {
            Nodes = data.Nodes.Select(CloneSerializableNode).ToList(),
            Connections = data.Connections.Select(conn => new ConnectionData
            {
                FromNodeId = conn.FromNodeId,
                FromPinName = conn.FromPinName,
                ToNodeId = conn.ToNodeId,
                ToPinName = conn.ToPinName,
                Sequence = conn.Sequence
            }).ToList()
        };
    }

    private static SerializableNode CloneSerializableNode(SerializableNode node)
    {
        return new SerializableNode
        {
            Id = node.Id,
            DefinitionName = node.DefinitionName,
            Location = node.Location,
            PinValues = ClonePinValues(node.PinValues),
            MetaData = new Dictionary<string, string>(node.MetaData, StringComparer.Ordinal),
            ReferenceDescriptors = node.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList(),
            PinSchemas = node.PinSchemas.Select(BlueprintModelCloner.Clone).ToList(),
            PropertyTemplateJsonByName = node.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
        };
    }

    private static Dictionary<string, object> ClonePinValues(Dictionary<string, object> values)
    {
        Dictionary<string, object> cloned = new(StringComparer.Ordinal);
        foreach ((string key, object value) in values)
            cloned[key] = ClonePinValue(value);
        return cloned;
    }

    private static object ClonePinValue(object value)
    {
        if (value is JsonElement element)
            return element.Clone();
        if (value is JsonNode node)
            return node.DeepClone();
        return value;
    }

    private static bool PinValuesEqual(Dictionary<string, object> left, Dictionary<string, object> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach ((string key, object leftValue) in left)
        {
            if (!right.TryGetValue(key, out object? rightValue))
                return false;
            if (!string.Equals(NormalizePinValueForComparison(leftValue), NormalizePinValueForComparison(rightValue), StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string NormalizePinValueForComparison(object? value)
    {
        return value switch
        {
            null => string.Empty,
            JsonElement element => element.ToString(),
            JsonNode node => node.ToJsonString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private void RebuildVariableCatalog()
    {
        variableEntriesByScope.Clear();

        foreach ((string propertyName, LoadedPropertyTemplate template) in AssetContext.ClassLoadedPropertyTemplatesByName)
        {
            RegisterVariableEntry(template, sourceFunctionName: null, ownerKindFallback: "Class");
        }

        foreach ((string functionName, BlueprintFunctionTemplate template) in AssetContext.FunctionTemplates)
        {
            foreach ((string _, LoadedPropertyTemplate propertyTemplate) in template.LoadedPropertyTemplatesByName)
            {
                RegisterVariableEntry(propertyTemplate, functionName, "Function");
            }
        }
    }

    private void RegisterVariableEntry(LoadedPropertyTemplate template, string? sourceFunctionName, string ownerKindFallback)
    {
        if (!TryCreateVariableDefinition(template, sourceFunctionName, ownerKindFallback, out BlueprintVariableDefinition? definition) ||
            definition == null)
        {
            return;
        }

        string scopeKey = BuildVariableScopeKey(definition.SourceFunctionName, definition.Name);
        if (!variableEntriesByScope.ContainsKey(scopeKey))
        {
            variableEntriesByScope[scopeKey] = new CanvasVariableEntry
            {
                Definition = definition
            };
        }
    }

    private static string BuildVariableScopeKey(string? functionName, string variableName)
    {
        return $"{functionName ?? string.Empty}::{variableName}";
    }

    private static bool TryCreateVariableDefinition(
        LoadedPropertyTemplate template,
        string? sourceFunctionName,
        string ownerKindFallback,
        out BlueprintVariableDefinition? definition)
    {
        definition = null;
        if (template == null ||
            string.IsNullOrWhiteSpace(template.PropertyName) ||
            string.IsNullOrWhiteSpace(template.PropertyTemplateJson))
        {
            return false;
        }

        JsonObject? propertyObj = JsonNode.Parse(template.PropertyTemplateJson) as JsonObject;
        if (propertyObj == null)
            return false;

        string propertyName = propertyObj["Name"]?.GetValue<string>() ?? template.PropertyName;
        if (string.IsNullOrWhiteSpace(propertyName) ||
            string.Equals(propertyName, "EntryPoint", StringComparison.Ordinal) ||
            string.Equals(propertyName, "UberGraphFrame", StringComparison.Ordinal))
        {
            return false;
        }

        string propertyFlags = propertyObj["PropertyFlags"]?.GetValue<string>() ?? string.Empty;
        if (propertyFlags.Contains("CPF_Parm", StringComparison.Ordinal))
            return false;

        string serializedType = propertyObj["SerializedType"]?.GetValue<string>() ?? string.Empty;
        if (!TryMapSerializedTypeToVariableType(serializedType, out BlueprintVariableType variableType))
        {
            return false;
        }

        string defaultValueText = variableType == BlueprintVariableType.Bool &&
            propertyObj["Value"] is JsonValue boolValueNode &&
            boolValueNode.TryGetValue<bool>(out bool boolValue)
                ? (boolValue ? "True" : "False")
                : string.Empty;

        definition = new BlueprintVariableDefinition
        {
            Name = propertyName,
            VariableType = variableType,
            DefaultValueText = defaultValueText,
            SerializedTypeName = serializedType,
            OwnerKind = string.IsNullOrWhiteSpace(template.OwnerKind) ? ownerKindFallback : template.OwnerKind,
            OwnerObjectName = template.OwnerObjectName,
            SourceFunctionName = sourceFunctionName ?? template.SourceFunctionName,
            PropertyTemplateJson = template.PropertyTemplateJson
        };
        return true;
    }

    private static bool TryMapSerializedTypeToVariableType(string serializedType, out BlueprintVariableType variableType)
    {
        variableType = serializedType switch
        {
            "IntProperty" => BlueprintVariableType.Int,
            "BoolProperty" => BlueprintVariableType.Bool,
            "FloatProperty" => BlueprintVariableType.Float,
            "DoubleProperty" => BlueprintVariableType.Double,
            "ByteProperty" => BlueprintVariableType.Byte,
            "StrProperty" => BlueprintVariableType.String,
            "NameProperty" => BlueprintVariableType.Name,
            "TextProperty" => BlueprintVariableType.Text,
            "ObjectProperty" or "ClassProperty" or "InterfaceProperty" => BlueprintVariableType.Object,
            "SoftObjectProperty" or "SoftClassProperty" => BlueprintVariableType.SoftObject,
            "ArrayProperty" => BlueprintVariableType.Array,
            "EnumProperty" => BlueprintVariableType.Enum,
            "StructProperty" => BlueprintVariableType.Struct,
            _ => BlueprintVariableType.Unknown
        };

        return !string.IsNullOrWhiteSpace(serializedType);
    }

    private void RefreshVariableEditors()
    {
        foreach (BlueprintNode node in nodes)
        {
            node.RefreshValueEditorChoices();
        }
    }

    internal IReadOnlyList<string> GetAvailableVariableNames(BlueprintNode node)
    {
        string? functionName = ResolveNodeFunctionName(node);
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (CanvasVariableEntry entry in EnumerateVariableEntriesForNode(functionName))
        {
            string variableName = entry.Definition.Name;
            if (!seen.Add(variableName))
                continue;
            names.Add(variableName);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    internal bool TryCreateVariableForNode(BlueprintNode node, out string variableName)
    {
        variableName = string.Empty;

        using AddBlueprintVariableDialog dialog = new(GetAllVariableNames(), BuildExemplarDefinitionsByType());
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return false;

        if (!hasPendingEditorUndoState && !isRestoringHistory && !suppressHistoryCapture)
        {
            pendingEditorUndoState = CreateUndoState();
            hasPendingEditorUndoState = true;
        }

        BlueprintVariableDefinition definition = dialog.Result;
        string? functionName = ResolveNodeFunctionName(node);
        LoadedPropertyTemplate template = CreateLoadedPropertyTemplate(definition, functionName);
        ApplyCreatedVariableTemplate(template, functionName);

        variableName = definition.Name;
        RebuildVariableCatalog();
        RefreshVariableEditors();
        return true;
    }

    private IEnumerable<string> GetAllVariableNames()
    {
        return variableEntriesByScope.Values
            .Select(entry => entry.Definition.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyDictionary<BlueprintVariableType, List<BlueprintVariableDefinition>> BuildExemplarDefinitionsByType()
    {
        Dictionary<BlueprintVariableType, List<BlueprintVariableDefinition>> grouped = [];
        foreach (CanvasVariableEntry entry in variableEntriesByScope.Values)
        {
            BlueprintVariableDefinition definition = entry.Definition;
            if (definition.VariableType == BlueprintVariableType.Unknown ||
                string.IsNullOrWhiteSpace(definition.PropertyTemplateJson))
            {
                continue;
            }

            if (!grouped.TryGetValue(definition.VariableType, out List<BlueprintVariableDefinition>? items))
            {
                items = [];
                grouped[definition.VariableType] = items;
            }

            if (!items.Any(existing => string.Equals(existing.Name, definition.Name, StringComparison.Ordinal)))
                items.Add(definition);
        }

        return grouped;
    }

    private IEnumerable<CanvasVariableEntry> EnumerateVariableEntriesForNode(string? functionName)
    {
        if (!string.IsNullOrWhiteSpace(functionName))
        {
            foreach ((_, CanvasVariableEntry entry) in variableEntriesByScope)
            {
                if (string.Equals(entry.Definition.SourceFunctionName, functionName, StringComparison.Ordinal))
                    yield return entry;
            }
        }

        foreach ((_, CanvasVariableEntry entry) in variableEntriesByScope)
        {
            if (string.IsNullOrWhiteSpace(entry.Definition.SourceFunctionName))
                yield return entry;
        }

        if (!string.IsNullOrWhiteSpace(functionName))
            yield break;

        foreach ((_, CanvasVariableEntry entry) in variableEntriesByScope)
        {
            if (!string.IsNullOrWhiteSpace(entry.Definition.SourceFunctionName))
                yield return entry;
        }
    }

    private static string? ResolveNodeFunctionName(BlueprintNode node)
    {
        return node.MetaData.TryGetValue("FunctionName", out string? functionName) &&
            !string.IsNullOrWhiteSpace(functionName)
                ? functionName
                : null;
    }

    private void ApplyCreatedVariableTemplate(LoadedPropertyTemplate template, string? functionName)
    {
        if (!string.IsNullOrWhiteSpace(functionName))
        {
            if (!AssetContext.FunctionTemplates.TryGetValue(functionName, out BlueprintFunctionTemplate? functionTemplate))
            {
                functionTemplate = new BlueprintFunctionTemplate
                {
                    FunctionName = functionName
                };
                AssetContext.FunctionTemplates[functionName] = functionTemplate;
            }

            functionTemplate.LoadedPropertyTemplatesByName[template.PropertyName] = BlueprintModelCloner.Clone(template);
            return;
        }

        AssetContext.ClassLoadedPropertyTemplatesByName[template.PropertyName] = BlueprintModelCloner.Clone(template);
    }

    private LoadedPropertyTemplate CreateLoadedPropertyTemplate(BlueprintVariableDefinition definition, string? functionName)
    {
        JsonObject templateJson = CreateVariablePropertyTemplateJson(definition);
        return new LoadedPropertyTemplate
        {
            PropertyName = definition.Name,
            PropertyTemplateJson = templateJson.ToJsonString(),
            OwnerObjectName = functionName ?? AssetContext.ClassExportObjectName ?? string.Empty,
            OwnerKind = string.IsNullOrWhiteSpace(functionName) ? "Class" : "Function",
            SourceFunctionName = functionName,
            OriginalOrdinal = -1,
            CreatedSequence = DateTime.UtcNow.Ticks,
            IsUserCreated = true,
            ReferenceDescriptors = new List<NodeReferenceDescriptor>()
        };
    }

    private JsonObject CreateVariablePropertyTemplateJson(BlueprintVariableDefinition definition)
    {
        JsonObject? exemplar = !string.IsNullOrWhiteSpace(definition.ExemplarTemplateJson) &&
            JsonNode.Parse(definition.ExemplarTemplateJson) is JsonObject explicitExemplar
                ? explicitExemplar
                : TryCloneVariableTemplateExemplar(definition.VariableType);
        if (exemplar == null)
            exemplar = CreateFallbackVariablePropertyTemplateJson(definition.VariableType, definition.DefaultValueText);

        exemplar["Name"] = definition.Name;
        exemplar["Flags"] ??= "RF_Public";
        exemplar["PropertyFlags"] ??= "CPF_None";
        exemplar["RepIndex"] ??= 0;
        exemplar["RepNotifyFunc"] ??= "None";
        exemplar["BlueprintReplicationCondition"] ??= "COND_None";
        exemplar["RawValue"] ??= null;
        exemplar["MetaDataMap"] ??= null;
        exemplar["UsmapPropertyTypeOverrides"] ??= CreateDefaultUsmapOverridesNode();

        if (definition.VariableType == BlueprintVariableType.Bool)
        {
            exemplar["Value"] = string.Equals(definition.DefaultValueText, "True", StringComparison.OrdinalIgnoreCase);
        }

        return exemplar;
    }

    private JsonObject? TryCloneVariableTemplateExemplar(BlueprintVariableType type)
    {
        foreach (CanvasVariableEntry entry in variableEntriesByScope.Values)
        {
            if (entry.Definition.VariableType != type || string.IsNullOrWhiteSpace(entry.Definition.PropertyTemplateJson))
                continue;

            if (JsonNode.Parse(entry.Definition.PropertyTemplateJson) is JsonObject exemplar)
                return exemplar;
        }

        return null;
    }

    private static JsonObject CreateFallbackVariablePropertyTemplateJson(BlueprintVariableType type, string defaultValueText)
    {
        return type switch
        {
            BlueprintVariableType.Bool => new JsonObject
            {
                ["$type"] = "UAssetAPI.FieldTypes.FBoolProperty, UAssetAPI",
                ["FieldSize"] = 1,
                ["ByteOffset"] = 0,
                ["ByteMask"] = 1,
                ["FieldMask"] = 255,
                ["NativeBool"] = true,
                ["Value"] = string.Equals(defaultValueText, "True", StringComparison.OrdinalIgnoreCase),
                ["ArrayDim"] = "TArray",
                ["ElementSize"] = 1,
                ["SerializedType"] = "BoolProperty"
            },
            BlueprintVariableType.Int => CreateGenericFallbackProperty("IntProperty", 4),
            BlueprintVariableType.Float => CreateGenericFallbackProperty("FloatProperty", 4),
            BlueprintVariableType.Double => CreateGenericFallbackProperty("DoubleProperty", 8),
            BlueprintVariableType.Byte => CreateGenericFallbackProperty("ByteProperty", 1),
            BlueprintVariableType.String => CreateGenericFallbackProperty("StrProperty", 16),
            BlueprintVariableType.Name => CreateGenericFallbackProperty("NameProperty", 8),
            BlueprintVariableType.Text => CreateGenericFallbackProperty("TextProperty", 16),
            _ => CreateGenericFallbackProperty("IntProperty", 4)
        };
    }

    private static JsonObject CreateGenericFallbackProperty(string serializedType, int elementSize)
    {
        return new JsonObject
        {
            ["$type"] = "UAssetAPI.FieldTypes.FGenericProperty, UAssetAPI",
            ["ArrayDim"] = "TArray",
            ["ElementSize"] = elementSize,
            ["SerializedType"] = serializedType
        };
    }

    private static JsonObject CreateDefaultUsmapOverridesNode()
    {
        return new JsonObject
        {
            ["$type"] = "System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[UAssetAPI.Unversioned.EPropertyType, UAssetAPI]], System.Private.CoreLib",
            ["MulticastInlineDelegateProperty"] = "MulticastDelegateProperty",
            ["ClassProperty"] = "ObjectProperty",
            ["SoftClassProperty"] = "SoftObjectProperty"
        };
    }

    private void ApplyZoomToAllNodes()
    {
        foreach (var node in nodes)
        {
            node.SetZoom(1.0f);
        }
    }

    private void UpdateScrollArea()
    {
        NormalizeContentBoundsIfNeeded();
        surface.Bounds = ClientRectangle;
        minimapOverlay.BringToFront();
        InvalidateCanvas();
    }

    private void Canvas_MouseWheel(object sender, MouseEventArgs e)
    {
        CloseActiveValueEditor(commit: true);
        Point mousePos = PointToClient(((Control)sender).PointToScreen(e.Location));
        PointF anchorWorld = new(
            viewportOrigin.X + mousePos.X / zoom,
            viewportOrigin.Y + mousePos.Y / zoom);
        float oldZoom = zoom;
        if (e.Delta > 0)
            Zoom += 0.1f;
        else
            Zoom -= 0.1f;

        if (Math.Abs(oldZoom - zoom) > 0.01f)
        {
            viewportOrigin = new PointF(
                anchorWorld.X - mousePos.X / zoom,
                anchorWorld.Y - mousePos.Y / zoom);
            InvalidateCanvas();
        }
    }

    // 替换原有的 Canvas_MouseDown 方法
    private void Canvas_MouseDown(object sender, MouseEventArgs e)
    {
        if (sender is Control senderControl)
            senderControl.Focus();
        Focus();

        if (e.Button == MouseButtons.Left)
        {
            Point canvasPoint = PointToClient(((Control)sender).PointToScreen(e.Location));
            Point worldPoint = ScreenToWorld(canvasPoint);

            if (isConnecting)
            {
                if (TryHandleConnectionEnd(worldPoint))
                    return;

                CancelActiveConnection("已取消连线");
                return;
            }

            if (TryHandleNodeMouseDown(worldPoint))
                return;

            if (TryGetConnectionAtPoint(worldPoint, out Connection? hitConnection))
            {
                ClearSelection();
                selectedConnection = hitConnection;
                selectedConnections.Add(hitConnection!);
                InvalidateCanvas();
                StatusMessage?.Invoke($"选中连接: {hitConnection!.FromNode.Definition.Name}.{hitConnection.FromPin.Name} → {hitConnection.ToNode.Definition.Name}.{hitConnection.ToPin.Name}");
                return;
            }

            BeginBoxSelection(worldPoint);
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            Point canvasPoint = PointToClient(((Control)sender).PointToScreen(e.Location));
            Point worldPoint = ScreenToWorld(canvasPoint);
            if (TryGetConnectionAtPoint(worldPoint, out Connection? hitConnection) && hitConnection != null)
            {
                SelectConnection(hitConnection);
                ShowConnectionContextMenu(hitConnection, canvasPoint);
                return;
            }

            isPanningCanvas = true;
            panStartPointCanvas = canvasPoint;
            panStartViewportOrigin = viewportOrigin;
            Cursor = Cursors.SizeAll;
            StatusMessage?.Invoke("开始平移画布 (右键拖动)");
        }
    }

    // 替换原有的 Canvas_MouseMove 方法
    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (isBoxSelecting)
        {
            Point currentCanvasPoint = PointToClient(((Control)sender).PointToScreen(e.Location));
            lastPointerCanvasPoint = currentCanvasPoint;
            AutoScrollViewportAtEdge(currentCanvasPoint);
            selectionCurrentSurface = ScreenToWorld(currentCanvasPoint);
            UpdateEdgeScrollTimerActive();
            InvalidateCanvas();
            return;
        }

        if (isDraggingNodes && dragAnchorNode != null)
        {
            lastPointerCanvasPoint = PointToClient(Cursor.Position);
            UpdateNodeDrag(dragAnchorNode, Point.Empty);
            UpdateEdgeScrollTimerActive();
            return;
        }

        if (isPanningCanvas)
        {
            Point currentPoint = PointToClient(((Control)sender).PointToScreen(e.Location));
            int deltaX = currentPoint.X - panStartPointCanvas.X;
            int deltaY = currentPoint.Y - panStartPointCanvas.Y;

            if (deltaX != 0 || deltaY != 0)
            {
                viewportOrigin = new PointF(
                    panStartViewportOrigin.X - deltaX / zoom,
                    panStartViewportOrigin.Y - deltaY / zoom);
                InvalidateCanvas();
            }
            return;
        }

        if (isConnecting)
        {
            Point canvasPoint = PointToClient(((Control)sender).PointToScreen(e.Location));
            lastPointerCanvasPoint = canvasPoint;
            AutoScrollViewportAtEdge(canvasPoint);
            tempLineEnd = ScreenToWorld(canvasPoint);
            if (!connectionDragStarted &&
                Math.Abs(canvasPoint.X - connectionStartCanvasPoint.X) + Math.Abs(canvasPoint.Y - connectionStartCanvasPoint.Y) >= SelectionDragThreshold)
            {
                connectionDragStarted = true;
            }
            UpdateEdgeScrollTimerActive();
            InvalidateCanvas();
        }
    }

    // 替换原有的 Canvas_MouseUp 方法
    private void Canvas_MouseUp(object sender, MouseEventArgs e)
    {
        if (isBoxSelecting && e.Button == MouseButtons.Left)
        {
            CompleteBoxSelection();
            UpdateEdgeScrollTimerActive();
            return;
        }

        if (isPanningCanvas)
        {
            isPanningCanvas = false;
            Cursor = Cursors.Default;
            StatusMessage?.Invoke($"结束平移画布，视口: ({viewportOrigin.X:F0}, {viewportOrigin.Y:F0})");
            return;
        }

        if (isDraggingNodes && e.Button == MouseButtons.Left && dragAnchorNode != null)
        {
            EndNodeDrag(dragAnchorNode, Point.Empty);
            UpdateEdgeScrollTimerActive();
            return;
        }

        if (isConnecting && e.Button == MouseButtons.Left)
        {
            Point canvasPoint = PointToClient(((Control)sender).PointToScreen(e.Location));
            Point worldPoint = ScreenToWorld(canvasPoint);
            tempLineEnd = worldPoint;

            if (TryHandleConnectionEnd(worldPoint))
            {
                UpdateEdgeScrollTimerActive();
                return;
            }

            if (connectionDragStarted)
            {
                CancelActiveConnection("已取消连线");
                UpdateEdgeScrollTimerActive();
                return;
            }

            UpdateEdgeScrollTimerActive();
            InvalidateCanvas();
        }
    }

    private bool TryHandleConnectionEnd(Point worldPoint)
    {
        foreach (BlueprintNode node in EnumerateVisibleNodes().Reverse())
        {
            if (!node.Bounds.Contains(worldPoint))
                continue;

            Point nodePoint = new(worldPoint.X - node.Left, worldPoint.Y - node.Top);
            if (node.TryGetInputPinAt(nodePoint, out Pin? inputPin) && inputPin != null)
            {
                OnEndConnection(node, inputPin);
                return true;
            }
        }

        return false;
    }

    private bool TryHandleNodeMouseDown(Point worldPoint)
    {
        foreach (BlueprintNode node in EnumerateVisibleNodes().Reverse())
        {
            if (!node.Bounds.Contains(worldPoint))
                continue;

            Point nodePoint = new(worldPoint.X - node.Left, worldPoint.Y - node.Top);
            if (node.GetCloseButtonBounds().Contains(nodePoint))
            {
                DeleteNodeInternal(node, reportStatus: true);
                return true;
            }

            if (node.TryGetOutputPinAt(nodePoint, out Pin? outputPin) && outputPin != null)
            {
                OnStartConnection(node, outputPin);
                tempLineEnd = worldPoint;
                return true;
            }

            if (TryBeginInlineEditor(node, nodePoint))
                return true;

            if (node.GetTitleBarBounds().Contains(nodePoint) || node.Bounds.Contains(worldPoint))
            {
                BeginNodeDrag(node, nodePoint);
                return true;
            }
        }

        CloseActiveValueEditor(commit: true);
        return false;
    }

    private bool TryBeginInlineEditor(BlueprintNode node, Point nodePoint)
    {
        Rectangle editorBounds = node.GetEditorBounds();
        if (editorBounds.IsEmpty || !editorBounds.Contains(nodePoint))
            return false;

        if (!node.TryGetEditableDescriptor(out string key, out object? value, out bool isVariableSelector, out bool isBoolean))
            return false;

        CloseActiveValueEditor(commit: true);

        Rectangle screenBounds = new(
            WorldToScreen(new Point(node.Left + editorBounds.Left, node.Top + editorBounds.Top)),
            new Size(
                Math.Max(40, (int)Math.Round(editorBounds.Width * zoom)),
                Math.Max(20, (int)Math.Round(editorBounds.Height * zoom))));

        Control editor;
        if (isVariableSelector)
        {
            ComboBox combo = new()
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(65, 65, 80),
                ForeColor = Color.White,
                Text = node.FormatEditorValue(value)
            };
            foreach (string variableName in GetAvailableVariableNames(node))
                combo.Items.Add(variableName);
            combo.Items.Add(AddVariableOptionText);
            int selectedIndex = combo.FindStringExact(combo.Text);
            if (selectedIndex >= 0)
                combo.SelectedIndex = selectedIndex;
            combo.SelectionChangeCommitted += (_, _) => BeginInvoke(new Action(() => CloseActiveValueEditor(commit: true)));
            editor = combo;
        }
        else if (isBoolean)
        {
            ComboBox combo = new()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(65, 65, 80),
                ForeColor = Color.White
            };
            combo.Items.AddRange(["True", "False"]);
            bool boolValue = value is bool b ? b : value != null && bool.TryParse(Convert.ToString(value), out bool parsed) && parsed;
            combo.SelectedIndex = boolValue ? 0 : 1;
            combo.SelectionChangeCommitted += (_, _) => CloseActiveValueEditor(commit: true);
            editor = combo;
        }
        else
        {
            TextBox textBox = new()
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(65, 65, 80),
                ForeColor = Color.White,
                Text = node.FormatEditorValue(value)
            };
            editor = textBox;
        }

        editor.Font = new Font("Segoe UI", Math.Max(7f, 8.5f * zoom));
        editor.Bounds = screenBounds;
        editor.KeyDown += ActiveValueEditor_KeyDown;
        editor.LostFocus += (_, _) => CloseActiveValueEditor(commit: true);
        activeValueEditor = editor;
        activeEditorNode = node;
        activeEditorKey = key;
        Controls.Add(editor);
        editor.BringToFront();
        editor.Focus();
        if (editor is TextBox tb)
            tb.SelectAll();
        else if (editor is ComboBox cb)
            cb.DroppedDown = isVariableSelector;
        return true;
    }

    private void ActiveValueEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt && !e.Shift)
        {
            CloseActiveValueEditor(commit: true);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            CloseActiveValueEditor(commit: false);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (TryHandleEditorShortcut(e.KeyData))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private static string GetCommittedComboText(ComboBox combo)
    {
        return combo.SelectedItem?.ToString() ?? combo.Text;
    }

    private void CloseActiveValueEditor(bool commit)
    {
        if (activeValueEditor == null)
            return;

        Control editor = activeValueEditor;
        BlueprintNode? node = activeEditorNode;
        string? key = activeEditorKey;

        activeValueEditor = null;
        activeEditorNode = null;
        activeEditorKey = null;

        if (commit && node != null && !string.IsNullOrWhiteSpace(key))
        {
            object? value = editor switch
            {
                ComboBox combo when string.Equals(GetCommittedComboText(combo), AddVariableOptionText, StringComparison.Ordinal) =>
                    TryCreateVariableForNode(node, out string createdVariableName) ? createdVariableName : node.PinValues.GetValueOrDefault(key),
                ComboBox combo when key == "Value" => combo.SelectedIndex == 0,
                ComboBox combo => GetCommittedComboText(combo).Trim(),
                TextBox textBox => textBox.Text,
                _ => null
            };

            if (value != null)
            {
                Dictionary<string, object> beforeValues = node.ExportPinValues();
                UndoState undoState = hasPendingEditorUndoState && pendingEditorUndoState != null
                    ? pendingEditorUndoState
                    : CreateUndoState();
                bool previousSuppressHistory = suppressHistoryCapture;
                suppressHistoryCapture = true;
                bool editorValueChanged;
                try
                {
                    editorValueChanged = node.CommitEditorValue(key, value);
                }
                finally
                {
                    suppressHistoryCapture = previousSuppressHistory;
                }

                if (editorValueChanged)
                {
                    if (!isRestoringHistory && !suppressHistoryCapture)
                    {
                        undoStack.Push(undoState);
                        TrimUndoStack();
                        redoStack.Clear();
                    }
                    OnBlueprintEdited();
                    RefreshVariableEditors();
                }
                else if (!PinValuesEqual(beforeValues, node.ExportPinValues()))
                {
                    CaptureUndoState();
                    OnBlueprintEdited();
                    RefreshVariableEditors();
                }
            }
        }

        hasPendingEditorUndoState = false;
        pendingEditorUndoState = null;

        Controls.Remove(editor);
        editor.Dispose();
        Focus();
        InvalidateCanvas();
    }

    public bool BeginNodeDrag(BlueprintNode node, Point nodePoint)
    {
        if (!nodes.Contains(node))
            return false;

        if (!selectedNodes.Contains(node))
        {
            ClearSelection();
            SelectNode(node);
        }
        else
        {
            selectedConnections.Clear();
            selectedConnection = null;
        }

        isDraggingNodes = true;
        dragAnchorNode = node;
        lastPointerCanvasPoint = PointToClient(Cursor.Position);
        nodeDragStartCanvas = ScreenToWorld(lastPointerCanvasPoint);
        nodeDragStartLocations = selectedNodes
            .Where(nodes.Contains)
            .ToDictionary(selectedNode => selectedNode, selectedNode => selectedNode.Location);
        if (nodeDragStartLocations.Count == 0)
            nodeDragStartLocations[node] = node.Location;

        UpdateEdgeScrollTimerActive();
        return true;
    }

    public bool IsNodeDragActive(BlueprintNode node)
    {
        return isDraggingNodes && dragAnchorNode == node;
    }

    public void UpdateNodeDrag(BlueprintNode node, Point nodePoint)
    {
        if (!IsNodeDragActive(node) || nodeDragStartLocations.Count == 0)
            return;

        Point currentCanvasPoint = PointToClient(Cursor.Position);
        lastPointerCanvasPoint = currentCanvasPoint;
        AutoScrollViewportAtEdge(currentCanvasPoint);
        Point currentWorldPoint = ScreenToWorld(currentCanvasPoint);

        int deltaX = currentWorldPoint.X - nodeDragStartCanvas.X;
        int deltaY = currentWorldPoint.Y - nodeDragStartCanvas.Y;

        foreach ((BlueprintNode selectedNode, Point startLocation) in nodeDragStartLocations)
        {
            Point newLocation = new(startLocation.X + deltaX, startLocation.Y + deltaY);
            selectedNode.Location = newLocation;
        }
        MarkSpatialIndexDirty();
        InvalidateCanvas();
    }

    public void EndNodeDrag(BlueprintNode node, Point nodePoint)
    {
        if (!IsNodeDragActive(node))
            return;

        bool moved = nodeDragStartLocations.Any(kv => kv.Key.Location != kv.Value);
        UndoState? undoState = moved ? CreateUndoStateFromNodeLocations(nodeDragStartLocations) : null;
        isDraggingNodes = false;
        dragAnchorNode = null;
        nodeDragStartLocations.Clear();
        UpdateEdgeScrollTimerActive();

        if (!moved)
            return;

        if (undoState != null && !isRestoringHistory && !suppressHistoryCapture)
        {
            undoStack.Push(undoState);
            TrimUndoStack();
            redoStack.Clear();
        }
        OnBlueprintEdited();
        UpdateScrollArea();
        InvalidateCanvas();
    }

    private UndoState CreateUndoStateFromNodeLocations(IReadOnlyDictionary<BlueprintNode, Point> originalLocations)
    {
        UndoState state = CreateUndoState();
        Dictionary<Guid, Point> originalByNodeId = originalLocations.ToDictionary(kv => kv.Key.NodeId, kv => kv.Value);
        foreach (SerializableNode node in state.Data.Nodes)
        {
            if (originalByNodeId.TryGetValue(node.Id, out Point originalLocation))
                node.Location = originalLocation;
        }

        return state;
    }

    private void AutoScrollViewportAtEdge(Point currentCanvasPoint)
    {
        int moveX = 0;
        int moveY = 0;

        if (currentCanvasPoint.X < BoxSelectEdgeScrollMargin)
            moveX = BoxSelectEdgeScrollSpeed;
        else if (currentCanvasPoint.X > ClientSize.Width - BoxSelectEdgeScrollMargin)
            moveX = -BoxSelectEdgeScrollSpeed;

        if (currentCanvasPoint.Y < BoxSelectEdgeScrollMargin)
            moveY = BoxSelectEdgeScrollSpeed;
        else if (currentCanvasPoint.Y > ClientSize.Height - BoxSelectEdgeScrollMargin)
            moveY = -BoxSelectEdgeScrollSpeed;

        if (moveX == 0 && moveY == 0)
            return;

        viewportOrigin = new PointF(
            viewportOrigin.X - moveX / zoom,
            viewportOrigin.Y - moveY / zoom);
        InvalidateCanvas();
    }

    private void EdgeScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!isBoxSelecting && !isDraggingNodes && !isConnecting)
        {
            UpdateEdgeScrollTimerActive();
            return;
        }

        if (isDraggingNodes && dragAnchorNode != null)
        {
            UpdateNodeDrag(dragAnchorNode, Point.Empty);
            return;
        }

        AutoScrollViewportAtEdge(lastPointerCanvasPoint);

        if (isConnecting)
        {
            tempLineEnd = ScreenToWorld(lastPointerCanvasPoint);
            InvalidateCanvas();
            return;
        }

        if (isBoxSelecting)
        {
            selectionCurrentSurface = ScreenToWorld(lastPointerCanvasPoint);
            InvalidateCanvas();
        }
    }

    private void UpdateEdgeScrollTimerActive()
    {
        bool shouldRun = (isBoxSelecting || isDraggingNodes || (isConnecting && connectionDragStarted)) &&
            IsPointInEdgeScrollZone(lastPointerCanvasPoint);

        if (shouldRun)
        {
            if (!edgeScrollTimer.Enabled)
                edgeScrollTimer.Start();
        }
        else if (edgeScrollTimer.Enabled)
        {
            edgeScrollTimer.Stop();
        }
    }

    private bool IsPointInEdgeScrollZone(Point canvasPoint)
    {
        return canvasPoint.X < BoxSelectEdgeScrollMargin ||
            canvasPoint.X > ClientSize.Width - BoxSelectEdgeScrollMargin ||
            canvasPoint.Y < BoxSelectEdgeScrollMargin ||
            canvasPoint.Y > ClientSize.Height - BoxSelectEdgeScrollMargin;
    }

    public void CreateNode(NodeDefinition def, Point location)
    {
        CaptureUndoState();
        var node = new BlueprintNode(def);
        string? callFunctionName = TryExtractSpecificCallFunctionName(def.Name);
        if (!string.IsNullOrWhiteSpace(callFunctionName))
        {
            node.MetaData["CallFunctionName"] = callFunctionName;
            RemoveSpecificCallFunctionOverrides(node.PinValues);
        }

        bool isSpecificCallDefinition = !string.IsNullOrWhiteSpace(callFunctionName);
        if (!string.IsNullOrWhiteSpace(def.SourceExpressionTemplateJson) && !isSpecificCallDefinition)
        {
            node.MetaData["NodeKind"] = "Expression";
            node.MetaData["SourceExpressionJson"] = def.SourceExpressionTemplateJson;
            if (!string.IsNullOrWhiteSpace(def.SourceExpressionType))
                node.MetaData["SourceExpressionType"] = def.SourceExpressionType!;
            node.MetaData["TemplateDerived"] = bool.TrueString;
        }
        node.OwnerCanvas = this;
        node.SetZoom(1.0f);
        node.NodeDeleted += OnNodeDeleted;
        node.NodeDataChanged += OnNodeDataChanged;
        node.StartConnection += OnStartConnection;
        node.EndConnection += OnEndConnection;
        nodes.Add(node);
        node.RefreshLayoutMetrics();
        Point worldLocation = ScreenToWorld(location);
        node.Location = new Point(worldLocation.X - node.Width / 2, worldLocation.Y - node.Height / 2);
        MarkSpatialIndexDirty();
        OnBlueprintEdited();
        UpdateScrollArea();
        InvalidateCanvas();
        StatusMessage?.Invoke($"创建节点: {def.Name} 位于 ({node.Location.X}, {node.Location.Y})");
    }

    private static string? TryExtractSpecificCallFunctionName(string definitionName)
    {
        definitionName = NormalizeLegacyDefinitionNameForCanvas(definitionName);
        string baseDefinitionName = GetBaseDefinitionName(definitionName);
        if (!baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
            return null;

        string functionName = baseDefinitionName[5..].Trim();
        return string.IsNullOrWhiteSpace(functionName) ? null : functionName;
    }

    private static string GetBaseDefinitionName(string definitionName)
    {
        definitionName = NormalizeLegacyDefinitionNameForCanvas(definitionName);
        int hashSuffix = definitionName.LastIndexOf(" #", StringComparison.Ordinal);
        if (hashSuffix > 0 &&
            int.TryParse(definitionName[(hashSuffix + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return definitionName[..hashSuffix].TrimEnd();
        }

        return definitionName;
    }

    private static string NormalizeLegacyDefinitionNameForCanvas(string definitionName)
        => BlueprintNode.NormalizeLegacyDefinitionName(definitionName);

    private static void RemoveSpecificCallFunctionOverrides(Dictionary<string, object> pinValues)
    {
        pinValues.Remove("CallFunctionName");
        pinValues.Remove("FunctionName");
        pinValues.Remove("TargetFunctionName");
        pinValues.Remove("EditorText");
    }

    private static void NormalizeSpecificCallNodeState(SerializableNode node)
    {
        string? callFunctionName = TryExtractSpecificCallFunctionName(node.DefinitionName);
        if (string.IsNullOrWhiteSpace(callFunctionName))
            return;

        RemoveSpecificCallFunctionOverrides(node.PinValues);
        node.MetaData["CallFunctionName"] = callFunctionName;
    }

    private static void NormalizeSpecificCallNodeState(BlueprintNode node)
    {
        string? callFunctionName = TryExtractSpecificCallFunctionName(node.Definition.Name);
        if (string.IsNullOrWhiteSpace(callFunctionName))
            return;

        RemoveSpecificCallFunctionOverrides(node.PinValues);
        node.MetaData["CallFunctionName"] = callFunctionName;
    }

    private void OnNodeDeleted(object sender, EventArgs e)
    {
        if (sender is BlueprintNode node)
            DeleteNodeInternal(node, reportStatus: true);
    }

    private void OnStartConnection(BlueprintNode node, Pin pin)
    {
        isConnecting = true;
        startNode = node;
        startPin = pin;
        lastPointerCanvasPoint = PointToClient(Cursor.Position);
        connectionStartCanvasPoint = lastPointerCanvasPoint;
        connectionDragStarted = false;
        tempLineEnd = node.GetPinWorldPosition(pin);
        UpdateEdgeScrollTimerActive();
        Cursor = Cursors.Cross;
        StatusMessage?.Invoke($"开始连线: 从 {node.Definition.Name}.{pin.Name} ({pin.Type})");
    }

    private void CancelActiveConnection(string? message = null)
    {
        isConnecting = false;
        startNode = null;
        startPin = null;
        connectionDragStarted = false;
        connectionStartCanvasPoint = Point.Empty;
        UpdateEdgeScrollTimerActive();
        Cursor = Cursors.Default;
        if (!string.IsNullOrWhiteSpace(message))
            StatusMessage?.Invoke(message);
        InvalidateCanvas();
    }

    private void OnEndConnection(BlueprintNode targetNode, Pin targetPin)
    {
        if (!isConnecting) return;

        // 检查连接有效性
        if (targetNode == null || targetPin == null)
        {
            CancelActiveConnection("未命中有效的输入引脚，连线取消");
            return;
        }

        if (startNode == targetNode)
        {
            CancelActiveConnection("连接失败: 不能连接同一节点上的引脚");
            return;
        }

        if (targetPin.Type != startPin.Type)
        {
            CancelActiveConnection($"连接失败: 引脚类型不匹配 (源: {startPin.Type}, 目标: {targetPin.Type})");
            return;
        }

        Connection? existingConnection = connections.LastOrDefault(c =>
            c.FromNode == startNode &&
            c.FromPin == startPin &&
            c.ToNode == targetNode &&
            c.ToPin == targetPin);

        bool willReplaceConnections = !AllowsMultipleIncomingConnections(targetPin) &&
            connections.Any(c =>
                c != existingConnection &&
                c.ToNode == targetNode &&
                string.Equals(c.ToPin.Name, targetPin.Name, StringComparison.Ordinal) &&
                c.ToPin.Type == targetPin.Type);
        if (existingConnection == null || willReplaceConnections)
            CaptureUndoState();

        int replacedConnections = RemoveIncomingConnections(targetNode, targetPin, existingConnection);
        if (existingConnection != null)
        {
            if (replacedConnections > 0)
            {
                OnBlueprintEdited();
                StatusMessage?.Invoke($"连接已存在，已清理 {replacedConnections} 条旧输入连接: {startNode.Definition.Name}.{startPin.Name} → {targetNode.Definition.Name}.{targetPin.Name}");
            }
            else
            {
                StatusMessage?.Invoke($"连接已存在: {startNode.Definition.Name}.{startPin.Name} → {targetNode.Definition.Name}.{targetPin.Name}，未重复添加");
            }
        }
        else
        {
            connections.Add(new Connection(startNode, startPin, targetNode, targetPin, AllocateConnectionSequence()));
            OnBlueprintEdited();
            string replacementSuffix = replacedConnections > 0 ? $"，替换了 {replacedConnections} 条旧输入连接" : string.Empty;
            StatusMessage?.Invoke($"已连接 {startNode.Definition.Name}.{startPin.Name} → {targetNode.Definition.Name}.{targetPin.Name}{replacementSuffix}");
        }

        isConnecting = false;
        startNode = null;
        startPin = null;
        connectionDragStarted = false;
        connectionStartCanvasPoint = Point.Empty;
        UpdateEdgeScrollTimerActive();
        Cursor = Cursors.Default;
        InvalidateCanvas();
    }

    private long AllocateConnectionSequence()
    {
        return nextConnectionSequence++;
    }

    private long NormalizeImportedConnectionSequence(long sequence)
    {
        if (sequence <= 0)
            sequence = nextConnectionSequence;

        nextConnectionSequence = Math.Max(nextConnectionSequence, sequence + 1);
        return sequence;
    }

    private int RemoveIncomingConnections(BlueprintNode targetNode, Pin targetPin, Connection? keepConnection = null)
    {
        if (AllowsMultipleIncomingConnections(targetPin))
            return 0;

        List<Connection> conflicts = connections
            .Where(c =>
                c != keepConnection &&
                c.ToNode == targetNode &&
                string.Equals(c.ToPin.Name, targetPin.Name, StringComparison.Ordinal) &&
                c.ToPin.Type == targetPin.Type)
            .ToList();

        if (conflicts.Count == 0)
            return 0;

        foreach (Connection conn in conflicts)
        {
            connections.Remove(conn);
            selectedConnections.Remove(conn);
            if (selectedConnection == conn)
                selectedConnection = null;
        }

        return conflicts.Count;
    }

    private static bool AllowsMultipleIncomingConnections(Pin targetPin)
    {
        return targetPin.Type == PinType.Exec &&
            string.Equals(targetPin.Name, "In", StringComparison.Ordinal);
    }

    private void DrawGrid(Graphics g)
    {
        int gridSize = Math.Max(8, (int)(20 * zoom));
        using (var pen = new Pen(Color.FromArgb(80, 80, 100)))
        {
            int startX = (int)Math.Round(-viewportOrigin.X * zoom) % gridSize;
            int startY = (int)Math.Round(-viewportOrigin.Y * zoom) % gridSize;
            if (startX > 0) startX -= gridSize;
            if (startY > 0) startY -= gridSize;
            int endX = surface.Width;
            int endY = surface.Height;

            for (int x = startX; x < endX; x += gridSize)
                g.DrawLine(pen, x, startY, x, endY);
            for (int y = startY; y < endY; y += gridSize)
                g.DrawLine(pen, startX, y, endX, y);
        }
    }

    private void DrawConnections(Graphics g)
    {
        Rectangle visibleWorld = GetVisibleWorldRectangle();
        float penWidth = Math.Max(1, 2);
        using (var execPen = new Pen(ExecPen.Color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.ArrowAnchor })
        using (var dataPen = new Pen(DataPen.Color, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.ArrowAnchor })
        using (var selectedExecPen = new Pen(Color.FromArgb(255, 245, 120), penWidth + 2f) { StartCap = LineCap.Round, EndCap = LineCap.ArrowAnchor })
        using (var selectedDataPen = new Pen(Color.FromArgb(120, 235, 255), penWidth + 2f) { StartCap = LineCap.Round, EndCap = LineCap.ArrowAnchor })
        {
            foreach (var conn in connections)
            {
                if (!IsConnectionVisible(conn, visibleWorld)) continue;
                if (selectedConnections.Contains(conn) || conn == selectedConnection) continue;
                if (conn.FromPin.Type == PinType.Exec)
                    DrawConnectionBezier(g, conn, execPen);
                else
                    DrawConnectionBezier(g, conn, dataPen);
            }

            foreach (Connection selectedConn in selectedConnections.Where(connections.Contains))
            {
                if (!IsConnectionVisible(selectedConn, visibleWorld)) continue;
                if (selectedConn.FromPin.Type == PinType.Exec)
                    DrawConnectionBezier(g, selectedConn, selectedExecPen);
                else
                    DrawConnectionBezier(g, selectedConn, selectedDataPen);
            }

            if (selectedConnection != null && connections.Contains(selectedConnection) && !selectedConnections.Contains(selectedConnection))
            {
                if (!IsConnectionVisible(selectedConnection, visibleWorld)) return;
                if (selectedConnection.FromPin.Type == PinType.Exec)
                    DrawConnectionBezier(g, selectedConnection, selectedExecPen);
                else
                    DrawConnectionBezier(g, selectedConnection, selectedDataPen);
            }
        }
    }

    private void DrawConnectionBezier(Graphics g, Connection conn, Pen pen)
    {
        Point start = conn.FromNode.GetPinWorldPosition(conn.FromPin);
        Point end = conn.ToNode.GetPinWorldPosition(conn.ToPin);
        int offset = Math.Abs(end.X - start.X) / 2;
        Point ctrl1 = new Point(start.X + offset, start.Y);
        Point ctrl2 = new Point(end.X - offset, end.Y);
        g.DrawBezier(pen, start, ctrl1, ctrl2, end);
    }

    private bool TryGetConnectionAtPoint(Point surfacePoint, out Connection? hitConnection)
    {
        for (int i = connections.Count - 1; i >= 0; i--)
        {
            Connection conn = connections[i];
            if (IsPointNearConnection(conn, surfacePoint))
            {
                hitConnection = conn;
                return true;
            }
        }

        hitConnection = null;
        return false;
    }

    private void SelectConnection(Connection connection)
    {
        ClearSelection();
        selectedConnection = connection;
        selectedConnections.Add(connection);
        InvalidateCanvas();
        StatusMessage?.Invoke($"选中连接: {connection.FromNode.Definition.Name}.{connection.FromPin.Name} → {connection.ToNode.Definition.Name}.{connection.ToPin.Name}");
    }

    private void ShowConnectionContextMenu(Connection connection, Point canvasPoint)
    {
        ContextMenuStrip menu = new();
        ToolStripMenuItem jumpToStart = new("跳转到起点");
        ToolStripMenuItem jumpToEnd = new("跳转到终点");

        jumpToStart.Click += (_, _) => JumpToConnectionEndpoint(connection, jumpToStart: true);
        jumpToEnd.Click += (_, _) => JumpToConnectionEndpoint(connection, jumpToStart: false);
        menu.Items.Add(jumpToStart);
        menu.Items.Add(jumpToEnd);
        menu.Show(this, canvasPoint);
    }

    private void JumpToConnectionEndpoint(Connection connection, bool jumpToStart)
    {
        if (!connections.Contains(connection))
        {
            StatusMessage?.Invoke("跳转失败: 连接已不存在");
            return;
        }

        BlueprintNode targetNode = jumpToStart ? connection.FromNode : connection.ToNode;
        if (!nodes.Contains(targetNode))
        {
            StatusMessage?.Invoke("跳转失败: 节点已不存在");
            return;
        }

        CenterViewportOnNode(targetNode);
        selectedConnection = connection;
        selectedConnections.Clear();
        selectedConnections.Add(connection);
        InvalidateCanvas();
        StatusMessage?.Invoke(jumpToStart
            ? $"已跳转到连接起点: {targetNode.Definition.Name}"
            : $"已跳转到连接终点: {targetNode.Definition.Name}");
    }

    private void CenterViewportOnNode(BlueprintNode node)
    {
        float visibleWidth = ClientSize.Width / zoom;
        float visibleHeight = ClientSize.Height / zoom;
        viewportOrigin = new PointF(
            node.Left + node.Width / 2f - visibleWidth / 2f,
            node.Top + node.Height / 2f - visibleHeight / 2f);
        UpdateScrollArea();
    }

    private bool IsPointNearConnection(Connection conn, Point testPoint)
    {
        Point start = conn.FromNode.GetPinWorldPosition(conn.FromPin);
        Point end = conn.ToNode.GetPinWorldPosition(conn.ToPin);
        int offset = Math.Abs(end.X - start.X) / 2;
        Point ctrl1 = new Point(start.X + offset, start.Y);
        Point ctrl2 = new Point(end.X - offset, end.Y);

        using GraphicsPath path = new();
        path.AddBezier(start, ctrl1, ctrl2, end);
        using Pen hitPen = new(Color.White, Math.Max(8f, 8f / zoom)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        return path.IsOutlineVisible(testPoint, hitPen);
    }

    private bool IsConnectionVisible(Connection conn, Rectangle visibleWorld)
    {
        Point start = conn.FromNode.GetPinWorldPosition(conn.FromPin);
        Point end = conn.ToNode.GetPinWorldPosition(conn.ToPin);
        Rectangle bounds = Rectangle.FromLTRB(
            Math.Min(start.X, end.X) - 80,
            Math.Min(start.Y, end.Y) - 80,
            Math.Max(start.X, end.X) + 80,
            Math.Max(start.Y, end.Y) + 80);
        return bounds.IntersectsWith(visibleWorld);
    }

    private void Canvas_KeyDown(object? sender, KeyEventArgs e)
    {
        if (TryHandleEditorShortcut(e.KeyData))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode != Keys.Delete && e.KeyCode != Keys.Back)
            return;

        bool hasDeletionTargets = selectedConnections.Count > 0 ||
            selectedConnection != null ||
            selectedNodes.Any(nodes.Contains);
        if (hasDeletionTargets)
            CaptureUndoState();

        int removedNodeCount = 0;
        int removedConnectionCount = 0;

        HashSet<Connection> removalTargets = new(selectedConnections);
        if (selectedConnection != null)
            removalTargets.Add(selectedConnection);

        foreach (Connection conn in removalTargets)
        {
            if (connections.Remove(conn))
                removedConnectionCount++;
        }

        selectedConnections.Clear();
        selectedConnection = null;

        List<BlueprintNode> nodeTargets = selectedNodes.Where(nodes.Contains).ToList();
        foreach (BlueprintNode node in nodeTargets)
        {
            if (DeleteNodeInternal(node, reportStatus: false))
                removedNodeCount++;
        }
        selectedNodes.Clear();

        if (removedNodeCount > 0 || removedConnectionCount > 0)
        {
            if (removedNodeCount == 0)
                OnBlueprintEdited();
            InvalidateCanvas();
            StatusMessage?.Invoke($"删除选择项: {removedNodeCount} 个节点, {removedConnectionCount} 条连接");
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    internal bool TryHandleEditorShortcut(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            Undo();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Y))
        {
            Redo();
            return true;
        }

        if (keyData == (Keys.Control | Keys.C))
        {
            CopySelectionToClipboard();
            return true;
        }

        if (keyData == (Keys.Control | Keys.V))
        {
            PasteFromClipboard();
            return true;
        }

        return false;
    }

    private void CopySelectionToClipboard()
    {
        CommitPendingEditorValues();
        PruneInvalidConnections();

        HashSet<BlueprintNode> nodeSelection = new(selectedNodes);
        if (nodeSelection.Count == 0)
        {
            StatusMessage?.Invoke("复制失败: 未选中任何节点");
            return;
        }

        Dictionary<Guid, BlueprintNode> selectedById = nodeSelection.ToDictionary(node => node.NodeId);
        HashSet<Connection> selectedConnSnapshot = new(selectedConnections);
        if (selectedConnection != null)
            selectedConnSnapshot.Add(selectedConnection);

        List<ConnectionData> copiedConnections = [];
        foreach (Connection conn in connections)
        {
            bool endpointSelected = selectedById.ContainsKey(conn.FromNode.NodeId) && selectedById.ContainsKey(conn.ToNode.NodeId);
            bool explicitSelected = selectedConnSnapshot.Contains(conn);
            if (!endpointSelected && !explicitSelected)
                continue;

            copiedConnections.Add(new ConnectionData
            {
                FromNodeId = conn.FromNode.NodeId,
                FromPinName = conn.FromPin.Name,
                ToNodeId = conn.ToNode.NodeId,
                ToPinName = conn.ToPin.Name,
                Sequence = conn.Sequence
            });
        }

        int minX = nodeSelection.Min(n => n.Location.X);
        int minY = nodeSelection.Min(n => n.Location.Y);
        Point origin = new(minX, minY);

        ClipboardPayload payload = new()
        {
            Origin = origin
        };

        foreach (BlueprintNode node in nodeSelection.OrderBy(n => n.Location.Y).ThenBy(n => n.Location.X))
        {
            payload.Nodes.Add(new SerializableNode
            {
                Id = node.NodeId,
                DefinitionName = node.Definition.Name,
                Location = new Point(node.Location.X - origin.X, node.Location.Y - origin.Y),
                PinValues = node.ExportPinValues(),
                MetaData = new Dictionary<string, string>(node.MetaData),
                ReferenceDescriptors = node.Definition.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList(),
                PinSchemas = node.Definition.PinSchemas.Select(BlueprintModelCloner.Clone).ToList(),
                PropertyTemplateJsonByName = node.Definition.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            });

            if (!payload.Definitions.ContainsKey(node.Definition.Name))
            {
                payload.Definitions[node.Definition.Name] = new NodeDefinition
                {
                    Name = node.Definition.Name,
                    InputPins = node.Definition.InputPins.Select(pin => new Pin(pin.Name, pin.Direction, pin.Type)).ToList(),
                    OutputPins = node.Definition.OutputPins.Select(pin => new Pin(pin.Name, pin.Direction, pin.Type)).ToList(),
                    SourceExpressionTemplateJson = node.Definition.SourceExpressionTemplateJson,
                    SourceExpressionType = node.Definition.SourceExpressionType,
                    ReferenceDescriptors = node.Definition.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList(),
                    PinSchemas = node.Definition.PinSchemas.Select(BlueprintModelCloner.Clone).ToList(),
                    PropertyTemplateJsonByName = node.Definition.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                    RepresentativeCallTemplateJson = node.Definition.RepresentativeCallTemplateJson,
                    RepresentativeCallExpressionType = node.Definition.RepresentativeCallExpressionType,
                    RepresentativeCallReferenceDescriptors = node.Definition.RepresentativeCallReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList()
                };
            }

            NormalizeSpecificCallNodeState(payload.Nodes[^1]);
        }

        payload.Connections = copiedConnections;
        payload.FunctionTemplates = AssetContext.FunctionTemplates.ToDictionary(
            kv => kv.Key,
            kv => BlueprintModelCloner.Clone(kv.Value),
            StringComparer.Ordinal);
        payload.LoadedPropertyTemplates = AssetContext.ClassLoadedPropertyTemplatesByName.ToDictionary(
            kv => kv.Key,
            kv => BlueprintModelCloner.Clone(kv.Value),
            StringComparer.Ordinal);
        string text = BlueprintClipboardPrefix + JsonSerializer.Serialize(payload);
        inMemoryClipboardPayload = text;
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 部分环境下系统剪贴板不可用，保留内存剪贴板作为后备。
        }

        StatusMessage?.Invoke($"已复制: {payload.Nodes.Count} 个节点, {payload.Connections.Count} 条连接");
    }

    private void PasteFromClipboard()
    {
        UndoState? undoState = null;
        string? raw = null;
        try
        {
            if (Clipboard.ContainsText())
                raw = Clipboard.GetText();
        }
        catch
        {
            // Ignore and fallback to in-memory payload.
        }

        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith(BlueprintClipboardPrefix, StringComparison.Ordinal))
            raw = inMemoryClipboardPayload;

        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith(BlueprintClipboardPrefix, StringComparison.Ordinal))
        {
            StatusMessage?.Invoke("粘贴失败: 剪贴板中没有可用的蓝图数据");
            return;
        }

        ClipboardPayload? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<ClipboardPayload>(raw[BlueprintClipboardPrefix.Length..]);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"粘贴失败: 剪贴板数据损坏 ({ex.Message})");
            return;
        }

        if (payload == null || payload.Nodes.Count == 0)
        {
            StatusMessage?.Invoke("粘贴失败: 剪贴板数据为空");
            return;
        }

        BlueprintAssetContext payloadContext = new BlueprintAssetContext
        {
            FunctionTemplates = payload.FunctionTemplates.ToDictionary(
                kv => kv.Key,
                kv => BlueprintModelCloner.Clone(kv.Value),
                StringComparer.Ordinal),
            ClassLoadedPropertyTemplatesByName = payload.LoadedPropertyTemplates.ToDictionary(
                kv => kv.Key,
                kv => BlueprintModelCloner.Clone(kv.Value),
                StringComparer.Ordinal)
        };
        AssetContext = BlueprintAssetContext.Merge(AssetContext, payloadContext);

        suppressOffsetRefreshDepth++;
        try
        {
            ClearSelection();
            undoState = CreateUndoState();

            int offsetStep = 32;
            int offset = offsetStep * Math.Max(0, pasteSequence++);
            Rectangle visibleSurfaceRect = GetVisibleSurfaceRectangle();
            Point pasteOrigin = CalculatePasteOriginForVisibleTop(payload.Nodes, visibleSurfaceRect, offset);

            Dictionary<Guid, BlueprintNode> oldToNewNodeMap = [];

            foreach (SerializableNode copiedNode in payload.Nodes.OrderBy(n => n.Location.Y).ThenBy(n => n.Location.X))
            {
                string normalizedDefinitionName = NormalizeLegacyDefinitionNameForCanvas(copiedNode.DefinitionName);
                if (!payload.Definitions.TryGetValue(copiedNode.DefinitionName, out NodeDefinition? definition) &&
                    !payload.Definitions.TryGetValue(normalizedDefinitionName, out definition))
                    continue;

                BlueprintNode newNode = new(definition);
                newNode.Definition.ReferenceDescriptors = copiedNode.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList();
                newNode.Definition.PinSchemas = copiedNode.PinSchemas.Select(BlueprintModelCloner.Clone).ToList();
                newNode.Definition.PropertyTemplateJsonByName = copiedNode.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                newNode.ApplyPinValues(copiedNode.PinValues);
                newNode.ApplyMetaData(copiedNode.MetaData);
                NormalizeSpecificCallNodeState(newNode);
                // Pasted nodes should be re-bound to current graph topology instead of carrying
                // stale function/top-level/offset metadata from source canvas.
                newNode.MetaData.Remove("FunctionName");
                newNode.MetaData.Remove("TopLevelOrder");
                newNode.MetaData.Remove("StatementIndex");
                newNode.MetaData.Remove("OriginalStatementIndex");
                newNode.OwnerCanvas = this;
                newNode.SetZoom(1.0f);

                newNode.NodeDeleted += OnNodeDeleted;
                newNode.NodeDataChanged += OnNodeDataChanged;
                newNode.StartConnection += OnStartConnection;
                newNode.EndConnection += OnEndConnection;

                nodes.Add(newNode);
                newNode.RefreshLayoutMetrics();
                Point targetLocation = new(
                    pasteOrigin.X + copiedNode.Location.X,
                    pasteOrigin.Y + copiedNode.Location.Y);
                newNode.Location = targetLocation;
                MarkSpatialIndexDirty();
                oldToNewNodeMap[copiedNode.Id] = newNode;
                SelectNode(newNode);
            }

            int pastedConnectionCount = 0;
            foreach (ConnectionData copiedConn in payload.Connections)
            {
                if (!oldToNewNodeMap.TryGetValue(copiedConn.FromNodeId, out BlueprintNode? fromNode))
                    continue;
                if (!oldToNewNodeMap.TryGetValue(copiedConn.ToNodeId, out BlueprintNode? toNode))
                    continue;

                Pin? fromPin = fromNode.OutputPins.FirstOrDefault(pin => pin.Name == copiedConn.FromPinName);
                Pin? toPin = toNode.InputPins.FirstOrDefault(pin => pin.Name == copiedConn.ToPinName);
                if (fromPin == null || toPin == null || fromPin.Type != toPin.Type)
                    continue;

                bool exists = connections.Any(c =>
                    c.FromNode == fromNode &&
                    c.FromPin.Name == fromPin.Name &&
                    c.ToNode == toNode &&
                    c.ToPin.Name == toPin.Name);
                if (exists)
                    continue;

                RemoveIncomingConnections(toNode, toPin);
                Connection conn = new(fromNode, fromPin, toNode, toPin, NormalizeImportedConnectionSequence(copiedConn.Sequence));
                connections.Add(conn);
                pastedConnectionCount++;
            }

            selectedConnection = null;
            selectedConnections.Clear();
            PruneInvalidConnections();
            UpdateScrollArea();
            InvalidateCanvas();
            if (oldToNewNodeMap.Count > 0 && !isRestoringHistory && !suppressHistoryCapture && undoState != null)
            {
                undoStack.Push(undoState);
                TrimUndoStack();
                redoStack.Clear();
            }
            OnBlueprintEdited();
            StatusMessage?.Invoke($"已粘贴: {oldToNewNodeMap.Count} 个节点, {pastedConnectionCount} 条连接");
        }
        finally
        {
            suppressOffsetRefreshDepth = Math.Max(0, suppressOffsetRefreshDepth - 1);
            RefreshStatementOffsets();
        }
    }

    private static Point CalculatePasteOriginForVisibleTop(IReadOnlyList<SerializableNode> pastedNodes, Rectangle visibleSurfaceRect, int offset)
    {
        if (pastedNodes.Count == 0)
            return new Point(visibleSurfaceRect.Left + offset, visibleSurfaceRect.Top + offset);

        int minX = pastedNodes.Min(node => node.Location.X);
        int minY = pastedNodes.Min(node => node.Location.Y);
        int maxX = pastedNodes.Max(node => node.Location.X);
        int payloadWidth = Math.Max(0, maxX - minX);

        int targetLeft = visibleSurfaceRect.Left + Math.Max(24, (visibleSurfaceRect.Width - payloadWidth) / 2) + offset;
        int targetTop = visibleSurfaceRect.Top + 24 + offset;

        return new Point(
            targetLeft - minX,
            targetTop - minY);
    }

    private Rectangle GetVisibleSurfaceRectangle()
    {
        return GetVisibleWorldRectangle(0);
    }

    private void DrawTempLine(Graphics g)
    {
        if (startNode != null && startPin != null)
        {
            Point start = startNode.GetPinWorldPosition(startPin);
            Point end = tempLineEnd;
            using (var pen = new Pen(Color.White, 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, start, end);
        }
    }

    private void Surface_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        DrawGrid(e.Graphics);
        GraphicsState state = e.Graphics.Save();
        e.Graphics.TranslateTransform(-viewportOrigin.X * zoom, -viewportOrigin.Y * zoom);
        e.Graphics.ScaleTransform(zoom, zoom);
        DrawConnections(e.Graphics);
        DrawVisibleNodes(e.Graphics);
        DrawSelectionBox(e.Graphics);
        if (isConnecting) DrawTempLine(e.Graphics);
        e.Graphics.Restore(state);
    }

    private void DrawVisibleNodes(Graphics g)
    {
        foreach (BlueprintNode node in EnumerateVisibleNodes())
        {
            node.Render(g);
        }
    }

    private void BlueprintCanvas_Resize(object? sender, EventArgs e)
    {
        UpdateScrollArea();
        UpdateMinimapLayout();
        minimapOverlay.Invalidate();
    }

    private void ClampSurfaceLocation()
    {
        // 无边界画布：视口位置不再被限制。
    }

    private void DrawMinimap(Graphics g)
    {
        Rectangle renderBounds = minimapOverlay.ClientRectangle;
        if (renderBounds.Width <= 0 || renderBounds.Height <= 0) return;

        if (isMinimapCollapsed)
        {
            using SolidBrush collapsedBrush = new(Color.FromArgb(215, 20, 24, 32));
            using Pen collapsedBorder = new(Color.FromArgb(120, 150, 160, 175));
            g.FillRectangle(collapsedBrush, renderBounds);
            g.DrawRectangle(collapsedBorder, 0, 0, renderBounds.Width - 1, renderBounds.Height - 1);
            DrawMinimapToggle(g, renderBounds, false);
            minimapViewportRect = Rectangle.Empty;
            return;
        }

        using (SolidBrush backBrush = new(Color.FromArgb(215, 20, 24, 32)))
        using (Pen borderPen = new(Color.FromArgb(120, 150, 160, 175)))
        {
            g.FillRectangle(backBrush, renderBounds);
            g.DrawRectangle(borderPen, renderBounds);
        }

        RectangleF worldBounds = GetWorldBounds();
        if (worldBounds.Width <= 0 || worldBounds.Height <= 0)
            return;

        float scale = Math.Min(
            Math.Max(1f, renderBounds.Width - 10) / worldBounds.Width,
            Math.Max(1f, renderBounds.Height - 10) / worldBounds.Height);
        float originX = renderBounds.X + (renderBounds.Width - worldBounds.Width * scale) / 2f;
        float originY = renderBounds.Y + (renderBounds.Height - worldBounds.Height * scale) / 2f;

        using (SolidBrush nodeBrush = new(Color.FromArgb(170, 110, 150, 210)))
        {
            foreach (BlueprintNode node in nodes)
            {
                RectangleF nodeRect = new(
                    originX + (node.Left - worldBounds.Left) * scale,
                    originY + (node.Top - worldBounds.Top) * scale,
                    Math.Max(3f, node.Width * scale),
                    Math.Max(3f, node.Height * scale));
                g.FillRectangle(nodeBrush, nodeRect);
            }
        }

        RectangleF viewportWorld = new(
            viewportOrigin.X,
            viewportOrigin.Y,
            ClientSize.Width / zoom,
            ClientSize.Height / zoom);
        minimapViewportRect = Rectangle.Round(new RectangleF(
            originX + (viewportWorld.Left - worldBounds.Left) * scale,
            originY + (viewportWorld.Top - worldBounds.Top) * scale,
            Math.Min(renderBounds.Width, Math.Max(12f, viewportWorld.Width * scale)),
            Math.Min(renderBounds.Height, Math.Max(12f, viewportWorld.Height * scale))));

        using SolidBrush viewportBrush = new(Color.FromArgb(70, 255, 255, 255));
        using Pen viewportPen = new(Color.FromArgb(220, 255, 255, 255), 2f);
        g.FillRectangle(viewportBrush, minimapViewportRect);
        g.DrawRectangle(viewportPen, minimapViewportRect);
        DrawMinimapToggle(g, renderBounds, true);
    }

    private void UpdateMinimapLayout()
    {
        int width = isMinimapCollapsed
            ? MinimapCollapsedWidth
            : Math.Min(MinimapWidth, Math.Max(120, ClientSize.Width / 4));
        int height = isMinimapCollapsed
            ? MinimapCollapsedHeight
            : Math.Min(MinimapHeight, Math.Max(90, ClientSize.Height / 4));
        minimapOverlay.Bounds = new Rectangle(
            Math.Max(MinimapMargin, ClientSize.Width - width - MinimapMargin),
            MinimapMargin,
            width,
            height);
        minimapOverlay.BringToFront();
    }

    private RectangleF GetWorldBounds()
    {
        float minX = viewportOrigin.X;
        float minY = viewportOrigin.Y;
        float maxX = minX + ClientSize.Width / zoom;
        float maxY = minY + ClientSize.Height / zoom;

        foreach (BlueprintNode node in nodes)
        {
            minX = Math.Min(minX, node.Left);
            minY = Math.Min(minY, node.Top);
            maxX = Math.Max(maxX, node.Right);
            maxY = Math.Max(maxY, node.Bottom);
        }

        return new RectangleF(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private void MoveViewportToMinimapPoint(Point minimapPoint, bool centerOnPoint)
    {
        RectangleF worldBounds = GetWorldBounds();
        if (worldBounds.Width <= 0 || worldBounds.Height <= 0) return;

        float scale = Math.Min(
            Math.Max(1f, minimapOverlay.ClientSize.Width - 10) / worldBounds.Width,
            Math.Max(1f, minimapOverlay.ClientSize.Height - 10) / worldBounds.Height);
        float originX = (minimapOverlay.ClientSize.Width - worldBounds.Width * scale) / 2f;
        float originY = (minimapOverlay.ClientSize.Height - worldBounds.Height * scale) / 2f;

        float localX = minimapPoint.X - originX;
        float localY = minimapPoint.Y - originY;
        if (centerOnPoint)
        {
            localX -= minimapViewportRect.Width / 2f;
            localY -= minimapViewportRect.Height / 2f;
        }

        float targetWorldX = worldBounds.Left + localX / scale;
        float targetWorldY = worldBounds.Top + localY / scale;
        viewportOrigin = new PointF(targetWorldX, targetWorldY);
        InvalidateCanvas();
    }

    private void MinimapOverlay_Paint(object? sender, PaintEventArgs e)
    {
        DrawMinimap(e.Graphics);
    }

    private void MinimapOverlay_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        Rectangle toggleRect = GetMinimapToggleRect(minimapOverlay.ClientRectangle, isMinimapCollapsed);
        if (toggleRect.Contains(e.Location))
        {
            isMinimapCollapsed = !isMinimapCollapsed;
            UpdateMinimapLayout();
            minimapOverlay.Invalidate();
            return;
        }

        if (isMinimapCollapsed) return;

        if (minimapViewportRect.Contains(e.Location))
        {
            isDraggingMinimap = true;
            minimapDragOffset = new Point(e.X - minimapViewportRect.X, e.Y - minimapViewportRect.Y);
            minimapOverlay.Cursor = Cursors.SizeAll;
            return;
        }

        MoveViewportToMinimapPoint(e.Location, centerOnPoint: true);
        isDraggingMinimap = true;
        minimapDragOffset = new Point(minimapViewportRect.Width / 2, minimapViewportRect.Height / 2);
        minimapOverlay.Cursor = Cursors.SizeAll;
    }

    private void MinimapOverlay_MouseMove(object? sender, MouseEventArgs e)
    {
        if (isMinimapCollapsed) return;
        if (!isDraggingMinimap) return;
        MoveViewportToMinimapPoint(new Point(e.X - minimapDragOffset.X, e.Y - minimapDragOffset.Y), centerOnPoint: false);
    }

    private void MinimapOverlay_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!isDraggingMinimap) return;
        isDraggingMinimap = false;
        minimapOverlay.Cursor = Cursors.Default;
        minimapOverlay.Invalidate();
    }

    private void DrawMinimapToggle(Graphics g, Rectangle bounds, bool expanded)
    {
        Rectangle toggleRect = GetMinimapToggleRect(bounds, !expanded ? true : false);
        using SolidBrush toggleBrush = new(Color.FromArgb(230, 40, 44, 54));
        using Pen togglePen = new(Color.FromArgb(140, 170, 180, 195));
        g.FillRectangle(toggleBrush, toggleRect);
        g.DrawRectangle(togglePen, toggleRect);

        string arrow = expanded ? ">" : "<";
        TextRenderer.DrawText(
            g,
            arrow,
            Font,
            toggleRect,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private Rectangle GetMinimapToggleRect(Rectangle bounds, bool collapsed)
    {
        return collapsed
            ? new Rectangle(0, 0, bounds.Width, bounds.Height)
            : new Rectangle(bounds.Width - MinimapToggleWidth, 0, MinimapToggleWidth, bounds.Height);
    }

    private void OnNodeDataChanged(object? sender, EventArgs e)
    {
        if (isRestoringHistory || suppressHistoryCapture)
            return;

        OnBlueprintEdited();
        InvalidateCanvas();
    }

    private void OnBlueprintEdited()
    {
        if (isRestoringHistory)
            return;

        RefreshStatementOffsets();
        BlueprintEdited?.Invoke(this);
    }

    private void RefreshStatementOffsets()
    {
        PruneInvalidConnections();

        if (suppressOffsetRefreshDepth > 0)
            return;

        if (OffsetCalculator == null)
        {
            foreach (BlueprintNode node in nodes)
            {
                node.SetStatementOffset(null);
            }
            InvalidateCanvas();
            return;
        }

        if (offsetRefreshRunning)
        {
            offsetRefreshPending = true;
            return;
        }

        BlueprintData snapshot = BuildBlueprintDataSnapshot();
        offsetRefreshRunning = true;
        offsetRefreshPending = false;

        Task.Run(() =>
        {
            try
            {
                return (Success: true, Offsets: OffsetCalculator(snapshot), Error: string.Empty);
            }
            catch (Exception ex)
            {
                return (Success: false, Offsets: new Dictionary<Guid, int>(), Error: ex.Message);
            }
        }).ContinueWith(t =>
        {
            if (IsDisposed || !IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                try
                {
                    if (IsDisposed) return;

                    if (t.Result.Success)
                    {
                        Dictionary<Guid, int> offsets = t.Result.Offsets;
                        foreach (BlueprintNode node in nodes)
                        {
                            node.SetStatementOffset(offsets.TryGetValue(node.NodeId, out int offset) ? offset : null);
                        }
                        InvalidateCanvas();
                    }
                    else
                    {
                        //_ = MessageBox.Show($"失败: {t.Result.Error}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        foreach (BlueprintNode node in nodes)
                        {
                            node.SetStatementOffset(null);
                        }
                        InvalidateCanvas();
                        //StatusMessage?.Invoke($"计算偏移失败: {t.Result.Error}");
                    }
                }
                finally
                {
                    offsetRefreshRunning = false;
                    if (offsetRefreshPending)
                    {
                        offsetRefreshPending = false;
                        OnBlueprintEdited();
                    }
                }
            }));
        });
    }

    public BlueprintData ExportData()
    {
        CommitPendingEditorValues();
        PruneInvalidConnections();
        BlueprintData data = BuildBlueprintDataSnapshot();
        StatusMessage?.Invoke($"导出蓝图: {data.Nodes.Count} 个节点, {data.Connections.Count} 条连接");
        return data;
    }

    private BlueprintData BuildBlueprintDataSnapshot()
    {
        CommitPendingEditorValues();
        PruneInvalidConnections();

        var data = new BlueprintData();
        foreach (BlueprintNode node in nodes)
        {
            Dictionary<string, string> metaData = new(node.MetaData);
            string? callFunctionName = TryExtractSpecificCallFunctionName(node.Definition.Name);
            if (!string.IsNullOrWhiteSpace(callFunctionName))
            {
                metaData["CallFunctionName"] = callFunctionName;
            }

            Dictionary<string, object> pinValues = node.ExportPinValues();
            if (!string.IsNullOrWhiteSpace(callFunctionName))
                RemoveSpecificCallFunctionOverrides(pinValues);

            var serNode = new SerializableNode
            {
                Id = node.NodeId,
                DefinitionName = node.Definition.Name,
                Location = node.Location,
                PinValues = pinValues,
                MetaData = metaData,
                ReferenceDescriptors = node.Definition.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList(),
                PinSchemas = node.Definition.PinSchemas.Select(BlueprintModelCloner.Clone).ToList(),
                PropertyTemplateJsonByName = node.Definition.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            };
            NormalizeSpecificCallNodeState(serNode);
            data.Nodes.Add(serNode);
        }
        foreach (var conn in connections)
        {
            if (!nodes.Contains(conn.FromNode) || !nodes.Contains(conn.ToNode))
                continue;

            bool fromPinExists = conn.FromNode.OutputPins.Any(p => string.Equals(p.Name, conn.FromPin.Name, StringComparison.Ordinal) && p.Type == conn.FromPin.Type);
            bool toPinExists = conn.ToNode.InputPins.Any(p => string.Equals(p.Name, conn.ToPin.Name, StringComparison.Ordinal) && p.Type == conn.ToPin.Type);
            if (!fromPinExists || !toPinExists)
                continue;

            data.Connections.Add(new ConnectionData
            {
                FromNodeId = conn.FromNode.NodeId,
                FromPinName = conn.FromPin.Name,
                ToNodeId = conn.ToNode.NodeId,
                ToPinName = conn.ToPin.Name,
                Sequence = conn.Sequence
            });
        }
        return data;
    }

    public void ImportData(BlueprintData data, Dictionary<string, NodeDefinition> definitions)
    {
        ImportData(data, definitions, preserveImportedLayout: false);
        if (!isRestoringHistory)
        {
            undoStack.Clear();
            redoStack.Clear();
            hasPendingEditorUndoState = false;
            pendingEditorUndoState = null;
        }
    }

    private void ImportData(BlueprintData data, Dictionary<string, NodeDefinition> definitions, bool preserveImportedLayout)
    {
        SuspendLayout();
        surface.SuspendLayout();
        suppressOffsetRefreshDepth++;
        try
        {
            int oldNodeCount = nodes.Count;
            // 清除现有
            foreach (var node in nodes.ToList())
                node.Dispose();
            nodes.Clear();
            MarkSpatialIndexDirty();
            CloseActiveValueEditor(commit: false);
            connections.Clear();
            nextConnectionSequence = 1;
            selectedConnection = null;
            selectedConnections.Clear();
            selectedNodes.Clear();
            if (oldNodeCount > 0)
                StatusMessage?.Invoke($"清除了 {oldNodeCount} 个现有节点");

            var nodeMap = new Dictionary<Guid, BlueprintNode>();
            var importedNodeBounds = new List<Rectangle>();
            int importedNodes = 0;
            int missingDefs = 0;
            foreach (var serNode in data.Nodes.OrderBy(n => n.Location.Y).ThenBy(n => n.Location.X))
            {
                string normalizedDefinitionName = NormalizeLegacyDefinitionNameForCanvas(serNode.DefinitionName);
                if (definitions.TryGetValue(serNode.DefinitionName, out var def) ||
                    definitions.TryGetValue(normalizedDefinitionName, out def))
                {
                    var node = new BlueprintNode(def);
                    node.Definition.ReferenceDescriptors = serNode.ReferenceDescriptors.Select(BlueprintModelCloner.Clone).ToList();
                    node.Definition.PinSchemas = serNode.PinSchemas.Select(BlueprintModelCloner.Clone).ToList();
                    node.Definition.PropertyTemplateJsonByName = serNode.PropertyTemplateJsonByName.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                    node.ApplyPinValues(serNode.PinValues);
                    node.ApplyMetaData(serNode.MetaData);
                    NormalizeSpecificCallNodeState(node);
                    node.OwnerCanvas = this;
                    node.SetZoom(1.0f);
                    node.NodeDeleted += OnNodeDeleted;
                    node.NodeDataChanged += OnNodeDataChanged;
                    node.StartConnection += OnStartConnection;
                    node.EndConnection += OnEndConnection;
                    nodes.Add(node);
                    node.Location = serNode.Location;
                    node.RefreshLayoutMetrics();
                    if (!preserveImportedLayout)
                        node.Location = FindNonOverlappingLocation(serNode.Location, node.Size, importedNodeBounds);
                    MarkSpatialIndexDirty();
                    nodeMap[serNode.Id] = node;
                    importedNodeBounds.Add(new Rectangle(node.Location, node.Size));
                    importedNodes++;
                }
                else
                {
                    missingDefs++;
                }
            }

            int importedConnections = 0;
            foreach (var connData in data.Connections)
            {
                if (nodeMap.TryGetValue(connData.FromNodeId, out var fromNode) &&
                    nodeMap.TryGetValue(connData.ToNodeId, out var toNode))
                {
                    var fromPin = fromNode.OutputPins.FirstOrDefault(p => p.Name == connData.FromPinName);
                    var toPin = toNode.InputPins.FirstOrDefault(p => p.Name == connData.ToPinName);
                    if (fromPin != null && toPin != null && fromPin.Type == toPin.Type)
                    {
                        connections.Add(new Connection(fromNode, fromPin, toNode, toPin, NormalizeImportedConnectionSequence(connData.Sequence)));
                        importedConnections++;
                    }
                }
            }
            PruneInvalidConnections();
            if (!preserveImportedLayout)
                RelayoutImportedNodes();
            UpdateScrollArea();
            surface.Invalidate();
            StatusMessage?.Invoke($"导入完成: {importedNodes} 个节点, {importedConnections} 条连接" +
                (missingDefs > 0 ? $" (跳过 {missingDefs} 个未知节点定义)" : ""));
        }
        finally
        {
            suppressOffsetRefreshDepth = Math.Max(0, suppressOffsetRefreshDepth - 1);
            RefreshStatementOffsets();
            surface.ResumeLayout();
            ResumeLayout();
        }
    }

    private void RelayoutImportedNodes()
    {
        if (nodes.Count == 0)
            return;

        int globalTop = FreeCanvasMargin;
        IEnumerable<IGrouping<string, BlueprintNode>> functionGroups = nodes
            .GroupBy(node => ResolveNodeFunctionName(node) ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(group => group.Min(node => node.Top))
            .ThenBy(group => group.Key, StringComparer.Ordinal);

        foreach (IGrouping<string, BlueprintNode> functionGroup in functionGroups)
        {
            List<BlueprintNode> functionNodes = functionGroup.ToList();
            if (functionNodes.Count == 0)
                continue;

            List<BlueprintNode> eventNodes = GetOrderedEventNodesForLayout(functionNodes);
            List<BlueprintNode> mainNodes = GetOrderedMainNodesForLayout(functionNodes);
            HashSet<Guid> mainNodeIds = mainNodes.Select(node => node.NodeId).ToHashSet();
            HashSet<Guid> positionedNodeIds = new();

            int functionTop = Math.Max(globalTop, functionNodes.Min(node => node.Top));
            int eventColumnX = FreeCanvasMargin;
            int eventBottom = functionTop;
            int eventColumnWidth = 0;

            foreach (BlueprintNode eventNode in eventNodes)
            {
                eventNode.Location = new Point(eventColumnX, eventBottom);
                MarkSpatialIndexDirty();
                positionedNodeIds.Add(eventNode.NodeId);
                eventColumnWidth = Math.Max(eventColumnWidth, eventNode.Width);
                eventBottom = eventNode.Bottom + LayoutNodeSpacing;
            }

            int mainColumnX = mainNodes.Count > 0
                ? eventNodes.Count > 0
                    ? Math.Max(functionNodes.Min(node => node.Left), eventColumnX + eventColumnWidth + LayoutEventHorizontalSpacing)
                    : functionNodes.Min(node => node.Left)
                : functionNodes.Min(node => node.Left);
            int currentTop = functionTop;
            int mainBottom = currentTop;

            foreach (BlueprintNode mainNode in mainNodes)
            {
                mainNode.Location = new Point(mainColumnX, currentTop);
                MarkSpatialIndexDirty();
                positionedNodeIds.Add(mainNode.NodeId);

                int clusterBottom = LayoutDataClusterRecursive(
                    mainNode,
                    parentNode: null,
                    clusterTop: currentTop,
                    mainNodeIds,
                    positionedNodeIds,
                    new HashSet<Guid>());

                currentTop = clusterBottom + LayoutNodeSpacing;
                mainBottom = Math.Max(mainBottom, clusterBottom);
            }

            List<Rectangle> occupiedBounds = functionNodes
                .Where(node => positionedNodeIds.Contains(node.NodeId))
                .Select(node => new Rectangle(node.Location, node.Size))
                .ToList();

            foreach (BlueprintNode leftoverNode in functionNodes
                .Where(node => !positionedNodeIds.Contains(node.NodeId))
                .OrderBy(node => node.Top)
                .ThenBy(node => node.Left))
            {
                Point desiredLocation = new(leftoverNode.Left, Math.Max(currentTop, leftoverNode.Top));
                leftoverNode.Location = FindNonOverlappingLocation(desiredLocation, leftoverNode.Size, occupiedBounds);
                MarkSpatialIndexDirty();
                positionedNodeIds.Add(leftoverNode.NodeId);
                occupiedBounds.Add(new Rectangle(leftoverNode.Location, leftoverNode.Size));
                currentTop = Math.Max(currentTop, leftoverNode.Bottom + LayoutNodeSpacing);
            }

            int functionBottom = Math.Max(Math.Max(mainBottom, eventBottom), currentTop);
            globalTop = functionBottom + LayoutNodeSpacing * 2;
        }

        NormalizeContentBoundsIfNeeded();
    }

    private List<BlueprintNode> GetOrderedEventNodesForLayout(List<BlueprintNode> functionNodes)
    {
        return functionNodes
            .Where(IsEventNode)
            .OrderBy(node => TryGetTopLevelOrder(node, out int order) ? order : int.MaxValue)
            .ThenBy(node => node.Top)
            .ThenBy(node => node.Left)
            .ToList();
    }

    private List<BlueprintNode> GetOrderedMainNodesForLayout(List<BlueprintNode> functionNodes)
    {
        List<BlueprintNode> ordered = new();

        foreach (BlueprintNode topLevelNode in functionNodes
            .Where(node => !IsEventNode(node))
            .Where(node => TryGetTopLevelOrder(node, out _))
            .OrderBy(node => GetTopLevelOrderOrMax(node))
            .ThenBy(node => node.Top)
            .ThenBy(node => node.Left))
        {
            AddDistinctNode(ordered, topLevelNode);
        }

        if (ordered.Count > 0)
            return ordered;

        foreach (BlueprintNode execNode in functionNodes
            .Where(node => !IsEventNode(node))
            .Where(HasExecPins)
            .OrderBy(node => node.Top)
            .ThenBy(node => node.Left))
        {
            AddDistinctNode(ordered, execNode);
        }

        if (ordered.Count > 0)
            return ordered;

        ordered.AddRange(functionNodes.Where(node => !IsEventNode(node)).OrderBy(node => node.Top).ThenBy(node => node.Left));
        return ordered;
    }

    private static void AddDistinctNode(List<BlueprintNode> nodes, BlueprintNode node)
    {
        if (!nodes.Any(existing => existing.NodeId == node.NodeId))
            nodes.Add(node);
    }

    private static bool IsEventNode(BlueprintNode node)
    {
        return node.Definition.Name.StartsWith("Event ", StringComparison.Ordinal);
    }

    private static bool HasExecPins(BlueprintNode node)
    {
        return node.InputPins.Any(pin => pin.Type == PinType.Exec) ||
            node.OutputPins.Any(pin => pin.Type == PinType.Exec);
    }

    private static bool TryGetTopLevelOrder(BlueprintNode node, out int order)
    {
        order = int.MaxValue;
        return node.MetaData.TryGetValue("TopLevelOrder", out string? orderText) &&
            int.TryParse(orderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out order);
    }

    private static int GetTopLevelOrderOrMax(BlueprintNode node)
    {
        return TryGetTopLevelOrder(node, out int order) ? order : int.MaxValue;
    }

    private int LayoutDataClusterRecursive(
        BlueprintNode anchorNode,
        BlueprintNode? parentNode,
        int clusterTop,
        HashSet<Guid> mainNodeIds,
        HashSet<Guid> positionedNodeIds,
        HashSet<Guid> recursionPath)
    {
        if (!recursionPath.Add(anchorNode.NodeId))
            return anchorNode.Bottom;

        int clusterBottom = anchorNode.Bottom;
        List<DataAttachment> leftAttachments = GetOrderedDataAttachments(anchorNode, parentNode, placeLeft: true, mainNodeIds, positionedNodeIds);
        List<DataAttachment> rightAttachments = GetOrderedDataAttachments(anchorNode, parentNode, placeLeft: false, mainNodeIds, positionedNodeIds);

        clusterBottom = Math.Max(clusterBottom, LayoutAttachmentColumn(anchorNode, leftAttachments, placeLeft: true, clusterTop, mainNodeIds, positionedNodeIds, recursionPath));
        clusterBottom = Math.Max(clusterBottom, LayoutAttachmentColumn(anchorNode, rightAttachments, placeLeft: false, clusterTop, mainNodeIds, positionedNodeIds, recursionPath));

        recursionPath.Remove(anchorNode.NodeId);
        return clusterBottom;
    }

    private int LayoutAttachmentColumn(
        BlueprintNode anchorNode,
        List<DataAttachment> attachments,
        bool placeLeft,
        int clusterTop,
        HashSet<Guid> mainNodeIds,
        HashSet<Guid> positionedNodeIds,
        HashSet<Guid> recursionPath)
    {
        int nextTop = clusterTop;
        int clusterBottom = clusterTop;

        foreach (DataAttachment attachment in attachments)
        {
            BlueprintNode childNode = attachment.Node;
            int top = Math.Max(Math.Max(clusterTop, attachment.AnchorTop), nextTop);
            int left = placeLeft
                ? anchorNode.Left - LayoutHorizontalSpacing - childNode.Width
                : anchorNode.Right + LayoutHorizontalSpacing;

            childNode.Location = new Point(left, top);
            MarkSpatialIndexDirty();
            positionedNodeIds.Add(childNode.NodeId);

            int subtreeBottom = LayoutDataClusterRecursive(
                childNode,
                anchorNode,
                top,
                mainNodeIds,
                positionedNodeIds,
                recursionPath);

            clusterBottom = Math.Max(clusterBottom, subtreeBottom);
            nextTop = subtreeBottom + LayoutNodeSpacing;
        }

        return clusterBottom;
    }

    private List<DataAttachment> GetOrderedDataAttachments(
        BlueprintNode anchorNode,
        BlueprintNode? parentNode,
        bool placeLeft,
        HashSet<Guid> mainNodeIds,
        HashSet<Guid> positionedNodeIds)
    {
        IEnumerable<(BlueprintNode Node, int PinOrder, int AnchorTop)> candidates = placeLeft
            ? connections
                .Where(conn => conn.ToNode == anchorNode &&
                    conn.ToPin.Type == PinType.Data &&
                    conn.FromNode != parentNode &&
                    !mainNodeIds.Contains(conn.FromNode.NodeId))
                .Select(conn => (
                    Node: conn.FromNode,
                    PinOrder: GetPinOrder(anchorNode.InputPins, conn.ToPin.Name, conn.ToPin.Type),
                    AnchorTop: anchorNode.Top + conn.ToPin.Center.Y - conn.FromNode.Height / 2))
            : connections
                .Where(conn => conn.FromNode == anchorNode &&
                    conn.FromPin.Type == PinType.Data &&
                    conn.ToNode != parentNode &&
                    !mainNodeIds.Contains(conn.ToNode.NodeId))
                .Select(conn => (
                    Node: conn.ToNode,
                    PinOrder: GetPinOrder(anchorNode.OutputPins, conn.FromPin.Name, conn.FromPin.Type),
                    AnchorTop: anchorNode.Top + conn.FromPin.Center.Y - conn.ToNode.Height / 2));

        return candidates
            .Where(candidate => !positionedNodeIds.Contains(candidate.Node.NodeId))
            .GroupBy(candidate => candidate.Node.NodeId)
            .Select(group =>
            {
                var best = group
                    .OrderBy(item => item.PinOrder)
                    .ThenBy(item => item.AnchorTop)
                    .First();
                return new DataAttachment
                {
                    Node = best.Node,
                    PinOrder = best.PinOrder,
                    AnchorTop = best.AnchorTop
                };
            })
            .OrderBy(item => item.PinOrder)
            .ThenBy(item => item.AnchorTop)
            .ThenBy(item => item.Node.Top)
            .ToList();
    }

    private static int GetPinOrder(List<Pin> pins, string pinName, PinType pinType)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            Pin pin = pins[i];
            if (pin.Type == pinType && string.Equals(pin.Name, pinName, StringComparison.Ordinal))
                return i;
        }

        return int.MaxValue;
    }

    private void CommitPendingEditorValues()
    {
        FindForm()?.ValidateChildren();
        foreach (BlueprintNode node in nodes)
        {
            node.FlushEditorValueToPinValues();
        }
    }

    private void BeginBoxSelection(Point surfacePoint)
    {
        ClearSelection();
        selectionStartSurface = surfacePoint;
        selectionCurrentSurface = surfacePoint;
        isBoxSelecting = true;
        InvalidateCanvas();
    }

    private void CompleteBoxSelection()
    {
        isBoxSelecting = false;
        Rectangle selectionRect = GetNormalizedRectangle(selectionStartSurface, selectionCurrentSurface);
        if (selectionRect.Width < SelectionDragThreshold && selectionRect.Height < SelectionDragThreshold)
        {
            InvalidateCanvas();
            return;
        }

        foreach (BlueprintNode node in nodes)
        {
            if (selectionRect.IntersectsWith(node.Bounds))
                SelectNode(node);
        }

        foreach (Connection conn in connections)
        {
            if (DoesConnectionIntersectSelection(conn, selectionRect))
                selectedConnections.Add(conn);
        }

        selectedConnection = selectedConnections.Count == 1 ? selectedConnections.First() : null;
        InvalidateCanvas();
        StatusMessage?.Invoke($"已框选: {selectedNodes.Count} 个节点, {selectedConnections.Count} 条连接");
    }

    private void DrawSelectionBox(Graphics g)
    {
        if (!isBoxSelecting) return;

        Rectangle selectionRect = GetNormalizedRectangle(selectionStartSurface, selectionCurrentSurface);
        if (selectionRect.Width <= 0 || selectionRect.Height <= 0) return;

        using SolidBrush fill = new(Color.FromArgb(40, 120, 180, 255));
        using Pen border = new(Color.FromArgb(180, 120, 180, 255), 1f) { DashStyle = DashStyle.Dash };
        g.FillRectangle(fill, selectionRect);
        g.DrawRectangle(border, selectionRect);
    }

    private static Rectangle GetNormalizedRectangle(Point start, Point end)
    {
        int left = Math.Min(start.X, end.X);
        int top = Math.Min(start.Y, end.Y);
        int right = Math.Max(start.X, end.X);
        int bottom = Math.Max(start.Y, end.Y);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private bool DoesConnectionIntersectSelection(Connection conn, Rectangle selectionRect)
    {
        Point start = conn.FromNode.GetPinWorldPosition(conn.FromPin);
        Point end = conn.ToNode.GetPinWorldPosition(conn.ToPin);
        int offset = Math.Abs(end.X - start.X) / 2;
        Point ctrl1 = new(start.X + offset, start.Y);
        Point ctrl2 = new(end.X - offset, end.Y);
        const int segmentCount = 28;
        PointF previous = start;
        if (selectionRect.Contains(Point.Round(previous)))
            return true;

        for (int i = 1; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            PointF current = EvaluateCubicBezier(start, ctrl1, ctrl2, end, t);
            if (selectionRect.Contains(Point.Round(current)) ||
                SegmentIntersectsRect(previous, current, selectionRect))
            {
                return true;
            }

            previous = current;
        }

        return false;
    }

    private static PointF EvaluateCubicBezier(Point p0, Point p1, Point p2, Point p3, float t)
    {
        float u = 1f - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        return new PointF(
            uuu * p0.X + 3f * uu * t * p1.X + 3f * u * tt * p2.X + ttt * p3.X,
            uuu * p0.Y + 3f * uu * t * p1.Y + 3f * u * tt * p2.Y + ttt * p3.Y);
    }

    private static bool SegmentIntersectsRect(PointF a, PointF b, Rectangle rect)
    {
        if (rect.Contains(Point.Round(a)) || rect.Contains(Point.Round(b)))
            return true;

        PointF topLeft = new(rect.Left, rect.Top);
        PointF topRight = new(rect.Right, rect.Top);
        PointF bottomLeft = new(rect.Left, rect.Bottom);
        PointF bottomRight = new(rect.Right, rect.Bottom);

        return SegmentsIntersect(a, b, topLeft, topRight) ||
            SegmentsIntersect(a, b, topRight, bottomRight) ||
            SegmentsIntersect(a, b, bottomRight, bottomLeft) ||
            SegmentsIntersect(a, b, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(PointF p1, PointF p2, PointF q1, PointF q2)
    {
        float o1 = Cross(p1, p2, q1);
        float o2 = Cross(p1, p2, q2);
        float o3 = Cross(q1, q2, p1);
        float o4 = Cross(q1, q2, p2);

        if ((o1 > 0f && o2 < 0f || o1 < 0f && o2 > 0f) &&
            (o3 > 0f && o4 < 0f || o3 < 0f && o4 > 0f))
        {
            return true;
        }

        const float epsilon = 0.01f;
        return (Math.Abs(o1) < epsilon && OnSegment(p1, q1, p2)) ||
            (Math.Abs(o2) < epsilon && OnSegment(p1, q2, p2)) ||
            (Math.Abs(o3) < epsilon && OnSegment(q1, p1, q2)) ||
            (Math.Abs(o4) < epsilon && OnSegment(q1, p2, q2));
    }

    private static float Cross(PointF a, PointF b, PointF c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool OnSegment(PointF a, PointF p, PointF b)
    {
        return p.X <= Math.Max(a.X, b.X) + 0.01f &&
            p.X >= Math.Min(a.X, b.X) - 0.01f &&
            p.Y <= Math.Max(a.Y, b.Y) + 0.01f &&
            p.Y >= Math.Min(a.Y, b.Y) - 0.01f;
    }

    private void SelectNode(BlueprintNode node)
    {
        if (selectedNodes.Add(node))
            node.IsSelected = true;
    }

    private void ClearSelection()
    {
        foreach (BlueprintNode node in selectedNodes)
            node.IsSelected = false;
        selectedNodes.Clear();
        selectedConnections.Clear();
        selectedConnection = null;
    }

    private bool DeleteNodeInternal(BlueprintNode node, bool reportStatus)
    {
        if (!nodes.Contains(node))
            return false;

        if (reportStatus)
            CaptureUndoState();

        List<Connection> related = connections.Where(c => c.FromNode == node || c.ToNode == node).ToList();
        int removedConnections = 0;
        foreach (Connection conn in related)
        {
            if (connections.Remove(conn))
                removedConnections++;
            selectedConnections.Remove(conn);
        }

        if (selectedConnection != null && related.Contains(selectedConnection))
            selectedConnection = null;

        selectedNodes.Remove(node);
        node.IsSelected = false;
        nodes.Remove(node);
        if (activeEditorNode == node)
            CloseActiveValueEditor(commit: false);
        node.Dispose();
        MarkSpatialIndexDirty();

        OnBlueprintEdited();
        UpdateScrollArea();
        InvalidateCanvas();

        if (reportStatus)
            StatusMessage?.Invoke($"删除节点: {node.Definition.Name}，移除了 {removedConnections} 条关联连接");

        return true;
    }

    private void PruneInvalidConnections()
    {
        HashSet<BlueprintNode> nodeSet = new(nodes);
        Dictionary<string, Connection> bestByExactKey = new(StringComparer.Ordinal);
        Dictionary<string, Connection> bestByIncomingKey = new(StringComparer.Ordinal);
        List<Connection> invalid = [];

        foreach (Connection conn in connections)
        {
            if (!nodeSet.Contains(conn.FromNode) || !nodeSet.Contains(conn.ToNode))
            {
                invalid.Add(conn);
                continue;
            }

            bool fromPinExists = conn.FromNode.OutputPins.Any(p =>
                string.Equals(p.Name, conn.FromPin.Name, StringComparison.Ordinal) &&
                p.Type == conn.FromPin.Type);
            bool toPinExists = conn.ToNode.InputPins.Any(p =>
                string.Equals(p.Name, conn.ToPin.Name, StringComparison.Ordinal) &&
                p.Type == conn.ToPin.Type);

            if (!fromPinExists || !toPinExists || conn.FromPin.Type != conn.ToPin.Type)
            {
                invalid.Add(conn);
                continue;
            }

            string key = $"{conn.FromNode.NodeId}|{conn.FromPin.Name}|{conn.ToNode.NodeId}|{conn.ToPin.Name}|{conn.FromPin.Type}";
            if (!TryKeepHigherSequence(bestByExactKey, key, conn, invalid))
            {
                continue;
            }

            if (!AllowsMultipleIncomingConnections(conn.ToPin))
            {
                string incomingKey = $"{conn.ToNode.NodeId}|{conn.ToPin.Name}|{conn.ToPin.Type}";
                _ = TryKeepHigherSequence(bestByIncomingKey, incomingKey, conn, invalid);
            }
        }

        if (invalid.Count == 0)
            return;

        foreach (Connection conn in invalid)
        {
            connections.Remove(conn);
            selectedConnections.Remove(conn);
            if (selectedConnection == conn)
                selectedConnection = null;
        }
    }

    private static bool TryKeepHigherSequence(Dictionary<string, Connection> bestByKey, string key, Connection candidate, List<Connection> invalid)
    {
        if (!bestByKey.TryGetValue(key, out Connection? existing))
        {
            bestByKey[key] = candidate;
            return true;
        }

        if (candidate.Sequence >= existing.Sequence)
        {
            invalid.Add(existing);
            bestByKey[key] = candidate;
            return true;
        }

        invalid.Add(candidate);
        return false;
    }

    private Point FindNonOverlappingLocation(Point desiredLocation, Size nodeSize, List<Rectangle> occupiedBounds)
    {
        const int spacingX = LayoutNodeSpacing;
        const int spacingY = LayoutNodeSpacing;
        int anchorX = desiredLocation.X;
        int anchorY = desiredLocation.Y;
        int stepY = Math.Max(spacingY + 8, Math.Min(140, nodeSize.Height / 3 + spacingY));
        int stepX = Math.Max(spacingX + 8, Math.Min(220, nodeSize.Width / 3 + spacingX));
        int maxRing = Math.Max(24, occupiedBounds.Count / 6 + 12);

        // 先尝试目标点，最大化保留原布局。
        if (IsPlacementAvailable(new Rectangle(anchorX, anchorY, nodeSize.Width, nodeSize.Height), occupiedBounds, spacingX, spacingY))
            return new Point(anchorX, anchorY);

        // 以目标点为中心按“环”搜索，优先使用最近候选点。
        for (int ring = 1; ring <= maxRing; ring++)
        {
            Point? best = null;
            long bestDistance = long.MaxValue;

            foreach (Point offset in EnumerateRingOffsets(ring))
            {
                int x = anchorX + offset.X * stepX;
                int y = anchorY + offset.Y * stepY;
                Rectangle candidate = new(x, y, nodeSize.Width, nodeSize.Height);
                if (!IsPlacementAvailable(candidate, occupiedBounds, spacingX, spacingY))
                    continue;

                long dx = x - anchorX;
                long dy = y - anchorY;
                long distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = new Point(x, y);
                }
            }

            if (best.HasValue)
                return best.Value;
        }

        // 兜底：在最右侧新列向下扫描，保证总能返回。
        int rightMost = occupiedBounds.Count == 0 ? anchorX : occupiedBounds.Max(rect => rect.Right);
        int fallbackX = Math.Max(anchorX, rightMost + spacingX);
        int fallbackY = anchorY;
        Rectangle fallback = new(fallbackX, fallbackY, nodeSize.Width, nodeSize.Height);
        while (!IsPlacementAvailable(fallback, occupiedBounds, spacingX, spacingY))
        {
            fallbackY += Math.Max(nodeSize.Height + spacingY, 32);
            fallback = new Rectangle(fallbackX, fallbackY, nodeSize.Width, nodeSize.Height);
        }
        return new Point(fallback.X, fallback.Y);
    }

    private void NormalizeContentBoundsIfNeeded()
    {
        if (nodes.Count == 0)
            return;

        int minLeft = nodes.Min(node => node.Left);
        int minTop = nodes.Min(node => node.Top);
        int shiftX = minLeft < FreeCanvasMargin ? FreeCanvasMargin - minLeft : 0;
        int shiftY = minTop < FreeCanvasMargin ? FreeCanvasMargin - minTop : 0;
        if (shiftX == 0 && shiftY == 0)
            return;

        foreach (BlueprintNode node in nodes)
        {
            node.Location = new Point(node.Left + shiftX, node.Top + shiftY);
        }
        MarkSpatialIndexDirty();

        viewportOrigin = new PointF(viewportOrigin.X + shiftX, viewportOrigin.Y + shiftY);
    }

    private static bool RectanglesOverlap(Rectangle left, Rectangle right, int spacingX, int spacingY)
    {
        Rectangle padded = Rectangle.Inflate(right, spacingX, spacingY);
        return padded.IntersectsWith(left);
    }

    private static bool IsPlacementAvailable(Rectangle candidate, List<Rectangle> occupiedBounds, int spacingX, int spacingY)
    {
        return !occupiedBounds.Any(existing => RectanglesOverlap(candidate, existing, spacingX, spacingY));
    }

    private static IEnumerable<Point> EnumerateRingOffsets(int ring)
    {
        // 上下边
        for (int x = -ring; x <= ring; x++)
        {
            yield return new Point(x, -ring);
            yield return new Point(x, ring);
        }

        // 左右边（去掉角点，避免重复）
        for (int y = -ring + 1; y <= ring - 1; y++)
        {
            yield return new Point(-ring, y);
            yield return new Point(ring, y);
        }
    }
}

// 连线类
public class Connection
{
    public BlueprintNode FromNode { get; }
    public Pin FromPin { get; }
    public BlueprintNode ToNode { get; }
    public Pin ToPin { get; }
    public long Sequence { get; }
    public Connection(BlueprintNode fromNode, Pin fromPin, BlueprintNode toNode, Pin toPin, long sequence)
    {
        FromNode = fromNode;
        FromPin = fromPin;
        ToNode = toNode;
        ToPin = toPin;
        Sequence = sequence;
    }
}

// ========== 动态定义对话框 ==========
public class DefineNodeDialog : Form
{
    private TextBox txtName;
    private DataGridView dgvPins;
    private Button btnOK, btnCancel;

    public NodeDefinition Result { get; private set; }

    public DefineNodeDialog()
    {
        Text = "定义新蓝图块";
        Size = new Size(500, 400);
        StartPosition = FormStartPosition.CenterParent;

        var lblName = new Label { Text = "节点名称:", Location = new Point(12, 15), AutoSize = true };
        txtName = new TextBox { Location = new Point(120, 12), Width = 200 };

        dgvPins = new DataGridView
        {
            Location = new Point(12, 50),
            Size = new Size(460, 280),
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Columns =
                {
                    new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "引脚名称" },
                    new DataGridViewComboBoxColumn { Name = "Direction", HeaderText = "方向", Items = { "Input", "Output" } },
                    new DataGridViewComboBoxColumn { Name = "Type", HeaderText = "类型", Items = { "Exec", "Data" } }
                }
        };

        btnOK = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(300, 340) };
        btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(390, 340) };

        Controls.Add(lblName);
        Controls.Add(txtName);
        Controls.Add(dgvPins);
        Controls.Add(btnOK);
        Controls.Add(btnCancel);

        btnOK.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("请输入节点名称");
                return;
            }
            Result = new NodeDefinition { Name = txtName.Text };
            var inputs = new List<Pin>();
            var outputs = new List<Pin>();
            foreach (DataGridViewRow row in dgvPins.Rows)
            {
                if (row.IsNewRow) continue;
                var name = row.Cells["Name"].Value?.ToString();
                var dirStr = row.Cells["Direction"].Value?.ToString();
                var typeStr = row.Cells["Type"].Value?.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                PinDirection dir = dirStr == "Output" ? PinDirection.Output : PinDirection.Input;
                PinType type = typeStr == "Exec" ? PinType.Exec : PinType.Data;
                var pin = new Pin(name, dir, type);
                if (dir == PinDirection.Input)
                    inputs.Add(pin);
                else
                    outputs.Add(pin);
            }
            Result.InputPins = inputs;
            Result.OutputPins = outputs;
            DialogResult = DialogResult.OK;
        };
    }
}
