using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.BudgetRequest
{
    /// <summary>
    /// Creates a history record when a budget request changes.
    /// </summary>
    public class CreateBudgetHistory : BusinessBase<bdgas_budgetrequest>
    {
        public CreateBudgetHistory(ILocalPluginContext context, StateManager<bdgas_budgetrequest> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.statuscode != bdgas_budgetrequest_statuscode.Verified) return;

            var projectCenterRef = Latest.bdgas_requestintakeid;

            var projectCenterData = projectCenterRef != null ? AdminService.Retrieve(bdgas_requestintake.EntityLogicalName, projectCenterRef.Id,
                new ColumnSet(bdgas_requestintake.Fields.bdgas_botowner)).ToEntity<bdgas_requestintake>() : new bdgas_requestintake();
            var botOwnerRef = projectCenterData.bdgas_botowner;

            var budgetRequests = GetActiveBudgetRequest();
            if (budgetRequests.Any())
            {
                var update = new bdgas_BudgetRequestHistory
                {
                    Id = budgetRequests.FirstOrDefault().Id,
                    bdgas_fee = Latest.bdgas_fee,
                    bdgas_financepoc = Latest.bdgas_financepoc,
                    bdgas_botowner = botOwnerRef,
                    bdgas_approveddate = System.DateTime.UtcNow,
                    bdgas_costcenter = Latest.bdgas_costcenter,
                    statecode = bdgas_budgetrequesthistory_statecode.Inactive,
                    statuscode = bdgas_budgetrequesthistory_statuscode.Approved
                };

                AdminService.Update(update);
            }
            else
            {
                var create = new bdgas_BudgetRequestHistory
                {
                    bdgas_projectcenterid = Latest.bdgas_requestintakeid,
                    bdgas_budgetrequest = Latest.ToEntityReference(),
                    bdgas_fee = Latest.bdgas_fee,
                    bdgas_financepoc = Latest.bdgas_financepoc,
                    bdgas_botowner = botOwnerRef,
                    bdgas_approveddate = System.DateTime.UtcNow,
                    bdgas_costcenter = Latest.bdgas_costcenter
                };
                var createRequest = new CreateRequest
                {
                    Target = create.ToEntity<Entity>()
                };
                createRequest.Parameters.Add("SuppressCallbackRegistrationExpanderJob", true);

                var result = AdminService.Execute(createRequest) as CreateResponse;

                create.Id = result?.id ?? System.Guid.Empty;
                if (create.Id == System.Guid.Empty) return;

                var update = new bdgas_BudgetRequestHistory
                {
                    Id = create.Id,
                    statecode = bdgas_budgetrequesthistory_statecode.Inactive,
                    statuscode = bdgas_budgetrequesthistory_statuscode.Approved
                };
                AdminService.Update(update);
            }
        }

        private bdgas_BudgetRequestHistory[] GetActiveBudgetRequest()
        {
            var query = new QueryExpression(bdgas_BudgetRequestHistory.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(true),
                TopCount = 1
            };

            if (Latest.bdgas_requestintakeid != null)
            {
                query.Criteria.AddCondition(bdgas_BudgetRequestHistory.Fields.bdgas_projectcenterid, ConditionOperator.Equal, Latest.bdgas_requestintakeid.Id);
            }

            query.Criteria.AddCondition(bdgas_BudgetRequestHistory.Fields.bdgas_budgetrequest, ConditionOperator.Equal, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_BudgetRequestHistory>()).ToArray();
        }
    }
}
