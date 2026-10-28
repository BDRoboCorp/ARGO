using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Cancels a scheduled appointment slot and updates its related records.
    /// </summary>
    public class CancelSlot : BusinessBase<Model.Appointment>
    {
        public CancelSlot(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Target.bdgas_SlotStatus != bdgas_appointment_bdgas_slotstatus.Cancel) return;

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
                Model.Appointment.Fields.Id,
                Model.Appointment.Fields.bdgas_SlotStatus,
                Model.Appointment.Fields.bdgas_eventid,
                Model.Appointment.Fields.OnlineMeetingJoinUrl,
                Model.Appointment.Fields.OnlineMeetingId,
                Model.Appointment.Fields.bdgas_sratemplate
            };

            foreach (var item in Latest.Attributes)
            {
                if (importantAttributes.Contains(item.Key)) continue;
                if (!item.Key.StartsWith("bdgas_")) continue;
                Target[item.Key] = null;
            }

            Target.RegardingObjectId = null;
            Target.bdgas_schedulesratype = null;
            Target.bdgas_swapsraslotid = null;
            Target.bdgas_mydigitalsraid = null;
            Target.bdgas_NetworkDataflowDiagram = null;
            Target.bdgas_networkdataflowdiagram2 = null;
            Target.bdgas_networkdataflowdiagram3 = null;
            Target.bdgas_networkdataflowdiagram4 = null;
            Target.bdgas_networkdataflowdiagram5 = null;
            Target.bdgas_Registrant = null;
            Target.bdgas_SlotStatus = Latest.ScheduledStart > DateTime.UtcNow ? Model.bdgas_appointment_bdgas_slotstatus.Available : Model.bdgas_appointment_bdgas_slotstatus.Notavailable;

            DeleteActiveAssignments<bdgas_authenticationmethodassignment>(
                bdgas_authenticationmethodassignment.EntityLogicalName,
                bdgas_authenticationmethodassignment.Fields.bdgas_appointmentid,
                bdgas_authenticationmethodassignment.Fields.statecode,
                (int)bdgas_authenticationmethodassignment_statecode.Active);
            DeleteActiveAssignments<bdgas_controlledsegmentationassignment>(
                bdgas_controlledsegmentationassignment.EntityLogicalName,
                bdgas_controlledsegmentationassignment.Fields.bdgas_appointmentid,
                bdgas_controlledsegmentationassignment.Fields.statecode,
                (int)bdgas_controlledsegmentationassignment_statecode.Active);
            DeleteActiveAssignments<bdgas_firewallchangesassignment>(
                bdgas_firewallchangesassignment.EntityLogicalName,
                bdgas_firewallchangesassignment.Fields.bdgas_appointmentid,
                bdgas_firewallchangesassignment.Fields.statecode,
                (int)bdgas_firewallchangesassignment_statecode.Active);
            DeleteActiveAssignments<bdgas_operatingenvironmentassignment>(
                bdgas_operatingenvironmentassignment.EntityLogicalName,
                bdgas_operatingenvironmentassignment.Fields.bdgas_appointmentid,
                bdgas_operatingenvironmentassignment.Fields.statecode,
                (int)bdgas_operatingenvironmentassignment_statecode.Active);
            DeleteActiveAssignments<bdgas_solutionarchitectureassignment>(
                bdgas_solutionarchitectureassignment.EntityLogicalName,
                bdgas_solutionarchitectureassignment.Fields.bdgas_appointmentid,
                bdgas_solutionarchitectureassignment.Fields.statecode,
                (int)bdgas_solutionarchitectureassignment_statecode.Active);
            DeleteActiveAssignments<bdgas_systemresideassignment>(
                bdgas_systemresideassignment.EntityLogicalName,
                bdgas_systemresideassignment.Fields.bdgas_appointmentid,
                bdgas_systemresideassignment.Fields.statecode,
                (int)bdgas_systemresideassignment_statecode.Active);

            // To update the email to this users only
            var allEmails = string.Join(";", GetEmails()?.Select(e => e?.bdgas_requiredemails + ";" + e?.bdgas_optionalemails));
            Target.bdgas_emailcancellation = string.Join(";", allEmails.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)?.Distinct()) ?? "NA";
        }

        private Model.Appointment[] GetEmails()
        {
            var query = new QueryExpression(Model.Appointment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Model.Appointment.Fields.bdgas_requiredemails, Model.Appointment.Fields.bdgas_optionalemails)
            };
            query.Criteria.AddCondition(Model.Appointment.Fields.OnlineMeetingId, ConditionOperator.Equal, Latest.OnlineMeetingId);
            query.Criteria.AddCondition(Model.Appointment.Fields.Id, ConditionOperator.NotEqual, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<Model.Appointment>()).ToArray();
        }

        private void DeleteActiveAssignments<T>(string entityName, string appointmentField, string stateField, int activeState)
            where T : Microsoft.Xrm.Sdk.Entity
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false)
            };
            query.Criteria.AddCondition(appointmentField, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(stateField, ConditionOperator.Equal, activeState);

            foreach (var assignment in AdminService.RetrieveMultiple(query).Entities)
            {
                var earlyBoundAssignment = assignment.ToEntity<T>();
                AdminService.Delete(earlyBoundAssignment.LogicalName, earlyBoundAssignment.Id);
            }
        }
    }
}
