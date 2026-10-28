using BD.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.ProjectEstimation
{
    /// <summary>
    /// Adjusts project estimation dates when an estimation is updated.
    /// </summary>
    public class OnUpdateEstimationDates : BusinessBase<bdgas_devestimation>
    {
        public OnUpdateEstimationDates(ILocalPluginContext context, StateManager<bdgas_devestimation> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;

            var requestIntakeRef = Latest.bdgas_requestintakeid;
            if (requestIntakeRef == null) return;

            var startDate = Latest.bdgas_planstartdate;
            if (startDate == null) return;

            var endDate = Latest.bdgas_planenddate;
            if (endDate == null) return;

            if (endDate.Value.Date < startDate.Value.Date)
            {
                throw new InvalidPluginExecutionException("Plan End Date must not be before Plan Start Date.");
            }

            var planStart = BusinessDateCalculator.MoveToBusinessDay(startDate.Value);
            var planEnd = BusinessDateCalculator.MoveToBusinessDay(endDate.Value);
            Target.bdgas_planstartdate = planStart;
            Target.bdgas_planenddate = planEnd;
            Target.bdgas_totaldays = BusinessDateCalculator.CountBusinessDays(planStart, planEnd);

            var sequence = Latest.bdgas_sequence.GetValueOrDefault();
            var projectEstimations = GetActiveProjectEstimation(requestIntakeRef.Id)
                .Where(e => e.bdgas_sequence.GetValueOrDefault() > sequence)
                .OrderBy(e => e.bdgas_sequence.GetValueOrDefault())
                .ToArray();

            var tempStart = BusinessDateCalculator.AddBusinessDays(planEnd, 1);
            foreach (var item in projectEstimations)
            {
                var totalDays = item.bdgas_totaldays.GetValueOrDefault();
                if (item.bdgas_planstartdate.HasValue && item.bdgas_planenddate.HasValue)
                {
                    if (item.bdgas_planenddate.Value.Date < item.bdgas_planstartdate.Value.Date)
                    {
                        throw new InvalidPluginExecutionException($"Project estimation {item.Id}: Plan End Date must not be before Plan Start Date.");
                    }

                    // Derive the duration from dates rather than carrying forward legacy off-by-one totals.
                    totalDays = BusinessDateCalculator.CountBusinessDays(
                        BusinessDateCalculator.MoveToBusinessDay(item.bdgas_planstartdate.Value),
                        BusinessDateCalculator.MoveToBusinessDay(item.bdgas_planenddate.Value));
                }

                if (totalDays < 1)
                {
                    throw new InvalidPluginExecutionException($"Project estimation {item.Id} must have Total Days of at least 1 before it can be rescheduled.");
                }

                var update = new bdgas_devestimation
                {
                    Id = item.Id,
                    bdgas_planstartdate = tempStart,
                    bdgas_planenddate = BusinessDateCalculator.AddBusinessDays(tempStart, totalDays - 1)
                };
                update.bdgas_totaldays = totalDays;
                AdminService.Update(update);

                tempStart = BusinessDateCalculator.AddBusinessDays(update.bdgas_planenddate.Value, 1);
            }
        }


        private bdgas_devestimation[] GetActiveProjectEstimation(Guid requestIntakeId)
        {
            var query = new QueryExpression(bdgas_devestimation.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_devestimation.Fields.bdgas_planstartdate, bdgas_devestimation.Fields.bdgas_planenddate, bdgas_devestimation.Fields.bdgas_sequence,
                    bdgas_devestimation.Fields.bdgas_totaldays)
            };
            query.Criteria.AddCondition(bdgas_devestimation.Fields.bdgas_requestintakeid, ConditionOperator.Equal, requestIntakeId);
            query.Criteria.AddCondition(bdgas_devestimation.Fields.statecode, ConditionOperator.Equal, (int)bdgas_devestimation_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_devestimation>()).ToArray();
        }
    }
}
