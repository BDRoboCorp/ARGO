using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a budget request is updated.
    /// </summary>
    public class PreBudgetRequestOnUpdate : PluginBase
    {
        public PreBudgetRequestOnUpdate() : base(typeof(PreBudgetRequestOnUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_budgetrequest>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_budgetrequest.Fields.bdgas_fee) || stateManager.Target.Contains(bdgas_budgetrequest.Fields.bdgas_isrecurring))
            {
                new Business.BudgetRequest.RecalculateTotal(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(bdgas_budgetrequest.Fields.statuscode))
            {
                new Business.BudgetRequest.CreateBudgetHistory(localPluginContext, stateManager).Execute();
            }
        }
    }
}
