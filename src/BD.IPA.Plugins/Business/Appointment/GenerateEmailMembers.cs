using System;
using System.Collections.Generic;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Builds the email participant list from appointment details.
    /// </summary>
    public class GenerateEmailMembers : BusinessBase<Model.Appointment>
    {
        public GenerateEmailMembers(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var registrantRef = Latest.bdgas_Registrant;
            var presenterRef = Latest.bdgas_Presenter;
            //var businessOwnerRef = Latest.bdgas_BusinessOwner;
            var businessPartnerRef = Latest.bdgas_BusinessPartner;
            var technicalOwnerRef = Latest.bdgas_TechnicalOwner;
            var validationLeadRef = Latest.bdgas_ValidationLead;
            var validationLead2Ref = Latest.bdgas_validationlead2;
            var otherRequiredEmails = Latest.bdgas_OtherRequiredEmails;
            var optionalEmails = Latest.bdgas_optionalemails;

            var requiredEmails = new[] {
                registrantRef?.Id,
                presenterRef?.Id,
                //businessOwnerRef?.Id,
                businessPartnerRef?.Id,
                technicalOwnerRef?.Id,
                validationLeadRef?.Id,
                validationLead2Ref?.Id
            }.Where(id => id.HasValue).Select(id => GetSystemUserEmail(id.Value)).ToList();

            var otherRequiredEmailsList = !string.IsNullOrWhiteSpace(otherRequiredEmails) ? otherRequiredEmails.Split(new[] { ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries).Select(email => email.Trim()).ToList() : new List<string>();
            var otherOptionalEmailsList = !string.IsNullOrWhiteSpace(optionalEmails) ? optionalEmails.Split(new[] { ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries).Select(email => email.Trim()).ToList() : new List<string>();

            var allRequiredEmails = requiredEmails.Concat(otherRequiredEmailsList).Distinct().ToList();
            Target.bdgas_requiredemails = string.Join(";", allRequiredEmails);
            Target.bdgas_optionalemails = string.Join(";", otherOptionalEmailsList);
        }

        private string GetSystemUserEmail(Guid systemUserId)
        {
            var systemUser = AdminService.Retrieve(Model.SystemUser.EntityLogicalName, systemUserId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(Model.SystemUser.Fields.InternalEMailAddress)).ToEntity<Model.SystemUser>();
            return systemUser.InternalEMailAddress;
        }
    }
}