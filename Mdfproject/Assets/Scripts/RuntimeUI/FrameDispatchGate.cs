namespace MDF.Runtime.UI
{
    /// <summary>
    /// Prevents a uGUI Button callback and a world-space pointer fallback from dispatching the
    /// same command twice in one frame while also tracking an in-flight request.
    /// </summary>
    public sealed class FrameDispatchGate
    {
        private int _lastDispatchFrame = -1;

        public bool IsPending { get; private set; }
        public int LastDispatchFrame => _lastDispatchFrame;

        public bool TryBegin(int frame)
        {
            if (IsPending || frame == _lastDispatchFrame)
            {
                return false;
            }

            IsPending = true;
            _lastDispatchFrame = frame;
            return true;
        }

        public void Complete()
        {
            IsPending = false;
        }

        public void Reset()
        {
            IsPending = false;
            _lastDispatchFrame = -1;
        }
    }
}
