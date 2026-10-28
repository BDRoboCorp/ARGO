using BD.Model;
using Microsoft.Xrm.Sdk;
using System;
using System.Text.Json;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Applies Planview project data to a request intake.
    /// </summary>
    public class SetPlanviewData : BusinessBase<Model.bdgas_requestintake>
    {
        public SetPlanviewData(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var configText = AdminService.GetEnvironmentVariable("bdgas_ARGOPlanviewDefaultValues");
            if (string.IsNullOrEmpty(configText)) return;

            var configs = JsonSerializer.Deserialize<JsonModel>(configText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Target.bdgas_businessvaluescoreid = SetLookup(configs.BusinessValueScoreId);
            Target.bdgas_reusabilityscoreid = SetLookup(configs.ReusabilityScoreId);
            Target.bdgas_valuerealizationscoreid = SetLookup(configs.ValueRealizationScoreId);
            Target.bdgas_businesscapabilitystatusid = SetLookup(configs.BusinessCapabilityStatusId);
            Target.bdgas_benefittypeid = SetLookup(configs.BenefitTypeId);
            Target.bdgas_bdstrategyalignmentid = SetLookup(configs.BDStrategyAlignmentId);
            Target.bdgas_roadmapalignmentid = SetLookup(configs.RoadmapAlignmentId);
            Target.bdgas_BenefitsAnticipated = GetLineFromMultilineText(Latest.bdgas_description, 3);
        }

        private string GetLineFromMultilineText(string multilineText, int lineNumber)
        {
            if (string.IsNullOrEmpty(multilineText))
                return string.Empty;

            var lines = multilineText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int index = lineNumber - 1;

            if (index < 0 || index >= lines.Length)
                return string.Empty;

            return lines[index];
        }

        private EntityReference SetLookup(Guid? id)
        {
            return id.HasValue ? new EntityReference(bdgas_regulatoryassessmentmaster.EntityLogicalName, id.Value) : null;
        }

        /// <summary>
        /// Represents the Planview values received for a request intake.
        /// </summary>
        public class JsonModel
        {
            public Guid? BusinessValueScoreId { get; set; }
            public Guid? ReusabilityScoreId { get; set; }
            public Guid? ValueRealizationScoreId { get; set; }
            public Guid? BusinessCapabilityStatusId { get; set; }
            public Guid? BenefitTypeId { get; set; }
            public Guid? BDStrategyAlignmentId { get; set; }
            public Guid? RoadmapAlignmentId { get; set; }
            public string OutOfScope { get; set; }
        }
    }
}
