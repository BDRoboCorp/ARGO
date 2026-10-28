using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a development task is updated.
    /// </summary>
    public class PreDevTaskOnUpdate : PluginBase
    {
        public PreDevTaskOnUpdate() : base(typeof(PreDevTaskOnUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_devtask>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_devtask.Fields.bdgas_hours))
            {
                new Business.DevTask.CalculateTotalHours(localPluginContext, stateManager).Execute();
            }
        }
    }
}
