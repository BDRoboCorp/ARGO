using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Creates SRA appointments according to the configured meeting schedule.
    /// </summary>
    public class GenerateSraMeetings : BusinessBase<Model.Appointment>
    {
        public GenerateSraMeetings(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Context.PluginExecutionContext.Depth > 1) return;
            if (Latest.bdgas_sratemplate == true) return;

            var subject = Latest.Subject;
            var startDate = Latest.ScheduledStart;

            if (startDate.GetValueOrDefault().Date < DateTime.UtcNow.Date) throw new InvalidPluginExecutionException("Please select a current or future date. The selected date is in the past.");
            var endDate = Latest.ScheduledEnd;
            var duration = Latest.bdgas_duration;
            var qty = Latest.bdgas_quantity;
            var description = Latest.Description;
            var breakDuration = Latest.bdgas_breakeachsession.GetValueOrDefault();

            if (startDate == null || endDate == null) return;

            Setting setting = null;
            var isGenerateMonthly = false;
            if (subject == "GENERATED FROM SCHEDULE")
            {
                var configText = AdminService.GetEnvironmentVariable("bdgas_ARGOMeetingSchedules");
                if (string.IsNullOrEmpty(configText)) return;

                setting = System.Text.Json.JsonSerializer.Deserialize<Setting>(configText,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (setting?.Settings == null || setting.Settings.Length == 0) return;

                isGenerateMonthly = true;
                subject = setting.Subject;
                duration = setting.Durations;
                qty = setting.Slots;
                breakDuration = setting.Break;
            }

            if (duration == null || qty == null || duration <= 0 || qty <= 0) return;

            var timezoneString = isGenerateMonthly
                ? setting.Timezone
                : TimeZoneDefinitionQueries.GetTimezoneByUserId(Context.PluginExecutionContext.UserId, AdminService);
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneString);

            DateTime? firstScheduledDate = null;
            TimeSpan firstScheduledTime = default(TimeSpan);
            if (isGenerateMonthly)
            {
                var tempDate = timezone.ConvertToLocal(DateTime.UtcNow).Date;
                var maxSearchDays = 7;
                for (var dayOffset = 0; dayOffset < maxSearchDays; dayOffset++)
                {
                    var candidateDate = tempDate.AddDays(dayOffset);
                    var daySetting = GetDaySetting(setting, candidateDate.DayOfWeek);
                    if (daySetting != null && TimeSpan.TryParse(daySetting.StartTime, out firstScheduledTime))
                    {
                        firstScheduledDate = candidateDate;
                        break;
                    }
                }

                if (!firstScheduledDate.HasValue) return;

                startDate = timezone.ConvertToUTC(firstScheduledDate.Value.Add(firstScheduledTime));
                // Days is inclusive of the first scheduled date
                endDate = startDate.Value.AddDays(Math.Max(setting.Days, 1) - 1);
            }

            var totalHours = (duration + breakDuration) * qty;
            var totalMinutes = ((double)duration * qty.Value) + ((double)breakDuration * (qty.Value - 1));

            var startTime = timezone.ConvertToLocal(startDate.Value).TimeOfDay;
            var endDateTime = timezone.ConvertToLocal(endDate.Value).Date;
            var currentDate = timezone.ConvertToLocal(startDate.Value).Date;

            var publicHolidayDates = new System.Collections.Generic.HashSet<DateTime>(
                GetPublicHoliday(currentDate, endDateTime)
                    .Where(holiday => holiday.bdgas_date.HasValue)
                    .Select(holiday => holiday.bdgas_date.Value.Date));

            if (isGenerateMonthly)
            {
                currentDate = firstScheduledDate.Value;
            }
            var totalAppointmentCount = 0;
            var isFirstAppointment = true;

            while (currentDate <= endDateTime)
            {
                if (publicHolidayDates.Contains(currentDate.Date))
                {
                    Context.Trace($"Skipping SRA meeting generation for public holiday {currentDate:yyyy-MM-dd}.");
                    currentDate = currentDate.AddDays(1);
                    continue;
                }

                DateTime dailyStartDateTime;
                DateTime dayEndDateTime;

                if (!isGenerateMonthly)
                {
                    // Skip weekends
                    if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
                    {
                        currentDate = currentDate.AddDays(1);
                        continue;
                    }

                    dailyStartDateTime = timezone.ConvertToUTC(currentDate.Add(startTime));
                    dayEndDateTime = dailyStartDateTime.AddHours((double)totalHours);
                }
                else
                {
                    var daySetting = GetDaySetting(setting, currentDate.DayOfWeek);
                    if (daySetting == null || !TimeSpan.TryParse(daySetting.StartTime, out var dayStartTime))
                    {
                        currentDate = currentDate.AddDays(1);
                        continue;
                    }

                    // StartTime in the setting is expressed in the configured timezone
                    dailyStartDateTime = timezone.ConvertToUTC(currentDate.Add(dayStartTime));
                    dayEndDateTime = dailyStartDateTime.AddMinutes(totalMinutes);
                }

                if (AppointmentExistsForDate(currentDate, timezone))
                {
                    currentDate = currentDate.AddDays(1);
                    continue;
                }

                // Create qty appointments for this day
                for (int i = 1; i <= qty; i++)
                {
                    var slotOffsetMinutes = ((double)duration.GetValueOrDefault() * (i - 1)) + ((double)breakDuration * (i - 1));
                    var appointmentStart = dailyStartDateTime.AddMinutes(slotOffsetMinutes);
                    var appointmentEnd = appointmentStart.AddMinutes((double)duration.GetValueOrDefault());

                    var localTime = timezone.ConvertToLocal(appointmentStart);
                    var sortDate = timezone.ConvertToUTC(localTime.Date);
                    var dateString = localTime.ToString("MM-dd-yyyy");

                    if (isFirstAppointment)
                    {
                        // Update the first appointment (the trigger record)
                        Target.bdgas_sratemplate = true;
                        Target.bdgas_SlotStatus = Model.bdgas_appointment_bdgas_slotstatus.Available;
                        Target.bdgas_timezone = timezoneString;
                        Target.Subject = $"{subject}_{dateString}_{i}";
                        Target.ScheduledStart = appointmentStart;
                        Target.ScheduledEnd = appointmentEnd;
                        Target.bdgas_duration = duration;
                        Target.bdgas_quantity = qty;
                        Target.bdgas_breakeachsession = breakDuration;
                        Target.ScheduledDurationMinutes = (int)duration;
                        Target.bdgas_datestring = dateString;
                        Target.bdgas_slotnumber = i.ToString();
                        Target.bdgas_IsFirstAppointment = true;
                        Target.ActualStart = dailyStartDateTime;
                        Target.ActualEnd = dayEndDateTime;
                        Target.SortDate = sortDate;
                        isFirstAppointment = false;
                    }
                    else
                    {
                        // Create new appointments
                        var newAppointment = new Model.Appointment
                        {
                            Subject = $"{subject}_{dateString}_{i}",
                            ScheduledStart = appointmentStart,
                            ScheduledEnd = appointmentEnd,
                            bdgas_duration = duration,
                            ScheduledDurationMinutes = (int)duration,
                            bdgas_quantity = qty,
                            bdgas_breakeachsession = breakDuration,
                            Description = description,
                            bdgas_sratemplate = true,
                            bdgas_SlotStatus = Model.bdgas_appointment_bdgas_slotstatus.Available,
                            bdgas_timezone = timezoneString,
                            bdgas_datestring = dateString,
                            bdgas_slotnumber = i.ToString(),
                            bdgas_IsFirstAppointment = i == 1,
                            ActualStart = dailyStartDateTime,
                            ActualEnd = dayEndDateTime,
                            SortDate = sortDate
                        };
                        AdminService.Create(newAppointment);
                    }

                    totalAppointmentCount++;
                }

                currentDate = currentDate.AddDays(1);
            }

            Context.Trace($"Total generated appointments: {totalAppointmentCount}.");
        }

        private Model.bdgas_publicholiday[] GetPublicHoliday(DateTime startDate, DateTime endDate)
        {
            var query = new QueryExpression(Model.bdgas_publicholiday.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Model.bdgas_publicholiday.Fields.bdgas_date)
            };
            query.Criteria.AddCondition(Model.bdgas_publicholiday.Fields.statecode, ConditionOperator.Equal, (int)Model.bdgas_publicholiday_statecode.Active);
            query.Criteria.AddCondition(Model.bdgas_publicholiday.Fields.bdgas_date, ConditionOperator.GreaterEqual, startDate.Date);
            query.Criteria.AddCondition(Model.bdgas_publicholiday.Fields.bdgas_date, ConditionOperator.LessThan, endDate.Date.AddDays(1));

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<Model.bdgas_publicholiday>()).ToArray();
        }

        private bool AppointmentExistsForDate(DateTime localDate, TimeZoneInfo timezone)
        {
            var utcDateStart = timezone.ConvertToUTC(localDate.Date);
            var utcDateEnd = timezone.ConvertToUTC(localDate.Date.AddDays(1));
            var query = new QueryExpression(Model.Appointment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            query.Criteria.AddCondition(Model.Appointment.Fields.bdgas_sratemplate, ConditionOperator.Equal, true);
            query.Criteria.AddCondition(Model.Appointment.Fields.ScheduledStart, ConditionOperator.GreaterEqual, utcDateStart);
            query.Criteria.AddCondition(Model.Appointment.Fields.ScheduledStart, ConditionOperator.LessThan, utcDateEnd);

            return AdminService.RetrieveMultiple(query).Entities.Any();
        }

        private static DaySetting GetDaySetting(Setting setting, DayOfWeek dayOfWeek)
        {
            foreach (var daySetting in setting.Settings)
            {
                if (Enum.TryParse<DayOfWeek>(daySetting.Day, true, out var settingDay) && settingDay == dayOfWeek)
                {
                    return daySetting;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Holds the configured settings for generating SRA meetings.
    /// </summary>
    public class Setting
    {
        public string Timezone { get; set; }
        public int Slots { get; set; }
        public int Durations { get; set; }
        public string Subject { get; set; }
        public int Break { get; set; }
        public int Days { get; set; }
        public DaySetting[] Settings { get; set; }
    }

    /// <summary>
    /// Holds the meeting configuration for a particular day.
    /// </summary>
    public class DaySetting
    {
        public string Day { get; set; }
        public string StartTime { get; set; }
    }

}
