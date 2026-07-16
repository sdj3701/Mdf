#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

public sealed class NetworkManagerReconnectAiTakeoverEditModeTests
{
    private static MethodInfo IdentityValidator => typeof(NetworkManager).GetMethod(
        "ValidateDisconnectedAiTakeoverIdentity",
        BindingFlags.NonPublic | BindingFlags.Static);

    [TestCase(4, "token-a", 4, "token-a", false, false, true, null)]
    [TestCase(4, "token-a", 5, "token-a", false, false, false, "durable_player_id_mismatch")]
    [TestCase(4, "token-a", 4, "token-b", false, false, false, "durable_connection_token_mismatch")]
    [TestCase(4, "token-a", 4, "token-a", true, false, false, "input_authority_reassigned")]
    [TestCase(4, "token-a", 4, "token-a", false, true, false, "durable_player_reconnected")]
    public void DisconnectedAiTakeoverRequiresTheExpectedUnownedDurableIdentity(
        int expectedPlayerId,
        string expectedToken,
        int actualPlayerId,
        string cachedToken,
        bool hasInputAuthority,
        bool hasActiveConnection,
        bool expectedResult,
        string expectedReason)
    {
        Assert.That(IdentityValidator, Is.Not.Null);
        object[] arguments =
        {
            expectedPlayerId,
            expectedToken,
            actualPlayerId,
            cachedToken,
            hasInputAuthority,
            hasActiveConnection,
            null
        };

        bool result = (bool)IdentityValidator.Invoke(null, arguments);

        Assert.That(result, Is.EqualTo(expectedResult));
        Assert.That(arguments[6] as string, Is.EqualTo(expectedReason));
    }

    [Test]
    public void ReassociationCancelsTheMatchingRetryBeforeReleasingAi()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/NetworkManager.cs");
        int reassociateMethod = source.IndexOf("private bool TryReassociateDisconnectedPlayer", StringComparison.Ordinal);
        int cancelRetry = source.IndexOf(
            "CancelPendingDisconnectedAiTakeover(cachedData.PlayerId, token);",
            reassociateMethod,
            StringComparison.Ordinal);
        int releaseAi = source.IndexOf(
            "ReleaseDisconnectedAiTakeover(targetPlayer, joinedPlayer);",
            reassociateMethod,
            StringComparison.Ordinal);

        Assert.That(reassociateMethod, Is.GreaterThanOrEqualTo(0));
        Assert.That(cancelRetry, Is.GreaterThan(reassociateMethod));
        Assert.That(releaseAi, Is.GreaterThan(cancelRetry));
    }

    [Test]
    public void RetryLifecycleUsesGenerationAndCancelsForRunnerShutdownAndManagerDestroy()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/NetworkManager.cs");
        int retryMethod = source.IndexOf(
            "private IEnumerator RetryDisconnectedAiTakeover(PendingDisconnectedAiTakeover pending",
            StringComparison.Ordinal);
        int shutdownMethod = source.IndexOf("public void OnShutdown", StringComparison.Ordinal);
        int destroyMethod = source.IndexOf("private void OnDestroy", StringComparison.Ordinal);

        Assert.That(source, Does.Contain("Dictionary<int, PendingDisconnectedAiTakeover>"));
        Assert.That(source, Does.Contain("Generation = ++_disconnectedAiTakeoverGeneration"));
        Assert.That(source.IndexOf("if (!IsCurrentDisconnectedAiTakeover(pending))", retryMethod, StringComparison.Ordinal),
            Is.GreaterThan(retryMethod));
        Assert.That(source.IndexOf("pending.ConnectionToken", retryMethod, StringComparison.Ordinal),
            Is.GreaterThan(retryMethod));
        Assert.That(source.IndexOf("CancelPendingDisconnectedAiTakeoversForRunner(runner);", shutdownMethod, StringComparison.Ordinal),
            Is.GreaterThan(shutdownMethod));
        Assert.That(source.IndexOf("CancelAllPendingDisconnectedAiTakeovers();", destroyMethod, StringComparison.Ordinal),
            Is.GreaterThan(destroyMethod));
    }
}
#endif
