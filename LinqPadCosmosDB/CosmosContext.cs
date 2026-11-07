using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace LinqPadCosmosDB;

public class CosmosContext : IDisposable
{
    private bool disposed;

    public CosmosClient Client { get; }
    public Database Database { get; }

    public CosmosContext(string endpoint, string key, string database)
    {
        Client = new CosmosClient(NormalizeEndpoint(endpoint), key);
        Database = Client.GetDatabase(database);
    }

    public IQueryable<T> Query<T>(string container, QueryRequestOptions? options = null)
    {
        ThrowIfDisposed();
        return Database.GetContainer(container).GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true, requestOptions: options);
    }

    public IQueryable<JObject> Documents(string container) => Query<JObject>(container);

    public async Task<List<T>> Sql<T>(string container, QueryDefinition query, QueryRequestOptions? options = null)
    {
        ThrowIfDisposed();

        using var iterator = Database.GetContainer(container).GetItemQueryIterator<T>(query, requestOptions: options);
        var result = new List<T>();
        while (iterator.HasMoreResults)
            result.AddRange(await iterator.ReadNextAsync().ConfigureAwait(false));
        return result;
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var value = endpoint.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value;

        if (uri.AbsolutePath is "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            return value;

        return new UriBuilder(uri.Scheme, uri.Host, uri.Port)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.AbsoluteUri;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(CosmosContext));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Client.Dispose();
        GC.SuppressFinalize(this);
    }
}
