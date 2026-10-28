using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a request intake is created.
    /// </summary>
    public class PreRequestIntakeOnCreate : PluginBase
    {
        public PreRequestIntakeOnCreate() : base(typeof(PreRequestIntakeOnCreate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_requestintake>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_triagedtechnology) &&
                !string.IsNullOrEmpty(stateManager.Target.bdgas_triagedtechnology))
            {
                stateManager.Target.bdgas_triagedtechnology = stateManager.Target.bdgas_triagedtechnology.ToUpper();
                stateManager.Target.bdgas_planviewrequestcreationdate = System.DateTime.UtcNow;
            }
            new Business.RequestIntake.GenerateConfigurationItemUrl(localPluginContext, stateManager).Execute();
            new Business.RequestIntake.SetFiscalYear(localPluginContext, stateManager).Execute();
            new Business.RequestIntake.SetAutonumber(localPluginContext, stateManager).Execute();
            new Business.RequestIntake.SetPlanviewData(localPluginContext, stateManager).Execute();
        }
    }
}
