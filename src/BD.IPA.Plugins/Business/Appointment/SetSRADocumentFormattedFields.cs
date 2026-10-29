using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Populates formatted appointment fields used in SRA documents.
    /// </summary>
    public class SetSRADocumentFormattedFields : BusinessBase<Model.Appointment>
    {
        private readonly string _selected = "☒";
        private readonly string _unselected = "☐";
        private bdgas_regulatoryassessmentmaster[] _RAMasters;

        public SetSRADocumentFormattedFields(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;

            _RAMasters = GetRAMaster();

            AuthenticationMethod();
            BDControlledSegmentation();
            FirewallChanges();
            OperationEnviromentText();
            SolutionArchitecture();
            SystemReside();

            Target.bdgas_srarequesttypetext = FormatSingleOptionFromRAMaster(Latest.bdgas_srarequesttypeid?.Id);

            var routedFromName = Latest.bdgas_routedfromid?.Name ?? GetRAName(Latest.bdgas_routedfromid?.Id);
            var routedFromText = string.Join(Environment.NewLine, new[] {
                $"Routed {(routedFromName == "Process/AME" ? _selected : _unselected) + " " + routedFromName } from Process/AME Validation #: "  + (routedFromName == "Process/AME" ? Latest.bdgas_OtherSRARequestTypeExplanation : "") ,
                $"Routed {(routedFromName == "BOT Request" ? _selected : _unselected) + " " + routedFromName } from BOT Request ARGO #:"+ (routedFromName == "BOT Request" ? Latest.bdgas_OtherSRARequestTypeExplanation : ""),
                $"{(routedFromName == "Other" ? _selected : _unselected) + " " + routedFromName } please expound:" + (routedFromName == "Other" ? Latest.bdgas_OtherSRARequestTypeExplanation : "")
            });

            Target.bdgas_routedfromtext = routedFromText;
            Target.bdgas_legacysystemtext = FormatSingleOptionFromRAMaster(Latest.bdgas_legacysystemid?.Id);
            Target.bdgas_systemhasdistinctmodulestext = FormatSingleOptionFromRAMaster(Latest.bdgas_systemhasdistinctmodulesid?.Id);
            Target.bdgas_requirevpnforaccesstext = FormatSingleOptionFromRAMaster(Latest.bdgas_requirvpnforaccessid?.Id);
            Target.bdgas_impacttootherapplicationstext = FormatSingleOptionFromRAMaster(Latest.bdgas_impacttootherapplicationsid?.Id);
            Target.bdgas_systemprocessdatatext = FormatSingleOptionFromRAMaster(Latest.bdgas_systemprocessdataid?.Id);
            Target.bdgas_dataclassificationtext = FormatSingleOptionFromRAMaster(Latest.bdgas_dataclassificationid?.Id);
            Target.bdgas_scopeofdataandprocesssupportedtext = FormatSingleOptionFromRAMaster(Latest.bdgas_scopeofdataandprocesssupportedid?.Id);
            Target.bdgas_productiondatawillbeusedtext = FormatSingleOptionFromRAMaster(Latest.bdgas_productiondatawillbeusedid?.Id);
            Target.bdgas_confidentialityimpacttext = FormatSingleOptionFromRAMaster(Latest.bdgas_confidentialityimpactid?.Id);
            Target.bdgas_integrityimpacttext = FormatSingleOptionFromRAMaster(Latest.bdgas_integrityimpactid?.Id);
            Target.bdgas_availabilityimpacttext = FormatSingleOptionFromRAMaster(Latest.bdgas_availabilityimpactid?.Id);
            Target.bdgas_gxpapplicabletext = FormatSingleOptionFromRAMaster(Latest.bdgas_gxpapplicableid?.Id);
            Target.bdgas_storeorprocesselectronicrecordstext = FormatSingleOptionFromRAMaster(Latest.bdgas_storeorprocesselectronicrecordsid?.Id);
            Target.bdgas_electronicsignaturecapabilitytext = FormatSingleOptionFromRAMaster(Latest.bdgas_electronicsignaturecapabilityid?.Id);
            Target.bdgas_gxpapplicabilitytext = FormatSingleOptionFromRAMaster(Latest.bdgas_gxpapplicabilityid?.Id);
            Target.bdgas_GxPApplicabilityFormatted = FormatSingleOptionFromRAMaster(Latest.bdgas_nongxpapplicabilityid?.Id);
            Target.bdgas_financialimpacttext = FormatSingleOptionFromRAMaster(Latest.bdgas_financialimpactid?.Id);
            Target.bdgas_soxapplicabletext = FormatSingleOptionFromRAMaster(Latest.bdgas_soxapplicableid?.Id);
            Target.bdgas_OperationatriskandorchangedFormatted = FormatSingleOptionFromRAMaster(Latest.bdgas_operationatriskandorchangedid?.Id);
            Target.bdgas_piijustificationtext = FormatSingleOptionFromRAMaster(Latest.bdgas_piijustificationid?.Id);
            Target.bdgas_otherregulationsorlegalconstraintimpactte = FormatSingleOptionFromRAMaster(Latest.bdgas_otherregulationsorlegalconstraintimpactid?.Id);
            Target.bdgas_internaltypetext = FormatSingleOptionFromRAMaster(Latest.bdgas_internaltypeid?.Id);
            Target.bdgas_documentstatustext = FormatSingleOptionFromRAMaster(Latest.bdgas_documentstatusid?.Id);
            Target.bdgas_chargebackcategorytext = FormatSingleOptionFromRAMaster(Latest.bdgas_chargebackcategoryid?.Id);
            Target.bdgas_fundedbytext = FormatSingleOptionFromRAMaster(Latest.bdgas_fundedbyid?.Id);
            Target.bdgas_drplanrequiredfromcmdbbusinessapptext = FormatSingleOptionFromRAMaster(Latest.bdgas_drplanrequiredfromcmdbbusinessappid?.Id);

            //Single value
            Target.bdgas_piistoredormanagedintheapptext = FormatSingleValueFromRAMaster(Latest.bdgas_piistoredormanagedintheappid?.Id);
            Target.bdgas_soxjustificationtext = FormatSingleValueFromRAMaster(Latest.bdgas_soxjustificationid?.Id);
            Target.bdgas_drplanstatustext = FormatSingleValueFromRAMaster(Latest.bdgas_drplanstatusid?.Id);
        }

        private void SystemReside()
        {
            var multipleChoice = _RAMasters.Where(e => e.bdgas_category == "System Reside")
                                .OrderBy(e => e.bdgas_sequence.GetValueOrDefault()).ToArray();
            var selectedMethod = GetSelectedSystemResideAssignment()?.Select(e => e.bdgas_ramasterid?.Id).ToArray();

            var text = string.Join(" ", multipleChoice.Select(choice =>
            {
                var selected = selectedMethod.Any(e => e == choice.Id);
                return $"{(selected ? _selected : _unselected)} {choice.bdgas_name}";
            }).ToArray());

            Target.bdgas_SystemResideFormatted = text;
        }

        private void SolutionArchitecture()
        {
            var multipleChoice = _RAMasters.Where(e => e.bdgas_category == "Solution Architecture")
                .OrderBy(e => e.bdgas_sequence.GetValueOrDefault()).ToArray();
            var selectedMethod = GetSelectedSolutionArchitectureAssignment()?.Select(e => e.bdgas_ramasterid?.Id).ToArray();

            var text = string.Join(" ", multipleChoice.Select(choice =>
            {
                var selected = selectedMethod.Any(e => e == choice.Id);
                return $"{(selected ? _selected : _unselected)} {choice.bdgas_name}";
            }).ToArray());

            Target.bdgas_SolutionArchitectureFormatted = text;
        }

        private void OperationEnviromentText()
        {
            var multipleChoice = _RAMasters.Where(e => e.bdgas_category == "Operating Environment")
                                .OrderBy(e => e.bdgas_sequence.GetValueOrDefault()).ToArray();
            var selectedMethod = GetSelectedOperatingEnvironmentAssignment()?.Select(e => e.bdgas_ramasterid?.Id).ToArray();

            var text = string.Join(" ", multipleChoice.Select(choice =>
            {
                var selected = selectedMethod.Any(e => e == choice.Id);
                return $"{(selected ? _selected : _unselected)} {choice.bdgas_name}";
            }).ToArray());

            Target.bdgas_OperatingEnvironmentFormatted = text;
        }

        private void FirewallChanges()
        {
            var multipleChoice = _RAMasters.Where(e => e.bdgas_category == "Firewall Changes")
                .OrderBy(e => e.bdgas_sequence.GetValueOrDefault()).ToArray();
            var selectedMethod = GetSelectedFirewallChangesAssignment()?.Select(e => e.bdgas_ramasterid?.Id).ToArray();

            var text = string.Join(" ", multipleChoice.Select(choice =>
            {
                var selected = selectedMethod.Any(e => e == choice.Id);
                return $"{(selected ? _selected : _unselected)} {choice.bdgas_name}";
            }).ToArray());

            Target.bdgas_FirewallChangesFormatted = text;
        }

        private void BDControlledSegmentation()
        {
            var multipleChoice = _RAMasters.Where(e => e.bdgas_category == "BD Controlled Segmentation")
                .OrderBy(e => e.bdgas_sequence.GetValueOrDefault()).ToArray();
            var selectedMethod = GetSelectedControlledSegmentationAssignment()?.Select(e => e.bdgas_ramasterid?.Id).ToArray();

            var text = string.Join(" ", multipleChoice.Select(choice =>
            {
                var selected = selectedMethod.Any(e => e == choice.Id);
                return $"{(selected ? _selected : _unselected)} {choice.bdgas_name}";
            }).ToArray());

            Target.bdgas_BDControlledSegmentationFormatted = text;
        }

        private void AuthenticationMethod()
        {
            var multipleChoice = _RAMasters.Where(e => e.bdgas_category == "Authentication Method")
                .OrderBy(e => e.bdgas_sequence.GetValueOrDefault()).ToArray();
            var selectedMethod = GetSelectedAuthenticationMethodAssignment()?.Select(e => e.bdgas_ramasterid?.Id).ToArray();

            var text = string.Join(" ", multipleChoice.Select(choice =>
            {
                var selected = selectedMethod.Any(e => e == choice.Id);
                return $"{(selected ? _selected : _unselected)} {choice.bdgas_name}" + (choice.bdgas_name == "Other" ? $": {Latest.bdgas_OtherAuthenticationMethod}" : "");
            }).ToArray());

            Target.bdgas_AuthenticationMethodFormatted = text;
        }

        private bdgas_authenticationmethodassignment[] GetSelectedAuthenticationMethodAssignment()
        {
            var query = new QueryExpression(bdgas_authenticationmethodassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_authenticationmethodassignment.Fields.bdgas_ramasterid)
            };
            query.Criteria.AddCondition(bdgas_authenticationmethodassignment.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_authenticationmethodassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_authenticationmethodassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_authenticationmethodassignment>()).ToArray();
        }

        private bdgas_controlledsegmentationassignment[] GetSelectedControlledSegmentationAssignment()
        {
            var query = new QueryExpression(bdgas_controlledsegmentationassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_controlledsegmentationassignment.Fields.bdgas_ramasterid)
            };
            query.Criteria.AddCondition(bdgas_controlledsegmentationassignment.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_controlledsegmentationassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_controlledsegmentationassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_controlledsegmentationassignment>()).ToArray();
        }

        private bdgas_firewallchangesassignment[] GetSelectedFirewallChangesAssignment()
        {
            var query = new QueryExpression(bdgas_firewallchangesassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_firewallchangesassignment.Fields.bdgas_ramasterid)
            };
            query.Criteria.AddCondition(bdgas_firewallchangesassignment.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_firewallchangesassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_firewallchangesassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_firewallchangesassignment>()).ToArray();
        }

        private bdgas_operatingenvironmentassignment[] GetSelectedOperatingEnvironmentAssignment()
        {
            var query = new QueryExpression(bdgas_operatingenvironmentassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_operatingenvironmentassignment.Fields.bdgas_ramasterid)
            };
            query.Criteria.AddCondition(bdgas_operatingenvironmentassignment.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_operatingenvironmentassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_operatingenvironmentassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_operatingenvironmentassignment>()).ToArray();
        }

        private bdgas_solutionarchitectureassignment[] GetSelectedSolutionArchitectureAssignment()
        {
            var query = new QueryExpression(bdgas_solutionarchitectureassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_solutionarchitectureassignment.Fields.bdgas_ramasterid)
            };
            query.Criteria.AddCondition(bdgas_solutionarchitectureassignment.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_solutionarchitectureassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_solutionarchitectureassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_solutionarchitectureassignment>()).ToArray();
        }

        private bdgas_systemresideassignment[] GetSelectedSystemResideAssignment()
        {
            var query = new QueryExpression(bdgas_systemresideassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_systemresideassignment.Fields.bdgas_ramasterid)
            };
            query.Criteria.AddCondition(bdgas_systemresideassignment.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_systemresideassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_systemresideassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_systemresideassignment>()).ToArray();
        }

        private bdgas_regulatoryassessmentmaster[] GetRAMaster()
        {
            var query = new QueryExpression(bdgas_regulatoryassessmentmaster.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(
                    bdgas_regulatoryassessmentmaster.Fields.bdgas_name,
                    bdgas_regulatoryassessmentmaster.Fields.bdgas_category,
                    bdgas_regulatoryassessmentmaster.Fields.bdgas_sequence
                ),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(bdgas_regulatoryassessmentmaster.Fields.statecode, ConditionOperator.Equal, (int)bdgas_regulatoryassessmentmaster_statecode.Active)
                    }
                },
            };
            return AdminService.RetrieveMultiple(query).Entities
                .Select(e => e.ToEntity<bdgas_regulatoryassessmentmaster>())
                .ToArray();
        }

        private string GetCategoryValue(Guid? selectedId)
        {
            if (!selectedId.HasValue) return string.Empty;

            var raMaster = _RAMasters.FirstOrDefault(r => r.Id == selectedId.Value);

            if (raMaster == null) return string.Empty;

            return raMaster?.bdgas_category;
        }

        private string GetRAName(Guid? raId)
        {
            return _RAMasters.FirstOrDefault(e => e.Id == raId)?.bdgas_name ?? "";
        }

        private string FormatSingleOptionFromRAMaster(Guid? selectedId)
        {
            var category = GetCategoryValue(selectedId);
            return string.Join(" ",
                _RAMasters.Where(r => r.bdgas_category == category)
                          .OrderBy(r => r.bdgas_sequence)
                          .Select(r => $"{(r.Id == selectedId ? _selected : _unselected)} {r.bdgas_name}")
            );
        }

        private string FormatSingleValueFromRAMaster(Guid? selectedId)
        {
            var category = GetCategoryValue(selectedId);
            return string.Join(" ",
                _RAMasters.Where(r => r.bdgas_category == category && r.Id == selectedId)
                          .Select(r => $"{(r.Id == selectedId ? _selected : _unselected)} {r.bdgas_name}")
            );
        }
    }
}
