using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Applies request intake processing when its status changes.
    /// </summary>
    public class OnStatusChanged : BusinessBase<bdgas_requestintake>
    {
        public OnStatusChanged(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var status = Target.statuscode;
            if (status == null) return;

            var masterMilestones = AdminService.GetMilestonesByRequestIntake(Initial).Where(e => e.bdgas_requestintakestatus == GetCurrentStatus()).ToArray();
            var currentMilestones = GetCurrentMilestones();

            foreach (var milestonetype in masterMilestones)
            {
                var isExists = currentMilestones.Any(e => e.bdgas_milestonetypeid?.Id == milestonetype.Id && e.statuscode == bdgas_projectmilestone_statuscode.Approved);
                if (!isExists)
                    throw new InvalidPluginExecutionException($"Action cannot be performed until milestone '{milestonetype.bdgas_milestonetype1}' is successfully achieved.");
            }
        }

        private bdgas_milestonetype_bdgas_requestintakestatus? GetCurrentStatus()
        {
            switch (Latest.statuscode)
            {
                case bdgas_requestintake_statuscode.InQueue: return bdgas_milestonetype_bdgas_requestintakestatus.InQueue;
                case bdgas_requestintake_statuscode.ConceptApproval: return bdgas_milestonetype_bdgas_requestintakestatus.InQueue;
                case bdgas_requestintake_statuscode.SolutionDesign: return bdgas_milestonetype_bdgas_requestintakestatus.ConceptApproval;
                case bdgas_requestintake_statuscode.DEV: return bdgas_milestonetype_bdgas_requestintakestatus.SolutionDesign;
                case bdgas_requestintake_statuscode.QA: return bdgas_milestonetype_bdgas_requestintakestatus.Dev;
                case bdgas_requestintake_statuscode.ReadyforSprintProdMove: return bdgas_milestonetype_bdgas_requestintakestatus.QA;
                case bdgas_requestintake_statuscode.PROD: return bdgas_milestonetype_bdgas_requestintakestatus.ReadyforSprintProdMove;
                default: return bdgas_milestonetype_bdgas_requestintakestatus.PROD;
            }
        }

        private bdgas_projectmilestone[] GetCurrentMilestones()
        {
            var query = new QueryExpression(bdgas_projectmilestone.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_projectmilestone.Fields.bdgas_milestonetypeid, bdgas_projectmilestone.Fields.bdgas_requestintakeid, bdgas_projectmilestone.Fields.statuscode)
            };
            query.Criteria.AddCondition(bdgas_projectmilestone.Fields.bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_projectmilestone.Fields.statuscode, ConditionOperator.Equal, (int)bdgas_projectmilestone_statuscode.Approved);
            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_projectmilestone>()).ToArray();
        }
    }
}
