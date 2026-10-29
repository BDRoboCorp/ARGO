using BD.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Schedules digital SRA appointments and their related meeting details.
    /// </summary>
    public class ScheduleDigitalSRA : BusinessBase<Model.Appointment>
    {
        private static readonly string[] SraAssessmentAttributes =
        {
            Model.Appointment.Fields.bdgas_routedfromid,
            Model.Appointment.Fields.bdgas_srarequesttypeid,
            Model.Appointment.Fields.bdgas_LocalChangeControl,
            Model.Appointment.Fields.bdgas_legacysystemid,
            Model.Appointment.Fields.bdgas_CHMDMNDRITMPlanviewNumber,
            Model.Appointment.Fields.bdgas_ConfigurationItemSerialNumber,
            Model.Appointment.Fields.bdgas_configurationitemname,
            Model.Appointment.Fields.bdgas_VersionNumber,
            Model.Appointment.Fields.bdgas_SystemDesciption,
            Model.Appointment.Fields.bdgas_systemhasdistinctmodulesid,
            Model.Appointment.Fields.bdgas_ChildSystemComponents,
            Model.Appointment.Fields.bdgas_aiusedid,
            Model.Appointment.Fields.bdgas_aiassessmentoutcome,
            Model.Appointment.Fields.bdgas_aiapprovaldate,
            Model.Appointment.Fields.bdgas_solutionarchitecturetext,
            Model.Appointment.Fields.bdgas_requirvpnforaccessid,
            Model.Appointment.Fields.bdgas_NetworkDataflowDiagram,
            Model.Appointment.Fields.bdgas_authenticationmethodtext,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram2,
            Model.Appointment.Fields.bdgas_OtherAuthenticationMethod,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram3,
            Model.Appointment.Fields.bdgas_operatingenvironmenttext,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram4,
            Model.Appointment.Fields.bdgas_impacttootherapplicationsid,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram5,
            Model.Appointment.Fields.bdgas_systemresidetext,
            Model.Appointment.Fields.bdgas_bdcontrolledsegmentationtext,
            Model.Appointment.Fields.bdgas_firewallchangestext,
            Model.Appointment.Fields.bdgas_systemprocessdataid,
            Model.Appointment.Fields.bdgas_HostingLocationoftheData,
            Model.Appointment.Fields.bdgas_dataclassificationid,
            Model.Appointment.Fields.bdgas_SystemAdministrationLocation,
            Model.Appointment.Fields.bdgas_scopeofdataandprocesssupportedid,
            Model.Appointment.Fields.bdgas_productiondatawillbeusedid,
            Model.Appointment.Fields.bdgas_confidentialityimpactid,
            Model.Appointment.Fields.bdgas_integrityimpactid,
            Model.Appointment.Fields.bdgas_availabilityimpactid
        };

        private static readonly HashSet<string> ImageAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Model.Appointment.Fields.bdgas_NetworkDataflowDiagram,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram2,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram3,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram4,
            Model.Appointment.Fields.bdgas_networkdataflowdiagram5
        };

        private const int FileAttachmentNotFoundErrorCode = unchecked((int)0x80040217);
        private const int BlockSize = 4 * 1024 * 1024;

        public ScheduleDigitalSRA(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        // Image columns are stored outside the record, so values set on the PreOperation Target are not persisted
        // and Retrieve only returns the thumbnail. Copy them through the file APIs or a separate update instead.
        private void CopyImages(Entity source, EntityReference target)
        {
            var thumbnailUpdate = new Entity(target.LogicalName, target.Id);
            foreach (var attribute in ImageAttributes)
            {
                var thumbnail = source.GetAttributeValue<byte[]>(attribute);
                if (thumbnail == null || thumbnail.Length == 0)
                {
                    thumbnailUpdate[attribute] = null;
                    continue;
                }

                if (!TryCopyFullImage(source.ToEntityReference(), target, attribute))
                {
                    thumbnailUpdate[attribute] = thumbnail;
                }
            }

            if (thumbnailUpdate.Attributes.Count > 0)
            {
                AdminService.Update(thumbnailUpdate);
            }
        }

        private bool TryCopyFullImage(EntityReference source, EntityReference target, string attribute)
        {
            InitializeFileBlocksDownloadResponse download;
            try
            {
                download = (InitializeFileBlocksDownloadResponse)AdminService.Execute(new InitializeFileBlocksDownloadRequest
                {
                    Target = source,
                    FileAttributeName = attribute
                });
            }
            catch (FaultException<OrganizationServiceFault> ex) when (ex.Detail?.ErrorCode == FileAttachmentNotFoundErrorCode)
            {
                // The column does not store full-sized images; fall back to the thumbnail bytes.
                return false;
            }

            var data = new List<byte>((int)download.FileSizeInBytes);
            for (long offset = 0; offset < download.FileSizeInBytes; offset += BlockSize)
            {
                var block = (DownloadBlockResponse)AdminService.Execute(new DownloadBlockRequest
                {
                    FileContinuationToken = download.FileContinuationToken,
                    Offset = offset,
                    BlockLength = Math.Min(BlockSize, download.FileSizeInBytes - offset)
                });
                data.AddRange(block.Data);
            }

            var upload = (InitializeFileBlocksUploadResponse)AdminService.Execute(new InitializeFileBlocksUploadRequest
            {
                Target = target,
                FileAttributeName = attribute,
                FileName = download.FileName
            });

            var bytes = data.ToArray();
            var blockIds = new List<string>();
            for (var offset = 0; offset < bytes.Length; offset += BlockSize)
            {
                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
                blockIds.Add(blockId);
                AdminService.Execute(new UploadBlockRequest
                {
                    FileContinuationToken = upload.FileContinuationToken,
                    BlockId = blockId,
                    BlockData = bytes.Skip(offset).Take(BlockSize).ToArray()
                });
            }

            AdminService.Execute(new CommitFileBlocksUploadRequest
            {
                FileContinuationToken = upload.FileContinuationToken,
                BlockList = blockIds.ToArray(),
                FileName = download.FileName,
                MimeType = GetImageMimeType(download.FileName)
            });
            return true;
        }

        private static string GetImageMimeType(string fileName)
        {
            switch (Path.GetExtension(fileName)?.ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".tif":
                case ".tiff": return "image/tiff";
                default: return "image/jpeg";
            }
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;

            var digitalSraRef = Target.bdgas_mydigitalsraid;
            if (digitalSraRef == null) return;

            var digitalSra = AdminService.Retrieve(
                digitalSraRef.LogicalName,
                digitalSraRef.Id,
                new ColumnSet(SraAssessmentAttributes)).ToEntity<Model.Appointment>();

            foreach (var attribute in SraAssessmentAttributes)
            {
                if (ImageAttributes.Contains(attribute)) continue;
                Target[attribute] = digitalSra.Attributes.TryGetValue(attribute, out var value) ? value : null;
            }

            var targetAppointment = Target.ToEntityReference();
            CopyImages(digitalSra, targetAppointment);
            CopyAssignments<bdgas_authenticationmethodassignment>(
                bdgas_authenticationmethodassignment.EntityLogicalName,
                bdgas_authenticationmethodassignment.Fields.bdgas_appointmentid,
                bdgas_authenticationmethodassignment.Fields.statecode,
                bdgas_authenticationmethodassignment.Fields.bdgas_name,
                bdgas_authenticationmethodassignment.Fields.bdgas_ramasterid,
                (int)bdgas_authenticationmethodassignment_statecode.Active,
                digitalSraRef.Id,
                targetAppointment,
                source => new bdgas_authenticationmethodassignment
                {
                    bdgas_appointmentid = targetAppointment,
                    bdgas_name = source.bdgas_name,
                    bdgas_ramasterid = source.bdgas_ramasterid
                });
            CopyAssignments<bdgas_controlledsegmentationassignment>(
                bdgas_controlledsegmentationassignment.EntityLogicalName,
                bdgas_controlledsegmentationassignment.Fields.bdgas_appointmentid,
                bdgas_controlledsegmentationassignment.Fields.statecode,
                bdgas_controlledsegmentationassignment.Fields.bdgas_name,
                bdgas_controlledsegmentationassignment.Fields.bdgas_ramasterid,
                (int)bdgas_controlledsegmentationassignment_statecode.Active,
                digitalSraRef.Id,
                targetAppointment,
                source => new bdgas_controlledsegmentationassignment
                {
                    bdgas_appointmentid = targetAppointment,
                    bdgas_name = source.bdgas_name,
                    bdgas_ramasterid = source.bdgas_ramasterid
                });
            CopyAssignments<bdgas_firewallchangesassignment>(
                bdgas_firewallchangesassignment.EntityLogicalName,
                bdgas_firewallchangesassignment.Fields.bdgas_appointmentid,
                bdgas_firewallchangesassignment.Fields.statecode,
                bdgas_firewallchangesassignment.Fields.bdgas_name,
                bdgas_firewallchangesassignment.Fields.bdgas_ramasterid,
                (int)bdgas_firewallchangesassignment_statecode.Active,
                digitalSraRef.Id,
                targetAppointment,
                source => new bdgas_firewallchangesassignment
                {
                    bdgas_appointmentid = targetAppointment,
                    bdgas_name = source.bdgas_name,
                    bdgas_ramasterid = source.bdgas_ramasterid
                });
            CopyAssignments<bdgas_operatingenvironmentassignment>(
                bdgas_operatingenvironmentassignment.EntityLogicalName,
                bdgas_operatingenvironmentassignment.Fields.bdgas_appointmentid,
                bdgas_operatingenvironmentassignment.Fields.statecode,
                bdgas_operatingenvironmentassignment.Fields.bdgas_name,
                bdgas_operatingenvironmentassignment.Fields.bdgas_ramasterid,
                (int)bdgas_operatingenvironmentassignment_statecode.Active,
                digitalSraRef.Id,
                targetAppointment,
                source => new bdgas_operatingenvironmentassignment
                {
                    bdgas_appointmentid = targetAppointment,
                    bdgas_name = source.bdgas_name,
                    bdgas_ramasterid = source.bdgas_ramasterid
                });
            CopyAssignments<bdgas_solutionarchitectureassignment>(
                bdgas_solutionarchitectureassignment.EntityLogicalName,
                bdgas_solutionarchitectureassignment.Fields.bdgas_appointmentid,
                bdgas_solutionarchitectureassignment.Fields.statecode,
                bdgas_solutionarchitectureassignment.Fields.bdgas_name,
                bdgas_solutionarchitectureassignment.Fields.bdgas_ramasterid,
                (int)bdgas_solutionarchitectureassignment_statecode.Active,
                digitalSraRef.Id,
                targetAppointment,
                source => new bdgas_solutionarchitectureassignment
                {
                    bdgas_appointmentid = targetAppointment,
                    bdgas_name = source.bdgas_name,
                    bdgas_ramasterid = source.bdgas_ramasterid
                });
            CopyAssignments<bdgas_systemresideassignment>(
                bdgas_systemresideassignment.EntityLogicalName,
                bdgas_systemresideassignment.Fields.bdgas_appointmentid,
                bdgas_systemresideassignment.Fields.statecode,
                bdgas_systemresideassignment.Fields.bdgas_name,
                bdgas_systemresideassignment.Fields.bdgas_ramasterid,
                (int)bdgas_systemresideassignment_statecode.Active,
                digitalSraRef.Id,
                targetAppointment,
                source => new bdgas_systemresideassignment
                {
                    bdgas_appointmentid = targetAppointment,
                    bdgas_name = source.bdgas_name,
                    bdgas_ramasterid = source.bdgas_ramasterid
                });

            var update = new Model.Appointment
            {
                Id = digitalSraRef.Id,
                ScheduledStart = Latest.ScheduledStart,
                ScheduledEnd = Latest.ScheduledEnd,
                ActualStart = Latest.ActualStart,
                ActualEnd = Latest.ActualEnd
            };
            AdminService.Update(update);
        }

        private void CopyAssignments<T>(
            string entityName,
            string appointmentField,
            string stateField,
            string nameField,
            string raMasterField,
            int activeState,
            Guid sourceAppointmentId,
            EntityReference targetAppointment,
            Func<T, T> createCopy)
            where T : Entity
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(nameField, raMasterField)
            };
            query.Criteria.AddCondition(appointmentField, ConditionOperator.Equal, sourceAppointmentId);
            query.Criteria.AddCondition(stateField, ConditionOperator.Equal, activeState);
            foreach (var assignment in AdminService.RetrieveMultiple(query).Entities)
            {
                AdminService.Create(createCopy(assignment.ToEntity<T>()));
            }
        }
    }
}
