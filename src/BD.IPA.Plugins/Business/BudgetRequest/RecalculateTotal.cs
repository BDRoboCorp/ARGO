using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.BudgetRequest
{
    /// <summary>
    /// Recalculates the total amount for a budget request.
    /// </summary>
    public class RecalculateTotal : BusinessBase<bdgas_budgetrequest>
    {
        public RecalculateTotal(ILocalPluginContext context, StateManager<bdgas_budgetrequest> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var requestIntakeRef = Latest.bdgas_requestintakeid;
            if (requestIntakeRef == null) return;

            var budgetRequests = GetBudgetRequests(requestIntakeRef.Id).Concat(new[] { Latest });

            var totalOperation = budgetRequests.Where(e => e.bdgas_isrecurring.GetValueOrDefault()).Sum(e => e.bdgas_fee?.Value ?? 0);
            var totalDevelopment = budgetRequests.Where(e => !e.bdgas_isrecurring.GetValueOrDefault()).Sum(e => e.bdgas_fee?.Value ?? 0);

            var update = new bdgas_requestintake { Id = requestIntakeRef.Id, bdgas_totaldevelopment = new Microsoft.Xrm.Sdk.Money(totalDevelopment), bdgas_totaloperation = new Microsoft.Xrm.Sdk.Money(totalOperation) };
            AdminService.Update(update);
        }

        private bdgas_budgetrequest[] GetBudgetRequests(Guid id)
        {

            var query = new QueryExpression(bdgas_budgetrequest.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_budgetrequest.Fields.bdgas_isrecurring, bdgas_budgetrequest.Fields.bdgas_fee)
            };
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_requestintakeid, ConditionOperator.Equal, id);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.statecode, ConditionOperator.Equal, (int)bdgas_budgetrequest_statecode.Active);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.Id, ConditionOperator.NotEqual, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_budgetrequest>()).ToArray();
        }
    }
}
