/// <summary>
/// Requests the local player's once-per-defense king skill. The gameplay
/// mutation and its final authority checks live in PlayerManager.
/// </summary>
public sealed class ActivateKingSkillCommand : ICommand
{
    public int PlayerId { get; set; }

    public ActivateKingSkillCommand(int playerId)
    {
        PlayerId = playerId;
    }

    public void Execute()
    {
        GameManagers.Instance?.GetPlayer(PlayerId)?.TryActivateKingSkillAuthoritative();
    }
}
