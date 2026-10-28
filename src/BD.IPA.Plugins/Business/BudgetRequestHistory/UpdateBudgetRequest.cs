using BD.Model;

namespace BD.IPA.Plugins.Business.BudgetRequestHistory
{
    /// <summary>
    /// Applies budget request history changes to the related budget request.
    /// </summary>
    public class UpdateBudgetRequest : BusinessBase<bdgas_BudgetRequestHistory>
    {
        public UpdateBudgetRequest(ILocalPluginContext context, StateManager<bdgas_BudgetRequestHistory> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.statuscode != bdgas_budgetrequesthistory_statuscode.Approved) return;
            if (Latest.bdgas_budgetrequest == null) return;

            var data = AdminService.Retrieve(bdgas_budgetrequest.EntityLogicalName, Latest.bdgas_budgetrequest.Id,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(bdgas_budgetrequest.Fields.bdgas_isrecurring, 
                bdgas_budgetrequest.Fields.bdgas_nextchargebackdate)).ToEntity<bdgas_budgetrequest>();
            if (!data.bdgas_isrecurring.GetValueOrDefault()) return;

            var nextChargeback = data.bdgas_nextchargebackdate;

            var update = new bdgas_budgetrequest
            {
                Id = data.Id,
                bdgas_financepoc = Latest.bdgas_financepoc,
                bdgas_fee = Latest.bdgas_fee,
                bdgas_datechargeback = nextChargeback,
                bdgas_nextchargebackdate = nextChargeback.GetValueOrDefault().AddYears(1)
            };
            AdminService.Update(update);
        }
    }
}
