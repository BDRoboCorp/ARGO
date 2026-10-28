using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BD.IPA.Plugins.Business
{
    /// <summary>
    /// Coordinates approval routing and approver assignment for business records.
    /// </summary>
    public class ApprovalHelper
    {
        public Dictionary<string, Entity> RetrievedData = new Dictionary<string, Entity>();

        public ApprovalHelper(ILocalPluginContext context)
        {
        }

        // [Attribute].[Attribute](Sequence); or [team:{guid}](Sequence); or [ChargeBack]
        public IEnumerable<ApproverModel> ParseApprovers(IOrganizationService service, bdgas_projectmilestone latest, string approvalSource)
        {
            var settings = approvalSource.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries).ToArray();
            foreach (var setting in settings)
            {
                var sequence = setting.Contains("(") && setting.Contains(")") ?
                    (int?)Convert.ToInt32(setting.Substring(setting.IndexOf("(") + 1, setting.IndexOf(")") -
                    setting.IndexOf("(") - 1)) : null;
                var readSetting = setting.Contains("(") && setting.Contains(")") ?
                    setting.Substring(0, setting.IndexOf("(")) : setting;

                // Check if this is a team format: [team:{guid}]
                if (readSetting == "[ChargeBack]")
                {
                    var chargeBackApprover = GetChargeBackApprovers(service, latest);
                    foreach (var member in chargeBackApprover)
                    {
                        yield return new ApproverModel
                        {
                            Attributes = readSetting,
                            Approver = member,
                            Sequence = sequence,
                            IsChargeBack = true
                        };
                    }
                    continue;
                }
                else if (readSetting.StartsWith("[team:") && readSetting.EndsWith("]"))
                {
                    var teamGuidString = readSetting.Substring(6, readSetting.Length - 7); // Extract guid between [team: and ]
                    if (Guid.TryParse(teamGuidString, out var teamGuid))
                    {
                        var teamMembers = GetTeamMembers(service, teamGuid);

                        foreach (var member in teamMembers)
                        {
                            yield return new ApproverModel
                            {
                                Attributes = readSetting,
                                Approver = member,
                                Sequence = sequence
                            };
                        }
                        continue;
                    }
                    else
                    {
                        throw new InvalidPluginExecutionException($"Invalid team GUID format in approval source: {readSetting}");
                    }
                }

                var parts = readSetting.Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    throw new InvalidPluginExecutionException($"Invalid approval source format: {readSetting}");
                }

                var currentAttributes = "";
                object value = null;
                Entity currentValue = latest;
                foreach (var part in parts)
                {
                    currentAttributes = string.IsNullOrEmpty(currentAttributes) ? part : $"{currentAttributes}.{part}";
                    var temp = currentValue.Contains(part) ? currentValue[part] : null;

                    if (temp is EntityReference entityReference)
                    {
                        currentValue = service.Retrieve(entityReference.LogicalName, entityReference.Id, new ColumnSet(true));
                        RetrievedData[entityReference.LogicalName] = currentValue;
                    }
                    else
                    {
                        value = temp;
                    }
                }

                if (value == null && currentValue != null && (currentValue.LogicalName == SystemUser.EntityLogicalName || currentValue.LogicalName == Team.EntityLogicalName))
                {
                    value = currentValue.ToEntityReference();
                }

                if (value == null || !(value is EntityReference approverRef))
                {
                    throw new InvalidPluginExecutionException($"Approver not found for the setting: {readSetting}.");
                }

                yield return new ApproverModel
                {
                    Attributes = currentAttributes,
                    Approver = approverRef,
                    Sequence = sequence
                };
            }
        }

        private EntityReference[] GetChargeBackApprovers(IOrganizationService service, bdgas_projectmilestone latest)
        {
            var requestIntakeRef = latest.bdgas_requestintakeid;
            if (requestIntakeRef == null) return new EntityReference[] { };

            var query = new QueryExpression(bdgas_budgetrequest.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_budgetrequest.Fields.bdgas_financepoc),
            };
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_requestintakeid, ConditionOperator.Equal, requestIntakeRef.Id);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.statecode, ConditionOperator.Equal, (int)bdgas_budgetrequest_statecode.Active);

            var results = service.RetrieveMultiple(query);

            return results.Entities.Select(e => e.ToEntity<bdgas_budgetrequest>())
                .Where(e => e.bdgas_financepoc != null)
                .Select(e => e.bdgas_financepoc)
                .GroupBy(e => new { e.LogicalName, e.Id })
                .Select(g => g.First())
                .ToArray();
        }

        private IEnumerable<EntityReference> GetTeamMembers(IOrganizationService service, Guid teamId)
        {
            var fetchXml = $@"
                <fetch xmlns:generator='MarkMpn.SQL4CDS'>
                  <entity name='teammembership'>
                    <link-entity name='systemuser' to='systemuserid' from='systemuserid' alias='a' link-type='inner'>
                      <attribute name='systemuserid' alias='systemuserid' />
                    </link-entity>
                    <link-entity name='team' to='teamid' from='teamid' alias='c' link-type='inner'>
                      <filter>
                        <condition attribute='teamtype' operator='eq' value='0' />
                        <condition attribute='teamid' operator='eq' value='{teamId}' />
                      </filter>
                    </link-entity>
                  </entity>
                </fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            return result.Entities.Select(e =>
            {
                var systemUserId = e.GetAttributeValue<Guid?>("systemuserid");
                return systemUserId.HasValue
                    ? new EntityReference(SystemUser.EntityLogicalName, systemUserId.Value)
                    : null;
            }).Where(e => e != null);
        }

        /// <summary>
        /// Holds the identifying information for an approval participant.
        /// </summary>
        public class ApproverModel
        {
            public string Attributes { get; set; }
            public EntityReference Approver { get; set; }
            public int? Sequence { get; set; }
            public EntityReference Team { get; set; }
            public bool IsChargeBack { get; set; } = false;
        }
    }
}
