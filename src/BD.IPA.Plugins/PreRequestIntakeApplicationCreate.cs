using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a request intake application is created.
    /// </summary>
    public class PreRequestIntakeApplicationCreate : PluginBase
    {
        public PreRequestIntakeApplicationCreate() : base(typeof(PreRequestIntakeApplicationCreate))
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
