using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.ProjectMilestone
{
    /// <summary>
    /// Creates the approval flow for a project milestone.
    /// </summary>
    public class GenerateApprovalFlow : BusinessBase<bdgas_projectmilestone>
    {
        public GenerateApprovalFlow(ILocalPluginContext context, StateManager<bdgas_projectmilestone> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.statuscode != bdgas_projectmilestone_statuscode.Submitted) return;

            var milestoneTypeRef = Latest.bdgas_milestonetypeid;
            if (milestoneTypeRef == null) return;

            var milestoneType = AdminService.Retrieve(bdgas_milestonetype.EntityLogicalName, milestoneTypeRef.Id, new ColumnSet(bdgas_milestonetype.Fields.bdgas_approvaltype, bdgas_milestonetype.Fields.bdgas_isapprovalneeded,
                bdgas_milestonetype.Fields.bdgas_approvalsource)).ToEntity<bdgas_milestonetype>();

            if (!milestoneType.bdgas_isapprovalneeded.GetValueOrDefault())
            {
                Target.statuscode = bdgas_projectmilestone_statuscode.Approved;
                Target.statecode = bdgas_projectmilestone_statecode.Inactive;
                return;
            }

            var approvalType = milestoneType.bdgas_approvaltype;
            var approvalSource = milestoneType.bdgas_approvalsource;

            if (string.IsNullOrEmpty(approvalSource) || approvalType == null)
            {
                throw new InvalidPluginExecutionException("Approval source or approval type is not defined for the milestone type.");
            }

            var approvers = new ApprovalHelper(Context).ParseApprovers(AdminService, Latest, approvalSource)
                .OrderBy(e => e.Sequence ?? int.MaxValue)
                .ToArray();
            if (approvers.Length == 0)
            {
                throw new InvalidPluginExecutionException($"No approvers resolved from approval source '{approvalSource}' for milestone type '{milestoneTypeRef.Id}'.");
            }

            int? minSeq = null;
            if (approvalType == bdgas_milestonetype_bdgas_approvaltype.Sequence)
            {
                var sequenceApprovers = approvers.Where(e => e.Sequence.HasValue).ToArray();
                if (sequenceApprovers.Length == 0)
                {
                    throw new InvalidPluginExecutionException($"Approval type is Sequence but no sequence values were resolved from approval source '{approvalSource}' for milestone type '{milestoneTypeRef.Id}'.");
                }

                minSeq = sequenceApprovers.Min(e => e.Sequence.Value);
            }

            foreach (var approver in approvers)
            {
                if (approvalType == bdgas_milestonetype_bdgas_approvaltype.Sequence && approver.Sequence != minSeq)
                    continue;

                CreateApprovalRecord(approver, approvalType);
            }
        }

        private void CreateApprovalRecord(ApprovalHelper.ApproverModel approver, bdgas_milestonetype_bdgas_approvaltype? approvalType)
        {
            if (approver?.Approver == null)
            {
                throw new InvalidPluginExecutionException("Approver reference is missing while creating the milestone approval record.");
            }

            var create = new bdgas_projectmilestoneapprover
            {
                bdgas_sequence = approvalType == bdgas_milestonetype_bdgas_approvaltype.Sequence ? approver.Sequence : default,
                bdgas_projectmilestoneid = Latest.ToEntityReference(),
                OwnerId = approver.Approver,
                statecode = bdgas_projectmilestoneapprover_statecode.Active,
                statuscode = bdgas_projectmilestoneapprover_statuscode.InQueue
            };

            AdminService.Create(create);
        }
    }
}
