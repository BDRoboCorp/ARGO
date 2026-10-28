using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Sets the fiscal year associated with a request intake.
    /// </summary>
    public class SetFiscalYear : BusinessBase<bdgas_requestintake>
    {
        public SetFiscalYear(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.bdgas_fiscalyear != null) return;

            var fiscalYear = GetActiveFiscalYear();
            Target.bdgas_fiscalyear = fiscalYear?.ToEntityReference();
        }

        public bdgas_FiscalYear GetActiveFiscalYear()
        {
            var query = new QueryExpression(bdgas_FiscalYear.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(bdgas_FiscalYear.Fields.statuscode, ConditionOperator.Equal, (int)bdgas_fiscalyear_statuscode.Active),
                        new ConditionExpression(bdgas_FiscalYear.Fields.bdgas_enddate, ConditionOperator.OnOrAfter, Latest.CreatedOn)
                    }
                }
            };
            query.AddOrder(bdgas_FiscalYear.Fields.bdgas_startdate, OrderType.Ascending);
            var result = AdminService.RetrieveMultiple(query);
            return result.Entities.FirstOrDefault()?.ToEntity<bdgas_FiscalYear>();
        }
    }
}
