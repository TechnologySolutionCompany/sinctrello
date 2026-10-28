using SincTrello.Infrastructure.Trello;
using Xunit;

namespace SincTrello.Infrastructure.Tests;

public sealed class TrelloCredentialTests
{
    [Fact]
    public void CredentialsRejectHeaderInjectionAndRedactTheirDescription()
    {
        Assert.Throws<ArgumentException>(() => new TrelloCredentials("key\r\ninject", "token"));
        Assert.Throws<ArgumentException>(() => new TrelloCredentials("key", "token\"inject"));
        Assert.DoesNotContain("private-token", new TrelloCredentials("key", "private-token").ToString());
    }

    [Fact]
    public void WindowsCredentialRoundTripAndRemovalUseAnIsolatedTarget()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsTrelloCredentialStore("SincTrello/Test/" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(store.Read());
            store.Save(new TrelloCredentials("test-key", "test-token"));
            var restored = Assert.IsType<TrelloCredentials>(store.Read());
            Assert.Equal("test-key", restored.ApiKey);
            Assert.Equal("test-token", restored.Token);
            store.Delete();
            Assert.Null(store.Read());
        }
        finally { store.Delete(); }
    }
}
