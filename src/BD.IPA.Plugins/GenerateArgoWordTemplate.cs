using BD.IPA.Plugins.Business;
using BD.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Extensions;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Generates and uploads an SRA document from the configured Word template.
    /// </summary>
    public class GenerateArgoWordTemplate : PluginBase
    {
        public readonly string AppointmentIdKey = "AppointmentId";
        public readonly string EnvironmentVariableKey = "bdgas_ARGOSRADocumentTemplate";
        public readonly string OutputKey = "Result";

        public GenerateArgoWordTemplate() : base(typeof(GenerateArgoWordTemplate))
        {
        }

        /// <summary>
        /// Holds the document template identifiers configured for SRA document generation.
        /// </summary>
        private class DocumentTemplateEnvVariableModel
        {
            public Guid? DocumentTemplateId { get; set; }
        }

        /// <summary>
        /// Represents the successful result returned after document generation.
        /// </summary>
        private class SuccessResponseModel
        {
            public string Status { get; set; }
        }

        /// <summary>
        /// Represents the failure result returned when document generation fails.
        /// </summary>
        private class ErrorResponseModel
        {
            public string Status { get; set; }
            public string Error { get; set; }
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            try
            {
                var appointmentId = localPluginContext.PluginExecutionContext.InputParameterOrDefault<Guid>(AppointmentIdKey);
                var sraDocument = localPluginContext.AdminService.Retrieve(Model.Appointment.EntityLogicalName, appointmentId, new ColumnSet(Model.Appointment.Fields.bdgas_requestname,
                    Model.Appointment.Fields.bdgas_NetworkDataflowDiagramId, Model.Appointment.Fields.bdgas_networkdataflowdiagram2Id, Model.Appointment.Fields.bdgas_networkdataflowdiagram3Id, Model.Appointment.Fields.bdgas_networkdataflowdiagram4Id,
                    Model.Appointment.Fields.bdgas_networkdataflowdiagram5Id)).ToEntity<Appointment>();
                var envVar = localPluginContext.AdminService.GetEnvironmentVariable(EnvironmentVariableKey);
                if (string.IsNullOrEmpty(envVar)) throw new Exception($"System can't found Environment Variable with Key: {EnvironmentVariableKey}.");

                var configs = JsonSerializer.Deserialize<DocumentTemplateEnvVariableModel[]>(envVar, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var totalCount = new[] { sraDocument.bdgas_NetworkDataflowDiagramId, sraDocument.bdgas_networkdataflowdiagram2Id, sraDocument.bdgas_networkdataflowdiagram3Id,
                 sraDocument.bdgas_networkdataflowdiagram4Id, sraDocument.bdgas_networkdataflowdiagram5Id}.Where(e => e.HasValue).ToArray().Count();
                var config = configs[totalCount - 1];

                var document = localPluginContext.AdminService.Retrieve(PersonalDocumentTemplate.EntityLogicalName, (Guid)config.DocumentTemplateId,
                    new ColumnSet(PersonalDocumentTemplate.Fields.Name)).ToEntity<PersonalDocumentTemplate>();
                if (document == null) throw new Exception($"System can't found Document Template with Id: {config.DocumentTemplateId}.");

                // Generate the document
                var req = new OrganizationRequest("SetWordTemplate")
                {
                    ["Target"] = new EntityReference(Appointment.EntityLogicalName, appointmentId),
                    ["SelectedTemplate"] = document.ToEntityReference()
                };
                localPluginContext.AdminService.Execute(req);

                var attachment = GetAttachment(localPluginContext.AdminService, appointmentId, document.Name);
                if (attachment == null) return;

                //Upload the generated document
                var fileName = attachment.GetAttributeValue<string>("filename");
                var base64Data = attachment.GetAttributeValue<string>("documentbody");
                var mimeType = attachment.GetAttributeValue<string>("mimetype");
                var fileBytes = Convert.FromBase64String(base64Data);

                int blockSize = 4 * 1024 * 1024; // 4 MB

                var initializeFileBlocksUploadRequest = new InitializeFileBlocksUploadRequest
                {
                    Target = new EntityReference(Appointment.EntityLogicalName, appointmentId),
                    FileAttributeName = Appointment.Fields.bdgas_sradocumentupload,
                    FileName = fileName
                };

                var extension = Path.GetExtension(fileName);

                var response = (InitializeFileBlocksUploadResponse)localPluginContext.AdminService.Execute(initializeFileBlocksUploadRequest);
                var token = response.FileContinuationToken;

                var blockIds = new List<string>();

                for (int i = 0; i < Math.Ceiling(fileBytes.Length / Convert.ToDecimal(blockSize)); i++)
                {
                    var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
                    blockIds.Add(blockId);

                    var blockData = fileBytes.Skip(i * blockSize).Take(blockSize).ToArray();
                    var blockRequest = new UploadBlockRequest()
                    {
                        FileContinuationToken = token,
                        BlockId = blockId,
                        BlockData = blockData
                    };

                    // Execute block upload request
                    localPluginContext.AdminService.Execute(blockRequest);
                }

                var fullFileName = $"{sraDocument.bdgas_requestname}_SRA{extension}";
                var commitFileBlocksUploadRequest = new CommitFileBlocksUploadRequest
                {
                    BlockList = blockIds.ToArray(),
                    FileContinuationToken = token,
                    FileName = fullFileName,
                    MimeType = mimeType
                };
                localPluginContext.AdminService.Execute(commitFileBlocksUploadRequest);

                // Delete the attachment
                localPluginContext.AdminService.Delete(attachment.LogicalName, attachment.Id);

                localPluginContext.PluginExecutionContext.OutputParameters[OutputKey] = JsonSerializer.Serialize(new SuccessResponseModel { Status = "Success" });
            }
            catch (Exception ex)
            {
                localPluginContext.PluginExecutionContext.OutputParameters[OutputKey] = JsonSerializer.Serialize(new ErrorResponseModel { Status = "Failed", Error = ex.Message });
            }
        }

        private Entity GetAttachment(IOrganizationService service, Guid appointmentId, string fileName)
        {
            var query = new QueryExpression("annotation")
            {
                ColumnSet = new ColumnSet("filename", "documentbody", "mimetype"),
                TopCount = 1
            };
            query.Criteria.AddCondition("objectid", ConditionOperator.Equal, appointmentId);
            query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, Appointment.EntityLogicalName);
            query.Criteria.AddCondition("filename", ConditionOperator.Like, fileName + "%");

            var result = service.RetrieveMultiple(query);

            return result.Entities.FirstOrDefault();
        }
    }
}
