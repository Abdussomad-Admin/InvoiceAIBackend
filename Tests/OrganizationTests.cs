using Domain.Common;
using Domain.Organizations;

namespace Tests;

public class OrganizationTests
{
    [Fact]
    public void Create_WithValidDetails_SetsTheValues()
    {
        // Arrange and Act
        var org = Organization.Create("  Acme Ltd ", "ng", "ngn", "Africa/Lagos");

        // Assert
        Assert.Equal("Acme Ltd", org.Name);
        Assert.Equal("NG", org.CountryCode);
        Assert.Equal("NGN", org.BaseCurrencyCode);
        Assert.Equal(30, org.DefaultPaymentTermsDays);
    }

    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<DomainException>(
            () => Organization.Create("", "NG", "NGN", "Africa/Lagos"));
    }
}