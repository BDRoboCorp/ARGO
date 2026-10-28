using System;
using System.Collections.Generic;
using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Synchronizes request intake status with its business process flow.
    /// </summary>
    public class SetBpfStatus : BusinessBase<Model.bdgas_requestintake>
    {
        public SetBpfStatus(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;
            if (Latest.bdgas_RequestIntakeType != bdgas_requestintake_bdgas_requestintaketype.QPA)
            {
                var statusCode = Target.statuscode;
                if (statusCode == null) return;

                var rpaBpf = GetRpaBpf();
                if (rpaBpf == null) return;

                var criteriaStatus = "Prod";
                switch (statusCode.Value)
                {
                    case bdgas_requestintake_statuscode.InQueue:
                        criteriaStatus = "In Queue";
                        break;
                    case bdgas_requestintake_statuscode.ConceptApproval:
                        criteriaStatus = "Concept Approval";
                        break;
                    case bdgas_requestintake_statuscode.SolutionDesign:
                        criteriaStatus = "Solution Design";
                        break;
                    case bdgas_requestintake_statuscode.DEV:
                        criteriaStatus = "DEV";
                        break;
                    case bdgas_requestintake_statuscode.QA:
                        criteriaStatus = "QA";
                        break;
                    case bdgas_requestintake_statuscode.ReadyforSprintProdMove:
                        criteriaStatus = "Ready For Sprint Prod Move";
                        break;
                    case bdgas_requestintake_statuscode.PROD:
                        if (Latest.bdgas_botisinhypercare1 == bdgas_requestintake_bdgas_botisinhypercare1.Yes)
                        {
                            criteriaStatus = "Hypercare";
                        }
                        else
                        {
                            criteriaStatus = "Prod";
                        }
                        break;
                }
                var processes = GetProcesses(rpaBpf.ProcessId).ToArray();
                var selectedProcess = processes.FirstOrDefault(e => e.StageName == criteriaStatus);
                if (selectedProcess == null) return;

                var path = BuildTraversedPath(processes, selectedProcess);
                Context.Trace($"Path: {path}..");
                ReactivateBpfIfNeeded(rpaBpf);

                var update = new bdgas_rpabpf
                {
                    Id = rpaBpf.Id,
                    ActiveStageId = selectedProcess.ToEntityReference(),
                    TraversedPath = path
                };
                AdminService.Update(update);
            }
            else if (Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.QPA)
            {
                var statusCode = Target.statuscode;
                if (statusCode == null) return;

                var qpaBpf = GetQpaBpf();
                if (qpaBpf == null) return;

                var criteriaStatus = "Prod";
                switch (statusCode.Value)
                {
                    case bdgas_requestintake_statuscode.InQueue:
                        criteriaStatus = "In Queue";
                        break;
                    case bdgas_requestintake_statuscode.ConceptApproval:
                        criteriaStatus = "Concept Approval";
                        break;
                    case bdgas_requestintake_statuscode.DEV:
                        criteriaStatus = "DEV";
                        break;
                    case bdgas_requestintake_statuscode.QA:
                        criteriaStatus = "QA";
                        break;
                    case bdgas_requestintake_statuscode.PROD:
                        if (Latest.bdgas_botisinhypercare1 == bdgas_requestintake_bdgas_botisinhypercare1.Yes)
                        {
                            criteriaStatus = "Hypercare";
                        }
                        else
                        {
                            criteriaStatus = "PROD";
                        }
                        break;
                }
                var processes = GetProcesses(qpaBpf.ProcessId).ToArray();
                var selectedProcess = processes.FirstOrDefault(e => e.StageName == criteriaStatus);

                if (selectedProcess == null) return;

                var path = BuildTraversedPath(processes, selectedProcess);

                Context.Trace($"Path: {path}..");
                ReactivateBpfIfNeeded(qpaBpf);

                var update = new bdgas_qpabpf
                {
                    Id = qpaBpf.Id,
                    ActiveStageId = selectedProcess.ToEntityReference(),
                    TraversedPath = path
                };
                AdminService.Update(update);
            }
        }

        private ProcessStage[] GetProcesses(Microsoft.Xrm.Sdk.EntityReference processId)
        {
            if (processId == null) return new ProcessStage[] { };

            var query = new QueryExpression(ProcessStage.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(ProcessStage.Fields.StageName, ProcessStage.Fields.ParentProcessStageId)
            };
            query.Criteria.AddCondition(ProcessStage.Fields.ProcessId, ConditionOperator.Equal, processId.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<ProcessStage>()).ToArray();
        }

        private string BuildTraversedPath(ProcessStage[] processes, ProcessStage selectedProcess)
        {
            var processesById = processes.ToDictionary(e => e.Id);
            var path = new List<Guid>();
            var currentProcess = selectedProcess;

            while (currentProcess != null && currentProcess.Id != Guid.Empty && !path.Contains(currentProcess.Id))
            {
                path.Insert(0, currentProcess.Id);

                var parentId = currentProcess.ParentProcessStageId?.Id;
                currentProcess = parentId.HasValue && processesById.ContainsKey(parentId.Value)
                    ? processesById[parentId.Value]
                    : null;
            }

            var firstProcess = processes.FirstOrDefault(e => e.StageName == "In Queue");
            if (firstProcess != null)
            {
                path.Remove(firstProcess.Id);
                path.Insert(0, firstProcess.Id);
            }

            return string.Join(",", path);
        }

        private void ReactivateBpfIfNeeded(bdgas_qpabpf bpf)
        {
            if (bpf.statecode != bdgas_qpabpf_statecode.Inactive) return;

            AdminService.Update(new bdgas_qpabpf
            {
                Id = bpf.Id,
                statecode = bdgas_qpabpf_statecode.Active,
                statuscode = bdgas_qpabpf_statuscode.Active
            });
        }

        private void ReactivateBpfIfNeeded(bdgas_rpabpf bpf)
        {
            if (bpf.statecode != bdgas_rpabpf_statecode.Inactive) return;

            AdminService.Update(new bdgas_rpabpf
            {
                Id = bpf.Id,
                statecode = bdgas_rpabpf_statecode.Active,
                statuscode = bdgas_rpabpf_statuscode.Active
            });
        }

        private bdgas_qpabpf GetQpaBpf()
        {
            var query = new QueryExpression(bdgas_qpabpf.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_qpabpf.Fields.ProcessId, bdgas_qpabpf.Fields.ActiveStageId, bdgas_qpabpf.Fields.TraversedPath, bdgas_qpabpf.Fields.statecode, bdgas_qpabpf.Fields.statuscode),
                TopCount = 1
            };
            query.Criteria.AddCondition(bdgas_qpabpf.Fields.bpf_bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.FirstOrDefault()?.ToEntity<bdgas_qpabpf>();
        }

        private bdgas_rpabpf GetRpaBpf()
        {
            var query = new QueryExpression(bdgas_rpabpf.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_rpabpf.Fields.ProcessId, bdgas_rpabpf.Fields.ActiveStageId, bdgas_rpabpf.Fields.TraversedPath, bdgas_rpabpf.Fields.statecode, bdgas_rpabpf.Fields.statuscode),
                TopCount = 1
            };
            query.Criteria.AddCondition(bdgas_rpabpf.Fields.bpf_bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.FirstOrDefault()?.ToEntity<bdgas_rpabpf>();
        }
    }
}
