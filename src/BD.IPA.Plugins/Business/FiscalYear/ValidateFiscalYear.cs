using BD.Model;
using Microsoft.Xrm.Sdk;

namespace BD.IPA.Plugins.Business.FiscalYear
{
    /// <summary>
    /// Validates fiscal year values and date ranges.
    /// </summary>
    public class ValidateFiscalYear : BusinessBase<bdgas_FiscalYear>
    {
        public ValidateFiscalYear(ILocalPluginContext context, StateManager<bdgas_FiscalYear> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var startDate = Latest.bdgas_startdate;
            var endDate = Latest.bdgas_enddate;

            if (startDate == null || endDate == null)
            {
                throw new InvalidPluginExecutionException("Start Date and End Date must be provided.");
            }

            if (startDate >= endDate)
            {
                throw new InvalidPluginExecutionException("Start Date must be earlier than End Date.");
            }

            var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(bdgas_FiscalYear.EntityLogicalName)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
                Criteria = new Microsoft.Xrm.Sdk.Query.FilterExpression
                {
                    Conditions =
                    {
                        new Microsoft.Xrm.Sdk.Query.ConditionExpression(bdgas_FiscalYear.Fields.bdgas_startdate, Microsoft.Xrm.Sdk.Query.ConditionOperator.LessEqual, endDate),
                        new Microsoft.Xrm.Sdk.Query.ConditionExpression(bdgas_FiscalYear.Fields.bdgas_enddate, Microsoft.Xrm.Sdk.Query.ConditionOperator.GreaterEqual, startDate)
                    }
                }
            };

            var result = AdminService.RetrieveMultiple(query);

            if (result.Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException("The specified fiscal year overlaps with an existing fiscal year.");
            }
        }
    }
}
