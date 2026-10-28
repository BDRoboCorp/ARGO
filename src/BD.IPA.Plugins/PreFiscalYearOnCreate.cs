using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a fiscal year is created.
    /// </summary>
    public class PreFiscalYearOnCreate : PluginBase
    {
        public PreFiscalYearOnCreate() : base(typeof(PreFiscalYearOnCreate))
        {
        }
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_FiscalYear>(localPluginContext);
            new Business.FiscalYear.ValidateFiscalYear(localPluginContext, stateManager).Execute();
        }
    }
}
