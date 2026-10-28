using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.SRAAppointmentAssignment
{
    /// <summary>
    /// Creates or updates the email details for an SRA appointment assignment.
    /// </summary>
    public class UpsertEmail : BusinessBase<bdgas_sraappointmentassignment>
    {
        public UpsertEmail(ILocalPluginContext context, StateManager<bdgas_sraappointmentassignment> stateManager) :
            base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var sraAppointmentRef = Latest.bdgas_sraappointmentid;
            if (sraAppointmentRef == null) return;

            var assignments = GetSRAAppointmentAssignments(sraAppointmentRef).Select(e => e.InternalEMailAddress).Distinct().ToArray();

            if (Context.PluginExecutionContext.MessageName != "Delete")
            {
                var userRef = Latest.bdgas_touser;
                if (userRef == null) return;

                var user = AdminService.Retrieve(SystemUser.EntityLogicalName, userRef.Id, new ColumnSet(SystemUser.Fields.InternalEMailAddress)).ToEntity<SystemUser>();
                assignments = assignments.Concat(new[] { user.InternalEMailAddress }).Distinct().ToArray();
            }

            var appointment = new Model.Appointment { Id = sraAppointmentRef.Id, bdgas_optionalemails = string.Join(";", assignments) };
            AdminService.Update(appointment);
        }

        private SystemUser[] GetSRAAppointmentAssignments(Microsoft.Xrm.Sdk.EntityReference sraAppointmentRef)
        {
            var query = new QueryExpression(SystemUser.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(SystemUser.Fields.InternalEMailAddress)
            };
            var link = query.AddLink(bdgas_sraappointmentassignment.EntityLogicalName, SystemUser.Fields.Id, bdgas_sraappointmentassignment.Fields.bdgas_touser);
            link.LinkCriteria.AddCondition(bdgas_sraappointmentassignment.Fields.bdgas_sraappointmentid, ConditionOperator.Equal, sraAppointmentRef.Id);
            link.LinkCriteria.AddCondition(bdgas_sraappointmentassignment.Fields.Id, ConditionOperator.NotEqual, Latest.Id);
            link.LinkCriteria.AddCondition(bdgas_sraappointmentassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_sraappointmentassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<SystemUser>()).ToArray();
        }
    }
}
