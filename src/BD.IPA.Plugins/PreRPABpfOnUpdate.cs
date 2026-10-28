using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before the RPA business process flow is updated.
    /// </summary>
    public class PreRPABpfOnUpdate : PluginBase
    {
        public PreRPABpfOnUpdate() : base(typeof(PreRPABpfOnUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_rpabpf>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_rpabpf.Fields.ActiveStageId))
            {
                new Business.RpaBPF.SetRequestIntakeStatus(localPluginContext, stateManager).Execute();
            }
        }
    }
}
