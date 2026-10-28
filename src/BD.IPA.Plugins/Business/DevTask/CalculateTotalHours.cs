using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.DevTask
{
    /// <summary>
    /// Calculates the total hours recorded for a development task.
    /// </summary>
    public class CalculateTotalHours : BusinessBase<bdgas_devtask>
    {
        public CalculateTotalHours(ILocalPluginContext context, StateManager<bdgas_devtask> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var projectCenterRef = Latest.bdgas_requestintakeid;
            if (projectCenterRef == null) return;

            var currentHours = Latest.bdgas_hours.GetValueOrDefault() + GetHoursDevTasks(projectCenterRef.Id);

            var update = new bdgas_requestintake { Id = projectCenterRef.Id, bdgas_totalspenthours = currentHours };
            AdminService.Update(update);
        }

        private int GetHoursDevTasks(Guid projectCenterId)
        {
            var query = new QueryExpression(bdgas_devtask.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_devtask.Fields.bdgas_hours)
            };
            query.Criteria.AddCondition(bdgas_devtask.Fields.bdgas_requestintakeid, ConditionOperator.Equal, projectCenterId);
            query.Criteria.AddCondition(bdgas_devtask.Fields.statecode, ConditionOperator.Equal, (int)bdgas_devtask_statecode.Active);
            query.Criteria.AddCondition(bdgas_devtask.Fields.Id, ConditionOperator.NotEqual, Latest.Id);
            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_devtask>()).Sum(e => e.bdgas_hours.GetValueOrDefault());
        }
    }
}
