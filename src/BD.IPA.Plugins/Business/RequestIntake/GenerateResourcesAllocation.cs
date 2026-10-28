using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Creates resource allocation records from request intake data.
    /// </summary>
    public class GenerateResourcesAllocation : BusinessBase<Model.bdgas_requestintake>
    {
        public GenerateResourcesAllocation(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var valid = Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.RPA &&
                Latest.statuscode == bdgas_requestintake_statuscode.SolutionDesign;
            if (!valid) return;

            var configText = AdminService.GetEnvironmentVariable("bdgas_argoresourcesallocation");
            if (string.IsNullOrEmpty(configText)) return;

            var configs = new JsonModel[] { };
            try
            {
                configs = System.Text.Json.JsonSerializer.Deserialize<JsonModel[]>(configText,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                // Do nothing
            }

            if (!configs.Any()) return;

            var applications = GetApplications();
            foreach (var app in applications)
            {
                if (app.bdgas_application == null) continue;

                var currentApp = configs.FirstOrDefault(e => e.ApplicationId == app.bdgas_application.Id);
                if (currentApp == null)
                {
                    var appData = AdminService.Retrieve(bdgas_Application.EntityLogicalName, app.bdgas_application.Id, new ColumnSet(bdgas_Application.Fields.bdgas_parentapplicationid)).ToEntity<bdgas_Application>();

                    currentApp = configs.FirstOrDefault(e => e.ApplicationId == appData.bdgas_parentapplicationid?.Id);

                    if (currentApp == null) continue;
                }

                var resourceAllocation = new bdgas_ResourceAllocation
                {
                    bdgas_requestintakeid = Latest.ToEntityReference(),
                    bdgas_application = new Microsoft.Xrm.Sdk.EntityReference(bdgas_Application.EntityLogicalName, currentApp.ApplicationId),
                    bdgas_percentage = currentApp.Percentage,
                    bdgas_pic = new Microsoft.Xrm.Sdk.EntityReference(SystemUser.EntityLogicalName, currentApp.PIC)
                };

                AdminService.Create(resourceAllocation);
            }
        }

        /// <summary>
        /// Represents a resource allocation entry from the configured request data.
        /// </summary>
        public class JsonModel
        {
            public Guid PIC { get; set; }
            public Guid ApplicationId { get; set; }
            public int Percentage { get; set; }
        }

        private bdgas_RequestIntakeApplication[] GetApplications()
        {
            var query = new QueryExpression(bdgas_RequestIntakeApplication.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_RequestIntakeApplication.Fields.bdgas_application)
            };
            query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.statecode, ConditionOperator.Equal, (int)bdgas_requestintakeapplication_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_RequestIntakeApplication>()).ToArray();
        }
    }
}
