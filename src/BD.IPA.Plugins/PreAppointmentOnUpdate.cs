using BD.IPA.Plugins.Business;
using BD.IPA.Plugins.Business.Appointment;
using BD.Model;
using Microsoft.Xrm.Sdk;
using System.Linq;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before an appointment is updated.
    /// </summary>
    public class PreAppointmentOnUpdate : PluginBase
    {
        public PreAppointmentOnUpdate() : base(typeof(PreAppointmentOnUpdate))
        {
        }
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<Appointment>(localPluginContext);

            new ValidateRecord(localPluginContext, stateManager).Execute();

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_requestintake) && stateManager.Target.bdgas_requestintake != null)
            {
                var existing = stateManager.Initial.bdgas_requestintake;
                if (existing != null)
                {
                    throw new InvalidPluginExecutionException("Booking could not be completed because this record was updated by another user. The selected time slot may no longer be available. Please refresh the page to view the latest availability and choose a different date or time slot.");
                }
            }

            if (stateManager.Target.Contains(Model.Appointment.Fields.bdgas_swapsraslotid) || stateManager.Target.Contains(Model.Appointment.Fields.RegardingObjectId) || stateManager.Target.Contains(Model.Appointment.Fields.bdgas_mydigitalsraid))
            {
                var count = 0;
                if (stateManager.Target.bdgas_mydigitalsraid != null) count++;
                if (stateManager.Target.bdgas_swapsraslotid != null) count++;
                if (stateManager.Target.RegardingObjectId != null && stateManager.Target.RegardingObjectId.LogicalName == bdgas_requestintake.EntityLogicalName) count++;

                if (count > 1) throw new InvalidPluginExecutionException("Invalid operation. Assignment of Digital SRA, Swap Slot, or Project Center is restricted to a single occurrence per record.");

                stateManager.Target.IsOnlineMeeting = true;
                stateManager.Target.bdgas_SlotStatus = bdgas_appointment_bdgas_slotstatus.Reserved;
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_Registrant) && stateManager.Target.bdgas_Registrant != null
                && stateManager.Latest.bdgas_SlotStatus == bdgas_appointment_bdgas_slotstatus.Available)
            {
                stateManager.Target.IsOnlineMeeting = true;
                stateManager.Target.bdgas_SlotStatus = bdgas_appointment_bdgas_slotstatus.Reserved;
            }

            if (localPluginContext.PluginExecutionContext.Depth == 1 &&
                stateManager.Target.Contains(Appointment.Fields.bdgas_SlotStatus) &&
                stateManager.Target.bdgas_SlotStatus == bdgas_appointment_bdgas_slotstatus.Reserved
                && stateManager.Latest.bdgas_Registrant == null)
            {
                stateManager.Target.bdgas_Registrant = new EntityReference(SystemUser.EntityLogicalName, localPluginContext.PluginExecutionContext.InitiatingUserId);
            }

            var emailMembersAttributes = new[] { Appointment.Fields.bdgas_Registrant, Appointment.Fields.bdgas_Presenter,
                Appointment.Fields.bdgas_BusinessOwner, Appointment.Fields.bdgas_BusinessPartner, Appointment.Fields.bdgas_TechnicalOwner,
                Appointment.Fields.bdgas_ValidationLead, Appointment.Fields.bdgas_validationlead2, Appointment.Fields.bdgas_OtherRequiredEmails,
                Appointment.Fields.bdgas_optionalemails};
            if (emailMembersAttributes.Any(attribute => stateManager.Target.Contains(attribute)))
            {
                new GenerateEmailMembers(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(Appointment.Fields.RegardingObjectId))
            {
                new RegisterProjectCenter(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_mydigitalsraid))
            {
                new ScheduleDigitalSRA(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_swapsraslotid))
            {
                new RescheduleSlot(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_systemresidetext))
            {
                new DeleteBDControlledSegmentation(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_authenticationmethodtext))
            {
                if (!(stateManager.Target.bdgas_authenticationmethodtext ?? "").ToLower().Contains("other"))
                {
                    stateManager.Target.bdgas_OtherAuthenticationMethod = "";
                }
            }

            if (localPluginContext.PluginExecutionContext.Depth == 1 && stateManager.Target.Contains(Appointment.Fields.StatusCode) && stateManager.Target.StatusCode == appointment_statuscode.Completed)
            {
                stateManager.Target.bdgas_SlotStatus = bdgas_appointment_bdgas_slotstatus.Complete;
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_SlotStatus))
            {
                if (stateManager.Target.bdgas_SlotStatus == bdgas_appointment_bdgas_slotstatus.Complete)
                {
                    stateManager.Target.StateCode = appointment_statecode.Completed;
                    stateManager.Target.StatusCode = appointment_statuscode.Completed;
                }
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_ConfigurationItemSerialNumber) &&
                !string.IsNullOrEmpty(stateManager.Target.bdgas_ConfigurationItemSerialNumber))
            {
                var serialNumber = stateManager.Target.bdgas_ConfigurationItemSerialNumber;
                var valid = (serialNumber.StartsWith("APP") && int.TryParse(serialNumber.Replace("APP", ""), out var _) && serialNumber.Length == 10) ||
                        (serialNumber.StartsWith("BOT -") && serialNumber.Length == 10);
                if (!valid)
                {
                    throw new InvalidPluginExecutionException("Configuration Item Serial Number must be blank or follow the format APP######## or BOT - ####");
                }
            }

            if (stateManager.Latest.bdgas_sratemplate.GetValueOrDefault() && stateManager.Target.Contains(Appointment.Fields.OnlineMeetingJoinUrl))
            {
                new CopyMeetingUrlForTheDay(localPluginContext, stateManager).Execute();
            }

            var assignedTaskAttributes = new[] { Appointment.Fields.bdgas_assignedsubject, Appointment.Fields.bdgas_assignedduedate, Appointment.Fields.bdgas_assigneddescription };
            if (assignedTaskAttributes.Any(attribute => stateManager.Target.Contains(attribute)))
            {
                new CreateTask(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_SlotStatus))
            {
                new CancelSlot(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Attributes.Any(e => e.Key.ToLower().EndsWith("id") || e.Key.ToLower().EndsWith("text")))
            {
                new SetSRADocumentFormattedFields(localPluginContext, stateManager).Execute();
            }

            var updateProjectCenterAttributes = new[] {
                Appointment.Fields.bdgas_gxpapplicableid,
                Appointment.Fields.bdgas_gxpapplicabilityid,
                Appointment.Fields.bdgas_soxapplicableid,
                Appointment.Fields.bdgas_piistoredormanagedintheappid,
                Appointment.Fields.bdgas_drplanrequiredfromcmdbbusinessappid,
                Appointment.Fields.bdgas_otherregulationsorlegalconstraintimpactid
            };
            if (stateManager.Target.Attributes.Any(e => updateProjectCenterAttributes.Contains(e.Key)))
            {
                new UpdateProjectCenterAttributes(localPluginContext, stateManager).Execute();
            }
        }
    }
}
