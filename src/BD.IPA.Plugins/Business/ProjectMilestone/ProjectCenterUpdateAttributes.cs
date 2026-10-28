using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.ProjectMilestone
{
    /// <summary>
    /// Updates project center attributes on a milestone from its related data.
    /// </summary>
    public class ProjectCenterUpdateAttributes : BusinessBase<bdgas_projectmilestone>
    {
        public ProjectCenterUpdateAttributes(ILocalPluginContext context, StateManager<bdgas_projectmilestone> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var projectCenterRef = Latest.bdgas_requestintakeid;
            if (projectCenterRef == null) return;

            var milestoneTypeRef = Latest.bdgas_milestonetypeid;
            if (milestoneTypeRef == null) return;

            var argoConfig = GetArgoConfig();
            if (argoConfig == null) return;

            var value = argoConfig.bdgas_value;
            if (string.IsNullOrEmpty(value)) return;

            var acceptedStatuses = new[] { bdgas_projectmilestone_statuscode.Approved, bdgas_projectmilestone_statuscode.RevisionNeeded, bdgas_projectmilestone_statuscode.Submitted };
            if (!acceptedStatuses.Any(e => e == Latest.statuscode)) return;

            var currentModel = System.Text.Json.JsonSerializer.Deserialize<Model[]>(value).FirstOrDefault(e => e.MilestoneTypeId == milestoneTypeRef?.Id);

            if (currentModel == null) return;

            if (currentModel.IsEstimation)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_estimationstatus = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_estimationstatus.Approved :
                        (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_estimationstatus.Rejected : bdgas_requestintake_bdgas_estimationstatus.Submitted)
                };
                AdminService.Update(update);
            }

            if (currentModel.IsURS)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_ursapprovalstatus1 = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_ursapprovalstatus1.Approved :
                        (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_ursapprovalstatus1.Rejected : bdgas_requestintake_bdgas_ursapprovalstatus1.Submitted)
                };
                AdminService.Update(update);
            }

            if (currentModel.IsCodeReview)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_finalcodereviewdate = DateTime.UtcNow,
                    bdgas_codereviewstatus = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_codereviewstatus.Approved :
                        (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_codereviewstatus.Rejected : bdgas_requestintake_bdgas_codereviewstatus.Submitted),
                    bdgas_finalcodereviewperformedby = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? new Microsoft.Xrm.Sdk.EntityReference(SystemUser.EntityLogicalName, Context.PluginExecutionContext.InitiatingUserId) : null
                };
                AdminService.Update(update);
            }

            if (currentModel.IsUAT)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_uatcompletedate = DateTime.UtcNow,
                    bdgas_UATApprovalStatus = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_uatapprovalstatus.Approved :
                        (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_uatapprovalstatus.Rejected : bdgas_requestintake_bdgas_uatapprovalstatus.Submitted)
                };
                AdminService.Update(update);
            }

            if (currentModel.IsHypercare)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_ktapprovaldate = DateTime.UtcNow,
                    bdgas_HypercareApprovalStatus = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_hypercareapprovalstatus.Approved :
                        (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_hypercareapprovalstatus.Rejected : bdgas_requestintake_bdgas_hypercareapprovalstatus.Submitted)
                };
                AdminService.Update(update);
            }

            if (currentModel.IsAMO)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_amoapprovalstatus = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_amoapprovalstatus.Approved :
                        (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_amoapprovalstatus.Rejected : bdgas_requestintake_bdgas_amoapprovalstatus.Submitted)
                };
                AdminService.Update(update);
            }

            if (currentModel.IsDecom)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_decommissioningdate = DateTime.UtcNow,
                    bdgas_decomissionstatus1 = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_decomissionstatus1.Approved :
                        (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_decomissionstatus1.Rejected : bdgas_requestintake_bdgas_decomissionstatus1.Submitted),
                    bdgas_requestedby = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? new Microsoft.Xrm.Sdk.EntityReference(SystemUser.EntityLogicalName, Context.PluginExecutionContext.InitiatingUserId) : null
                };
                AdminService.Update(update);
            }

            if (currentModel.IsProdRelease)
            {
                var update = new bdgas_requestintake
                {
                    Id = projectCenterRef.Id,
                    bdgas_readytoprodstatus = Latest.statuscode == bdgas_projectmilestone_statuscode.Approved ? bdgas_requestintake_bdgas_readytoprodstatus.Approved :
                       (Latest.statuscode == bdgas_projectmilestone_statuscode.RevisionNeeded ? bdgas_requestintake_bdgas_readytoprodstatus.Rejected : bdgas_requestintake_bdgas_readytoprodstatus.Submitted)
                };
                AdminService.Update(update);
            }
        }

        public bdgas_ARGOConfig GetArgoConfig()
        {
            var query = new QueryExpression(bdgas_ARGOConfig.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_ARGOConfig.Fields.bdgas_value),
                TopCount = 1
            };
            query.Criteria.AddCondition(bdgas_ARGOConfig.Fields.bdgas_Name, ConditionOperator.Equal, "ARGO Project Center Automation");

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.FirstOrDefault()?.ToEntity<bdgas_ARGOConfig>();
        }

        /// <summary>
        /// Holds project center attribute values used to update a milestone.
        /// </summary>
        public class Model
        {
            public Guid MilestoneTypeId { get; set; }
            public bool IsEstimation { get; set; } = false;
            public bool IsURS { get; set; } = false;
            public bool IsCodeReview { get; set; } = false;
            public bool IsUAT { get; set; } = false;
            public bool IsAMO { get; set; } = false;
            public bool IsProd { get; set; } = false;
            public bool IsHypercare { get; set; } = false;
            public bool IsDecom { get; set; } = false;
            public bool IsProdRelease { get; set; } = false;
        }
    }
}
