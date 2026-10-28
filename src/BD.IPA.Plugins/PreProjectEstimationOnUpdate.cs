using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a project estimation is updated.
    /// </summary>
    public class PreProjectEstimationOnUpdate : PluginBase
    {
        public PreProjectEstimationOnUpdate() : base(typeof(PreProjectEstimationOnUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_devestimation>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_devestimation.Fields.bdgas_planstartdate) || stateManager.Target.Contains(bdgas_devestimation.Fields.bdgas_planenddate))
            {
                new Business.ProjectEstimation.OnUpdateEstimationDates(localPluginContext, stateManager).Execute();
            }
        }
    }
}
