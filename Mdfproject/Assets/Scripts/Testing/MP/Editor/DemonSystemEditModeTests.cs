using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

public sealed class DemonSystemEditModeTests
{
    private const BindingFlags AllMembers =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void CatalogDefinesFiveUniqueAllowListedDemonsUsingMonsterPortraits()
    {
        Assert.That(DemonSelectionCatalog.Entries.Count, Is.EqualTo(5));

        var hashes = new HashSet<int>();
        string[] allowedMonsterPortraits =
        {
            "Spr_Port_EvilMage",
            "Spr_Port_Orc",
            "Spr_Port_Bat",
            "Spr_Port_Skeleton",
            "Spr_Port_Golem"
        };

        foreach (DemonSelectionCatalog.Entry entry in DemonSelectionCatalog.Entries)
        {
            Assert.That(entry.KeyHash, Is.Not.EqualTo(0));
            Assert.That(hashes.Add(entry.KeyHash), Is.True, $"Duplicate demon hash: {entry.ContentId}");
            Assert.That(DemonSelectionCatalog.IsAllowedHash(entry.KeyHash), Is.True);
            Assert.That(allowedMonsterPortraits, Does.Contain(entry.IconKey));
            Assert.That(entry.PassiveHealthMultiplier, Is.GreaterThanOrEqualTo(1f));
            Assert.That(entry.PassiveMoveSpeedMultiplier, Is.GreaterThanOrEqualTo(1f));
            Assert.That(entry.PassiveDamageMultiplier, Is.GreaterThanOrEqualTo(1f));
            Assert.That(entry.SkillName, Is.Not.Empty);
            Assert.That(entry.SkillDescription, Is.Not.Empty);
        }
    }

    [Test]
    public void LobbyDemonChoiceIsNotReplicatedButGameplayChoiceIsNetworked()
    {
        PropertyInfo lobbyLocalProperty = typeof(NetworkPlayer).GetProperty(
            nameof(NetworkPlayer.LocalSelectedDemonKeyHash),
            AllMembers);
        Assert.That(lobbyLocalProperty, Is.Not.Null);
        Assert.That(
            lobbyLocalProperty.GetCustomAttribute<NetworkedAttribute>(),
            Is.Null,
            "The lobby demon choice must remain owner-private.");
        Assert.That(
            typeof(NetworkPlayer).GetProperties(AllMembers)
                .Where(property => property.GetCustomAttribute<NetworkedAttribute>() != null)
                .Select(property => property.Name),
            Does.Not.Contain("SelectedDemonKeyHash"));

        PropertyInfo gameplayProperty = typeof(PlayerManager).GetProperty(
            nameof(PlayerManager.SelectedDemonKeyHash),
            AllMembers);
        Assert.That(gameplayProperty, Is.Not.Null);
        Assert.That(gameplayProperty.GetCustomAttribute<NetworkedAttribute>(), Is.Not.Null);

        FieldInfo cache = typeof(NetworkManager).GetField("_lobbyDemonSelections", AllMembers);
        Assert.That(cache?.FieldType, Is.EqualTo(typeof(LobbyDemonSelectionSessionCache)));
    }

    [Test]
    public void DemonCommandUsesAppendedSharedCommandEnvelope()
    {
        Assert.That((int)CommandType.ActivateKingSkill, Is.EqualTo(15));
        Assert.That((int)CommandType.UpgradeWall, Is.EqualTo(16));
        Assert.That((int)CommandType.ActivateDemonSkill, Is.EqualTo(17));
        Assert.That(typeof(ActivateDemonSkillCommand).GetProperty(nameof(ActivateDemonSkillCommand.PlayerId)), Is.Not.Null);
        Assert.That(
            typeof(CommandProcessor).GetMethods(AllMembers).Any(method => method.Name == "SerializeCommand"),
            Is.True);
        Assert.That(
            typeof(PlayerCommandRequestValidator).GetMethod("ValidateActivateDemonSkillRequest", AllMembers),
            Is.Not.Null);
    }

    [Test]
    public void DemonRuntimeAndSummonerIdentityAreMigrationAndSnapshotDurable()
    {
        Type durableSnapshot = typeof(HostMigrationHandler).GetNestedType(
            "DurablePlayerMigrationSnapshot",
            BindingFlags.NonPublic);
        Assert.That(durableSnapshot, Is.Not.Null);
        Assert.That(
            durableSnapshot.GetField("DemonState", AllMembers)?.FieldType,
            Is.EqualTo(typeof(DemonRuntimeMigrationState)));

        Type playerSnapshot = typeof(MPTestStateSnapshot).GetNestedType("PlayerSnapshot", AllMembers);
        Assert.That(playerSnapshot, Is.Not.Null);
        Assert.That(playerSnapshot.GetField("SelectedDemonKeyHash", AllMembers), Is.Not.Null);
        Assert.That(playerSnapshot.GetField("DemonSkillUsedThisAttack", AllMembers), Is.Not.Null);
        Assert.That(playerSnapshot.GetField("DemonAttackSequenceId", AllMembers), Is.Not.Null);

        Assert.That(
            typeof(Monster).GetProperty(nameof(Monster.SnapshotSpawnAttackerPlayerId), AllMembers),
            Is.Not.Null);
        Assert.That(
            typeof(Monster).GetMethod(nameof(Monster.SetSpawnAttackerPlayerIdAuthoritative), AllMembers),
            Is.Not.Null);
        Assert.That(
            typeof(Monster).GetMethod(nameof(Monster.ApplyDemonSkillBuff), AllMembers),
            Is.Not.Null);
    }

    [Test]
    public void LobbyAndGameUiExposeSelectionAndAttackSkillWithoutRemoteLobbyDemonText()
    {
        string lobbyUxml = MdfSourcePolicy.ReadStaticContract("Assets/UI/JoinLobby/JoinLobby.uxml");
        Assert.That(lobbyUxml, Does.Contain("name=\"demonSelectionPanel\""));
        Assert.That(lobbyUxml, Does.Contain("name=\"demonSelectButton0\""));
        for (int index = 0; index < DemonSelectionCatalog.Entries.Count; index++)
        {
            Assert.That(lobbyUxml, Does.Contain($"name=\"demonCard{index}\""));
        }
        Assert.That(lobbyUxml, Does.Not.Contain("slotDemon"));

        string gameUxml = MdfSourcePolicy.ReadStaticContract("Assets/Resources/UI/GamePrepare/GamePreparePanels.uxml");
        Assert.That(gameUxml, Does.Contain("name=\"game-demon-skill-button\""));
        Assert.That(gameUxml, Does.Contain("name=\"game-demon-skill-icon\""));
    }

    [Test]
    public void LobbyDemonAndPortraitCallbacksUseSymmetricEnableLifecycle()
    {
        string source = MdfSourcePolicy.ReadStaticContract("Assets/Scripts/Network/JoinLobbyUI.cs");
        int registerStart = source.IndexOf("private void RegisterCallbacks()", StringComparison.Ordinal);
        int unregisterStart = source.IndexOf("private void UnregisterCallbacks()", StringComparison.Ordinal);
        int subscribeStart = source.IndexOf("private void SubscribeNetworkEvents()", StringComparison.Ordinal);

        Assert.That(registerStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(unregisterStart, Is.GreaterThan(registerStart));
        Assert.That(subscribeStart, Is.GreaterThan(unregisterStart));

        string registerBody = source.Substring(registerStart, unregisterStart - registerStart);
        Assert.That(registerBody, Does.Contain(
            "demonCards[i]?.Root?.RegisterCallback<PointerUpEvent>(OnDemonCardPointerUp)"));
        Assert.That(registerBody, Does.Contain(
            "slots[i]?.Portrait?.RegisterCallback<PointerUpEvent>(OnSlotPortraitPointerUp)"));
        Assert.That(registerBody, Does.Contain(
            "slots[i]?.DemonButton?.RegisterCallback<PointerUpEvent>(OnDemonSelectButtonPointerUp)"));
        Assert.That(registerBody, Does.Not.Contain("UnregisterCallback<PointerUpEvent>"));

        string unregisterBody = source.Substring(unregisterStart, subscribeStart - unregisterStart);
        Assert.That(unregisterBody, Does.Contain(
            "demonCards[i]?.Root?.UnregisterCallback<PointerUpEvent>(OnDemonCardPointerUp)"));
        Assert.That(unregisterBody, Does.Contain(
            "slots[i]?.Portrait?.UnregisterCallback<PointerUpEvent>(OnSlotPortraitPointerUp)"));
        Assert.That(unregisterBody, Does.Contain(
            "slots[i]?.DemonButton?.UnregisterCallback<PointerUpEvent>(OnDemonSelectButtonPointerUp)"));
        Assert.That(unregisterBody, Does.Not.Contain("RegisterCallback<PointerUpEvent>"));
    }

    [Test]
    public void NetworkProtocolAndSnapshotVersionIncludeDemonSchema()
    {
        Assert.That(MdfNetworkProtocol.AppVersion, Is.EqualTo("mdf-p4-demon-selection"));
        MPTestStateSnapshot.Snapshot snapshot = MPTestStateSnapshot.Capture("test", "demon_schema", "offline");
        Assert.That(snapshot.Version, Is.EqualTo(4));
    }
}
