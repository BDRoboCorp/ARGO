using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Calculates and assigns the opportunity score for a request intake.
    /// </summary>
    public class SetOpportunityScore : BusinessBase<Model.bdgas_requestintake>
    {
        public SetOpportunityScore(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) :
            base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var businessValueScore = GetScore(Latest.bdgas_businessvaluescoreid);
            var developmentSize = GetScore(Latest.bdgas_developmentsizeid);
            var reusabilityScore = GetScore(Latest.bdgas_reusabilityscoreid);
            var valueRealizationScore = GetScore(Latest.bdgas_valuerealizationscoreid);

            Target.bdgas_opportunityscore = (businessValueScore + developmentSize + reusabilityScore) * valueRealizationScore;
        }

        private int GetScore(EntityReference entityRef)
        {
            if (entityRef == null) return 0;

            var name = entityRef.Name;
            if (string.IsNullOrEmpty(entityRef.Name)) name = GetRAMasterName(entityRef.Id);

            var parseResult = int.TryParse(name.Substring(name.Length - 3).Trim(')', '('), out var result);
            return parseResult ? result : 0;
        }

        private string GetRAMasterName(Guid entityId)
        {
            return AdminService.Retrieve(bdgas_regulatoryassessmentmaster.EntityLogicalName,
                entityId, new ColumnSet(bdgas_regulatoryassessmentmaster.Fields.bdgas_name)).ToEntity<bdgas_regulatoryassessmentmaster>().bdgas_name;
        }
    }
}
