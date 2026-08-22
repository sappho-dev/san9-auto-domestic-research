namespace San9AutoDomestic.V8Transaction
{
    /// <summary>
    /// Design-only boundary for a future separately authorized native layer.
    /// This prototype contains no implementation and the synthetic state
    /// machine never resolves or invokes this interface.
    /// </summary>
    internal interface INativeSingleCommandCallbacks
    {
        void Begin(SingleCommandRequest request);

        SyntheticStageEvidence BindTargetCandidate(
            SingleCommandRequest request,
            ActionSideEffectPermit permit);

        SyntheticStageEvidence OpenOuter(
            SingleCommandRequest request,
            ActionSideEffectPermit permit);

        SyntheticStageEvidence OpenSelector(
            SingleCommandRequest request,
            ActionSideEffectPermit permit);

        SyntheticStageEvidence Clear(
            SingleCommandRequest request,
            ActionSideEffectPermit permit);

        SyntheticStageEvidence NativeFillMax(
            SingleCommandRequest request,
            ActionSideEffectPermit permit);

        SyntheticStageEvidence AcceptInner(
            SingleCommandRequest request,
            ActionSideEffectPermit permit);

        SyntheticStageEvidence AcceptOuter(
            SingleCommandRequest request,
            ActionSideEffectPermit permit);

        void VerifyCommitted(SingleCommandRequest request);
    }

    /// <summary>
    /// Design-only recovery boundary.  A future cleanup implementation must
    /// be synchronously routed by the coordinator-owned invoker with an exact
    /// one-shot side-effect permit before touching any bound root/target/modal
    /// object.  Only after that method returns does the coordinator issue and
    /// invoke two ordered post-read permits, then build the authenticated A/B
    /// receipt itself.  No implementation exists in this prototype.
    /// </summary>
    internal interface INativeAbortCleanupCallback
    {
        void ConditionalRestoreTarget(CleanupSideEffectPermit permit);

        void CancelSelector(CleanupSideEffectPermit permit);

        void CancelOuter(CleanupSideEffectPermit permit);

        void ReviewAfterAccept(CleanupSideEffectPermit permit);

        CleanupPostStateRead ReadPostState(CleanupPostReadPermit permit);
    }
}
