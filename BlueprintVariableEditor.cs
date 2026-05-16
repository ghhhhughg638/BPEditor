using System.Globalization;

public enum BlueprintVariableType
{
    Int,
    Bool,
    Float,
    Double,
    Byte,
    String,
    Name,
    Text,
    Object,
    Array,
    Enum,
    Struct,
    SoftObject,
    Unknown
}

public sealed class BlueprintVariableDefinition
{
    public string Name { get; set; } = string.Empty;
    public BlueprintVariableType VariableType { get; set; } = BlueprintVariableType.Int;
    public string DefaultValueText { get; set; } = string.Empty;
    public string SerializedTypeName { get; set; } = string.Empty;
    public string OwnerKind { get; set; } = "Function";
    public string OwnerObjectName { get; set; } = string.Empty;
    public string? SourceFunctionName { get; set; }
    public string PropertyTemplateJson { get; set; } = string.Empty;
    public string? ExemplarTemplateJson { get; set; }
}

internal sealed class AddBlueprintVariableDialog : Form
{
    private readonly HashSet<string> existingNames;
    private readonly IReadOnlyDictionary<BlueprintVariableType, List<BlueprintVariableDefinition>> exemplarDefinitionsByType;
    private readonly TextBox txtName;
    private readonly ComboBox cboType;
    private readonly ComboBox cboBoolDefault;
    private readonly Label lblDefault;
    private readonly Label lblTemplate;
    private readonly ComboBox cboTemplate;
    private readonly Button btnOk;
    private readonly Button btnCancel;

    public BlueprintVariableDefinition Result { get; private set; } = new();

    public AddBlueprintVariableDialog(
        IEnumerable<string> existingNames,
        IReadOnlyDictionary<BlueprintVariableType, List<BlueprintVariableDefinition>> exemplarDefinitionsByType)
    {
        this.existingNames = new HashSet<string>(existingNames ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        this.exemplarDefinitionsByType = exemplarDefinitionsByType ?? new Dictionary<BlueprintVariableType, List<BlueprintVariableDefinition>>();

        Text = "添加变量";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(390, 220);

        Label lblName = new()
        {
            Text = "变量名",
            AutoSize = true,
            Location = new Point(18, 20)
        };
        Controls.Add(lblName);

        txtName = new TextBox
        {
            Location = new Point(112, 16),
            Width = 220
        };
        Controls.Add(txtName);

        Label lblType = new()
        {
            Text = "变量类型",
            AutoSize = true,
            Location = new Point(18, 60)
        };
        Controls.Add(lblType);

        cboType = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(112, 56),
            Width = 250
        };
        cboType.Items.AddRange(GetCreatableTypes()
            .Select(type => type.ToString())
            .Cast<object>()
            .ToArray());
        cboType.SelectedIndex = 0;
        cboType.SelectedIndexChanged += (_, _) => UpdateDynamicEditors();
        Controls.Add(cboType);

        lblDefault = new Label
        {
            Text = "默认值",
            AutoSize = true,
            Location = new Point(18, 100)
        };
        Controls.Add(lblDefault);

        cboBoolDefault = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(112, 96),
            Width = 250
        };
        cboBoolDefault.Items.AddRange(["False", "True"]);
        cboBoolDefault.SelectedIndex = 0;
        Controls.Add(cboBoolDefault);

        lblTemplate = new Label
        {
            Text = "克隆模板",
            AutoSize = true,
            Location = new Point(18, 136),
            Visible = false
        };
        Controls.Add(lblTemplate);

        cboTemplate = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(112, 132),
            Width = 250,
            Visible = false
        };
        Controls.Add(cboTemplate);

        btnOk = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.None,
            Location = new Point(206, 172),
            Width = 75
        };
        btnOk.Click += (_, _) => ConfirmAndClose();
        Controls.Add(btnOk);

        btnCancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(287, 172),
            Width = 75
        };
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        UpdateDynamicEditors();
    }

    private IEnumerable<BlueprintVariableType> GetCreatableTypes()
    {
        yield return BlueprintVariableType.Int;
        yield return BlueprintVariableType.Bool;
        yield return BlueprintVariableType.Float;
        yield return BlueprintVariableType.Double;
        yield return BlueprintVariableType.Byte;
        yield return BlueprintVariableType.String;
        yield return BlueprintVariableType.Name;
        yield return BlueprintVariableType.Text;

        foreach (BlueprintVariableType complexType in new[]
        {
            BlueprintVariableType.Object,
            BlueprintVariableType.Array,
            BlueprintVariableType.Enum,
            BlueprintVariableType.Struct,
            BlueprintVariableType.SoftObject
        })
        {
            if (exemplarDefinitionsByType.TryGetValue(complexType, out List<BlueprintVariableDefinition>? exemplars) &&
                exemplars.Count > 0)
            {
                yield return complexType;
            }
        }
    }

    private void UpdateDynamicEditors()
    {
        BlueprintVariableType type = ResolveSelectedType();
        bool isBool = type == BlueprintVariableType.Bool;
        bool needsTemplate = TypeRequiresExemplar(type);

        lblDefault.Visible = isBool;
        cboBoolDefault.Visible = isBool;
        lblTemplate.Visible = needsTemplate;
        cboTemplate.Visible = needsTemplate;

        if (needsTemplate)
        {
            cboTemplate.BeginUpdate();
            try
            {
                cboTemplate.Items.Clear();
                if (exemplarDefinitionsByType.TryGetValue(type, out List<BlueprintVariableDefinition>? exemplars))
                {
                    foreach (BlueprintVariableDefinition exemplar in exemplars.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        cboTemplate.Items.Add(exemplar.Name);
                    }
                }

                cboTemplate.SelectedIndex = cboTemplate.Items.Count > 0 ? 0 : -1;
            }
            finally
            {
                cboTemplate.EndUpdate();
            }
        }
    }

    private BlueprintVariableType ResolveSelectedType()
    {
        string raw = cboType.SelectedItem?.ToString() ?? BlueprintVariableType.Int.ToString();
        return Enum.TryParse(raw, ignoreCase: true, out BlueprintVariableType parsed)
            ? parsed
            : BlueprintVariableType.Int;
    }

    private void ConfirmAndClose()
    {
        string name = txtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "变量名不能为空。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            txtName.Focus();
            return;
        }

        if (string.Equals(name, BlueprintCanvas.AddVariableOptionText, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "该名称保留给下拉框命令项，请换一个变量名。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            txtName.Focus();
            return;
        }

        if (existingNames.Contains(name))
        {
            MessageBox.Show(this, "变量名已存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            txtName.Focus();
            return;
        }

        BlueprintVariableType type = ResolveSelectedType();
        string defaultValueText = type == BlueprintVariableType.Bool
            ? (cboBoolDefault.SelectedIndex == 1 ? "True" : "False")
            : string.Empty;
        string normalizedBoolValue = string.Empty;

        if (type == BlueprintVariableType.Bool &&
            !ValidateDefaultValue(type, defaultValueText, out normalizedBoolValue))
        {
            MessageBox.Show(this, "默认值格式不正确。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            cboBoolDefault.Focus();
            return;
        }

        string normalized = type == BlueprintVariableType.Bool
            ? normalizedBoolValue
            : string.Empty;

        BlueprintVariableDefinition? exemplarDefinition = null;
        if (TypeRequiresExemplar(type))
        {
            string? templateName = cboTemplate.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(templateName) ||
                !exemplarDefinitionsByType.TryGetValue(type, out List<BlueprintVariableDefinition>? exemplars))
            {
                MessageBox.Show(this, "当前类型缺少可用模板，无法创建变量。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                cboTemplate.Focus();
                return;
            }

            exemplarDefinition = exemplars.FirstOrDefault(item => string.Equals(item.Name, templateName, StringComparison.Ordinal));
            if (exemplarDefinition == null || string.IsNullOrWhiteSpace(exemplarDefinition.PropertyTemplateJson))
            {
                MessageBox.Show(this, "当前类型缺少可用模板，无法创建变量。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                cboTemplate.Focus();
                return;
            }
        }

        Result = new BlueprintVariableDefinition
        {
            Name = name,
            VariableType = type,
            DefaultValueText = normalized,
            SerializedTypeName = exemplarDefinition?.SerializedTypeName ?? string.Empty,
            ExemplarTemplateJson = exemplarDefinition?.PropertyTemplateJson
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool ValidateDefaultValue(BlueprintVariableType type, string rawValue, out string normalized)
    {
        normalized = rawValue ?? string.Empty;
        switch (type)
        {
            case BlueprintVariableType.Int:
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                    return false;
                normalized = intValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case BlueprintVariableType.Bool:
                if (!bool.TryParse(rawValue, out bool boolValue))
                    return false;
                normalized = boolValue ? "True" : "False";
                return true;
            case BlueprintVariableType.Float:
                if (!float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float floatValue))
                    return false;
                normalized = floatValue.ToString("0.0######", CultureInfo.InvariantCulture);
                return true;
            case BlueprintVariableType.Double:
                if (!double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue))
                    return false;
                normalized = doubleValue.ToString("0.0###############", CultureInfo.InvariantCulture);
                return true;
            case BlueprintVariableType.Byte:
                if (!byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte byteValue))
                    return false;
                normalized = byteValue.ToString(CultureInfo.InvariantCulture);
                return true;
            case BlueprintVariableType.String:
            case BlueprintVariableType.Name:
            case BlueprintVariableType.Text:
                normalized = rawValue ?? string.Empty;
                return true;
            default:
                return false;
        }
    }

    private static bool TypeRequiresExemplar(BlueprintVariableType type)
    {
        return type is BlueprintVariableType.Object or
            BlueprintVariableType.Array or
            BlueprintVariableType.Enum or
            BlueprintVariableType.Struct or
            BlueprintVariableType.SoftObject;
    }
}
