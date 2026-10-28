using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before the QPA business process flow is updated.
    /// </summary>
    public class PreQPABpfOnUpdate : PluginBase
    {
        public PreQPABpfOnUpdate() : base(typeof(PreQPABpfOnUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_qpabpf>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_qpabpf.Fields.ActiveStageId))
            {
                new Business.QpaBpf.SetRequestIntakeStatus(localPluginContext, stateManager).Execute();
            }
        }
    }
}
