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
    /// Retrieves business intelligence data for a request intake.
    /// </summary>
    public class GetBDIntelligenceDataApi : PluginBase
    {
        public readonly string RequestIntakeIdKey = "RequestIntakeId";
        public readonly string OutputKey = "Result";
        //public readonly string EnvironmentVariableKey = "bdgas_ARGOPlanviewDefaultValues";

        
        /// <summary>
        /// Holds business intelligence values returned for a request intake.
        /// </summary>
        public class BDIntelligenceDataModel
        {
            public string DigitalProductName { get; set; }
            public string BusinessUnit { get; set; }
            public string Function { get; set; }
            public string Region { get; set; }
            public string StrategicAlignment { get; set; }
            public string ValueDrivers { get; set; }
            public string TechnologiesUsed { get; set; }
            public string Availability { get; set; }
            public string IntegrationType { get; set; }
            public string AccessUrl { get; set; }
            public string ProductType { get; set; }
            public string ProductOwner { get; set; }
            public string ProductOwnerEmail { get; set; }
            public string ShortDescription { get; set; }
            public string LongDescription { get; set; }
            public string RequestLinkUrl { get; set; }
            public string DemoVideo { get; set; }
            public string VideoUrl { get; set; }
            public string PlaybookInstructionGuide { get; set; }
            public string MaxwellUrl { get; set; }
            public string IsActive { get; set; }
            public string ProductCategory { get; set; }
            public string Bookmarked { get; set; }
            public string TrainingUrl { get; set; }
            public string WhitepaperUrl { get; set; }
        }

        /// <summary>
        /// Represents an error returned by the business intelligence data operation.
        /// </summary>
        private class ErrorResponseModel
        {
            public string Error { get; set; }
        }

        public GetBDIntelligenceDataApi() : base(typeof(GetBDIntelligenceDataApi))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            try
            {
                var requestIntakeId = localPluginContext.PluginExecutionContext.InputParameterOrDefault<Guid>(RequestIntakeIdKey);
                var requestIntake = GetRequestIntake(requestIntakeId, localPluginContext);


                var bdIntelligenceData = new BDIntelligenceDataModel
                {
                    DigitalProductName = requestIntake.bdgas_digitalproductname,
                    BusinessUnit = requestIntake.bdgas_bdintelligencebuid?.Name ?? "N/A",
                    Function = requestIntake.bdgas_bdintelligencefunction?.Name ?? "N/A",
                    Region = requestIntake.bdgas_bdintelligenceregion?.Name ?? "N/A",
                    StrategicAlignment = requestIntake.bdgas_bdintelligencestrategicalignment?.Name ?? "N/A",
                    ValueDrivers = GetSelectedValueDriversAssignment(requestIntakeId, localPluginContext),
                    TechnologiesUsed = requestIntake.bdgas_bdintelligencetechnologiesused?.Name ?? "N/A",
                    Availability = requestIntake.bdgas_bdintelligenceavailability?.Name ?? "N/A",
                    IntegrationType = requestIntake.bdgas_bdintelligenceintegrationtype?.Name ?? "N/A",
                    AccessUrl = requestIntake.bdgas_bdintelligenceaccessurl ?? "N/A",
                    ProductType = requestIntake.bdgas_bdintelligenceproducttype?.Name ?? "N/A",
                    ProductOwner = requestIntake.bdgas_bdintelligenceproductowner ?? "N/A",
                    ProductOwnerEmail = requestIntake.bdgas_bdintelligenceproductemail ?? "N/A",
                    ShortDescription = Truncate(requestIntake.bdgas_description, 150),
                    LongDescription = Truncate(requestIntake.bdgas_description, 400),
                    RequestLinkUrl = requestIntake.bdgas_bdintelligencerequestlinkurl ?? "N/A",
                    DemoVideo = requestIntake.bdgas_bdintelligencedemovideo ?? "N/A",
                    VideoUrl = requestIntake.bdgas_bdintelligencevideourl ?? "N/A",
                    PlaybookInstructionGuide = requestIntake.bdgas_bdintelligenceplaybookinstructrionguide ?? "N/A",
                    MaxwellUrl = requestIntake.bdgas_bdintelligencemaxwellurl ?? "N/A",
                    IsActive = requestIntake.bdgas_bdintelligenceisactiveid?.Name ?? "N/A",
                    ProductCategory = requestIntake.bdgas_bdintelligenceproductcategory?.Name ?? "N/A",
                    Bookmarked = requestIntake.bdgas_bdintelligencebookmarkedid?.Name ?? "N/A",
                    TrainingUrl = requestIntake.bdgas_bdintelligencetrainingurl ?? "N/A",
                    WhitepaperUrl = requestIntake.bdgas_bdintelligencewhitepaperurl ?? "N/A",
                };

                localPluginContext.PluginExecutionContext.OutputParameters[OutputKey] = JsonSerializer.Serialize(bdIntelligenceData);
            }
            catch (Exception ex)
            {
                localPluginContext.PluginExecutionContext.OutputParameters[OutputKey] = JsonSerializer.Serialize(new ErrorResponseModel { Error = ex.Message });
            }
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private string GetSelectedValueDriversAssignment(Guid requestIntakeId, ILocalPluginContext localPluginContext)
        {
            var multipleChoice = GetRAMaster(localPluginContext);

            var query = new QueryExpression(bdgas_valuedriversassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_valuedriversassignment.Fields.bdgas_ramasterid)
            };
            query.Criteria.AddCondition(bdgas_valuedriversassignment.Fields.bdgas_projectcenterid, ConditionOperator.Equal, requestIntakeId);
            query.Criteria.AddCondition(bdgas_valuedriversassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_valuedriversassignment_statecode.Active);

            var selectedIds = localPluginContext.AdminService.RetrieveMultiple(query).Entities
                                    .Select(e => e.ToEntity<bdgas_valuedriversassignment>())
                                    .Select(e => e.bdgas_ramasterid?.Id)
                                    .Where(id => id.HasValue)
                                    .Select(id => id.Value)
                                    .ToArray();

            return string.Join(", ", multipleChoice
                    .Where(choice => selectedIds.Contains(choice.Id))
                    .Select(choice => choice.bdgas_name)
            );
        }

        private bdgas_regulatoryassessmentmaster[] GetRAMaster(ILocalPluginContext localPluginContext)
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
                        new ConditionExpression(bdgas_regulatoryassessmentmaster.Fields.statecode, ConditionOperator.Equal, (int)bdgas_regulatoryassessmentmaster_statecode.Active),
                        new ConditionExpression(bdgas_regulatoryassessmentmaster.Fields.bdgas_category, ConditionOperator.Equal, "BD Intelligence Value Drivers")
                    }
                },
            };
            return localPluginContext.AdminService.RetrieveMultiple(query).Entities
                .Select(e => e.ToEntity<bdgas_regulatoryassessmentmaster>())
                .ToArray();
        }

        private bdgas_requestintake GetRequestIntake(Guid requestIntakeId, ILocalPluginContext localPluginContext)
        {
            var requestIntake = localPluginContext.AdminService.Retrieve(bdgas_requestintake.EntityLogicalName, requestIntakeId, new ColumnSet(
                    bdgas_requestintake.Fields.bdgas_digitalproductname,
                    bdgas_requestintake.Fields.bdgas_description,
                    bdgas_requestintake.Fields.bdgas_bdintelligencebuid,
                    bdgas_requestintake.Fields.bdgas_bdintelligencefunction,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceregion,
                    bdgas_requestintake.Fields.bdgas_bdintelligencestrategicalignment,
                    bdgas_requestintake.Fields.bdgas_valuedrivers,
                    bdgas_requestintake.Fields.bdgas_bdintelligencetechnologiesused,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceavailability,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceintegrationtype,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceaccessurl,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceproducttype,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceproductowner,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceproductemail,
                    bdgas_requestintake.Fields.bdgas_bdintelligencerequestlinkurl,
                    bdgas_requestintake.Fields.bdgas_bdintelligencedemovideo,
                    bdgas_requestintake.Fields.bdgas_bdintelligencevideourl,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceplaybookinstructrionguide,
                    bdgas_requestintake.Fields.bdgas_bdintelligencemaxwellurl,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceisactiveid,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceproductcategory,
                    bdgas_requestintake.Fields.bdgas_bdintelligencebookmarkedid,
                    bdgas_requestintake.Fields.bdgas_bdintelligencetrainingurl,
                    bdgas_requestintake.Fields.bdgas_bdintelligencewhitepaperurl,
                    bdgas_requestintake.Fields.bdgas_bdintelligenceregion
                ));
            if (requestIntake == null) throw new Exception($"System can't found Request Intake with ID: {requestIntakeId}.");

            return requestIntake.ToEntity<bdgas_requestintake>();
        }
    }
}
