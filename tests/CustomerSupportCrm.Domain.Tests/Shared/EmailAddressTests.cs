using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Shared;

namespace CustomerSupportCrm.Domain.Tests.Shared;

public sealed class EmailAddressTests
{
    [Fact]
    public void NormalizesToTrimmedLowerCase()
    {
        Assert.Equal("user@example.com", EmailAddress.Create(" User@Example.com ").Value);
    }

    [Fact]
    public void EqualByValue()
    {
        Assert.Equal(EmailAddress.Create("a@b.co"), EmailAddress.Create("A@B.CO"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@localhost")]
    [InlineData("a@b@c.com")]
    [InlineData("us er@example.com")]
    public void RejectsMalformedAddresses(string? value)
    {
        var exception = Assert.Throws<DomainException>(() => EmailAddress.Create(value));

        Assert.Equal(EmailAddress.InvalidCode, exception.Code);
    }
}
