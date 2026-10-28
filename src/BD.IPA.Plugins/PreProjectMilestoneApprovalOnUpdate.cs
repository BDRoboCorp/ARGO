using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a project milestone approval is updated.
    /// </summary>
    public class PreProjectMilestoneApprovalOnUpdate : PluginBase
    {
        public PreProjectMilestoneApprovalOnUpdate() : base(typeof(PreProjectMilestoneApprovalOnUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_projectmilestoneapprover>(localPluginContext);
            if (stateManager.Target.Contains(bdgas_projectmilestoneapprover.Fields.statuscode))
            {
                new Business.ProjectMilestoneApproval.ContinueApprovalFlow(localPluginContext, stateManager).Execute();
            }
        }
    }
}
