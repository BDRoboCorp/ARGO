using BD.IPA.Plugins.Business;
using BD.IPA.Plugins.Business.Appointment;
using BD.Model;
using System;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before an appointment is created.
    /// </summary>
    public class PreAppointmentOnCreate : PluginBase
    {
        public PreAppointmentOnCreate() : base(typeof(PreAppointmentOnCreate))
        {
        }
        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<Appointment>(localPluginContext);
            new GenerateSraMeetings(localPluginContext, stateManager).Execute();

            if (stateManager.Target.Contains(Appointment.Fields.bdgas_SystemDesciption))
            {
                var timezoneString = TimeZoneDefinitionQueries.GetTimezoneByUserId(localPluginContext.PluginExecutionContext.InitiatingUserId, localPluginContext.AdminService);
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneString);
                var now = timezone.ConvertToLocal(DateTime.UtcNow).Date;

                stateManager.Target.Subject = $"SRA_{now:MM-dd-yyyy}";
                stateManager.Target.bdgas_VersionNumber = "1.0";

                new SetAutonumber(localPluginContext, stateManager).Execute();
            }
        }
    }
}
