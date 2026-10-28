using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a project milestone is created.
    /// </summary>
    public class PreProjectMilestoneOnCreate : PluginBase
    {
        public PreProjectMilestoneOnCreate() : base(typeof(PreProjectMilestoneOnCreate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_projectmilestone>(localPluginContext);
            new Business.ProjectMilestone.ProjectCenterUpdateAttributes(localPluginContext, stateManager).Execute();
            new Business.ProjectMilestone.ProjectCenterUpdateAttributes(localPluginContext, stateManager).Execute();
        }
    }
}
