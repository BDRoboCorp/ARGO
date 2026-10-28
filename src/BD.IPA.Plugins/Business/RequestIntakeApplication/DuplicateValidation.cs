using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntakeApplication
{
    /// <summary>
    /// Checks request intake applications for duplicate records.
    /// </summary>
    public class DuplicateValidation : BusinessBase<bdgas_RequestIntakeApplication>
    {
        public DuplicateValidation(ILocalPluginContext context, StateManager<bdgas_RequestIntakeApplication> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var applicationRef = Latest.bdgas_application;
            if (applicationRef == null) return;

            var projectCenterRef = Latest.bdgas_requestintakeid;
            var sraRef = Latest.bdgas_appointmentid;

            if (projectCenterRef == null && sraRef == null) return;

            var applications = GetApplications(projectCenterRef, sraRef);

            var duplicateAny = applications.Any(e => e.bdgas_application?.Id == applicationRef.Id);
            if (duplicateAny) throw new InvalidPluginExecutionException("This application appears to have already been submitted. Please check your existing applications before submitting again.");
        }

        private bdgas_RequestIntakeApplication[] GetApplications(Microsoft.Xrm.Sdk.EntityReference projectCenterRef, Microsoft.Xrm.Sdk.EntityReference sraRef)
        {
            var query = new QueryExpression(bdgas_RequestIntakeApplication.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_RequestIntakeApplication.Fields.bdgas_application)
            };

            if (projectCenterRef != null)
            {
                query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.bdgas_requestintakeid, ConditionOperator.Equal, projectCenterRef.Id);
            }

            if (sraRef != null)
            {
                query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.bdgas_appointmentid, ConditionOperator.Equal, sraRef.Id);
            }

            query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.statecode, ConditionOperator.Equal, (int)bdgas_requestintakeapplication_statecode.Active);
            query.Criteria.AddCondition(bdgas_RequestIntakeApplication.Fields.Id, ConditionOperator.NotEqual, Latest.Id);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_RequestIntakeApplication>()).ToArray();

        }
    }
}
