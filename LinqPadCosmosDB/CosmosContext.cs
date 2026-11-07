using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace LinqPadCosmosDB;

public class CosmosContext : IDisposable
{
    public CosmosClient Client { get; }
    public Database Database { get; }

    public CosmosContext(string endpoint, string key, string database)
    {
        Client = new CosmosClient(endpoint, key);
        Database = Client.GetDatabase(database);
    }

    public IQueryable<T> Query<T>(string container, QueryRequestOptions? options = null) =>
        Database.GetContainer(container).GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true, requestOptions: options);

    public IQueryable<JObject> Documents(string container) => Query<JObject>(container);

    public async Task<List<T>> Sql<T>(string container, QueryDefinition query, QueryRequestOptions? options = null)
    {
        using var iterator = Database.GetContainer(container).GetItemQueryIterator<T>(query, requestOptions: options);
        var result = new List<T>();
        while (iterator.HasMoreResults)
            result.AddRange(await iterator.ReadNextAsync().ConfigureAwait(false));
        return result;
    }

    public void Dispose() => Client.Dispose();
}
