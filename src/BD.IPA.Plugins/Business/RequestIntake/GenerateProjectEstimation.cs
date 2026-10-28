using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;
using System.Text.Json;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Creates project estimation records from the request intake and configured estimation stages.
    /// </summary>
    public class GenerateProjectEstimation : BusinessBase<Model.bdgas_requestintake>
    {
        public GenerateProjectEstimation(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var settingText = AdminService.GetEnvironmentVariable("bdgas_ARGOProjectEstimation");

            if (string.IsNullOrEmpty(settingText)) return;

            if (Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.RPA && Latest.statuscode == bdgas_requestintake_statuscode.SolutionDesign)
            {
                var applications = GetBudgetRequests().Where(e => e.bdgas_applicationid != null).GroupBy(e => e.bdgas_applicationid.Id).ToArray();
                var existingEstimations = GetActiveProjectEstimation();
                var data = JsonSerializer.Deserialize<EnvironmentModel[]>(settingText).OrderBy(e => e.Sequence).ToArray();

                foreach (var app in applications)
                {
                    var startDate = BusinessDateCalculator.MoveToBusinessDay(DateTime.UtcNow);
                    foreach (var item in data)
                    {
                        var name = item.Stage;
                        var isDev = item.IsDev.GetValueOrDefault();
                        var durations = item.Durations.GetValueOrDefault();

                        var current = app.FirstOrDefault();
                        if (current == null) continue;

                        var appRef = current.bdgas_applicationid;
                        var efforts = ((isDev ? GetEfforts(current.bdgas_DevelopmentEffort) : durations) / 8) - 1;
                        if (efforts < 0) efforts = 0;

                        if (existingEstimations.Any(e => e.bdgas_applicationid?.Id == appRef?.Id && e.bdgas_name == name)) continue;

                        var newEstimation = new bdgas_devestimation
                        {
                            bdgas_requestintakeid = Latest.ToEntityReference(),
                            bdgas_applicationid = appRef,
                            bdgas_description = name,
                            bdgas_name = name,
                            bdgas_planstartdate = startDate,
                            bdgas_planenddate = BusinessDateCalculator.AddBusinessDays(startDate, efforts),
                            bdgas_sequence = item.Sequence
                        };
                        newEstimation.bdgas_totaldays = BusinessDateCalculator.CountBusinessDays(
                            newEstimation.bdgas_planstartdate.Value, newEstimation.bdgas_planenddate.Value);

                        AdminService.Create(newEstimation);

                        startDate = BusinessDateCalculator.AddBusinessDays(newEstimation.bdgas_planenddate.Value, 1);
                    }
                }
            }
            else if (Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.QPA && Latest.statuscode == bdgas_requestintake_statuscode.ConceptApproval)
            {
                var data = JsonSerializer.Deserialize<EnvironmentModel[]>(settingText).OrderBy(e => e.Sequence).ToArray();
                var existingEstimations = GetActiveProjectEstimation();

                foreach (var item in data)
                {
                    var name = item.Stage;
                    var durations = item.Durations.GetValueOrDefault();

                    if (existingEstimations.Any(e => e.bdgas_name == name)) continue;

                    var newEstimation = new bdgas_devestimation
                    {
                        bdgas_requestintakeid = Latest.ToEntityReference(),
                        bdgas_description = name,
                        bdgas_name = name,
                        bdgas_sequence = item.Sequence
                    };

                    AdminService.Create(newEstimation);
                }
            }
        }

        private int GetEfforts(bdgas_budgetrequest_bdgas_developmenteffort? effort)
        {
            switch (effort)
            {
                case bdgas_budgetrequest_bdgas_developmenteffort.Small_100hours: return 48;
                case bdgas_budgetrequest_bdgas_developmenteffort.Medium_100200hours: return 152;
                case bdgas_budgetrequest_bdgas_developmenteffort.Large_200300hours: return 248;
                case bdgas_budgetrequest_bdgas_developmenteffort.XLarge_300400hours: return 352;
                case bdgas_budgetrequest_bdgas_developmenteffort._2XLarge_400500hours: return 448;
                case bdgas_budgetrequest_bdgas_developmenteffort._3XLarge_500600hours: return 552;
            }

            return 0;
        }

        private bdgas_devestimation[] GetActiveProjectEstimation()
        {
            var query = new QueryExpression(bdgas_devestimation.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_devestimation.Fields.bdgas_name, bdgas_devestimation.Fields.bdgas_applicationid)
            };
            query.Criteria.AddCondition(bdgas_devestimation.Fields.bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_devestimation.Fields.statecode, ConditionOperator.Equal, (int)bdgas_devestimation_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_devestimation>()).ToArray();
        }

        private bdgas_budgetrequest[] GetBudgetRequests()
        {
            var query = new QueryExpression(bdgas_budgetrequest.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_budgetrequest.Fields.bdgas_applicationid, bdgas_budgetrequest.Fields.bdgas_DevelopmentEffort)
            };
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_isrecurring, ConditionOperator.Equal, false);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.bdgas_requestintakeid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_budgetrequest.Fields.statecode, ConditionOperator.Equal, (int)bdgas_budgetrequest_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_budgetrequest>()).ToArray();
        }

        /// <summary>
        /// Represents a configured estimation stage and its duration and sequence.
        /// </summary>
        public class EnvironmentModel
        {
            public string Stage { get; set; }
            public bool? IsDev { get; set; }
            public int? Durations { get; set; }
            public int Sequence { get; set; }
        }
    }
}
