using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Copies the meeting URL to appointments scheduled for the same day.
    /// </summary>
    public class CopyMeetingUrlForTheDay : BusinessBase<Model.Appointment>
    {
        public CopyMeetingUrlForTheDay(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) :
            base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;
            if (!Latest.bdgas_IsFirstAppointment.GetValueOrDefault()) return;
            if (!Latest.bdgas_sratemplate.GetValueOrDefault()) return;
            if (!Latest.ScheduledStart.HasValue) return;

            if (string.IsNullOrEmpty(Latest.bdgas_eventid)) return;

            var appointments = GetAppointments();
            foreach (var current in appointments)
            {
                var update = new Model.Appointment
                {
                    Id = current.Id,
                    bdgas_eventid = Latest.bdgas_eventid,
                    OnlineMeetingJoinUrl = Latest.OnlineMeetingJoinUrl,
                    OnlineMeetingId = Latest.OnlineMeetingId
                };
                AdminService.Update(update);
            }
        }

        private Model.Appointment[] GetAppointments()
        {
            var timezoneString = !string.IsNullOrEmpty(Latest.bdgas_timezone)
                ? Latest.bdgas_timezone
                : TimeZoneDefinitionQueries.GetTimezoneByUserId(Context.PluginExecutionContext.UserId, AdminService);
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneString);

            var scheduleDate = timezone.ConvertToLocal(Latest.ScheduledStart.GetValueOrDefault()).Date;
            var utcDayStart = timezone.ConvertToUTC(scheduleDate);
            var utcDayEnd = timezone.ConvertToUTC(scheduleDate.AddDays(1));

            var query = new QueryExpression(Model.Appointment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false)
            };
            query.Criteria.AddCondition(Model.Appointment.Fields.ActivityId, ConditionOperator.NotEqual, Latest.Id);
            query.Criteria.AddCondition(Model.Appointment.Fields.bdgas_sratemplate, ConditionOperator.Equal, true);
            query.Criteria.AddCondition(Model.Appointment.Fields.bdgas_SlotStatus, ConditionOperator.Equal, (int)Model.bdgas_appointment_bdgas_slotstatus.Available);
            // "On" compares only the date part of the value, so use an explicit UTC range for the local day
            query.Criteria.AddCondition(Model.Appointment.Fields.ScheduledStart, ConditionOperator.GreaterEqual, utcDayStart);
            query.Criteria.AddCondition(Model.Appointment.Fields.ScheduledStart, ConditionOperator.LessThan, utcDayEnd);

            var result = AdminService.RetrieveMultiple(query);
            return result.Entities.Select(e => e.ToEntity<Model.Appointment>()).ToArray();
        }
    }
}
