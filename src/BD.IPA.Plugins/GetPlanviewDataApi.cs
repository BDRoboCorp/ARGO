using BD.IPA.Plugins.Business;
using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Extensions;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Retrieves request intake and resource details in the format expected by Planview.
    /// </summary>
    public class GetPlanviewDataApi : PluginBase
    {
        public readonly string RequestIntakeIdKey = "RequestIntakeId";
        public readonly string OutputKey = "Result";
        public readonly string EnvironmentVariableKey = "bdgas_ARGOPlanviewDefaultValues";

        /// <summary>
        /// Represents the project and assessment data sent to Planview.
        /// </summary>
        private class PlanviewModel
        {
            public string ProjectName { get; set; }

            public string BusinessValueScore { get; set; }

            [JsonPropertyName("Size&Complexity")]
            public string SizeComplexity { get; set; }

            public string ReusabilityScore { get; set; }

            public string ValueRealizationScore { get; set; }

            public int OpportunityScore { get; set; }

            public string BusinessCapabilityStatus { get; set; }

            public string DataClassification { get; set; }

            public List<ApplicationModel> ImpactedPlatformsOrDomains { get; set; }

            public string DeliveryModel { get; set; }

            //public bool PrototypeRequired { get; set; }

            //public string ExecutionStage { get; set; }

            //public bool PrototypeRAReview { get; set; }

            //public string PrototypeScope { get; set; }

            //public string PrototypeFindings { get; set; }

            public string WorkDescription { get; set; }

            public string Scope { get; set; }

            public string OutOfScope { get; set; }

            public string ResponsibleAIRiskProfile { get; set; }

            public string CINumber { get; set; }

            //public string AddLinkToTechnicalOrProcess { get; set; }

            public string ExLTOwner { get; set; }

            public string KnownVendorsOrPartners { get; set; }

            //public string InfrastructureDescription { get; set; }

            //public bool IncludeCloudInfrastructureOrServices { get; set; }

            public string RAMeetingCompleted { get; set; }

            public string RAMeetingDate { get; set; }

            public bool RAApplicability { get; set; }

            public List<AssessmentModel> AssessmentsRequired { get; set; }

            public string BenefitsAnticipated { get; set; }

            [JsonPropertyName("BenefitType(P)")]
            public string BenefitType { get; set; }

            public string BDStrategyAlignment { get; set; }

            public string RoadmapAlignment { get; set; }

            public string SuccessCriteriaKPIs { get; set; }

            public List<ResourceAllocationModel> ResourceAllocations { get; set; }

            public string FinancePOC { get; set; }

            public string CostCenterOperations { get; set; }

            public string CostCenterDevelopment { get; set; }

            public int TotalDevelopmentCost { get; set; }

            public int TotalOperationCost { get; set; }
        }

        /// <summary>
        /// Represents an application included in the Planview project data.
        /// </summary>
        private class ApplicationModel
        {
            public string ApplicationName { get; set; }
        }

        /// <summary>
        /// Represents an assessment included in the Planview project data.
        /// </summary>
        private class AssessmentModel
        {
            public string AssessmentName { get; set; }
        }

        /// <summary>
        /// Represents a person's allocation to the Planview project.
        /// </summary>
        private class ResourceAllocationModel
        {
            public string PICName { get; set; }

            public int Percentage { get; set; }
        }

        /// <summary>
        /// Holds configured values used when mapping data to Planview.
        /// </summary>
        private class PlanviewEnvVariableModel
        {
            public Guid? BusinessValueScoreId { get; set; }
            public Guid? ReusabilityScoreId { get; set; }
            public Guid? ValueRealizationScoreId { get; set; }
            public Guid? BusinessCapabilityStatusId { get; set; }
            public Guid? BenefitTypeId { get; set; }
            public Guid? BDStrategyAlignmentId { get; set; }
            public Guid? RoadmapAlignmentId { get; set; }
            public string OutOfScope { get; set; }
        }

        /// <summary>
        /// Represents an error returned by the Planview data operation.
        /// </summary>
        private class ErrorResponseModel
        {
            public string Error { get; set; }
        }

        public GetPlanviewDataApi() : base(typeof(GetPlanviewDataApi))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            try
            {
                var requestIntakeId = localPluginContext.PluginExecutionContext.InputParameterOrDefault<Guid>(RequestIntakeIdKey);
                var requestIntake = GetRequestIntake(requestIntakeId, localPluginContext);
                var appointment = GetSRAAppointment(requestIntake.bdgas_sraid.Id, localPluginContext);
                var assessmentList = GetAssessment(appointment.Id, localPluginContext);
                var config = GetEnvironmentVariable(EnvironmentVariableKey, localPluginContext);
                var budgetRequests = GetBudgetRequest(requestIntakeId, localPluginContext);
                var devCost = budgetRequests.FirstOrDefault(br => br.bdgas_chargebacktype == bdgas_chargebacktype.Development);
                var opsCost = budgetRequests.FirstOrDefault(br => br.bdgas_chargebacktype == bdgas_chargebacktype.Operations);

                var planviewData = new PlanviewModel
                {
                    ProjectName = requestIntake.bdgas_digitalproductname,
                    BusinessValueScore = TrimScoreString(requestIntake.bdgas_businessvaluescoreid?.Name),
                    SizeComplexity = TrimScoreString(requestIntake.bdgas_developmentsizeid?.Name),
                    ReusabilityScore = TrimScoreString(requestIntake.bdgas_reusabilityscoreid?.Name),
                    ValueRealizationScore = TrimScoreString(requestIntake.bdgas_valuerealizationscoreid?.Name),
                    OpportunityScore = requestIntake.bdgas_opportunityscore ?? 0,
                    BusinessCapabilityStatus = requestIntake.bdgas_businesscapabilitystatusid?.Name,
                    DataClassification = appointment.bdgas_dataclassificationid?.Name,
                    ImpactedPlatformsOrDomains = GetApplication(requestIntakeId, localPluginContext),
                    DeliveryModel = requestIntake.bdgas_deliverymodelid?.Name,
                    WorkDescription = requestIntake.bdgas_description,
                    Scope = requestIntake.bdgas_changeticket,
                    OutOfScope = config.OutOfScope,
                    ResponsibleAIRiskProfile = requestIntake.bdgas_responsibleaisubmitted.HasValue && requestIntake.bdgas_responsibleaisubmitted.Value ? "Not Applicable" : string.Empty,
                    CINumber = requestIntake.bdgas_changeticket,
                    ExLTOwner = requestIntake.bdgas_botvp?.Name,
                    KnownVendorsOrPartners = requestIntake.bdgas_developmentteamid?.Name,
                    RAMeetingCompleted = appointment.bdgas_SlotStatus.HasValue && appointment.bdgas_SlotStatus.Equals(bdgas_appointment_bdgas_slotstatus.Complete) ? "Yes" : "No",
                    RAMeetingDate = appointment.ActualStart?.ToString("M/d/yyyy"),
                    RAApplicability = assessmentList.Any(),
                    AssessmentsRequired = assessmentList,
                    BenefitsAnticipated = requestIntake.bdgas_BenefitsAnticipated,
                    BenefitType = requestIntake.bdgas_benefittypeid?.Name,
                    BDStrategyAlignment = requestIntake.bdgas_bdstrategyalignmentid?.Name,
                    RoadmapAlignment = requestIntake.bdgas_roadmapalignmentid?.Name,
                    SuccessCriteriaKPIs = requestIntake.bdgas_SuccessCriteriaKPIs,
                    ResourceAllocations = GetResourceAllocation(requestIntakeId, localPluginContext),
                    FinancePOC = devCost?.bdgas_financepoc?.Name,
                    CostCenterOperations = opsCost?.bdgas_costcenter,
                    CostCenterDevelopment = devCost?.bdgas_costcenter,
                    TotalDevelopmentCost = (int)requestIntake.bdgas_totaldevelopment.Value,
                    TotalOperationCost = (int)requestIntake.bdgas_totaloperation.Value
                };

                localPluginContext.PluginExecutionContext.OutputParameters[OutputKey] = JsonSerializer.Serialize(planviewData);
            }
            catch (Exception ex)
            {
                localPluginContext.PluginExecutionContext.OutputParameters[OutputKey] = JsonSerializer.Serialize(new ErrorResponseModel { Error = ex.Message });
            }
        }

        private string TrimScoreString(string text)
        {
            return text?.Substring(0, text.Length - 4);
        }

        private PlanviewEnvVariableModel GetEnvironmentVariable(string key, ILocalPluginContext localPluginContext)
        {
            var envVar = localPluginContext.AdminService.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(envVar)) throw new Exception($"System can't found Environment Variable with Key: {key}.");
            
            return JsonSerializer.Deserialize<PlanviewEnvVariableModel>(envVar, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private bdgas_requestintake GetRequestIntake(Guid requestIntakeId, ILocalPluginContext localPluginContext)
        {
            var requestIntake = localPluginContext.AdminService.Retrieve(bdgas_requestintake.EntityLogicalName, requestIntakeId, new ColumnSet(
                    bdgas_requestintake.Fields.bdgas_digitalproductname,
                    bdgas_requestintake.Fields.bdgas_description,
                    bdgas_requestintake.Fields.bdgas_botvp,
                    bdgas_requestintake.Fields.bdgas_responsibleaisubmitted,
                    bdgas_requestintake.Fields.bdgas_sraid,
                    bdgas_requestintake.Fields.bdgas_developmentteamid,
                    bdgas_requestintake.Fields.bdgas_totaldevelopment,
                    bdgas_requestintake.Fields.bdgas_totaloperation,
                    bdgas_requestintake.Fields.bdgas_changeticket,
                    bdgas_requestintake.Fields.bdgas_businessvaluescoreid,
                    bdgas_requestintake.Fields.bdgas_developmentsizeid,
                    bdgas_requestintake.Fields.bdgas_reusabilityscoreid,
                    bdgas_requestintake.Fields.bdgas_valuerealizationscoreid,
                    bdgas_requestintake.Fields.bdgas_opportunityscore,
                    bdgas_requestintake.Fields.bdgas_businesscapabilitystatusid,
                    bdgas_requestintake.Fields.bdgas_deliverymodelid,
                    bdgas_requestintake.Fields.bdgas_bdstrategyalignmentid,
                    bdgas_requestintake.Fields.bdgas_benefittypeid,
                    bdgas_requestintake.Fields.bdgas_roadmapalignmentid,
                    bdgas_requestintake.Fields.bdgas_BenefitsAnticipated,
                    bdgas_requestintake.Fields.bdgas_SuccessCriteriaKPIs
                ));
            if (requestIntake == null) throw new Exception($"System can't found Request Intake with ID: {requestIntakeId}.");

            return requestIntake.ToEntity<bdgas_requestintake>();
        }

        private Appointment GetSRAAppointment(Guid appointmentId, ILocalPluginContext localPluginContext)
        {
            var appointment = localPluginContext.AdminService.Retrieve(Appointment.EntityLogicalName, appointmentId, new ColumnSet(
                    Appointment.Fields.ActualStart,
                    Appointment.Fields.bdgas_dataclassificationid,
                    Appointment.Fields.bdgas_SlotStatus
                ));
            if (appointment == null) throw new Exception($"System can't found SRA Appointment with ID: {appointmentId}.");

            return appointment.ToEntity<Appointment>();
        }

        private List<AssessmentModel> GetAssessment(Guid appointmentId, ILocalPluginContext localPluginContext)
        {
            var appointment = localPluginContext.AdminService.Retrieve(Appointment.EntityLogicalName, appointmentId, new ColumnSet(
                    Appointment.Fields.bdgas_gxpapplicableid,
                    Appointment.Fields.bdgas_soxapplicableid,
                    Appointment.Fields.bdgas_piistoredormanagedintheappid
                )).ToEntity<Appointment>();

            if (appointment == null) throw new Exception($"System can't found SRA Appointment with ID: {appointmentId}.");

            var assessmentList = new List<AssessmentModel>();
            if (appointment.bdgas_gxpapplicableid != null)
            {
                assessmentList.Add(new AssessmentModel { AssessmentName = "GxP" });
            }

            if (appointment.bdgas_soxapplicableid != null)
            {
                assessmentList.Add(new AssessmentModel { AssessmentName = "SOX" });
            }

            if (appointment.bdgas_piistoredormanagedintheappid != null && !appointment.bdgas_piistoredormanagedintheappid.Name.Equals("No PII is stored or managed"))
            {
                assessmentList.Add(new AssessmentModel { AssessmentName = "Privacy" });
            }

            return assessmentList;
        }

        private List<ApplicationModel> GetApplication(Guid requestIntakeId, ILocalPluginContext localPluginContext)
        {
            var query = new QueryExpression(bdgas_RequestIntakeApplication.EntityLogicalName);
            query.ColumnSet = new ColumnSet(bdgas_RequestIntakeApplication.Fields.bdgas_Name);
            query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.bdgas_requestintakeid, ConditionOperator.Equal, requestIntakeId);

            var linkQuery = query.AddLink(bdgas_Application.EntityLogicalName, bdgas_RequestIntakeApplication.Fields.bdgas_application, bdgas_Application.Fields.Id);
            linkQuery.EntityAlias = "application";
            linkQuery.Columns = new ColumnSet(bdgas_Application.Fields.bdgas_applicationname);

            var applicationEntities = localPluginContext.AdminService.RetrieveMultiple(query).Entities.Select(e => e.ToEntity<bdgas_RequestIntakeApplication>()).ToArray();
            if (!applicationEntities.Any()) throw new Exception($"System can't found Request Intake Application with Request Intake ID: {requestIntakeId}.");

            var applicationList = new List<ApplicationModel>();
            foreach (var application in applicationEntities)
            {
                var appName = application.GetAttributeValue<AliasedValue>("application." + bdgas_Application.Fields.bdgas_applicationname);
                if (appName != null)
                {
                    applicationList.Add(new ApplicationModel { ApplicationName = appName.Value.ToString() });
                }
            }

            return applicationList;
        }

        private List<ResourceAllocationModel> GetResourceAllocation(Guid requestIntakeId, ILocalPluginContext localPluginContext)
        {
            var query = new QueryExpression(bdgas_ResourceAllocation.EntityLogicalName);
            query.ColumnSet = new ColumnSet(bdgas_ResourceAllocation.Fields.bdgas_percentage, bdgas_ResourceAllocation.Fields.bdgas_pic);
            query.Criteria.AddCondition(bdgas_ResourceAllocation.Fields.bdgas_requestintakeid, ConditionOperator.Equal, requestIntakeId);

            var resourceAllocationEntities = localPluginContext.AdminService.RetrieveMultiple(query).Entities.Select(e => e.ToEntity<bdgas_ResourceAllocation>()).ToArray();
            if (!resourceAllocationEntities.Any()) throw new Exception($"System can't found Resource Allocation with Request Intake ID: {requestIntakeId}.");

            var resourceAllocationList = new List<ResourceAllocationModel>();
            foreach (var resource in resourceAllocationEntities)
            {
                resourceAllocationList.Add(new ResourceAllocationModel { PICName = resource.bdgas_pic.Name, Percentage = (int)resource.bdgas_percentage.Value });
            }

            return resourceAllocationList;
        }

        private bdgas_budgetrequest[] GetBudgetRequest(Guid requestIntakeId, ILocalPluginContext localPluginContext)
        {
            var query = new QueryExpression(bdgas_budgetrequest.EntityLogicalName);
            query.ColumnSet = new ColumnSet(bdgas_budgetrequest.Fields.bdgas_financepoc, bdgas_budgetrequest.Fields.bdgas_costcenter, bdgas_budgetrequest.Fields.bdgas_chargebacktype);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_requestintakeid, ConditionOperator.Equal, requestIntakeId);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_chargebacktype, ConditionOperator.In, new object[] { (int)bdgas_chargebacktype.Development, (int)bdgas_chargebacktype.Operations });
            
            var budgetRequestEntities = localPluginContext.AdminService.RetrieveMultiple(query).Entities.Select(e => e.ToEntity<bdgas_budgetrequest>()).ToArray();
            if (!budgetRequestEntities.Any()) throw new Exception($"System can't found Budget Request (Development or Operation) with Request Intake ID: {requestIntakeId}.");

            return budgetRequestEntities;
        }
    }
}
