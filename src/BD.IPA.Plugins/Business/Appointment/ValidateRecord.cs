using Microsoft.Xrm.Sdk;
using System;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Validates appointment data against the configured validation rules.
    /// </summary>
    public class ValidateRecord : BusinessBase<Model.Appointment>
    {
        public ValidateRecord(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.bdgas_Registrant == null) return;

            var settingsText = AdminService.GetEnvironmentVariable("bdgas_regulatorylockconfiguration");
            var settings = new JsonModel();
            if (!string.IsNullOrEmpty(settingsText))
            {
                try
                {
                    settings = System.Text.Json.JsonSerializer.Deserialize<JsonModel>(settingsText,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    // Do nothing
                }
            }

            var registrantRef = Latest.bdgas_Registrant;
            var isRATeam = settings.RegulatoryAdmin != Guid.Empty ?
                AdminService.IsTeamMember(Context.PluginExecutionContext.InitiatingUserId, settings.RegulatoryAdmin) : false;
            var validUpdate = (registrantRef != null && Context.PluginExecutionContext.InitiatingUserId == registrantRef.Id) || isRATeam;

            if (!validUpdate && Context.PluginExecutionContext.Depth == 1)
            {
                throw new InvalidPluginExecutionException("This record can only be updated by the Registrant or the RA Admin Team.");
            }
        }

        /// <summary>
        /// Represents a validation rule loaded from configuration.
        /// </summary>
        public class JsonModel
        {
            public Guid RegulatoryAdmin { get; set; }
        }
    }
}
