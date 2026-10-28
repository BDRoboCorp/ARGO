using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a budget request history record is updated.
    /// </summary>
    public class PreBudgetRequestHistoryUpdate : PluginBase
    {
        public PreBudgetRequestHistoryUpdate() : base(typeof(PreBudgetRequestHistoryUpdate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_BudgetRequestHistory>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_BudgetRequestHistory.Fields.bdgas_approvalbotowner) ||
                stateManager.Target.Contains(bdgas_BudgetRequestHistory.Fields.bdgas_approvalfinancepoc))
            {
                var isApproved = stateManager.Latest.bdgas_approvalbotowner.GetValueOrDefault() && stateManager.Latest.bdgas_approvalfinancepoc.GetValueOrDefault();

                if (isApproved)
                {
                    stateManager.Target.statuscode = bdgas_budgetrequesthistory_statuscode.Approved;
                    stateManager.Target.statecode = bdgas_budgetrequesthistory_statecode.Inactive;
                }
            }

            if (stateManager.Target.Contains(bdgas_BudgetRequestHistory.Fields.statuscode))
            {
                new Business.BudgetRequestHistory.UpdateBudgetRequest(localPluginContext, stateManager).Execute();
            }
        }
    }
}
