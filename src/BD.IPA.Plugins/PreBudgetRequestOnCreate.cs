using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a budget request is created.
    /// </summary>
    public class PreBudgetRequestOnCreate : PluginBase
    {
        public PreBudgetRequestOnCreate() : base(typeof(PreBudgetRequestOnCreate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_budgetrequest>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_budgetrequest.Fields.bdgas_fee) || stateManager.Target.Contains(bdgas_budgetrequest.Fields.bdgas_isrecurring))
            {
                new Business.BudgetRequest.RecalculateTotal(localPluginContext, stateManager).Execute();
            }
        }
    }
}
