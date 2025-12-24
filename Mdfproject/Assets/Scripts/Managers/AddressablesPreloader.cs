using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class AddressablesPreloader : MonoBehaviour
{
    [SerializeField] private bool autoLoadOnStart = false;
    [SerializeField] private List<string> assetKeys = new List<string>();

    public bool AssetsReady { get; private set; }

    private void Start()
    {
        if (autoLoadOnStart)
        {
            LoadAllAsync().Forget();
        }
    }

    public async UniTask LoadAllAsync()
    {
        AssetsReady = false;
        for (int i = 0; i < assetKeys.Count; i++)
        {
            string key = assetKeys[i];
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }
            await AssetLoader.LoadAssetAsync<Object>(key);
        }
        AssetsReady = true;
    }

    public void RegisterKey(string key)
    {
        if (string.IsNullOrEmpty(key) || assetKeys.Contains(key))
        {
            return;
        }
        assetKeys.Add(key);
    }
}
