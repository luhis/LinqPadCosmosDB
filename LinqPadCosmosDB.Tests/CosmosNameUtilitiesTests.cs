using LinqPadCosmosDB;
using Xunit;

namespace LinqPadCosmosDB.Tests;

public class CosmosNameUtilitiesTests
{
    [Fact]
    public void ToPropertyName_ShouldNormalizeLeadingDigitAndSeparators()
    {
        var result = CosmosNameUtilities.ToPropertyName("1st-name");

        Assert.Equal("_1stName", result);
    }

    [Fact]
    public void ToPropertyName_ShouldFallbackToValue_WhenNameHasNoLettersOrDigits()
    {
        var result = CosmosNameUtilities.ToPropertyName("---");

        Assert.Equal("Value", result);
    }

    [Fact]
    public void ToClassName_ShouldConvertToPascalCase()
    {
        var result = CosmosNameUtilities.ToClassName("order-items");

        Assert.Equal("EntityOrderItems", result);
    }

    [Fact]
    public void ToClassName_ShouldAppendItem_WhenNoValidCharactersArePresent()
    {
        var result = CosmosNameUtilities.ToClassName("---");

        Assert.Equal("EntityItem", result);
    }

    [Fact]
    public void EncodeContainerPropertyName_ShouldEncodeNonAlphanumericCharacters()
    {
        var result = CosmosNameUtilities.EncodeContainerPropertyName("orders-v2");

        Assert.Equal("C_orders_2Dv2", result);
    }

    [Fact]
    public void ToTypedClassName_ShouldIncludeContainerAndType()
    {
        var result = CosmosNameUtilities.ToTypedClassName("orders", "domain.invoice");

        Assert.Equal("EntityOrdersDomainInvoice", result);
    }

    [Fact]
    public void ToContainerPropertyName_ShouldCreateReadableName()
    {
        var result = CosmosNameUtilities.ToContainerPropertyName("AutoTestContext Club");

        Assert.Equal("AutoTestContextClub", result);
    }

    [Fact]
    public void ToTypedContainerPropertyName_ShouldCreateReadableName()
    {
        var result = CosmosNameUtilities.ToTypedContainerPropertyName("AutoTestContext", "Club");

        Assert.Equal("AutoTestContextClub", result);
    }

    [Fact]
    public void EncodeTypedContainerPropertyName_ShouldIncludeContainerAndTypeSafely()
    {
        var result = CosmosNameUtilities.EncodeTypedContainerPropertyName("orders", "domain.invoice");

        Assert.Equal("C_orders_20domain_2Einvoice", result);
    }
}
