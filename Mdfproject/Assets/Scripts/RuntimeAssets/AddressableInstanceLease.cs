using UnityEngine;

namespace MDF.Runtime.Assets
{
    /// <summary>
    /// Couples an instantiated object's lifetime to the lease for its source prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AddressableInstanceLease : MonoBehaviour
    {
        private AddressableAssetLease<GameObject> _lease;

        public void Initialize(AddressableAssetLease<GameObject> lease)
        {
            if (ReferenceEquals(_lease, lease))
            {
                return;
            }

            _lease?.Dispose();
            _lease = lease;
        }

        private void OnDestroy()
        {
            _lease?.Dispose();
            _lease = null;
        }
    }
}
