using Newtonsoft.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.FieldTypes;
using UAssetAPI.Kismet;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.UnrealTypes;

namespace BPEditor
{
    public partial class Form1
    {
        private const string JsonObjectIndexPointerSeparator = "/";

        private sealed class VariableLoadedPropertyResolution
        {
            public LoadedPropertyTemplate Template { get; init; } = new();
            public string OwnerKind { get; init; } = string.Empty;
        }

        private sealed class FieldOwnerResolutionContext
        {
            public string FunctionName { get; init; } = string.Empty;
            public int FunctionOwnerIndex { get; init; }
            public int ClassOwnerIndex { get; init; }
            public int SuperClassOwnerIndex { get; init; }
            public HashSet<string> FunctionFieldNames { get; init; } = new(StringComparer.Ordinal);
            public HashSet<string> CurrentClassFieldNames { get; init; } = new(StringComparer.Ordinal);
            public HashSet<string> ReferencedClassFieldNames { get; init; } = new(StringComparer.Ordinal);
            public Dictionary<string, HashSet<int>> FieldOwnerEvidenceOwners { get; init; } = new(StringComparer.Ordinal);
            public Dictionary<string, int> AlternateFunctionFieldOwners { get; init; } = new(StringComparer.Ordinal);
            public Dictionary<string, int> ObservedInstanceFieldOwners { get; init; } = new(StringComparer.Ordinal);
            public Dictionary<int, BlueprintObjectSymbol> SymbolsByIndex { get; init; } = new();
        }

        private BlueprintAssetContext BuildBlueprintAssetContext(UAsset uAsset, string json, JsonObject? root)
        {
            BlueprintAssetContext context = BlueprintAssetContext.CreateFallback(json);
            context.SourceAssetRelativePath = ResolveSourceAssetRelativePath(uAsset);
            if (root == null)
                return context;

            Dictionary<int, BlueprintObjectSymbol> symbolMap = BuildBlueprintObjectSymbols(root);
            context.ExportSymbols = symbolMap.Values
                .Where(symbol => symbol.IsExport)
                .OrderBy(symbol => symbol.Index)
                .Select(CloneBlueprintObjectSymbol)
                .ToList();
            context.ImportSymbols = symbolMap.Values
                .Where(symbol => !symbol.IsExport)
                .OrderBy(symbol => symbol.Index)
                .Select(CloneBlueprintObjectSymbol)
                .ToList();

            JsonArray? exports = root["Exports"] as JsonArray;
            if (exports == null)
            {
                context.RefreshCaches();
                return context;
            }

            for (int i = 0; i < exports.Count; i++)
            {
                if (exports[i] is not JsonObject exportObj)
                    continue;

                string exportType = exportObj["$type"]?.GetValue<string>() ?? string.Empty;
                string objectName = exportObj["ObjectName"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(objectName))
                    continue;

                if (exportType.Contains("ClassExport", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(context.ClassExportTemplateJson))
                {
                    context.ClassExportTemplateJson = exportObj.ToJsonString();
                    context.ClassExportObjectName = objectName;
                    context.ClassExportIndex = i + 1;
                    if (exportObj["LoadedProperties"] is JsonArray classLoadedProperties)
                    {
                        foreach (JsonObject propertyObj in classLoadedProperties.OfType<JsonObject>())
                        {
                            string propertyName = propertyObj["Name"]?.GetValue<string>() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(propertyName))
                                continue;

                    context.ClassLoadedPropertyTemplatesByName[propertyName] = new LoadedPropertyTemplate
                    {
                        PropertyName = propertyName,
                        PropertyTemplateJson = propertyObj.ToJsonString(),
                        OwnerObjectName = objectName,
                        OwnerKind = "Class",
                        OriginalOrdinal = context.ClassLoadedPropertyTemplatesByName.Count,
                        ReferenceDescriptors = CollectReferenceDescriptors(propertyObj, symbolMap, objectName, context.SourceAssetRelativePath)
                    };
                }
            }
                    continue;
                }

                if (!exportType.Contains("FunctionExport", StringComparison.Ordinal))
                    continue;

                BlueprintFunctionTemplate template = BuildBlueprintFunctionTemplate(exportObj, objectName, symbolMap, context.SourceAssetRelativePath);
                context.FunctionTemplates[objectName] = template;

                if (exportObj["ScriptBytecode"] is JsonArray scriptBytecode)
                {
                    foreach (string fieldName in CollectReferencedClassFieldNames(scriptBytecode))
                    {
                        context.ReferencedClassFieldNames.Add(fieldName);
                    }

                    context.FieldOwnerEvidences.AddRange(CollectFieldOwnerEvidences(scriptBytecode, symbolMap, objectName));
                }
            }

            context.RefreshCaches();
            MergeFunctionTemplateCache(context.FunctionTemplates);
            return context;
        }

        private string? ResolveSourceAssetRelativePath(UAsset uAsset)
        {
            if (string.IsNullOrWhiteSpace(WorkDir) || string.IsNullOrWhiteSpace(uAsset.FilePath))
                return null;

            try
            {
                string sourceRoot = Path.GetFullPath(Path.Combine(WorkDir, "Source"));
                string assetPath = Path.GetFullPath(uAsset.FilePath);
                if (!assetPath.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
                    return null;

                string relative = Path.GetRelativePath(sourceRoot, assetPath);
                return NormalizePath(relative);
            }
            catch
            {
                return null;
            }
        }

        private void MergeFunctionTemplateCache(IEnumerable<KeyValuePair<string, BlueprintFunctionTemplate>> templates)
        {
            foreach ((string key, BlueprintFunctionTemplate value) in templates)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!functionTemplatesByName.TryGetValue(key, out BlueprintFunctionTemplate? existing))
                {
                    functionTemplatesByName[key] = BlueprintModelCloner.Clone(value);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.FunctionExportTemplateJson) &&
                    !string.IsNullOrWhiteSpace(value.FunctionExportTemplateJson))
                {
                    existing.FunctionExportTemplateJson = value.FunctionExportTemplateJson;
                    existing.FunctionExportReferences = value.FunctionExportReferences
                        .Select(BlueprintModelCloner.Clone)
                        .ToList();
                }

                if (existing.LoadedPropertyTemplatesByName.Count == 0 && value.LoadedPropertyTemplatesByName.Count > 0)
                {
                    existing.LoadedPropertyTemplatesByName = value.LoadedPropertyTemplatesByName.ToDictionary(
                        kv => kv.Key,
                        kv => BlueprintModelCloner.Clone(kv.Value),
                        StringComparer.Ordinal);
                }

                if (existing.ParameterNames.Count == 0 && value.ParameterNames.Count > 0)
                    existing.ParameterNames = new List<string>(value.ParameterNames);
                if (existing.ReturnValueNames.Count == 0 && value.ReturnValueNames.Count > 0)
                    existing.ReturnValueNames = new List<string>(value.ReturnValueNames);

                if (string.IsNullOrWhiteSpace(existing.RepresentativeCallTemplateJson) &&
                    !string.IsNullOrWhiteSpace(value.RepresentativeCallTemplateJson))
                {
                    existing.RepresentativeCallTemplateJson = value.RepresentativeCallTemplateJson;
                    existing.RepresentativeCallExpressionType = value.RepresentativeCallExpressionType;
                    existing.RepresentativeCallReferences = value.RepresentativeCallReferences
                        .Select(BlueprintModelCloner.Clone)
                        .ToList();
                }

                if (string.IsNullOrWhiteSpace(existing.SourceAssetRelativePath) &&
                    !string.IsNullOrWhiteSpace(value.SourceAssetRelativePath))
                {
                    existing.SourceAssetRelativePath = value.SourceAssetRelativePath;
                }

                existing.IsUbergraphFunction |= value.IsUbergraphFunction;
            }
        }

        private bool TryEnsureFunctionTemplateAvailable(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return false;

            if (functionTemplatesByName.TryGetValue(functionName, out BlueprintFunctionTemplate? existingTemplate) &&
                !string.IsNullOrWhiteSpace(existingTemplate.FunctionExportTemplateJson))
            {
                return true;
            }

            if (!functionDefinitionAssetPathByName.TryGetValue(functionName, out string? relativeAssetPath) ||
                string.IsNullOrWhiteSpace(relativeAssetPath))
            {
                return false;
            }

            if (!TryLoadSourceAssetContext(relativeAssetPath, out BlueprintAssetContext? sourceContext) ||
                sourceContext == null)
            {
                return false;
            }

            MergeFunctionTemplateCache(sourceContext.FunctionTemplates);
            return functionTemplatesByName.TryGetValue(functionName, out BlueprintFunctionTemplate? loadedTemplate) &&
                !string.IsNullOrWhiteSpace(loadedTemplate.FunctionExportTemplateJson);
        }

        private bool TryLoadSourceAssetContext(string relativeAssetPath, out BlueprintAssetContext? context)
        {
            context = null;
            if (string.IsNullOrWhiteSpace(relativeAssetPath))
                return false;

            string normalizedRelativePath = NormalizePath(relativeAssetPath);
            if (sourceAssetContextByRelativePath.TryGetValue(normalizedRelativePath, out BlueprintAssetContext? cachedContext))
            {
                context = BlueprintModelCloner.Clone(cachedContext);
                return true;
            }

            string? assetPath = ResolveSourceAssetFilePath(normalizedRelativePath);
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
                return false;

            UAssetAPI.Unversioned.Usmap? mappings = null;
            if (!string.IsNullOrWhiteSpace(UsmapPath) && File.Exists(UsmapPath))
                mappings = new UAssetAPI.Unversioned.Usmap(UsmapPath);

            UAsset sourceAsset = new(assetPath, engineVersion, mappings);
            string json = sourceAsset.SerializeJson();
            JsonObject? root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
                return false;

            BlueprintAssetContext loadedContext = BuildBlueprintAssetContext(sourceAsset, json, root);
            loadedContext.SourceAssetRelativePath = normalizedRelativePath;
            foreach ((string functionName, BlueprintFunctionTemplate template) in loadedContext.FunctionTemplates)
            {
                template.SourceAssetRelativePath = normalizedRelativePath;
                functionDefinitionAssetPathByName[functionName] = normalizedRelativePath;
            }

            sourceAssetContextByRelativePath[normalizedRelativePath] = BlueprintModelCloner.Clone(loadedContext);
            context = BlueprintModelCloner.Clone(loadedContext);
            return true;
        }

        private string? ResolveSourceAssetFilePath(string normalizedRelativePath)
        {
            if (string.IsNullOrWhiteSpace(WorkDir))
                return null;

            string assetPath = Path.Combine(
                WorkDir,
                "Source",
                normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(assetPath))
                return assetPath;

            if (currentPakReader != null &&
                currentPakFileStream != null &&
                normalizedRelativePath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(assetPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    byte[] assetBytes = currentPakReader.Get(currentPakFileStream, normalizedRelativePath);
                    File.WriteAllBytes(assetPath, assetBytes);

                    string companionPath = normalizedRelativePath[..^5] + "exp";
                    try
                    {
                        byte[] companionBytes = currentPakReader.Get(currentPakFileStream, companionPath);
                        File.WriteAllBytes(assetPath[..^5] + "exp", companionBytes);
                    }
                    catch
                    {
                        // Some assets do not have a sidecar companion; ignore.
                    }

                    if (File.Exists(assetPath))
                        return assetPath;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private void ApplyFunctionTemplateMetadataToDefinition(string definitionName, string functionName)
        {
            if (!nodeDefinitions.TryGetValue(definitionName, out NodeDefinition? definition))
                return;
            if (!functionTemplatesByName.TryGetValue(functionName, out BlueprintFunctionTemplate? functionTemplate))
                return;

            PruneCallTargetDescriptorsForFunction(definition.ReferenceDescriptors, functionName);
            PruneCallTargetDescriptorsForFunction(definition.RepresentativeCallReferenceDescriptors, functionName);

            bool hasMatchingRepresentativeCall = TryGetMatchingRepresentativeCallTemplate(
                functionTemplate,
                functionName,
                out string? representativeTemplateJson,
                out string? representativeExpressionType,
                out IReadOnlyList<NodeReferenceDescriptor> representativeReferences);

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

            if (hasMatchingRepresentativeCall &&
                !string.IsNullOrWhiteSpace(representativeTemplateJson))
            {
                definition.SourceExpressionTemplateJson = representativeTemplateJson;
            }

            if (hasMatchingRepresentativeCall &&
                !string.IsNullOrWhiteSpace(representativeExpressionType))
            {
                definition.SourceExpressionType = representativeExpressionType;
            }

            if (hasMatchingRepresentativeCall &&
                !string.IsNullOrWhiteSpace(representativeTemplateJson))
            {
                definition.RepresentativeCallTemplateJson = representativeTemplateJson;
            }

            if (hasMatchingRepresentativeCall &&
                !string.IsNullOrWhiteSpace(representativeExpressionType))
            {
                definition.RepresentativeCallExpressionType = representativeExpressionType;
            }

            if (hasMatchingRepresentativeCall)
            {
                MergeReferenceDescriptorLists(definition.ReferenceDescriptors, representativeReferences);
                MergeReferenceDescriptorLists(definition.RepresentativeCallReferenceDescriptors, representativeReferences);
            }

            MergePropertyTemplateDictionary(definition.PropertyTemplateJsonByName, functionTemplate.LoadedPropertyTemplatesByName);
        }

        private bool TryGetMatchingRepresentativeCallTemplate(
            BlueprintFunctionTemplate functionTemplate,
            string functionName,
            out string? representativeTemplateJson,
            out string? representativeExpressionType,
            out IReadOnlyList<NodeReferenceDescriptor> representativeReferences)
        {
            representativeTemplateJson = null;
            representativeExpressionType = null;
            representativeReferences = Array.Empty<NodeReferenceDescriptor>();

            if (string.IsNullOrWhiteSpace(functionTemplate.RepresentativeCallTemplateJson))
                return false;

            JsonObject? representativeCallExpression;
            try
            {
                representativeCallExpression = JsonNode.Parse(functionTemplate.RepresentativeCallTemplateJson) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }

            if (representativeCallExpression == null ||
                !TryResolveExpressionCallFunctionName(representativeCallExpression, out string representativeFunctionName) ||
                !string.Equals(representativeFunctionName, functionName, StringComparison.Ordinal))
            {
                return false;
            }

            representativeTemplateJson = functionTemplate.RepresentativeCallTemplateJson;
            representativeExpressionType = string.IsNullOrWhiteSpace(functionTemplate.RepresentativeCallExpressionType)
                ? SimplifyExpressionType(representativeCallExpression["$type"]?.GetValue<string>() ?? string.Empty)
                : functionTemplate.RepresentativeCallExpressionType;
            representativeReferences = functionTemplate.RepresentativeCallReferences;
            return true;
        }

        private bool DefinitionCallTemplateTargetsDifferentFunction(string? templateJson, string functionName)
        {
            if (string.IsNullOrWhiteSpace(templateJson))
                return false;

            JsonObject? templateExpression;
            try
            {
                templateExpression = JsonNode.Parse(templateJson) as JsonObject;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }

            if (templateExpression == null ||
                !TryResolveExpressionCallFunctionName(templateExpression, out string templateFunctionName) ||
                string.IsNullOrWhiteSpace(templateFunctionName))
            {
                return false;
            }

            return !string.Equals(templateFunctionName, functionName, StringComparison.Ordinal);
        }

        private bool TryGetFunctionTemplate(BlueprintAssetContext assetContext, string functionName, out BlueprintFunctionTemplate? functionTemplate)
        {
            functionTemplate = null;
            if (string.IsNullOrWhiteSpace(functionName))
                return false;

            if (assetContext.FunctionTemplates.TryGetValue(functionName, out functionTemplate))
                return true;

            _ = TryEnsureFunctionTemplateAvailable(functionName);
            if (!functionTemplatesByName.TryGetValue(functionName, out BlueprintFunctionTemplate? cachedTemplate))
                return false;

            BlueprintFunctionTemplate clonedTemplate = BlueprintModelCloner.Clone(cachedTemplate);
            assetContext.FunctionTemplates[functionName] = clonedTemplate;
            functionTemplate = clonedTemplate;
            return true;
        }

        private void ApplyExternalCallTemplateMetadataToDefinition(string definitionName, ExternalCallSignature signature)
        {
            if (!nodeDefinitions.TryGetValue(definitionName, out NodeDefinition? definition))
                return;

            PruneCallTargetDescriptorsForFunction(definition.ReferenceDescriptors, signature.Name);
            PruneCallTargetDescriptorsForFunction(definition.RepresentativeCallReferenceDescriptors, signature.Name);

            if (DefinitionCallTemplateTargetsDifferentFunction(definition.SourceExpressionTemplateJson, signature.Name))
            {
                definition.SourceExpressionTemplateJson = null;
                definition.SourceExpressionType = null;
            }

            if (DefinitionCallTemplateTargetsDifferentFunction(definition.RepresentativeCallTemplateJson, signature.Name))
            {
                definition.RepresentativeCallTemplateJson = null;
                definition.RepresentativeCallExpressionType = null;
                definition.RepresentativeCallReferenceDescriptors.Clear();
            }

            if (!string.IsNullOrWhiteSpace(signature.RepresentativeTemplateJson))
            {
                definition.SourceExpressionTemplateJson = signature.RepresentativeTemplateJson;
            }

            if (!string.IsNullOrWhiteSpace(signature.RepresentativeExpressionType))
            {
                definition.SourceExpressionType = signature.RepresentativeExpressionType;
            }

            if (!string.IsNullOrWhiteSpace(signature.RepresentativeTemplateJson))
            {
                definition.RepresentativeCallTemplateJson = signature.RepresentativeTemplateJson;
            }

            if (!string.IsNullOrWhiteSpace(signature.RepresentativeExpressionType))
            {
                definition.RepresentativeCallExpressionType = signature.RepresentativeExpressionType;
            }

            MergeReferenceDescriptorLists(definition.ReferenceDescriptors, signature.ReferenceDescriptors);
            MergeReferenceDescriptorLists(definition.RepresentativeCallReferenceDescriptors, signature.ReferenceDescriptors);
        }

        private static void PruneCallTargetDescriptorsForFunction(List<NodeReferenceDescriptor> descriptors, string functionName)
        {
            if (descriptors == null || descriptors.Count == 0)
                return;

            descriptors.RemoveAll(descriptor =>
                descriptor != null &&
                IsStackNodeDescriptor(descriptor) &&
                !string.Equals(descriptor.ObjectName, functionName, StringComparison.Ordinal));
        }

        private void PopulateNodeExportMetadata(
            SerializableNode node,
            string definitionName,
            string functionName,
            JsonObject sourceExpression)
        {
            if (currentImportAssetContext == null)
                return;

            List<NodeReferenceDescriptor> expressionDescriptors = CollectReferenceDescriptors(
                sourceExpression,
                currentImportAssetContext.SymbolsByIndex,
                functionName,
                currentImportAssetContext.SourceAssetRelativePath);
            node.ReferenceDescriptors = expressionDescriptors.Select(BlueprintModelCloner.Clone).ToList();

            Dictionary<string, string> propertyTemplatesByName = [];
            foreach ((string propertyName, string propertyJson) in ResolvePropertyTemplatesForExpression(functionName, sourceExpression))
            {
                propertyTemplatesByName[propertyName] = propertyJson;
            }
            node.PropertyTemplateJsonByName = propertyTemplatesByName;
            node.PinSchemas = BuildPinSchemasForExpression(functionName, sourceExpression);

            if (nodeDefinitions.TryGetValue(definitionName, out NodeDefinition? definition))
            {
                string baseDefinitionName = GetBaseDefinitionName(definitionName);
                if (ShouldPersistInstanceMetadataToDefinition(baseDefinitionName))
                {
                    MergeReferenceDescriptorLists(definition.ReferenceDescriptors, expressionDescriptors);
                    foreach ((string propertyName, string propertyJson) in propertyTemplatesByName)
                    {
                        definition.PropertyTemplateJsonByName.TryAdd(propertyName, propertyJson);
                    }
                    MergePinSchemaLists(definition.PinSchemas, node.PinSchemas);
                }

                if (baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal))
                {
                    string callFunctionName = ExtractCallFunctionName(baseDefinitionName);
                    ApplyFunctionTemplateMetadataToDefinition(definitionName, callFunctionName);
                }
            }
        }

        private static bool ShouldPersistInstanceMetadataToDefinition(string baseDefinitionName)
        {
            if (string.IsNullOrWhiteSpace(baseDefinitionName))
                return false;

            if (string.Equals(baseDefinitionName, "Get Variable", StringComparison.Ordinal) ||
                string.Equals(baseDefinitionName, "Set Variable", StringComparison.Ordinal))
            {
                return false;
            }

            if (baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal) ||
                baseDefinitionName.StartsWith("Event ", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private List<PinExportSchema> BuildPinSchemasForExpression(string functionName, JsonObject sourceExpression)
        {
            List<PinExportSchema> schemas = [];
            string expressionType = SimplifyExpressionType(sourceExpression["$type"]?.GetValue<string>() ?? string.Empty);

            if (expressionType is "FinalFunction" or "LocalFinalFunction" or "CallMath" or "LocalVirtualFunction")
            {
                string schemaFunctionName = functionName;
                if (TryResolveCallFunctionName(sourceExpression, expressionType, out string resolvedCallFunctionName) &&
                    !string.IsNullOrWhiteSpace(resolvedCallFunctionName))
                {
                    schemaFunctionName = resolvedCallFunctionName;
                }

                if (sourceExpression["ObjectExpression"] != null)
                {
                    schemas.Add(new PinExportSchema
                    {
                        PinName = "Target",
                        SemanticKind = "TargetObject",
                        ExpressionPath = "/ObjectExpression",
                        IsTargetObject = true
                    });
                }

                if (sourceExpression["Parameters"] is JsonArray parameters)
                {
                    int argIndex = 0;
                    List<string> parameterNames = ResolveCallParameterNames(schemaFunctionName);
                    foreach (JsonObject parameter in parameters.OfType<JsonObject>())
                    {
                        if (IsTerminalExpression(parameter))
                            continue;
                        string parameterName = argIndex < parameterNames.Count && !string.IsNullOrWhiteSpace(parameterNames[argIndex])
                            ? parameterNames[argIndex]
                            : $"Arg{argIndex + 1}";
                        schemas.Add(new PinExportSchema
                        {
                            PinName = parameterName,
                            SemanticKind = "Parameter",
                            ExpressionPath = $"/Parameters/{argIndex}",
                            PropertyName = parameterName,
                            IsParameter = true
                        });
                        argIndex++;
                    }
                }

                schemas.Add(new PinExportSchema
                {
                    PinName = "Result",
                    SemanticKind = "ReturnValue"
                });

                return schemas;
            }

            if (expressionType is "LocalVariable" or "LocalOutVariable" or "InstanceVariable" or "DefaultVariable")
            {
                string propertyName = ResolveFieldPathName(sourceExpression["Variable"]);
                schemas.Add(new PinExportSchema
                {
                    PinName = "Value",
                    SemanticKind = "Variable",
                    PropertyName = propertyName
                });
                return schemas;
            }

            if (expressionType is "Let" or "LetBool" or "LetObj" or "LetWeakObjPtr" or "LetDelegate" or "LetMulticastDelegate" or "LetValueOnPersistentFrame")
            {
                string propertyName = ResolveAssignmentTargetName(sourceExpression);
                schemas.Add(new PinExportSchema
                {
                    PinName = "Value",
                    SemanticKind = "Variable",
                    PropertyName = propertyName
                });
                return schemas;
            }

            if (expressionType == "Return" && functionTemplatesByName.TryGetValue(functionName, out BlueprintFunctionTemplate? returnTemplate))
            {
                foreach (string returnName in returnTemplate.ReturnValueNames)
                {
                    schemas.Add(new PinExportSchema
                    {
                        PinName = "Return",
                        SemanticKind = "ReturnValue",
                        PropertyName = returnName,
                        IsReturnValue = true
                    });
                }
            }

            return schemas;
        }

        private List<string> ResolveCallParameterNames(string functionName)
        {
            if (string.IsNullOrWhiteSpace(functionName))
                return [];

            if (knownFunctionParametersByName.TryGetValue(functionName, out List<string>? knownParameterNames) &&
                knownParameterNames != null &&
                knownParameterNames.Count > 0)
            {
                return knownParameterNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
            }

            if (functionTemplatesByName.TryGetValue(functionName, out BlueprintFunctionTemplate? functionTemplate) &&
                functionTemplate.ParameterNames.Count > 0)
            {
                return functionTemplate.ParameterNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
            }

            return [];
        }

        private IEnumerable<KeyValuePair<string, string>> ResolvePropertyTemplatesForExpression(
            string functionName,
            JsonObject expressionObj)
        {
            _ = TryEnsureFunctionTemplateAvailable(functionName);
            if (!functionTemplatesByName.TryGetValue(functionName, out BlueprintFunctionTemplate? functionTemplate))
                yield break;

            string expressionType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            string? propertyName = expressionType switch
            {
                "LocalVariable" or "LocalOutVariable" or "InstanceVariable" or "DefaultVariable"
                    => ResolveFieldPathName(expressionObj["Variable"]),
                "Let" or "LetBool" or "LetObj" or "LetWeakObjPtr" or "LetDelegate" or "LetMulticastDelegate" or "LetValueOnPersistentFrame"
                    => ResolveAssignmentTargetName(expressionObj),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(propertyName) &&
                functionTemplate.LoadedPropertyTemplatesByName.TryGetValue(propertyName, out LoadedPropertyTemplate? propertyTemplate))
            {
                yield return new KeyValuePair<string, string>(propertyName, propertyTemplate.PropertyTemplateJson);
            }
            else if (!string.IsNullOrWhiteSpace(propertyName) &&
                currentImportAssetContext != null &&
                currentImportAssetContext.ClassLoadedPropertyTemplatesByName.TryGetValue(propertyName, out LoadedPropertyTemplate? classTemplate))
            {
                yield return new KeyValuePair<string, string>(propertyName, classTemplate.PropertyTemplateJson);
            }
            if (expressionType == "Return")
            {
                foreach (string returnName in functionTemplate.ReturnValueNames)
                {
                    if (!functionTemplate.LoadedPropertyTemplatesByName.TryGetValue(returnName, out LoadedPropertyTemplate? returnTemplate))
                        continue;
                    yield return new KeyValuePair<string, string>(returnName, returnTemplate.PropertyTemplateJson);
                }
            }
        }

        private static void MergePropertyTemplateDictionary(
            IDictionary<string, string> target,
            IReadOnlyDictionary<string, LoadedPropertyTemplate> source)
        {
            foreach ((string propertyName, LoadedPropertyTemplate propertyTemplate) in source)
            {
                if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(propertyTemplate.PropertyTemplateJson))
                    continue;
                target.TryAdd(propertyName, propertyTemplate.PropertyTemplateJson);
            }
        }

        private static void MergeReferenceDescriptorLists(
            ICollection<NodeReferenceDescriptor> target,
            IEnumerable<NodeReferenceDescriptor> source)
        {
            HashSet<string> seen = new(target.Select(descriptor =>
                $"{descriptor.PointerPath}|{descriptor.ReferenceKind}|{descriptor.ObjectName}|{descriptor.OriginalIndex}"),
                StringComparer.Ordinal);

            foreach (NodeReferenceDescriptor descriptor in source)
            {
                string key = $"{descriptor.PointerPath}|{descriptor.ReferenceKind}|{descriptor.ObjectName}|{descriptor.OriginalIndex}";
                if (!seen.Add(key))
                    continue;
                target.Add(BlueprintModelCloner.Clone(descriptor));
            }
        }

        private static void MergePinSchemaLists(
            ICollection<PinExportSchema> target,
            IEnumerable<PinExportSchema> source)
        {
            HashSet<string> seen = new(target.Select(schema =>
                $"{schema.PinName}|{schema.SemanticKind}|{schema.PropertyName}|{schema.ExpressionPath}"),
                StringComparer.Ordinal);

            foreach (PinExportSchema schema in source)
            {
                string key = $"{schema.PinName}|{schema.SemanticKind}|{schema.PropertyName}|{schema.ExpressionPath}";
                if (!seen.Add(key))
                    continue;
                target.Add(BlueprintModelCloner.Clone(schema));
            }
        }

        private BlueprintFunctionTemplate BuildBlueprintFunctionTemplate(
            JsonObject exportObj,
            string functionName,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceAssetRelativePath)
        {
            BlueprintFunctionTemplate template = new()
            {
                FunctionName = functionName,
                FunctionExportTemplateJson = exportObj.ToJsonString(),
                FunctionExportReferences = CollectReferenceDescriptors(exportObj, symbolMap, functionName, sourceAssetRelativePath),
                SourceAssetRelativePath = sourceAssetRelativePath,
                IsUbergraphFunction = HasUbergraphFunctionFlag(exportObj["FunctionFlags"]?.GetValue<string>()) ||
                    IsLikelyUbergraphFunctionName(functionName)
            };

            (List<string> parameterNames, List<string> returnValueNames) = GetFunctionSignatureParts(exportObj);
            template.ParameterNames = parameterNames;
            template.ReturnValueNames = returnValueNames;

            if (exportObj["LoadedProperties"] is JsonArray loadedProperties)
            {
                int originalOrdinal = 0;
                foreach (JsonObject propertyObj in loadedProperties.OfType<JsonObject>())
                {
                    string propertyName = propertyObj["Name"]?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(propertyName))
                    {
                        originalOrdinal++;
                        continue;
                    }

                    template.LoadedPropertyTemplatesByName[propertyName] = new LoadedPropertyTemplate
                    {
                        PropertyName = propertyName,
                        PropertyTemplateJson = propertyObj.ToJsonString(),
                        OwnerObjectName = functionName,
                        OwnerKind = "Function",
                        SourceFunctionName = functionName,
                        OriginalOrdinal = originalOrdinal,
                        ReferenceDescriptors = CollectReferenceDescriptors(propertyObj, symbolMap, functionName, sourceAssetRelativePath)
                    };
                    originalOrdinal++;
                }
            }

            if (TryFindRepresentativeCallExpression(exportObj["ScriptBytecode"] as JsonArray, out JsonObject? callExpression, out string callExpressionType))
            {
                template.RepresentativeCallTemplateJson = callExpression.ToJsonString();
                template.RepresentativeCallExpressionType = callExpressionType;
                template.RepresentativeCallReferences = CollectReferenceDescriptors(callExpression, symbolMap, functionName, sourceAssetRelativePath);
            }

            return template;
        }

        private static HashSet<string> CollectReferencedClassFieldNames(JsonArray scriptBytecode)
        {
            HashSet<string> fieldNames = new(StringComparer.Ordinal);
            foreach (JsonObject expressionObj in scriptBytecode.OfType<JsonObject>())
            {
                CollectReferencedClassFieldNamesRecursive(expressionObj, string.Empty, fieldNames);
            }

            return fieldNames;
        }

        private static void CollectReferencedClassFieldNamesRecursive(
            JsonNode? node,
            string currentExpressionType,
            ISet<string> fieldNames)
        {
            if (node is JsonObject obj)
            {
                string resolvedExpressionType = currentExpressionType;
                if (obj["$type"] is JsonValue typeNode &&
                    typeNode.TryGetValue<string>(out string? rawType) &&
                    !string.IsNullOrWhiteSpace(rawType))
                {
                    resolvedExpressionType = SimplifyExpressionType(rawType);
                }

                if (resolvedExpressionType is "InstanceVariable" or "DefaultVariable")
                {
                    string fieldName = ResolveFieldPathName(obj["Variable"]);
                    if (!string.IsNullOrWhiteSpace(fieldName))
                    {
                        fieldNames.Add(fieldName);
                    }
                }

                foreach ((_, JsonNode? child) in obj)
                {
                    CollectReferencedClassFieldNamesRecursive(child, resolvedExpressionType, fieldNames);
                }

                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    CollectReferencedClassFieldNamesRecursive(child, currentExpressionType, fieldNames);
                }
            }
        }

        private static bool TryFindRepresentativeCallExpression(JsonArray? scriptBytecode, out JsonObject? representativeCall, out string expressionType)
        {
            representativeCall = null;
            expressionType = string.Empty;
            if (scriptBytecode == null)
                return false;

            foreach (JsonObject expressionObj in scriptBytecode.OfType<JsonObject>())
            {
                if (!TryFindRepresentativeCallExpressionRecursive(expressionObj, out representativeCall, out expressionType))
                    continue;
                if (representativeCall != null)
                    return true;
            }

            representativeCall = null;
            expressionType = string.Empty;
            return false;
        }

        private static bool TryFindRepresentativeCallExpressionRecursive(JsonObject expressionObj, out JsonObject? representativeCall, out string expressionType)
        {
            representativeCall = null;
            expressionType = string.Empty;

            if (TryGetMergedContextCallData(expressionObj, out JsonObject mergedCall, out string mergedCallType))
            {
                representativeCall = mergedCall;
                expressionType = mergedCallType;
                return true;
            }

            string currentType = SimplifyExpressionType(expressionObj["$type"]?.GetValue<string>() ?? string.Empty);
            if (currentType is "FinalFunction" or "LocalFinalFunction" or "CallMath" or "LocalVirtualFunction")
            {
                representativeCall = expressionObj;
                expressionType = currentType;
                return true;
            }

            foreach (JsonObject child in EnumerateExpressionChildren(expressionObj))
            {
                if (TryFindRepresentativeCallExpressionRecursive(child, out representativeCall, out expressionType))
                    return true;
            }

            representativeCall = null;
            expressionType = string.Empty;
            return false;
        }

        private Dictionary<int, BlueprintObjectSymbol> BuildBlueprintObjectSymbols(JsonObject root)
        {
            Dictionary<int, BlueprintObjectSymbol> symbols = new();
            JsonArray? exports = root["Exports"] as JsonArray;
            JsonArray? imports = root["Imports"] as JsonArray;

            if (exports != null)
            {
                for (int i = 0; i < exports.Count; i++)
                {
                    if (exports[i] is not JsonObject exportObj)
                        continue;

                    int index = i + 1;
                    string objectName = exportObj["ObjectName"]?.GetValue<string>() ?? string.Empty;
                    string? className = ResolveIndexedObjectNameFromTables(exports, imports, TryGetIntValue(exportObj["ClassIndex"]));
                    string? outerObjectName = ResolveIndexedObjectNameFromTables(exports, imports, TryGetIntValue(exportObj["OuterIndex"]));
                    symbols[index] = new BlueprintObjectSymbol
                    {
                        Index = index,
                        OuterIndex = TryGetIntValue(exportObj["OuterIndex"]),
                        ObjectName = objectName,
                        ClassName = className,
                        OuterObjectName = outerObjectName,
                        IsExport = true
                    };
                }
            }

            if (imports != null)
            {
                for (int i = 0; i < imports.Count; i++)
                {
                    if (imports[i] is not JsonObject importObj)
                        continue;

                    int index = -(i + 1);
                    string objectName = importObj["ObjectName"]?.GetValue<string>() ?? string.Empty;
                    string? outerObjectName = ResolveIndexedObjectNameFromTables(exports, imports, TryGetIntValue(importObj["OuterIndex"]));
                    symbols[index] = new BlueprintObjectSymbol
                    {
                        Index = index,
                        OuterIndex = TryGetIntValue(importObj["OuterIndex"]),
                        ObjectName = objectName,
                        ClassName = importObj["ClassName"]?.GetValue<string>(),
                        ClassPackage = importObj["ClassPackage"]?.GetValue<string>(),
                        PackageName = importObj["PackageName"]?.GetValue<string>(),
                        OuterObjectName = outerObjectName,
                        IsExport = false
                    };
                }
            }

            return symbols;
        }

        private static BlueprintObjectSymbol CloneBlueprintObjectSymbol(BlueprintObjectSymbol symbol)
        {
            return new BlueprintObjectSymbol
            {
                Index = symbol.Index,
                OuterIndex = symbol.OuterIndex,
                ObjectName = symbol.ObjectName,
                ClassName = symbol.ClassName,
                ClassPackage = symbol.ClassPackage,
                PackageName = symbol.PackageName,
                OuterObjectName = symbol.OuterObjectName,
                IsExport = symbol.IsExport
            };
        }

        private static int TryGetIntValue(JsonNode? node)
        {
            if (node is JsonValue jsonValue && jsonValue.TryGetValue<int>(out int intValue))
                return intValue;
            return 0;
        }

        private static string? ResolveIndexedObjectNameFromTables(JsonArray? exports, JsonArray? imports, int index)
        {
            if (index > 0)
            {
                int exportOffset = index - 1;
                if (exports != null && exportOffset >= 0 && exportOffset < exports.Count && exports[exportOffset] is JsonObject exportObj)
                    return exportObj["ObjectName"]?.GetValue<string>();
            }
            else if (index < 0)
            {
                int importOffset = -index - 1;
                if (imports != null && importOffset >= 0 && importOffset < imports.Count && imports[importOffset] is JsonObject importObj)
                    return importObj["ObjectName"]?.GetValue<string>();
            }

            return null;
        }

        private List<NodeReferenceDescriptor> CollectReferenceDescriptors(
            JsonNode? node,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceFunctionName,
            string? sourceAssetRelativePath)
        {
            List<NodeReferenceDescriptor> descriptors = [];
            CollectReferenceDescriptorsRecursive(node, symbolMap, sourceFunctionName, sourceAssetRelativePath, string.Empty, descriptors);
            return descriptors;
        }

        private static List<FieldOwnerEvidence> CollectFieldOwnerEvidences(
            JsonNode? node,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceFunctionName)
        {
            List<FieldOwnerEvidence> evidences = [];
            CollectFieldOwnerEvidencesRecursive(
                node,
                symbolMap,
                sourceFunctionName,
                currentExpressionType: string.Empty,
                pointerPath: string.Empty,
                evidences);
            return evidences;
        }

        private static void CollectFieldOwnerEvidencesRecursive(
            JsonNode? node,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceFunctionName,
            string currentExpressionType,
            string pointerPath,
            ICollection<FieldOwnerEvidence> evidences)
        {
            if (node is JsonObject obj)
            {
                string expressionType = currentExpressionType;
                if (obj["$type"] is JsonValue typeNode &&
                    typeNode.TryGetValue<string>(out string? rawType) &&
                    !string.IsNullOrWhiteSpace(rawType))
                {
                    string simplifiedType = SimplifyExpressionType(rawType);
                    if (rawType.Contains("UAssetAPI.Kismet.Bytecode.Expressions.", StringComparison.Ordinal))
                    {
                        expressionType = simplifiedType;
                    }

                    if (rawType.Contains("UAssetAPI.UnrealTypes.FFieldPath", StringComparison.Ordinal))
                    {
                        string fieldName = ExtractFieldPathLeafName(obj);
                        int ownerIndex = obj["ResolvedOwner"]?.GetValue<int>() ?? 0;
                        if (!string.IsNullOrWhiteSpace(fieldName) && ownerIndex != 0)
                        {
                            symbolMap.TryGetValue(ownerIndex, out BlueprintObjectSymbol? ownerSymbol);
                            evidences.Add(new FieldOwnerEvidence
                            {
                                FieldName = fieldName,
                                OwnerIndex = ownerIndex,
                                OwnerSymbol = ownerSymbol == null ? null : CloneBlueprintObjectSymbol(ownerSymbol),
                                ExpressionType = expressionType,
                                PointerPath = pointerPath,
                                SourceFunctionName = sourceFunctionName
                            });
                        }
                    }
                }

                foreach ((string key, JsonNode? child) in obj)
                {
                    CollectFieldOwnerEvidencesRecursive(
                        child,
                        symbolMap,
                        sourceFunctionName,
                        expressionType,
                        AppendJsonPointer(pointerPath, key),
                        evidences);
                }

                return;
            }

            if (node is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    CollectFieldOwnerEvidencesRecursive(
                        array[i],
                        symbolMap,
                        sourceFunctionName,
                        currentExpressionType,
                        AppendJsonPointer(pointerPath, i.ToString()),
                        evidences);
                }
            }
        }

        private static void CollectReferenceDescriptorsRecursive(
            JsonNode? node,
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceFunctionName,
            string? sourceAssetRelativePath,
            string pointerPath,
            ICollection<NodeReferenceDescriptor> descriptors)
        {
            if (node is JsonObject obj)
            {
                string currentType = obj["$type"]?.GetValue<string>() ?? string.Empty;
                foreach ((string key, JsonNode? child) in obj)
                {
                    string childPointer = AppendJsonPointer(pointerPath, key);
                    if (child is JsonValue childValue && childValue.TryGetValue<int>(out int index) && IsObjectIndexPointer(currentType, key))
                    {
                        descriptors.Add(CreateDescriptor(symbolMap, sourceFunctionName, sourceAssetRelativePath, childPointer, key, index));
                        continue;
                    }

                    CollectReferenceDescriptorsRecursive(child, symbolMap, sourceFunctionName, sourceAssetRelativePath, childPointer, descriptors);
                }

                return;
            }

            if (node is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    CollectReferenceDescriptorsRecursive(array[i], symbolMap, sourceFunctionName, sourceAssetRelativePath, AppendJsonPointer(pointerPath, i.ToString()), descriptors);
                }
            }
        }

        private static bool IsObjectIndexPointer(string currentType, string key)
        {
            if (key is "StackNode" or "OuterIndex" or "ClassIndex" or "SuperIndex" or "SuperStruct" or "TemplateIndex")
                return true;

            if (key is "PropertyClass" or "Struct" or "Enum" or "MetaClass" or "SignatureFunction" or "InterfaceClass" or "UnderlyingProp" or "ElementProp")
                return true;

            if (key == "Value" &&
                (currentType.Contains("EX_ObjectConst", StringComparison.Ordinal) ||
                 currentType.Contains("SoftObjectProperty", StringComparison.Ordinal) ||
                 currentType.Contains("ObjectProperty", StringComparison.Ordinal)))
            {
                return true;
            }

            return false;
        }

        private static NodeReferenceDescriptor CreateDescriptor(
            IReadOnlyDictionary<int, BlueprintObjectSymbol> symbolMap,
            string? sourceFunctionName,
            string? sourceAssetRelativePath,
            string pointerPath,
            string key,
            int index)
        {
            symbolMap.TryGetValue(index, out BlueprintObjectSymbol? symbol);
            return new NodeReferenceDescriptor
            {
                PointerPath = pointerPath,
                ReferenceKind = key,
                OriginalIndex = index,
                SourceAssetRelativePath = sourceAssetRelativePath,
                ObjectName = symbol?.ObjectName,
                ClassName = symbol?.ClassName,
                ClassPackage = symbol?.ClassPackage,
                PackageName = symbol?.PackageName,
                OuterObjectName = symbol?.OuterObjectName,
                SourceFunctionName = sourceFunctionName
            };
        }

        private static string AppendJsonPointer(string pointerPath, string segment)
        {
            if (string.IsNullOrEmpty(pointerPath))
                return JsonObjectIndexPointerSeparator + EscapeJsonPointerSegment(segment);
            return pointerPath + JsonObjectIndexPointerSeparator + EscapeJsonPointerSegment(segment);
        }

        private static string EscapeJsonPointerSegment(string segment)
        {
            return segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        }

        private static string UnescapeJsonPointerSegment(string segment)
        {
            return segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        }

        private static bool TryGetNodeByPointer(JsonNode root, string pointerPath, out JsonNode? targetNode)
        {
            targetNode = root;
            if (string.IsNullOrWhiteSpace(pointerPath) || pointerPath == JsonObjectIndexPointerSeparator)
                return true;

            string[] segments = pointerPath.Split(JsonObjectIndexPointerSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawSegment in segments)
            {
                string segment = UnescapeJsonPointerSegment(rawSegment);
                if (targetNode is JsonObject obj)
                {
                    if (!obj.TryGetPropertyValue(segment, out targetNode))
                        return false;
                    continue;
                }

                if (targetNode is JsonArray array)
                {
                    if (!int.TryParse(segment, out int index) || index < 0 || index >= array.Count)
                        return false;
                    targetNode = array[index];
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool TrySetIntNodeByPointer(JsonNode root, string pointerPath, int value)
        {
            if (string.IsNullOrWhiteSpace(pointerPath) || pointerPath == JsonObjectIndexPointerSeparator)
                return false;

            int separatorIndex = pointerPath.LastIndexOf(JsonObjectIndexPointerSeparator, StringComparison.Ordinal);
            string parentPointer = separatorIndex <= 0 ? string.Empty : pointerPath[..separatorIndex];
            string leaf = UnescapeJsonPointerSegment(pointerPath[(separatorIndex + 1)..]);
            if (!TryGetNodeByPointer(root, parentPointer, out JsonNode? parentNode) || parentNode == null)
                return false;

            if (parentNode is JsonObject obj)
            {
                obj[leaf] = value;
                return true;
            }

            if (parentNode is JsonArray array &&
                int.TryParse(leaf, out int arrayIndex) &&
                arrayIndex >= 0 &&
                arrayIndex < array.Count)
            {
                array[arrayIndex] = value;
                return true;
            }

            return false;
        }

        private string ConvertBlueprintDataToUAsset(BlueprintData blueprintData, BlueprintAssetContext assetContext, bool includeStatementIndex = false)
        {
            assetContext ??= BlueprintAssetContext.CreateFallback(string.Empty);
            string originalJson = string.IsNullOrWhiteSpace(assetContext.OriginalAssetJson)
                ? "{}"
                : assetContext.OriginalAssetJson;

            JsonObject? root = JsonNode.Parse(originalJson) as JsonObject;
            if (root == null)
                return originalJson;

            UAsset asset = UAsset.DeserializeJson(originalJson);
            BlueprintAssetContext effectiveContext = assetContext.FunctionTemplates.Count == 0 || assetContext.ImportSymbols.Count == 0
                ? BuildBlueprintAssetContext(asset, originalJson, root)
                : BlueprintAssetContext.CloneOrFallback(assetContext);

            NormalizeSpecificCallNodesForExport(blueprintData);
            PruneInvalidBlueprintData(blueprintData);

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
            Dictionary<(Guid NodeId, string PinName), List<ConnectionData>> outgoingConnections = blueprintData.Connections
                .GroupBy(connection => (connection.FromNodeId, connection.FromPinName))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(connection => connection.Sequence)
                        .ThenByDescending(connection => connection.ToNodeId)
                        .ToList());
            Dictionary<Guid, JsonObject> rebuiltExpressionByNodeId = [];

            Dictionary<string, List<SerializableNode>> topLevelNodesByFunction =
                CollectReachableTopLevelNodesByFunction(blueprintData, nodeById);

            foreach (string functionName in topLevelNodesByFunction.Keys)
            {
                _ = TryGetFunctionTemplate(effectiveContext, functionName, out _);
            }

            Dictionary<string, FunctionExport> functionExports = EnsureTypedFunctionExportsForBlueprintFunctions(
                asset,
                effectiveContext,
                topLevelNodesByFunction.Keys);

            Dictionary<string, JsonObject> functionExportJsonTemplates = new(StringComparer.Ordinal);
            foreach ((string functionName, BlueprintFunctionTemplate functionTemplate) in effectiveContext.FunctionTemplates)
            {
                if (string.IsNullOrWhiteSpace(functionTemplate.FunctionExportTemplateJson))
                    continue;
                if (JsonNode.Parse(functionTemplate.FunctionExportTemplateJson) is JsonObject exportJson)
                    functionExportJsonTemplates[functionName] = exportJson;
            }

            foreach ((string functionName, FunctionExport functionExport) in functionExports)
            {
                JsonArray rebuiltScriptJson = [];
                if (topLevelNodesByFunction.TryGetValue(functionName, out List<SerializableNode>? topLevelNodes))
                {
                    foreach (SerializableNode topLevelNode in topLevelNodes)
                    {
                        HashSet<Guid> rebuiltNodeIds = [];
                        JsonObject rebuiltExpression = RebuildExpressionNode(
                            topLevelNode,
                            functionName,
                            asset,
                            effectiveContext,
                            functionExports,
                            nodeById,
                            incomingConnections,
                            rebuiltExpressionByNodeId,
                            rebuiltNodeIds,
                            new HashSet<Guid>());

                        foreach (Guid rebuiltNodeId in rebuiltNodeIds)
                        {
                            if (!nodeById.TryGetValue(rebuiltNodeId, out SerializableNode? rebuiltNode) ||
                                !rebuiltExpressionByNodeId.TryGetValue(rebuiltNodeId, out JsonObject? rebuiltNodeExpression))
                            {
                                continue;
                            }

                            ApplyReferenceMetadataForNode(rebuiltNodeExpression, rebuiltNode, functionName, asset, effectiveContext, functionExports);
                        }
                        rebuiltScriptJson.Add(rebuiltExpression);
                    }
                }

                if (functionExportJsonTemplates.TryGetValue(functionName, out JsonObject? functionTemplateJson) &&
                    functionTemplateJson["ScriptBytecode"] is JsonArray originalScript)
                {
                    foreach (JsonObject terminalExpression in originalScript.OfType<JsonObject>().Where(IsTerminalExpression))
                    {
                        rebuiltScriptJson.Add(terminalExpression.DeepClone());
                    }
                }
                else if ((functionExport.ScriptBytecode?.Length ?? 0) > 0)
                {
                    JsonArray? serialized = JsonNode.Parse(KismetSerializer.SerializeScript(functionExport.ScriptBytecode).ToString()) as JsonArray;
                    if (serialized != null)
                    {
                        foreach (JsonObject terminalExpression in serialized.OfType<JsonObject>().Where(IsTerminalExpression))
                        {
                            rebuiltScriptJson.Add(terminalExpression.DeepClone());
                        }
                    }
                }

                functionExport.LoadedProperties = BuildTypedLoadedPropertiesForFunction(asset, effectiveContext, functionName, blueprintData, rebuiltScriptJson);
                NormalizeAndValidateFunctionFieldOwners(asset, effectiveContext, functionName, functionExport, rebuiltScriptJson);
                ValidateAssignmentOpcodesForFunction(functionName, functionExport.LoadedProperties, rebuiltScriptJson);
                EnsureValidContextPointersRecursive(rebuiltScriptJson);
                functionExport.ScriptBytecode = asset.DeserializeJsonObject<KismetExpression[]>(rebuiltScriptJson.ToJsonString());
                functionExport.ScriptBytecodeRaw = null!;
                functionExport.ScriptBytecodeSize = 0;
                functionExport.Asset = asset;

                IReadOnlyList<SerializableNode> functionTopLevelNodes = topLevelNodesByFunction.TryGetValue(functionName, out List<SerializableNode>? functionTopLevelNodeList)
                    ? functionTopLevelNodeList
                    : Array.Empty<SerializableNode>();
                Dictionary<Guid, int> topLevelStatementIndices = ComputeTopLevelStatementIndicesForFunction(asset, functionExport, functionTopLevelNodes, rebuiltScriptJson);
                ApplyOffsetDrivenExecTargetsForFunction(
                    functionName,
                    functionTopLevelNodes,
                    rebuiltScriptJson,
                    nodeById,
                    incomingConnections,
                    outgoingConnections,
                    topLevelStatementIndices);

                functionExport.ScriptBytecode = asset.DeserializeJsonObject<KismetExpression[]>(rebuiltScriptJson.ToJsonString());
                functionExport.ScriptBytecodeRaw = null!;
                functionExport.ScriptBytecodeSize = 0;
                functionExport.Asset = asset;
            }

            EnsureTypedDependsMapLength(asset);
            EnsureTypedAssetNameMapCoverage(asset);
            asset.ResolveAncestries();

            string serializedJson = asset.SerializeJson(Formatting.Indented);
            JsonObject? serializedRoot = JsonNode.Parse(serializedJson) as JsonObject;
            JsonArray? serializedExports = serializedRoot?["Exports"] as JsonArray;
            if (serializedRoot == null || serializedExports == null)
            {
                assetContext.OriginalAssetJson = serializedJson;
                assetContext.RefreshCaches();
                return serializedJson;
            }

            if (includeStatementIndex)
            {
                AnnotateTopLevelStatementIndices(asset, serializedExports);
            }
            else
            {
                RemoveFieldRecursively(serializedRoot, "StatementIndex");
            }

            string finalJson = serializedRoot.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });

            BlueprintAssetContext refreshedContext = BuildBlueprintAssetContext(asset, finalJson, serializedRoot);
            assetContext.OriginalAssetJson = refreshedContext.OriginalAssetJson;
            assetContext.FunctionTemplates = refreshedContext.FunctionTemplates;
            assetContext.ClassLoadedPropertyTemplatesByName = refreshedContext.ClassLoadedPropertyTemplatesByName;
            assetContext.ReferencedClassFieldNames = refreshedContext.ReferencedClassFieldNames;
            assetContext.FieldOwnerEvidences = refreshedContext.FieldOwnerEvidences;
            assetContext.ClassExportTemplateJson = refreshedContext.ClassExportTemplateJson;
            assetContext.ClassExportObjectName = refreshedContext.ClassExportObjectName;
            assetContext.ClassExportIndex = refreshedContext.ClassExportIndex;
            assetContext.ExportSymbols = refreshedContext.ExportSymbols;
            assetContext.ImportSymbols = refreshedContext.ImportSymbols;
            assetContext.RefreshCaches();
            MergeFunctionTemplateCache(assetContext.FunctionTemplates);

            return finalJson;
        }

        private Dictionary<Guid, int> ComputeTopLevelStatementIndicesForFunction(
            UAsset asset,
            FunctionExport functionExport,
            IReadOnlyList<SerializableNode> topLevelNodes,
            JsonArray rebuiltScriptJson)
        {
            Dictionary<Guid, int> statementIndices = [];
            if (topLevelNodes.Count == 0 || functionExport.ScriptBytecode == null || functionExport.ScriptBytecode.Length == 0)
                return statementIndices;

            KismetSerializer.asset = asset;
            JsonArray? exactScript = JsonNode.Parse(KismetSerializer.SerializeScript(functionExport.ScriptBytecode).ToString()) as JsonArray;
            if (exactScript == null)
                return statementIndices;

            int count = Math.Min(topLevelNodes.Count, Math.Min(rebuiltScriptJson.Count, exactScript.Count));
            for (int i = 0; i < count; i++)
            {
                if (exactScript[i] is not JsonObject exactExpression ||
                    rebuiltScriptJson[i] is not JsonObject rebuiltExpression ||
                    exactExpression["StatementIndex"] is not JsonValue statementValue ||
                    !statementValue.TryGetValue<int>(out int statementIndex))
                {
                    continue;
                }

                rebuiltExpression["StatementIndex"] = statementIndex;
                statementIndices[topLevelNodes[i].Id] = statementIndex;
            }

            return statementIndices;
        }

        private void ApplyOffsetDrivenExecTargetsForFunction(
            string functionName,
            IReadOnlyList<SerializableNode> topLevelNodes,
            JsonArray rebuiltScriptJson,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById,
            IReadOnlyDictionary<(Guid NodeId, string PinName), Guid> incomingConnections,
            IReadOnlyDictionary<(Guid NodeId, string PinName), List<ConnectionData>> outgoingConnections,
            IReadOnlyDictionary<Guid, int> topLevelStatementIndices)
        {
            if (topLevelNodes.Count == 0 || topLevelStatementIndices.Count == 0)
                return;

            for (int i = 0; i < topLevelNodes.Count && i < rebuiltScriptJson.Count; i++)
            {
                if (rebuiltScriptJson[i] is not JsonObject expressionObject)
                    continue;

                SerializableNode node = topLevelNodes[i];
                string expressionType = SimplifyExpressionType(expressionObject["$type"]?.GetValue<string>() ?? string.Empty);
                switch (expressionType)
                {
                    case "Jump":
                    case "JumpIfNot":
                    {
                        SerializableNode targetNode = ResolveOffsetExecTargetNode(
                            functionName,
                            node,
                            "To",
                            topLevelStatementIndices,
                            nodeById,
                            outgoingConnections);
                        expressionObject["CodeOffset"] = topLevelStatementIndices[targetNode.Id];
                        break;
                    }
                    case "PushExecutionFlow":
                    {
                        SerializableNode targetNode = ResolveOffsetExecTargetNode(
                            functionName,
                            node,
                            "To",
                            topLevelStatementIndices,
                            nodeById,
                            outgoingConnections);
                        expressionObject["PushingAddress"] = topLevelStatementIndices[targetNode.Id];
                        break;
                    }
                }
            }
        }

        private SerializableNode ResolveOffsetExecTargetNode(
            string functionName,
            SerializableNode node,
            string outputPinName,
            IReadOnlyDictionary<Guid, int> topLevelStatementIndices,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById,
            IReadOnlyDictionary<(Guid NodeId, string PinName), List<ConnectionData>> outgoingConnections)
        {
            if (!outgoingConnections.TryGetValue((node.Id, outputPinName), out List<ConnectionData>? candidates))
                throw new InvalidOperationException($"函数 {functionName} 中的节点 {node.DefinitionName} 缺少 {outputPinName} 执行流连接，无法回填偏移。");

            List<SerializableNode> targetNodes = candidates
                .Where(connection => string.Equals(connection.ToPinName, "In", StringComparison.Ordinal))
                .Select(connection => nodeById.TryGetValue(connection.ToNodeId, out SerializableNode? targetNode) ? targetNode : null)
                .Where(targetNode => targetNode != null && topLevelStatementIndices.ContainsKey(targetNode.Id))
                .Cast<SerializableNode>()
                .DistinctBy(targetNode => targetNode.Id)
                .ToList();

            if (targetNodes.Count != 1)
                throw new InvalidOperationException($"函数 {functionName} 中的节点 {node.DefinitionName} 的 {outputPinName} 执行流连接数量无效，期望 1 条，实际 {targetNodes.Count} 条。");

            return targetNodes[0];
        }

        private SerializableNode ResolveOffsetExecSourceNode(
            string functionName,
            SerializableNode node,
            string inputPinName,
            IReadOnlyDictionary<Guid, int> topLevelStatementIndices,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById,
            IReadOnlyDictionary<(Guid NodeId, string PinName), Guid> incomingConnections)
        {
            if (!incomingConnections.TryGetValue((node.Id, inputPinName), out Guid sourceNodeId) ||
                !nodeById.TryGetValue(sourceNodeId, out SerializableNode? sourceNode) ||
                !topLevelStatementIndices.ContainsKey(sourceNode.Id))
            {
                throw new InvalidOperationException($"函数 {functionName} 中的节点 {node.DefinitionName} 缺少 {inputPinName} 执行流连接，无法回填偏移。");
            }

            return sourceNode;
        }

        private void ApplyReferenceMetadataForNode(
            JsonObject rebuiltExpression,
            SerializableNode node,
            string functionName,
            UAsset asset,
            BlueprintAssetContext assetContext,
            IReadOnlyDictionary<string, FunctionExport> localFunctionExports)
        {
            Dictionary<int, BlueprintObjectSymbol> currentSymbols = BuildCurrentAssetSymbols(asset);
            List<NodeReferenceDescriptor> descriptors = GetEffectiveReferenceDescriptorsForExport(node);
            foreach (NodeReferenceDescriptor descriptor in descriptors)
            {
                int resolvedIndex = ResolveOrCreateObjectIndex(asset, assetContext, descriptor, currentSymbols, localFunctionExports);
                if (resolvedIndex == 0)
                    continue;
                _ = TrySetIntNodeByPointer(rebuiltExpression, descriptor.PointerPath, resolvedIndex);
            }

            ApplyResolvedCallMetadataForNode(rebuiltExpression, node, asset, assetContext, currentSymbols, localFunctionExports);
            ApplyVariableOwnerFallback(rebuiltExpression, node, functionName, asset, localFunctionExports);
        }

        private void ApplyVariableOwnerFallback(
            JsonObject rebuiltExpression,
            SerializableNode node,
            string functionName,
            UAsset asset,
            IReadOnlyDictionary<string, FunctionExport> localFunctionExports)
        {
            if (!IsGenericVariableDefinitionName(node.DefinitionName))
                return;

            int ownerIndex = 0;
            if (localFunctionExports.TryGetValue(functionName, out FunctionExport? functionExport))
            {
                int exportOffset = asset.Exports.IndexOf(functionExport);
                if (exportOffset >= 0)
                    ownerIndex = exportOffset + 1;
            }

            if (ownerIndex == 0)
                return;

            ApplyVariableOwnerFallbackRecursive(rebuiltExpression, ownerIndex);
        }

        private static void ApplyVariableOwnerFallbackRecursive(JsonNode? node, int ownerIndex)
        {
            if (node is JsonObject obj)
            {
                if (obj["ResolvedOwner"] is JsonValue ownerValue &&
                    ownerValue.TryGetValue<int>(out int resolvedOwner) &&
                    resolvedOwner == 0)
                {
                    obj["ResolvedOwner"] = ownerIndex;
                }

                foreach ((_, JsonNode? child) in obj)
                {
                    ApplyVariableOwnerFallbackRecursive(child, ownerIndex);
                }

                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    ApplyVariableOwnerFallbackRecursive(child, ownerIndex);
                }
            }
        }

        private void NormalizeAndValidateFunctionFieldOwners(
            UAsset asset,
            BlueprintAssetContext assetContext,
            string functionName,
            FunctionExport functionExport,
            JsonArray rebuiltScriptJson)
        {
            FieldOwnerResolutionContext context = BuildFieldOwnerResolutionContext(asset, assetContext, functionName, functionExport, rebuiltScriptJson);
            List<string> validationErrors = [];
            for (int i = 0; i < rebuiltScriptJson.Count; i++)
            {
                if (rebuiltScriptJson[i] is not JsonObject expressionObject)
                    continue;

                NormalizeAndValidateFieldOwnersRecursive(
                    expressionObject,
                    context,
                    validationErrors,
                    currentExpressionType: string.Empty,
                    fieldOwnerExpressionType: string.Empty,
                    pointerPath: $"/ScriptBytecode/{i}",
                    isFieldPointerNewObject: false);
            }

            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Field owner normalization failed for function '{functionName}':{Environment.NewLine}{string.Join(Environment.NewLine, validationErrors)}");
            }
        }

        private FieldOwnerResolutionContext BuildFieldOwnerResolutionContext(
            UAsset asset,
            BlueprintAssetContext assetContext,
            string functionName,
            FunctionExport functionExport,
            JsonArray rebuiltScriptJson)
        {
            int functionOwnerIndex = asset.Exports.IndexOf(functionExport) + 1;
            int classOwnerIndex = 0;
            int superClassOwnerIndex = 0;
            Dictionary<int, BlueprintObjectSymbol> symbolsByIndex = BuildCurrentAssetSymbols(asset);

            ClassExport? classExport = asset.Exports.OfType<ClassExport>().FirstOrDefault();
            if (classExport != null)
            {
                classOwnerIndex = asset.Exports.IndexOf(classExport) + 1;
                superClassOwnerIndex = classExport.SuperStruct?.Index ?? 0;
            }

            if (classOwnerIndex == 0)
                classOwnerIndex = assetContext.ClassExportIndex;

            HashSet<string> functionFieldNames = new(
                (functionExport.LoadedProperties ?? Array.Empty<FProperty>())
                    .Where(property => property?.Name != null)
                    .Select(property => property.Name.ToString())
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);

            if (TryGetFunctionTemplate(assetContext, functionName, out BlueprintFunctionTemplate? functionTemplate) &&
                functionTemplate != null)
            {
                foreach (string propertyName in functionTemplate.LoadedPropertyTemplatesByName.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(propertyName))
                        functionFieldNames.Add(propertyName);
                }

                foreach (string parameterName in functionTemplate.ParameterNames)
                {
                    if (!string.IsNullOrWhiteSpace(parameterName))
                        functionFieldNames.Add(parameterName);
                }

                foreach (string returnValueName in functionTemplate.ReturnValueNames)
                {
                    if (!string.IsNullOrWhiteSpace(returnValueName))
                        functionFieldNames.Add(returnValueName);
                }
            }

            HashSet<string> currentClassFieldNames = new(
                assetContext.ClassLoadedPropertyTemplatesByName.Keys.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);

            HashSet<string> referencedClassFieldNames = new(
                assetContext.ReferencedClassFieldNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);
            Dictionary<string, int> alternateFunctionFieldOwners = new(StringComparer.Ordinal);

            foreach (string fieldName in CollectReferencedClassFieldNames(rebuiltScriptJson))
            {
                referencedClassFieldNames.Add(fieldName);
            }

            Dictionary<string, HashSet<int>> fieldOwnerEvidenceOwners = BuildFieldOwnerEvidenceOwners(assetContext, symbolsByIndex);
            foreach (FunctionExport candidateExport in asset.Exports.OfType<FunctionExport>())
            {
                int candidateIndex = asset.Exports.IndexOf(candidateExport) + 1;
                if (candidateIndex <= 0 || candidateIndex == functionOwnerIndex)
                    continue;

                bool preferCandidate = IsUbergraphFunctionExport(candidateExport);
                foreach (FProperty property in candidateExport.LoadedProperties ?? Array.Empty<FProperty>())
                {
                    string? propertyName = property?.Name?.ToString();
                    if (string.IsNullOrWhiteSpace(propertyName))
                        continue;

                    if (!alternateFunctionFieldOwners.TryGetValue(propertyName, out int existingOwner))
                    {
                        alternateFunctionFieldOwners[propertyName] = candidateIndex;
                        continue;
                    }

                    if (!symbolsByIndex.TryGetValue(existingOwner, out BlueprintObjectSymbol? existingSymbol) ||
                        !string.Equals(existingSymbol.ObjectName, candidateExport.ObjectName?.ToString(), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (preferCandidate)
                        alternateFunctionFieldOwners[propertyName] = candidateIndex;
                }
            }

            return new FieldOwnerResolutionContext
            {
                FunctionName = functionName,
                FunctionOwnerIndex = functionOwnerIndex,
                ClassOwnerIndex = classOwnerIndex,
                SuperClassOwnerIndex = superClassOwnerIndex,
                FunctionFieldNames = functionFieldNames,
                CurrentClassFieldNames = currentClassFieldNames,
                ReferencedClassFieldNames = referencedClassFieldNames,
                FieldOwnerEvidenceOwners = fieldOwnerEvidenceOwners,
                AlternateFunctionFieldOwners = alternateFunctionFieldOwners,
                SymbolsByIndex = symbolsByIndex
            };
        }

        private static Dictionary<string, HashSet<int>> BuildFieldOwnerEvidenceOwners(
            BlueprintAssetContext assetContext,
            IDictionary<int, BlueprintObjectSymbol> currentSymbols)
        {
            Dictionary<string, HashSet<int>> result = new(StringComparer.Ordinal);
            foreach (FieldOwnerEvidence evidence in assetContext.FieldOwnerEvidences)
            {
                if (string.IsNullOrWhiteSpace(evidence.FieldName))
                    continue;

                int ownerIndex = ResolveEvidenceOwnerIndex(evidence, currentSymbols);
                if (ownerIndex == 0)
                    continue;

                if (!result.TryGetValue(evidence.FieldName, out HashSet<int>? owners))
                {
                    owners = new HashSet<int>();
                    result[evidence.FieldName] = owners;
                }

                owners.Add(ownerIndex);
            }

            return result;
        }

        private static int ResolveEvidenceOwnerIndex(
            FieldOwnerEvidence evidence,
            IDictionary<int, BlueprintObjectSymbol> currentSymbols)
        {
            if (evidence.OwnerSymbol != null)
            {
                int symbolIndex = FindObjectIndexBySymbol(currentSymbols, evidence.OwnerSymbol);
                if (symbolIndex != 0)
                    return symbolIndex;
            }

            if (evidence.OwnerIndex != 0 && currentSymbols.ContainsKey(evidence.OwnerIndex))
                return evidence.OwnerIndex;

            return 0;
        }

        private void NormalizeAndValidateFieldOwnersRecursive(
            JsonNode? node,
            FieldOwnerResolutionContext context,
            List<string> validationErrors,
            string currentExpressionType,
            string fieldOwnerExpressionType,
            string pointerPath,
            bool isFieldPointerNewObject)
        {
            if (node is JsonObject obj)
            {
                string resolvedExpressionType = currentExpressionType;
                string resolvedFieldOwnerExpressionType = fieldOwnerExpressionType;
                if (obj["$type"] is JsonValue typeNode &&
                    typeNode.TryGetValue<string>(out string? rawType) &&
                    !string.IsNullOrWhiteSpace(rawType))
                {
                    string simplifiedType = SimplifyExpressionType(rawType);
                    resolvedExpressionType = simplifiedType;

                    if (rawType.Contains("UAssetAPI.Kismet.Bytecode.Expressions.", StringComparison.Ordinal))
                    {
                        resolvedFieldOwnerExpressionType = simplifiedType;
                    }
                }

                if (isFieldPointerNewObject)
                {
                    NormalizeAndValidateFieldPointer(
                        obj,
                        context,
                        validationErrors,
                        resolvedFieldOwnerExpressionType,
                        pointerPath);
                }

                foreach ((string key, JsonNode? child) in obj)
                {
                    bool childIsFieldPointerNewObject = string.Equals(key, "New", StringComparison.Ordinal) &&
                        (pointerPath.EndsWith("/Variable", StringComparison.Ordinal) ||
                         pointerPath.EndsWith("/Value", StringComparison.Ordinal) ||
                         pointerPath.EndsWith("/DestinationProperty", StringComparison.Ordinal) ||
                         pointerPath.EndsWith("/RValuePointer", StringComparison.Ordinal));

                    NormalizeAndValidateFieldOwnersRecursive(
                        child,
                        context,
                        validationErrors,
                        resolvedExpressionType,
                        resolvedFieldOwnerExpressionType,
                        $"{pointerPath}/{key}",
                        childIsFieldPointerNewObject);
                }

                return;
            }

            if (node is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    NormalizeAndValidateFieldOwnersRecursive(
                        array[i],
                        context,
                        validationErrors,
                        currentExpressionType,
                        fieldOwnerExpressionType,
                        $"{pointerPath}/{i}",
                        isFieldPointerNewObject: false);
                }
            }
        }

        private void NormalizeAndValidateFieldPointer(
            JsonObject fieldPathObject,
            FieldOwnerResolutionContext context,
            List<string> validationErrors,
            string currentExpressionType,
            string pointerPath)
        {
            string fieldName = ExtractFieldPathLeafName(fieldPathObject);
            int actualOwner = fieldPathObject["ResolvedOwner"]?.GetValue<int>() ?? 0;

            if (string.IsNullOrWhiteSpace(fieldName))
            {
                bool allowEmptyPointer = IsAllowedEmptyFieldPointer(pointerPath, currentExpressionType);
                if (allowEmptyPointer)
                {
                    if (actualOwner != 0)
                    {
                        validationErrors.Add(
                            $"Empty field pointer at {pointerPath} in {currentExpressionType} must use owner 0, but found {actualOwner}.");
                    }
                }
                else
                {
                    validationErrors.Add(
                        $"Unresolved empty field pointer at {pointerPath} in {currentExpressionType}.");
                }
                return;
            }

            int expectedOwner = ResolveExpectedFieldOwnerIndex(context, fieldName, actualOwner, currentExpressionType, pointerPath);
            if (expectedOwner != 0)
            {
                fieldPathObject["ResolvedOwner"] = expectedOwner;
                actualOwner = expectedOwner;
            }

            if (actualOwner == 0)
            {
                validationErrors.Add(
                    $"Field '{fieldName}' at {pointerPath} in {currentExpressionType} resolved to owner 0.");
                return;
            }

            if (!context.SymbolsByIndex.TryGetValue(actualOwner, out BlueprintObjectSymbol? symbol))
            {
                validationErrors.Add(
                    $"Field '{fieldName}' at {pointerPath} in {currentExpressionType} resolved to missing owner {actualOwner}.");
                return;
            }

            bool isFunctionField = IsFunctionScopedField(context, fieldName, currentExpressionType, pointerPath);
            bool isCurrentClassField = context.CurrentClassFieldNames.Contains(fieldName);
            bool isReferencedClassField = context.ReferencedClassFieldNames.Contains(fieldName);
            bool isNonEmptyRValuePointer = IsNonEmptyRValuePointer(pointerPath, fieldName);
            bool isPersistentFrameDestination = IsPersistentFrameDestination(pointerPath, currentExpressionType);
            bool isInstanceField = currentExpressionType is "InstanceVariable" or "DefaultVariable";
            bool hasOwnerEvidence = HasFieldOwnerEvidence(context, fieldName, actualOwner);

            if (isFunctionField && actualOwner != context.FunctionOwnerIndex)
            {
                validationErrors.Add(
                    $"Function field '{fieldName}' at {pointerPath} bound to owner {actualOwner} ({symbol.ObjectName}) instead of current function {context.FunctionOwnerIndex}.");
                return;
            }

            if (isInstanceField)
            {
                if (context.ObservedInstanceFieldOwners.TryGetValue(fieldName, out int observedOwner) &&
                    observedOwner != actualOwner)
                {
                    string observedName = context.SymbolsByIndex.TryGetValue(observedOwner, out BlueprintObjectSymbol? observedSymbol)
                        ? observedSymbol.ObjectName
                        : observedOwner.ToString();
                    validationErrors.Add(
                        $"Instance field '{fieldName}' at {pointerPath} resolved to owner {actualOwner} ({symbol.ObjectName}) but was already resolved to {observedOwner} ({observedName}) in this function.");
                    return;
                }

                context.ObservedInstanceFieldOwners[fieldName] = actualOwner;

                if (!hasOwnerEvidence &&
                    IsSuspiciousInstanceFieldOwner(symbol))
                {
                    validationErrors.Add(
                        $"Instance field '{fieldName}' at {pointerPath} resolved to suspicious owner {actualOwner} ({symbol.ObjectName}); no original owner evidence allows this binding.");
                    return;
                }
            }

            if (!isFunctionField &&
                (isCurrentClassField || isReferencedClassField) &&
                string.Equals(symbol.ClassName, "Function", StringComparison.Ordinal))
            {
                validationErrors.Add(
                    $"Class field '{fieldName}' at {pointerPath} still points to function owner {actualOwner} ({symbol.ObjectName}).");
                return;
            }

            if (isNonEmptyRValuePointer &&
                string.Equals(symbol.ClassName, "Function", StringComparison.Ordinal) &&
                actualOwner != context.FunctionOwnerIndex)
            {
                validationErrors.Add(
                    $"Non-empty RValuePointer field '{fieldName}' at {pointerPath} still points to foreign function owner {actualOwner} ({symbol.ObjectName}).");
                return;
            }

            if (isPersistentFrameDestination &&
                string.Equals(symbol.ClassName, "Function", StringComparison.Ordinal))
            {
                return;
            }

            if (currentExpressionType is "LocalVariable" or "Let" or "LetBool" or "LetValueOnPersistentFrame" &&
                symbol.ClassName == "Function" &&
                actualOwner != context.FunctionOwnerIndex &&
                !isCurrentClassField)
            {
                validationErrors.Add(
                    $"Local field '{fieldName}' at {pointerPath} still points to foreign function owner {actualOwner} ({symbol.ObjectName}).");
            }
        }

        private int ResolveExpectedFieldOwnerIndex(
            FieldOwnerResolutionContext context,
            string fieldName,
            int actualOwner,
            string currentExpressionType,
            string pointerPath)
        {
            if (IsPersistentFrameDestination(pointerPath, currentExpressionType))
            {
                if (actualOwner != 0 &&
                    context.SymbolsByIndex.TryGetValue(actualOwner, out BlueprintObjectSymbol? actualOwnerSymbol) &&
                    string.Equals(actualOwnerSymbol.ClassName, "Function", StringComparison.Ordinal) &&
                    actualOwner != context.FunctionOwnerIndex)
                {
                    return actualOwner;
                }

                if (context.AlternateFunctionFieldOwners.TryGetValue(fieldName, out int alternateOwner) &&
                    alternateOwner != context.FunctionOwnerIndex)
                {
                    return alternateOwner;
                }
            }

            if (IsFunctionScopedField(context, fieldName, currentExpressionType, pointerPath))
                return context.FunctionOwnerIndex;

            if (currentExpressionType is "InstanceVariable" or "DefaultVariable")
            {
                if (TryResolveFieldOwnerFromEvidence(context, fieldName, actualOwner, out int evidenceOwner, out _))
                    return evidenceOwner;

                if (context.CurrentClassFieldNames.Contains(fieldName))
                    return context.ClassOwnerIndex;

                if (context.ReferencedClassFieldNames.Contains(fieldName))
                {
                    return context.SuperClassOwnerIndex != 0
                        ? context.SuperClassOwnerIndex
                        : context.ClassOwnerIndex;
                }

                if (actualOwner != 0 &&
                    context.SymbolsByIndex.TryGetValue(actualOwner, out BlueprintObjectSymbol? actualOwnerSymbol) &&
                    !string.Equals(actualOwnerSymbol.ClassName, "Function", StringComparison.Ordinal) &&
                    !IsSuspiciousInstanceFieldOwner(actualOwnerSymbol))
                {
                    return actualOwner;
                }

                return 0;
            }

            if (TryResolveFieldOwnerFromEvidence(context, fieldName, actualOwner, out int ownerFromEvidence, out bool ambiguousEvidence))
                return ownerFromEvidence;

            if (ambiguousEvidence)
                return 0;

            if (context.CurrentClassFieldNames.Contains(fieldName))
                return context.ClassOwnerIndex;

            if (context.ReferencedClassFieldNames.Contains(fieldName))
            {
                return context.SuperClassOwnerIndex != 0
                    ? context.SuperClassOwnerIndex
                    : context.ClassOwnerIndex;
            }

            if (IsNonEmptyRValuePointer(pointerPath, fieldName) &&
                actualOwner != 0 &&
                context.SymbolsByIndex.TryGetValue(actualOwner, out BlueprintObjectSymbol? actualOwnerSymbolForRValue))
            {
                if (string.Equals(actualOwnerSymbolForRValue.ClassName, "Function", StringComparison.Ordinal))
                    return context.FunctionOwnerIndex;

                return actualOwner;
            }

            if (currentExpressionType == "LocalVariable" ||
                pointerPath.Contains("/DestinationProperty/", StringComparison.Ordinal) ||
                pointerPath.Contains("/Variable/", StringComparison.Ordinal) ||
                pointerPath.Contains("/Value/", StringComparison.Ordinal))
            {
                if (actualOwner != 0 &&
                    context.SymbolsByIndex.TryGetValue(actualOwner, out BlueprintObjectSymbol? ownerSymbol) &&
                    string.Equals(ownerSymbol.ClassName, "Function", StringComparison.Ordinal))
                {
                    return context.FunctionOwnerIndex;
                }
            }

            return 0;
        }

        private static bool TryResolveFieldOwnerFromEvidence(
            FieldOwnerResolutionContext context,
            string fieldName,
            int actualOwner,
            out int ownerIndex,
            out bool ambiguous)
        {
            ownerIndex = 0;
            ambiguous = false;

            if (!context.FieldOwnerEvidenceOwners.TryGetValue(fieldName, out HashSet<int>? owners) ||
                owners.Count == 0)
            {
                return false;
            }

            if (owners.Count == 1)
            {
                ownerIndex = owners.First();
                return true;
            }

            List<int> nonSuspiciousOwners = owners
                .Where(owner => context.SymbolsByIndex.TryGetValue(owner, out BlueprintObjectSymbol? symbol) &&
                    !IsSuspiciousInstanceFieldOwner(symbol))
                .Distinct()
                .ToList();
            if (nonSuspiciousOwners.Count == 1)
            {
                ownerIndex = nonSuspiciousOwners[0];
                return true;
            }

            if (actualOwner != 0 &&
                owners.Contains(actualOwner) &&
                context.SymbolsByIndex.TryGetValue(actualOwner, out BlueprintObjectSymbol? actualOwnerSymbol) &&
                !IsSuspiciousInstanceFieldOwner(actualOwnerSymbol))
            {
                ownerIndex = actualOwner;
                return true;
            }

            ambiguous = true;
            return false;
        }

        private static bool HasFieldOwnerEvidence(
            FieldOwnerResolutionContext context,
            string fieldName,
            int ownerIndex)
        {
            return ownerIndex != 0 &&
                context.FieldOwnerEvidenceOwners.TryGetValue(fieldName, out HashSet<int>? owners) &&
                owners.Contains(ownerIndex);
        }

        private static bool IsSuspiciousInstanceFieldOwner(BlueprintObjectSymbol symbol)
        {
            if (string.Equals(symbol.ClassName, "Function", StringComparison.Ordinal))
                return true;

            if (string.Equals(symbol.ObjectName, "KismetMathLibrary", StringComparison.Ordinal) ||
                string.Equals(symbol.ObjectName, "KismetArrayLibrary", StringComparison.Ordinal) ||
                string.Equals(symbol.ObjectName, "KismetSystemLibrary", StringComparison.Ordinal) ||
                string.Equals(symbol.ObjectName, "KismetStringLibrary", StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(symbol.ClassName, "BlueprintGeneratedClass", StringComparison.Ordinal) ||
                string.Equals(symbol.ObjectName, "BlueprintGeneratedClass", StringComparison.Ordinal);
        }

        private static bool IsFunctionScopedField(
            FieldOwnerResolutionContext context,
            string fieldName,
            string currentExpressionType,
            string pointerPath)
        {
            if (!context.FunctionFieldNames.Contains(fieldName))
                return false;

            if (currentExpressionType is "InstanceVariable" or "DefaultVariable")
                return false;

            return true;
        }

        private static string ExtractFieldPathLeafName(JsonObject fieldPathObject)
        {
            if (fieldPathObject["Path"] is not JsonArray pathArray || pathArray.Count == 0)
                return string.Empty;

            for (int i = pathArray.Count - 1; i >= 0; i--)
            {
                string? candidate = pathArray[i]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static bool IsAllowedEmptyFieldPointer(string pointerPath, string currentExpressionType)
        {
            return pointerPath.Contains("/RValuePointer/", StringComparison.Ordinal);
        }

        private static bool IsPersistentFrameDestination(string pointerPath, string currentExpressionType)
        {
            return string.Equals(currentExpressionType, "LetValueOnPersistentFrame", StringComparison.Ordinal) &&
                pointerPath.Contains("/DestinationProperty/", StringComparison.Ordinal);
        }

        private static bool IsNonEmptyRValuePointer(string pointerPath, string fieldName)
        {
            return !string.IsNullOrWhiteSpace(fieldName) &&
                pointerPath.Contains("/RValuePointer/", StringComparison.Ordinal);
        }

        private List<NodeReferenceDescriptor> GetEffectiveReferenceDescriptorsForExport(SerializableNode node)
        {
            string expectedCallFunctionName = ResolveCallFunctionNameFromNode(node);
            bool isCallNode = IsCallNodeDefinition(node.DefinitionName);
            HashSet<string> occupiedPointers = new(StringComparer.Ordinal);
            List<NodeReferenceDescriptor> descriptors = [];

            AddReferenceDescriptors(node.ReferenceDescriptors, allowOverwrite: true);

            if (nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
            {
                IEnumerable<NodeReferenceDescriptor> fallbackDescriptors =
                    isCallNode && definition.RepresentativeCallReferenceDescriptors.Count > 0
                        ? definition.RepresentativeCallReferenceDescriptors
                        : definition.ReferenceDescriptors;

                AddReferenceDescriptors(fallbackDescriptors, allowOverwrite: false);
            }

            return descriptors;

            void AddReferenceDescriptors(IEnumerable<NodeReferenceDescriptor> source, bool allowOverwrite)
            {
                foreach (NodeReferenceDescriptor descriptor in source)
                {
                    if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.PointerPath))
                        continue;
                    if (!IsReplayableReferenceDescriptor(descriptor))
                        continue;

                    if (isCallNode &&
                        IsStackNodeDescriptor(descriptor) &&
                        !string.IsNullOrWhiteSpace(expectedCallFunctionName) &&
                        !string.Equals(expectedCallFunctionName, "Function", StringComparison.Ordinal) &&
                        !DescriptorMatchesObjectName(descriptor, expectedCallFunctionName))
                    {
                        continue;
                    }

                    if (!allowOverwrite && occupiedPointers.Contains(descriptor.PointerPath))
                        continue;

                    descriptors.Add(BlueprintModelCloner.Clone(descriptor));
                    occupiedPointers.Add(descriptor.PointerPath);
                }
            }
        }

        private static bool IsReplayableReferenceDescriptor(NodeReferenceDescriptor descriptor)
        {
            if (descriptor == null)
                return false;

            if (IsStackNodeDescriptor(descriptor) &&
                !string.Equals(descriptor.PointerPath, "/StackNode", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(descriptor.ReferenceKind, "ResolvedOwner", StringComparison.Ordinal))
                return false;

            return string.IsNullOrWhiteSpace(descriptor.PointerPath) ||
                !descriptor.PointerPath.EndsWith("/ResolvedOwner", StringComparison.Ordinal);
        }

        private void ApplyResolvedCallMetadataForNode(
            JsonObject rebuiltExpression,
            SerializableNode node,
            UAsset asset,
            BlueprintAssetContext assetContext,
            IDictionary<int, BlueprintObjectSymbol> currentSymbols,
            IReadOnlyDictionary<string, FunctionExport> localFunctionExports)
        {
            string callFunctionName = ResolveCallFunctionNameFromNode(node);
            if (string.IsNullOrWhiteSpace(callFunctionName) ||
                string.Equals(callFunctionName, "Function", StringComparison.Ordinal))
            {
                return;
            }

            JsonObject callExpression = rebuiltExpression;
            string expressionType = SimplifyExpressionType(callExpression["$type"]?.GetValue<string>() ?? string.Empty);
            if (TryGetMergedContextCallData(rebuiltExpression, out JsonObject contextCallExpression, out string contextCallType))
            {
                callExpression = contextCallExpression;
                expressionType = contextCallType;
            }

            if (expressionType == "LocalVirtualFunction")
            {
                callExpression["VirtualFunctionName"] = callFunctionName;
                return;
            }

            if (expressionType is not "FinalFunction" and not "LocalFinalFunction" and not "CallMath")
                return;

            int resolvedIndex = ResolveCallTargetObjectIndex(
                node,
                callFunctionName,
                asset,
                assetContext,
                currentSymbols,
                localFunctionExports);
            if (resolvedIndex != 0)
                callExpression["StackNode"] = resolvedIndex;
        }

        private int ResolveCallTargetObjectIndex(
            SerializableNode node,
            string functionName,
            UAsset asset,
            BlueprintAssetContext assetContext,
            IDictionary<int, BlueprintObjectSymbol> currentSymbols,
            IReadOnlyDictionary<string, FunctionExport> localFunctionExports)
        {
            if (localFunctionExports.TryGetValue(functionName, out FunctionExport? localFunctionExport))
            {
                int localIndex = asset.Exports.IndexOf(localFunctionExport);
                if (localIndex >= 0)
                    return localIndex + 1;
            }

            int currentAssetIndex = FindFunctionObjectIndexByName(currentSymbols, functionName);
            if (currentAssetIndex != 0)
                return currentAssetIndex;

            foreach (NodeReferenceDescriptor descriptor in EnumerateCallTargetDescriptors(node, functionName))
            {
                int resolvedIndex = ResolveOrCreateObjectIndex(asset, assetContext, descriptor, currentSymbols, localFunctionExports);
                if (resolvedIndex != 0)
                    return resolvedIndex;
            }

            NodeReferenceDescriptor fallbackDescriptor = BuildFallbackFunctionReferenceDescriptor(functionName, currentSymbols);
            return ResolveOrCreateObjectIndex(asset, assetContext, fallbackDescriptor, currentSymbols, localFunctionExports);
        }

        private static int FindFunctionObjectIndexByName(
            IEnumerable<KeyValuePair<int, BlueprintObjectSymbol>> symbols,
            string functionName)
        {
            foreach ((int index, BlueprintObjectSymbol symbol) in symbols)
            {
                if (!string.Equals(symbol.ObjectName, functionName, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(symbol.ClassName) &&
                    !string.Equals(symbol.ClassName, "Function", StringComparison.Ordinal))
                {
                    continue;
                }

                return index;
            }

            return 0;
        }

        private IEnumerable<NodeReferenceDescriptor> EnumerateCallTargetDescriptors(SerializableNode node, string functionName)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (NodeReferenceDescriptor descriptor in node.ReferenceDescriptors)
            {
                if (!IsEligibleCallTargetDescriptor(descriptor, functionName))
                    continue;

                string key = $"{descriptor.PointerPath}|{descriptor.ObjectName}|{descriptor.OriginalIndex}|{descriptor.SourceAssetRelativePath}";
                if (!seen.Add(key))
                    continue;
                yield return BlueprintModelCloner.Clone(descriptor);
            }

            if (!nodeDefinitions.TryGetValue(node.DefinitionName, out NodeDefinition? definition))
                yield break;

            IEnumerable<NodeReferenceDescriptor> fallbackDescriptors =
                definition.RepresentativeCallReferenceDescriptors.Count > 0
                    ? definition.RepresentativeCallReferenceDescriptors
                    : definition.ReferenceDescriptors;

            foreach (NodeReferenceDescriptor descriptor in fallbackDescriptors)
            {
                if (!IsEligibleCallTargetDescriptor(descriptor, functionName))
                    continue;

                string key = $"{descriptor.PointerPath}|{descriptor.ObjectName}|{descriptor.OriginalIndex}|{descriptor.SourceAssetRelativePath}";
                if (!seen.Add(key))
                    continue;
                yield return BlueprintModelCloner.Clone(descriptor);
            }
        }

        private static bool IsEligibleCallTargetDescriptor(NodeReferenceDescriptor descriptor, string functionName)
        {
            return IsStackNodeDescriptor(descriptor) &&
                !string.IsNullOrWhiteSpace(descriptor.ObjectName) &&
                string.Equals(descriptor.ObjectName, functionName, StringComparison.Ordinal);
        }

        private static bool IsStackNodeDescriptor(NodeReferenceDescriptor descriptor)
        {
            return string.Equals(descriptor.PointerPath, "/StackNode", StringComparison.Ordinal) ||
                descriptor.PointerPath.EndsWith("/StackNode", StringComparison.Ordinal);
        }

        private static bool DescriptorMatchesObjectName(NodeReferenceDescriptor descriptor, string objectName)
        {
            return string.IsNullOrWhiteSpace(descriptor.ObjectName) ||
                string.Equals(descriptor.ObjectName, objectName, StringComparison.Ordinal);
        }

        private static bool IsCallNodeDefinition(string definitionName)
        {
            string baseDefinitionName = GetBaseDefinitionName(definitionName);
            return string.Equals(baseDefinitionName, "Call", StringComparison.Ordinal) ||
                baseDefinitionName.StartsWith("Call ", StringComparison.Ordinal);
        }

        private static NodeReferenceDescriptor BuildFallbackFunctionReferenceDescriptor(
            string functionName,
            IEnumerable<KeyValuePair<int, BlueprintObjectSymbol>> currentSymbols)
        {
            string? ownerObjectName = InferFunctionOwnerObjectName(functionName, currentSymbols);
            return new NodeReferenceDescriptor
            {
                PointerPath = "/StackNode",
                ReferenceKind = "StackNode",
                OriginalIndex = 0,
                ObjectName = functionName,
                ClassName = "Function",
                ClassPackage = "/Script/CoreUObject",
                OuterObjectName = ownerObjectName
            };
        }

        private static string? InferFunctionOwnerObjectName(
            string functionName,
            IEnumerable<KeyValuePair<int, BlueprintObjectSymbol>> currentSymbols)
        {
            string? preferredOwner = functionName.StartsWith("Array_", StringComparison.Ordinal)
                ? "KismetArrayLibrary"
                : LooksLikeMathLibraryFunction(functionName)
                    ? "KismetMathLibrary"
                    : ResolveOwnerFromFunctionPrefix(functionName);

            if (string.IsNullOrWhiteSpace(preferredOwner))
                return null;

            foreach ((_, BlueprintObjectSymbol symbol) in currentSymbols)
            {
                if (string.Equals(symbol.ObjectName, preferredOwner, StringComparison.Ordinal))
                    return preferredOwner;
            }

            return preferredOwner;
        }

        private static bool LooksLikeMathLibraryFunction(string functionName)
        {
            return functionName.Contains("_Int", StringComparison.Ordinal) ||
                functionName.Contains("_Float", StringComparison.Ordinal) ||
                functionName.Contains("_Double", StringComparison.Ordinal) ||
                functionName.Contains("_Byte", StringComparison.Ordinal) ||
                functionName.Contains("_Object", StringComparison.Ordinal) ||
                functionName.Contains("_Vector", StringComparison.Ordinal) ||
                functionName.Contains("Equal", StringComparison.Ordinal) ||
                functionName.Contains("NotEqual", StringComparison.Ordinal) ||
                functionName.Contains("Less", StringComparison.Ordinal) ||
                functionName.Contains("Greater", StringComparison.Ordinal);
        }

        private static string? ResolveOwnerFromFunctionPrefix(string functionName)
        {
            int underscore = functionName.IndexOf('_');
            if (underscore <= 0)
                return null;

            string ownerName = functionName[..underscore];
            return string.IsNullOrWhiteSpace(ownerName) ? null : ownerName;
        }

        private Dictionary<int, BlueprintObjectSymbol> BuildCurrentAssetSymbols(UAsset asset)
        {
            Dictionary<int, BlueprintObjectSymbol> symbols = new();

            for (int i = 0; i < asset.Exports.Count; i++)
            {
                Export export = asset.Exports[i];
                string objectName = export.ObjectName?.ToString() ?? string.Empty;
                string? className = export.ClassIndex?.IsNull() == false
                    ? ResolvePackageIndexObjectName(asset, export.ClassIndex.Index)
                    : null;
                string? outerObjectName = export.OuterIndex?.IsNull() == false
                    ? ResolvePackageIndexObjectName(asset, export.OuterIndex.Index)
                    : null;
                symbols[i + 1] = new BlueprintObjectSymbol
                {
                    Index = i + 1,
                    OuterIndex = export.OuterIndex?.Index ?? 0,
                    ObjectName = objectName,
                    ClassName = className,
                    OuterObjectName = outerObjectName,
                    IsExport = true
                };
            }

            for (int i = 0; i < asset.Imports.Count; i++)
            {
                Import import = asset.Imports[i];
                symbols[-(i + 1)] = new BlueprintObjectSymbol
                {
                    Index = -(i + 1),
                    OuterIndex = import.OuterIndex?.Index ?? 0,
                    ObjectName = import.ObjectName?.ToString() ?? string.Empty,
                    ClassName = import.ClassName?.ToString(),
                    ClassPackage = import.ClassPackage?.ToString(),
                    PackageName = import.PackageName?.ToString(),
                    OuterObjectName = import.OuterIndex?.IsNull() == false
                        ? ResolvePackageIndexObjectName(asset, import.OuterIndex.Index)
                        : null,
                    IsExport = false
                };
            }

            return symbols;
        }

        private string? ResolvePackageIndexObjectName(UAsset asset, int index)
        {
            if (index > 0)
            {
                int exportOffset = index - 1;
                if (exportOffset >= 0 && exportOffset < asset.Exports.Count)
                    return asset.Exports[exportOffset].ObjectName?.ToString();
            }
            else if (index < 0)
            {
                int importOffset = -index - 1;
                if (importOffset >= 0 && importOffset < asset.Imports.Count)
                    return asset.Imports[importOffset].ObjectName?.ToString();
            }

            return null;
        }

        private int ResolveOrCreateObjectIndex(
            UAsset asset,
            BlueprintAssetContext assetContext,
            NodeReferenceDescriptor descriptor,
            IDictionary<int, BlueprintObjectSymbol> currentSymbols,
            IReadOnlyDictionary<string, FunctionExport> localFunctionExports)
        {
            if (descriptor.OriginalIndex == 0 && string.IsNullOrWhiteSpace(descriptor.ObjectName))
                return 0;

            if (!string.IsNullOrWhiteSpace(descriptor.ObjectName) &&
                localFunctionExports.TryGetValue(descriptor.ObjectName, out FunctionExport? localFunctionExport))
            {
                int localIndex = asset.Exports.IndexOf(localFunctionExport);
                if (localIndex >= 0)
                    return localIndex + 1;
            }

            int existingIndex = FindObjectIndexByDescriptor(currentSymbols, descriptor);
            if (existingIndex != 0)
                return existingIndex;

            if (descriptor.OriginalIndex < 0 ||
                descriptor.OriginalIndex > 0 ||
                string.Equals(descriptor.ClassName, "Function", StringComparison.Ordinal))
            {
                int importIndex = CreateImportFromDescriptor(asset, assetContext, descriptor, currentSymbols);
                if (importIndex != 0)
                    return importIndex;
            }

            if (descriptor.OriginalIndex > 0 && assetContext.SymbolsByIndex.TryGetValue(descriptor.OriginalIndex, out BlueprintObjectSymbol? originalSymbol))
            {
                existingIndex = FindObjectIndexBySymbol(currentSymbols, originalSymbol);
                if (existingIndex != 0)
                    return existingIndex;
            }

            return descriptor.OriginalIndex;
        }

        private static int FindObjectIndexByDescriptor(
            IEnumerable<KeyValuePair<int, BlueprintObjectSymbol>> symbols,
            NodeReferenceDescriptor descriptor)
        {
            foreach ((int index, BlueprintObjectSymbol symbol) in symbols)
            {
                if (!string.Equals(symbol.ObjectName, descriptor.ObjectName, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(descriptor.ClassName) &&
                    !string.Equals(symbol.ClassName, descriptor.ClassName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(descriptor.ClassPackage) &&
                    !string.Equals(symbol.ClassPackage, descriptor.ClassPackage, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(descriptor.PackageName) &&
                    !string.Equals(symbol.PackageName, descriptor.PackageName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(descriptor.OuterObjectName) &&
                    !string.Equals(symbol.OuterObjectName, descriptor.OuterObjectName, StringComparison.Ordinal))
                {
                    continue;
                }
                return index;
            }

            return 0;
        }

        private static int FindObjectIndexBySymbol(
            IEnumerable<KeyValuePair<int, BlueprintObjectSymbol>> symbols,
            BlueprintObjectSymbol descriptor)
        {
            foreach ((int index, BlueprintObjectSymbol symbol) in symbols)
            {
                if (!string.Equals(symbol.ObjectName, descriptor.ObjectName, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(descriptor.ClassName) &&
                    !string.Equals(symbol.ClassName, descriptor.ClassName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(descriptor.ClassPackage) &&
                    !string.Equals(symbol.ClassPackage, descriptor.ClassPackage, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(descriptor.PackageName) &&
                    !string.Equals(symbol.PackageName, descriptor.PackageName, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(descriptor.OuterObjectName) &&
                    !string.Equals(symbol.OuterObjectName, descriptor.OuterObjectName, StringComparison.Ordinal))
                {
                    continue;
                }
                return index;
            }

            return 0;
        }

        private int CreateImportFromDescriptor(
            UAsset asset,
            BlueprintAssetContext assetContext,
            NodeReferenceDescriptor descriptor,
            IDictionary<int, BlueprintObjectSymbol> currentSymbols)
        {
            string objectName = descriptor.ObjectName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(objectName))
                return 0;

            BlueprintObjectSymbol? sourceSymbol = TryResolveSourceSymbol(assetContext, descriptor, out BlueprintAssetContext? sourceContext);
            if (sourceSymbol != null && sourceContext != null)
            {
                int sourceIndex = CreateImportFromSymbol(asset, sourceContext, sourceSymbol, currentSymbols, new HashSet<string>(StringComparer.Ordinal));
                if (sourceIndex != 0)
                    return sourceIndex;
            }

            int outerIndex = 0;
            if (!string.IsNullOrWhiteSpace(descriptor.OuterObjectName))
            {
                NodeReferenceDescriptor outerDescriptor = new()
                {
                    ObjectName = descriptor.OuterObjectName
                };
                outerIndex = FindObjectIndexByDescriptor(currentSymbols, outerDescriptor);
                if (outerIndex == 0 && descriptor.OuterObjectName.StartsWith("/", StringComparison.Ordinal))
                {
                    outerIndex = CreateImportFromDescriptor(
                        asset,
                        assetContext,
                        new NodeReferenceDescriptor
                        {
                            ObjectName = descriptor.OuterObjectName,
                            ClassName = "Package",
                            ClassPackage = "/Script/CoreUObject",
                            PackageName = null,
                            OuterObjectName = null
                        },
                        currentSymbols);
                }
            }

            Import import = new()
            {
                ObjectName = new FName(asset, objectName),
                OuterIndex = new FPackageIndex(outerIndex),
                ClassPackage = new FName(asset, string.IsNullOrWhiteSpace(descriptor.ClassPackage) ? "/Script/CoreUObject" : descriptor.ClassPackage),
                ClassName = new FName(asset, ResolveImportClassName(descriptor)),
                PackageName = string.IsNullOrWhiteSpace(descriptor.PackageName) ? null : new FName(asset, descriptor.PackageName),
                bImportOptional = false
            };

            asset.Imports.Add(import);
            int newIndex = -asset.Imports.Count;
            currentSymbols[newIndex] = new BlueprintObjectSymbol
            {
                Index = newIndex,
                ObjectName = objectName,
                ClassName = descriptor.ClassName,
                ClassPackage = descriptor.ClassPackage,
                PackageName = descriptor.PackageName,
                OuterObjectName = descriptor.OuterObjectName,
                IsExport = false
            };

            return newIndex;
        }

        private BlueprintObjectSymbol? TryResolveSourceSymbol(
            BlueprintAssetContext assetContext,
            NodeReferenceDescriptor descriptor,
            out BlueprintAssetContext? sourceContext)
        {
            sourceContext = null;

            if (!string.IsNullOrWhiteSpace(descriptor.SourceAssetRelativePath) &&
                TryLoadSourceAssetContext(descriptor.SourceAssetRelativePath, out BlueprintAssetContext? descriptorContext) &&
                descriptorContext != null &&
                descriptorContext.SymbolsByIndex.TryGetValue(descriptor.OriginalIndex, out BlueprintObjectSymbol? descriptorSymbol))
            {
                sourceContext = descriptorContext;
                return CloneBlueprintObjectSymbol(descriptorSymbol);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.SourceFunctionName) &&
                TryEnsureFunctionTemplateAvailable(descriptor.SourceFunctionName) &&
                functionTemplatesByName.TryGetValue(descriptor.SourceFunctionName, out BlueprintFunctionTemplate? sourceTemplate) &&
                !string.IsNullOrWhiteSpace(sourceTemplate.SourceAssetRelativePath) &&
                TryLoadSourceAssetContext(sourceTemplate.SourceAssetRelativePath, out BlueprintAssetContext? functionContext) &&
                functionContext != null &&
                functionContext.SymbolsByIndex.TryGetValue(descriptor.OriginalIndex, out BlueprintObjectSymbol? functionSymbol))
            {
                sourceContext = functionContext;
                return CloneBlueprintObjectSymbol(functionSymbol);
            }

            if ((string.IsNullOrWhiteSpace(descriptor.SourceAssetRelativePath) ||
                 string.Equals(assetContext.SourceAssetRelativePath, descriptor.SourceAssetRelativePath, StringComparison.OrdinalIgnoreCase)) &&
                assetContext.SymbolsByIndex.TryGetValue(descriptor.OriginalIndex, out BlueprintObjectSymbol? currentSymbol))
            {
                sourceContext = assetContext;
                return CloneBlueprintObjectSymbol(currentSymbol);
            }

            return null;
        }

        private int CreateImportFromSymbol(
            UAsset asset,
            BlueprintAssetContext sourceContext,
            BlueprintObjectSymbol symbol,
            IDictionary<int, BlueprintObjectSymbol> currentSymbols,
            ISet<string> recursionGuard)
        {
            string key = $"{symbol.ObjectName}|{symbol.ClassName}|{symbol.OuterObjectName}|{symbol.ClassPackage}|{symbol.PackageName}";
            if (!recursionGuard.Add(key))
                return 0;

            int existingIndex = FindObjectIndexBySymbol(currentSymbols, symbol);
            if (existingIndex != 0)
                return existingIndex;

            int outerIndex = 0;
            if (symbol.OuterIndex != 0 &&
                sourceContext.SymbolsByIndex.TryGetValue(symbol.OuterIndex, out BlueprintObjectSymbol? outerSymbol))
            {
                outerIndex = CreateImportFromSymbol(asset, sourceContext, outerSymbol, currentSymbols, recursionGuard);
            }
            else if (!string.IsNullOrWhiteSpace(symbol.OuterObjectName) &&
                symbol.OuterObjectName.StartsWith("/", StringComparison.Ordinal))
            {
                outerIndex = CreateImportFromDescriptor(
                    asset,
                    sourceContext,
                    new NodeReferenceDescriptor
                    {
                        ObjectName = symbol.OuterObjectName,
                        ClassName = "Package",
                        ClassPackage = "/Script/CoreUObject"
                    },
                    currentSymbols);
            }

            Import import = new()
            {
                ObjectName = new FName(asset, symbol.ObjectName),
                OuterIndex = new FPackageIndex(outerIndex),
                ClassPackage = new FName(asset, string.IsNullOrWhiteSpace(symbol.ClassPackage) ? "/Script/CoreUObject" : symbol.ClassPackage),
                ClassName = new FName(asset, string.IsNullOrWhiteSpace(symbol.ClassName)
                    ? ResolveImportClassName(new NodeReferenceDescriptor { ObjectName = symbol.ObjectName })
                    : symbol.ClassName),
                PackageName = string.IsNullOrWhiteSpace(symbol.PackageName) ? null : new FName(asset, symbol.PackageName),
                bImportOptional = false
            };

            asset.Imports.Add(import);
            int newIndex = -asset.Imports.Count;
            currentSymbols[newIndex] = new BlueprintObjectSymbol
            {
                Index = newIndex,
                OuterIndex = outerIndex,
                ObjectName = symbol.ObjectName,
                ClassName = symbol.ClassName,
                ClassPackage = symbol.ClassPackage,
                PackageName = symbol.PackageName,
                OuterObjectName = symbol.OuterObjectName,
                IsExport = false
            };

            return newIndex;
        }

        private static string ResolveImportClassName(NodeReferenceDescriptor descriptor)
        {
            if (!string.IsNullOrWhiteSpace(descriptor.ClassName))
                return descriptor.ClassName;
            if (!string.IsNullOrWhiteSpace(descriptor.ObjectName) &&
                descriptor.ObjectName.StartsWith("/", StringComparison.Ordinal))
            {
                return "Package";
            }
            return "Object";
        }

        private Dictionary<string, FunctionExport> EnsureTypedFunctionExportsForBlueprintFunctions(
            UAsset asset,
            BlueprintAssetContext assetContext,
            IEnumerable<string> functionNames)
        {
            Dictionary<string, FunctionExport> result = asset.Exports
                .OfType<FunctionExport>()
                .Where(export => export.ObjectName != null)
                .ToDictionary(export => export.ObjectName.ToString(), export => export, StringComparer.Ordinal);

            ClassExport? classExport = asset.Exports.OfType<ClassExport>().FirstOrDefault();
            FunctionExport? existingUbergraph = asset.Exports
                .OfType<FunctionExport>()
                .FirstOrDefault(IsUbergraphFunctionExport);
            FunctionExport? existingFunction = asset.Exports
                .OfType<FunctionExport>()
                .FirstOrDefault(export => !IsUbergraphFunctionExport(export))
                ?? asset.Exports.OfType<FunctionExport>().FirstOrDefault();

            foreach (string functionName in functionNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                if (result.TryGetValue(functionName, out FunctionExport? existing))
                {
                    EnsureFunctionInClassExport(asset, classExport, functionName, existing);
                    continue;
                }

                FunctionExport created = CreateTypedFunctionExport(asset, assetContext, functionName, existingUbergraph, existingFunction);
                asset.Exports.Add(created);
                created.Asset = asset;
                result[functionName] = created;
                EnsureFunctionInClassExport(asset, classExport, functionName, created);
            }

            return result;
        }

        private FunctionExport CreateTypedFunctionExport(
            UAsset asset,
            BlueprintAssetContext assetContext,
            string functionName,
            FunctionExport? existingUbergraph,
            FunctionExport? existingFunction)
        {
            FunctionExport? export = null;
            bool wantsUbergraphFunction = IsLikelyUbergraphFunctionName(functionName);
            if (TryGetFunctionTemplate(assetContext, functionName, out BlueprintFunctionTemplate? resolvedTemplate) &&
                resolvedTemplate != null &&
                !string.IsNullOrWhiteSpace(resolvedTemplate.FunctionExportTemplateJson))
            {
                wantsUbergraphFunction = resolvedTemplate.IsUbergraphFunction;
                export = DeserializeFunctionExportTemplate(asset, assetContext, resolvedTemplate);
            }

            if (export == null)
            {
                BlueprintFunctionTemplate? fallbackTemplate = assetContext.FunctionTemplates.Values
                    .FirstOrDefault(template => template.IsUbergraphFunction == wantsUbergraphFunction);
                if (fallbackTemplate != null && !string.IsNullOrWhiteSpace(fallbackTemplate.FunctionExportTemplateJson))
                {
                    export = DeserializeFunctionExportTemplate(asset, assetContext, fallbackTemplate);
                }
            }

            if (export == null && wantsUbergraphFunction && existingUbergraph != null)
            {
                export = (FunctionExport)existingUbergraph.Clone();
            }

            if (export == null && !wantsUbergraphFunction && existingFunction != null)
            {
                export = (FunctionExport)existingFunction.Clone();
            }

            if (export == null && existingFunction != null)
            {
                export = (FunctionExport)existingFunction.Clone();
            }

            if (export == null && existingUbergraph != null)
            {
                export = (FunctionExport)existingUbergraph.Clone();
            }

            export ??= new FunctionExport();
            export.Asset = asset;
            export.ObjectName = new FName(asset, functionName);
            export.ScriptBytecode = [];
            export.ScriptBytecodeRaw = null!;
            export.ScriptBytecodeSize = 0;
            export.LoadedProperties = [];
            export.Children = [];

            if (wantsUbergraphFunction)
            {
                export.FunctionFlags |= EFunctionFlags.FUNC_UbergraphFunction;
            }
            else
            {
                export.FunctionFlags &= ~EFunctionFlags.FUNC_UbergraphFunction;
            }

            NormalizeNewFunctionExportSuperStruct(export);
            return export;
        }

        private static void NormalizeNewFunctionExportSuperStruct(FunctionExport export)
        {
            int superIndex = export.SuperIndex?.Index ?? 0;
            if (superIndex == 0)
            {
                export.SuperStruct ??= new FPackageIndex(0);
                return;
            }

            int oldSuperStruct = export.SuperStruct?.Index ?? 0;
            if (oldSuperStruct == superIndex)
                return;

            export.SuperStruct = new FPackageIndex(superIndex);
            ReplaceDependencyIndex(export.SerializationBeforeSerializationDependencies, oldSuperStruct, superIndex);
            ReplaceDependencyIndex(export.CreateBeforeSerializationDependencies, oldSuperStruct, superIndex);
            ReplaceDependencyIndex(export.SerializationBeforeCreateDependencies, oldSuperStruct, superIndex);
            ReplaceDependencyIndex(export.CreateBeforeCreateDependencies, oldSuperStruct, superIndex);
        }

        private static void ReplaceDependencyIndex(List<FPackageIndex>? dependencies, int oldIndex, int newIndex)
        {
            if (dependencies == null || oldIndex == 0 || oldIndex == newIndex)
                return;

            for (int i = 0; i < dependencies.Count; i++)
            {
                if ((dependencies[i]?.Index ?? 0) == oldIndex)
                    dependencies[i] = new FPackageIndex(newIndex);
            }
        }

        private static bool IsUbergraphFunctionExport(FunctionExport? export)
        {
            if (export == null)
                return false;

            if (export.FunctionFlags.HasFlag(EFunctionFlags.FUNC_UbergraphFunction))
                return true;

            return IsLikelyUbergraphFunctionName(export.ObjectName?.ToString());
        }

        private static bool IsLikelyUbergraphFunctionName(string? functionName)
        {
            return !string.IsNullOrWhiteSpace(functionName) &&
                functionName.StartsWith("ExecuteUbergraph_", StringComparison.Ordinal);
        }

        private static bool HasUbergraphFunctionFlag(string? rawFunctionFlags)
        {
            return !string.IsNullOrWhiteSpace(rawFunctionFlags) &&
                rawFunctionFlags.Contains("FUNC_UbergraphFunction", StringComparison.Ordinal);
        }

        private FunctionExport? DeserializeFunctionExportTemplate(
            UAsset asset,
            BlueprintAssetContext assetContext,
            BlueprintFunctionTemplate functionTemplate)
        {
            if (string.IsNullOrWhiteSpace(functionTemplate.FunctionExportTemplateJson))
                return null;

            JsonObject? exportJson = JsonNode.Parse(functionTemplate.FunctionExportTemplateJson) as JsonObject;
            if (exportJson == null)
                return null;

            Dictionary<int, BlueprintObjectSymbol> currentSymbols = BuildCurrentAssetSymbols(asset);
            Dictionary<string, FunctionExport> localFunctionExports = asset.Exports
                .OfType<FunctionExport>()
                .Where(candidate => candidate.ObjectName != null)
                .ToDictionary(candidate => candidate.ObjectName.ToString(), candidate => candidate, StringComparer.Ordinal);

            foreach (NodeReferenceDescriptor descriptor in functionTemplate.FunctionExportReferences)
            {
                if (!IsReplayableReferenceDescriptor(descriptor))
                    continue;

                int resolvedIndex = ResolveOrCreateObjectIndex(asset, assetContext, descriptor, currentSymbols, localFunctionExports);
                if (resolvedIndex == 0)
                    continue;
                _ = TrySetIntNodeByPointer(exportJson, descriptor.PointerPath, resolvedIndex);
            }

            return asset.DeserializeJsonObject<FunctionExport>(exportJson.ToJsonString());
        }

        private void EnsureFunctionInClassExport(UAsset asset, ClassExport? classExport, string functionName, FunctionExport functionExport)
        {
            if (classExport == null)
                return;

            int exportIndex = asset.Exports.IndexOf(functionExport) + 1;
            if (exportIndex <= 0)
                return;

            classExport.FuncMap ??= new UAssetAPI.UnrealTypes.TMap<FName, FPackageIndex>();
            bool hasFuncMap = classExport.FuncMap.Keys.Any(name => string.Equals(name.ToString(), functionName, StringComparison.Ordinal));
            if (!hasFuncMap)
            {
                classExport.FuncMap.Add(new FName(asset, functionName), new FPackageIndex(exportIndex));
            }

            classExport.Children ??= [];
            if (!classExport.Children.Any(child => child.Index == exportIndex))
            {
                List<FPackageIndex> children = classExport.Children.ToList();
                children.Add(new FPackageIndex(exportIndex));
                classExport.Children = children.ToArray();
            }
        }

        private FProperty[] BuildTypedLoadedPropertiesForFunction(
            UAsset asset,
            BlueprintAssetContext assetContext,
            string functionName,
            BlueprintData blueprintData,
            JsonArray rebuiltScriptJson)
        {
            Dictionary<string, LoadedPropertyTemplate> loadedProperties = new(StringComparer.Ordinal);
            HashSet<string> persistentFrameDestinationNames = new(StringComparer.Ordinal);
            Dictionary<Guid, SerializableNode> nodeById = blueprintData.Nodes.ToDictionary(node => node.Id);
            Dictionary<Guid, List<Guid>> nodeAdjacency = BuildNodeAdjacency(blueprintData);
            if (TryGetFunctionTemplate(assetContext, functionName, out BlueprintFunctionTemplate? functionTemplate) &&
                functionTemplate != null)
            {
                foreach ((string key, LoadedPropertyTemplate value) in functionTemplate.LoadedPropertyTemplatesByName)
                {
                    loadedProperties[key] = BlueprintModelCloner.Clone(value);
                }
            }

            foreach (SerializableNode node in blueprintData.Nodes)
            {
                if (!NodeBelongsToFunction(node, functionName, nodeById, nodeAdjacency))
                {
                    continue;
                }

                foreach ((string propertyName, string propertyJson) in node.PropertyTemplateJsonByName)
                {
                    if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(propertyJson))
                        continue;

                    UpsertLoadedPropertyTemplate(loadedProperties, new LoadedPropertyTemplate
                    {
                        PropertyName = propertyName,
                        PropertyTemplateJson = propertyJson,
                        OwnerObjectName = functionName,
                        OwnerKind = "Function",
                        SourceFunctionName = functionName,
                        ReferenceDescriptors = node.ReferenceDescriptors
                            .Where(IsReplayableReferenceDescriptor)
                            .Select(BlueprintModelCloner.Clone)
                            .ToList()
                    });
                }

                foreach (PinExportSchema schema in node.PinSchemas)
                {
                    if (string.IsNullOrWhiteSpace(schema.PropertyName) ||
                        string.IsNullOrWhiteSpace(schema.PropertyTemplateJson) ||
                        string.Equals(schema.PropertyOwnerKind, "Class", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    UpsertLoadedPropertyTemplate(loadedProperties, new LoadedPropertyTemplate
                    {
                        PropertyName = schema.PropertyName,
                        PropertyTemplateJson = schema.PropertyTemplateJson,
                        OwnerObjectName = functionName,
                        OwnerKind = "Function",
                        SourceFunctionName = functionName,
                        ReferenceDescriptors = node.ReferenceDescriptors
                            .Where(IsReplayableReferenceDescriptor)
                            .Select(BlueprintModelCloner.Clone)
                            .ToList()
                    });
                }

                if (TryResolveVariableLoadedPropertyTemplate(assetContext, functionName, node, out VariableLoadedPropertyResolution? variableResolution) &&
                    variableResolution != null &&
                    string.Equals(variableResolution.OwnerKind, "Function", StringComparison.Ordinal))
                {
                    UpsertLoadedPropertyTemplate(loadedProperties, variableResolution.Template);
                }

                if (node.MetaData.TryGetValue("SourceExpressionType", out string? sourceExpressionType) &&
                    string.Equals(sourceExpressionType, "LetValueOnPersistentFrame", StringComparison.Ordinal))
                {
                    foreach (PinExportSchema schema in node.PinSchemas)
                    {
                        if (!string.IsNullOrWhiteSpace(schema.PropertyName))
                            persistentFrameDestinationNames.Add(schema.PropertyName);
                    }
                }
            }

            if (functionTemplate != null)
            {
                HashSet<string> protectedPropertyNames = new(functionTemplate.ParameterNames, StringComparer.Ordinal);
                foreach (string returnName in functionTemplate.ReturnValueNames)
                {
                    if (!string.IsNullOrWhiteSpace(returnName))
                        protectedPropertyNames.Add(returnName);
                }

                foreach (string propertyName in persistentFrameDestinationNames)
                {
                    if (string.IsNullOrWhiteSpace(propertyName) || protectedPropertyNames.Contains(propertyName))
                        continue;

                    loadedProperties.Remove(propertyName);
                }
            }

            Dictionary<string, int> firstUseOrdinals = CollectFunctionFieldFirstUseOrdinals(rebuiltScriptJson, loadedProperties.Keys);
            foreach ((string propertyName, int firstUseOrdinal) in firstUseOrdinals)
            {
                if (loadedProperties.TryGetValue(propertyName, out LoadedPropertyTemplate? template))
                    template.FirstUseOrdinal = firstUseOrdinal;
            }

            List<FProperty> typedProperties = [];
            foreach (LoadedPropertyTemplate template in GetOrderedLoadedPropertyTemplatesForFunction(loadedProperties, functionTemplate))
            {
                JsonNode? propertyNode = JsonNode.Parse(template.PropertyTemplateJson);
                if (propertyNode == null)
                    continue;

                Dictionary<int, BlueprintObjectSymbol> currentSymbols = BuildCurrentAssetSymbols(asset);
                foreach (NodeReferenceDescriptor descriptor in template.ReferenceDescriptors)
                {
                    if (!IsReplayableReferenceDescriptor(descriptor))
                        continue;

                    int resolvedIndex = ResolveOrCreateObjectIndex(asset, assetContext, descriptor, currentSymbols, new Dictionary<string, FunctionExport>(StringComparer.Ordinal));
                    if (resolvedIndex == 0)
                        continue;
                    _ = TrySetIntNodeByPointer(propertyNode, descriptor.PointerPath, resolvedIndex);
                }

                FProperty property = asset.DeserializeJsonObject<FProperty>(propertyNode.ToJsonString());
                typedProperties.Add(property);
            }

            return typedProperties.ToArray();
        }

        private static IEnumerable<LoadedPropertyTemplate> GetOrderedLoadedPropertyTemplatesForFunction(
            IDictionary<string, LoadedPropertyTemplate> loadedProperties,
            BlueprintFunctionTemplate? functionTemplate)
        {
            List<LoadedPropertyTemplate> originalTemplates = loadedProperties.Values
                .Where(template => template.OriginalOrdinal >= 0)
                .OrderBy(template => template.OriginalOrdinal)
                .ThenBy(template => template.PropertyName, StringComparer.Ordinal)
                .ToList();

            List<LoadedPropertyTemplate> addedTemplates = loadedProperties.Values
                .Where(template => template.OriginalOrdinal < 0)
                .ToList();

            if (originalTemplates.Count == 0)
            {
                foreach (LoadedPropertyTemplate template in OrderAddedLoadedPropertyTemplates(addedTemplates))
                    yield return template;
                yield break;
            }

            int protectedPrefixLength = CalculateProtectedLoadedPropertyPrefixLength(originalTemplates, functionTemplate);
            Dictionary<int, List<LoadedPropertyTemplate>> addedByInsertIndex = [];
            foreach (LoadedPropertyTemplate addedTemplate in addedTemplates)
            {
                int insertIndex = ResolveAddedLoadedPropertyInsertIndex(
                    addedTemplate,
                    originalTemplates,
                    protectedPrefixLength);
                if (!addedByInsertIndex.TryGetValue(insertIndex, out List<LoadedPropertyTemplate>? bucket))
                {
                    bucket = [];
                    addedByInsertIndex[insertIndex] = bucket;
                }
                bucket.Add(addedTemplate);
            }

            for (int i = 0; i < originalTemplates.Count; i++)
            {
                if (addedByInsertIndex.TryGetValue(i, out List<LoadedPropertyTemplate>? beforeOriginal))
                {
                    foreach (LoadedPropertyTemplate template in OrderAddedLoadedPropertyTemplates(beforeOriginal))
                        yield return template;
                }

                yield return originalTemplates[i];
            }

            if (addedByInsertIndex.TryGetValue(originalTemplates.Count, out List<LoadedPropertyTemplate>? tail))
            {
                foreach (LoadedPropertyTemplate template in OrderAddedLoadedPropertyTemplates(tail))
                    yield return template;
            }
        }

        private static void UpsertLoadedPropertyTemplate(
            IDictionary<string, LoadedPropertyTemplate> loadedProperties,
            LoadedPropertyTemplate incomingTemplate)
        {
            if (string.IsNullOrWhiteSpace(incomingTemplate.PropertyName))
                return;

            LoadedPropertyTemplate template = BlueprintModelCloner.Clone(incomingTemplate);
            if (loadedProperties.TryGetValue(template.PropertyName, out LoadedPropertyTemplate? existingTemplate))
            {
                if (existingTemplate.OriginalOrdinal >= 0 && template.OriginalOrdinal < 0)
                    template.OriginalOrdinal = existingTemplate.OriginalOrdinal;
                if (existingTemplate.CreatedSequence != 0 && template.CreatedSequence == 0)
                    template.CreatedSequence = existingTemplate.CreatedSequence;
                if (existingTemplate.FirstUseOrdinal != int.MaxValue && template.FirstUseOrdinal == int.MaxValue)
                    template.FirstUseOrdinal = existingTemplate.FirstUseOrdinal;
                template.IsUserCreated |= existingTemplate.IsUserCreated;
            }

            loadedProperties[template.PropertyName] = template;
        }

        private static Dictionary<string, int> CollectFunctionFieldFirstUseOrdinals(
            JsonArray rebuiltScriptJson,
            IEnumerable<string> candidatePropertyNames)
        {
            HashSet<string> candidates = new(candidatePropertyNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
            Dictionary<string, int> firstUseOrdinals = new(StringComparer.Ordinal);
            int ordinal = 0;
            CollectFunctionFieldFirstUseOrdinalsRecursive(rebuiltScriptJson, candidates, firstUseOrdinals, ref ordinal);
            return firstUseOrdinals;
        }

        private static void CollectFunctionFieldFirstUseOrdinalsRecursive(
            JsonNode? node,
            ISet<string> candidatePropertyNames,
            IDictionary<string, int> firstUseOrdinals,
            ref int ordinal)
        {
            if (node is JsonObject obj)
            {
                if (IsFieldPathObject(obj, out string fieldName))
                {
                    if (candidatePropertyNames.Contains(fieldName) && !firstUseOrdinals.ContainsKey(fieldName))
                        firstUseOrdinals[fieldName] = ordinal;
                    ordinal++;
                }

                foreach ((_, JsonNode? child) in obj)
                    CollectFunctionFieldFirstUseOrdinalsRecursive(child, candidatePropertyNames, firstUseOrdinals, ref ordinal);
                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                    CollectFunctionFieldFirstUseOrdinalsRecursive(child, candidatePropertyNames, firstUseOrdinals, ref ordinal);
            }
        }

        private static bool IsFieldPathObject(JsonObject obj, out string fieldName)
        {
            fieldName = string.Empty;
            string typeName = obj["$type"]?.GetValue<string>() ?? string.Empty;
            if (!typeName.EndsWith("FFieldPath, UAssetAPI", StringComparison.Ordinal) ||
                obj["Path"] is not JsonArray pathArray ||
                pathArray.Count == 0)
            {
                return false;
            }

            for (int i = pathArray.Count - 1; i >= 0; i--)
            {
                string? candidate = pathArray[i]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    fieldName = candidate;
                    return true;
                }
            }

            return false;
        }

        private static int CalculateProtectedLoadedPropertyPrefixLength(
            IReadOnlyList<LoadedPropertyTemplate> originalTemplates,
            BlueprintFunctionTemplate? functionTemplate)
        {
            int prefixLength = 0;
            HashSet<string> signatureNames = new(StringComparer.Ordinal);
            if (functionTemplate != null)
            {
                foreach (string parameterName in functionTemplate.ParameterNames)
                {
                    if (!string.IsNullOrWhiteSpace(parameterName))
                        signatureNames.Add(parameterName);
                }
                foreach (string returnName in functionTemplate.ReturnValueNames)
                {
                    if (!string.IsNullOrWhiteSpace(returnName))
                        signatureNames.Add(returnName);
                }
            }

            foreach (LoadedPropertyTemplate template in originalTemplates)
            {
                if (!signatureNames.Contains(template.PropertyName) &&
                    !PropertyTemplateHasFlag(template.PropertyTemplateJson, "CPF_Parm"))
                {
                    break;
                }

                prefixLength++;
            }

            return prefixLength;
        }

        private static int ResolveAddedLoadedPropertyInsertIndex(
            LoadedPropertyTemplate addedTemplate,
            IReadOnlyList<LoadedPropertyTemplate> originalTemplates,
            int protectedPrefixLength)
        {
            if (addedTemplate.FirstUseOrdinal == int.MaxValue)
                return originalTemplates.Count;

            for (int i = protectedPrefixLength; i < originalTemplates.Count; i++)
            {
                int originalFirstUse = originalTemplates[i].FirstUseOrdinal;
                if (originalFirstUse != int.MaxValue && originalFirstUse > addedTemplate.FirstUseOrdinal)
                    return i;
            }

            return originalTemplates.Count;
        }

        private static IEnumerable<LoadedPropertyTemplate> OrderAddedLoadedPropertyTemplates(IEnumerable<LoadedPropertyTemplate> templates)
        {
            return templates
                .OrderBy(template => template.FirstUseOrdinal)
                .ThenBy(template => template.CreatedSequence == 0 ? long.MaxValue : template.CreatedSequence)
                .ThenBy(template => template.PropertyName, StringComparer.Ordinal);
        }

        private static bool PropertyTemplateHasFlag(string propertyTemplateJson, string flagName)
        {
            if (string.IsNullOrWhiteSpace(propertyTemplateJson) || string.IsNullOrWhiteSpace(flagName))
                return false;

            try
            {
                if (JsonNode.Parse(propertyTemplateJson) is not JsonObject obj)
                    return false;
                string flags = obj["PropertyFlags"]?.GetValue<string>() ?? string.Empty;
                return flags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Any(flag => string.Equals(flag, flagName, StringComparison.Ordinal));
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        private static void ValidateAssignmentOpcodesForFunction(
            string functionName,
            IEnumerable<FProperty> loadedProperties,
            JsonArray rebuiltScriptJson)
        {
            Dictionary<string, string> serializedTypeByPropertyName = loadedProperties
                .Where(property => property?.Name != null && property.SerializedType != null)
                .ToDictionary(
                    property => property.Name.ToString(),
                    property => property.SerializedType.ToString(),
                    StringComparer.Ordinal);

            List<string> errors = [];
            ValidateAssignmentOpcodesRecursive(rebuiltScriptJson, serializedTypeByPropertyName, errors);
            if (errors.Count == 0)
                return;

            throw new InvalidOperationException(
                $"Assignment opcode validation failed for function '{functionName}':\n" +
                string.Join("\n", errors));
        }

        private static void ValidateAssignmentOpcodesRecursive(
            JsonNode? node,
            IReadOnlyDictionary<string, string> serializedTypeByPropertyName,
            ICollection<string> errors)
        {
            if (node is JsonObject obj)
            {
                string expressionType = SimplifyExpressionType(obj["$type"]?.GetValue<string>() ?? string.Empty);
                if (expressionType == "LetBool")
                {
                    string targetName = ResolveAssignmentTargetName(obj);
                    if (serializedTypeByPropertyName.TryGetValue(targetName, out string? serializedType) &&
                        !string.Equals(serializedType, "BoolProperty", StringComparison.Ordinal))
                    {
                        errors.Add($"EX_LetBool targets non-bool field '{targetName}' ({serializedType}).");
                    }
                }
                else if (expressionType == "Let" && obj["Value"] == null)
                {
                    string targetName = ResolveAssignmentTargetName(obj);
                    errors.Add($"EX_Let for field '{targetName}' is missing Value property pointer.");
                }

                foreach ((_, JsonNode? child) in obj)
                    ValidateAssignmentOpcodesRecursive(child, serializedTypeByPropertyName, errors);
                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                    ValidateAssignmentOpcodesRecursive(child, serializedTypeByPropertyName, errors);
            }
        }

        private static Dictionary<Guid, List<Guid>> BuildNodeAdjacency(BlueprintData blueprintData)
        {
            Dictionary<Guid, List<Guid>> adjacency = [];
            foreach (ConnectionData connection in blueprintData.Connections)
            {
                if (!adjacency.TryGetValue(connection.FromNodeId, out List<Guid>? fromList))
                {
                    fromList = [];
                    adjacency[connection.FromNodeId] = fromList;
                }

                if (!fromList.Contains(connection.ToNodeId))
                    fromList.Add(connection.ToNodeId);

                if (!adjacency.TryGetValue(connection.ToNodeId, out List<Guid>? toList))
                {
                    toList = [];
                    adjacency[connection.ToNodeId] = toList;
                }

                if (!toList.Contains(connection.FromNodeId))
                    toList.Add(connection.FromNodeId);
            }

            return adjacency;
        }

        private static bool NodeBelongsToFunction(
            SerializableNode node,
            string functionName,
            IReadOnlyDictionary<Guid, SerializableNode> nodeById,
            IReadOnlyDictionary<Guid, List<Guid>> adjacency)
        {
            if (node.MetaData.TryGetValue("FunctionName", out string? directFunctionName) &&
                string.Equals(directFunctionName, functionName, StringComparison.Ordinal))
            {
                return true;
            }

            Queue<Guid> pending = new();
            HashSet<Guid> visited = [];
            pending.Enqueue(node.Id);
            visited.Add(node.Id);

            while (pending.Count > 0)
            {
                Guid currentId = pending.Dequeue();
                if (!nodeById.TryGetValue(currentId, out SerializableNode? currentNode))
                    continue;

                if (currentNode.MetaData.TryGetValue("FunctionName", out string? currentFunctionName) &&
                    string.Equals(currentFunctionName, functionName, StringComparison.Ordinal))
                {
                    return true;
                }

                if (TryResolveEntryFunctionName(currentNode, out string entryFunctionName) &&
                    string.Equals(entryFunctionName, functionName, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!adjacency.TryGetValue(currentId, out List<Guid>? neighbors))
                    continue;

                foreach (Guid neighborId in neighbors)
                {
                    if (visited.Add(neighborId))
                        pending.Enqueue(neighborId);
                }
            }

            return false;
        }

        private static bool TryResolveVariableLoadedPropertyTemplate(
            BlueprintAssetContext assetContext,
            string functionName,
            SerializableNode node,
            out VariableLoadedPropertyResolution? resolution)
        {
            resolution = null;
            if (!IsGenericVariableDefinitionName(node.DefinitionName))
                return false;

            string variableName = ResolveNodeEditorText(node).Trim();
            if (string.IsNullOrWhiteSpace(variableName))
                return false;

            if (assetContext.FunctionTemplates.TryGetValue(functionName, out BlueprintFunctionTemplate? functionTemplate) &&
                functionTemplate.LoadedPropertyTemplatesByName.TryGetValue(variableName, out LoadedPropertyTemplate? functionPropertyTemplate))
            {
                resolution = new VariableLoadedPropertyResolution
                {
                    Template = functionPropertyTemplate,
                    OwnerKind = "Function"
                };
                return true;
            }

            if (node.MetaData.TryGetValue("FunctionName", out string? nodeFunctionName) &&
                !string.IsNullOrWhiteSpace(nodeFunctionName) &&
                assetContext.FunctionTemplates.TryGetValue(nodeFunctionName, out BlueprintFunctionTemplate? nodeFunctionTemplate) &&
                nodeFunctionTemplate.LoadedPropertyTemplatesByName.TryGetValue(variableName, out LoadedPropertyTemplate? nodeFunctionPropertyTemplate))
            {
                resolution = new VariableLoadedPropertyResolution
                {
                    Template = nodeFunctionPropertyTemplate,
                    OwnerKind = "Function"
                };
                return true;
            }

            if (assetContext.ClassLoadedPropertyTemplatesByName.TryGetValue(variableName, out LoadedPropertyTemplate? classTemplate))
            {
                resolution = new VariableLoadedPropertyResolution
                {
                    Template = classTemplate,
                    OwnerKind = "Class"
                };
                return true;
            }

            foreach (BlueprintFunctionTemplate candidateTemplate in assetContext.FunctionTemplates.Values)
            {
                if (candidateTemplate.LoadedPropertyTemplatesByName.TryGetValue(variableName, out LoadedPropertyTemplate? fallbackTemplate))
                {
                    resolution = new VariableLoadedPropertyResolution
                    {
                        Template = fallbackTemplate,
                        OwnerKind = "Function"
                    };
                    return true;
                }
            }

            return false;
        }

        private static void EnsureTypedDependsMapLength(UAsset asset)
        {
            asset.DependsMap ??= [];
            while (asset.DependsMap.Count < asset.Exports.Count)
            {
                asset.DependsMap.Add(Array.Empty<int>());
            }
            while (asset.DependsMap.Count > asset.Exports.Count)
            {
                asset.DependsMap.RemoveAt(asset.DependsMap.Count - 1);
            }
        }
    }
}
