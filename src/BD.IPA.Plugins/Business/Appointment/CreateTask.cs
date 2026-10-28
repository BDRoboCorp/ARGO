using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Creates a follow-up task from an appointment.
    /// </summary>
    public class CreateTask : BusinessBase<Model.Appointment>
    {
        public CreateTask(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var description = Latest.bdgas_assigneddescription;
            var assignments = GetAssignments();
            var dueDate = Latest.bdgas_assignedduedate;
            var requestIntakeRef = Latest.RegardingObjectId;
            var subject = Latest.bdgas_assignedsubject;
            var valid = !string.IsNullOrEmpty(description) && !string.IsNullOrEmpty(subject) && assignments.Any() && dueDate != null;
            if (!valid) return;

            var assignedToRef = assignments.FirstOrDefault()?.bdgas_touser;
            var restAssignments = assignments.Length > 1 ? assignments.Skip(1).Take(assignments.Count() - 1) : new Model.bdgas_sraappointmentassignment[] { };
            if (assignedToRef == null) return;

            var task = new Model.Task
            {
                OwnerId = assignedToRef,
                Subject = subject,
                Description = description,
                RegardingObjectId = requestIntakeRef,
                ScheduledEnd = dueDate,
                bdgas_appointment = Latest.ToEntityReference()
            };
            task.Id = AdminService.Create(task);

            var assignedTaskAttributes = new[] { Model.Appointment.Fields.bdgas_assignedsubject, Model.Appointment.Fields.bdgas_assignedduedate,
                Model.Appointment.Fields.bdgas_assigneddescription };
            foreach (var attr in assignedTaskAttributes)
            {
                Target[attr] = null;
            }

            foreach (var assignment in assignments)
            {
                var toRef = assignment?.bdgas_touser;
                if (toRef == null) continue;
                var taskAssignment = new Model.bdgas_TaskAssignment
                {
                    bdgas_taskid = task.ToEntityReference(),
                    bdgas_userid = toRef
                };
                AdminService.Create(taskAssignment);
                AdminService.Delete(assignment.LogicalName, assignment.Id);
            }
        }

        private Model.bdgas_sraappointmentassignment[] GetAssignments()
        {
            var query = new QueryExpression(Model.bdgas_sraappointmentassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Model.bdgas_sraappointmentassignment.Fields.bdgas_touser)
            };
            query.Criteria.AddCondition(Model.bdgas_sraappointmentassignment.Fields.statecode, ConditionOperator.Equal,
                (int)Model.bdgas_sraappointmentassignment_statecode.Active);
            query.Criteria.AddCondition(Model.bdgas_sraappointmentassignment.Fields.bdgas_sraappointmentid, ConditionOperator.Equal,
                Latest.Id);

            var result = AdminService.RetrieveMultiple(query);
            return result.Entities.Select(e => e.ToEntity<Model.bdgas_sraappointmentassignment>()).ToArray();
        }
    }
}