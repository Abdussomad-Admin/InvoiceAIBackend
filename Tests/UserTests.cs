using Domain.Common;
using Domain.Users;

namespace Tests;

public class UserTests
{
    [Fact]
    public void Register_WithValidDetails_CleansTheEmail()
    {
        var user = User.Register("  Ada@Example.COM ", "somehash", "Ada Obi");

        Assert.Equal("ada@example.com", user.Email);
        Assert.True(user.IsActive);
        Assert.False(user.IsEmailVerified);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("a@b@c.com")]
    [InlineData("ada@")]
    public void Register_WithInvalidEmail_Throws(string email)
    {
        Assert.Throws<DomainException>(
            () => User.Register(email, "somehash", "Ada Obi"));
    }

    [Fact]
    public void VerifyEmail_CalledTwice_KeepsTheFirstTime()
    {
        var user = User.Register("ada@example.com", "somehash", "Ada Obi");
        var firstTime = DateTimeOffset.UtcNow;

        user.VerifyEmail(firstTime);
        user.VerifyEmail(firstTime.AddDays(1));

        Assert.Equal(firstTime, user.EmailVerifiedAt);
    }
}