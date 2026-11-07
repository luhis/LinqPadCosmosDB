using System.Reflection;
using System.Text;
using LINQPad.Extensibility.DataContext;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LinqPadCosmosDB;

public class CosmosDriver : DynamicDataContextDriver
{
    public override string Name => "Azure Cosmos DB for NoSQL";
    public override string Author => "LinqPadCosmosDB";

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

    public override void TearDownContext(IConnectionInfo cxInfo, object context, QueryExecutionManager executionManager, object[] constructorArguments) =>
        ((IDisposable)context).Dispose();

    public override List<ExplorerItem> GetSchemaAndBuildAssembly(IConnectionInfo cxInfo, AssemblyName assemblyToBuild, ref string nameSpace, ref string typeName)
    {
        var args = GetContextConstructorArguments(cxInfo);
        using var context = new CosmosContext((string)args[0], (string)args[1], (string)args[2]);
        var containers = new List<string>();
        using (var iterator = context.Database.GetContainerQueryIterator<ContainerProperties>())
            while (iterator.HasMoreResults)
                containers.AddRange(iterator.ReadNextAsync().GetAwaiter().GetResult().Select(c => c.Id));
        containers.Sort(StringComparer.Ordinal);

        var source = new StringBuilder();
        source.AppendLine($"namespace {nameSpace} {{ public class {typeName} : LinqPadCosmosDB.CosmosContext {{");
        source.AppendLine($"public {typeName}(string endpoint, string key, string database) : base(endpoint, key, database) {{ }}");
        var items = new List<ExplorerItem>();
        for (var i = 0; i < containers.Count; i++)
        {
            var property = "C_" + string.Concat(Encoding.UTF8.GetBytes(containers[i]).Select(b =>
                b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
                    ? ((char)b).ToString() : $"_{b:X2}"));
            source.AppendLine($"public System.Linq.IQueryable<Newtonsoft.Json.Linq.JObject> {property} => Documents({JsonConvert.ToString(containers[i])});");
            items.Add(new ExplorerItem($"{property} ({containers[i]})", ExplorerItemKind.QueryableObject, ExplorerIcon.Table)
            {
                DragText = property,
                IsEnumerable = true
            });
        }
        source.AppendLine("} }");

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
