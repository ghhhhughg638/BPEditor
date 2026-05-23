1.0-1.3
用户有一个 BPEditor 工具（C# WinForms，用于修改 KARDS 游戏 UE4 蓝图资产 .uasset/.json）。用户需要修改一张卡牌 card_unit_jet_protoype_a：

原版效果：弃掉牌堆顶部的偶数费用卡牌，每弃一张减少自身 1 点操作费用（evenCount * -1 → ChangeOperationCost）

目标效果：弃掉牌堆顶部的偶数费用卡牌，每弃一张增加自身 1 点攻击力（evenCount * 1 → ChangeAttack）

遇到的 Bug
Bug 1：EX_ArrayGetByRef 被替换成 EX_Nothing
BPE 在保存蓝图时，会把 EX_ArrayGetByRef（获取数组元素）错误地替换成 EX_Nothing，导致弃牌逻辑失效。原因是在 CollectChildExpressionSlotsCore 中，key == "Expression" 时直接跳过，影响了 Int Const 的保存。

Bug 2：Int Const 值修改后保存被重置
由于上述修复过于宽泛，导致独立的 Int Const 节点值无法保存。

最终修复方案
在 Form1.cs 中精确修改：

CollectChildExpressionSlotsCore 方法
csharp
private IEnumerable<ExpressionChildSlot> CollectChildExpressionSlotsCore(JsonObject expressionObj)
{
    string parentType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);

    foreach ((string key, JsonNode? value) in expressionObj)
    {
        if (key == "$type" || value == null) continue;

        // 只跳过 EX_Let 中指向 ArrayGetByRef 的 Expression
        if (key == "Expression" && parentType == "Let")
        {
            if (value is JsonObject exprObj)
            {
                string innerType = SimplifyExpressionType(exprObj["$type"]?.GetValue<string>() ?? string.Empty);
                if (innerType == "ArrayGetByRef") continue;
            }
        }

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

            string pinName = key == "Parameters" ? $"Arg{i + 1}" : $"{NormalizePinName(key)}[{i}]";
            int arrayIndex = i;
            yield return new ExpressionChildSlot(pinName, replacement => arrayValue[arrayIndex] = replacement);
        }
    }
}
ShouldManageUserChildSlot 方法
csharp
private bool ShouldManageUserChildSlot(SerializableNode node, JsonObject expressionObj, string slotPinName)
{
    string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);

    if (expressionType == "Let" && slotPinName == "Expression")
    {
        if (expressionObj["Expression"] is JsonObject exprObj)
        {
            string innerType = SimplifyExpressionType(exprObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (innerType == "ArrayGetByRef") return false;
        }
    }

    // ... 原有代码保持不变 ...
}
编译方法
cmd
cd E:\BPEditor
dotnet build -c Release
编译输出：E:\BPEditor\bin\Release\net8.0-windows\BPEditor.exe

注意事项
项目中引用 UAssetAPI 类库，需要放在正确路径（E:\UAssetAPI\UAssetAPI\）

.NET 8 SDK 已安装

编译时若出现 icon 错误，删除 .csproj 中的 <ApplicationIcon> 行
