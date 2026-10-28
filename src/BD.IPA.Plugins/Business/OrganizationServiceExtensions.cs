using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business
{
    /// <summary>
    /// Adds convenience operations for working with the Dataverse organization service.
    /// </summary>
    public static class OrganizationServiceExtensions
    {
        public static bool IsTeamMember(this IOrganizationService service, Guid userId, Guid teamId)
        {
            var query = new QueryExpression("teammembership")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);

            var result = service.RetrieveMultiple(query);

            return result.Entities.Any();
        }

        public static bool IsRpaScenario(this IOrganizationService organizationService, Guid projectCenterId)
        {
            var projectCenter = organizationService.Retrieve(bdgas_requestintake.EntityLogicalName, projectCenterId, new ColumnSet(bdgas_requestintake.Fields.bdgas_RequestIntakeType)).ToEntity<bdgas_requestintake>();

            return projectCenter.bdgas_RequestIntakeType != bdgas_requestintake_bdgas_requestintaketype.QPA;
        }

        public static bdgas_milestonetype[] GetMilestonesByRequestIntake(this IOrganizationService service, bdgas_requestintake requestintake)
        {
            var query = new QueryExpression(bdgas_milestonetype.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_milestonetype.Fields.bdgas_milestonetype1, bdgas_milestonetype.Fields.bdgas_sequence, bdgas_milestonetype.Fields.bdgas_isapprovalneeded, bdgas_milestonetype.Fields.bdgas_approvaltype,
                    bdgas_milestonetype.Fields.bdgas_isfileattachmentneeded, bdgas_milestonetype.Fields.bdgas_isnotificationneeded, bdgas_milestonetype.Fields.bdgas_requestintakestatus)
            };

            var requestIntakeType = requestintake.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.RPA ? 
                bdgas_milestonetype_bdgas_requestintaketype.RPA : bdgas_milestonetype_bdgas_requestintaketype.QPA;
            query.Criteria.AddCondition(bdgas_milestonetype.Fields.bdgas_requestintaketype, ConditionOperator.Equal, (int)requestIntakeType);
            query.Criteria.AddCondition(bdgas_milestonetype.Fields.statuscode, ConditionOperator.Equal, (int)bdgas_milestonetype_statuscode.Active);

            var result = service.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_milestonetype>()).ToArray();
        }

        public static string GetEnvironmentVariable(this IOrganizationService service, string environmentVariable)
        {
            var query = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("defaultvalue", "schemaname")
            };
            query.Criteria.AddCondition("schemaname", ConditionOperator.Equal, environmentVariable);

            var childLink =
                query.AddLink("environmentvariablevalue", "environmentvariabledefinitionid", "environmentvariabledefinitionid", JoinOperator.LeftOuter);
            childLink.EntityAlias = "ev";
            childLink.Columns = new ColumnSet("schemaname", "value");

            var result = service.RetrieveMultiple(query);

            var data = result.Entities.FirstOrDefault() ?? new Entity();
            var value = data.GetAttributeValue<AliasedValue>("ev.value")?.Value as string ?? data.GetAttributeValue<string>("defaultvalue");

            return value;
        }
    }
}
