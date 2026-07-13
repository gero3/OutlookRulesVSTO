using System.Runtime.InteropServices;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Bridge;

[ComVisible(true)]
[Guid("797AE4B4-9439-4387-B64B-652209CA17A0")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IVstoBridgeSavePlan
{
    string RuleId { get; }
    string RuleName { get; }
    int AttachmentIndex { get; }
    string AttachmentName { get; }
    string SaveMode { get; }
    string DestinationPath { get; }
    string Status { get; }
    string Message { get; }
}

[ComVisible(true)]
[Guid("5A4C7BA2-66CD-48EB-A66F-9637E3A8CE4A")]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IVstoBridgeSavePlan))]
public sealed class VstoBridgeSavePlan : IVstoBridgeSavePlan
{
    internal VstoBridgeSavePlan(SavePlan plan)
    {
        RuleId = plan.RuleId; RuleName = plan.RuleName; AttachmentIndex = plan.AttachmentIndex;
        AttachmentName = plan.AttachmentName; DestinationPath = plan.DestinationPath;
        SaveMode = plan.SaveMode.ToString();
        Status = plan.Status.ToString(); Message = plan.Message;
    }
    public string RuleId { get; }
    public string RuleName { get; }
    public int AttachmentIndex { get; }
    public string AttachmentName { get; }
    public string SaveMode { get; }
    public string DestinationPath { get; }
    public string Status { get; }
    public string Message { get; }
}

[ComVisible(true)]
[Guid("F0AA732D-425E-40FB-A7A9-5CAB5B698010")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IVstoBridgeEvaluation
{
    int PlanCount { get; }
    string Summary { get; }
    IVstoBridgeSavePlan GetPlan(int oneBasedIndex);
}

[ComVisible(true)]
[Guid("192F52E8-028B-4C65-B5B5-5F18EE157C93")]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IVstoBridgeEvaluation))]
public sealed class VstoBridgeEvaluation : IVstoBridgeEvaluation
{
    private readonly List<VstoBridgeSavePlan> _plans;
    internal VstoBridgeEvaluation(IEnumerable<RuleEvaluationResult> results)
    {
        var evaluated = results.ToList();
        _plans = evaluated.SelectMany(result => result.SavePlans).Select(plan => new VstoBridgeSavePlan(plan)).ToList();
        Summary = string.Join("; ", evaluated.Select(Describe));
    }
    public int PlanCount => _plans.Count;
    public string Summary { get; }
    public IVstoBridgeSavePlan GetPlan(int oneBasedIndex) => _plans[oneBasedIndex - 1];

    private static string Describe(RuleEvaluationResult result)
    {
        if (result.SavePlans.Count > 0) return result.Rule.Name + ": match.";
        string reason = string.Join(" ", result.Messages);
        if (string.IsNullOrWhiteSpace(reason)) reason = result.IsMatch ? "No save action created." : "No match.";
        return result.Rule.Name + ": " + reason;
    }
}
