using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.ProjectMilestoneApproval
{
    /// <summary>
    /// Advances the approval flow after a milestone approval is processed.
    /// </summary>
    public class ContinueApprovalFlow : BusinessBase<bdgas_projectmilestoneapprover>
    {
        public ContinueApprovalFlow(ILocalPluginContext context, StateManager<bdgas_projectmilestoneapprover> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var milestoneRef = Latest.bdgas_projectmilestoneid;
            if (milestoneRef == null) return;

            var milestone = AdminService.Retrieve(bdgas_projectmilestone.EntityLogicalName, milestoneRef.Id, new ColumnSet(true)).ToEntity<bdgas_projectmilestone>();
            var milestoneTypeRef = milestone.bdgas_milestonetypeid;
            if (milestoneTypeRef == null) return;

            var milestoneType = AdminService.Retrieve(bdgas_milestonetype.EntityLogicalName, milestoneTypeRef.Id, new ColumnSet(bdgas_milestonetype.Fields.bdgas_approvaltype, bdgas_milestonetype.Fields.bdgas_isapprovalneeded,
                bdgas_milestonetype.Fields.bdgas_approvalsource)).ToEntity<bdgas_milestonetype>();

            if (!milestoneType.bdgas_isapprovalneeded.GetValueOrDefault()) return;

            var approvalType = milestoneType.bdgas_approvaltype;
            var approvalSource = milestoneType.bdgas_approvalsource;

            if (string.IsNullOrEmpty(approvalSource) || approvalType == null)
            {
                throw new InvalidPluginExecutionException("Approval source or approval type is not defined for the milestone type.");
            }

            // Latest merges Target on every access, so capture the incoming status before Target is modified.
            var latestStatus = Latest.statuscode;

            Target.statecode = bdgas_projectmilestoneapprover_statecode.Inactive;

            ApprovalHelper.ApproverModel[] approvers = null;
            if (latestStatus == bdgas_projectmilestoneapprover_statuscode.Approved1)
            {
                Target.statuscode = bdgas_projectmilestoneapprover_statuscode.Approved;

                approvers = new ApprovalHelper(Context).ParseApprovers(AdminService, milestone, approvalSource)
                    .OrderBy(e => e.Sequence ?? int.MaxValue)
                    .ToArray();

                var isChargeBackStep = approvalType == bdgas_milestonetype_bdgas_approvaltype.Sequence &&
                    Latest.bdgas_sequence.HasValue &&
                    approvers.Any(e => e.IsChargeBack && e.Sequence == Latest.bdgas_sequence);

                if (isChargeBackStep && !IsChargeBackStepComplete(approvers))
                {
                    return;
                }
            }

            DeleteApprovalRecords();

            if (latestStatus == bdgas_projectmilestoneapprover_statuscode.Approved1)
            {
                ApprovalHelper.ApproverModel approver = null;
                if (Latest.bdgas_sequence.HasValue && approvalType == bdgas_milestonetype_bdgas_approvaltype.Sequence)
                {
                    var nextSequence = Latest.bdgas_sequence + 1;
                    var tempApprovers = approvers.Where(e => e.Sequence == nextSequence).ToArray();

                    for (int i = 0; i < tempApprovers.Length; i++)
                    {
                        if (i == 0)
                        {
                            approver = tempApprovers[i];
                        }
                        else
                        {
                            CreateNextApprovalRecord(tempApprovers[i], approvalType);
                        }
                    }
                }
                else if (approvalType == bdgas_milestonetype_bdgas_approvaltype.Parallel)
                {
                    var allApproved = GetApprovedProjectMilestoneApprover().Concat(new[] { Latest }).ToArray();
                    var isApprovedAll = true;
                    foreach (var approverParralel in approvers)
                    {
                        if (!allApproved.Any(e => e.OwnerId?.Id == approverParralel.Approver?.Id &&
                            e.OwnerId?.LogicalName == approverParralel.Approver?.LogicalName))
                        {
                            isApprovedAll = false;
                            break;
                        }
                    }

                    if (!isApprovedAll) return;
                    approver = null;
                }
                else if (approvalType == bdgas_milestonetype_bdgas_approvaltype.AnyApprover)
                {
                    approver = null;
                }

                if (approver == null)
                {
                    var approverName = Latest.OwnerId?.Name ?? AdminService.Retrieve(SystemUser.EntityLogicalName, Latest.OwnerId.Id, new ColumnSet(SystemUser.Fields.FullName)).ToEntity<SystemUser>()?.FullName;

                    var projectmileStone = new bdgas_projectmilestone
                    {
                        Id = milestoneRef.Id,
                        statuscode = bdgas_projectmilestone_statuscode.Approved,
                        statecode = bdgas_projectmilestone_statecode.Inactive,
                        bdgas_approvalhistory = $"{approverName}: {Latest.bdgas_comments}"
                    };
                    Service.Update(projectmileStone);
                    return;
                }
                else
                {
                    CreateNextApprovalRecord(approver, approvalType);
                }
            }
            else if (latestStatus == bdgas_projectmilestoneapprover_statuscode.Rejected1)
            {
                Target.statuscode = bdgas_projectmilestoneapprover_statuscode.Rejected;

                var approverName = Latest.OwnerId?.Name ?? AdminService.Retrieve(SystemUser.EntityLogicalName, Latest.OwnerId.Id, new ColumnSet(SystemUser.Fields.FullName)).ToEntity<SystemUser>()?.FullName;

                var update = new bdgas_projectmilestone
                {
                    Id = milestoneRef.Id,
                    statuscode = bdgas_projectmilestone_statuscode.RevisionNeeded,
                    statecode = bdgas_projectmilestone_statecode.Active,
                    bdgas_approvalhistory = $"{approverName}: {Latest.bdgas_comments}"
                };
                Service.Update(update);
            }
        }

        // In a ChargeBack step, all distinct finance POCs plus any one other approver must approve.
        private bool IsChargeBackStepComplete(ApprovalHelper.ApproverModel[] approvers)
        {
            var stepApprovers = approvers.Where(e => e.Sequence == Latest.bdgas_sequence).ToArray();
            var chargeBackApprovers = stepApprovers.Where(e => e.IsChargeBack && e.Approver != null).Select(e => e.Approver).ToArray();
            if (chargeBackApprovers.Length == 0) return true;

            var otherApprovers = stepApprovers.Where(e => !e.IsChargeBack && e.Approver != null).Select(e => e.Approver).ToArray();

            var approvedOwners = GetApprovedOwnersForSequence(Latest.bdgas_sequence.Value)
                .Concat(new[] { Latest.OwnerId })
                .Where(e => e != null)
                .ToArray();

            bool IsApproved(EntityReference reference) =>
                approvedOwners.Any(e => e.Id == reference.Id && e.LogicalName == reference.LogicalName);

            var isOtherApproved = otherApprovers.Length == 0 || otherApprovers.Any(IsApproved);
            if (chargeBackApprovers.All(IsApproved) && isOtherApproved) return true;

            if (otherApprovers.Length > 0 && isOtherApproved)
            {
                DeleteApprovalRecords(owner => chargeBackApprovers.Any(e => e.Id == owner?.Id && e.LogicalName == owner?.LogicalName));
            }

            return false;
        }

        private EntityReference[] GetApprovedOwnersForSequence(int sequence)
        {
            var query = new QueryExpression(bdgas_projectmilestoneapprover.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_projectmilestoneapprover.Fields.OwnerId)
            };
            // Approved records are saved as Confirmed by this plugin.
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.statuscode, ConditionOperator.In,
                (int)bdgas_projectmilestoneapprover_statuscode.Approved, (int)bdgas_projectmilestoneapprover_statuscode.Approved1);
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_projectmilestoneid, ConditionOperator.Equal, Latest.bdgas_projectmilestoneid?.Id);
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_sequence, ConditionOperator.Equal, sequence);
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_projectmilestoneapproverId, ConditionOperator.NotEqual, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);
            return result.Entities.Select(e => e.ToEntity<bdgas_projectmilestoneapprover>().OwnerId).ToArray();
        }

        private void DeleteApprovalRecords(Func<EntityReference, bool> shouldKeep = null)
        {
            var query = new QueryExpression(bdgas_projectmilestoneapprover.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_projectmilestoneapprover.Fields.OwnerId)
            };
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_projectmilestoneapproverId, ConditionOperator.NotEqual, Latest.Id);
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_projectmilestoneid, ConditionOperator.Equal, Latest.bdgas_projectmilestoneid?.Id);
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.statuscode, ConditionOperator.In, (int)bdgas_projectmilestoneapprover_statuscode.InQueue);
            if (Latest.bdgas_sequence.HasValue)
            {
                query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_sequence, ConditionOperator.Equal, Latest.bdgas_sequence.GetValueOrDefault());
            }

            var result = AdminService.RetrieveMultiple(query);
            if (!result.Entities.Any()) return;

            foreach (var entity in result.Entities)
            {
                if (shouldKeep != null && shouldKeep(entity.GetAttributeValue<EntityReference>(bdgas_projectmilestoneapprover.Fields.OwnerId))) continue;

                AdminService.Delete(entity.LogicalName, entity.Id);
            }
        }

        private void CreateNextApprovalRecord(ApprovalHelper.ApproverModel approver, bdgas_milestonetype_bdgas_approvaltype? approvalType)
        {
            if (approver?.Approver == null)
            {
                throw new InvalidPluginExecutionException("Next approver reference is missing while creating the milestone approval record.");
            }

            var create = new bdgas_projectmilestoneapprover
            {
                bdgas_sequence = approvalType == bdgas_milestonetype_bdgas_approvaltype.Sequence ? approver.Sequence : default,
                bdgas_projectmilestoneid = Latest.bdgas_projectmilestoneid,
                OwnerId = approver.Approver,
                statecode = bdgas_projectmilestoneapprover_statecode.Active,
                statuscode = bdgas_projectmilestoneapprover_statuscode.InQueue
            };

            AdminService.Create(create);
        }

        private bdgas_projectmilestoneapprover[] GetApprovedProjectMilestoneApprover()
        {
            var query = new QueryExpression(bdgas_projectmilestoneapprover.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_projectmilestone.Fields.OwnerId)
            };
            // Approved records are saved as Confirmed by this plugin.
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.statuscode, ConditionOperator.In,
                (int)bdgas_projectmilestoneapprover_statuscode.Approved, (int)bdgas_projectmilestoneapprover_statuscode.Approved1);
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_projectmilestoneid, ConditionOperator.Equal, Latest.bdgas_projectmilestoneid?.Id);
            query.Criteria.AddCondition(bdgas_projectmilestoneapprover.Fields.bdgas_projectmilestoneapproverId, ConditionOperator.NotEqual, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);
            return result.Entities.Select(e => e.ToEntity<bdgas_projectmilestoneapprover>()).ToArray();
        }
    }
}
