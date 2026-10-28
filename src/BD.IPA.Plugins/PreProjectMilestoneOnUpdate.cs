using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a project milestone is updated.
    /// </summary>
    public class PreProjectMilestoneOnUpdate : PluginBase
    {
        public PreProjectMilestoneOnUpdate() : base(typeof(PreProjectMilestoneOnUpdate))
        {
        }
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_projectmilestone>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_projectmilestone.Fields.statuscode))
            {
                if (stateManager.Target.statuscode == bdgas_projectmilestone_statuscode.Submitted)
                {
                    stateManager.Target.bdgas_assigneduserid = new Microsoft.Xrm.Sdk.EntityReference(SystemUser.EntityLogicalName, localPluginContext.PluginExecutionContext.InitiatingUserId);
                    new Business.ProjectMilestone.GenerateApprovalFlow(localPluginContext, stateManager).Execute();
                }
                
                new Business.ProjectMilestone.ProjectCenterUpdateAttributes(localPluginContext, stateManager).Execute();
            }
        }
    }
}
