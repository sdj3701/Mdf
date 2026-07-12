namespace MDF.Runtime.Assets
{
    /// <summary>
    /// Pure retention state used by runtime caches. It has no Unity or Addressables dependency and
    /// can be exercised directly by PlayMode tests.
    /// </summary>
    public sealed class AssetRetentionState
    {
        public bool IsPinned { get; private set; }
        public int LeaseCount { get; private set; }
        public bool IsRetained => IsPinned || LeaseCount > 0;

        public void Pin()
        {
            IsPinned = true;
        }

        public void Unpin()
        {
            IsPinned = false;
        }

        public void AcquireLease()
        {
            LeaseCount++;
        }

        public bool ReleaseLease()
        {
            if (LeaseCount <= 0)
            {
                return false;
            }

            LeaseCount--;
            return true;
        }

        public void Reset()
        {
            IsPinned = false;
            LeaseCount = 0;
        }
    }
}
