using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Registers project center information from an appointment.
    /// </summary>
    public class RegisterProjectCenter : BusinessBase<Model.Appointment>
    {
        public RegisterProjectCenter(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;

            if (Target.Contains(Model.Appointment.Fields.RegardingObjectId) && Target.RegardingObjectId != null)
            {
                var regardingObject = Target.RegardingObjectId;
                if (regardingObject.LogicalName == bdgas_requestintake.EntityLogicalName)
                {
                    var applications = GetRequestIntakeApplications();
                    foreach (var app in applications)
                    {
                        var update = new bdgas_RequestIntakeApplication
                        {
                            Id = app.Id,
                            bdgas_requestintakeid = regardingObject,
                            bdgas_appointmentid = Latest.ToEntityReference()
                        };
                        AdminService.Update(update);
                    }


                    var pcUpdate = new bdgas_requestintake
                    {
                        Id = Target.RegardingObjectId.Id,
                        bdgas_sraid = Latest.ToEntityReference()
                    };
                    AdminService.Update(pcUpdate);
                }
            }
        }

        private bdgas_RequestIntakeApplication[] GetRequestIntakeApplications()
        {
            var query = new QueryExpression(bdgas_RequestIntakeApplication.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false)
            };

            var filterOr = new FilterExpression(LogicalOperator.Or);
            filterOr.AddCondition(bdgas_RequestIntakeApplication.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            if (Target.RegardingObjectId != null && Target.RegardingObjectId.LogicalName == bdgas_requestintake.EntityLogicalName)
            {
                filterOr.AddCondition(bdgas_RequestIntakeApplication.Fields.bdgas_requestintakeid, ConditionOperator.Equal, Target.RegardingObjectId.Id);
            }
            query.Criteria.AddFilter(filterOr);
            query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.statecode, ConditionOperator.Equal, (int)bdgas_requestintakeapplication_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_RequestIntakeApplication>()).ToArray();
        }
    }
}
