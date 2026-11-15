using LinqPadCosmosDB;
using Newtonsoft.Json.Linq;

namespace LinqPadCosmosDB.Tests;

public class CosmosDriverTests
{
    [Fact]
    public void InferredField_ShouldInferValueType_WhenFirstSampleIsNull()
    {
        var field = new CosmosDriver.InferredField(CosmosDriver.InferredKind.Null);
        field.MarkOptional();
        field.Merge(JToken.Parse("42"));

        Assert.Equal("long?", field.GetClrTypeName());
    }
}
