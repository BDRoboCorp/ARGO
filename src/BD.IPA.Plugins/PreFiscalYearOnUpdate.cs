using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a fiscal year is updated.
    /// </summary>
    public class PreFiscalYearOnUpdate : PluginBase
    {
        public PreFiscalYearOnUpdate() : base(typeof(PreFiscalYearOnUpdate))
        {
        }
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_FiscalYear>(localPluginContext);
            if (stateManager.Target.Contains(bdgas_FiscalYear.Fields.bdgas_startdate) ||
                stateManager.Target.Contains(bdgas_FiscalYear.Fields.bdgas_enddate))
            {
                new Business.FiscalYear.ValidateFiscalYear(localPluginContext, stateManager).Execute();
            }
        }
    }
}
