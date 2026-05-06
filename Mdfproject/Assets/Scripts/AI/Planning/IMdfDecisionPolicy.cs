public interface IMdfDecisionPolicy
{
    bool TryChoose(MdfDecisionContext context, out MdfDecision decision);
}
