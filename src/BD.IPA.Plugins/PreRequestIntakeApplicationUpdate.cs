using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a request intake application is updated.
    /// </summary>
    public class PreRequestIntakeApplicationUpdate : PluginBase
    {
        public PreRequestIntakeApplicationUpdate() : base(typeof(PreRequestIntakeApplicationUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_RequestIntakeApplication>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_RequestIntakeApplication.Fields.bdgas_application))
            {
                new Business.RequestIntakeApplication.DuplicateValidation(localPluginContext, stateManager).Execute();
            }
        }
    }
}
