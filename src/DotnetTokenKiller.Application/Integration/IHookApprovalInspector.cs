namespace DotnetTokenKiller.Application.Integration;

/// <summary>One thing a harness requires before it runs an installed hook, and whether it holds.</summary>
/// <param name="Label">The check label, e.g. <c>hook approval</c>.</param>
/// <param name="Satisfied">Whether the requirement holds; doctor warns when it does not.</param>
/// <param name="Message">What was found, and what to do when it is not satisfied.</param>
internal sealed record HookApprovalFinding(string Label, bool Satisfied, string Message);

/// <summary>Implemented by integrators whose harness runs a registered hook only once the user has approved it.</summary>
internal interface IHookApprovalInspector
{
    /// <summary>Reports the harness's approval state for one registered installation.</summary>
    /// <param name="installation">A registration doctor found current.</param>
    /// <param name="projectDirectory">The directory doctor treats as the project root.</param>
    IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory);
}
