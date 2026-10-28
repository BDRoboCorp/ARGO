using BD.IPA.Plugins.Business;
using BD.IPA.Plugins.Business.RequestIntake;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a request intake is updated.
    /// </summary>
    public class PreRequestIntakeOnUpdate : PluginBase
    {
        public PreRequestIntakeOnUpdate() : base(typeof(PreRequestIntakeOnCreate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_requestintake>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_assigneeba) && stateManager.Target.bdgas_assigneeba != null &&
                stateManager.Latest.statuscode == bdgas_requestintake_statuscode.InQueue)
            {
                stateManager.Target.statuscode = bdgas_requestintake_statuscode.ConceptApproval;
                stateManager.Target.statecode = bdgas_requestintake_statecode.Active;
            }

            if (stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_digitalproductname))
            {
                new GenerateConfigurationItemUrl(localPluginContext, stateManager).Execute();
            }

            if (stateManager.Target.Contains(bdgas_requestintake.Fields.statuscode))
            {
                new OnStatusChanged(localPluginContext, stateManager).Execute();

                if (localPluginContext.AdminService.IsRpaScenario(stateManager.Latest.Id))
                {
                    new GenerateResourcesAllocation(localPluginContext, stateManager).Execute();
                    new GenerateProjectMilestones(localPluginContext, stateManager).Execute();
                    new GenerateProjectEstimation(localPluginContext, stateManager).Execute();
                    new SetBudgetRequestToVerified(localPluginContext, stateManager).Execute();
                    new SetBDIntelligenceData(localPluginContext, stateManager).Execute();
                }
                else if (stateManager.Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.QPA)
                {
                    new GenerateProjectEstimation(localPluginContext, stateManager).Execute();
                }
            }

            if (stateManager.Target.Contains(bdgas_requestintake.Fields.statuscode) || stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_botisinhypercare1))
            {
                new SetBpfStatus(localPluginContext, stateManager).Execute();
            }


            if (!localPluginContext.AdminService.IsRpaScenario(stateManager.Latest.Id)) return;

            if (stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_businessvaluescoreid) ||
                stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_developmentsizeid) ||
                stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_reusabilityscoreid) ||
                stateManager.Target.Contains(bdgas_requestintake.Fields.bdgas_valuerealizationscoreid)
                )
            {
                new SetOpportunityScore(localPluginContext, stateManager).Execute();
            }
        }
    }
}
