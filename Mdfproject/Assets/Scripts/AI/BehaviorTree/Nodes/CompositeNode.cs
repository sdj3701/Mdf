using System.Collections.Generic;

namespace AI.BehaviorTree.Nodes
{
    public abstract class CompositeNode : Node
    {
        protected List<Node> children = new List<Node>();

        public CompositeNode(params Node[] childNodes)
        {
            children.AddRange(childNodes);
        }
    }
}
