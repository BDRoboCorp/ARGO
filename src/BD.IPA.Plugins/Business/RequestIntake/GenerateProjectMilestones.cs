using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Creates project milestones from the request intake configuration.
    /// </summary>
    public class GenerateProjectMilestones : BusinessBase<bdgas_requestintake>
    {
        public GenerateProjectMilestones(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var status = Target.statuscode;
            if (status == null) return;
            
            var masterMilestones = AdminService.GetMilestonesByRequestIntake(Latest)
                .Where(e => e.bdgas_requestintakestatus == GetCurrentStatus()).ToArray();
            var currentMilestones = GetCurrentMilestones();
            foreach (var milestone in masterMilestones)
            {
                if (currentMilestones.Any(e => e.bdgas_milestonetypeid?.Id == milestone.Id)) continue;

                var projectMilestone = new bdgas_projectmilestone
                {
                    bdgas_milestonetypeid = milestone.ToEntityReference(),
                    bdgas_requestintakeid = Latest.ToEntityReference()
                };

                AdminService.Create(projectMilestone);
            }
        }

        private bdgas_milestonetype_bdgas_requestintakestatus? GetCurrentStatus()
        {
            switch (Latest.statuscode)
            {
                case bdgas_requestintake_statuscode.InQueue: return bdgas_milestonetype_bdgas_requestintakestatus.InQueue;
                case bdgas_requestintake_statuscode.ConceptApproval: return bdgas_milestonetype_bdgas_requestintakestatus.ConceptApproval;
                case bdgas_requestintake_statuscode.SolutionDesign: return bdgas_milestonetype_bdgas_requestintakestatus.SolutionDesign;
                case bdgas_requestintake_statuscode.DEV: return bdgas_milestonetype_bdgas_requestintakestatus.Dev;
                case bdgas_requestintake_statuscode.QA: return bdgas_milestonetype_bdgas_requestintakestatus.QA;
                case bdgas_requestintake_statuscode.ReadyforSprintProdMove: return bdgas_milestonetype_bdgas_requestintakestatus.ReadyforSprintProdMove;
                case bdgas_requestintake_statuscode.PROD: return bdgas_milestonetype_bdgas_requestintakestatus.PROD;
            }

            return bdgas_milestonetype_bdgas_requestintakestatus.InQueue;
        }

        private bdgas_projectmilestone[] GetCurrentMilestones()
        {
            var query = new QueryExpression(bdgas_projectmilestone.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_projectmilestone.Fields.bdgas_milestonetypeid, bdgas_projectmilestone.Fields.bdgas_requestintakeid)
            };
            query.Criteria.AddCondition(bdgas_projectmilestone.Fields.bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);
            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_projectmilestone>()).ToArray();
        }
    }
}
