/// <summary>
/// Requests the local player's once-per-attack-sequence demon skill. Final validation and
/// every monster mutation remain State Authority owned in PlayerManager.
/// </summary>
public sealed class ActivateDemonSkillCommand : ICommand
{
    public int PlayerId { get; set; }

    public ActivateDemonSkillCommand(int playerId)
    {
        PlayerId = playerId;
    }

    public void Execute()
    {
        GameManagers.Instance?.GetPlayer(PlayerId)?.TryActivateDemonSkillAuthoritative();
    }
}
