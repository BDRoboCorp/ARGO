using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Reschedules an appointment into an available slot.
    /// </summary>
    public class RescheduleSlot : BusinessBase<Model.Appointment>
    {
        public RescheduleSlot(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;

            var swappedAppointmentRef = Target.bdgas_swapsraslotid;

            if (swappedAppointmentRef == null) return;
            var importantAttributes = new[]
            {
                Model.Appointment.Fields.Subject,
                Model.Appointment.Fields.ScheduledStart,
                Model.Appointment.Fields.ScheduledEnd,
                Model.Appointment.Fields.bdgas_duration,
                Model.Appointment.Fields.bdgas_quantity,
                Model.Appointment.Fields.bdgas_breakeachsession,
                Model.Appointment.Fields.ScheduledDurationMinutes,
                Model.Appointment.Fields.bdgas_datestring,
                Model.Appointment.Fields.bdgas_slotnumber,
                Model.Appointment.Fields.bdgas_IsFirstAppointment,
                Model.Appointment.Fields.ActualStart,
                Model.Appointment.Fields.ActualEnd,
                Model.Appointment.Fields.SortDate,
                Model.Appointment.Fields.Id
            };

            var mySRAData = AdminService.Retrieve(swappedAppointmentRef.LogicalName, swappedAppointmentRef.Id, new ColumnSet(importantAttributes)).ToEntity<Model.Appointment>();
            var swappedScheduledStart = mySRAData.ScheduledStart;
            var update = new Model.Appointment();
            foreach (var attr in Latest.Attributes)
            {
                if (!importantAttributes.Contains(attr.Key)) continue;
                update[attr.Key] = attr.Value;
            }
            var slotStatusTarget = DateTime.UtcNow > update.ScheduledStart ? bdgas_appointment_bdgas_slotstatus.Notavailable : bdgas_appointment_bdgas_slotstatus.Available;
            update.bdgas_SlotStatus = slotStatusTarget;
            update.Id = mySRAData.Id;
            AdminService.Update(update);

            foreach (var attr in mySRAData.Attributes)
            {
                if (attr.Key == Model.Appointment.Fields.Id) continue;
                Target[attr.Key] = attr.Value;
            }
            slotStatusTarget = DateTime.UtcNow > swappedScheduledStart ? bdgas_appointment_bdgas_slotstatus.Notavailable : bdgas_appointment_bdgas_slotstatus.Reserved;
            Target.bdgas_SlotStatus = slotStatusTarget;
            Target.bdgas_mydigitalsraid = null;
            Target.bdgas_swapsraslotid = null;
        }
    }

}
