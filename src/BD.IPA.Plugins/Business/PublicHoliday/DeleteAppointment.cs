using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.PublicHoliday
{
    /// <summary>
    /// Removes appointments associated with a deleted public holiday.
    /// </summary>
    public class DeleteAppointment : BusinessBase<bdgas_publicholiday>
    {
        public DeleteAppointment(ILocalPluginContext context, StateManager<bdgas_publicholiday> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var date = Latest.bdgas_date?.ToString("MM-dd-yyyy");
            if (string.IsNullOrEmpty(date)) return;

            var meetingDates = GetMeetings(date);
            foreach (var item in meetingDates)
            {
                AdminService.Delete(item.LogicalName, item.Id);
            }
        }

        private Model.Appointment[] GetMeetings(string date)
        {
            var query = new QueryExpression(Model.Appointment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false)
            };
            query.Criteria.AddCondition(Model.Appointment.Fields.bdgas_sratemplate, ConditionOperator.Equal, true);
            query.Criteria.AddCondition(Model.Appointment.Fields.bdgas_SlotStatus, ConditionOperator.Equal, (int)Model.bdgas_appointment_bdgas_slotstatus.Available);
            query.Criteria.AddCondition(Model.Appointment.Fields.Subject, ConditionOperator.Like, $"%{date}%");

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<Model.Appointment>()).ToArray();
        }
    }
}
