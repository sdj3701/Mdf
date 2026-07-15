/// <summary>
/// Immutable authored identity for gameplay content. Display names, asset names, and
/// Addressable addresses may change; ContentId must not change after release.
/// </summary>
public interface IStableContentIdentity
{
    string ContentId { get; }
    int ContentIdHash { get; }
}
