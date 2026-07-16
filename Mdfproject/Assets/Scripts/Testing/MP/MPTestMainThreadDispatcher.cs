#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class MPTestMainThreadDispatcher : MonoBehaviour
{
    private static MPTestMainThreadDispatcher _instance;
    private static int _mainThreadId;
    private readonly Queue<IWorkItem> _queue = new Queue<IWorkItem>();

    public static MPTestMainThreadDispatcher Ensure()
    {
        if (_instance != null)
        {
            return _instance;
        }

        var go = new GameObject("MPTestMainThreadDispatcher");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<MPTestMainThreadDispatcher>();
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        return _instance;
    }

    public static Task<T> Run<T>(Func<T> action)
    {
        if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
        {
            return Task.FromResult(action());
        }

        if (_instance == null)
        {
            var missing = new TaskCompletionSource<T>();
            missing.SetException(new InvalidOperationException("MPTestMainThreadDispatcher is not initialized."));
            return missing.Task;
        }

        var item = new WorkItem<T>(action);
        lock (_instance._queue)
        {
            _instance._queue.Enqueue(item);
        }

        return item.Task;
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        Task<T> innerTask = await Run(action);
        return await innerTask;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        while (true)
        {
            IWorkItem item;
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    return;
                }

                item = _queue.Dequeue();
            }

            item.Execute();
        }
    }

    private interface IWorkItem
    {
        void Execute();
    }

    private sealed class WorkItem<T> : IWorkItem
    {
        private readonly Func<T> _action;
        private readonly TaskCompletionSource<T> _completion = new TaskCompletionSource<T>();

        public WorkItem(Func<T> action)
        {
            _action = action;
        }

        public Task<T> Task => _completion.Task;

        public void Execute()
        {
            try
            {
                _completion.SetResult(_action());
            }
            catch (Exception ex)
            {
                _completion.SetException(ex);
            }
        }
    }
}
#endif
