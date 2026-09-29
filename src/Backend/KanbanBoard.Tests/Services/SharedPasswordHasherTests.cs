using KanbanBoard.Api.Services;

namespace KanbanBoard.Tests.Services;

public class SharedPasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_RoundTrips_WithRandomSalt()
    {
        var a = SharedPasswordHasher.Hash("s3cret-pass", 10_000);
        var b = SharedPasswordHasher.Hash("s3cret-pass", 10_000);

        Assert.NotEqual(a, b);
        Assert.StartsWith("pbkdf2-sha256$10000$", a);
        Assert.True(SharedPasswordHasher.Verify("s3cret-pass", a));
        Assert.False(SharedPasswordHasher.Verify("s3cret-pasS", a));
        Assert.False(SharedPasswordHasher.Verify("", a));
        Assert.False(SharedPasswordHasher.Verify(null, a));
    }

    [Fact]
    public void DefaultIterations_FollowOwasp()
    {
        Assert.Contains("$600000$", SharedPasswordHasher.Hash("longenough"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plaintext-password")]
    [InlineData("pbkdf2-sha256$100$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // too few iterations
    [InlineData("pbkdf2-sha256$600000$not-base64$AAAA")]
    [InlineData("bcrypt$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void MalformedHashes_NeverVerify(string? stored)
    {
        Assert.False(SharedPasswordHasher.IsWellFormed(stored));
        Assert.False(SharedPasswordHasher.Verify("anything", stored));
    }

    [Fact]
    public void Fingerprint_ChangesWithTheHash()
    {
        var a = SharedPasswordHasher.Hash("one-password", 10_000);
        var b = SharedPasswordHasher.Hash("one-password", 10_000);
        Assert.Equal(16, SharedPasswordHasher.Fingerprint(a).Length);
        Assert.Equal(SharedPasswordHasher.Fingerprint(a), SharedPasswordHasher.Fingerprint(a));
        Assert.NotEqual(SharedPasswordHasher.Fingerprint(a), SharedPasswordHasher.Fingerprint(b));
    }
}
