using BD.Model;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xrm.Sdk.Query;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Maps business intelligence values onto a request intake.
    /// </summary>
    public class SetBDIntelligenceData : BusinessBase<Model.bdgas_requestintake>
    {
        public SetBDIntelligenceData(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var statusCode = Target.statuscode;
            if (statusCode != bdgas_requestintake_statuscode.ReadyforSprintProdMove) return;

            var configText = AdminService.GetEnvironmentVariable("bdgas_ARGOBDIntelligence");
            if (string.IsNullOrEmpty(configText)) return;

            var configs = JsonSerializer.Deserialize<JsonModel>(configText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Target.bdgas_bdintelligencestrategicalignment = SetLookup(configs.StrategicAlignmentId);
            Target.bdgas_bdintelligencetechnologiesused = SetLookup(configs.TechnologiesUsedId);
            Target.bdgas_bdintelligenceavailability = SetLookup(configs.AvailabilityId);
            Target.bdgas_bdintelligenceintegrationtype = SetLookup(configs.IntegrationTypeId);
            Target.bdgas_bdintelligenceproducttype = SetLookup(configs.ProductTypeId);

            var productOwner = GetSystemUser(Latest.bdgas_botowner.Id);
            Target.bdgas_bdintelligenceproductowner = productOwner.FullName;
            Target.bdgas_bdintelligenceproductemail = productOwner.InternalEMailAddress;

            foreach (var itemId in configs.ValueDriversId)
            {
                var newValueDriverAssignment = new bdgas_valuedriversassignment()
                {
                    bdgas_projectcenterid = new EntityReference(bdgas_requestintake.EntityLogicalName, Target.Id),
                    bdgas_ramasterid = SetLookup(itemId)
                };

                AdminService.Create(newValueDriverAssignment);
            }
        }

        private SystemUser GetSystemUser(Guid userId)
        {
            return AdminService.Retrieve(SystemUser.EntityLogicalName, userId, new ColumnSet(SystemUser.Fields.InternalEMailAddress, SystemUser.Fields.FullName)).ToEntity<SystemUser>();
        }

        private EntityReference SetLookup(Guid? id)
        {
            return id.HasValue ? new EntityReference(bdgas_regulatoryassessmentmaster.EntityLogicalName, id.Value) : null;
        }

        /// <summary>
        /// Represents business intelligence values received for a request intake.
        /// </summary>
        public class JsonModel
        {
            public Guid? StrategicAlignmentId { get; set; }
            public Guid? TechnologiesUsedId { get; set; }
            public Guid? AvailabilityId { get; set; }
            public Guid? IntegrationTypeId { get; set; }
            public Guid? ProductTypeId { get; set; }
            public List<Guid?> ValueDriversId { get; set; }
        }
    }
}
