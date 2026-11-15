using System.Reflection;
using System.Text;
using LINQPad.Extensibility.DataContext;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LinqPadCosmosDB;

public class CosmosDriver : DynamicDataContextDriver
{
    internal enum InferredKind
    {
        Null,
        Boolean,
        Integer,
        Decimal,
        Double,
        DateTime,
        Guid,
        String,
        Object,
        Array,
        Mixed
    }

    internal sealed class InferredField
    {
        private InferredKind arrayElementKind = InferredKind.Null;
        private bool isArrayElementOptional;
        private Dictionary<string, InferredField>? objectFields;
        private Dictionary<string, InferredField>? arrayObjectFields;

        public InferredKind Kind { get; private set; }
        public bool IsOptional { get; private set; }
        public bool HasObjectFields => objectFields is { Count: > 0 };
        public bool HasArrayObjectFields => arrayObjectFields is { Count: > 0 };
        public IReadOnlyDictionary<string, InferredField>? ObjectFields => objectFields;
        public IReadOnlyDictionary<string, InferredField>? ArrayObjectFields => arrayObjectFields;

        public InferredField(InferredKind kind)
        {
            Kind = kind;
        }

        public void MarkOptional() => IsOptional = true;

        public void Merge(JToken token)
        {
            var nextKind = InferKind(token);
            Kind = MergeKinds(Kind, nextKind);

            if (nextKind == InferredKind.Object && token is JObject nestedObject)
            {
                objectFields ??= new Dictionary<string, InferredField>(StringComparer.Ordinal);
                MergeFieldsFromObject(objectFields, nestedObject);
            }

            if (nextKind == InferredKind.Array && token is JArray array)
                MergeArrayElementKind(array);
        }

        private void MergeArrayElementKind(JArray array)
        {
            foreach (var item in array)
            {
                if (item.Type is JTokenType.Null or JTokenType.Undefined)
                {
                    isArrayElementOptional = true;
                    continue;
                }

                var itemKind = InferKind(item);
                arrayElementKind = MergeKinds(arrayElementKind, itemKind);

                if (itemKind == InferredKind.Object && item is JObject nestedObject)
                {
                    arrayObjectFields ??= new Dictionary<string, InferredField>(StringComparer.Ordinal);
                    MergeFieldsFromObject(arrayObjectFields, nestedObject);
                }
            }
        }

        public string GetClrTypeName(string? objectTypeName = null, string? arrayObjectElementTypeName = null)
        {
            if (Kind == InferredKind.Array)
                return $"System.Collections.Generic.List<{GetArrayElementClrTypeName(arrayObjectElementTypeName)}>";

            if (Kind == InferredKind.Object && !string.IsNullOrWhiteSpace(objectTypeName))
                return objectTypeName;

            return GetClrTypeNameForKind(Kind, IsOptional);
        }

        private string GetArrayElementClrTypeName(string? arrayObjectElementTypeName)
        {
            if (arrayElementKind is InferredKind.Null or InferredKind.Mixed)
                return "object?";

            if (arrayElementKind == InferredKind.Object && !string.IsNullOrWhiteSpace(arrayObjectElementTypeName))
                return arrayObjectElementTypeName;

            var elementType = GetClrTypeNameForKind(arrayElementKind, isArrayElementOptional);
            if (isArrayElementOptional && !IsValueTypeKind(arrayElementKind) && !elementType.EndsWith("?", StringComparison.Ordinal))
                return elementType + "?";

            return elementType;
        }

        private static string GetClrTypeNameForKind(InferredKind kind, bool optional)
        {
            var typeName = kind switch
            {
                InferredKind.Boolean => "bool",
                InferredKind.Integer => "long",
                InferredKind.Decimal => "decimal",
                InferredKind.Double => "double",
                InferredKind.DateTime => "System.DateTime",
                InferredKind.Guid => "System.Guid",
                InferredKind.Object => "System.Collections.Generic.Dictionary<string, object?>",
                InferredKind.Array => "System.Collections.Generic.List<object?>",
                _ => "string"
            };

            if (optional && IsValueTypeKind(kind))
                return typeName + "?";

            return typeName;
        }
    }

    public override string Name => "Azure Cosmos DB for NoSQL";
    public override string Author => "LinqPadCosmosDB";

    private static string ToContainerPropertyName(string container) => CosmosNameUtilities.ToContainerPropertyName(container);

    private static string ToClassName(string container) => CosmosNameUtilities.ToClassName(container);

    private static string ToPropertyName(string name) => CosmosNameUtilities.ToPropertyName(name);

    private static bool IsValueTypeKind(InferredKind kind) =>
        kind is InferredKind.Boolean or InferredKind.Integer or InferredKind.Decimal or InferredKind.Double or InferredKind.DateTime or InferredKind.Guid;

    private static InferredKind InferKind(JToken token)
    {
        if (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            return InferredKind.Null;

        return token.Type switch
        {
            JTokenType.Boolean => InferredKind.Boolean,
            JTokenType.Integer => InferredKind.Integer,
            JTokenType.Float => InferNumericKind((JValue)token),
            JTokenType.Date => InferredKind.DateTime,
            JTokenType.Guid => InferredKind.Guid,
            JTokenType.Object => InferredKind.Object,
            JTokenType.Array => InferredKind.Array,
            JTokenType.String => InferStringKind((string?)((JValue)token).Value),
            _ => InferredKind.String
        };
    }

    private static InferredKind InferStringKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return InferredKind.String;

        if (DateTime.TryParse(value, out _))
            return InferredKind.DateTime;

        if (Guid.TryParse(value, out _))
            return InferredKind.Guid;

        return InferredKind.String;
    }

    private static InferredKind InferNumericKind(JValue token)
    {
        if (token.Value is decimal)
            return InferredKind.Decimal;

        if (token.Value is float or double)
            return InferredKind.Double;

        return InferredKind.Integer;
    }

    private static InferredKind MergeKinds(InferredKind current, InferredKind next)
    {
        if (next == InferredKind.Null)
            return current;
        if (current == InferredKind.Null)
            return next;
        if (current == next)
            return current;

        if ((current == InferredKind.Integer && next == InferredKind.Decimal) || (current == InferredKind.Decimal && next == InferredKind.Integer))
            return InferredKind.Decimal;

        if ((current is InferredKind.Integer or InferredKind.Decimal && next == InferredKind.Double) ||
            (next is InferredKind.Integer or InferredKind.Decimal && current == InferredKind.Double))
            return InferredKind.Double;

        if ((current == InferredKind.DateTime && next == InferredKind.String) || (current == InferredKind.String && next == InferredKind.DateTime))
            return InferredKind.String;

        if ((current == InferredKind.Guid && next == InferredKind.String) || (current == InferredKind.String && next == InferredKind.Guid))
            return InferredKind.String;

        return InferredKind.Mixed;
    }

    private sealed class ContainerProjection
    {
        public string ContainerId { get; }
        public string? TypeValue { get; }
        public Dictionary<string, InferredField> Fields { get; }

        public ContainerProjection(string containerId, string? typeValue, Dictionary<string, InferredField> fields)
        {
            ContainerId = containerId;
            TypeValue = typeValue;
            Fields = fields;
        }
    }

    private sealed class GeneratedQueryable
    {
        public string ContainerId { get; }
        public string EntityName { get; }
        public string PropertyName { get; }
        public string? TypeValue { get; }
        public string? TypePropertyName { get; }

        public GeneratedQueryable(string containerId, string entityName, string propertyName, string? typeValue, string? typePropertyName)
        {
            ContainerId = containerId;
            EntityName = entityName;
            PropertyName = propertyName;
            TypeValue = typeValue;
            TypePropertyName = typePropertyName;
        }
    }

    private static List<JObject> SampleContainerDocuments(CosmosContext context, string containerId, int sampleSize)
    {
        var documents = new List<JObject>();
        var container = context.Database.GetContainer(containerId);
        var options = new QueryRequestOptions
        {
            MaxItemCount = Math.Min(sampleSize, 100)
        };

        using var iterator = container.GetItemQueryIterator<JObject>(requestOptions: options);
        while (iterator.HasMoreResults && documents.Count < sampleSize)
        {
            var page = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var document in page)
            {
                documents.Add(document);
                if (documents.Count >= sampleSize)
                    break;
            }
        }

        return documents;
    }

    private static void MergeFieldsFromObject(Dictionary<string, InferredField> fields, JObject document)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.Properties())
        {
            seen.Add(property.Name);
            var kind = InferKind(property.Value);
            if (!fields.TryGetValue(property.Name, out var field))
            {
                field = new InferredField(kind);
                fields[property.Name] = field;
            }

            if (kind == InferredKind.Null)
            {
                field.MarkOptional();
            }
            else
            {
                field.Merge(property.Value);
            }
        }

        foreach (var knownField in fields)
        {
            if (!seen.Contains(knownField.Key))
                knownField.Value.MarkOptional();
        }
    }

    private static Dictionary<string, InferredField> InferFieldsFromDocuments(IReadOnlyList<JObject> documents)
    {
        var fields = new Dictionary<string, InferredField>(StringComparer.Ordinal);
        foreach (var document in documents)
            MergeFieldsFromObject(fields, document);

        return fields;
    }

    private static List<ContainerProjection> InferContainerProjections(CosmosContext context, string containerId, int sampleSize)
    {
        var documents = SampleContainerDocuments(context, containerId, sampleSize);
        var byType = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (document["$type"] is JValue { Type: JTokenType.String } typeToken && typeToken.Value is string typeValue && !string.IsNullOrWhiteSpace(typeValue))
            {
                if (!byType.TryGetValue(typeValue, out var list))
                {
                    list = new List<JObject>();
                    byType[typeValue] = list;
                }

                list.Add(document);
            }
        }

        if (byType.Count == 0)
            return new List<ContainerProjection> { new(containerId, null, InferFieldsFromDocuments(documents)) };

        var projections = new List<ContainerProjection>();
        foreach (var pair in byType.OrderBy(x => x.Key, StringComparer.Ordinal))
            projections.Add(new ContainerProjection(containerId, pair.Key, InferFieldsFromDocuments(pair.Value)));

        return projections;
    }

    private static void EnsureSystemFields(Dictionary<string, InferredField> fields)
    {
        if (!fields.ContainsKey("id"))
            fields["id"] = new InferredField(InferredKind.String);

        if (!fields.ContainsKey("_etag"))
        {
            var etag = new InferredField(InferredKind.String);
            etag.MarkOptional();
            fields["_etag"] = etag;
        }
    }

    private static string EnsureUniqueName(HashSet<string> existingNames, string baseName)
    {
        var name = baseName;
        var suffix = 2;
        while (!existingNames.Add(name))
        {
            name = baseName + suffix;
            suffix++;
        }

        return name;
    }

    private static Dictionary<string, string> GetFieldPropertyNames(Dictionary<string, InferredField> fields)
    {
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        var fieldProperties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields.OrderBy(x => x.Key, StringComparer.Ordinal))
            fieldProperties[field.Key] = EnsureUniqueName(propertyNames, ToPropertyName(field.Key));

        return fieldProperties;
    }

    private static Dictionary<string, string> AppendEntityClass(StringBuilder source, string className, Dictionary<string, InferredField> fields, HashSet<string> usedEntityNames)
    {
        var fieldProperties = GetFieldPropertyNames(fields);
        var objectTypeNamesByField = new Dictionary<string, string>(StringComparer.Ordinal);
        var arrayObjectTypeNamesByField = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in fields.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var propertyName = fieldProperties[field.Key];

            if (field.Value.HasObjectFields && field.Value.ObjectFields is not null)
            {
                var nestedTypeName = EnsureUniqueName(usedEntityNames, className + propertyName);
                AppendEntityClass(source, nestedTypeName, new Dictionary<string, InferredField>(field.Value.ObjectFields, StringComparer.Ordinal), usedEntityNames);
                objectTypeNamesByField[field.Key] = nestedTypeName;
            }

            if (field.Value.HasArrayObjectFields && field.Value.ArrayObjectFields is not null)
            {
                var nestedTypeName = EnsureUniqueName(usedEntityNames, className + propertyName + "Item");
                AppendEntityClass(source, nestedTypeName, new Dictionary<string, InferredField>(field.Value.ArrayObjectFields, StringComparer.Ordinal), usedEntityNames);
                arrayObjectTypeNamesByField[field.Key] = nestedTypeName;
            }
        }

        source.AppendLine($"public class {className} {{");
        foreach (var field in fields.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            objectTypeNamesByField.TryGetValue(field.Key, out var objectTypeName);
            arrayObjectTypeNamesByField.TryGetValue(field.Key, out var arrayObjectTypeName);
            source.AppendLine($"[Newtonsoft.Json.JsonProperty({JsonConvert.ToString(field.Key)})]");
            source.AppendLine($"public {field.Value.GetClrTypeName(objectTypeName, arrayObjectTypeName)} {fieldProperties[field.Key]} {{ get; set; }}");
        }
        source.AppendLine("}");

        return fieldProperties;
    }

    public override string GetConnectionDescription(IConnectionInfo cxInfo) =>
        $"Cosmos DB: {(string?)cxInfo.DriverData.Element("Endpoint")} / {(string?)cxInfo.DriverData.Element("Database")}";

    public override bool ShowConnectionDialog(IConnectionInfo cxInfo, ConnectionDialogOptions dialogOptions) =>
        new ConnectionDialog(cxInfo).ShowDialog() == true;

    public override IEnumerable<string> GetAssembliesToAdd(IConnectionInfo cxInfo) =>
        new[] { typeof(CosmosContext).Assembly.Location, typeof(CosmosClient).Assembly.Location, typeof(JObject).Assembly.Location };

    public override IEnumerable<string> GetNamespacesToAdd(IConnectionInfo cxInfo) =>
        new[] { "LinqPadCosmosDB", "Microsoft.Azure.Cosmos", "Microsoft.Azure.Cosmos.Linq", "Newtonsoft.Json.Linq" };

    public override ParameterDescriptor[] GetContextConstructorParameters(IConnectionInfo cxInfo) =>
        new[] { new ParameterDescriptor("endpoint", "System.String"), new ParameterDescriptor("key", "System.String"), new ParameterDescriptor("database", "System.String") };

    public override object[] GetContextConstructorArguments(IConnectionInfo cxInfo) =>
        new object[] { (string?)cxInfo.DriverData.Element("Endpoint") ?? "", cxInfo.Decrypt((string?)cxInfo.DriverData.Element("Key") ?? ""), (string?)cxInfo.DriverData.Element("Database") ?? "" };

    public override void TearDownContext(IConnectionInfo cxInfo, object context, QueryExecutionManager executionManager, object[] constructorArguments)
    {
        if (context is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        if (context is IDisposable disposable)
            disposable.Dispose();
    }

    public override List<ExplorerItem> GetSchemaAndBuildAssembly(IConnectionInfo cxInfo, AssemblyName assemblyToBuild, ref string nameSpace, ref string typeName)
    {
        var args = GetContextConstructorArguments(cxInfo);
        using var context = new CosmosContext((string)args[0], (string)args[1], (string)args[2]);
        var containers = new List<string>();
        using (var iterator = context.Database.GetContainerQueryIterator<ContainerProperties>())
            while (iterator.HasMoreResults)
                containers.AddRange(iterator.ReadNextAsync().GetAwaiter().GetResult().Select(c => c.Id));
        containers.Sort(StringComparer.Ordinal);

        const int sampleSize = 64;
        var source = new StringBuilder();
        source.AppendLine($"namespace {nameSpace} {{");
        var items = new List<ExplorerItem>();
        var usedEntityNames = new HashSet<string>(StringComparer.Ordinal);
        var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        var queryables = new List<GeneratedQueryable>();

        for (var i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            var projections = InferContainerProjections(context, container, sampleSize);
            for (var projectionIndex = 0; projectionIndex < projections.Count; projectionIndex++)
            {
                var projection = projections[projectionIndex];
                EnsureSystemFields(projection.Fields);

                var entityBaseName = projection.TypeValue is null
                    ? ToClassName(container)
                    : CosmosNameUtilities.ToTypedClassName(container, projection.TypeValue);
                var entityName = EnsureUniqueName(usedEntityNames, entityBaseName);
                var fieldProperties = AppendEntityClass(source, entityName, projection.Fields, usedEntityNames);

                var propertyBaseName = projection.TypeValue is null
                    ? ToContainerPropertyName(container)
                    : CosmosNameUtilities.ToTypedContainerPropertyName(container, projection.TypeValue);
                var propertyName = EnsureUniqueName(usedPropertyNames, propertyBaseName);

                var typeProperty = projection.TypeValue is null
                    ? null
                    : fieldProperties.GetValueOrDefault("$type");

                queryables.Add(new GeneratedQueryable(container, entityName, propertyName, projection.TypeValue, typeProperty));

                items.Add(new ExplorerItem($"{propertyName} ({container}{(projection.TypeValue is null ? string.Empty : " / " + projection.TypeValue)})", ExplorerItemKind.QueryableObject, ExplorerIcon.Table)
                {
                    DragText = propertyName,
                    IsEnumerable = true
                });
            }
        }

        source.AppendLine($"public class {typeName} : LinqPadCosmosDB.CosmosContext {{");
        source.AppendLine($"public {typeName}(string endpoint, string key, string database) : base(endpoint, key, database) {{ }}");
        foreach (var queryable in queryables)
        {
            if (queryable.TypeValue is not null && queryable.TypePropertyName is not null)
            {
                source.AppendLine($"public System.Linq.IQueryable<{queryable.EntityName}> {queryable.PropertyName} => System.Linq.Queryable.Where(Query<{queryable.EntityName}>({JsonConvert.ToString(queryable.ContainerId)}), x => x.{queryable.TypePropertyName} == {JsonConvert.ToString(queryable.TypeValue)});");
            }
            else
            {
                source.AppendLine($"public System.Linq.IQueryable<{queryable.EntityName}> {queryable.PropertyName} => Query<{queryable.EntityName}>({JsonConvert.ToString(queryable.ContainerId)});");
            }
        }
        source.AppendLine("}");
        source.AppendLine("}");

#pragma warning disable SYSLIB0044
        var result = CompileSource(new CompilationInput
        {
            FilePathsToReference = GetCoreFxReferenceAssemblies(cxInfo).Concat(GetAssembliesToAdd(cxInfo)).Distinct().ToArray(),
            OutputPath = assemblyToBuild.CodeBase,
            SourceCode = new[] { source.ToString() }
        });
#pragma warning restore SYSLIB0044
        if (result.Errors.Length > 0)
            throw new InvalidOperationException("Could not compile Cosmos DB context: " + string.Join(Environment.NewLine, result.Errors));
        return items;
    }
}
