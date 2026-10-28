using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Marks eligible budget requests as verified for a request intake.
    /// </summary>
    public class SetBudgetRequestToVerified : BusinessBase<bdgas_requestintake>
    {
        public SetBudgetRequestToVerified(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) :
            base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.statuscode != bdgas_requestintake_statuscode.SolutionDesign) return;

            var budgetRequests = GetBudgetRequests();
            foreach (var budgetRequest in budgetRequests)
            {
                var update = new bdgas_budgetrequest
                {
                    Id = budgetRequest.Id,
                    statuscode = bdgas_budgetrequest_statuscode.Verified
                };

                AdminService.Update(update);
            }
        }

        private bdgas_budgetrequest[] GetBudgetRequests()
        {
            var query = new QueryExpression(bdgas_budgetrequest.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false)
            };
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.statecode, ConditionOperator.Equal, (int)bdgas_budgetrequest_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_budgetrequest>()).ToArray();
        }
    }
}
