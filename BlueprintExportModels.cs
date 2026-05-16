using System.Text.Json.Serialization;

public sealed class BlueprintObjectSymbol
{
    public int Index { get; set; }
    public int OuterIndex { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public string? ClassPackage { get; set; }
    public string? PackageName { get; set; }
    public string? OuterObjectName { get; set; }
    public bool IsExport { get; set; }
}

public sealed class NodeReferenceDescriptor
{
    public string PointerPath { get; set; } = string.Empty;
    public string ReferenceKind { get; set; } = string.Empty;
    public int OriginalIndex { get; set; }
    public string? SourceAssetRelativePath { get; set; }
    public string? ObjectName { get; set; }
    public string? ClassName { get; set; }
    public string? ClassPackage { get; set; }
    public string? PackageName { get; set; }
    public string? OuterObjectName { get; set; }
    public string? SourceFunctionName { get; set; }
}

public sealed class FieldOwnerEvidence
{
    public string FieldName { get; set; } = string.Empty;
    public int OwnerIndex { get; set; }
    public BlueprintObjectSymbol? OwnerSymbol { get; set; }
    public string ExpressionType { get; set; } = string.Empty;
    public string PointerPath { get; set; } = string.Empty;
    public string? SourceFunctionName { get; set; }
}

public sealed class PinExportSchema
{
    public string PinName { get; set; } = string.Empty;
    public string SemanticKind { get; set; } = string.Empty;
    public string? ExpressionPath { get; set; }
    public string? PropertyName { get; set; }
    public string? PropertyOwnerKind { get; set; }
    public string? PropertyTemplateJson { get; set; }
    public string? ContainerType { get; set; }
    public string? ValueTypeName { get; set; }
    public bool IsReturnValue { get; set; }
    public bool IsParameter { get; set; }
    public bool IsTargetObject { get; set; }
}

public sealed class LoadedPropertyTemplate
{
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyTemplateJson { get; set; } = string.Empty;
    public string OwnerObjectName { get; set; } = string.Empty;
    public string OwnerKind { get; set; } = string.Empty;
    public string? SourceFunctionName { get; set; }
    public int OriginalOrdinal { get; set; } = -1;
    public long CreatedSequence { get; set; }
    public int FirstUseOrdinal { get; set; } = int.MaxValue;
    public bool IsUserCreated { get; set; }
    public List<NodeReferenceDescriptor> ReferenceDescriptors { get; set; } = new();
}

public sealed class DataPropertyTemplate
{
    public string Name { get; set; } = string.Empty;
    public string PropertyType { get; set; } = string.Empty;
    public string TemplateJson { get; set; } = string.Empty;
    public string? EnumType { get; set; }
    public List<string> EnumOptions { get; set; } = new();
    public int OccurrenceCount { get; set; }
    public string? SourceAssetRelativePath { get; set; }
}

public sealed class BlueprintFunctionTemplate
{
    public string FunctionName { get; set; } = string.Empty;
    public string FunctionExportTemplateJson { get; set; } = string.Empty;
    public List<NodeReferenceDescriptor> FunctionExportReferences { get; set; } = new();
    public Dictionary<string, LoadedPropertyTemplate> LoadedPropertyTemplatesByName { get; set; } = new(StringComparer.Ordinal);
    public List<string> ParameterNames { get; set; } = new();
    public List<string> ReturnValueNames { get; set; } = new();
    public string? RepresentativeCallTemplateJson { get; set; }
    public string? RepresentativeCallExpressionType { get; set; }
    public List<NodeReferenceDescriptor> RepresentativeCallReferences { get; set; } = new();
    public string? SourceAssetRelativePath { get; set; }
    public bool IsUbergraphFunction { get; set; }
}

public sealed class BlueprintAssetContext
{
    public string OriginalAssetJson { get; set; } = string.Empty;
    public string? SourceAssetRelativePath { get; set; }
    public Dictionary<string, BlueprintFunctionTemplate> FunctionTemplates { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, LoadedPropertyTemplate> ClassLoadedPropertyTemplatesByName { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> ReferencedClassFieldNames { get; set; } = new(StringComparer.Ordinal);
    public List<FieldOwnerEvidence> FieldOwnerEvidences { get; set; } = new();
    public string? ClassExportTemplateJson { get; set; }
    public string? ClassExportObjectName { get; set; }
    public int ClassExportIndex { get; set; }
    public List<BlueprintObjectSymbol> ExportSymbols { get; set; } = new();
    public List<BlueprintObjectSymbol> ImportSymbols { get; set; } = new();

    [JsonIgnore]
    public IReadOnlyDictionary<int, BlueprintObjectSymbol> SymbolsByIndex =>
        cachedSymbolsByIndex ??= BuildSymbolsByIndex();

    private Dictionary<int, BlueprintObjectSymbol>? cachedSymbolsByIndex;

    public void RefreshCaches()
    {
        cachedSymbolsByIndex = null;
    }

    private Dictionary<int, BlueprintObjectSymbol> BuildSymbolsByIndex()
    {
        Dictionary<int, BlueprintObjectSymbol> result = new();
        foreach (BlueprintObjectSymbol symbol in ExportSymbols)
        {
            result[symbol.Index] = symbol;
        }

        foreach (BlueprintObjectSymbol symbol in ImportSymbols)
        {
            result[symbol.Index] = symbol;
        }

        return result;
    }

    public static BlueprintAssetContext CreateFallback(string json)
    {
        return new BlueprintAssetContext
        {
            OriginalAssetJson = json ?? string.Empty
        };
    }

    public static BlueprintAssetContext Merge(BlueprintAssetContext target, BlueprintAssetContext source)
    {
        if (target == null) return CloneOrFallback(source);
        if (source == null) return CloneOrFallback(target);

        BlueprintAssetContext merged = BlueprintModelCloner.Clone(target);
        if (string.IsNullOrWhiteSpace(merged.OriginalAssetJson) && !string.IsNullOrWhiteSpace(source.OriginalAssetJson))
            merged.OriginalAssetJson = source.OriginalAssetJson;
        if (string.IsNullOrWhiteSpace(merged.SourceAssetRelativePath) && !string.IsNullOrWhiteSpace(source.SourceAssetRelativePath))
            merged.SourceAssetRelativePath = source.SourceAssetRelativePath;

        foreach ((string key, BlueprintFunctionTemplate value) in source.FunctionTemplates)
        {
            if (!merged.FunctionTemplates.TryGetValue(key, out BlueprintFunctionTemplate? existingTemplate))
            {
                merged.FunctionTemplates[key] = BlueprintModelCloner.Clone(value);
                continue;
            }

            if (string.IsNullOrWhiteSpace(existingTemplate.FunctionExportTemplateJson) &&
                !string.IsNullOrWhiteSpace(value.FunctionExportTemplateJson))
            {
                existingTemplate.FunctionExportTemplateJson = value.FunctionExportTemplateJson;
                existingTemplate.FunctionExportReferences = value.FunctionExportReferences
                    .Select(BlueprintModelCloner.Clone)
                    .ToList();
            }

            foreach ((string propertyName, LoadedPropertyTemplate propertyTemplate) in value.LoadedPropertyTemplatesByName)
            {
                if (!existingTemplate.LoadedPropertyTemplatesByName.ContainsKey(propertyName))
                    existingTemplate.LoadedPropertyTemplatesByName[propertyName] = BlueprintModelCloner.Clone(propertyTemplate);
            }

            if (existingTemplate.ParameterNames.Count == 0 && value.ParameterNames.Count > 0)
                existingTemplate.ParameterNames = new List<string>(value.ParameterNames);
            if (existingTemplate.ReturnValueNames.Count == 0 && value.ReturnValueNames.Count > 0)
                existingTemplate.ReturnValueNames = new List<string>(value.ReturnValueNames);

            if (string.IsNullOrWhiteSpace(existingTemplate.RepresentativeCallTemplateJson) &&
                !string.IsNullOrWhiteSpace(value.RepresentativeCallTemplateJson))
            {
                existingTemplate.RepresentativeCallTemplateJson = value.RepresentativeCallTemplateJson;
                existingTemplate.RepresentativeCallExpressionType = value.RepresentativeCallExpressionType;
                existingTemplate.RepresentativeCallReferences = value.RepresentativeCallReferences
                    .Select(BlueprintModelCloner.Clone)
                    .ToList();
            }

            if (string.IsNullOrWhiteSpace(existingTemplate.SourceAssetRelativePath) &&
                !string.IsNullOrWhiteSpace(value.SourceAssetRelativePath))
            {
                existingTemplate.SourceAssetRelativePath = value.SourceAssetRelativePath;
            }

            existingTemplate.IsUbergraphFunction |= value.IsUbergraphFunction;
        }

        foreach ((string key, LoadedPropertyTemplate value) in source.ClassLoadedPropertyTemplatesByName)
        {
            if (!merged.ClassLoadedPropertyTemplatesByName.ContainsKey(key))
                merged.ClassLoadedPropertyTemplatesByName[key] = BlueprintModelCloner.Clone(value);
        }

        foreach (string fieldName in source.ReferencedClassFieldNames)
        {
            merged.ReferencedClassFieldNames.Add(fieldName);
        }

        foreach (FieldOwnerEvidence evidence in source.FieldOwnerEvidences)
        {
            if (string.IsNullOrWhiteSpace(evidence.FieldName))
                continue;
            bool exists = merged.FieldOwnerEvidences.Any(existing =>
                string.Equals(existing.FieldName, evidence.FieldName, StringComparison.Ordinal) &&
                existing.OwnerIndex == evidence.OwnerIndex &&
                string.Equals(existing.OwnerSymbol?.ObjectName, evidence.OwnerSymbol?.ObjectName, StringComparison.Ordinal) &&
                string.Equals(existing.OwnerSymbol?.ClassName, evidence.OwnerSymbol?.ClassName, StringComparison.Ordinal) &&
                string.Equals(existing.SourceFunctionName, evidence.SourceFunctionName, StringComparison.Ordinal));
            if (!exists)
                merged.FieldOwnerEvidences.Add(BlueprintModelCloner.Clone(evidence));
        }

        if (string.IsNullOrWhiteSpace(merged.ClassExportTemplateJson) && !string.IsNullOrWhiteSpace(source.ClassExportTemplateJson))
            merged.ClassExportTemplateJson = source.ClassExportTemplateJson;
        if (string.IsNullOrWhiteSpace(merged.ClassExportObjectName) && !string.IsNullOrWhiteSpace(source.ClassExportObjectName))
            merged.ClassExportObjectName = source.ClassExportObjectName;
        if (merged.ClassExportIndex == 0 && source.ClassExportIndex != 0)
            merged.ClassExportIndex = source.ClassExportIndex;

        MergeSymbols(merged.ExportSymbols, source.ExportSymbols);
        MergeSymbols(merged.ImportSymbols, source.ImportSymbols);
        merged.RefreshCaches();
        return merged;
    }

    public static BlueprintAssetContext CloneOrFallback(BlueprintAssetContext? context)
    {
        return context == null ? CreateFallback(string.Empty) : BlueprintModelCloner.Clone(context);
    }

    private static void MergeSymbols(List<BlueprintObjectSymbol> target, IEnumerable<BlueprintObjectSymbol> source)
    {
        HashSet<int> existing = new(target.Select(symbol => symbol.Index));
        foreach (BlueprintObjectSymbol symbol in source)
        {
            if (!existing.Add(symbol.Index))
                continue;
            target.Add(new BlueprintObjectSymbol
            {
                Index = symbol.Index,
                OuterIndex = symbol.OuterIndex,
                ObjectName = symbol.ObjectName,
                ClassName = symbol.ClassName,
                ClassPackage = symbol.ClassPackage,
                PackageName = symbol.PackageName,
                OuterObjectName = symbol.OuterObjectName,
                IsExport = symbol.IsExport
            });
        }
    }
}

public static class BlueprintModelCloner
{
    public static NodeReferenceDescriptor Clone(NodeReferenceDescriptor descriptor)
    {
        return new NodeReferenceDescriptor
        {
            PointerPath = descriptor.PointerPath,
            ReferenceKind = descriptor.ReferenceKind,
            OriginalIndex = descriptor.OriginalIndex,
            SourceAssetRelativePath = descriptor.SourceAssetRelativePath,
            ObjectName = descriptor.ObjectName,
            ClassName = descriptor.ClassName,
            ClassPackage = descriptor.ClassPackage,
            PackageName = descriptor.PackageName,
            OuterObjectName = descriptor.OuterObjectName,
            SourceFunctionName = descriptor.SourceFunctionName
        };
    }

    public static FieldOwnerEvidence Clone(FieldOwnerEvidence evidence)
    {
        return new FieldOwnerEvidence
        {
            FieldName = evidence.FieldName,
            OwnerIndex = evidence.OwnerIndex,
            OwnerSymbol = evidence.OwnerSymbol == null
                ? null
                : new BlueprintObjectSymbol
                {
                    Index = evidence.OwnerSymbol.Index,
                    OuterIndex = evidence.OwnerSymbol.OuterIndex,
                    ObjectName = evidence.OwnerSymbol.ObjectName,
                    ClassName = evidence.OwnerSymbol.ClassName,
                    ClassPackage = evidence.OwnerSymbol.ClassPackage,
                    PackageName = evidence.OwnerSymbol.PackageName,
                    OuterObjectName = evidence.OwnerSymbol.OuterObjectName,
                    IsExport = evidence.OwnerSymbol.IsExport
                },
            ExpressionType = evidence.ExpressionType,
            PointerPath = evidence.PointerPath,
            SourceFunctionName = evidence.SourceFunctionName
        };
    }

    public static PinExportSchema Clone(PinExportSchema schema)
    {
        return new PinExportSchema
        {
            PinName = schema.PinName,
            SemanticKind = schema.SemanticKind,
            ExpressionPath = schema.ExpressionPath,
            PropertyName = schema.PropertyName,
            PropertyOwnerKind = schema.PropertyOwnerKind,
            PropertyTemplateJson = schema.PropertyTemplateJson,
            ContainerType = schema.ContainerType,
            ValueTypeName = schema.ValueTypeName,
            IsReturnValue = schema.IsReturnValue,
            IsParameter = schema.IsParameter,
            IsTargetObject = schema.IsTargetObject
        };
    }

    public static LoadedPropertyTemplate Clone(LoadedPropertyTemplate template)
    {
        return new LoadedPropertyTemplate
        {
            PropertyName = template.PropertyName,
            PropertyTemplateJson = template.PropertyTemplateJson,
            OwnerObjectName = template.OwnerObjectName,
            OwnerKind = template.OwnerKind,
            SourceFunctionName = template.SourceFunctionName,
            OriginalOrdinal = template.OriginalOrdinal,
            CreatedSequence = template.CreatedSequence,
            FirstUseOrdinal = template.FirstUseOrdinal,
            IsUserCreated = template.IsUserCreated,
            ReferenceDescriptors = template.ReferenceDescriptors.Select(Clone).ToList()
        };
    }

    public static DataPropertyTemplate Clone(DataPropertyTemplate template)
    {
        return new DataPropertyTemplate
        {
            Name = template.Name,
            PropertyType = template.PropertyType,
            TemplateJson = template.TemplateJson,
            EnumType = template.EnumType,
            EnumOptions = new List<string>(template.EnumOptions),
            OccurrenceCount = template.OccurrenceCount,
            SourceAssetRelativePath = template.SourceAssetRelativePath
        };
    }

    public static BlueprintFunctionTemplate Clone(BlueprintFunctionTemplate template)
    {
        return new BlueprintFunctionTemplate
        {
            FunctionName = template.FunctionName,
            FunctionExportTemplateJson = template.FunctionExportTemplateJson,
            FunctionExportReferences = template.FunctionExportReferences.Select(Clone).ToList(),
            LoadedPropertyTemplatesByName = template.LoadedPropertyTemplatesByName.ToDictionary(
                kv => kv.Key,
                kv => Clone(kv.Value),
                StringComparer.Ordinal),
            ParameterNames = new List<string>(template.ParameterNames),
            ReturnValueNames = new List<string>(template.ReturnValueNames),
            RepresentativeCallTemplateJson = template.RepresentativeCallTemplateJson,
            RepresentativeCallExpressionType = template.RepresentativeCallExpressionType,
            RepresentativeCallReferences = template.RepresentativeCallReferences.Select(Clone).ToList(),
            SourceAssetRelativePath = template.SourceAssetRelativePath,
            IsUbergraphFunction = template.IsUbergraphFunction
        };
    }

    public static BlueprintAssetContext Clone(BlueprintAssetContext context)
    {
        return new BlueprintAssetContext
        {
            OriginalAssetJson = context.OriginalAssetJson,
            SourceAssetRelativePath = context.SourceAssetRelativePath,
            FunctionTemplates = context.FunctionTemplates.ToDictionary(
                kv => kv.Key,
                kv => Clone(kv.Value),
                StringComparer.Ordinal),
            ClassLoadedPropertyTemplatesByName = context.ClassLoadedPropertyTemplatesByName.ToDictionary(
                kv => kv.Key,
                kv => Clone(kv.Value),
                StringComparer.Ordinal),
            ReferencedClassFieldNames = new HashSet<string>(context.ReferencedClassFieldNames, StringComparer.Ordinal),
            FieldOwnerEvidences = context.FieldOwnerEvidences.Select(Clone).ToList(),
            ClassExportTemplateJson = context.ClassExportTemplateJson,
            ClassExportObjectName = context.ClassExportObjectName,
            ClassExportIndex = context.ClassExportIndex,
            ExportSymbols = context.ExportSymbols.Select(symbol => new BlueprintObjectSymbol
            {
                Index = symbol.Index,
                OuterIndex = symbol.OuterIndex,
                ObjectName = symbol.ObjectName,
                ClassName = symbol.ClassName,
                ClassPackage = symbol.ClassPackage,
                PackageName = symbol.PackageName,
                OuterObjectName = symbol.OuterObjectName,
                IsExport = symbol.IsExport
            }).ToList(),
            ImportSymbols = context.ImportSymbols.Select(symbol => new BlueprintObjectSymbol
            {
                Index = symbol.Index,
                OuterIndex = symbol.OuterIndex,
                ObjectName = symbol.ObjectName,
                ClassName = symbol.ClassName,
                ClassPackage = symbol.ClassPackage,
                PackageName = symbol.PackageName,
                OuterObjectName = symbol.OuterObjectName,
                IsExport = symbol.IsExport
            }).ToList()
        };
    }
}
