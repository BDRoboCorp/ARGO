using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.RpaBPF
{
    /// <summary>
    /// Synchronizes the request intake status with its RPA process flow.
    /// </summary>
    public class SetRequestIntakeStatus : BusinessBase<bdgas_rpabpf>
    {
        public SetRequestIntakeStatus(ILocalPluginContext context, StateManager<bdgas_rpabpf> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;
            if (Latest.bpf_bdgas_requestintakeid == null) return;

            var processStageId = Latest.ActiveStageId;
            var currentStage = GetProcesses(Latest.ProcessId).FirstOrDefault(e => e.Id == processStageId?.Id);
            if (currentStage == null) return;

            var criteriaStatus = currentStage.StageName;
            var updateStatus = bdgas_requestintake_statuscode.PROD;
            var isHypercare = false;
            switch (criteriaStatus)
            {
                case "In Queue":
                    updateStatus = bdgas_requestintake_statuscode.InQueue;
                    break;
                case "Concept Approval":
                    updateStatus = bdgas_requestintake_statuscode.ConceptApproval;
                    break;
                case "Solution Design":
                    updateStatus = bdgas_requestintake_statuscode.SolutionDesign;
                    break;
                case "DEV":
                    updateStatus = bdgas_requestintake_statuscode.DEV;
                    break;
                case "QA":
                    updateStatus = bdgas_requestintake_statuscode.QA;
                    break;
                case "Ready For Sprint Prod Move":
                    updateStatus = bdgas_requestintake_statuscode.ReadyforSprintProdMove;
                    break;
                case "Hypercare":
                    updateStatus = bdgas_requestintake_statuscode.PROD;
                    isHypercare = true;
                    break;
                case "Prod":
                    updateStatus = bdgas_requestintake_statuscode.PROD;
                    break;
            }

            var update = new bdgas_requestintake
            {
                Id = Latest.bpf_bdgas_requestintakeid.Id,
                statuscode = updateStatus,
                statecode = bdgas_requestintake_statecode.Active,
                bdgas_botisinhypercare1 = isHypercare ? bdgas_requestintake_bdgas_botisinhypercare1.Yes : bdgas_requestintake_bdgas_botisinhypercare1.No
            };
            Service.Update(update);

        }

        private ProcessStage[] GetProcesses(Microsoft.Xrm.Sdk.EntityReference processId)
        {
            if (processId == null) return new ProcessStage[] { };

            var query = new QueryExpression(ProcessStage.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(ProcessStage.Fields.StageName)
            };
            query.Criteria.AddCondition(ProcessStage.Fields.ProcessId, ConditionOperator.Equal, processId.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<ProcessStage>()).ToArray();
        }
    }
}
